using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GCTonePrism.Manager.Services;

namespace GCTonePrism.Manager
{
    internal static class Program
    {
        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// </summary>
        [STAThread]
        static void Main()
        {
            // ログ機構を最初に初期化することで、PathManager 以降のすべての Console.WriteLine が
            // 自動的にファイル (logs/manager_YYYY-MM-DD.log) にも残る (#116)
            Logger.Initialize();

            try
            {
                // パスの確認（デバッグ用）
                PathManager.VerifyPaths();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (DirectoryNotFoundException ex)
            {
                Logger.Error("起動エラー (DirectoryNotFound)", ex);
                MessageBox.Show(
                    ex.Message,
                    "起動エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
            }
            catch (Exception ex)
            {
                Logger.Error("起動エラー", ex);
                MessageBox.Show(
                    $"アプリケーションの起動に失敗しました。\n\n{ex.Message}",
                    "起動エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
            }
            finally
            {
                Logger.Shutdown();
            }
        }
    }
}
