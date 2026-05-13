using System;
using System.Diagnostics;
using System.Threading;

namespace GCTonePrism.Updater
{
    /// <summary>
    /// `WaitForManagerExit` の戻り値 (round 4 H-1 対応)。
    /// 旧 `bool` 返しでは 3 種類の失敗 (timeout / force-kill bounded retry exhausted /
    /// enumeration 連続失敗) を全て同じ exit 3 に倒していたため、Phase 4 Manager UI が
    /// 再試行戦略を分岐実装する際に区別できなかった問題を解消。各失敗を別 exit code に
    /// マップするための切り分け enum。
    /// </summary>
    internal enum WaitResult
    {
        /// <summary>Manager プロセスが期待通り終了した (→ exit 0 経路)</summary>
        Success,
        /// <summary>timeout 経過 + `--force-kill` 未指定 (→ exit 3、caller は --force-kill 付与か手動 close で再試行可能)</summary>
        TimedOutNoForceKill,
        /// <summary>`--force-kill` 指定下で MaxForceKillAttempts (3 回) 連続で kill 失敗 (→ exit 7、permission denied 等の構造的問題、機械的再試行は無意味)</summary>
        ForceKillExhausted,
        /// <summary>process enumeration が MaxEnumerationFailures (5 回) 連続で throw (→ exit 8、IPC/WMI 一時障害、短時間後再試行に意味あり)。round 5 M-3 で「連続 N 回失敗の早期 abort path 専用」に限定、timeout 経路では使わない (両者排他)</summary>
        EnumerationFailed,
    }

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
        /// <returns>失敗種別を区別した <see cref="WaitResult"/> (round 4 H-1、旧 bool 返しから差し替え)。caller (Program.cs) が switch で exit code 0/3/7/8 に分岐する</returns>
        public static WaitResult WaitForManagerExit(int timeoutSeconds, bool forceKill, int callerPid)
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
                        return WaitResult.EnumerationFailed;
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
                        return WaitResult.Success;
                    }

                    if (!enumerationFailed)
                    {
                        if (iter == 0)
                        {
                            // round 6 Low-2: `--wait-timeout 0 = 無制限待機` は UsageText / XML doc /
                            // SPEC §3.7.4 で公式仕様化済だが、ランタイムログには反映されておらず
                            // 「timeout 0s」と表示されると「0 秒待ち = 即 timeout」と誤読される
                            // 可能性。三項演算で表記分岐。
                            string timeoutDisplay = timeoutSeconds == 0 ? "無制限" : $"{timeoutSeconds}s";
                            Logger.Info($"Manager プロセス {procs.Length} 件検出、終了待機 (timeout {timeoutDisplay})");
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
                                return WaitResult.ForceKillExhausted;
                            }
                            Logger.Warn($"timeout {timeoutSeconds}s 経過、force-kill 試行 {forceKillAttempts}/{MaxForceKillAttempts}: Manager プロセスを強制終了します ({procs.Length} 件)");
                            KillAll(procs);
                            // kill 後 1 秒待って再 check
                            Thread.Sleep(1000);
                            continue;
                        }
                        else
                        {
                            // round 5 M-3: timeout 経路は **常に** TimedOutNoForceKill (exit 3) を返す。
                            //
                            // round 4 H-1 では「timeout 時に enumerationFailed なら EnumerationFailed
                            // (exit 8)」と分岐していたが、`enumerationFailed` 単独 (1 回でも失敗) で exit 8
                            // を返すと「偶発的 1 回失敗 + timeout コインシデンス」が exit 8 になり、Phase 4
                            // Manager UI が「短時間後再試行する価値あり」と誤判定 → 同じ timeout で再度
                            // exit 8 → 無限ループ化する path があった (round 5 M-3)。
                            //
                            // 修正方針: timeout 経路は常に TimedOutNoForceKill (exit 3) で、user 介入経路
                            // (--force-kill 付与 or 手動 close 後 retry) に倒す。EnumerationFailed (exit 8)
                            // は **`consecutiveEnumerationFailures >= MaxEnumerationFailures` の早期 abort
                            // path 専用** に限定 (line 100 付近)、両者排他。
                            string reason = enumerationFailed ? "enumeration 失敗中" : $"{procs.Length} 件残存";
                            Logger.Error($"timeout {timeoutSeconds}s 経過 ({reason})。--force-kill 未指定 or enumeration 失敗のため中止。");
                            return WaitResult.TimedOutNoForceKill;
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
        ///
        /// PID-only モード (Codex round 2 P1 #1) の **ProcessName 検証** (シニアレビュー round 3 H1):
        ///   Windows は exit 済プロセスの PID を別プロセスに再利用する。Manager (PID=1234) が Updater
        ///   spawn 直後に exit → OS が 1234 を別プロセス (例: notepad) に割当 → `GetProcessById(1234)`
        ///   が notepad を返し Manager と誤認 → `--force-kill` 指定時に notepad を kill する silent
        ///   danger があった。`ProcessName == "GCTonePrism_Manager"` の検証で誤認を排除。不一致なら
        ///   「Manager 既終了 + PID 再利用」とみなして空配列扱い (= 待機 skip 経路)。
        /// </summary>
        private static Process[] GetTargetProcesses(int callerPid)
        {
            if (callerPid > 0)
            {
                // PID-only モード: GetProcessById は対象不在で ArgumentException を投げるので捕捉して空配列扱い
                Process p;
                try
                {
                    p = Process.GetProcessById(callerPid);
                }
                catch (ArgumentException)
                {
                    // PID 不在 = プロセス終了済 (= 期待状態)
                    return new Process[0];
                }

                // ProcessName 検証 (PID 再利用での別プロセス誤認防止、H1)。
                // `.exe` 拡張子は ProcessName には含まれないので比較値は "GCTonePrism_Manager"。
                string actualName;
                try
                {
                    actualName = p.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    // ProcessName アクセス中にプロセス exit → Manager 終了済と同じ扱い
                    try { p.Dispose(); } catch { }
                    return new Process[0];
                }
                if (!string.Equals(actualName, ManagerProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Info($"PID={callerPid} は別プロセス '{actualName}' (PID 再利用と判定)、Manager 既終了扱い");
                    try { p.Dispose(); } catch { }
                    return new Process[0];
                }
                return new[] { p };
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
