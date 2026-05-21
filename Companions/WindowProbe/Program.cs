using System;
using System.Text;

namespace TonePrism.WindowProbe
{
    /// <summary>
    /// TonePrism_WindowProbe entry point.
    ///
    /// 単発クエリツール (SPEC §2.4 Companions)。指定 PID のプロセスツリーが可視 / 前面ウィンドウを
    /// 持つかを 1 回だけ判定し、結果を stdout に 1 行で出して終了する。
    /// Launcher が起動中→プレイ中の遷移検知 (#101) と前面化異常検知 (#216) のため ~150ms 間隔で
    /// 繰り返し呼び出す前提なので、起動毎にログファイルを作らない (高頻度 spawn でファイルが氾濫する)。
    /// 結果は stdout、エラーは stderr のみ。意味のある遷移ログは呼び出し元 Launcher 側で記録する。
    ///
    /// 使い方:
    ///   TonePrism_WindowProbe.exe &lt;pid&gt;
    ///
    /// stdout (1 行):
    ///   not_found          プロセス不在
    ///   not_visible        稼働中だが可視ウィンドウ無し
    ///   visible_background 可視ウィンドウあり、前面でない
    ///   visible_foreground 可視ウィンドウあり、かつ前面
    ///
    /// 終了コード:
    ///   0 = 成功 (stdout に結果)
    ///   2 = 引数エラー (stderr に usage)
    ///   1 = 予期しない実行時例外 (stderr に詳細)
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // stderr/stdout を UTF-8 に統一 (呼び出し元 Launcher が UTF-8 で読む)。
            try { Console.OutputEncoding = Encoding.UTF8; } catch { /* best-effort */ }

            if (args.Length == 1 && (args[0] == "-h" || args[0] == "--help" || args[0] == "/?"))
            {
                Console.Out.WriteLine(UsageText());
                return 0;
            }

            if (args.Length != 1 || !int.TryParse(args[0], out int pid) || pid <= 0)
            {
                Console.Error.WriteLine("[ERROR] 引数は正の整数 PID 1 個のみ。");
                Console.Error.WriteLine();
                Console.Error.WriteLine(UsageText());
                return 2;
            }

            try
            {
                string result = Win32Windows.Probe(pid);
                Console.Out.WriteLine(result);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] 予期しない例外: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static string UsageText()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "TonePrism_WindowProbe — 指定 PID のプロセスツリーの可視/前面ウィンドウ状態を判定する",
                "",
                "使い方:",
                "  TonePrism_WindowProbe.exe <pid>",
                "",
                "stdout (1 行): not_found | not_visible | visible_background | visible_foreground",
                "終了コード: 0=成功 / 2=引数エラー / 1=実行時例外",
            });
        }
    }
}
