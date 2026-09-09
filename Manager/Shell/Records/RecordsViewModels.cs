using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell.Records
{
    /// <summary>ゲーム別ランキングの並べ替え軸。</summary>
    public enum RecordsMetric
    {
        /// <summary>遊ばれた回数。会期の「人気」として一番素直な指標。</summary>
        Plays,
        /// <summary>平均★。回答が少ないゲームは参考値扱いで下に落とす。</summary>
        Rating,
        /// <summary>プレイ時間の中央値。長く遊ばれた = 熱中度の代理指標。</summary>
        Duration,
        /// <summary>回答率 (アンケート / プレイ)。アンケートまで進んだ割合。</summary>
        ResponseRate,
    }

    /// <summary>横棒 1 本 (★内訳・時間分布・時間帯で共用)。幅は px で持たせて XAML から converter 無しで使う。</summary>
    public sealed class RecordsBar
    {
        public string Label { get; set; }
        public string CountText { get; set; }
        public string PercentText { get; set; }
        public double BarWidth { get; set; }
        /// <summary>強調表示するか (ピーク・最頻バケットなど)。</summary>
        public bool Highlight { get; set; }
    }

    /// <summary>「見出し: 値」1 行。ゲーム詳細のメタ情報欄で使う。</summary>
    public sealed class RecordsMeta
    {
        public string Label { get; set; }
        public string Value { get; set; }
    }

    /// <summary>概況タイル 1 枚。</summary>
    public sealed class RecordsTile
    {
        public string Value { get; set; }
        public string Label { get; set; }
        public string Sub { get; set; }
    }

    /// <summary>
    /// ゲーム 1 行。**表示用の文字列と px 幅まで全部ここで確定させる**ので、XAML 側は
    /// バインドするだけでよい (AGENTS「UI は薄く、ロジックは外へ」)。
    /// </summary>
    public sealed class RecordsGameRow
    {
        public int Rank { get; set; }
        public string RankText { get { return Rank > 0 ? Rank.ToString(CultureInfo.InvariantCulture) : "—"; } }
        public string Title { get; set; }
        public string SubTitle { get; set; }
        /// <summary>「未プレイ」「不明なゲーム」「非表示」等の注意ラベル。null なら出さない。</summary>
        public string Badge { get; set; }
        public bool HasBadge { get { return !string.IsNullOrEmpty(Badge); } }

        /// <summary>並べ替え軸の値 (大きく出す)。</summary>
        public string MetricText { get; set; }
        /// <summary>並べ替え軸の補足 (件数・母数など小さく出す)。</summary>
        public string MetricSubText { get; set; }
        /// <summary>件数が少なく額面通り読めない (平均★の参考値扱い)。</summary>
        public bool IsReference { get; set; }
        public double BarWidth { get; set; }

        // 常時表示する 4 指標 (並べ替え軸に関わらず横に並べる)
        public string PlaysText { get; set; }
        public string RatingText { get; set; }
        public string DurationText { get; set; }
        public string ResponseRateText { get; set; }

        // --- 展開したときの詳細 ---
        public List<RecordsBar> RatingBars { get; set; } = new List<RecordsBar>();
        public List<RecordsBar> DurationBars { get; set; } = new List<RecordsBar>();
        public List<RecordsBar> HourBars { get; set; } = new List<RecordsBar>();
        public List<RecordsMeta> Meta { get; set; } = new List<RecordsMeta>();
        public List<PlayStatsComment> Comments { get; set; } = new List<PlayStatsComment>();
        public bool HasComments { get { return Comments.Count > 0; } }
        public string CommentsHeader { get; set; }
        public bool HasSurveys { get; set; }
        public bool HasPlays { get; set; }
        /// <summary>展開しても中身が無いゲーム (1 回も遊ばれていない) の説明。</summary>
        public string EmptyNote { get; set; }
        public bool HasEmptyNote { get { return !string.IsNullOrEmpty(EmptyNote); } }
    }

    /// <summary>切り口別 (ジャンル / 難易度 / 期生 / 制作者) の 1 行。</summary>
    public sealed class RecordsGroupRow
    {
        public string Key { get; set; }
        public string PlaysText { get; set; }
        public string GamesText { get; set; }
        public string RatingText { get; set; }
        public string TopGameText { get; set; }
        public double BarWidth { get; set; }
    }

    /// <summary>画面 1 枚ぶんの表示データ。</summary>
    public sealed class RecordsViewModel
    {
        public List<RecordsTile> Tiles { get; set; } = new List<RecordsTile>();
        public List<RecordsGameRow> Games { get; set; } = new List<RecordsGameRow>();
        public List<PlayStatsComment> Comments { get; set; } = new List<PlayStatsComment>();
        public List<RecordsGroupRow> Groups { get; set; } = new List<RecordsGroupRow>();
        public List<PlayStatsAward> Awards { get; set; } = new List<PlayStatsAward>();
        public List<RecordsMeta> Highlights { get; set; } = new List<RecordsMeta>();
        public bool HasAwards { get { return Awards.Count > 0; } }
        public bool HasHighlights { get { return Highlights.Count > 0; } }

        public string RankingCaption { get; set; }
        public string CommentsCaption { get; set; }
        public string OverallCaption { get; set; }
        public List<RecordsBar> OverallRatingBars { get; set; } = new List<RecordsBar>();
        public List<string> Notes { get; set; } = new List<string>();
        public bool IsEmpty { get; set; }
        public string EmptyMessage { get; set; }
    }

    /// <summary>
    /// (#464) <see cref="PlayStatsSnapshot"/> を画面表示用に組み替える presenter。数値の意味づけ
    /// (何位か・参考値か・棒の長さ) はここで確定させ、`RecordsPage` はイベント配線と表示に徹する。
    /// </summary>
    public static class RecordsPresenter
    {
        private const double RankBarMax = 300;
        private const double DetailBarMax = 220;
        private const double HourBarMax = 130;
        private const double GroupBarMax = 260;

        public static RecordsViewModel Build(PlayStatsSnapshot snap, RecordsMetric metric, RecordsGroupAxis axis)
        {
            var vm = new RecordsViewModel();
            if (snap == null) return vm;

            vm.Tiles = BuildTiles(snap);
            vm.Games = BuildGameRows(snap, metric);
            vm.Comments = snap.Comments;
            vm.Groups = BuildGroupRows(snap, axis);
            vm.Awards = snap.Awards;
            vm.Highlights = BuildHighlights(snap);
            vm.RankingCaption = RankingCaption(metric);
            vm.CommentsCaption = string.Format("{0} 件（アンケート {1} 件のうち）", snap.Comments.Count, snap.TotalSurveys);
            vm.OverallCaption = BuildOverallCaption(snap.Overall);
            vm.OverallRatingBars = BuildRatingBars(snap.Overall.RatingCounts, snap.Overall.Count);
            vm.Notes = BuildNotes(snap);
            vm.IsEmpty = snap.TotalPlays == 0 && snap.TotalSurveys == 0;
            vm.EmptyMessage = vm.IsEmpty
                ? (snap.Range.IsAll
                    ? "まだ記録がありません。ランチャーでゲームが遊ばれると、ここに集計が出ます。"
                    : snap.Range.Label + " の記録はありません。")
                : null;
            return vm;
        }

        // ------------------------------------------------------------------

        private static List<RecordsTile> BuildTiles(PlayStatsSnapshot s)
        {
            var tiles = new List<RecordsTile>
            {
                new RecordsTile
                {
                    Value = s.TotalPlays.ToString(CultureInfo.InvariantCulture),
                    Label = "プレイ回数",
                    Sub = s.PlayedGameCount + " 種類が遊ばれた",
                },
                new RecordsTile
                {
                    Value = s.TotalSurveys.ToString(CultureInfo.InvariantCulture),
                    Label = "アンケート回答",
                    Sub = string.Format("ゲーム別 {0} / 全体 {1}", s.GameSurveys, s.Overall.Count),
                },
                new RecordsTile
                {
                    Value = s.AverageRating.HasValue ? "★" + s.AverageRating.Value.ToString("0.00") : "—",
                    Label = "平均評価（ゲーム別）",
                    Sub = s.GameSurveys + " 件の平均",
                },
                new RecordsTile
                {
                    Value = PlayStatsService.FormatHours(s.TotalPlaySeconds),
                    Label = "総プレイ時間",
                    Sub = "みんなが遊んだ合計",
                },
                new RecordsTile
                {
                    Value = s.PcCount.ToString(CultureInfo.InvariantCulture),
                    Label = "稼働したPC",
                    Sub = "記録を書いた台数",
                },
                new RecordsTile
                {
                    Value = s.PeakAt.HasValue ? s.PeakAt.Value.ToString("HH:mm") : "—",
                    Label = "ピーク時間帯",
                    Sub = s.PeakCount > 0 ? "15分で " + s.PeakCount + " プレイ" : "—",
                },
            };
            return tiles;
        }

        private static string RankingCaption(RecordsMetric metric)
        {
            switch (metric)
            {
                case RecordsMetric.Rating:
                    return "平均★の高い順。回答が " + PlayStatsTuning.MinSurveysForReliableAverage
                        + " 件未満のゲームは「参考」として下にまとめています（1〜2件の★5で 1 位になるのを防ぐため）。";
                case RecordsMetric.Duration:
                    return "1 回あたりのプレイ時間（中央値）が長い順。長く遊ばれた＝熱中度の目安です。";
                case RecordsMetric.ResponseRate:
                    return "遊んだあとアンケートまで答えてくれた割合の高い順。";
                default:
                    return "遊ばれた回数の多い順。登録済みで 1 回も遊ばれなかったゲームも下に並びます。";
            }
        }

        private static List<RecordsGameRow> BuildGameRows(PlayStatsSnapshot snap, RecordsMetric metric)
        {
            List<GameStats> ordered = Sort(snap.Games, metric);
            double max = MetricMax(ordered, metric);

            var rows = new List<RecordsGameRow>();
            int rank = 0;
            foreach (GameStats g in ordered)
            {
                rank++;
                double? value = MetricValue(g, metric);
                var row = new RecordsGameRow
                {
                    Rank = rank,
                    Title = g.Title,
                    SubTitle = BuildSubTitle(g),
                    Badge = BuildBadge(g),
                    IsReference = metric == RecordsMetric.Rating && !g.RatingIsReliable && g.SurveyCount > 0,
                    BarWidth = max > 0 && value.HasValue ? Math.Max(2, value.Value / max * RankBarMax) : 0,
                    PlaysText = g.PlayCount + " 回",
                    RatingText = g.AverageRating.HasValue ? "★" + g.AverageRating.Value.ToString("0.00") : "—",
                    DurationText = PlayStatsService.FormatDuration(g.MedianDurationSeconds),
                    ResponseRateText = g.ResponseRate.HasValue ? (g.ResponseRate.Value * 100).ToString("0") + "%" : "—",
                    HasPlays = g.PlayCount > 0,
                    HasSurveys = g.SurveyCount > 0,
                    Comments = g.Comments,
                    CommentsHeader = "このゲームへのコメント（" + g.Comments.Count + " 件）",
                };
                SetMetricText(row, g, metric);

                row.RatingBars = BuildRatingBars(g.RatingCounts, g.SurveyCount);
                row.DurationBars = BuildDurationBars(g);
                row.HourBars = BuildHourBars(g);
                row.Meta = BuildMeta(g);
                if (g.PlayCount == 0)
                {
                    row.EmptyNote = g.KnownGame
                        ? "このゲームは登録されていますが、この期間のプレイ記録はありません。"
                        : "記録にだけ現れる番号です（登録が削除された可能性があります）。";
                }
                rows.Add(row);
            }
            return rows;
        }

        private static void SetMetricText(RecordsGameRow row, GameStats g, RecordsMetric metric)
        {
            switch (metric)
            {
                case RecordsMetric.Rating:
                    row.MetricText = g.AverageRating.HasValue ? "★" + g.AverageRating.Value.ToString("0.00") : "—";
                    row.MetricSubText = g.SurveyCount > 0 ? g.SurveyCount + " 件の回答" : "回答なし";
                    break;
                case RecordsMetric.Duration:
                    row.MetricText = PlayStatsService.FormatDuration(g.MedianDurationSeconds);
                    row.MetricSubText = g.SaneDurationCount > 0 ? g.SaneDurationCount + " 回の中央値" : "記録なし";
                    break;
                case RecordsMetric.ResponseRate:
                    row.MetricText = g.ResponseRate.HasValue ? (g.ResponseRate.Value * 100).ToString("0") + "%" : "—";
                    row.MetricSubText = g.PlayCount > 0 ? g.SurveyCount + " / " + g.PlayCount : "プレイなし";
                    break;
                default:
                    row.MetricText = g.PlayCount.ToString(CultureInfo.InvariantCulture) + " 回";
                    row.MetricSubText = g.PcCount > 0 ? g.PcCount + " 台で" : "—";
                    break;
            }
        }

        private static List<GameStats> Sort(List<GameStats> games, RecordsMetric metric)
        {
            switch (metric)
            {
                case RecordsMetric.Rating:
                    // 回答が閾値未満のものは「参考」として必ず下へ (少数回答の★5 が 1 位を取るのを防ぐ)。
                    return games
                        .OrderByDescending(g => g.SurveyCount > 0)
                        .ThenByDescending(g => g.RatingIsReliable)
                        .ThenByDescending(g => g.AverageRating ?? 0)
                        .ThenByDescending(g => g.SurveyCount)
                        .ThenBy(g => g.Title, StringComparer.CurrentCulture)
                        .ToList();
                case RecordsMetric.Duration:
                    return games
                        .OrderByDescending(g => g.MedianDurationSeconds.HasValue)
                        .ThenByDescending(g => g.MedianDurationSeconds ?? 0)
                        .ThenBy(g => g.Title, StringComparer.CurrentCulture)
                        .ToList();
                case RecordsMetric.ResponseRate:
                    return games
                        .OrderByDescending(g => g.ResponseRate.HasValue)
                        .ThenByDescending(g => g.ResponseRate ?? 0)
                        .ThenByDescending(g => g.PlayCount)
                        .ThenBy(g => g.Title, StringComparer.CurrentCulture)
                        .ToList();
                default:
                    return games
                        .OrderByDescending(g => g.PlayCount)
                        .ThenBy(g => g.Title, StringComparer.CurrentCulture)
                        .ToList();
            }
        }

        private static double? MetricValue(GameStats g, RecordsMetric metric)
        {
            switch (metric)
            {
                case RecordsMetric.Rating: return g.AverageRating;
                case RecordsMetric.Duration: return g.MedianDurationSeconds;
                case RecordsMetric.ResponseRate: return g.ResponseRate;
                default: return g.PlayCount;
            }
        }

        private static double MetricMax(List<GameStats> games, RecordsMetric metric)
        {
            double max = 0;
            foreach (GameStats g in games)
            {
                double? v = MetricValue(g, metric);
                if (v.HasValue && v.Value > max) max = v.Value;
            }
            return max;
        }

        private static string BuildSubTitle(GameStats g)
        {
            var parts = new List<string>();
            if (g.Genre != null && g.Genre.Count > 0) parts.Add(string.Join(" / ", g.Genre));
            if (!string.IsNullOrWhiteSpace(g.DevelopersDisplay)) parts.Add(g.DevelopersDisplay);
            return parts.Count > 0 ? string.Join("　", parts) : null;
        }

        private static string BuildBadge(GameStats g)
        {
            if (!g.KnownGame) return "登録なし";
            if (g.PlayCount == 0) return "未プレイ";
            if (!g.IsVisible) return "非表示";
            return null;
        }

        private static List<RecordsBar> BuildRatingBars(int[] counts, int total)
        {
            var bars = new List<RecordsBar>();
            if (counts == null) return bars;
            int max = counts.Length > 0 ? counts.Max() : 0;
            // ★5 を上に置く (高評価から読む方が自然)。
            for (int star = 5; star >= 1; star--)
            {
                int c = counts[star - 1];
                bars.Add(new RecordsBar
                {
                    Label = new string('★', star),
                    CountText = c.ToString(CultureInfo.InvariantCulture) + " 件",
                    PercentText = total > 0 ? (100.0 * c / total).ToString("0.0") + "%" : "—",
                    BarWidth = max > 0 ? (double)c / max * DetailBarMax : 0,
                    Highlight = max > 0 && c == max,
                });
            }
            return bars;
        }

        private static List<RecordsBar> BuildDurationBars(GameStats g)
        {
            var bars = new List<RecordsBar>();
            int[] counts = g.DurationBuckets;
            int max = counts.Length > 0 ? counts.Max() : 0;
            for (int i = 0; i < counts.Length; i++)
            {
                bars.Add(new RecordsBar
                {
                    Label = PlayStatsTuning.DurationBucketLabels[i],
                    CountText = counts[i] + " 回",
                    PercentText = g.SaneDurationCount > 0
                        ? (100.0 * counts[i] / g.SaneDurationCount).ToString("0") + "%" : "—",
                    BarWidth = max > 0 ? (double)counts[i] / max * DetailBarMax : 0,
                    Highlight = max > 0 && counts[i] == max,
                });
            }
            return bars;
        }

        private static List<RecordsBar> BuildHourBars(GameStats g)
        {
            var bars = new List<RecordsBar>();
            int[] h = g.HourCounts;
            int max = h.Length > 0 ? h.Max() : 0;
            if (max == 0) return bars;
            // 記録のある時間帯だけ出す (深夜の 0 行が 20 本並ぶのを避ける)。
            int first = Array.FindIndex(h, c => c > 0);
            int last = Array.FindLastIndex(h, c => c > 0);
            for (int i = first; i <= last; i++)
            {
                bars.Add(new RecordsBar
                {
                    Label = i.ToString("00") + "時",
                    CountText = h[i] + " 回",
                    PercentText = null,
                    BarWidth = (double)h[i] / max * HourBarMax,
                    Highlight = h[i] == max,
                });
            }
            return bars;
        }

        private static List<RecordsMeta> BuildMeta(GameStats g)
        {
            var meta = new List<RecordsMeta>();
            meta.Add(new RecordsMeta { Label = "ゲーム番号", Value = "no." + g.GameNo });
            if (g.KnownGame)
            {
                meta.Add(new RecordsMeta { Label = "難易度", Value = PlayStatsService.DifficultyLabel(g.Difficulty) });

                // 申告した想定プレイ時間と実測のズレ。制作側が一番知りたい情報なので言葉にして出す。
                string band = PlayStatsService.PlayTimeBandLabel(g.PlayTimeBand);
                string gap = PlayStatsService.PlayTimeGapLabel(g.PlayTimeBand, g.MedianDurationSeconds);
                meta.Add(new RecordsMeta
                {
                    Label = "想定プレイ時間",
                    Value = gap != null ? band + "（実測は " + gap + "）" : band,
                });

                if (g.MinPlayers.HasValue || g.MaxPlayers.HasValue)
                {
                    string players = g.MinPlayers.HasValue && g.MaxPlayers.HasValue && g.MinPlayers != g.MaxPlayers
                        ? g.MinPlayers + "〜" + g.MaxPlayers + " 人"
                        : (g.MaxPlayers ?? g.MinPlayers) + " 人";
                    meta.Add(new RecordsMeta { Label = "対応人数", Value = players });
                }
                if (!string.IsNullOrWhiteSpace(g.DevelopersDisplay))
                    meta.Add(new RecordsMeta { Label = "制作者", Value = g.DevelopersDisplay });
            }

            if (g.PlayCount > 0)
            {
                meta.Add(new RecordsMeta { Label = "平均プレイ時間", Value = PlayStatsService.FormatDuration(g.AverageDurationSeconds) });
                meta.Add(new RecordsMeta { Label = "合計プレイ時間", Value = PlayStatsService.FormatHours(g.TotalDurationSeconds) });
                meta.Add(new RecordsMeta { Label = "遊ばれたPC", Value = g.PcCount + " 台" });
                if (g.FirstPlayAt.HasValue)
                    meta.Add(new RecordsMeta { Label = "はじめて遊ばれた", Value = g.FirstPlayAt.Value.ToString("M/d HH:mm") });
                if (g.LastPlayAt.HasValue)
                    meta.Add(new RecordsMeta { Label = "最後に遊ばれた", Value = g.LastPlayAt.Value.ToString("M/d HH:mm") });
            }

            if (g.SurveyCount > 0 && g.AverageRatingWithComment.HasValue)
            {
                // (#463) 「意思のある回答」だけの平均。全体平均との差が★3 バイアスの手がかりになる。
                meta.Add(new RecordsMeta
                {
                    Label = "コメント付き回答の平均★",
                    Value = "★" + g.AverageRatingWithComment.Value.ToString("0.00") + "（" + g.CommentCount + " 件）",
                });
            }
            return meta;
        }

        private static string BuildOverallCaption(OverallSurveyStats o)
        {
            if (o.Count == 0) return "この期間の全体アンケートはありません。";
            return string.Format("{0} 件・平均 ★{1}　（ランチャーを終了するときに聞く「TONE 全体はどうだった？」の回答。ゲーム別とは別集計です）",
                o.Count, o.AverageRating.HasValue ? o.AverageRating.Value.ToString("0.00") : "—");
        }

        /// <summary>
        /// 表彰の合間に読み上げられる「この期間の記録」。ランキングに載らないが場が沸く数字を拾う。
        /// 値が取れなかった行は**足さない** (「—」の行が並ぶより短い方が読み上げやすい)。
        /// </summary>
        private static List<RecordsMeta> BuildHighlights(PlayStatsSnapshot snap)
        {
            var rows = new List<RecordsMeta>();
            if (snap.TotalPlays == 0) return rows;

            var names = new Dictionary<long, string>();
            foreach (GameStats g in snap.Games) names[g.GameNo] = g.Title;

            Func<long, string> name = no =>
            {
                string t;
                return names.TryGetValue(no, out t) ? t : "no." + no;
            };

            if (snap.FirstPlayAt.HasValue)
            {
                rows.Add(new RecordsMeta
                {
                    Label = "最初のプレイ",
                    Value = snap.FirstPlayAt.Value.ToString("M/d HH:mm") + "　" + name(snap.FirstPlayGameNo),
                });
            }
            if (snap.LastPlayAt.HasValue)
            {
                rows.Add(new RecordsMeta
                {
                    Label = "最後のプレイ",
                    Value = snap.LastPlayAt.Value.ToString("M/d HH:mm") + "　" + name(snap.LastPlayGameNo),
                });
            }
            if (snap.PeakAt.HasValue)
            {
                rows.Add(new RecordsMeta
                {
                    Label = "いちばん忙しかった15分",
                    Value = snap.PeakAt.Value.ToString("M/d HH:mm") + " から　" + snap.PeakCount + " プレイ",
                });
            }
            if (!string.IsNullOrEmpty(snap.BusiestPc))
            {
                rows.Add(new RecordsMeta
                {
                    Label = "いちばん働いたPC",
                    Value = snap.BusiestPc + "　" + snap.BusiestPcPlays + " プレイ",
                });
            }
            rows.Add(new RecordsMeta
            {
                Label = "遊ばれたゲーム",
                // 登録本数はゲーム一覧を読めたときだけ添える (読めないと「登録 0 本」という嘘になる)。
                Value = snap.PlayedGameCount + " 種類"
                    + (snap.RegisteredGameCount > 0 ? "　（登録 " + snap.RegisteredGameCount + " 本）" : ""),
            });
            rows.Add(new RecordsMeta
            {
                Label = "総プレイ時間",
                Value = PlayStatsService.FormatHours(snap.TotalPlaySeconds),
            });
            return rows;
        }

        private static List<RecordsGroupRow> BuildGroupRows(PlayStatsSnapshot snap, RecordsGroupAxis axis)
        {
            List<PlayStatsGroup> src;
            switch (axis)
            {
                case RecordsGroupAxis.Difficulty: src = snap.ByDifficulty; break;
                case RecordsGroupAxis.Grade: src = snap.ByGrade; break;
                case RecordsGroupAxis.Developer: src = snap.ByDeveloper; break;
                default: src = snap.ByGenre; break;
            }
            int max = src.Count > 0 ? src.Max(g => g.PlayCount) : 0;
            return src.Select(g => new RecordsGroupRow
            {
                Key = g.Key,
                PlaysText = g.PlayCount + " 回",
                GamesText = g.GameCount + " 本",
                RatingText = g.AverageRating.HasValue
                    ? "★" + g.AverageRating.Value.ToString("0.00") + "（" + g.SurveyCount + " 件）"
                    : "回答なし",
                TopGameText = g.TopGameTitle,
                BarWidth = max > 0 ? Math.Max(2, (double)g.PlayCount / max * GroupBarMax) : 0,
            }).ToList();
        }

        private static List<string> BuildNotes(PlayStatsSnapshot snap)
        {
            var notes = new List<string>();
            notes.Add("この画面は読むだけです。ここでの操作でゲームの登録や記録が変わることはありません。");
            notes.Add("「プレイ回数」はゲームが 1 回起動して終わるごとに 1 件です。サービスモードの試遊テストは記録されません。");
            notes.Add("アンケートは「ゲームを終えたとき」と「ランチャーを終了するとき（全体）」の 2 種類があり、"
                + "ゲーム別ランキングにはゲームを終えたときの回答だけを使っています。");
            // (#463) ★3 に初期カーソルが当たる導線は**データに関係なく常にある**ので、分析ではなく
            // UI の事実として淡々と 1 行置く。逆転の有無を判定して警告を出す作りは、結果を見せる場で
            // 前置きが主役になってしまうため撤去した (判定ロジックごと削除済み)。
            notes.Add("アンケートは★を選ばないと送信できませんが、開いた直後は★3 にカーソルが当たっています。"
                + "そのまま決定を押すと★3 で送られるため、★3 はやや多めに出ている可能性があります。");
            notes.Add("ストアの「人気」並び順（直近の回数で決まる運用値）とは別の集計です。数字が違っていて問題ありません。");
            if (snap.OutlierDurationCount > 0)
            {
                notes.Add(string.Format(
                    "プレイ時間が異常だった {0} 件は時間の統計から除きました（ゲームを閉じずに放置された記録など）。"
                    + "プレイ回数には含めています。", snap.OutlierDurationCount));
            }
            return notes;
        }
    }

    /// <summary>切り口別の軸。</summary>
    public enum RecordsGroupAxis
    {
        Genre,
        Difficulty,
        Grade,
        Developer,
    }
}
