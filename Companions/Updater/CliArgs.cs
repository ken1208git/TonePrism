using System;
using System.Collections.Generic;

namespace GCTonePrism.Updater
{
    /// <summary>
    /// Updater の CLI 引数モデル + parser。
    ///
    /// 想定呼出し (Manager から):
    ///   GCTonePrism_Updater.exe ^
    ///     --staging "C:\Users\user\AppData\Local\Temp\GCTonePrism_update_0.2.1\" ^
    ///     --manager-target "D:\Games\GCTonePrism\Manager\" ^
    ///     --restart-exe "D:\Games\GCTonePrism\Manager\GCTonePrism_Manager.exe" ^
    ///     --log-dir "D:\Games\GCTonePrism\logs\updater\"
    ///
    /// 引数仕様 (SPEC §3.7.4):
    ///   --staging          (必須) staging dir のルート。`&lt;staging&gt;/files/Manager/` を新 Manager のソースとする
    ///   --manager-target   (必須) 既存 Manager dir。rename-rollback で置換する dir
    ///   --restart-exe      (必須) 置換後に起動する Manager.exe フルパス
    ///   --log-dir          (任意) ログ出力先。省略時は `<install>/logs/updater/` (manager-target の親から導出、SPEC §3.7.4 準拠)
    ///   --wait-timeout     (任意) Manager プロセス終了待ちの timeout 秒数 (default: 60、0 = 無制限待機)
    ///   --force-kill       (任意) timeout 経過後に Manager を強制 kill するか (default off)
    ///
    /// 引数 parse 失敗時は ArgumentException を投げる (Program.cs 側で catch + Logger.Error + exit 2)。
    /// </summary>
    internal sealed class CliArgs
    {
        public string StagingDir { get; private set; }
        public string ManagerTargetDir { get; private set; }
        public string RestartExe { get; private set; }
        public string LogDir { get; private set; }
        public int WaitTimeoutSeconds { get; private set; } = 60;
        public bool ForceKill { get; private set; }

        public static CliArgs Parse(string[] args)
        {
            if (args == null) throw new ArgumentException("引数が null です");

            var result = new CliArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                switch (key)
                {
                    case "--staging":
                        result.StagingDir = ReadValue(args, ref i, key);
                        break;
                    case "--manager-target":
                        result.ManagerTargetDir = ReadValue(args, ref i, key);
                        break;
                    case "--restart-exe":
                        result.RestartExe = ReadValue(args, ref i, key);
                        break;
                    case "--log-dir":
                        result.LogDir = ReadValue(args, ref i, key);
                        break;
                    case "--wait-timeout":
                        string ts = ReadValue(args, ref i, key);
                        int t;
                        if (!int.TryParse(ts, out t) || t < 0)
                        {
                            throw new ArgumentException($"--wait-timeout は 0 以上の整数を指定してください: '{ts}'");
                        }
                        result.WaitTimeoutSeconds = t;
                        break;
                    case "--force-kill":
                        result.ForceKill = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        throw new ArgumentException("HELP");
                    default:
                        throw new ArgumentException($"未知の引数: '{key}'");
                }
            }

            ValidateRequired(result);
            // path 引数 4 種を絶対パス化 (シニアレビュー round 1 M3 + L4)。
            //   - Manager UI (Phase 4) が相対 path を渡すケース、または OS テスト等で相対 path を
            //     渡された場合、Updater は spawn 元の CWD に依存して動く silent path があった。
            //   - Path.GetFullPath で正規化することで、後段の Logger / FileReplacer / Process.Start
            //     全箇所で path の絶対性を仮定できる。`"\\"` のような病的入力も `"C:\"` 等の
            //     drive root absolute path に正規化されるので L4 も副次的に解消。
            //
            // catch 範囲 (シニアレビュー round 2 M2): GetFullPath 公式契約のうち「引数自体の不備」
            // 系の例外のみ ArgumentException に変換 (= exit 2 引数エラー、user の入力ミスを明示)。
            //   - ArgumentException: null / 空 / invalid characters
            //   - PathTooLongException: MAX_PATH (260) 超過
            //   - NotSupportedException: ":" 含む (drive letter 以外) など
            // それ以外 (SecurityException / UnauthorizedAccessException 等の権限・環境問題) は
            // throw を抜けて Program.cs:77 の catch (Exception) で exit 1 (予期しない例外) に倒す。
            // 「引数エラー」表記の正確性を保ち、Manager UI 側の障害解析を misleading にしない。
            try
            {
                result.StagingDir = System.IO.Path.GetFullPath(result.StagingDir);
                result.ManagerTargetDir = System.IO.Path.GetFullPath(result.ManagerTargetDir);
                result.RestartExe = System.IO.Path.GetFullPath(result.RestartExe);
                if (!string.IsNullOrEmpty(result.LogDir))
                {
                    result.LogDir = System.IO.Path.GetFullPath(result.LogDir);
                }
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"path 引数の絶対パス化に失敗 (引数不備): {ex.Message}", ex);
            }
            catch (System.IO.PathTooLongException ex)
            {
                throw new ArgumentException($"path 引数が長すぎます (MAX_PATH 260 超過): {ex.Message}", ex);
            }
            catch (NotSupportedException ex)
            {
                throw new ArgumentException($"path 引数の形式がサポートされません: {ex.Message}", ex);
            }
            return result;
        }

        private static string ReadValue(string[] args, ref int i, string key)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"{key} に値が指定されていません");
            }
            i++;
            return args[i];
        }

        private static void ValidateRequired(CliArgs a)
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(a.StagingDir)) missing.Add("--staging");
            if (string.IsNullOrEmpty(a.ManagerTargetDir)) missing.Add("--manager-target");
            if (string.IsNullOrEmpty(a.RestartExe)) missing.Add("--restart-exe");
            if (missing.Count > 0)
            {
                throw new ArgumentException($"必須引数が不足: {string.Join(", ", missing)}");
            }
        }

        public static string UsageText()
        {
            return
                "GCTonePrism_Updater - Manager 置換 + 再起動の最小 CLI (SPEC §3.7.4)\n" +
                "\n" +
                "Usage:\n" +
                "  GCTonePrism_Updater.exe --staging <path> --manager-target <path> --restart-exe <path>\n" +
                "                          [--log-dir <path>] [--wait-timeout <seconds>] [--force-kill]\n" +
                "\n" +
                "Required:\n" +
                "  --staging <path>          staging dir のルート (中の files/Manager/ をソースに使う)\n" +
                "  --manager-target <path>   置換先の既存 Manager dir\n" +
                "  --restart-exe <path>      置換後に起動する Manager.exe のフルパス\n" +
                "\n" +
                "Optional:\n" +
                "  --log-dir <path>          ログ出力先 (default: <install>/logs/updater/、manager-target の親から導出、SPEC §3.7.4 準拠)\n" +
                "  --wait-timeout <seconds>  Manager プロセス終了待ちの timeout (default: 60、0 = 無制限待機)\n" +
                "  --force-kill              timeout 経過後に Manager を強制終了する\n" +
                "\n" +
                "Exit codes:\n" +
                "  0  成功\n" +
                "  2  引数エラー / 必須引数不足\n" +
                "  3  Manager プロセスが timeout 内に終了しなかった (--force-kill 未指定)\n" +
                "  4  ファイル置換に失敗 (rollback 実施済)\n" +
                "  5  rollback にも失敗した致命的状態\n" +
                "  6  新 Manager.exe の起動に失敗\n";
        }
    }
}
