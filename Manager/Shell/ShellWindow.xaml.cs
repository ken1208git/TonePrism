using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5) Manager の本シェル (可視メイン窓)。Win11 設定アプリ風の FluentWindow(ダーク) +
    /// NavigationView(左サイドバー)。各 NavigationView ページが既存 WinForms セクションパネルを
    /// WindowsFormsHost で単一インスタンスホストする (設定のみ WPF ネイティブ)。MainForm は隠し裏方
    /// オーケストレータとして message loop を駆動し、シェル生成失敗時は旧 WinForms UI へ graceful fallback。
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

        // (#245 PR5) Windows タスクバー進捗の駆動用に、現行シェルを ProcessingDialog から参照するための
        // 静的ハンドル (SharedDb / HostForm と同じ暫定 static 共有パターン)。1 シェル/プロセスなので単一で足りる。
        internal static ShellWindow Instance;

        public ShellWindow()
        {
            InitializeComponent();
            Instance = this;
            // (ダッシュボード) 起動着地はダッシュボード (準備完了度の一目把握)。データは背景取得で固めない。
            Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
            // シェルが閉じたら Instance を掃除し、破棄済み窓を掴み続けないようにする (ProcessingDialog /
            // SplashScreenHost の stale なタスクバー参照を防ぐ)。SharedDb / HostForm は意図的に残す:
            // シェル close → MainForm.Close → 即プロセス exit で掃除不要、かつ close 中の host ページ
            // Unloaded が HostForm を参照するため、ここで null 化すると teardown 順序次第で NRE になる。
            Closed += (_, _) => { if (Instance == this) Instance = null; };
        }

        // (#362) WinForms メッセージループ上の WPF 窓 + NavigationView では、マウスホイール/二本指スクロールが
        // WPF の ScrollViewer に届かず効かない (キーボードは EnableModelessKeyboardInterop で解決済だが
        // ホイールは別問題)。窓の HWND で WM_MOUSEWHEEL を直接拾い、現在ページの ScrollViewer (PageScroll) を
        // 明示スクロールする。WinForms ホストページにはホイールが WinForms 側に行くため本 hook は no-op。
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var src = (System.Windows.Interop.HwndSource)System.Windows.PresentationSource.FromVisual(this);
            src?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (msg == WM_MOUSEWHEEL)
            {
                int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                var sv = FindScrollable(this);
                if (sv != null)
                {
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - delta);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        // 実際にスクロール可能 (ScrollableHeight>0) な ScrollViewer を visual tree から探す。無ければ null (no-op)。
        // 注: ページ自前の ScrollViewer は NavigationView 下で高さ非拘束になり scrollable=0 のことがある。
        // 実スクローラ (NavigationView の content ScrollViewer 等) を ScrollableHeight で特定する。
        private static System.Windows.Controls.ScrollViewer FindScrollable(System.Windows.DependencyObject root)
        {
            if (root is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var r = FindScrollable(VisualTreeHelper.GetChild(root, i));
                if (r != null) return r;
            }
            return null;
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

        private System.Windows.Threading.DispatcherTimer _toastTimer;

        /// <summary>(#324) 保存成功などの非モーダル成功トースト。右下隅に小さい一行を 160ms でフェードイン → 約2.6秒
        /// 保持 → 360ms でフェードアウトして畳む。WinForms ダイアログを置き換え、操作を止めない。UI スレッド外でも安全。
        /// 連続呼び出しはタイマー再起動で最新メッセージに差し替わる。</summary>
        public void ShowSuccessToast(string message)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowSuccessToast(message))); return; }
            ToastText.Text = message ?? string.Empty;
            ToastBox.Visibility = Visibility.Visible;
            ToastBox.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160))));

            _toastTimer?.Stop();
            _toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            _toastTimer.Tick += (_, __) =>
            {
                _toastTimer.Stop();
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(360)));
                fadeOut.Completed += (_, ___) => ToastBox.Visibility = Visibility.Collapsed;
                ToastBox.BeginAnimation(OpacityProperty, fadeOut);
            };
            _toastTimer.Start();
        }

        // ===== (#245 PR5) Windows タスクバーアイコンの進捗 (緑バー) =====
        // ProcessingDialog (全進捗オペレーションの関所) が ReportProgress からここを叩く。復元/バックアップ/
        // 更新/アセット処理が横断的にタスクバーへ乗る。表示は cosmetic なので、ここでの失敗は握り潰して
        // 進捗オペレーション本体 (worker) には絶対に例外を伝播させない。

        /// <summary>タスクバー進捗を更新する。indeterminate=不定 (Marquee 相当) / それ以外は 0-100% を緑バーで表示。</summary>
        internal void SetTaskbarProgress(int percentage, bool indeterminate)
        {
            try
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetTaskbarProgress(percentage, indeterminate))); return; }
                if (Taskbar == null) return;
                if (indeterminate)
                {
                    Taskbar.ProgressState = TaskbarItemProgressState.Indeterminate;
                }
                else if (percentage >= 0 && percentage <= 100)
                {
                    Taskbar.ProgressState = TaskbarItemProgressState.Normal;
                    Taskbar.ProgressValue = percentage / 100.0;
                }
            }
            catch { /* cosmetic: タスクバー表示の失敗は無視 */ }
        }

        /// <summary>タスクバー進捗を消す (オペレーション完了/中止/失敗時)。</summary>
        internal void ClearTaskbarProgress()
        {
            try
            {
                if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(ClearTaskbarProgress)); return; }
                if (Taskbar != null) Taskbar.ProgressState = TaskbarItemProgressState.None;
            }
            catch { /* cosmetic */ }
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
