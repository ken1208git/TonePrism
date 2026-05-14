using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// Launcher / Companion プロセスの検出 + 待機 + (最終手段の) kill。Phase 4 (#108) の Manager UI が
    /// 置換前に呼ぶ。
    ///
    /// SPEC §3.7.3 [4] で「Launcher / 常駐 Companions 起動中なら閉じるよう案内」を Manager UI 側の責務と
    /// 定義。本 class は:
    ///   - <see cref="EnumerateRunning"/>: 起動中のプロセスをリストアップ (UI で「以下のプロセスを閉じてください」表示)
    ///   - <see cref="WaitForExit"/>: 手動 close を一定時間 polling で待機
    ///   - <see cref="KillAll"/>: timeout 後に user 明示同意の上で強制 kill
    /// を提供する。
    ///
    /// process name 判定は AGENTS.md「Naming Conventions」の `GCTonePrism_<Name>` 命名規約に従う:
    ///   - Launcher: "GCTonePrism_Launcher"
    ///   - Companion (Updater 以外): `<install>/Companions/<Name>/` の dir 名から「GCTonePrism_<Name>」を導出
    ///   - Updater 自身は Manager UI から終了対象にしない (Manager が自分で spawn する仕組み)
    /// </summary>
    internal static class ProcessTerminator
    {
        public const string LauncherProcessName = "GCTonePrism_Launcher";
        private const int PollIntervalMs = 500;

        /// <summary>
        /// 置換対象プロセス (Launcher + Companions/Updater 以外) で起動中のものを返す。
        /// 各 dir に対応する exe が起動中なら ProcessInfo を 1 件含める。
        /// </summary>
        public static IReadOnlyList<RunningProcessInfo> EnumerateRunning()
        {
            var list = new List<RunningProcessInfo>();

            // Launcher
            AppendIfRunning(list, LauncherProcessName, "Launcher");

            // Companions (Updater 以外)
            if (Directory.Exists(PathManager.CompanionsDir))
            {
                foreach (string companionDir in SafeEnumerateDirectories(PathManager.CompanionsDir))
                {
                    string name = Path.GetFileName(companionDir.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, "Updater", StringComparison.OrdinalIgnoreCase)) continue;
                    string procName = "GCTonePrism_" + name;
                    AppendIfRunning(list, procName, "Companion: " + name);
                }
            }
            return list;
        }

        /// <summary>
        /// 指定プロセス名すべての終了を polling で待機。timeout 経過時に false を返す。
        /// `processNames` が空 / null なら即 true。
        /// </summary>
        public static bool WaitForExit(IReadOnlyList<string> processNames, TimeSpan timeout, CancellationToken ct)
        {
            if (processNames == null || processNames.Count == 0) return true;
            var sw = Stopwatch.StartNew();
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                bool anyRunning = false;
                foreach (string n in processNames)
                {
                    if (CountRunning(n) > 0) { anyRunning = true; break; }
                }
                if (!anyRunning) return true;
                if (sw.Elapsed >= timeout) return false;
                Thread.Sleep(PollIntervalMs);
            }
        }

        /// <summary>指定プロセス名すべてを強制終了 (user 明示同意 path のみで呼ぶこと)。</summary>
        public static void KillAll(IReadOnlyList<string> processNames)
        {
            if (processNames == null) return;
            foreach (string n in processNames)
            {
                Process[] procs;
                try
                {
                    procs = Process.GetProcessesByName(n);
                }
                catch (Exception ex)
                {
                    Services.Logger.Warn("[ProcessTerminator] process enumeration 失敗 (" + n + "): " + ex.Message);
                    continue;
                }
                foreach (var p in procs)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            Services.Logger.Info("[ProcessTerminator] kill: " + n + " (PID=" + p.Id + ")");
                            p.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Services.Logger.Warn("[ProcessTerminator]   kill 失敗: " + ex.Message);
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
        }

        private static void AppendIfRunning(List<RunningProcessInfo> list, string processName, string displayLabel)
        {
            int count = CountRunning(processName);
            if (count > 0)
            {
                list.Add(new RunningProcessInfo
                {
                    ProcessName = processName,
                    DisplayLabel = displayLabel,
                    InstanceCount = count,
                });
            }
        }

        private static int CountRunning(string processName)
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName(processName);
            }
            catch
            {
                return 0;
            }
            int n = procs.Length;
            foreach (var p in procs)
            {
                try { p.Dispose(); } catch { }
            }
            return n;
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string root)
        {
            try
            {
                return Directory.EnumerateDirectories(root);
            }
            catch
            {
                return new string[0];
            }
        }
    }

    internal sealed class RunningProcessInfo
    {
        public string ProcessName { get; set; }
        public string DisplayLabel { get; set; }
        public int InstanceCount { get; set; }
    }
}
