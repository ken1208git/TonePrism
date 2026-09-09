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
        /// 指定期間 (null = 全期間) で集計する。
        /// </summary>
        public static PlayStatsSnapshot Compute(PlayStatsData data, PlayStatsRange range)
        {
            PlayStatsRange span = range ?? PlayStatsRange.All;
            var snap = new PlayStatsSnapshot { Range = span };
            if (data == null) return snap;

            List<PlayRecordEntry> plays = span.IsAll
                ? data.Plays
                : data.Plays.Where(p => span.Contains(p.CreatedAtLocal)).ToList();
            List<SurveyEntry> surveys = span.IsAll
                ? data.Surveys
                : data.Surveys.Where(s => span.Contains(s.CreatedAtLocal)).ToList();

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
            ApplyRepeatPlays(snap.Games, plays);
            FillHighlights(snap, plays);
            snap.Overall = BuildOverallStats(overallSurveys);
            snap.Comments = CollectComments(snap.Games, snap.Overall);
            snap.Trend = BuildTrend(plays);
            FillPeak(snap, plays);

            snap.ByGenre = GroupBy(snap.Games, g => g.Genre != null && g.Genre.Count > 0
                ? g.Genre.Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                : new List<string> { "(ジャンル未設定)" });
            snap.ByDifficulty = GroupBy(snap.Games, g => new List<string> { DifficultyLabel(g.Difficulty) });
            snap.ByGrade = GroupBy(snap.Games, g => DistinctOrUnset(
                g.Developers.Select(d => GradeLabel(d)), "(期生未設定)"));
            snap.ByDeveloper = GroupBy(snap.Games, g => DistinctOrUnset(
                g.Developers.Select(DeveloperLabel), "(制作者未登録)"));

            // 賞は切り口別まで揃ってから (制作者賞が ByDeveloper を使う)。
            snap.Awards = BuildAwards(snap);

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

        /// <summary>
        /// 概況タイルの「ピーク時間帯」。**刻みは 15 分で固定**する — タイルの文言が
        /// 「15 分で N プレイ」と言い切っているので、グラフの粒度が変わってもここは動かさない。
        /// </summary>
        private static void FillPeak(PlayStatsSnapshot snap, List<PlayRecordEntry> plays)
        {
            int per = PlayStatsTuning.TimelineBucketMinutes;
            var buckets = new Dictionary<DateTime, int>();
            foreach (PlayRecordEntry p in plays)
            {
                if (p.CreatedAtLocal == DateTime.MinValue) continue;
                DateTime key = p.CreatedAtLocal.Date
                    .AddMinutes((p.CreatedAtLocal.Hour * 60 + p.CreatedAtLocal.Minute) / per * per);
                int n;
                buckets[key] = buckets.TryGetValue(key, out n) ? n + 1 : 1;
            }
            if (buckets.Count == 0) return;

            KeyValuePair<DateTime, int> best = buckets
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
            snap.PeakAt = best.Key;
            snap.PeakCount = best.Value;
        }

        /// <summary>
        /// 「いつ遊ばれたか」の時系列を 1 本だけ作る。**刻みは期間の長さで決める。**
        ///
        /// 日ごとに 1 枚ずつ描く作りだと、記録が増えるほどグラフの枚数が際限なく増えた。
        /// 刻みを期間に従わせれば本数は常に有界になる (下の表のとおり最大 96 本)。
        /// カレンダーが日表示と月表示を持つのと同じ理屈で、期間が広がれば粒度も粗くなる。
        ///
        /// | 期間 | 刻み | 本数の上限 |
        /// |---|---|---|
        /// | 1 日 | 15 分 | 96 |
        /// | 2〜3 日 | 1 時間 | 72 |
        /// | 4〜31 日 | 1 日 | 31 |
        /// | 32〜182 日 | 1 週 | 26 |
        /// | 183 日〜3 年 | 1 か月 | 36 |
        /// | 3 年超 | 1 年 | — |
        ///
        /// **空の区間も点として残す。** 連続した時間軸なので「その間は遊ばれていない」ことも情報で、
        /// 詰めてしまうと夜や休みの日が消えて時間が縮んで見える。
        /// </summary>
        private static PlayStatsTrend BuildTrend(List<PlayRecordEntry> plays)
        {
            var trend = new PlayStatsTrend { Unit = PlayStatsTrendUnit.Minute15 };
            List<PlayRecordEntry> valid = plays.Where(p => p.CreatedAtLocal != DateTime.MinValue).ToList();
            if (valid.Count == 0) return trend;

            DateTime from = valid.Min(p => p.CreatedAtLocal);
            DateTime to = valid.Max(p => p.CreatedAtLocal);
            int days = (int)(to.Date - from.Date).TotalDays + 1;

            trend.Unit = PickUnit(days);

            var counts = new Dictionary<DateTime, int>();
            foreach (PlayRecordEntry p in valid)
            {
                DateTime key = Truncate(p.CreatedAtLocal, trend.Unit);
                int n;
                counts[key] = counts.TryGetValue(key, out n) ? n + 1 : 1;
            }

            DateTime cursor = Truncate(from, trend.Unit);
            DateTime end = Truncate(to, trend.Unit);
            // 上限を切って暴走を防ぐ (刻みの選択が想定外でも 1 本のグラフに収める)。
            for (int guard = 0; cursor <= end && guard < 1000; guard++)
            {
                int n;
                counts.TryGetValue(cursor, out n);
                trend.Points.Add(new PlayStatsTrendPoint { Start = cursor, Count = n });
                cursor = Next(cursor, trend.Unit);
            }

            for (int i = 0; i < trend.Points.Count; i++)
            {
                if (trend.PeakIndex >= 0 && trend.Points[i].Count <= trend.PeakCount) continue;
                trend.PeakIndex = i;
                trend.PeakCount = trend.Points[i].Count;
            }
            return trend;
        }

        private static PlayStatsTrendUnit PickUnit(int days)
        {
            if (days <= 1) return PlayStatsTrendUnit.Minute15;
            if (days <= 3) return PlayStatsTrendUnit.Hour;
            if (days <= 31) return PlayStatsTrendUnit.Day;
            if (days <= 182) return PlayStatsTrendUnit.Week;
            if (days <= 1096) return PlayStatsTrendUnit.Month;
            return PlayStatsTrendUnit.Year;
        }

        /// <summary>その時刻が属する区間の開始。週は月曜始まり。</summary>
        private static DateTime Truncate(DateTime at, PlayStatsTrendUnit unit)
        {
            switch (unit)
            {
                case PlayStatsTrendUnit.Minute15:
                    int per = PlayStatsTuning.TimelineBucketMinutes;
                    return at.Date.AddMinutes((at.Hour * 60 + at.Minute) / per * per);
                case PlayStatsTrendUnit.Hour:
                    return at.Date.AddHours(at.Hour);
                case PlayStatsTrendUnit.Day:
                    return at.Date;
                case PlayStatsTrendUnit.Week:
                    int back = ((int)at.DayOfWeek + 6) % 7;   // 月曜を週の頭にする
                    return at.Date.AddDays(-back);
                case PlayStatsTrendUnit.Month:
                    return new DateTime(at.Year, at.Month, 1);
                default:
                    return new DateTime(at.Year, 1, 1);
            }
        }

        private static DateTime Next(DateTime start, PlayStatsTrendUnit unit)
        {
            switch (unit)
            {
                case PlayStatsTrendUnit.Minute15: return start.AddMinutes(PlayStatsTuning.TimelineBucketMinutes);
                case PlayStatsTrendUnit.Hour: return start.AddHours(1);
                case PlayStatsTrendUnit.Day: return start.AddDays(1);
                case PlayStatsTrendUnit.Week: return start.AddDays(7);
                case PlayStatsTrendUnit.Month: return start.AddMonths(1);
                default: return start.AddYears(1);
            }
        }

        // ------------------------------------------------------------------
        // 再プレイ / この期間の記録
        // ------------------------------------------------------------------

        /// <summary>
        /// 「終わってすぐ、同じ台で同じゲームがもう一度始まった」回数を数える。
        ///
        /// **PC ごとに時系列へ並べ直して隣同士を見る**。ゲーム単位で見ると別の台の記録が隣接して
        /// しまい、無関係なプレイを再プレイと誤認する。判定は前のプレイの**終了**から次の**開始**まで
        /// (開始同士の差ではない — 長いゲームほど再プレイ扱いされにくくなってしまう)。
        ///
        /// **これは「同じ人がもう一度遊んだ」の代理指標であって証明ではない。** 次の来場者が
        /// たまたま同じゲームを選んだ場合も混ざる。窓を 2 分と短く取っているのはその混入を抑えるため
        /// (<see cref="PlayStatsTuning.RepeatGapSeconds"/> のコメント参照)。
        /// </summary>
        private static void ApplyRepeatPlays(List<GameStats> games, List<PlayRecordEntry> plays)
        {
            var counts = new Dictionary<long, int>();

            foreach (IGrouping<string, PlayRecordEntry> perPc in plays
                .Where(p => !string.IsNullOrEmpty(p.SourcePc))
                .GroupBy(p => p.SourcePc, StringComparer.OrdinalIgnoreCase))
            {
                List<PlayRecordEntry> ordered = perPc.OrderBy(p => p.StartTime).ToList();
                for (int i = 1; i < ordered.Count; i++)
                {
                    PlayRecordEntry prev = ordered[i - 1];
                    PlayRecordEntry cur = ordered[i];
                    if (cur.GameNo != prev.GameNo) continue;
                    long gap = cur.StartTime - prev.EndTime;
                    if (gap < 0 || gap > PlayStatsTuning.RepeatGapSeconds) continue;
                    int n;
                    counts[cur.GameNo] = counts.TryGetValue(cur.GameNo, out n) ? n + 1 : 1;
                }
            }

            foreach (GameStats g in games)
            {
                int n;
                g.RepeatPlayCount = counts.TryGetValue(g.GameNo, out n) ? n : 0;
            }
        }

        /// <summary>表彰の合間に読み上げられる「この期間の記録」を拾う。</summary>
        private static void FillHighlights(PlayStatsSnapshot snap, List<PlayRecordEntry> plays)
        {
            if (plays.Count == 0) return;

            PlayRecordEntry first = plays.OrderBy(p => p.StartTime).First();
            PlayRecordEntry last = plays.OrderByDescending(p => p.EndTime).First();
            snap.FirstPlayAt = first.CreatedAtLocal;
            snap.FirstPlayGameNo = first.GameNo;
            snap.LastPlayAt = last.CreatedAtLocal;
            snap.LastPlayGameNo = last.GameNo;

            IGrouping<string, PlayRecordEntry> busiest = plays
                .Where(p => !string.IsNullOrEmpty(p.SourcePc))
                .GroupBy(p => p.SourcePc, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (busiest != null)
            {
                snap.BusiestPc = busiest.Key;
                snap.BusiestPcPlays = busiest.Count();
            }
        }

        // ------------------------------------------------------------------
        // 表彰
        // ------------------------------------------------------------------

        /// <summary>
        /// 表彰を自動選出する。
        ///
        /// **軸は互いに相関しにくいものを選んである** — 回数 / 満足度 / のべ時間 / 回答率 /
        /// ★5 率 / 隠れた名作 / 制作者。全部が同じゲームに集まると表彰として機能しないため。
        /// のべ時間は回数と順位相関 0.887 とよく似た並びになるが、1 位は入れ替わる
        /// (実データでは回数 1 位と合計時間 1 位が別のゲームだった) ので賞としては成立する。
        ///
        /// **率で競う賞にはプレイ回数の下限を置く**。置かないと 2 回遊ばれて 1 回答された
        /// ゲームが「回答率 50%」で 1 位を取り、賞が母数の小ささを表彰することになる。
        ///
        /// **入れていない軸が 3 つある。いずれも「作りの違い」を実力として表彰してしまうため。**
        ///
        /// - **最長 1 プレイ**: 実データの 1 位は 1 時間 50 分で上限 (2 時間) の内側なので異常値として
        ///   弾かれない。**それが本当に遊ばれた時間か閉じ忘れかは、記録からは区別できない** —
        ///   ランチャーはプロセスの起動と終了しか見ておらず、放置されたのか熱中したのかを知る手段が無い。
        ///   区別できない値を 1 位として表彰すると、たまたま閉じ忘れられた作品を讃えることになる。
        /// - **もう一回 (再プレイ回数)**: <see cref="GameStats.RepeatPlayCount"/> は残してあるが賞にはしない。
        ///   **1 回の短いゲームほど有利になる交絡がある** — 実データで中央プレイ時間と再プレイ率の相関は
        ///   -0.547、再プレイ率の上位 5 本は中央値がすべて 31 秒以下 (最上位は 12 秒)、中央値 2 分以下の
        ///   平均再プレイ率 0.30 に対し 5 分以上は 0.03 と 10 倍開く。さらに**ゲーム内でリトライできる
        ///   作品は再起動が起きないので原理的に受賞できない**。ランチャーはプロセスの起動と終了しか
        ///   観測できず、ゲーム内の遊び直しは見えないので、この交絡はデータ側では外せない。
        /// - **コメント数**: 母数 17 件で最多 4 件。賞として薄い。
        /// </summary>
        private static List<PlayStatsAward> BuildAwards(PlayStatsSnapshot snap)
        {
            var awards = new List<PlayStatsAward>();
            List<GameStats> games = snap.Games;
            if (games.Count == 0) return awards;

            int minPlays = PlayStatsTuning.MinPlaysForAward;
            int minSurveys = PlayStatsTuning.MinSurveysForReliableAverage;

            Add(awards, games, "最多プレイ賞", "遊ばれた回数がいちばん多い",
                g => g.PlayCount > 0, g => g.PlayCount,
                g => g.PlayCount + " 回", g => g.PcCount + " 台で遊ばれました",
                g => g.PcCount);

            Add(awards, games, "満足度賞", "アンケートの平均★がいちばん高い（回答 " + minSurveys + " 件以上）",
                g => g.SurveyCount >= minSurveys, g => g.AverageRating.Value,
                g => "★" + g.AverageRating.Value.ToString("0.00"), g => g.SurveyCount + " 件の回答",
                g => g.SurveyCount);

            // **1 回あたりの長さでは競わせない (旧「熱中賞」)。** 終了のしかたはゲームの作り次第で、
            // 終わったら即閉じる作品と、タイトル画面に戻って待つ作品では記録される長さが桁で変わる。
            // 「熱中度」という名前は遊びの濃さを主張するが、実際に測っているのは終了設計の差。
            // のべ時間なら「来場者の時間をどれだけ占めたか」という事実に留まる。
            // **賞名に「合計」を入れること** — 「いちばん長く遊ばれた賞」だと 1 プレイの長さと読める。
            // 母数の下限も自然に効く — 1 回だけのゲームは合計では上位に来られない。
            Add(awards, games, "合計プレイ時間賞", "みんなが遊んだ時間の合計がいちばん長い",
                g => g.TotalDurationSeconds > 0, g => g.TotalDurationSeconds,
                g => FormatSpan(g.TotalDurationSeconds),
                g => g.PlayCount + " 回 × 1 回あたり中央 " + FormatDuration(g.MedianDurationSeconds),
                g => g.PlayCount);

            Add(awards, games, "語りたくなる賞", "遊んだあとアンケートまで答えてもらえた割合がいちばん高い",
                g => g.PlayCount >= minPlays && g.ResponseRate.HasValue, g => g.ResponseRate.Value,
                g => (g.ResponseRate.Value * 100).ToString("0") + "%",
                g => g.PlayCount + " 回中 " + g.SurveyCount + " 件の回答",
                g => g.PlayCount);

            Add(awards, games, "満点賞", "★5 の割合がいちばん高い（回答 " + minSurveys + " 件以上）",
                g => g.SurveyCount >= minSurveys && g.FiveStarRate.HasValue, g => g.FiveStarRate.Value,
                g => (g.FiveStarRate.Value * 100).ToString("0") + "%",
                g => g.SurveyCount + " 件中 " + g.RatingCounts[4] + " 件が★5",
                g => g.SurveyCount);

            // 「隠れた名作」= 回数はプレイ回数の中央値未満だが評価が高い。中央値を境にするのは、
            // 固定回数だと会期の規模 (総プレイ数) が変わるたびに意味が変わってしまうため。
            double medianPlays = Median(games.Select(g => (long)g.PlayCount).OrderBy(v => v).ToList());
            Add(awards, games, "隠れた名作賞",
                "プレイ回数は控えめ（全体のまん中より下）でも評価が高い",
                g => g.PlayCount > 0 && g.PlayCount < medianPlays
                     && g.SurveyCount >= PlayStatsTuning.MinSurveysForHiddenGem,
                g => g.AverageRating.Value,
                g => "★" + g.AverageRating.Value.ToString("0.00"),
                g => g.PlayCount + " 回・" + g.SurveyCount + " 件の回答",
                g => g.SurveyCount);

            PlayStatsAward dev = BuildDeveloperAward(snap);
            if (dev != null) awards.Add(dev);

            return awards;
        }

        /// <summary>
        /// 1 つの賞を作って追加する。受賞者が居ない (条件を満たすゲームが無い) 場合と、同点が多すぎて
        /// 賞として意味を成さない場合は**何も追加しない** — 「該当なし」の枠が並ぶより出さない方がよい。
        /// </summary>
        private static void Add(List<PlayStatsAward> awards, List<GameStats> games,
            string title, string criterion,
            Func<GameStats, bool> eligible, Func<GameStats, double> score,
            Func<GameStats, string> value, Func<GameStats, string> note,
            Func<GameStats, double> sample)
        {
            List<GameStats> pool = games.Where(eligible).ToList();
            if (pool.Count == 0) return;

            double best = pool.Max(score);
            List<GameStats> winners = pool.Where(g => score(g) >= best).ToList();

            // **同率は母数の多い方を採る。** 率で競う賞では同率が普通に起きる (実データでは★5 率 0.80 が
            // 4 本並んだ) が、10 件の回答で 80% と 5 件で 80% を対等に扱うと、賞が母数の小ささを
            // 表彰しかねない。ここで落とすと賞自体が消えてしまうので、切るのではなく順位を付ける。
            if (winners.Count > 1)
            {
                double bestSample = winners.Max(sample);
                winners = winners.Where(g => sample(g) >= bestSample).ToList();
            }

            // それでも並ぶなら本物の同点。多すぎると賞として意味を成さないので出さない。
            if (winners.Count > PlayStatsTuning.MaxAwardWinners) return;

            awards.Add(new PlayStatsAward
            {
                Title = title,
                Criterion = criterion,
                WinnerNames = string.Join("・", winners.Select(g => g.Title)),
                ValueText = value(winners[0]),
                // 同点のときは 1 本目の補足だけ出すと他の受賞者に嘘をつくので出さない。
                Note = winners.Count == 1 ? note(winners[0]) : null,
                WinnerGameNos = winners.Select(g => g.GameNo).ToList(),
                IsTie = winners.Count > 1,
            });
        }

        /// <summary>
        /// 制作者の賞。担当ゲームの合計プレイ回数で決める。**共作は両方に計上**されるので、
        /// 合計はのべ数であり全体のプレイ回数とは一致しない (切り口別と同じ扱い)。
        /// </summary>
        private static PlayStatsAward BuildDeveloperAward(PlayStatsSnapshot snap)
        {
            List<PlayStatsGroup> pool = snap.ByDeveloper
                .Where(g => g.PlayCount > 0 && !g.Key.StartsWith("(")).ToList();
            if (pool.Count == 0) return null;

            int best = pool.Max(g => g.PlayCount);
            List<PlayStatsGroup> winners = pool.Where(g => g.PlayCount == best).ToList();

            // **ここでは同点を切り捨てない (ゲームの賞と規則を変える)。** ゲーム側の
            // `MaxAwardWinners` は「4 本が同率 = 指標が差を付けられていない」という失敗の印なので
            // 賞ごと落としてよい。一方こちらの同点は**共作**で、共作者は同じゲーム群を共有する以上
            // 合計プレイ回数が必ず一致する = 構造的に起きる正常な結果。4 人で作ったゲームが最多
            // プレイだったときに賞が消えるのは、まさに表彰したい相手を落とすことになる。
            // よってチーム全員を連名で出す。

            return new PlayStatsAward
            {
                Title = "いちばん遊ばれた制作者",
                Criterion = "つくったゲームの合計プレイ回数がいちばん多い",
                WinnerNames = string.Join("・", winners.Select(g => g.Key)),
                ValueText = best + " 回",
                Note = winners.Count == 1
                    ? winners[0].GameCount + " 本・いちばん遊ばれたのは「" + winners[0].TopGameTitle + "」"
                    : null,
                IsTie = winners.Count > 1,
            };
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
                    // その区分でいちばん遊ばれた 1 本。**0 回のときは出さない** — 初期値との比較で
                    // 素通しすると、1 本も遊ばれなかった区分にも「最多」としてゲーム名が付いてしまう。
                    // 同数のときは先に現れた方が残る。games は回数降順→名前順で並んでいるので、
                    // 同数なら名前順で先のものになり、集計し直しても結果は変わらない。
                    if (g.PlayCount > 0 && g.PlayCount > topPlays[key])
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

        /// <summary>
        /// 合計時間を一覧に出す用。1 時間未満は「36 分」、それ以上は「6.6 時間」。
        /// 短いゲームが軒並み「0.1 時間」で並ぶと差が読めないため、桁で単位を変える。
        /// </summary>
        public static string FormatSpan(long seconds)
        {
            if (seconds <= 0) return "—";
            if (seconds < 3600) return Math.Max(1, seconds / 60) + "分";
            return FormatHours(seconds);
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
