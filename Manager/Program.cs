using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
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
            //
            // (#201, v0.15.0) `logs_root_path` を SQLite 直接 read + 旧 `log_destination_path` の auto-migrate
            // を **1 接続**で完結。SettingsRepository / DatabaseManager は使わない (= Logger は
            // SettingsRepository に依存しない invariant 維持、DB 不在時の fallback 経路もシンプル)。
            // SMB 共有 DB の latency 削減のため 2 関数分離前の round 3 review L-3 最適化 (= 1 起動 1 接続) を継承。
            // 失敗時は null fallback で default path。
            var initialSettings = ReadInitialLogSettingsWithMigration();
            Logger.Initialize(initialSettings.LogsRootPath);
            // (#201) PathManager.LogsRootDirectory も同じ値で 1 回 set (= UpdaterLogDir / LauncherLogDir 等の
            // 派生 getter が一貫した値を返すように)。SetLogsRootDirectory は **2 回目以降は
            // `InvalidOperationException` で fail-fast** (= R2 review M-5 で immutable invariant 厳守、
            // setter→getter 順序逆転 / 重複呼出を dev/test で発覚させる discipline)。
            PathManager.SetLogsRootDirectory(initialSettings.LogsRootPath);
            // (#201) Launcher への path 伝搬: `responses/launcher_logs_root.json` を atomic write。
            // Launcher Logger は autoload 最先頭 init 時に DB 接続前で本 file を読込、log dir を決定する。
            // 例外は内部で握り潰し済、Manager 起動を阻害しない。
            Services.LauncherLogsRootBridge.WriteCurrentLogsRoot(initialSettings.LogsRootPath);

            // (#170 followup round 2 review H-1) 古いログ削除を **MainForm 経由ではなく Program.Main で実行**。
            // 旧実装 (round 1) は MainForm.ContinueLoadAfterSessionCheck から呼んでいたが、
            // (a) dbReady=false (= 新規ユーザーが DB 作成 dialog で No 押下) / (b) SessionConflictDialog Cancel
            // という early-return path で CleanupOldLogs に到達せず、「DB 未作成のまま頻繁に起動 → 閉じる」
            // 運用でログが永久に溜まる silent regression があった。
            // 本 fix で SQLite 直接 read で retention 値を取得 → Logger.CleanupOldLogs を Program.Main で即時呼出。
            // MainForm 状態に依存しないため全起動 path で確実に走る。Logger は依然 SettingsRepository 不要
            // (invariant 維持)、DB 不在時は default 30 日に fallback。
            Logger.CleanupOldLogs(initialSettings.LogRetentionDays);

            // WebBrowser コントロール (UpdateSectionPanel のリリースノート表示で使用、Phase 4 #108) は
            // default で IE7 quirks mode で動作するため、render 崩れ + CSS 制限あり。HKCU レジストリで
            // 自プロセス名を IE11 emulation に登録する (best-effort、失敗してもアプリは起動可能)。
            TrySetIE11EmulationMode();

            // (#258 PR3) net10 化で旧 ServicePointManager.SecurityProtocol Tls12 設定を撤去。
            // net10 では ServicePointManager は obsolete (SYSLIB0014) かつ HttpClient の TLS に無効
            // (TLS は OS / SslStream が自動ネゴシエート) — 旧 net48 では HttpClient handshake に効いたが
            // net10 では no-op。Win10/11 + net10 は default で Tls12/13 を使うため挙動不変。

            // (#179) Named Mutex で同 PC 重複起動を物理 block。
            // - mutex name に install path hash を含めて dev 環境 (= repo) と本番 install (= 学校 LAN
            //   の install dir) で別 mutex に分離する。同 PC で複数 install dir 並存する場合も衝突なし。
            // - `Global\` prefix で Windows session 全体に effective (= 同 user の別 RDP session でも block)。
            // - createdNew=false で既に取得中の同 mutex 検出 → modal dialog → return (Application.Run 不到達)。
            // - mutex は process lifetime 中保持、`Application.Run` 終了で `using` 経由 release。
            // 詳細: SPEC §3.8 同時起動検出機構、CHANGELOG ## Manager v0.10.0 参照。
            string mutexName = "Global\\TonePrism_Manager_SingleInstance_" + ComputeInstallPathHash(Application.StartupPath);
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

                    // (#245 Phase 0/DPI) WinForms↔WPF 共存のため DPI awareness を起動時に明示固定する。
                    // Manager の WinForms フォームは DPI 非対応前提のレイアウト (app.manifest で DPI aware 化すると
                    // 全フォーム崩壊する — Services/DpiSafeFolderPicker.cs 参照)。一方 WPF は初回ロード時にプロセスを
                    // DPI aware へ切り替えようとするため、明示固定しないと WinForms 窓が素ピクセル=小窓化する
                    // (高拡大率で顕著)。DpiUnaware で固定すれば WPF の切替が no-op になり、WinForms は従来どおり
                    // 正しく bitmap-scale され、WPF も同様に scale される (高 DPI でやや soft だが正サイズ・一貫)。
                    // ※移行が進み WinForms フォームが WPF に置き換わったら PerMonitorV2 へ上げて crisp 化する (将来)。
                    // 最初の window 生成前に呼ぶ必要があるため EnableVisualStyles より前に置く。
                    Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    // (#245 PR5) WPF-UI のテーマ/リソースを app 全体で有効にするため WPF Application を確立する。
                    // App.xaml が無い (WinForms primary) ので C# で ThemesDictionary/ControlsDictionary を merge。
                    // WinForms 側が message loop を駆動するため WPF の Application.Run は呼ばない
                    // (ShutdownMode=OnExplicitShutdown で WPF window を閉じてもプロセスが落ちないようにする)。
                    var wpfApp = new System.Windows.Application
                    {
                        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                    };
                    wpfApp.Resources.MergedDictionaries.Add(
                        new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                    wpfApp.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());

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
                    catch (Exception relEx)
                    {
                        // (round 8 L-4) Warn message を ReleaseMutex 失敗の **cause** 主題に書換え。
                        // 旧 message「Dispose で abandoned 状態経由」は cause/effect が逆で、その後の
                        // Dispose 挙動を説明していて triage 上 noise。実際の cause は `Main` thread 以外
                        // (= ThreadPool / Task 経由) から ReleaseMutex を呼んだ場合の ApplicationException
                        // が typical (mutex を所有していない thread から release を試みた)、または
                        // AbandonedMutexException 経由で取得した case。 docstring L106-107 の「abandoned
                        // 状態経由」説明は preamble 側で retain (= 設計判断の根拠としては正しい)。
                        Logger.Warn("[Program] Mutex.ReleaseMutex 失敗 (mutex 非所有 thread からの release 等が typical cause): " + relEx.GetType().Name + ": " + relEx.Message);
                    }
                }
            }
        }

        /// <summary>
        /// (#201, v0.15.0) Logger.Initialize の前に `log_destination_path` (旧 v0.14.0) / `logs_root_path` /
        /// `log_retention_days` を **1 SQLite 接続**で同時取得 + 必要なら旧 key auto-migrate。
        /// SettingsRepository を経由しないのは Logger が SettingsRepository に依存しない invariant を
        /// 維持するため (= Logger は最早期に初期化する責務、DB が無くても動く必要がある)。
        ///
        /// **接続 1 つに統合した理由 (R2 review Medium #4 対応)**: 旧 R1 実装は migration check と read を
        /// 別関数 + 別接続にしていたが、毎起動 2 接続に増えて round 3 review L-3 の最適化 (= 1 接続/起動で
        /// SMB 共有 DB latency 軽減) を退行させていた。本実装で migration 完了後の通常 boot も 1 接続を維持。
        ///
        /// **auto-migrate 条件 + 副作用**:
        /// - 旧 key 値が非空 + 新 key 値が空 → INSERT new / DELETE old を 1 transaction、return は migrated 値
        ///   + sentinel file `&lt;BaseDirectory&gt;/.logs_root_migrated` を書出 (MainForm 経由で 1 回限り MessageBox)
        /// - 旧 key 値が非空 + 新 key 値も非空 → 二重設定状態、旧 key DELETE のみ (新値を正として尊重)
        /// - その他 (= 旧 key 空 / 全 key 不在) → no-op
        ///
        /// 失敗時 (DB 不在 / 解析失敗 / migration SQL 失敗) は default fallback で起動継続、
        /// Logger 未初期化のため warn 不可、try-catch で握り潰し。
        /// </summary>
        private static (string LogsRootPath, int LogRetentionDays) ReadInitialLogSettingsWithMigration()
        {
            string logsRootPath = null;
            int retentionDays = Services.SettingsKeys.DefaultLogRetentionDays;
            try
            {
                string dbPath = PathManager.DatabasePath;
                if (string.IsNullOrEmpty(dbPath) || !System.IO.File.Exists(dbPath))
                    return (logsRootPath, retentionDays);

                // migration の可能性があるため Read Only=True を外す (= 同 1 接続で read + write 両方やる)
                using (var conn = new System.Data.SQLite.SQLiteConnection("Data Source=" + dbPath + ";Version=3;"))
                {
                    conn.Open();

                    string legacyValue = null;
                    using (var cmd = new System.Data.SQLite.SQLiteCommand(
                        "SELECT key, value FROM settings WHERE key IN ('"
                        + Services.SettingsKeys.LogDestinationPath + "', '"
                        + Services.SettingsKeys.LogsRootPath + "', '"
                        + Services.SettingsKeys.LogRetentionDays + "')", conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string key = reader.IsDBNull(0) ? null : reader.GetString(0);
                            string val = reader.IsDBNull(1) ? null : reader.GetString(1);
                            if (key == Services.SettingsKeys.LogDestinationPath)
                            {
                                legacyValue = val;
                            }
                            else if (key == Services.SettingsKeys.LogsRootPath)
                            {
                                if (!string.IsNullOrWhiteSpace(val)) logsRootPath = val;
                            }
                            else if (key == Services.SettingsKeys.LogRetentionDays)
                            {
                                if (int.TryParse(val, out int days) && days > 0) retentionDays = days;
                            }
                        }
                    }

                    // Migration: 旧値あり + 新値なし → INSERT new / DELETE old + sentinel 書出
                    if (!string.IsNullOrWhiteSpace(legacyValue) && string.IsNullOrWhiteSpace(logsRootPath))
                    {
                        // (R6 review Low-1) 絶対 path invariant の defense-in-depth。旧 v0.14.0 UI の save 経路
                        // には IsPathRooted ガードが無かった (= Manager 設定 UI の ApplyLogSection で enforce) ため、
                        // 相対 path を持つ v0.14.0 install が存在しうる。相対値をそのまま `logs_root_path` に
                        // copy すると「logs_root_path は常に絶対 path」invariant が migration 経路だけ破れ、
                        // Launcher 側が CWD 依存の予測不能 path に倒れる。相対値の場合は **value copy を skip
                        // (= default 化) しつつ旧 key は DELETE** (= 再 migration loop 防止)、sentinel も書かない
                        // (= 引き継ぐ値がないので「そのまま引き継がれました」dialog は出さず silent に default)。
                        bool legacyIsAbsolute = false;
                        try { legacyIsAbsolute = System.IO.Path.IsPathRooted(legacyValue); }
                        catch { legacyIsAbsolute = false; } // 構文不正 path 等は相対扱いで default 化

                        try
                        {
                            if (legacyIsAbsolute)
                            {
                                using (var tx = conn.BeginTransaction())
                                {
                                    using (var insertCmd = new System.Data.SQLite.SQLiteCommand(
                                        "INSERT OR REPLACE INTO settings (key, value) VALUES ('"
                                        + Services.SettingsKeys.LogsRootPath + "', @v)", conn, tx))
                                    {
                                        insertCmd.Parameters.AddWithValue("@v", legacyValue);
                                        insertCmd.ExecuteNonQuery();
                                    }
                                    using (var deleteCmd = new System.Data.SQLite.SQLiteCommand(
                                        "DELETE FROM settings WHERE key = '"
                                        + Services.SettingsKeys.LogDestinationPath + "'", conn, tx))
                                    {
                                        deleteCmd.ExecuteNonQuery();
                                    }
                                    tx.Commit();
                                }
                                WriteLogsRootMigrationSentinel(legacyValue);
                                // migrated 値を return 値に reflect (= Logger は新 root で init される)
                                logsRootPath = legacyValue;
                            }
                            else
                            {
                                // 相対 path: value copy skip + 旧 key DELETE のみ (= default 化、sentinel なし)
                                using (var deleteCmd = new System.Data.SQLite.SQLiteCommand(
                                    "DELETE FROM settings WHERE key = '"
                                    + Services.SettingsKeys.LogDestinationPath + "'", conn))
                                {
                                    deleteCmd.ExecuteNonQuery();
                                }
                                // logsRootPath は空のまま (= Logger は default `<install>/logs/` で init)
                            }
                        }
                        catch
                        {
                            // migration SQL 失敗時: 旧 key も新 key も触らず、Logger は default path で init。
                            // 次回起動で再 migration が試みられる (idempotent)。
                        }
                    }
                    // 二重設定 cleanup: 旧 key 残存 + 新 key 設定済 → 旧 key DELETE のみ
                    else if (!string.IsNullOrWhiteSpace(legacyValue) && !string.IsNullOrWhiteSpace(logsRootPath))
                    {
                        try
                        {
                            using (var deleteCmd = new System.Data.SQLite.SQLiteCommand(
                                "DELETE FROM settings WHERE key = '"
                                + Services.SettingsKeys.LogDestinationPath + "'", conn))
                            {
                                deleteCmd.ExecuteNonQuery();
                            }
                        }
                        catch { /* swallow */ }
                    }
                }
            }
            catch
            {
                // DB 不在 / 解析失敗時は default fallback、Logger 未初期化のため warn 不可
            }
            return (logsRootPath, retentionDays);
        }

        /// <summary>
        /// migration 完了 sentinel file `&lt;BaseDirectory&gt;/.logs_root_migrated` を atomic write。
        /// MainForm.TryShowLogsRootMigratedDialog が読込んで 1 回限り MessageBox 表示 + sentinel 削除。
        /// wire format は **真の camelCase** (`migratedFrom` / `migratedAt`) — JavaScriptSerializer は
        /// underscore stripping を行わないため、snake_case を書くと PascalCase DTO property に bind できず
        /// dialog 永久不発火 (R2 review Critical #1 対応)。
        ///
        /// **crash window (R4 review M-4)**: caller (`ReadInitialLogSettingsWithMigration`) で `tx.Commit()`
        /// 成功直後 + 本関数呼出前に process crash した場合、次回起動で legacyValue は DELETE 済 +
        /// logsRootPath は新値 set 済 → migration 条件 (`legacyValue 非空 && logsRootPath 空`) 不成立 +
        /// cleanup 条件 (`両方非空`) も不成立 → silent に migration スキップ + sentinel 書出されない →
        /// **MainForm dialog 永久不発火 (= user は migration が起きたことに気付かない)**。
        /// 発生確率は μs オーダーで極めて低いが、user 通知欠落 path として明示。本 PR では受容、recovery
        /// 経路 (= sentinel を tx commit より前に書出 + commit 失敗時に sentinel 削除 pattern) は別 PR で検討。
        /// </summary>
        private static void WriteLogsRootMigrationSentinel(string migratedFrom)
        {
            try
            {
                string sentinelPath = System.IO.Path.Combine(PathManager.BaseDirectory, ".logs_root_migrated");
                string sentinelTmp = sentinelPath + ".tmp";
                string sentinelJson = "{\"migratedFrom\":\"" + Services.JsonEscape.EscapeString(migratedFrom) + "\","
                    + "\"migratedAt\":\"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\"}";
                System.IO.File.WriteAllText(sentinelTmp, sentinelJson, new System.Text.UTF8Encoding(false));
                if (System.IO.File.Exists(sentinelPath)) System.IO.File.Delete(sentinelPath);
                System.IO.File.Move(sentinelTmp, sentinelPath);
            }
            catch
            {
                // sentinel 書出失敗時は migration MessageBox 出ないだけで migration 自体は完了済、
                // 起動阻害なし。Logger 未初期化のため warn 不可。
            }
        }

        /// <summary>
        /// (#179) Named Mutex の name に含める install path hash を算出 (SHA256 ベース、衝突回避目的のみで
        /// crypto 用途ではない)。dev 環境と本番 install を別 mutex に分離するため、`Application.StartupPath`
        /// (= 自 exe の dir) を hash 化。
        ///
        /// **path 正規化** (round 2 Low-2 + round 5 L-5 で拡張):
        /// - `Path.GetFullPath()` で 8.3 短縮形式 (`PROGRA~1`) / 相対 path / 末尾 `\` 有無を解決
        /// - `ToLowerInvariant()` で case-insensitive 正規化 (NTFS / SMB default)
        /// - `Application.StartupPath` は呼び方 (cmd 直叩き / Explorer shortcut / Process.Start) で
        ///   casing / 8.3 表記が変わりうるため、同 install dir でも別 mutex に化ける drift を物理閉鎖
        /// - SMB UNC path の `\\?\` prefix 等は学校 LAN 運用では稀、本 PR では未対応 (別 issue 候補)
        ///
        /// **hash algorithm** (round 7 L-2 で MD5 → SHA256 移行): 旧 MD5 実装は `FIPS=enabled` group
        /// policy が適用された Windows 環境で `MD5.Create()` 自体が `InvalidOperationException ("not part
        /// of the Windows Platform FIPS validated cryptographic algorithms")` を throw する path があった。
        /// 本関数は mutex 取得 **前** に呼ばれるため `Main` の最外 catch がなく、企業 PC 流用の LAN 環境で
        /// FIPS 有効化されると Manager が silent crash する drift 路があった。SHA256 は FIPS 認証 algorithm
        /// で policy 影響なし、先頭 16 文字を使う形は維持 (= 衝突回避用途で 16 文字 = 64 bit で十分)。
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
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(normalized);
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16);
            }
        }

        /// <summary>
        /// WebBrowser コントロール (System.Windows.Forms.WebBrowser) を IE11 mode で動作させるための
        /// HKCU レジストリ設定。Phase 4 (#108) で UpdateSectionPanel のリリースノート表示に使う。
        ///
        /// レジストリ key: `HKCU\Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION`
        ///   value name: 自 exe のファイル名 (= "TonePrism_Manager.exe")
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
                // (#258 PR4) Assembly.Location は single-file で空 → Environment.ProcessPath (実 exe パス) を使う。
                string exeName = Path.GetFileName(Environment.ProcessPath ?? "");
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
