using System;
using System.Diagnostics;
using System.IO;

namespace GCTonePrism.Updater
{
    /// <summary>
    /// GCTonePrism_Updater entry point.
    ///
    /// 責務 (SPEC §3.7.4 minimum scope):
    ///   1. CLI 引数 parse
    ///   2. Manager プロセス完全終了を polling 待機
    ///   3. `Manager/` dir を rename-rollback 方式で置換
    ///   4. 新 Manager.exe を起動
    ///   5. 自分終了
    ///
    /// Manager UI 側 (§3.7.3 [4]〜[10]、Phase 4) が Launcher / Companions / shortcut bat / Updater 自身の
    /// 置換まで担当した後で本 Updater を spawn する。本 Updater は spawn 後に Manager が graceful 終了
    /// するのを待ち、自分自身が Manager 置換 + 再起動を行う。
    ///
    /// Exit codes (CliArgs.UsageText() / SPEC §3.7.4 と三者同期、round 4 H-1 + M-1):
    ///   0 = 成功
    ///   1 = 予期しない実行時例外 (Logger に stack trace 残る、運用上 bug report 対象)
    ///   2 = 引数エラー (必須引数不足 / path 解析失敗 / --restart-exe が --manager-target 外 等)
    ///   3 = Manager プロセス終了 timeout (--force-kill 未指定、caller は --force-kill 付与か手動 close で再試行可能)
    ///   4 = ファイル置換失敗 (rollback 実施済、旧 Manager 復元)
    ///   5 = rollback も失敗した致命的状態 (手動復旧必要、`.bak` から手動 rename)
    ///   6 = 新 Manager.exe 起動失敗 (Process.Start null/throw、restart-exe 不在 等)
    ///   7 = force-kill 試行 bounded retry (3 回) 超過 (permission denied 等の構造的問題、機械的再試行は無意味)
    ///   8 = process enumeration 連続失敗 (5 回、IPC/WMI 一時障害、短時間後の再試行に意味あり)
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // ----- Step 0: 引数 parse -----
            CliArgs parsed;
            try
            {
                parsed = CliArgs.Parse(args);
            }
            catch (ArgumentException ex)
            {
                if (ex.Message == "HELP")
                {
                    Console.Out.WriteLine(CliArgs.UsageText());
                    return 0;
                }
                Console.Error.WriteLine($"[ERROR] 引数解析失敗: {ex.Message}");
                Console.Error.WriteLine();
                Console.Error.WriteLine(CliArgs.UsageText());
                return 2;
            }

            // ----- Step 0.5: Logger 初期化 -----
            // log-dir 未指定なら manager-target の親 dir / logs / updater を default に使う
            // (SPEC §3.7.4: `<install>/logs/updater/`、シニアレビュー round 1 M1 + L3 で
            //  UsageText / Logger fallback の三者一致に揃え直した。Program 側で最終 path を確定
            //  して Logger.Initialize に渡す方針。Logger.Initialize 内の exe-relative fallback は
            //  **通常運用では到達しない** が、drive root 病的入力等の極限ケース用に残してある
            //  ※ round 3 M5 で "到達不能" 表現を訂正。fallback は defensive、消さないこと)
            string logDir = parsed.LogDir;
            if (string.IsNullOrEmpty(logDir))
            {
                // CliArgs.Parse で ManagerTargetDir は GetFullPath 済み (絶対パス保証、M3)
                // なので親 dir 計算は通常成功。null になる極限ケース (drive root が manager-target
                // 等の病的入力) のみ Logger 側 fallback に流れる。
                string managerParent = Path.GetDirectoryName(parsed.ManagerTargetDir.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(managerParent))
                {
                    logDir = Path.Combine(managerParent, "logs", "updater");
                }
            }
            Logger.Initialize(logDir);

            int exitCode;
            try
            {
                exitCode = Run(parsed);
            }
            catch (Exception ex)
            {
                Logger.Error("予期しない例外で abort", ex);
                exitCode = 1;
            }
            finally
            {
                Logger.Shutdown();
            }
            return exitCode;
        }

        private static int Run(CliArgs args)
        {
            Logger.Info("============================================================");
            Logger.Info("GCTonePrism_Updater (Phase 3 minimal CLI)");
            Logger.Info("============================================================");
            Logger.Info($"  --staging         : {args.StagingDir}");
            Logger.Info($"  --manager-target  : {args.ManagerTargetDir}");
            Logger.Info($"  --restart-exe     : {args.RestartExe}");
            Logger.Info($"  --wait-timeout    : {args.WaitTimeoutSeconds}s");
            Logger.Info($"  --force-kill      : {args.ForceKill}");
            Logger.Info($"  --caller-pid      : {(args.CallerPid > 0 ? args.CallerPid.ToString() : "<未指定 = system-wide fallback>")}");
            Logger.Info("------------------------------------------------------------");

            // ----- Step 1: Manager プロセス終了待機 -----
            // round 4 H-1: ProcessWaiter は WaitResult enum を返すようになり、3 種の失敗
            // (timeout / force-kill 超過 / enumeration 失敗) を別 exit code に分岐できる。
            // Phase 4 Manager UI が再試行戦略を組む際に「再試行で直る (8)」「user 介入要 (3)」
            // 「構造的問題 (7)」を区別可能。
            Logger.Info("[Step 1/3] Manager プロセスの終了を待機");
            WaitResult waitResult = ProcessWaiter.WaitForManagerExit(args.WaitTimeoutSeconds, args.ForceKill, args.CallerPid);
            switch (waitResult)
            {
                case WaitResult.Success:
                    break;
                case WaitResult.TimedOutNoForceKill:
                    Logger.Error("Manager プロセスが timeout 内に終了せず (--force-kill 未指定)。--force-kill 付与か手動 close 後に再試行してください。");
                    return 3;
                case WaitResult.ForceKillExhausted:
                    Logger.Error("force-kill 試行が bounded retry 上限に達して abort。permission denied 等の構造的問題、機械的再試行では解決しません。");
                    return 7;
                case WaitResult.EnumerationFailed:
                    Logger.Error("process enumeration が連続失敗で abort。IPC/WMI 一時不調の可能性、短時間後の再試行で回復するケースあり。");
                    return 8;
                default:
                    Logger.Error($"未知の WaitResult: {waitResult}");
                    return 1;
            }

            // ----- Step 2: Manager dir 置換 (Replace + restart-exe 検証 + CleanupBak) -----
            // シニアレビュー round 1 H1 対応: 旧実装は FileReplacer.Replace 内で .bak 削除まで
            // 行ってから restart-exe 検証していたため、release packaging bug 等で staging に新 exe
            // が無いケースで「旧 Manager 消失 + 新 Manager 不在」の復旧不能 broken state があった。
            // 修正: Replace は Step 1 (rename) + Step 2 (copy) のみ → restart-exe 存在検証 → OK なら
            // CleanupBak、NG なら RollbackFromBak で旧 Manager 復元。これで H1 silent path を閉じる。
            Logger.Info("[Step 2/3] Manager dir 置換 (rename-rollback)");
            bool replaced;
            try
            {
                replaced = FileReplacer.Replace(args.StagingDir, args.ManagerTargetDir);
            }
            catch (InvalidOperationException ex)
            {
                // rollback にも失敗した致命的状態
                Logger.Error($"FATAL: rollback failure", ex);
                return 5;
            }
            if (!replaced)
            {
                Logger.Error("ファイル置換失敗 (rollback で旧 Manager 復元済)");
                return 4;
            }

            // [Step 2 sub-validation] .bak 削除前に restart-exe が新 target に存在することを検証。
            // 不在なら旧 Manager を .bak から復元してから exit 6 (新 Manager 不在 + 旧 Manager 消失
            // の復旧不能 broken state を回避)。
            if (!File.Exists(args.RestartExe))
            {
                Logger.Error($"起動対象 exe が存在しません (staging に欠損か release packaging bug): {args.RestartExe}");
                Logger.Warn("旧 Manager を .bak から復元します...");
                try
                {
                    FileReplacer.RollbackFromBak(args.ManagerTargetDir);
                }
                catch (InvalidOperationException ex)
                {
                    Logger.Error($"FATAL: restart-exe 検証失敗後の rollback も失敗", ex);
                    return 5;
                }
                Logger.Warn("旧 Manager 復元完了。Install.bat 再実行で正しい staging から復旧してください。");
                return 6;
            }

            // 検証 OK → .bak 削除 (best-effort、失敗してもアップデート自体は成功扱い)
            FileReplacer.CleanupBak(args.ManagerTargetDir);

            // ----- Step 3: 新 Manager.exe 起動 -----
            // round 4 M-2: Process.Start の戻り値を null check。
            // UseShellExecute=true では「OS が既存プロセスを reuse した」「OS が起動を抑止した」等の
            // 場合に null を返すと公式ドキュメントで明記されている (Microsoft Docs: ProcessStartInfo)。
            // 旧実装は null/非 null 問わず "Manager 起動完了" ログを出していたため、起動が実体として
            // 走らなかったケースで Manager UI / 部員視点で「Updater は exit 0 で抜けたから OK」と
            // 誤誘導される silent false-success path があった。
            //
            // round 4 M-5: UseShellExecute=true は Manager.exe が requireAdministrator manifest を
            // 持つ場合に UAC prompt を別 window で出す挙動を含む。現 Manager は非 admin 前提
            // (SPEC §3.7.4 で明文化済) なので発火しないが、将来 admin 化された場合に「Updater が
            // 消えた直後に UAC prompt が突然出る」体験になる点は SPEC 規約として固定。
            Logger.Info("[Step 3/3] 新 Manager.exe を起動");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = args.RestartExe,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(args.RestartExe) ?? string.Empty
                };
                Process proc = Process.Start(psi);
                if (proc == null)
                {
                    Logger.Error($"Process.Start が null を返却 (起動が実体として走らず、shell association reuse / OS による起動抑止 等の可能性): {args.RestartExe}");
                    return 6;
                }
                // PID は best-effort で記録 (取得失敗しても起動自体は成功扱い)
                int? pid = null;
                try { pid = proc.Id; } catch { /* swallow */ }
                Logger.Info($"Manager 起動完了: {args.RestartExe}" + (pid.HasValue ? $" (PID={pid.Value})" : ""));
            }
            catch (Exception ex)
            {
                Logger.Error($"Manager 起動失敗", ex);
                return 6;
            }

            Logger.Info("============================================================");
            Logger.Info("Updater 全工程完了");
            Logger.Info("============================================================");
            return 0;
        }
    }
}
