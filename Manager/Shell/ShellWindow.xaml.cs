using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5) Win11 設定アプリ風シェルのプレビュー窓 (throwaway)。FluentWindow(Mica/ダーク) +
    /// NavigationView(左サイドバー) の見た目を実機確認するためのもの。本配線では各 NavigationView ページに
    /// 既存 WinForms セクションパネルを WindowsFormsHost でホストし、これが Manager の本シェルになる。
    /// 見た目 OK なら本シェルへ発展、ダメなら撤去。
    /// </summary>
    public partial class ShellWindow : FluentWindow
    {
        // (#245 PR5) ホストする DB 接続パネル (ゲーム/ストア等) に渡す dbManager。NavigationView の Page は
        // parameterless ctor で生成されるため、ページ側がここから取得する。暫定 static 共有 (startup 移管で
        // シェルが dbManager を正式所有したら整理する)。
        internal static DatabaseManager SharedDb;

        // (#245 PR5 startup移管 step2) 実パネルを単一インスタンスでホストするための MainForm 参照。
        // host ページが MainForm の内部アクセサ (例: GameSectionPanel) から実パネルを取得して
        // WindowsFormsHost に attach する (fresh 生成廃止)。暫定 static 共有 (SharedDb と同様、
        // orchestration の正式移管時に整理)。
        internal static MainForm HostForm;

        public ShellWindow()
        {
            InitializeComponent();
            // (#245 PR5 step4) 起動時は最初の実セクション (ゲーム) に着地する (旧: 飾りの PreviewPage)。
            Loaded += (_, _) => RootNavigation.Navigate(typeof(GameHostPage));
        }

        /// <summary>
        /// (#245 PR5 step4) 左ゾーンのステータス文字列 (DB状態 + ゲーム数) を更新する。MainForm が
        /// UpdateStatusBar から呼ぶ。WPF UI thread 以外から呼ばれても安全なよう Dispatcher で marshal する。
        /// </summary>
        public void SetStatusText(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetStatusText(text))); return; }
            StatusText.Text = text ?? string.Empty;
        }

        // ===== (#245 PR5 step4 v2) バックアップ feedback (進捗/✓/⚠/中止/今すぐ) =====
        // MainForm の既存 backup 状態メソッド (ShowBackupProgress 等) から転送されて呼ばれる。
        // ボタンは HostForm(MainForm) 経由で coordinator を叩く (owner = MainForm、旧 statusStrip と同実体)。
        private DispatcherTimer _backupClearTimer;
        private static readonly Brush BackupWhite = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
        private static readonly Brush BackupGreen = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5A));
        private static readonly Brush BackupOrange = new SolidColorBrush(Color.FromRgb(0xE0, 0x9F, 0x3E));

        /// <summary>バックアップ実行中: リング + 「バックアップ中… N%」+ 中止。</summary>
        public void ShowBackupProgress(int percent, string fileName)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowBackupProgress(percent, fileName))); return; }
            StopBackupClearTimer();
            int p = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
            BackupRing.Visibility = Visibility.Visible;
            BackupText.Foreground = BackupWhite;
            BackupText.Text = $"バックアップ中… {p}%";
            BackupCancelButton.Visibility = Visibility.Visible;
            BackupRecaptureButton.Visibility = Visibility.Collapsed;
            BackupPanel.Visibility = Visibility.Visible;
        }

        /// <summary>未バックアップ (失敗/中断): ⚠ 警告 + 「今すぐバックアップ」(sticky)。</summary>
        public void ShowBackupUnhealthy(string message)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowBackupUnhealthy(message))); return; }
            StopBackupClearTimer();
            BackupRing.Visibility = Visibility.Collapsed;
            BackupText.Foreground = BackupOrange;
            BackupText.Text = message ?? string.Empty;
            BackupCancelButton.Visibility = Visibility.Collapsed;
            BackupRecaptureButton.Visibility = Visibility.Visible;
            BackupPanel.Visibility = Visibility.Visible;
        }

        /// <summary>進捗 item を隠す。BackupText は直前の SetBackupStatus の ✓/⚠ を残すため触らない。</summary>
        public void HideBackupProgress()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(HideBackupProgress)); return; }
            BackupRing.Visibility = Visibility.Collapsed;
            BackupCancelButton.Visibility = Visibility.Collapsed;
            BackupRecaptureButton.Visibility = Visibility.Collapsed;
            if (string.IsNullOrEmpty(BackupText.Text)) BackupPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>完了状態 (✓ 成功 = 7秒で自動消去 / ⚠ 失敗 = sticky)。</summary>
        public void SetBackupStatus(string message, bool ok)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetBackupStatus(message, ok))); return; }
            StopBackupClearTimer();
            BackupRing.Visibility = Visibility.Collapsed;
            BackupCancelButton.Visibility = Visibility.Collapsed;
            BackupRecaptureButton.Visibility = Visibility.Collapsed;
            BackupText.Foreground = ok ? BackupGreen : BackupOrange;
            BackupText.Text = message ?? string.Empty;
            BackupPanel.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
            if (ok && !string.IsNullOrEmpty(message))
            {
                _backupClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
                _backupClearTimer.Tick += (s, e) =>
                {
                    StopBackupClearTimer();
                    BackupText.Text = string.Empty;
                    BackupPanel.Visibility = Visibility.Collapsed;
                };
                _backupClearTimer.Start();
            }
        }

        private void StopBackupClearTimer()
        {
            if (_backupClearTimer != null) { _backupClearTimer.Stop(); _backupClearTimer = null; }
        }

        private void BackupCancelButton_Click(object sender, RoutedEventArgs e) => HostForm?.CancelSessionBackup();
        private void BackupRecaptureButton_Click(object sender, RoutedEventArgs e) => HostForm?.RecaptureBackupNow();
    }
}
