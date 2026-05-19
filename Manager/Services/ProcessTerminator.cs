using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// Launcher / Companion プロセスの検出 + 待機 + (最終手段の) kill。Phase 4 (#108) の Manager UI が
    /// 置換前に呼ぶ。
    ///
    /// SPEC §3.7.3 [4] で「Launcher / 常駐 Companions 起動中なら閉じるよう案内」を Manager UI 側の責務と
    /// 定義。本 class は:
    ///   - <see cref="EnumerateRunning"/>: 起動中のプロセスをリストアップ (UI で「以下のプロセスを閉じてください」表示)
    /// のみを提供。手動 close を retry loop で促す UX (UpdateSectionPanel.btnUpdateNow_Click) に統一済。
    ///
    /// (#108 Phase 4 round 1 M5 fix) 旧版は `WaitForExit` / `KillAll` も持っていたが、UI 側から呼ばれる
    /// 配線がなく、`UpdaterClient.Spawn` の `forceKill` 引数も常に false 固定だったため dead code 化
    /// していた。docstring と実装の乖離が「強制終了 path が動くように見える」誤読を生むため削除。
    /// 将来 `--force-kill` UI を Manager UI 側に配線する場合は本 class に再導入する。
    ///
    /// process name 判定は AGENTS.md「Naming Conventions」の `TonePrism_<Name>` 命名規約に従う:
    ///   - Launcher: "TonePrism_Launcher"
    ///   - Companion (Updater 以外): `<install>/Companions/<Name>/` の dir 名から「TonePrism_<Name>」を導出
    ///   - Updater 自身は Manager UI から終了対象にしない (Manager が自分で spawn する仕組み)
    /// </summary>
    internal static class ProcessTerminator
    {
        public const string LauncherProcessName = "TonePrism_Launcher";

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
                    string procName = "TonePrism_" + name;
                    AppendIfRunning(list, procName, "Companion: " + name);
                }
            }
            return list;
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
