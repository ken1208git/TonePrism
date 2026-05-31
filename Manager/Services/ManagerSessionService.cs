using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;

namespace TonePrism.Manager.Services
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
        // (#271) 検出 stale 閾値 30→60 秒。stale 判定は「読み手 now − 書き手自己申告 heartbeat」で PC 間 clock
        // drift を被る (#269 Launcher と同根)。DB ベースのため per-row mtime が無く max(json,mtime) は使えず、
        // 閾値マージン (heartbeat ×6) で読み手側 skew を吸収するのが SMB 非依存でできる主防御。サーバ時刻基準化
        // (net time / マーカー mtime) は SMB 構成確定後の別案として #271 に残す。
        private const int StaleTimeoutSeconds = 60;
        // (#271) 起動時 cleanup 用の「放置 (abandoned)」閾値。stale 閾値 (60秒) で他 PC row を DELETE すると、
        // clock skew で **生存中の遠隔 Manager row を物理削除** → 検出から外れ → 両者同時 write → DB 破損、の
        // 危険があった。1 日超の明らかに放置された row のみ削除し、live-but-skewed row は決して消さない。table は
        // pc_name PRIMARY KEY で 1 PC 1 row、放置 row も次回その PC 起動の UPSERT で上書きされるため緩い cleanup
        // でも肥大しない。検出側は query 時に 60 秒閾値で stale を除外するので放置 row が残っても誤検出しない。
        private const int AbandonedSessionTimeoutSeconds = 86400; // 1 日

        private readonly ManagerSessionRepository _repo;
        private readonly string _pcName;
        private readonly long _pid;
        private readonly string _managerVersion;
        private long _startedAtUnixMs;
        private CancellationTokenSource _heartbeatCts;
        private Task _heartbeatTask;
        private bool _initialized;

        /// <summary>
        /// (#179 round 7 M-1) `Initialize()` が **完全成功** したかを caller が判定するための read-only flag。
        /// `Initialize` 失敗 path (= catch L114-119 に到達) では `_initialized = false` のまま、`_sessionService`
        /// 自体は non-null で残る。caller (`MainForm.CheckSessionConflictBeforeWrite`) はこの flag を見て
        /// 「`_sessionService` 非 null だが Init 失敗」case を short-circuit、毎 click の DB query 空振り
        /// (busy_timeout × maxRetries で最大 ~30 秒 UI freeze + Warn noise) を回避する。
        /// </summary>
        public bool IsInitialized => _initialized;
        // (round 3 L-1 / L-3) Shutdown 進行中 flag。heartbeat loop が token.WaitHandle.WaitOne や
        // _repo.UpsertHeartbeat で race 例外を踏んでも、Shutdown 中は silent に処理して noise を抑える。
        // volatile で thread 間の memory ordering を保証 (Shutdown thread の set と heartbeat thread の
        // read が並走するため)。
        private volatile bool _shuttingDown;

        public ManagerSessionService(ManagerSessionRepository repo)
        {
            _repo = repo;
            // (round 5 L-3) Environment.MachineName は MSDN 仕様で null を返さず、取得不能時は
            // InvalidOperationException を throw する。旧 `?? "(unknown)"` は dead code だった drift を
            // 修正、実際の例外 path を catch して fallback。
            try { _pcName = Environment.MachineName; }
            catch (InvalidOperationException) { _pcName = "(unknown)"; }
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
            // (#179 round 7 M-3) step (2) UpsertSelfSession 成功 → step (3) heartbeat thread 起動 で例外、
            // という partial-success path で self row が DB 登録済 + heartbeat 不在 = orphaned zombie に
            // なる drift があった。`selfRegistered` flag で step (2) 完了を tracking、catch 内で rollback。
            bool selfRegistered = false;
            try
            {
                long now = NowUnixMs();
                _startedAtUnixMs = now;

                // (1) abandoned row cleanup (#271): clock skew で生存中の遠隔 Manager row を消さないよう、
                // stale 閾値 (60秒) ではなく「1 日超の明らかに放置された row」だけを削除する。自 crash 残骸は
                // この後の (2) UPSERT (pc_name PK で上書き) でも回収される。検出側 (DetectOtherActiveSessions)
                // は query 時に 60 秒閾値で stale を除外するため、放置 row が table に残っても誤検出しない。
                long abandonedThreshold = now - AbandonedSessionTimeoutSeconds * 1000L;
                int deleted = _repo.DeleteStaleSessions(abandonedThreshold);
                if (deleted > 0) Logger.Info("[ManagerSessionService] abandoned session を " + deleted + " 件 cleanup (1 日超放置)");

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
                selfRegistered = true;
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

                // (#179 round 7 M-3) partial-success rollback: step (2) 成功 + step (3) 失敗で self row が
                // 残ると、他 PC からは「自 PC で起動中」と誤検出される (= 最大 60 秒間の false positive、
                // 検出閾値で非表示になるまで。#271 で cleanup を 1 日 abandoned 化したため物理削除は次回起動
                // UPSERT 上書きで起きるが、誤検出が止まるのは検出閾値の 60 秒)。rollback で即座に DELETE、
                // docstring claim「DB 不到達は heartbeat 不在で機能退化、self row も残らない」を物理保証する。
                // rollback 自体の失敗は検出閾値 (60 秒) と次回 UPSERT に委ねる (= 二重失敗の log noise のみ Warn)。
                if (selfRegistered)
                {
                    try
                    {
                        _repo.DeleteSelfSession(_pcName);
                        Logger.Info("[ManagerSessionService] Initialize partial-success rollback: self row 削除 (pc=" + _pcName + ")");
                    }
                    catch (Exception rollbackEx)
                    {
                        Logger.Warn("[ManagerSessionService] Initialize partial-success rollback 失敗 (検出閾値 60 秒で非表示・次回起動 UPSERT で物理上書き、最大 60 秒の false positive 残存): " + rollbackEx.Message);
                    }
                }
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
                    // Task.Wait は thread block するが shutdown 同期 path で許容 (= 通常数秒以内に完了)。
                    // (round 1 L-2) OperationCanceled だけ silent swallow、他 inner type (= heartbeat
                    // 最後の UpsertHeartbeat で SQLite 例外等) は Warn 出力で trail を残す。
                    // (round 8 L-1) `Wait(2s)` の戻り値 bool (false=timeout) を確認。timeout 経由で抜けた
                    // 場合 heartbeat task は **まだ生きている** ことが多く (= 内部で UpsertHeartbeat の
                    // ExecuteWithRetry が busy_timeout=10000ms × maxRetries=3 で最悪 ~30 秒 block 中)、
                    // 直後の `DeleteSelfSession(_pcName)` 実行 → heartbeat task が遅れて UpsertHeartbeat
                    // を完了させると **zombie self row** が再登録される silent race path がある。
                    // 他 PC からは検出閾値 (60 秒) で非表示になり、物理的には次回この PC の起動時 UPSERT で
                    // 上書きされるため致命的ではない (#271 で他 PC cleanup を 1 日 abandoned 化したので、回収は
                    // cleanup ではなく検出閾値 + 次回起動に依存)。log trail を残さないと triage 不能になるため
                    // Warn で記録する (silent fail-soft の暗黙運用を撤回、明示 trail に倒す)。
                    bool completedInTime = true;
                    try
                    {
                        if (_heartbeatTask != null)
                        {
                            completedInTime = _heartbeatTask.Wait(TimeSpan.FromSeconds(2));
                        }
                    }
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
                    if (!completedInTime)
                    {
                        Logger.Warn("[ManagerSessionService] heartbeat task が 2 秒以内に終了せず (= UpsertHeartbeat の SMB query が block 中の可能性)、検出閾値 60 秒で非表示・次回起動 UPSERT で物理上書き (zombie self row が最大 60 秒 active 表示される可能性)");
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
                        // (round 7 L-3) 旧 message の「token race?」臆測表現を撤回。実際の例外 type +
                        // message は embed 済なので triage 側は事実から原因判定可能、`?` で推測 hint を
                        // 残すと log noise に。
                        Logger.Warn("[ManagerSessionService] heartbeat Wait 例外 (継続): " + ex.GetType().Name + ": " + ex.Message);
                    }
                    if (IsCancelled(token)) break;

                    try
                    {
                        // (round 3 H-2 fix) `UPDATE WHERE pc_name = ...` だと row 不在時 silent no-op で
                        // 自 PC 永久不可視化 path があったため `INSERT OR REPLACE` UPSERT に変更。
                        // 万一 self row が削除されても (#271 後は他 PC cleanup が 1 日 abandoned 化したため
                        // 通常起きないが、手動削除等) 次の heartbeat で reanimate できる safety net。
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
                        // (round 7 L-3) 「失敗」表現を「例外」に統一、Wait 部分との表記揺れを解消。
                        Logger.Warn("[ManagerSessionService] heartbeat UpsertHeartbeat 例外 (継続): " + ex.GetType().Name + ": " + ex.Message);
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
