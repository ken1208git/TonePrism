using System;
using System.Collections.Generic;
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
    ///   - Launcher session tracking (PR3b 別 PR で SPEC §3.X の JSON drop folder 方式を採用予定)
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
                _heartbeatCts = new CancellationTokenSource();
                _heartbeatTask = Task.Run(() => HeartbeatLoop(_heartbeatCts.Token), _heartbeatCts.Token);

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
            try
            {
                if (_heartbeatCts != null)
                {
                    _heartbeatCts.Cancel();
                    // Task.Wait は thread block するが shutdown 同期 path で許容 (= 数秒以内に完了)
                    try { _heartbeatTask?.Wait(TimeSpan.FromSeconds(2)); }
                    catch (AggregateException) { /* OperationCanceled は想定済 */ }
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
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
                        if (token.IsCancellationRequested) break;
                        _repo.UpdateHeartbeat(_pcName, NowUnixMs());
                    }
                    catch (Exception ex)
                    {
                        // 1 回の heartbeat 失敗は致命的でない (次の 10 秒で retry)。Warn のみで継続。
                        Logger.Warn("[ManagerSessionService] heartbeat update 失敗 (継続): " + ex.Message);
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
    }
}
