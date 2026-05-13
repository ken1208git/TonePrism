using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace GCTonePrism.Updater
{
    /// <summary>
    /// Manager プロセスの完全終了を polling で待機する。
    ///
    /// SPEC §3.7.4 [責務 2]: Manager は caller。spawn 直後に graceful 終了を始めるが、.NET CLR の cleanup
    /// に数秒かかることがある。Updater 側は Process.GetProcessesByName("GCTonePrism_Manager") を polling
    /// して、結果が空になるまで待つ。timeout 経過後の挙動は --force-kill 引数で制御。
    ///
    /// 注: Launcher / 常駐 Companions の終了待機は **Manager UI 側の責務** (SPEC §3.7.3 [4]、Phase 4 で実装)。
    /// Updater は Manager のみを対象にする。
    ///
    /// 注 (シニアレビュー round 1 L5): Process.GetProcessesByName は system-wide で全 Manager.exe を
    /// 検出する。校内運用は「1 PC = 1 install」想定 (SPEC §3.7.1 / §3.7.5) なので実害なしだが、
    /// テスト用 / production 用の Manager.exe が同 PC で同時稼働しているような edge case では両方
    /// を待機 / kill する。将来 caller (Manager UI) から `--caller-pid` で自身の PID を渡してもらい
    /// その PID のみ wait/kill する形に拡張する余地あり。
    /// </summary>
    internal static class ProcessWaiter
    {
        private const string ManagerProcessName = "GCTonePrism_Manager";
        private const int PollIntervalMs = 500;

        /// <summary>
        /// Manager プロセスが全て終了するまで polling で待機する。
        /// </summary>
        /// <param name="timeoutSeconds">timeout 秒数 (0 で無制限)</param>
        /// <param name="forceKill">timeout 経過時に強制 kill するか</param>
        /// <returns>true: 終了確認できた / false: timeout 経過で残存 (forceKill=false の場合)</returns>
        public static bool WaitForManagerExit(int timeoutSeconds, bool forceKill)
        {
            var sw = Stopwatch.StartNew();
            int iter = 0;
            while (true)
            {
                Process[] procs;
                try
                {
                    procs = Process.GetProcessesByName(ManagerProcessName);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Process.GetProcessesByName 失敗 ({iter} iter): {ex.Message}");
                    procs = new Process[0];
                }

                try
                {
                    if (procs.Length == 0)
                    {
                        if (iter > 0)
                        {
                            Logger.Info($"Manager プロセス終了確認 ({sw.Elapsed.TotalSeconds:F1}s 経過)");
                        }
                        else
                        {
                            // シニアレビュー round 1 L1: 初回 polling で既に終了済の場合もログを残す
                            // (待機 skip が機能していることを後追い確認できるように)
                            Logger.Info("Manager プロセスは既に終了済み、待機 skip");
                        }
                        return true;
                    }

                    if (iter == 0)
                    {
                        Logger.Info($"Manager プロセス {procs.Length} 件検出、終了待機 (timeout {timeoutSeconds}s)");
                    }
                    else if (iter % 10 == 0)
                    {
                        // 5 秒ごとに 1 回ログ
                        Logger.Info($"...待機継続中 ({sw.Elapsed.TotalSeconds:F1}s 経過、{procs.Length} 件残存)");
                    }

                    if (timeoutSeconds > 0 && sw.Elapsed.TotalSeconds >= timeoutSeconds)
                    {
                        if (forceKill)
                        {
                            Logger.Warn($"timeout {timeoutSeconds}s 経過、Manager プロセスを強制終了します ({procs.Length} 件)");
                            KillAll(procs);
                            // kill 後 1 秒待って再 check
                            Thread.Sleep(1000);
                            continue;
                        }
                        else
                        {
                            Logger.Error($"timeout {timeoutSeconds}s 経過、Manager プロセスが残っています ({procs.Length} 件)。--force-kill 未指定のため中止。");
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
