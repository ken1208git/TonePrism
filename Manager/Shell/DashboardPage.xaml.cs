using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (ダッシュボード) セキュリティソフト風の「要対応チェックリスト」(左) + 概況タイル (右)。
    /// <see cref="DashboardService"/> が集めた登録不備 (RestoreReconciliationService 流用) を部員に分かる
    /// プレーン日本語で 1 リストに出し、× で恒久的に非表示 (settings 永続) にできる。総合ステータス
    /// (盾) は起動不能/スキーマ未完だけで赤くなり、画像欠落などの任意項目は緑のまま=対応を迫らない。
    /// 取得は SMB I/O を含むので Task.Run で背景実行する。生ログは部員に意味不明なため出さない (『ログ』タブが SoT)。
    /// </summary>
    public partial class DashboardPage : Page
    {
        // 総合ステータス盾の配色 (緑=準備OK / 琥珀=要対応)。文字は明色。
        private static readonly Brush OkBg = new SolidColorBrush(Color.FromRgb(0x2E, 0x4A, 0x2E));
        private static readonly Brush OkFg = new SolidColorBrush(Color.FromRgb(0x9F, 0xD7, 0x9F));
        private static readonly Brush WarnBg = new SolidColorBrush(Color.FromRgb(0x4A, 0x3C, 0x1E));
        private static readonly Brush WarnFg = new SolidColorBrush(Color.FromRgb(0xE0, 0xB9, 0x68));

        // ランチャー稼働バッジ (緑=稼働中 / 灰=停止中)。
        private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5A));
        private static readonly Brush IdleBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));

        private bool _loading;
        // この「表示」中に既に初回ロード + タイマー開始済か。NavigationView は初期レイアウト中に Loaded を複数回
        // 投げることがあるため 1 表示 1 回に絞る。Unloaded で reset するので、別ページから戻れば再取得 + タイマー再開。
        private bool _loadedThisShow;

        // 自動更新 (near-real-time)。ページ表示中だけ回し、Unloaded / 非表示で止める。二段構え:
        //  - 軽い「ランチャー稼働バッジ」= 1 秒ごと (responses/launcher_sessions/ の小さな heartbeat スキャンのみ)。
        //  - 重い全体 Gather (recon 全資産スキャン + backups 走査 + 件数) = 20 秒ごと (= 1 秒 ×20)。
        // findings/件数はめったに変わらず重いので 20 秒、稼働状況は即応したいので 1 秒、と粒度を分ける。
        // 各経路とも実行中なら skip (self-throttle) し、SMB が遅くても積み上がらない。
        private static readonly TimeSpan LauncherTickInterval = TimeSpan.FromSeconds(1);
        private const int FullRefreshEveryNTicks = 20; // 1 秒 ×20 = 20 秒で重い全体スキャン
        private readonly DispatcherTimer _autoRefreshTimer;
        private int _tickCount;
        private bool _launcherBusy;

        private DatabaseManager Db => ShellWindow.SharedDb;

        public DashboardPage()
        {
            InitializeComponent();
            _autoRefreshTimer = new DispatcherTimer { Interval = LauncherTickInterval };
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                if (!IsVisible) return;
                _tickCount++;
                if (_tickCount % FullRefreshEveryNTicks == 0)
                    _ = LoadAsync(silent: true);       // 重い全体更新 (badge も Populate 経由で更新される)
                else
                    await RefreshLauncherBadgeAsync();  // 軽量: ランチャーバッジだけ
            };
            Loaded += async (_, _) =>
            {
                if (_loadedThisShow) return;
                _loadedThisShow = true;
                _tickCount = 0;
                _autoRefreshTimer.Start();
                await LoadAsync();
            };
            Unloaded += (_, _) => { _loadedThisShow = false; _autoRefreshTimer.Stop(); };
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

        // silent=true: 自動更新 tick 用。スピナー / ボタン無効化を出さず、結果だけ in-place 反映する (チラつき防止)。
        private async Task LoadAsync(bool silent = false)
        {
            if (_loading) return;
            _loading = true;
            try
            {
                if (!silent)
                {
                    RefreshButton.IsEnabled = false;
                    LoadingPanel.Visibility = Visibility.Visible;
                }

                DatabaseManager db = Db;
                // LAN-wide ランチャー検出用サービスは MainForm (編集前競合チェックと同一インスタンス) から借りる。
                // 取得は UI スレッドで参照だけし、実スキャン (SMB I/O) は Task.Run 内で走らせる。
                var launcherSvc = ShellWindow.HostForm?.LauncherSessionService;
                DashboardSnapshot snap = await Task.Run(() => DashboardService.Gather(db, launcherSvc));
                Populate(snap);
            }
            catch (Exception ex)
            {
                Logger.Warn("[DashboardPage] 読み込み失敗: " + ex.Message);
            }
            finally
            {
                if (!silent) RefreshButton.IsEnabled = true;
                LoadingPanel.Visibility = Visibility.Collapsed;
                _loading = false;
            }
        }

        // 軽量: ランチャー稼働だけ取り直してバッジ更新 (1 秒間隔)。全体更新中 / 前回スキャン未完なら skip。
        private async Task RefreshLauncherBadgeAsync()
        {
            if (_loading || _launcherBusy) return;
            _launcherBusy = true;
            try
            {
                var svc = ShellWindow.HostForm?.LauncherSessionService;
                LauncherStatus ls = await Task.Run(() => DashboardService.DetectLauncher(svc));
                SetLauncherBadge(ls.Count, ls.PcNames);
            }
            catch (Exception ex) { Logger.Warn("[DashboardPage] ランチャーバッジ更新失敗: " + ex.Message); }
            finally { _launcherBusy = false; }
        }

        // ランチャー稼働バッジ (LAN 全体・stale 除外済)。0 台なら灰で中立 (全キオスク停止は正常もありうる)。
        // 稼働中は緑 + ツールチップに PC 名。全体 Populate と 1 秒間隔更新の両方が呼ぶ共通 setter。
        private void SetLauncherBadge(int count, List<string> pcNames)
        {
            bool up = count > 0;
            LauncherDot.Fill = up ? RunningBrush : IdleBrush;
            LauncherStatusText.Foreground = up ? RunningBrush : IdleBrush;
            LauncherStatusText.Text = up ? ("ランチャー稼働中（" + count + "台）") : "ランチャー停止中";
            LauncherBadge.ToolTip = (up && pcNames != null && pcNames.Count > 0)
                ? "稼働中のPC: " + string.Join("、", pcNames)
                : null;
        }

        private void Populate(DashboardSnapshot s)
        {
            var active = s.ActiveFindings ?? new List<DashboardFinding>();
            var dismissed = s.DismissedFindings ?? new List<DashboardFinding>();

            // ===== 要対応リスト (左) =====
            // 重大度順 (Critical→Recommended→Info)。各重大度内は収集順 (= 不備が先、ログが後)。
            var ordered = active.OrderBy(f => (int)f.Severity).ToList();
            FindingsList.ItemsSource = ordered;

            int critical = active.Count(f => f.Severity == FindingSeverity.Critical);
            int recommended = active.Count(f => f.Severity == FindingSeverity.Recommended);
            int info = active.Count(f => f.Severity == FindingSeverity.Info);

            bool any = ordered.Count > 0;
            FindingsList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            FindingsHeader.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            FindingsHeader.Text = "気になる項目（" + ordered.Count + "）";
            // 取得失敗時は「✓ 気になる項目はありません」の緑箱を出さない (未チェックを all-clear に見せない)。盾が代わりに警告。
            EmptyText.Visibility = (!any && !s.Failed) ? Visibility.Visible : Visibility.Collapsed;

            // 総合ステータス盾。赤化は「本当に壊れてる」critical だけ。画像欠落等は緑のまま=迫らない。
            // ただし取得失敗 (recon の AnalysisFailed 等) のときは緑「準備OK」と言い切らず警告にする
            // (チェックリストの存在意義は問題の表面化なので、失敗を all-clear に見せない)。
            if (critical > 0)
            {
                StatusIcon.Text = "⚠";
                StatusIcon.Foreground = WarnFg;
                StatusIconBg.Background = WarnBg;
                StatusTitle.Text = critical + "件の対応が必要です";
                StatusSubtitle.Text = "起動できないゲームやデータベースの問題があります"
                    + (s.Failed ? "（※一部の情報は取得できませんでした）" : "");
            }
            else if (s.Failed)
            {
                StatusIcon.Text = "⚠";
                StatusIcon.Foreground = WarnFg;
                StatusIconBg.Background = WarnBg;
                StatusTitle.Text = "一部を確認できませんでした";
                StatusSubtitle.Text = "情報の取得に失敗しました。「更新」で再試行するか、ログをご確認ください。";
            }
            else
            {
                StatusIcon.Text = "✓";
                StatusIcon.Foreground = OkFg;
                StatusIconBg.Background = OkBg;
                StatusTitle.Text = "準備は整っています";
                // 確認推奨 (recommended) と 参考 (info=画像未設定など) を分けて、軽い参考の束が「要対応」に見えないように。
                var parts = new List<string>();
                if (recommended > 0) parts.Add("確認をおすすめ " + recommended + " 件");
                if (info > 0) parts.Add("参考 " + info + " 件");
                StatusSubtitle.Text = parts.Count > 0
                    ? string.Join("・", parts) + "（任意・× で非表示にできます）"
                    : "起動を妨げる問題は見つかりませんでした";
            }

            // 非表示にした項目 (折り畳み)。
            DismissedList.ItemsSource = dismissed;
            DismissedExpander.Visibility = dismissed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DismissedExpander.Header = "非表示にした項目（" + dismissed.Count + "）";

            // ランチャー稼働バッジ (1 秒間隔の軽量更新と共有する setter)。
            SetLauncherBadge(s.LauncherSessionCount, s.LauncherPcNames);

            // ===== 概況タイル (右・独立: 登録コンテンツ + バックアップ) =====
            GamesValue.Text = s.VisibleGameCount + " / " + s.GameCount + " 件";
            SectionsValue.Text = s.StoreSectionCount + " 個";
            SlidesValue.Text = s.IntroSlideCount + " 枚";

            // 日時と (経過・トリガ) を明示改行で 2 行に分け、タイルを細くしても "避）" だけ折返す不格好を避ける。
            LastBackupValue.Text = s.LastBackupAt.HasValue
                ? s.LastBackupAt.Value.ToString("yyyy-MM-dd HH:mm")
                  + "\n（" + FormatAge(s.LastBackupAt.Value) + "・" + FormatTrigger(s.LastBackupTrigger) + "）"
                : "まだありません";
            BackupSummaryValue.Text = s.BackupCount + " 世代 / " + FormatBytes(s.BackupTotalBytes);

            TilesPanel.Visibility = Visibility.Visible;

            SubtitleText.Text = "最終更新 " + DateTime.Now.ToString("HH:mm:ss") + "・自動更新中";
        }

        // × = 非表示にして恒久的に黙らせる (settings 永続)。書込は SMB I/O を含むので背景で。
        private async void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DashboardFinding f && !string.IsNullOrEmpty(f.Id))
            {
                DatabaseManager db = Db;
                await Task.Run(() => DashboardService.Dismiss(db, f.Id));
                await LoadAsync();
            }
        }

        // 「戻す」= 非表示を解除して再びチェック対象に戻す。
        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DashboardFinding f && !string.IsNullOrEmpty(f.Id))
            {
                DatabaseManager db = Db;
                await Task.Run(() => DashboardService.Restore(db, f.Id));
                await LoadAsync();
            }
        }

        private static string FormatTrigger(string t)
        {
            switch (t)
            {
                case "auto": return "自動";
                case "manual": return "手動";
                case "safety": return "退避";
                default: return "不明";
            }
        }

        private static string FormatAge(DateTime when)
        {
            TimeSpan d = DateTime.Now - when;
            if (d.TotalMinutes < 1) return "たった今";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "分前";
            if (d.TotalHours < 24) return (int)d.TotalHours + "時間前";
            return (int)d.TotalDays + "日前";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 MB";
            double mb = bytes / (1024.0 * 1024.0);
            return mb < 1024 ? (mb.ToString("0.#") + " MB") : ((mb / 1024.0).ToString("0.##") + " GB");
        }
    }
}
