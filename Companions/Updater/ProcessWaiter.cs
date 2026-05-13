using System;
using System.Diagnostics;
using System.Threading;

namespace GCTonePrism.Updater
{
    /// <summary>
    /// Manager プロセスの完全終了を polling で待機する。
    ///
    /// SPEC §3.7.4 [責務 2]: Manager は caller。spawn 直後に graceful 終了を始めるが、.NET CLR の cleanup
    /// に数秒かかることがある。Updater 側は polling して結果が空になるまで待つ。timeout 経過後の挙動は
    /// --force-kill 引数で制御。
    ///
    /// 注: Launcher / 常駐 Companions の終了待機は **Manager UI 側の責務** (SPEC §3.7.3 [4]、Phase 4 で実装)。
    /// Updater は Manager のみを対象にする。
    ///
    /// **wait/kill 対象の決定** (Codex round 2 P1 #1 対応):
    ///   - `callerPid > 0` (Manager UI から `--caller-pid` 指定) → PID-only wait/kill
    ///     (`Process.GetProcessById(pid)` で対象を絞る、同 PC の他 install Manager を巻き添えにしない)
    ///   - `callerPid == -1` (未指定) → system-wide fallback (`GetProcessesByName("GCTonePrism_Manager")`)
    ///     (round 1 L5 で acknowledged、校内 1 PC 1 install 前提なら実害なしの fallback)
    ///   Phase 4 で Manager UI が `Process.GetCurrentProcess().Id` を渡す前提。
    /// </summary>
    internal static class ProcessWaiter
    {
        private const string ManagerProcessName = "GCTonePrism_Manager";
        private const int PollIntervalMs = 500;
        // 待機継続ログを N iter ごとに 1 回出す (PollIntervalMs × LogEveryNIter = 実 interval)。
        // 名前付き定数化 (シニアレビュー round 2 L4) で「PollIntervalMs を変えるとログ間隔も連動して
        // 暗黙に変わる」silent magic number 連動を解消、変更時に意図して両方触る形に。
        // 現状: 500ms × 10 = 5 秒ごとログ。
        private const int LogEveryNIter = 10;
        // force-kill 試行回数の上限 (Codex round 2 P2 #2)。permission denied 等で kill が失敗し続けると
        // continue で無限ループ → Updater が hang する path があった。bounded retry で必ず終わらせる。
        private const int MaxForceKillAttempts = 3;
        // process enumeration 失敗の連続許容回数 (Codex round 2 P2 #4)。IPC / WMI 一時不調等での throw を
        // 空配列 fallback すると「Manager 既終了」誤判定 → Manager 生存中に置換に進む silent path に。
        // 連続 N 回まで「unknown state、待機継続」扱いにし、それ以降は abort。
        private const int MaxEnumerationFailures = 5;

        /// <summary>
        /// Manager プロセスが全て終了するまで polling で待機する。
        /// </summary>
        /// <param name="timeoutSeconds">timeout 秒数 (0 で無制限)</param>
        /// <param name="forceKill">timeout 経過時に強制 kill するか</param>
        /// <param name="callerPid">caller Manager の PID (> 0 で PID-only モード、-1 で system-wide fallback、Codex round 2 P1 #1)</param>
        /// <returns>true: 終了確認できた / false: timeout 経過で残存 (forceKill=false) または force-kill bounded retry 超過 (forceKill=true) または enumeration 連続失敗</returns>
        public static bool WaitForManagerExit(int timeoutSeconds, bool forceKill, int callerPid)
        {
            var sw = Stopwatch.StartNew();
            int iter = 0;
            int forceKillAttempts = 0;
            int consecutiveEnumerationFailures = 0;

            if (callerPid > 0)
            {
                Logger.Info($"PID-only モード: caller-pid={callerPid} のみを wait/kill 対象にする");
            }
            else
            {
                Logger.Info($"system-wide モード: {ManagerProcessName}.exe 全て (--caller-pid 未指定、巻き添えリスクあり)");
            }

            while (true)
            {
                Process[] procs;
                bool enumerationFailed = false;
                try
                {
                    procs = GetTargetProcesses(callerPid);
                    consecutiveEnumerationFailures = 0;  // 成功で reset
                }
                catch (Exception ex)
                {
                    consecutiveEnumerationFailures++;
                    Logger.Warn($"process enumeration 失敗 ({iter} iter、連続 {consecutiveEnumerationFailures} 回): {ex.Message}");
                    procs = new Process[0];
                    enumerationFailed = true;
                    if (consecutiveEnumerationFailures >= MaxEnumerationFailures)
                    {
                        Logger.Error($"process enumeration が連続 {MaxEnumerationFailures} 回失敗、abort");
                        return false;
                    }
                }

                try
                {
                    // Codex round 2 P2 #4: enumeration 失敗時は「unknown state」扱い、空配列を Manager 既終了
                    // と誤判定しないよう、待機継続経路に流す (timeout 経過で fail)。
                    if (!enumerationFailed && procs.Length == 0)
                    {
                        if (iter > 0)
                        {
                            Logger.Info($"Manager プロセス終了確認 ({sw.Elapsed.TotalSeconds:F1}s 経過)");
                        }
                        else
                        {
                            // シニアレビュー round 1 L1: 初回 polling で既に終了済の場合もログを残す
                            Logger.Info("Manager プロセスは既に終了済み、待機 skip");
                        }
                        return true;
                    }

                    if (!enumerationFailed)
                    {
                        if (iter == 0)
                        {
                            Logger.Info($"Manager プロセス {procs.Length} 件検出、終了待機 (timeout {timeoutSeconds}s)");
                        }
                        else if (iter % LogEveryNIter == 0)
                        {
                            Logger.Info($"...待機継続中 ({sw.Elapsed.TotalSeconds:F1}s 経過、{procs.Length} 件残存)");
                        }
                    }

                    if (timeoutSeconds > 0 && sw.Elapsed.TotalSeconds >= timeoutSeconds)
                    {
                        if (forceKill && !enumerationFailed)
                        {
                            forceKillAttempts++;
                            if (forceKillAttempts > MaxForceKillAttempts)
                            {
                                // Codex round 2 P2 #2: bounded retry 超過で abort、無限ループ防止
                                Logger.Error($"force-kill 試行が {MaxForceKillAttempts} 回連続で残存プロセスを終了できず、abort");
                                return false;
                            }
                            Logger.Warn($"timeout {timeoutSeconds}s 経過、force-kill 試行 {forceKillAttempts}/{MaxForceKillAttempts}: Manager プロセスを強制終了します ({procs.Length} 件)");
                            KillAll(procs);
                            // kill 後 1 秒待って再 check
                            Thread.Sleep(1000);
                            continue;
                        }
                        else
                        {
                            string reason = enumerationFailed ? "enumeration 失敗継続中" : $"{procs.Length} 件残存";
                            Logger.Error($"timeout {timeoutSeconds}s 経過 ({reason})。--force-kill 未指定 or enumeration 失敗のため中止。");
                            return false;
                        }
                    }
                }
                finally
                {
                    foreach (var p in procs)
                    {
                        try { p.Dispose(); } catch { }
                    }
                }

                Thread.Sleep(PollIntervalMs);
                iter++;
            }
        }

        /// <summary>
        /// wait/kill 対象のプロセス配列を取得。PID-only モード or system-wide fallback。
        /// </summary>
        private static Process[] GetTargetProcesses(int callerPid)
        {
            if (callerPid > 0)
            {
                // PID-only モード: GetProcessById は対象不在で ArgumentException を投げるので捕捉して空配列扱い
                try
                {
                    Process p = Process.GetProcessById(callerPid);
                    return new[] { p };
                }
                catch (ArgumentException)
                {
                    // PID 不在 = プロセス終了済 (= 期待状態)
                    return new Process[0];
                }
            }
            // system-wide fallback
            return Process.GetProcessesByName(ManagerProcessName);
        }

        private static void KillAll(Process[] procs)
        {
            foreach (var p in procs)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        Logger.Info($"  kill PID={p.Id}");
                        p.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"  kill PID={p.Id} 失敗: {ex.Message}");
                }
            }
        }
    }
}
