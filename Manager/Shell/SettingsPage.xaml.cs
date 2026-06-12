using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#362 step B1) 初の WPF ネイティブ画面。設定を「適用ボタン＋#201 ガード」から即時反映へ。
    /// 反映: トグル=即 / 数値=離脱時 / パス=離脱・参照選択時に <see cref="SettingsPathPolicy"/> で検証して反映。
    /// 書込先は <see cref="ShellWindow.SharedDb"/> の SettingsRepository (既存と同キー)。他 PC 競合チェックは
    /// MainForm 経由。B2 でバージョン情報 + DB リセットを追加予定。
    /// </summary>
    public partial class SettingsPage : Page
    {
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x5C));
        private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x9F, 0x3E));

        private bool _loading;
        private DatabaseManager Db => ShellWindow.SharedDb;

        public SettingsPage()
        {
            InitializeComponent();

            // ホイール/二本指スクロール対策は ShellWindow 側で WM_MOUSEWHEEL を直接拾って処理する
            // (WinForms ループ上の WPF 窓 + NavigationView で WPF の wheel routing に届かないため)。
            Loaded += (_, _) => LoadSettings();

            // (#362) 数値は ValueChanged で即反映 (spin / Enter 確定時)。LostFocus はナビ項目クリックでの
            // 離脱時に発火しないことがあり保存漏れになるため、値変化そのものを契機にする。
            LogRetentionBox.ValueChanged += (s, e) =>
            {
                if (!_loading) ApplyInt(SettingsKeys.LogRetentionDays, (int)Math.Round(LogRetentionBox.Value ?? 0), "ログ設定の適用");
            };
            BackupRetentionBox.ValueChanged += (s, e) =>
            {
                if (!_loading) ApplyInt("backup_retention_count", (int)Math.Round(BackupRetentionBox.Value ?? 0), "バックアップ設定の適用");
            };
            // (#362) ナビ離脱時にパスの未コミット直接入力を反映する保険 (prompt は出さない)。
            Unloaded += (_, _) => FlushPaths();
        }

        private void LoadSettings()
        {
            if (Db == null) return;
            _loading = true;
            try
            {
                var repo = Db.SettingsRepository;
                LogPathBox.Text = (repo.GetString(SettingsKeys.LogsRootPath, "") ?? "").Trim();
                LogRetentionBox.Value = Clamp(repo.GetInt32(SettingsKeys.LogRetentionDays, SettingsKeys.DefaultLogRetentionDays), 1, 3650);
                BackupPathBox.Text = (repo.GetString("backup_destination_path", "") ?? "").Trim();
                BackupAutoToggle.IsChecked = !string.Equals(repo.GetString(SettingsKeys.BackupAutoEnabled, "true"), "false", StringComparison.OrdinalIgnoreCase);
                BackupRetentionBox.Value = Clamp(repo.GetInt32("backup_retention_count", 30), 1, 999);
                LoadVersionInfo();
            }
            catch (Exception ex) { Logger.Warn("[SettingsPage] 設定読込失敗: " + ex.Message); }
            finally { _loading = false; }
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // 他 PC 競合チェック (MainForm 経由)。Cancel なら false = 書込中止。host 不在は OK 扱い。
        private bool AllowWrite(string label)
        {
            var host = ShellWindow.HostForm;
            return host == null || host.CheckSessionConflictBeforeWrite(label) != System.Windows.Forms.DialogResult.Cancel;
        }

        // ---- ログ ----
        private void LogPath_LostFocus(object sender, RoutedEventArgs e)
            => ApplyPath(LogPathBox, LogPathMsg, SettingsKeys.LogsRootPath, "ログ設定の適用", isLog: true);

        private void LogBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (Browse(LogPathBox)) ApplyPath(LogPathBox, LogPathMsg, SettingsKeys.LogsRootPath, "ログ設定の適用", isLog: true);
        }

        private void LogRetention_LostFocus(object sender, RoutedEventArgs e)
            => ApplyInt(SettingsKeys.LogRetentionDays, (int)Math.Round(LogRetentionBox.Value ?? 0), "ログ設定の適用");

        // ---- バックアップ ----
        private void BackupPath_LostFocus(object sender, RoutedEventArgs e)
            => ApplyPath(BackupPathBox, BackupPathMsg, "backup_destination_path", "バックアップ設定の適用", isLog: false);

        private void BackupBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (Browse(BackupPathBox)) ApplyPath(BackupPathBox, BackupPathMsg, "backup_destination_path", "バックアップ設定の適用", isLog: false);
        }

        private void BackupAuto_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || Db == null) return;
            if (!AllowWrite("バックアップ設定の適用")) return;
            try { Db.SettingsRepository.SetString(SettingsKeys.BackupAutoEnabled, BackupAutoToggle.IsChecked == true ? "true" : "false"); }
            catch (Exception ex) { Logger.Warn("[SettingsPage] 自動バックアップ保存失敗: " + ex.Message); }
        }

        private void BackupRetention_LostFocus(object sender, RoutedEventArgs e)
            => ApplyInt("backup_retention_count", (int)Math.Round(BackupRetentionBox.Value ?? 0), "バックアップ設定の適用");

        // ---- 共通 ----
        private bool Browse(Wpf.Ui.Controls.TextBox box)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "フォルダを選択してください";
                if (!string.IsNullOrWhiteSpace(box.Text))
                {
                    try { dialog.SelectedPath = box.Text.Trim(); } catch { /* 無効 path は初期位置で開く */ }
                }
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    box.Text = dialog.SelectedPath;
                    return true;
                }
            }
            return false;
        }

        private void ApplyInt(string key, int value, string label)
        {
            if (_loading || Db == null) return;
            if (!AllowWrite(label)) return;
            try { Db.SettingsRepository.SetInt32(key, value); }
            catch (Exception ex) { Logger.Warn("[SettingsPage] 数値設定保存失敗 (" + key + "): " + ex.Message); }
        }

        // (#362) ナビ離脱時にパスの直接入力を prompt なしで反映する保険。MissingLocal は prompt 不可のため skip。
        private void FlushPaths()
        {
            ApplyPath(LogPathBox, LogPathMsg, SettingsKeys.LogsRootPath, "ログ設定の適用", isLog: true, allowPrompt: false);
            ApplyPath(BackupPathBox, BackupPathMsg, "backup_destination_path", "バックアップ設定の適用", isLog: false, allowPrompt: false);
        }

        private void ApplyPath(Wpf.Ui.Controls.TextBox box, TextBlock msg, string key, string label, bool isLog, bool allowPrompt = true)
        {
            if (_loading || Db == null) return;
            string val = (box.Text ?? "").Trim();
            HideMsg(msg);

            switch (SettingsPathPolicy.Classify(val, Directory.Exists))
            {
                case SettingsPathKind.Empty:
                case SettingsPathKind.Ok:
                    break; // そのまま反映
                case SettingsPathKind.Relative:
                case SettingsPathKind.Invalid:
                    ShowMsg(msg, "絶対パスを入力してください (例: D:\\TonePrism_logs)。空欄でデフォルト。", ErrorBrush);
                    return; // 反映しない
                case SettingsPathKind.MissingLocal:
                    if (!allowPrompt) return; // flush (ナビ離脱) 時は prompt できないので適用しない
                    var r = MessageBox.Show($"フォルダ「{val}」は存在しません。作成しますか？",
                        "フォルダの作成", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return; // 作らない → 反映しない
                    try { Directory.CreateDirectory(val); }
                    catch (Exception ex) { ShowMsg(msg, "フォルダを作成できませんでした: " + ex.Message, ErrorBrush); return; }
                    break;
                case SettingsPathKind.Unreachable:
                    ShowMsg(msg, "現在アクセスできません (設定は保存します)。", WarnBrush);
                    break; // 反映は通す
            }

            if (!AllowWrite(label)) return;
            try
            {
                Db.SettingsRepository.SetString(key, val);
                if (isLog) LauncherLogsRootBridge.WriteCurrentLogsRoot(val);
                Logger.Info("[SettingsPage] " + label + ": " + (val.Length == 0 ? "(デフォルト)" : val));
            }
            catch (Exception ex) { ShowMsg(msg, "保存に失敗しました: " + ex.Message, ErrorBrush); }
        }

        private void ShowMsg(TextBlock msg, string text, Brush brush)
        {
            msg.Text = text;
            msg.Foreground = brush;
            msg.Visibility = Visibility.Visible;
        }

        private void HideMsg(TextBlock msg) => msg.Visibility = Visibility.Collapsed;

        // ---- (B2) バージョン情報 / DB リセット ----
        private void LoadVersionInfo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                Version v = asm.GetName().Version;
                string product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "TonePrism マネージャー";
                string ver = v == null ? "?" : (v.Major + "." + v.Minor + "." + v.Build + (v.Revision > 0 ? "." + v.Revision : ""));
                string copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
                int actual = Db?.GetActualDatabaseVersion() ?? -1;
                int target = Db?.GetTargetDatabaseVersion() ?? -1;
                VersionInfoText.Text =
                    "製品名: " + product + "\n" +
                    "バージョン: " + ver + "\n" +
                    "データベース構造: v" + actual + " (ターゲット: v" + target + ")\n" +
                    copyright + "\n" +
                    "ライセンス: MIT License";
            }
            catch (Exception ex)
            {
                VersionInfoText.Text = "バージョン情報の取得に失敗しました。";
                Logger.Warn("[SettingsPage] バージョン情報取得失敗: " + ex.Message);
            }
        }

        private void ResetDb_Click(object sender, RoutedEventArgs e)
        {
            // DB リセット本体は既存 WinForms SettingsSectionPanel に集約 (確認/進捗ダイアログ + ResetDatabase +
            // DatabaseReset イベントで MainForm が各パネルを再読込)。WPF 側は呼出後に設定表示を default へ同期。
            var panel = ShellWindow.HostForm?.SettingsSectionPanel;
            if (panel == null) return;
            panel.ResetDatabaseWithConfirm();
            LoadSettings();
        }
    }
}
