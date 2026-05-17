using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// `Companions/Updater/GCTonePrism_Updater.exe` の spawn と exit code dispatch。Phase 4 (#108)。
    ///
    /// SPEC §3.7.4 で固定された Updater CLI 規約に従って Manager UI が Updater を起動する。重要な
    /// 規約 (Phase 3 round 6-8 で固まった):
    ///   - `UseShellExecute=false` (round 8: shell 経由 spawn は handle 紐付けが遅延)
    ///   - `RedirectStandardError=true` + `StandardErrorEncoding=UTF8` (round 7 Codex P2: stderr の文字化け回避)
    ///   - `--caller-pid &lt;Process.GetCurrentProcess().Id&gt;` を **必ず** 渡す (round 8 Codex P2-1: PID 再利用検知のため)
    ///   - exit codes 0-8 を documented dispatch (round 4 H-1 + M-1 で 3 → 3/7/8 分割、round 6 で 6 仕様化)
    ///
    /// 本 class は Updater を spawn した直後に Manager 自身を終了させるための「最後の関数」として
    /// 機能する。Manager は Updater を待たない (待つと Updater の Step 1/4 polling が Manager を
    /// 生存中と判定し続けて timeout してしまう) ため、spawn 直後に Application.Exit() を呼ぶ。
    /// </summary>
    internal static class UpdaterClient
    {
        /// <summary>
        /// Updater を spawn する。spawn 自体に失敗した場合は false を返す (= 起動失敗、Manager は終了しない)。
        /// 成功時 (true) は呼出元が即 Application.Exit() を呼んで Manager を終了させる責務。
        /// </summary>
        /// <param name="stagingDir">展開済み staging dir (絶対 path)</param>
        /// <param name="forceKill">user が「強制終了して再試行」を選択している場合 true</param>
        /// <param name="logSink">Updater stdout/stderr の async 読取結果を流す sink (null 可)。各行が arrival 順で渡される</param>
        public static bool Spawn(string stagingDir, bool forceKill, Action<string> logSink)
        {
            if (string.IsNullOrEmpty(stagingDir) || !Directory.Exists(stagingDir))
            {
                Services.Logger.Error("[UpdaterClient] staging dir が見つかりません: " + (stagingDir ?? "(null)"));
                return false;
            }
            string updaterExe = PathManager.UpdaterExePath;
            if (!File.Exists(updaterExe))
            {
                Services.Logger.Error("[UpdaterClient] Updater.exe が見つかりません: " + updaterExe);
                return false;
            }
            string managerExe;
            try
            {
                managerExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[UpdaterClient] Manager.exe path 取得失敗: " + ex.Message);
                return false;
            }

            var args = new List<string>
            {
                "--staging",        Quote(stagingDir),
                "--manager-target", Quote(PathManager.ManagerDir),
                "--restart-exe",    Quote(managerExe),
                "--log-dir",        Quote(PathManager.UpdaterLogDir),
                "--caller-pid",     Process.GetCurrentProcess().Id.ToString(),
            };
            if (forceKill) args.Add("--force-kill");
            string argLine = string.Join(" ", args);
            Services.Logger.Info("[UpdaterClient] Updater spawn: " + updaterExe + " " + argLine);

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = updaterExe,
                    Arguments = argLine,
                    UseShellExecute = false,
                    CreateNoWindow = false,           // Updater は console window を持つ短命プロセス、ユーザーに見える方が安心
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = Path.GetDirectoryName(updaterExe) ?? string.Empty,
                };

                Process proc = Process.Start(psi);
                if (proc == null)
                {
                    Services.Logger.Error("[UpdaterClient] Process.Start が null を返却");
                    return false;
                }

                // async event 読取で 4KB buffer deadlock 回避 (SPEC §3.7.4 round 7 Codex P2 規約)
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        Services.Logger.Info("[Updater stdout] " + e.Data);
                        if (logSink != null) try { logSink(e.Data); } catch { }
                    }
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        Services.Logger.Warn("[Updater stderr] " + e.Data);
                        if (logSink != null) try { logSink(e.Data); } catch { }
                    }
                };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                Services.Logger.Info("[UpdaterClient] Updater spawn 成功 (PID=" + proc.Id + ")。Manager は終了します。");
                // proc.Dispose() しない: child process が exit 前に handle close すると stream redirect が
                // 切れる。Manager が Application.Exit で抜けると process tree が綺麗に分離するので問題なし。
                return true;
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[UpdaterClient] Updater spawn 失敗: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// `<install>/logs/updater/` 配下の直近 **2 分以内** のログから Updater の exit code を回収する。
        /// 新 Manager が起動した直後 (MainForm_Load) で呼んで「前回アップデート結果」を UI バナーに出す用途。
        /// 該当ログが見つからない / parse 失敗時は null。
        ///
        /// **注意 (round 2 M8)**: 旧 docstring は「1 分以内」と書いていたが実装は `AddMinutes(-2)` (= 2 分)、
        /// round 1 H3 fix で書き直した際の追従漏れ。docstring を実装に揃えて訂正。
        /// </summary>
        public static int? TryLoadLastExitCode()
        {
            string dir = PathManager.UpdaterLogDir;
            if (!Directory.Exists(dir)) return null;
            try
            {
                DateTime cutoff = DateTime.Now.AddMinutes(-2);
                FileInfo latest = null;
                foreach (string path in Directory.EnumerateFiles(dir, "updater_*.log"))
                {
                    var fi = new FileInfo(path);
                    if (fi.LastWriteTime < cutoff) continue;
                    if (latest == null || fi.LastWriteTime > latest.LastWriteTime) latest = fi;
                }
                if (latest == null) return null;
                // ログ末尾近辺に Run() の各 Step 結果 / 例外を Logger が書いている。
                // exit code 自体はログには直接書かない (return statement なので)、ただし「全工程完了」/
                // 「予期しない例外で abort」/ 「[Step X/4] ... abort」等の言い回しから推測可能。
                // (#108 Phase 4 round 1 H3 fix) 旧実装は 0 か null のみ返却で 1-8 失敗 dispatch path が
                // dead だった。defensive fallback: 末尾 20 行を scan して "Updater 全工程完了" 検出 → 0、
                // それ以外で `[FATAL]` / `[ERROR]` 含む行があれば「不明だが失敗」として 1 を返す
                // (DispatchExitCode(1) = "内部エラー、ログを確認" の汎用文言になり、少なくとも banner
                // 自体は表示される)。詳細な exit code 取得は将来 event-driven 通知 (= Updater spawn 時に
                // pipe 経由で exit code 受信) に置換予定、本 fallback は中間 measure。
                string[] lines = File.ReadAllLines(latest.FullName);
                bool sawFailure = false;
                for (int i = lines.Length - 1; i >= 0 && i >= lines.Length - 20; i--)
                {
                    string line = lines[i];
                    if (line.Contains("Updater 全工程完了")) return 0;
                    // (#108 Phase 4 round 2 M8) 行頭 Logger format prefix (`[YYYY-MM-DD HH:mm:ss] [LEVEL]`)
                    // を確認してから level tag を検出することで、message 本文に偶然 `[ERROR]` 文字列を
                    // 含む log line (例: `Logger.Info("[FOO] mode=ERROR_RECOVERY")`) を false positive
                    // しないように anchor を厳密化。
                    if (line.StartsWith("[20") && (line.Contains("] [ERROR]") || line.Contains("] [FATAL]")))
                    {
                        sawFailure = true;
                    }
                }
                return sawFailure ? 1 : (int?)null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Updater の exit code を user-facing なメッセージ + 推奨アクションに変換 (SPEC §3.7.4 dispatch 規約)。
        /// 8 種類の exit code それぞれに対応した user 向け文言を返す。
        /// </summary>
        public static ExitCodeDispatch DispatchExitCode(int exitCode)
        {
            switch (exitCode)
            {
                case 0:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Success,
                        Title = "アップデート完了",
                        Message = "アップデートに成功しました。",
                        SuggestedAction = ExitAction.NoOp,
                    };
                case 1:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Error,
                        Title = "内部エラー",
                        Message = "Updater で予期しない例外が発生しました。ログを確認の上、再試行してください。",
                        SuggestedAction = ExitAction.OpenLog,
                    };
                case 2:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Error,
                        Title = "内部 bug",
                        Message = "Updater の引数組立に失敗しました。本問題は開発側の bug です。GitHub Issue で報告してください。",
                        SuggestedAction = ExitAction.OpenIssueTracker,
                    };
                case 3:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Warn,
                        Title = "Manager が時間内に閉じませんでした",
                        Message = "Manager プロセスの終了待機が timeout しました。手動で Manager を閉じてから再試行するか、「強制終了して再試行」を選んでください。",
                        SuggestedAction = ExitAction.RetryWithForceKill,
                    };
                case 4:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Warn,
                        Title = "ファイル置換失敗",
                        Message = "ファイル置換に失敗しました (旧 Manager は復元済)。ディスク空き容量とアンチウイルスソフトの設定を確認してから再試行してください。",
                        SuggestedAction = ExitAction.Retry,
                    };
                case 5:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Critical,
                        Title = "致命的エラー (手動復旧要)",
                        Message = "rollback にも失敗しました。`.bak` フォルダから手動で復元する必要があります。ログをコピーして開発者に連絡してください。",
                        SuggestedAction = ExitAction.OpenLog,
                    };
                case 6:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Warn,
                        Title = "新 Manager 起動失敗",
                        Message = "新しい Manager が起動できませんでした (旧 Manager は復元済)。再試行で改善しない場合は Install.bat で再 install してください。",
                        SuggestedAction = ExitAction.Retry,
                    };
                case 7:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Error,
                        Title = "Manager 強制終了不能",
                        Message = "force-kill 試行が上限に達しました (権限不足等)。タスクマネージャから Manager を終了してから再試行してください。",
                        SuggestedAction = ExitAction.Retry,
                    };
                case 8:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Warn,
                        Title = "一時的な不調",
                        Message = "プロセス列挙が連続失敗しました (IPC / WMI 一時障害の可能性)。数分後に再試行してください。",
                        SuggestedAction = ExitAction.Retry,
                    };
                default:
                    return new ExitCodeDispatch
                    {
                        Severity = ExitSeverity.Error,
                        Title = "不明な exit code: " + exitCode,
                        Message = "Updater が想定外の exit code (" + exitCode + ") を返しました。ログを確認してください。",
                        SuggestedAction = ExitAction.OpenLog,
                    };
            }
        }

        /// <summary>
        /// Process.Start の Arguments 用に文字列を quote する (空白 path を扱う)。
        ///
        /// **既知の制約 (round 1 L1 + round 2 L13)**: trailing backslash + `"` を含む path は厳密 escape
        /// しない。例: `C:\foo\` を quote すると `"C:\foo\"` → CommandLineToArgvW は末尾 `\"` を escaped
        /// quote と解釈して次の引数まで延長する。現状の caller (`PathManager.StagingRootForUpdate` /
        /// `ManagerDir` / `Assembly.Location` / `UpdaterLogDir`) は全て trailing backslash 無しで実害なし、
        /// trailing backslash 含む path を渡す path が将来出現する場合は `s.TrimEnd('\\')` で剥がすか
        /// CommandLineToArgvW 規約に従って `\` → `\\` escape を入れること。
        /// </summary>
        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            // 既に quote 付きならそのまま、含まない場合は囲む
            if (s.Contains(" ") || s.Contains("\t"))
            {
                // 内部 quote はバックスラッシュ escape (cmd 規約)
                return "\"" + s.Replace("\"", "\\\"") + "\"";
            }
            return s;
        }
    }

    internal sealed class ExitCodeDispatch
    {
        public ExitSeverity Severity { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public ExitAction SuggestedAction { get; set; }
    }
    internal enum ExitSeverity { Success, Warn, Error, Critical }
    internal enum ExitAction { NoOp, Retry, RetryWithForceKill, OpenLog, OpenIssueTracker }
}
