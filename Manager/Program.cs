using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
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
            // WebBrowser コントロール (UpdateSectionPanel のリリースノート表示で使用、Phase 4 #108) は
            // default で IE7 quirks mode で動作するため、render 崩れ + CSS 制限あり。HKCU レジストリで
            // 自プロセス名を IE11 emulation に登録する (best-effort、失敗してもアプリは起動可能)。
            TrySetIE11EmulationMode();

            // ログ機構を最初に初期化することで、PathManager 以降のすべての Console.WriteLine が
            // 自動的にファイル (logs/manager_YYYY-MM-DD.log) にも残る (#116)
            Logger.Initialize();

            // (#108 Phase 4 round 3 L-1) AppDomain global の SecurityProtocol を起動時 1 回設定。
            // GitHubReleaseChecker / BackupService 等 HttpClient 共有 consumer の SSL/TLS handshake が
            // 起動順序で behavior 差を持たないように、process 起動直後に Tls12 を含む形に固定。
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            }
            catch
            {
                // 古い .NET / SecurityProtocol 未定義の極端ケースは ignore (Win10/11 default で OK)
            }

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

        /// <summary>
        /// WebBrowser コントロール (System.Windows.Forms.WebBrowser) を IE11 mode で動作させるための
        /// HKCU レジストリ設定。Phase 4 (#108) で UpdateSectionPanel のリリースノート表示に使う。
        ///
        /// レジストリ key: `HKCU\Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION`
        ///   value name: 自 exe のファイル名 (= "GCTonePrism_Manager.exe")
        ///   value (DWORD): 11001 = IE11 Edge mode (latest)
        /// 詳細: https://docs.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/general-info/ee330730
        ///
        /// HKCU を使うので管理者権限不要。失敗 (RegOpenKeyEx denied / disk 不調) しても
        /// Logger に warn を残してアプリ起動は続行 (WebBrowser は IE7 mode の見た目になるが操作は可能)。
        /// </summary>
        private static void TrySetIE11EmulationMode()
        {
            try
            {
                string exeName = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(exeName)) return;
                using (var key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    if (key == null) return;
                    object current = key.GetValue(exeName);
                    if (current is int && (int)current == 11001) return; // 既に設定済
                    key.SetValue(exeName, 11001, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                // (#108 Phase 4 round 6 M-3) docstring「Logger に warn を残してアプリ起動は続行」と
                // 整合させるため Logger.Warn を仕込む。best-effort、失敗しても起動継続。
                // Note: Logger.Initialize() より前に呼ばれる場合は Logger 内部で no-op になる仕様の
                // ため Logger 初期化前 path も safe。
                try { Services.Logger.Warn("[Program] IE11 emulation 設定失敗 (best-effort、起動継続): " + ex.Message); }
                catch { /* Logger 初期化前 / Logger 内部例外も握り潰し */ }
            }
        }
    }
}
