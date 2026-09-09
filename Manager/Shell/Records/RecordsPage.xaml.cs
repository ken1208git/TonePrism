using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell.Records
{
    /// <summary>
    /// (#464) 会期の記録を振り返る**読み取り専用**ページ。`responses/` のプレイ記録・アンケートを
    /// DB のゲーム情報 (名前 / ジャンル / 難易度 / 制作者) と突き合わせて集計する。
    ///
    /// **走査は 1 回・再集計はメモリ内**: `responses/` は本番では SMB 共有上にあり、日付タブや指標を
    /// 切り替えるたびに数百ファイルを開き直すのは往復の無駄。<see cref="PlayStatsLoader"/> で生データを
    /// 1 度だけ読み、<see cref="PlayStatsService.Compute"/> をその場で回す (「更新」を押したときだけ再走査)。
    ///
    /// **書き込みは一切しない。** 記録は取り直しの効かないデータで、会期はもう終わっている。この画面が
    /// 原因で 1 件でも壊れたら復旧手段が無いので、DB / responses への書き込み経路をここに足さないこと。
    ///
    /// UI は配線と表示に徹し、数値の意味づけ (順位・参考値・棒の長さ) は
    /// <see cref="RecordsPresenter"/> が持つ (AGENTS「UI は薄く、ロジックは外へ」)。
    /// </summary>
    public partial class RecordsPage : Page
    {
        private DatabaseManager Db { get { return ShellWindow.SharedDb; } }

        private PlayStatsData _data;
        private DateTime? _day;                 // null = 全期間
        private RecordsMetric _metric = RecordsMetric.Plays;
        private RecordsGroupAxis _axis = RecordsGroupAxis.Genre;
        private PlayStatsSnapshot _snapshot;

        private bool _loading;
        private bool _loadedThisShow;
        /// <summary>XAML の <c>IsChecked="True"</c> が InitializeComponent 中に Checked を投げるため、初期化中の再集計を抑える。</summary>
        private bool _ready;

        public RecordsPage()
        {
            InitializeComponent();
            Loaded += async (_, _) =>
            {
                if (_loadedThisShow) return;
                _loadedThisShow = true;
                _ready = true;
                if (_data == null) await LoadAsync();
                else Recompute();
            };
            // 別ページへ移ってから戻ったときに再表示できるよう戻す (再走査はしない = _data は保持)。
            Unloaded += (_, _) => { _loadedThisShow = false; };
        }

        // ------------------------------------------------------------------
        // 読み込み
        // ------------------------------------------------------------------

        private async Task LoadAsync()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                ContentRoot.Visibility = Visibility.Collapsed;
                EmptyPanel.Visibility = Visibility.Collapsed;
                RefreshButton.IsEnabled = false;

                DatabaseManager db = Db;
                // SMB 越しの全走査を含むので必ず背景スレッドで。
                _data = await Task.Run(() => PlayStatsLoader.Load(db));

                BuildDayChips();
                Recompute();
            }
            catch (Exception ex)
            {
                Logger.Warn("[RecordsPage] 記録の読み込みに失敗しました: " + ex.Message);
                ShowEmpty("記録を読み込めませんでした。詳しくは『ログ』タブを確認してください。\n" + ex.Message);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                RefreshButton.IsEnabled = true;
                _loading = false;
            }
        }

        /// <summary>記録のある日付ぶんだけチップを作る (「全期間」+ 各日)。</summary>
        private void BuildDayChips()
        {
            DayFilterPanel.Children.Clear();
            if (_data == null) return;

            AddDayChip("全期間", null, true);
            foreach (DateTime d in _data.AvailableDates)
            {
                int plays = 0;
                foreach (PlayRecordEntry p in _data.Plays) if (p.CreatedAtLocal.Date == d) plays++;
                AddDayChip(d.ToString("M/d") + "（" + plays + "）", d, false);
            }
            // 日が 1 つしか無いなら切り替える意味が無いので隠す。
            DayFilterPanel.Visibility = _data.AvailableDates.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            _day = null;
        }

        private void AddDayChip(string label, DateTime? day, bool isChecked)
        {
            var rb = new RadioButton
            {
                Content = label,
                GroupName = "day",
                IsChecked = isChecked,
                Style = (Style)FindResource("Chip"),
                Tag = day,
            };
            rb.Checked += (s, e) =>
            {
                if (!_ready) return;
                _day = (DateTime?)((RadioButton)s).Tag;
                Recompute();
            };
            DayFilterPanel.Children.Add(rb);
        }

        // ------------------------------------------------------------------
        // 集計 → 表示
        // ------------------------------------------------------------------

        /// <summary>読み込み済みの生データから、現在の期間・指標・切り口で作り直す (I/O なし)。</summary>
        private void Recompute()
        {
            if (_data == null) return;
            try
            {
                _snapshot = PlayStatsService.Compute(_data, _day);
                RecordsViewModel vm = RecordsPresenter.Build(_snapshot, _metric, _axis);
                Populate(vm);
            }
            catch (Exception ex)
            {
                Logger.Warn("[RecordsPage] 集計に失敗しました: " + ex.Message);
                ShowEmpty("集計できませんでした。詳しくは『ログ』タブを確認してください。\n" + ex.Message);
            }
        }

        private void Populate(RecordsViewModel vm)
        {
            StatusText.Text = BuildStatusText();

            if (vm.IsEmpty)
            {
                ShowEmpty(vm.EmptyMessage);
                return;
            }

            EmptyPanel.Visibility = Visibility.Collapsed;
            ContentRoot.Visibility = Visibility.Visible;

            TilesList.ItemsSource = vm.Tiles;
            RankingCaption.Text = vm.RankingCaption;
            GamesList.ItemsSource = vm.Games;
            CommentsCaption.Text = vm.CommentsCaption;
            CommentsList.ItemsSource = vm.Comments;
            OverallCaption.Text = vm.OverallCaption;
            OverallBars.ItemsSource = vm.OverallRatingBars;
            GroupsList.ItemsSource = vm.Groups;
            NotesList.ItemsSource = vm.Notes;

            BuildTimelines();
        }

        /// <summary>日ごとに 1 枚ずつ折れ線を積む。日付見出し + グラフ。</summary>
        private void BuildTimelines()
        {
            TimelinePanel.Children.Clear();
            if (_snapshot == null) return;

            if (_snapshot.Timelines.Count == 0)
            {
                TimelinePanel.Children.Add(new TextBlock
                {
                    Text = "この期間のプレイ記録がありません。",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 12,
                });
                return;
            }

            foreach (PlayStatsTimeline t in _snapshot.Timelines)
            {
                var header = new TextBlock
                {
                    Text = t.Date.ToString("M月d日（ddd）", CultureInfo.CurrentCulture) + "　" + t.Total + " プレイ",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xC2, 0xC7, 0xCC)),
                    Margin = new Thickness(0, 6, 0, 6),
                };
                TimelinePanel.Children.Add(header);
                TimelinePanel.Children.Add(new TimelineChart
                {
                    Timeline = t,
                    Height = 150,
                    Margin = new Thickness(0, 0, 0, 12),
                });
            }
        }

        private string BuildStatusText()
        {
            if (_data == null) return null;

            var parts = new List<string>();
            parts.Add("プレイ " + _data.Plays.Count + " 件 / アンケート " + _data.Surveys.Count + " 件");
            if (_data.AvailableDates.Count > 0)
            {
                parts.Add(_data.AvailableDates[0].ToString("M/d") + "〜"
                    + _data.AvailableDates[_data.AvailableDates.Count - 1].ToString("M/d"));
            }
            parts.Add(_data.LoadedAt.ToString("HH:mm") + " 時点");
            if (_data.GamesUnavailable) parts.Add("※ゲーム一覧を読めず番号のまま表示しています");
            if (_data.RecoveredFromFileNameCount > 0)
                parts.Add("※" + _data.RecoveredFromFileNameCount + " 件はファイル名から復元");
            if (_data.UnreadableFileCount > 0)
                parts.Add("※" + _data.UnreadableFileCount + " 件は読めず除外");
            return string.Join("　/　", parts);
        }

        private void ShowEmpty(string message)
        {
            EmptyText.Text = message;
            EmptyPanel.Visibility = Visibility.Visible;
            ContentRoot.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------
        // 操作
        // ------------------------------------------------------------------

        private void Metric_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            if (MetricRating.IsChecked == true) _metric = RecordsMetric.Rating;
            else if (MetricDuration.IsChecked == true) _metric = RecordsMetric.Duration;
            else if (MetricRate.IsChecked == true) _metric = RecordsMetric.ResponseRate;
            else _metric = RecordsMetric.Plays;
            Recompute();
        }

        private void Axis_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            if (AxisDifficulty.IsChecked == true) _axis = RecordsGroupAxis.Difficulty;
            else if (AxisGrade.IsChecked == true) _axis = RecordsGroupAxis.Grade;
            else if (AxisDeveloper.IsChecked == true) _axis = RecordsGroupAxis.Developer;
            else _axis = RecordsGroupAxis.Genre;
            Recompute();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadAsync();
        }
    }
}
