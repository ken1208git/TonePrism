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
            // (#108 Phase 4 round 7 M-4) Logger.Initialize() を最先頭に移動。
            // 旧順序 (TrySetIE11EmulationMode → Logger.Initialize) では TrySetIE11EmulationMode の
            // catch 内 Logger.Warn (round 6 M-3 で導入) が常に Logger 未初期化状態で呼ばれ Logger 内部で
            // no-op になる silent path だった (= round 6 M-3 fix は「docstring に合わせたフリ」で
            // path 不到達)。本 round で reorder して `Logger.Warn` が実際にファイルに書かれる形に。
            // ログ機構を最初に初期化することで、PathManager 以降のすべての Console.WriteLine が
            // 自動的にファイル (logs/manager_YYYY-MM-DD.log) にも残る (#116)。
            Logger.Initialize();

            // WebBrowser コントロール (UpdateSectionPanel のリリースノート表示で使用、Phase 4 #108) は
            // default で IE7 quirks mode で動作するため、render 崩れ + CSS 制限あり。HKCU レジストリで
            // 自プロセス名を IE11 emulation に登録する (best-effort、失敗してもアプリは起動可能)。
            TrySetIE11EmulationMode();

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

            // (#179) Named Mutex で同 PC 重複起動を物理 block。
            // - mutex name に install path hash を含めて dev 環境 (= repo) と本番 install (= 学校 LAN
            //   の install dir) で別 mutex に分離する。同 PC で複数 install dir 並存する場合も衝突なし。
            // - `Global\` prefix で Windows session 全体に effective (= 同 user の別 RDP session でも block)。
            // - createdNew=false で既に取得中の同 mutex 検出 → modal dialog → return (Application.Run 不到達)。
            // - mutex は process lifetime 中保持、`Application.Run` 終了で `using` 経由 release。
            // 詳細: SPEC §3.8 同時起動検出機構、CHANGELOG ## Manager v0.10.0 参照。
            string mutexName = "Global\\GCTonePrism_Manager_SingleInstance_" + ComputeInstallPathHash(Application.StartupPath);
            using (var singleInstanceMutex = new System.Threading.Mutex(initiallyOwned: true, name: mutexName, createdNew: out bool createdNew))
            {
                if (!createdNew)
                {
                    Logger.Warn("[Program] 同 PC で Manager が既に起動中 (mutex 取得失敗、name=" + mutexName + ")、2 個目 process は exit");
                    MessageBox.Show(
                        "既に同じ PC で Manager が起動中です。\n\n" +
                        "2 つ目を起動すると編集内容や設定がお互いに上書きされて消える恐れがあります。\n" +
                        "もう一方を閉じてから再度お試しください。",
                        "Manager は 1 つだけ起動できます",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Stop);
                    // (#179 round 2 Low-1) 重複起動 path でも Logger.Shutdown を呼んで "Manager 終了" trail
                    // を残す。これがないと log 解析時に「crash したのか正常 exit したのか」区別できなくなる。
                    Logger.Shutdown();
                    return;
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
                    // (#179 round 1 L-1) initiallyOwned: true で取得した Mutex は ReleaseMutex で
                    // 明示 release してから Dispose する。`using` の Dispose 単独では kernel 上「abandoned
                    // mutex」状態を経由するため、将来 `WaitOne` 経由 pattern を追加した時に
                    // AbandonedMutexException を踏む path を予防。
                    try { singleInstanceMutex.ReleaseMutex(); }
                    catch (Exception relEx) { Logger.Warn("[Program] Mutex.ReleaseMutex 失敗 (Dispose で abandoned 状態経由): " + relEx.Message); }
                }
            }
        }

        /// <summary>
        /// (#179) Named Mutex の name に含める install path hash を算出 (MD5 ベース、衝突回避目的のみで
        /// crypto 用途ではない)。dev 環境と本番 install を別 mutex に分離するため、`Application.StartupPath`
        /// (= 自 exe の dir) を hash 化。
        ///
        /// **path 正規化** (round 2 Low-2 + round 5 L-5 で拡張):
        /// - `Path.GetFullPath()` で 8.3 短縮形式 (`PROGRA~1`) / 相対 path / 末尾 `\` 有無を解決
        /// - `ToLowerInvariant()` で case-insensitive 正規化 (NTFS / SMB default)
        /// - `Application.StartupPath` は呼び方 (cmd 直叩き / Explorer shortcut / Process.Start) で
        ///   casing / 8.3 表記が変わりうるため、同 install dir でも別 mutex に化ける drift を物理閉鎖
        /// - SMB UNC path の `\\?\` prefix 等は学校 LAN 運用では稀、本 PR では未対応 (別 issue 候補)
        /// </summary>
        private static string ComputeInstallPathHash(string installPath)
        {
            string normalized;
            try
            {
                // GetFullPath: 8.3 短縮形式展開 + 相対 path 解決 + 末尾 `\` 正規化
                normalized = System.IO.Path.GetFullPath(installPath ?? string.Empty).ToLowerInvariant();
            }
            catch (Exception)
            {
                // GetFullPath は invalid path char / I/O error 等で throw、fallback で生 path を lower 化
                normalized = (installPath ?? string.Empty).ToLowerInvariant();
            }
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(normalized);
                byte[] hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16);
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
                // (#108 Phase 4 round 6 M-3 → round 7 M-4) docstring「Logger に warn を残してアプリ
                // 起動は続行」と整合させるため Logger.Warn を仕込む。best-effort、失敗しても起動継続。
                // Note: round 7 M-4 で `Program.Main` 冒頭で `Logger.Initialize()` を本関数より先に呼ぶ
                // 順序に reorder したため、本 catch 到達時点で Logger は初期化済前提。それでも Logger
                // 自身の内部例外で再帰落ちしないように try/catch で握り潰す (AGENTS.md「Logger 自体の
                // 障害は握り潰す、再帰ハング回避」原則)。
                try { Services.Logger.Warn("[Program] IE11 emulation 設定失敗 (best-effort、起動継続): " + ex.Message); }
                catch { /* Logger 内部例外も握り潰し */ }
            }
        }
    }
}
