using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Repositories;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// (#179) Manager の LAN-wide 同時起動検出 service。`manager_sessions` table に self row を
    /// heartbeat 周期で update + stale row 自動 cleanup + 他 PC row 検出 を提供する。
    ///
    /// **責務範囲**:
    ///   - 起動時 cleanup (stale row DELETE) + self row INSERT/UPDATE (= `Initialize`)
    ///   - heartbeat thread の lifecycle 管理 (10 秒間隔 update、`CancellationTokenSource` で停止)
    ///   - 他 PC active session の検出 (`DetectOtherActiveSessions`)
    ///   - shutdown 時 cleanup (heartbeat 停止 + self row DELETE) (= `Shutdown`)
    ///
    /// **責務外** (別 layer):
    ///   - 同 PC 重複起動 block (Named Mutex で `Program.cs` で物理 prevention、本 service は touch しない)
    ///   - 検出結果の UI 表示 (= caller の `MainForm` / `SessionConflictDialog` 責務)
    ///   - Launcher session tracking (PR3b 別 PR で SPEC §3.8 の JSON drop folder 方式を採用予定)
    ///
    /// thread safety: heartbeat thread と UI thread (起動時 check / 編集前 check) が同時に DB へ
    /// access する。DB 操作は `DatabaseConnection.ExecuteWithRetry` で SQLite BUSY/LOCKED retry +
    /// `busy_timeout=10000` ms で competition 緩和。
    /// </summary>
    public class ManagerSessionService
    {
        private const int HeartbeatIntervalSeconds = 10;
        private const int StaleTimeoutSeconds = 30;

        private readonly ManagerSessionRepository _repo;
        private readonly string _pcName;
        private readonly long _pid;
        private readonly string _managerVersion;
        private long _startedAtUnixMs;
        private CancellationTokenSource _heartbeatCts;
        private Task _heartbeatTask;
        private bool _initialized;
        // (round 3 L-1 / L-3) Shutdown 進行中 flag。heartbeat loop が token.WaitHandle.WaitOne や
        // _repo.UpsertHeartbeat で race 例外を踏んでも、Shutdown 中は silent に処理して noise を抑える。
        // volatile で thread 間の memory ordering を保証 (Shutdown thread の set と heartbeat thread の
        // read が並走するため)。
        private volatile bool _shuttingDown;

        public ManagerSessionService(ManagerSessionRepository repo)
        {
            _repo = repo;
            _pcName = Environment.MachineName ?? "(unknown)";
            _pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            _managerVersion = ver != null ? ver.ToString(3) : "(unknown)";
        }

        /// <summary>self の PC 名 (`Environment.MachineName`)。</summary>
        public string SelfPcName => _pcName;

        /// <summary>
        /// 起動時 1 度だけ呼ぶ。stale cleanup + self row 登録 + heartbeat thread 起動の 3 steps。
        /// 多重 call は 2 回目以降 no-op + Warn (= `MainForm.Load` で誤って 2 度呼ばれた場合の defensive)。
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
            {
                Logger.Warn("[ManagerSessionService] Initialize 多重 call、2 回目以降 no-op");
                return;
            }
            // (round 4 L-1) idempotent な再 init (= Shutdown 後の再 Initialize、現運用では発生しないが
            // 将来 test / restart pattern で踏みうる) で `_shuttingDown` 残置 → heartbeat task が初回
            // iteration で silent break する drift を予防。docstring 「多重 call は no-op + Warn」semantic
            // と整合させ、re-init pathways で flag を確実にリセットする。
            _shuttingDown = false;
            try
            {
                long now = NowUnixMs();
                _startedAtUnixMs = now;
                long staleThreshold = now - StaleTimeoutSeconds * 1000L;

                // (1) stale row cleanup (crash 残骸の自動回収)
                int deleted = _repo.DeleteStaleSessions(staleThreshold);
                if (deleted > 0) Logger.Info("[ManagerSessionService] stale session を " + deleted + " 件 cleanup");

                // (2) self row INSERT OR REPLACE
                var self = new ManagerSessionInfo
                {
                    PcName                = _pcName,
                    StartedAtUnixMs       = now,
                    LastHeartbeatAtUnixMs = now,
                    Pid                   = _pid,
                    ManagerVersion        = _managerVersion,
                };
                _repo.UpsertSelfSession(self);
                Logger.Info("[ManagerSessionService] self session 登録: pc=" + _pcName + " pid=" + _pid + " ver=" + _managerVersion);

                // (3) heartbeat thread 起動
                // (round 3 L-2) lambda 内で `_heartbeatCts.Token` を late-capture すると、Initialize 直後
                // に Shutdown が走って `_heartbeatCts = null` に set された場合 NRE で task が落ちる
                // race path がある (実害は startup Cancel-終了 path で発火)。local var に capture して
                // lambda は token のみ参照する形に変更。
                _heartbeatCts = new CancellationTokenSource();
                var capturedToken = _heartbeatCts.Token;
                _heartbeatTask = Task.Run(() => HeartbeatLoop(capturedToken), capturedToken);

                _initialized = true;
            }
            catch (Exception ex)
            {
                // DB 不到達は致命的だが、Manager 起動自体は続行する (= heartbeat 不在で他 PC 検出機能が
                // 退化、user は MessageBox レス起動を体験する fail-soft path)。Logger.Error で trail 残し。
                Logger.Error("[ManagerSessionService] Initialize 失敗 (heartbeat 機構なしで継続)", ex);
            }
        }

        /// <summary>
        /// shutdown 時に呼ぶ。heartbeat thread を停止 + self row DELETE で clean exit を記録。
        /// </summary>
        public void Shutdown()
        {
            if (!_initialized) return;
            // (round 3 L-1/L-3) shutdown flag を先に立てて、heartbeat loop が以降に race 例外を踏んでも
            // 「heartbeat update 失敗」Warn を抑制し、shutdown trail を本物に clean にする。Cancel()
            // 直後の token.WaitHandle.WaitOne / UpsertHeartbeat が ObjectDisposedException や別例外を
            // 投げる path はあるが、すべて shutting down 中の race として silent 扱いになる。
            _shuttingDown = true;
            try
            {
                if (_heartbeatCts != null)
                {
                    _heartbeatCts.Cancel();
                    // Task.Wait は thread block するが shutdown 同期 path で許容 (= 数秒以内に完了)。
                    // (round 1 L-2) OperationCanceled だけ silent swallow、他 inner type (= heartbeat
                    // 最後の UpsertHeartbeat で SQLite 例外等) は Warn 出力で trail を残す。
                    try { _heartbeatTask?.Wait(TimeSpan.FromSeconds(2)); }
                    catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
                    {
                        // 想定済の cancellation、silent OK
                    }
                    catch (AggregateException ae)
                    {
                        foreach (var inner in ae.InnerExceptions.Where(e => !(e is OperationCanceledException)))
                        {
                            Logger.Warn("[ManagerSessionService] heartbeat task shutdown 時 inner exception (継続): " + inner.GetType().Name + ": " + inner.Message);
                        }
                    }
                    // (round 3 L-1 fix) CancellationTokenSource.Dispose() 自体は idempotent で例外を
                    // 投げない (MSDN 仕様)。旧 round 2 L-1 で「Dispose の race で ObjectDisposedException」と
                    // 書いた rationale は誤り (実際の noise source は heartbeat loop 側の WaitOne 例外、
                    // round 3 L-1/L-3 fix で `_shuttingDown` flag による heartbeat loop 内 silent 化に
                    // 集約)。Dispose の try/catch wrapper は撤回。
                    _heartbeatCts.Dispose();
                    _heartbeatCts = null;
                }
                _repo.DeleteSelfSession(_pcName);
                Logger.Info("[ManagerSessionService] self session 削除 (clean shutdown): pc=" + _pcName);
            }
            catch (Exception ex)
            {
                // shutdown path 失敗は致命的でない (stale cleanup で 30 秒後に自動回収)。Warn のみ。
                Logger.Warn("[ManagerSessionService] Shutdown 失敗 (stale cleanup に委ねる): " + ex.Message);
            }
            _initialized = false;
        }

        /// <summary>
        /// 他 PC で active な session (= heartbeat が stale でない) を一覧。
        /// 起動時 check + 編集操作前 check で `MainForm` / `SessionConflictDialog` 経由で呼ばれる。
        /// 戻り値が空 list なら「他 PC 起動中なし」、1 件以上なら検出 → dialog 表示の trigger。
        /// </summary>
        public IReadOnlyList<ManagerSessionInfo> DetectOtherActiveSessions()
        {
            try
            {
                long now = NowUnixMs();
                long staleThreshold = now - StaleTimeoutSeconds * 1000L;
                return _repo.SelectOtherActiveSessions(_pcName, staleThreshold);
            }
            catch (Exception ex)
            {
                // DB 不到達 = 検出不能 → 空 list 返却 (= fail-soft、user は MessageBox 出ず通過)。
                // Warn 残しで debug 容易化、実害は「他 PC 起動中の警告が出ない」path 一時的のみ。
                Logger.Warn("[ManagerSessionService] DetectOtherActiveSessions 失敗 (空 list で fallback): " + ex.Message);
                return new List<ManagerSessionInfo>();
            }
        }

        private void HeartbeatLoop(CancellationToken token)
        {
            Logger.Info("[ManagerSessionService] heartbeat loop 起動 (interval=" + HeartbeatIntervalSeconds + "s)");
            try
            {
                // (round 4 L-2) `while` 条件 / `if` 内の `token.IsCancellationRequested` access も
                // CTS.Dispose 後は ObjectDisposedException を投げうるため、try/catch 外で参照すると
                // unhandled exception で task fault → TaskScheduler.UnobservedTaskException で silent。
                // helper `IsCancelled()` で wrap + shutdown 中は確実に終了 (= flag を見て exit) する形に。
                while (!IsCancelled(token))
                {
                    // (round 3 L-3) try を Wait 部分と UpsertHeartbeat 部分に分けて、example. SQLite BUSY
                    // / network blip / shutdown race の例外を message 上で区別可能に。Wait 部分は shutdown
                    // race (= ObjectDisposedException 等) を _shuttingDown flag で silent 化、UpsertHeartbeat
                    // は SQLite BUSY 等で例外を投げても shutdown 中なら同様 silent。
                    try
                    {
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
                    }
                    catch (Exception ex)
                    {
                        if (_shuttingDown) break; // shutdown race による Wait 例外、silent break
                        Logger.Warn("[ManagerSessionService] heartbeat Wait 失敗 (token race?、継続): " + ex.GetType().Name + ": " + ex.Message);
                    }
                    if (IsCancelled(token)) break;

                    try
                    {
                        // (round 3 H-2 fix) `UPDATE WHERE pc_name = ...` だと row 不在時 silent no-op で
                        // 自 PC 永久不可視化 path があったため `INSERT OR REPLACE` UPSERT に変更。
                        // 他 PC の stale cleanup で削除されても次の heartbeat で reanimate できる。
                        var self = new ManagerSessionInfo
                        {
                            PcName                = _pcName,
                            StartedAtUnixMs       = _startedAtUnixMs,
                            LastHeartbeatAtUnixMs = NowUnixMs(),
                            Pid                   = _pid,
                            ManagerVersion        = _managerVersion,
                        };
                        _repo.UpsertHeartbeat(self);
                    }
                    catch (Exception ex)
                    {
                        if (_shuttingDown) break; // shutdown race による UpsertHeartbeat 例外、silent break
                        // 1 回の heartbeat 失敗は致命的でない (次の 10 秒で retry)。Warn のみで継続。
                        Logger.Warn("[ManagerSessionService] heartbeat UpsertHeartbeat 失敗 (継続): " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                Logger.Info("[ManagerSessionService] heartbeat loop 終了");
            }
        }

        private static long NowUnixMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// (round 4 L-2) `CancellationToken.IsCancellationRequested` の access は CTS.Dispose 後に
        /// `ObjectDisposedException` を投げうる (.NET Framework 4.8、MSDN 仕様)。Heartbeat loop の
        /// `while` / `if` 条件で参照する箇所を本 helper 経由にして、Dispose race で task が unhandled
        /// fault しないように防御。shutdown 中 (= `_shuttingDown == true`) は確実に「cancelled」扱いで
        /// loop を抜けさせる。
        /// </summary>
        private bool IsCancelled(CancellationToken token)
        {
            if (_shuttingDown) return true;
            try { return token.IsCancellationRequested; }
            catch (ObjectDisposedException) { return true; }
        }
    }
}
