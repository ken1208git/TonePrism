using System;
using System.Collections.Generic;
using System.Linq;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#464) <see cref="PlayStatsLoader"/> が読んだ生データを集計する **純粋計算層**。I/O も DB も触らない
    /// ので、期間タブの切り替えは UI スレッドで即座に回してよい (走査は 1 回きり)。
    ///
    /// **ストアの人気順 (#36) とは別集計**。あちらは「直近 N 日の単純回数」を Launcher の並び順に使う
    /// 運用値で、こちらは会期全体を振り返るための読み物。母数も期間も既定値も違うので、同じ数字が
    /// 出ることを期待しないこと。共有するのは <see cref="PlayStatsLoader"/> (読み方) までとする。
    /// </summary>
    public static class PlayStatsService
    {
        /// <summary>
        /// 指定日 (null = 全期間) で集計する。
        /// </summary>
        public static PlayStatsSnapshot Compute(PlayStatsData data, DateTime? day)
        {
            var snap = new PlayStatsSnapshot { Day = day };
            if (data == null) return snap;

            List<PlayRecordEntry> plays = day.HasValue
                ? data.Plays.Where(p => p.CreatedAtLocal.Date == day.Value.Date).ToList()
                : data.Plays;
            List<SurveyEntry> surveys = day.HasValue
                ? data.Surveys.Where(s => s.CreatedAtLocal.Date == day.Value.Date).ToList()
                : data.Surveys;

            // game_end (ゲーム個別) と launcher_end (全体アンケート) は母集団も問いも違うので必ず分ける。
            // 混ぜると「no.0」という存在しないゲームが評価件数トップに現れる。
            List<SurveyEntry> gameSurveys = surveys.Where(s => !s.IsOverall && s.GameNo > 0).ToList();
            List<SurveyEntry> overallSurveys = surveys.Where(s => s.IsOverall).ToList();

            Dictionary<long, GameInfo> byNo = BuildGameIndex(data.Games);

            snap.RegisteredGameCount = data.Games != null ? data.Games.Count : 0;
            snap.TotalPlays = plays.Count;
            snap.TotalSurveys = surveys.Count;
            snap.GameSurveys = gameSurveys.Count;
            snap.AverageRating = Average(gameSurveys.Select(s => (double)s.Rating));
            snap.OutlierDurationCount = plays.Count(p => !p.DurationIsSane);
            snap.TotalPlaySeconds = plays.Where(p => p.DurationIsSane).Sum(p => p.DurationSeconds);
            snap.PcCount = plays.Select(p => p.SourcePc)
                .Concat(surveys.Select(s => s.SourcePc))
                .Where(pc => !string.IsNullOrEmpty(pc))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            snap.PlayedGameCount = plays.Select(p => p.GameNo).Where(n => n > 0).Distinct().Count();

            snap.Games = BuildGameStats(plays, gameSurveys, byNo, data.Games);
            snap.Overall = BuildOverallStats(overallSurveys);
            snap.Comments = CollectComments(snap.Games, snap.Overall);
            snap.Timelines = BuildTimelines(plays);

            PlayStatsTimeline peak = snap.Timelines.OrderByDescending(t => t.PeakCount).FirstOrDefault();
            if (peak != null && peak.PeakCount > 0)
            {
                snap.PeakAt = peak.PeakAt;
                snap.PeakCount = peak.PeakCount;
            }

            snap.ByGenre = GroupBy(snap.Games, g => g.Genre != null && g.Genre.Count > 0
                ? g.Genre.Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                : new List<string> { "(ジャンル未設定)" });
            snap.ByDifficulty = GroupBy(snap.Games, g => new List<string> { DifficultyLabel(g.Difficulty) });
            snap.ByGrade = GroupBy(snap.Games, g => DistinctOrUnset(
                g.Developers.Select(d => GradeLabel(d)), "(期生未設定)"));
            snap.ByDeveloper = GroupBy(snap.Games, g => DistinctOrUnset(
                g.Developers.Select(DeveloperLabel), "(制作者未登録)"));

            return snap;
        }

        // ------------------------------------------------------------------
        // ゲーム別
        // ------------------------------------------------------------------

        /// <summary>
        /// `game_no` → 登録ゲーム。**同じ番号が 2 件ある DB は想定しない** (v24 で UNIQUE INDEX 済) が、
        /// 万一あっても落ちないよう先勝ちにする。
        /// </summary>
        private static Dictionary<long, GameInfo> BuildGameIndex(List<GameInfo> games)
        {
            var map = new Dictionary<long, GameInfo>();
            if (games == null) return map;
            foreach (GameInfo g in games)
            {
                if (!g.GameNo.HasValue || g.GameNo.Value <= 0) continue;
                if (!map.ContainsKey(g.GameNo.Value)) map[g.GameNo.Value] = g;
            }
            return map;
        }

        private static List<GameStats> BuildGameStats(
            List<PlayRecordEntry> plays, List<SurveyEntry> gameSurveys,
            Dictionary<long, GameInfo> byNo, List<GameInfo> allGames)
        {
            // 母集合は「登録済みの全ゲーム ∪ 記録に現れた番号」。
            // **登録済みで 1 回も遊ばれなかったゲームを 0 件の行として残すのが要点** — 記録側だけを
            // 見て集計すると「遊ばれなかった」が消えてしまい、ランキングの下位が事実と食い違う。
            // これは DB を持つ Manager でしか出せない情報で、ログ由来のレポートには原理的に作れない。
            var numbers = new HashSet<long>(byNo.Keys);
            foreach (PlayRecordEntry p in plays) if (p.GameNo > 0) numbers.Add(p.GameNo);
            foreach (SurveyEntry s in gameSurveys) if (s.GameNo > 0) numbers.Add(s.GameNo);

            ILookup<long, PlayRecordEntry> playsByNo = plays.ToLookup(p => p.GameNo);
            ILookup<long, SurveyEntry> surveysByNo = gameSurveys.ToLookup(s => s.GameNo);

            var result = new List<GameStats>();
            foreach (long no in numbers)
            {
                GameInfo info;
                bool known = byNo.TryGetValue(no, out info);
                List<PlayRecordEntry> ps = playsByNo[no].ToList();
                List<SurveyEntry> ss = surveysByNo[no].ToList();

                var st = new GameStats
                {
                    GameNo = no,
                    KnownGame = known,
                    GameId = known ? info.GameId : null,
                    // 記録にしか居ない番号は「削除されたゲーム」等。名前が出せないことを画面で分かるようにする。
                    Title = known ? info.Title : string.Format("(不明なゲーム no.{0})", no),
                    PlayCount = ps.Count,
                };

                if (known)
                {
                    st.Genre = info.Genre ?? new List<string>();
                    st.Difficulty = info.Difficulty;
                    st.PlayTimeBand = info.PlayTime;
                    st.MinPlayers = info.MinPlayers;
                    st.MaxPlayers = info.MaxPlayers;
                    st.IsVisible = info.IsVisible;
                    st.Developers = info.Developers ?? new List<DeveloperInfo>();
                    st.DevelopersDisplay = info.DevelopersDisplay;
                }

                FillPlayStats(st, ps);
                FillSurveyStats(st, ss);
                result.Add(st);
            }

            // 既定は遊ばれた回数の降順。同数はゲーム名で安定化 (再集計のたびに並びが揺れないように)。
            return result
                .OrderByDescending(g => g.PlayCount)
                .ThenBy(g => g.Title, StringComparer.CurrentCulture)
                .ToList();
        }

        private static void FillPlayStats(GameStats st, List<PlayRecordEntry> ps)
        {
            if (ps.Count == 0) return;

            st.FirstPlayAt = ps.Min(p => p.CreatedAtLocal);
            st.LastPlayAt = ps.Max(p => p.CreatedAtLocal);
            st.PcCount = ps.Select(p => p.SourcePc)
                .Where(pc => !string.IsNullOrEmpty(pc))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();

            foreach (PlayRecordEntry p in ps)
            {
                int hour = p.CreatedAtLocal.Hour;
                if (hour >= 0 && hour < 24) st.HourCounts[hour]++;
            }

            // 時計ズレ・閉じ忘れを外してから時間統計を出す (件数には影響させない)。
            List<long> sane = ps.Where(p => p.DurationIsSane).Select(p => p.DurationSeconds).OrderBy(d => d).ToList();
            st.SaneDurationCount = sane.Count;
            if (sane.Count == 0) return;

            st.TotalDurationSeconds = sane.Sum();
            st.AverageDurationSeconds = (int)Math.Round(sane.Average());
            st.MedianDurationSeconds = (int)Median(sane);

            foreach (long d in sane) st.DurationBuckets[DurationBucketIndex(d)]++;
        }

        private static void FillSurveyStats(GameStats st, List<SurveyEntry> ss)
        {
            st.SurveyCount = ss.Count;
            if (ss.Count == 0) return;

            st.AverageRating = ss.Average(s => (double)s.Rating);
            foreach (SurveyEntry s in ss) st.RatingCounts[s.Rating - 1]++;

            // (#463) ★3 が初期フォーカスなので「決定を 1 回押しただけ」でも★3 が入りうる。
            // コメントを書いた回答は明らかに意思があるので、その層だけの平均を比較材料として持つ。
            List<SurveyEntry> commented = ss.Where(s => s.HasComment).ToList();
            st.CommentCount = commented.Count;
            st.AverageRatingWithComment = Average(commented.Select(s => (double)s.Rating));

            st.Comments = commented
                .OrderByDescending(s => s.CreatedAtLocal)
                .Select(s => new PlayStatsComment
                {
                    At = s.CreatedAtLocal,
                    GameNo = st.GameNo,
                    GameTitle = st.Title,
                    Rating = s.Rating,
                    Text = s.Comment.Trim(),
                    IsOverall = false,
                    SourcePc = s.SourcePc,
                })
                .ToList();
        }

        private static OverallSurveyStats BuildOverallStats(List<SurveyEntry> overall)
        {
            var o = new OverallSurveyStats { Count = overall.Count };
            if (overall.Count == 0) return o;

            o.AverageRating = overall.Average(s => (double)s.Rating);
            foreach (SurveyEntry s in overall) o.RatingCounts[s.Rating - 1]++;

            List<SurveyEntry> commented = overall.Where(s => s.HasComment).ToList();
            o.CommentCount = commented.Count;
            o.AverageRatingWithComment = Average(commented.Select(s => (double)s.Rating));
            o.Comments = commented
                .OrderByDescending(s => s.CreatedAtLocal)
                .Select(s => new PlayStatsComment
                {
                    At = s.CreatedAtLocal,
                    GameNo = 0,
                    GameTitle = "TONE 全体",
                    Rating = s.Rating,
                    Text = s.Comment.Trim(),
                    IsOverall = true,
                    SourcePc = s.SourcePc,
                })
                .ToList();
            return o;
        }

        private static List<PlayStatsComment> CollectComments(List<GameStats> games, OverallSurveyStats overall)
        {
            var all = new List<PlayStatsComment>();
            foreach (GameStats g in games) all.AddRange(g.Comments);
            all.AddRange(overall.Comments);
            return all.OrderByDescending(c => c.At).ToList();
        }

        // ------------------------------------------------------------------
        // 時間帯
        // ------------------------------------------------------------------

        private static List<PlayStatsTimeline> BuildTimelines(List<PlayRecordEntry> plays)
        {
            int per = PlayStatsTuning.TimelineBucketMinutes;
            int count = 24 * 60 / per;

            return plays
                .Where(p => p.CreatedAtLocal != DateTime.MinValue)
                .GroupBy(p => p.CreatedAtLocal.Date)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var buckets = new int[count];
                    foreach (PlayRecordEntry p in g)
                    {
                        int idx = (p.CreatedAtLocal.Hour * 60 + p.CreatedAtLocal.Minute) / per;
                        if (idx >= 0 && idx < count) buckets[idx]++;
                    }
                    int peakIdx = 0;
                    for (int i = 1; i < count; i++) if (buckets[i] > buckets[peakIdx]) peakIdx = i;
                    return new PlayStatsTimeline
                    {
                        Date = g.Key,
                        Buckets = buckets,
                        Total = g.Count(),
                        PeakCount = buckets[peakIdx],
                        PeakAt = g.Key.AddMinutes(peakIdx * per),
                    };
                })
                .ToList();
        }

        // ------------------------------------------------------------------
        // 切り口別
        // ------------------------------------------------------------------

        /// <summary>
        /// ゲームを任意のキー集合で束ねる。1 ゲームが複数キーに属してよい (ジャンル・制作者)。
        /// **平均★はゲームの平均の平均ではなく回答 1 件単位で重み付けする** — 1 回答しかないゲームと
        /// 100 回答のゲームを対等に扱うと、少数回答のゲームがグループ平均を支配してしまう。
        /// </summary>
        private static List<PlayStatsGroup> GroupBy(List<GameStats> games, Func<GameStats, List<string>> keySelector)
        {
            var acc = new Dictionary<string, PlayStatsGroup>();
            var ratingSum = new Dictionary<string, double>();
            var topPlays = new Dictionary<string, int>();

            foreach (GameStats g in games)
            {
                foreach (string key in keySelector(g).Distinct())
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    PlayStatsGroup grp;
                    if (!acc.TryGetValue(key, out grp))
                    {
                        grp = new PlayStatsGroup { Key = key };
                        acc[key] = grp;
                        ratingSum[key] = 0;
                        topPlays[key] = -1;
                    }
                    grp.GameCount++;
                    grp.PlayCount += g.PlayCount;
                    grp.SurveyCount += g.SurveyCount;
                    if (g.AverageRating.HasValue) ratingSum[key] += g.AverageRating.Value * g.SurveyCount;
                    if (g.PlayCount > topPlays[key])
                    {
                        topPlays[key] = g.PlayCount;
                        grp.TopGameTitle = g.Title;
                    }
                }
            }

            foreach (KeyValuePair<string, PlayStatsGroup> kv in acc)
            {
                if (kv.Value.SurveyCount > 0)
                    kv.Value.AverageRating = ratingSum[kv.Key] / kv.Value.SurveyCount;
            }

            return acc.Values
                .OrderByDescending(g => g.PlayCount)
                .ThenBy(g => g.Key, StringComparer.CurrentCulture)
                .ToList();
        }

        private static List<string> DistinctOrUnset(IEnumerable<string> keys, string fallback)
        {
            List<string> list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
            return list.Count > 0 ? list : new List<string> { fallback };
        }

        // ------------------------------------------------------------------
        // ラベル / 小さな算術
        // ------------------------------------------------------------------

        /// <summary>難易度の表示名。値は AddGameForm / EditGameForm のコンボと同じ 1-3。</summary>
        public static string DifficultyLabel(int? difficulty)
        {
            switch (difficulty)
            {
                case 1: return "易しい";
                case 2: return "普通";
                case 3: return "難しい";
                default: return "(難易度未設定)";
            }
        }

        /// <summary>登録時に申告した想定プレイ時間の帯 (GameFormHelper のコンボと同じ 1-3)。</summary>
        public static string PlayTimeBandLabel(int? band)
        {
            switch (band)
            {
                case 1: return "〜5分";
                case 2: return "5〜15分";
                case 3: return "15分以上";
                default: return "未設定";
            }
        }

        /// <summary>
        /// 申告した想定プレイ時間と実測中央値のズレ。制作側が一番知りたい情報なので言葉で出す。
        /// 帯の境界は <see cref="PlayTimeBandLabel"/> と揃える (1: ≤5 分 / 2: 5〜15 分 / 3: ≥15 分)。
        /// </summary>
        public static string PlayTimeGapLabel(int? band, int? medianSeconds)
        {
            if (!band.HasValue || !medianSeconds.HasValue) return null;
            double min = medianSeconds.Value / 60.0;
            double low, high;
            switch (band.Value)
            {
                case 1: low = 0; high = 5; break;
                case 2: low = 5; high = 15; break;
                case 3: low = 15; high = double.MaxValue; break;
                default: return null;
            }
            if (min < low) return "想定より短い";
            if (min > high) return "想定より長い";
            return "ほぼ想定通り";
        }

        public static string GradeLabel(DeveloperInfo d)
        {
            return d == null ? null : d.GradeDisplay;
        }

        public static string DeveloperLabel(DeveloperInfo d)
        {
            if (d == null) return null;
            string name = ((d.LastName ?? string.Empty) + " " + (d.FirstName ?? string.Empty)).Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        /// <summary>プレイ時間 (秒) が属する分布バケットの index。</summary>
        public static int DurationBucketIndex(long seconds)
        {
            long[] bounds = PlayStatsTuning.DurationBucketBounds;
            for (int i = 0; i < bounds.Length; i++) if (seconds < bounds[i]) return i;
            return bounds.Length;
        }

        /// <summary>秒を「M分S秒」形式に。1 時間以上は「H時間M分」。</summary>
        public static string FormatDuration(long? seconds)
        {
            if (!seconds.HasValue) return "—";
            long s = seconds.Value;
            if (s < 0) return "—";
            if (s < 60) return s + "秒";
            if (s < 3600) return (s / 60) + "分" + (s % 60).ToString("00") + "秒";
            return (s / 3600) + "時間" + ((s % 3600) / 60) + "分";
        }

        /// <summary>合計時間のような大きな値を「H.H 時間」で。</summary>
        public static string FormatHours(long seconds)
        {
            return (seconds / 3600.0).ToString("0.0") + "時間";
        }

        private static double? Average(IEnumerable<double> values)
        {
            List<double> list = values.ToList();
            return list.Count == 0 ? (double?)null : list.Average();
        }

        private static double Median(List<long> sorted)
        {
            int n = sorted.Count;
            if (n == 0) return 0;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
