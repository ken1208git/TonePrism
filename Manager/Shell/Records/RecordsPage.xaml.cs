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
        private PlayStatsRange _range = PlayStatsRange.All;
        private RecordsMetric _metric = RecordsMetric.Plays;
        private RecordsGroupAxis _axis = RecordsGroupAxis.Genre;
        private PlayStatsSnapshot _snapshot;

        private bool _loading;
        private bool _loadedThisShow;
        /// <summary>XAML の <c>IsChecked="True"</c> が InitializeComponent 中に Checked を投げるため、初期化中の再集計を抑える。</summary>
        private bool _ready;
        /// <summary>ピッカーへ既定値を流し込む間だけ立てる (SelectedDateChanged の連鎖再集計を防ぐ)。</summary>
        private bool _suppressRangePickers;

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

                BuildRangeChips();
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

        /// <summary>
        /// 期間のチップを作る。**「全期間」と「期間を指定」の 2 つだけ**。
        ///
        /// 日ごとのチップは置かない。`responses/` は部活のたびに増えるので、日数に比例してチップが
        /// 伸び続ける。件数で表示を切り替える (少ないうちは日ごと / 増えたらプリセット) 案も入れて
        /// みたが、**データ量で画面が勝手に組み変わる**のは説明しづらく、切り替わる日が来ることを
        /// 誰も覚えていられない。会期の 2〜3 日だけ嬉しくて以降ずっと分かりにくい取引になる。
        ///
        /// 日ごとの件数は失われない — 時間帯グラフの見出しが「9月5日（土）　613 プレイ」を出す。
        /// </summary>
        private void BuildRangeChips()
        {
            DayFilterPanel.Children.Clear();
            CustomRangePanel.Visibility = Visibility.Collapsed;
            _range = PlayStatsRange.All;
            if (_data == null) return;

            List<DateTime> dates = _data.AvailableDates;

            AddChip("全期間", PlayStatsRange.All, true);
            AddCustomRangeChip(dates);

            // 期間が 1 日しか無いなら選びようが無いので帯ごと隠す。
            FilterArea.Visibility = dates.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddChip(string label, PlayStatsRange range, bool isChecked)
        {
            var rb = new RadioButton
            {
                Content = label,
                GroupName = "day",
                IsChecked = isChecked,
                Style = (Style)FindResource("Chip"),
                Tag = range,
            };
            rb.Checked += (s, e) =>
            {
                if (!_ready) return;
                CustomRangePanel.Visibility = Visibility.Collapsed;
                _range = (PlayStatsRange)((RadioButton)s).Tag;
                Recompute();
            };
            DayFilterPanel.Children.Add(rb);
        }

        /// <summary>
        /// 「期間を指定」チップ。選ぶと日付ピッカーが開く。**開いた時点では集計を変えない** —
        /// 既定値のまま勝手に絞り込むと、押した人が意図していない期間の数字を見ることになる。
        /// 初期値には記録の最初と最後を入れておき、動かした時点で初めて反映する。
        /// </summary>
        private void AddCustomRangeChip(List<DateTime> dates)
        {
            var rb = new RadioButton
            {
                Content = "期間を指定",
                GroupName = "day",
                Style = (Style)FindResource("Chip"),
            };
            rb.Checked += (s, e) =>
            {
                if (!_ready) return;
                _suppressRangePickers = true;
                try
                {
                    if (dates.Count > 0)
                    {
                        // 記録が 1 件も無い月へ迷い込めないよう、選べる範囲を記録の実期間に収める。
                        RangeFrom.DisplayDateStart = dates[0];
                        RangeFrom.DisplayDateEnd = dates[dates.Count - 1];
                        RangeTo.DisplayDateStart = dates[0];
                        RangeTo.DisplayDateEnd = dates[dates.Count - 1];

                        if (!RangeFrom.SelectedDate.HasValue) RangeFrom.SelectedDate = dates[0];
                        if (!RangeTo.SelectedDate.HasValue) RangeTo.SelectedDate = dates[dates.Count - 1];
                    }
                }
                finally { _suppressRangePickers = false; }

                CustomRangePanel.Visibility = Visibility.Visible;
                ApplyCustomRange();
            };
            DayFilterPanel.Children.Add(rb);
        }

        private void CustomRange_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _suppressRangePickers) return;
            if (CustomRangePanel.Visibility != Visibility.Visible) return;
            ApplyCustomRange();
        }

        private void ApplyCustomRange()
        {
            _range = PlayStatsRange.Between(RangeFrom.SelectedDate, RangeTo.SelectedDate);
            RangeSummary.Text = _range.IsAll ? null : _range.Label;
            Recompute();
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
                _snapshot = PlayStatsService.Compute(_data, _range);
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

            PopulateOverview(vm);
            RankingCaption.Text = vm.RankingCaption;
            GamesList.ItemsSource = vm.Games;
            CommentsCaption.Text = vm.CommentsCaption;
            CommentsList.ItemsSource = vm.Comments;
            OverallCaption.Text = vm.OverallCaption;
            OverallBars.ItemsSource = vm.OverallRatingBars;
            GroupsList.ItemsSource = vm.Groups;
            AwardsList.ItemsSource = vm.Awards;
            AwardsCard.Visibility = vm.HasAwards ? Visibility.Visible : Visibility.Collapsed;
            HighlightsList.ItemsSource = vm.Highlights;
            HighlightsCard.Visibility = vm.HasHighlights ? Visibility.Visible : Visibility.Collapsed;
            NotesList.ItemsSource = vm.Notes;

            BuildTimelines();
        }

        // 3 指標を見分けるための色。ダーク面で CVD 検証済み (validate_palette、最悪隣接 ΔE 8.4)。
        // 弧だけが色を持ち、文字は文字色トークンのままにする。
        private static readonly System.Windows.Media.Color RatingHue =
            System.Windows.Media.Color.FromRgb(0xC9, 0x85, 0x00);   // 黄
        private static readonly System.Windows.Media.Color ResponseHue =
            System.Windows.Media.Color.FromRgb(0x39, 0x87, 0xE5);   // 青
        private static readonly System.Windows.Media.Color CoverageHue =
            System.Windows.Media.Color.FromRgb(0x19, 0x9E, 0x70);   // 緑

        /// <summary>概況 (ヒーロー数値 + スパークライン + メーター 3 つ + 数値の帯) を流し込む。</summary>
        private void PopulateOverview(RecordsViewModel vm)
        {
            HeroLabel.Text = vm.HeroLabel;
            HeroValue.Text = vm.HeroValue;
            HeroSub.Text = vm.HeroSub;

            HeroSpark.Hue = ResponseHue;
            HeroSpark.Values = vm.HeroSpark;
            int[] hours = vm.HeroSpark;
            // 直接ラベルはピークだけ (点ごとに数字を置くと読まれないうえ画面が荒れる)。
            HeroSpark.LabelAt = i => hours != null && i >= 0 && i < hours.Length
                ? i.ToString("00") + "時　" + hours[i] + " プレイ"
                : null;

            ApplyMeter(vm.RatingMeter, RatingMeterHost, RatingMeter, RatingMeterValue,
                RatingMeterLabel, RatingMeterSub, RatingHue);
            ApplyMeter(vm.ResponseMeter, ResponseMeterHost, ResponseMeter, ResponseMeterValue,
                ResponseMeterLabel, ResponseMeterSub, ResponseHue);
            ApplyMeter(vm.CoverageMeter, CoverageMeterHost, CoverageMeter, CoverageMeterValue,
                CoverageMeterLabel, CoverageMeterSub, CoverageHue);

            StripList.ItemsSource = vm.Strip;
        }

        /// <summary>値が取れなかったメーターは**枠ごと隠す**(空の弧が「0%」に見えるのを避ける)。</summary>
        private static void ApplyMeter(RecordsMeter model, System.Windows.Controls.Grid host,
            StatMeter meter, TextBlock value, TextBlock label, TextBlock sub,
            System.Windows.Media.Color hue)
        {
            if (model == null)
            {
                host.Visibility = Visibility.Collapsed;
                return;
            }
            host.Visibility = Visibility.Visible;
            meter.Hue = hue;
            meter.Max = model.Max;
            meter.Value = model.Value;
            value.Text = model.ValueText;
            label.Text = model.Label;
            sub.Text = model.Sub;
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

        /// <summary>
        /// 見出し下の 1 行。**ここは「ファイルから読み込んだもの全部」の話で、下の集計とは母数が違う。**
        /// 期間を絞っても件数は動かない (絞り込みはメモリ上の再集計で、読み込み結果そのものは変わらない)
        /// ため、黙って並べると下の数字と食い違って見える。**先頭の「全期間：」がそれを引き受ける。**
        /// </summary>
        private string BuildStatusText()
        {
            if (_data == null) return null;

            var parts = new List<string>();
            string span = _data.AvailableDates.Count > 0
                ? "（" + _data.AvailableDates[0].ToString("M/d") + "〜"
                    + _data.AvailableDates[_data.AvailableDates.Count - 1].ToString("M/d") + "）"
                : string.Empty;
            parts.Add("全期間：プレイ " + _data.Plays.Count + " 件・アンケート "
                + _data.Surveys.Count + " 件" + span);
            // **日付は常に出す。** 走査は「更新」を押したときだけなので、この画面の数字がいつのものかは
            // ここだけが伝える。「当日は時刻だけ」にすると、日付が無いのが今日だからなのか表示が
            // 壊れているのか読み手には区別できず、条件で見え方が変わるぶん説明もしづらい。
            parts.Add(_data.LoadedAt.ToString("M/d HH:mm") + " 時点");
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
            else if (MetricDuration.IsChecked == true) _metric = RecordsMetric.TotalTime;
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
