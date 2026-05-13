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
    /// Exit codes (CliArgs.UsageText() も参照):
    ///   0 = 成功
    ///   2 = 引数エラー
    ///   3 = Manager プロセス終了 timeout (--force-kill 未指定)
    ///   4 = ファイル置換失敗 (rollback 実施済)
    ///   5 = rollback も失敗した致命的状態
    ///   6 = 新 Manager.exe 起動失敗
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
            Logger.Info("[Step 1/3] Manager プロセスの終了を待機");
            bool exited = ProcessWaiter.WaitForManagerExit(args.WaitTimeoutSeconds, args.ForceKill, args.CallerPid);
            if (!exited)
            {
                Logger.Error("Manager プロセスが終了しなかったため abort (--force-kill で強制終了可能)");
                return 3;
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
            Logger.Info("[Step 3/3] 新 Manager.exe を起動");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = args.RestartExe,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(args.RestartExe) ?? string.Empty
                };
                Process.Start(psi);
                Logger.Info($"Manager 起動完了: {args.RestartExe}");
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
