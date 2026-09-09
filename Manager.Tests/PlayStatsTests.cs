using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using TonePrism.Manager.Shell.Records;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#464) 会期記録の集計 (`PlayStatsLoader` / `PlayStatsService` / `RecordsPresenter`) のテスト。
    ///
    /// **ここを固めておく意味**: 記録は文化祭当日にしか取れず、取り直しが効かない。集計が静かに間違って
    /// いても「そういう結果だった」と受け取られてしまい、誰も気づけない。特に
    /// (a) 全体アンケート (`launcher_end` / `game_no = 0`) がゲーム別に混ざらないこと、
    /// (b) 1 回も遊ばれなかったゲームが消えないこと、
    /// (c) 少数回答の★5 がランキング上位を取らないこと、
    /// の 3 つは、間違えると結論そのものが変わる。
    /// </summary>
    public class PlayStatsTests
    {
        private static readonly DateTime Day1 = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Local);
        private static readonly DateTime Day2 = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Local);

        private static long Unix(DateTime local)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeSeconds();
        }

        private static PlayRecordEntry Play(long gameNo, DateTime at, int durationSeconds, string pc = "PC-1")
        {
            long start = Unix(at);
            return new PlayRecordEntry
            {
                CreatedAt = start + durationSeconds,
                StartTime = start,
                EndTime = start + durationSeconds,
                GameNo = gameNo,
                SourcePc = pc,
                CreatedAtLocal = at.AddSeconds(durationSeconds),
            };
        }

        private static SurveyEntry Survey(long gameNo, DateTime at, int rating, string comment = null,
            string trigger = SurveyEntry.TriggerGameEnd, string pc = "PC-1")
        {
            return new SurveyEntry
            {
                CreatedAt = Unix(at),
                GameNo = gameNo,
                Rating = rating,
                Comment = comment,
                Trigger = trigger,
                SourcePc = pc,
                CreatedAtLocal = at,
            };
        }

        private static GameInfo Game(long no, string id, string title, params string[] genre)
        {
            return new GameInfo
            {
                GameId = id,
                GameNo = no,
                Title = title,
                Genre = genre.ToList(),
                IsVisible = true,
                Developers = new List<DeveloperInfo>(),
            };
        }

        // ==================================================================
        // 全体アンケートの分離
        // ==================================================================

        [Fact]
        public void Compute_OverallSurvey_IsNotCountedAsAGame()
        {
            // launcher_end は game_no = 0 で「どのゲームでもない」全体アンケート。ゲーム別に混ぜると
            // 存在しない no.0 が評価件数トップのゲームとして現れる (本番データで 95 件がこれに当たる)。
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "アルファ") },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 100) },
                Surveys = new List<SurveyEntry>
                {
                    Survey(1, Day2, 4),
                    Survey(0, Day2, 5, trigger: SurveyEntry.TriggerLauncherEnd),
                    Survey(0, Day2, 3, trigger: SurveyEntry.TriggerLauncherEnd),
                },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);

            Assert.Equal(3, snap.TotalSurveys);
            Assert.Equal(1, snap.GameSurveys);              // ゲーム別は 1 件だけ
            Assert.Equal(2, snap.Overall.Count);
            Assert.Equal(4.0, snap.AverageRating.Value, 3); // 全体アンケートの★は混ざらない
            Assert.Equal(4.0, snap.Overall.AverageRating.Value, 3);
            Assert.DoesNotContain(snap.Games, g => g.GameNo == 0);
        }

        // ==================================================================
        // 遊ばれなかったゲーム
        // ==================================================================

        [Fact]
        public void Compute_RegisteredButNeverPlayed_StillAppearsWithZero()
        {
            // 記録側だけを見て集計すると「遊ばれなかった」が可視化されない。DB を持つ Manager の利点。
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "遊ばれた"), Game(2, "b", "遊ばれなかった") },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60) },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);

            Assert.Equal(2, snap.Games.Count);
            GameStats zero = snap.Games.Single(g => g.GameNo == 2);
            Assert.Equal(0, zero.PlayCount);
            Assert.Null(zero.ResponseRate);          // 0 除算にしない
            Assert.Equal(1, snap.PlayedGameCount);   // 「遊ばれた種類」は 1
            Assert.Equal(2, snap.RegisteredGameCount);
        }

        [Fact]
        public void Compute_NumberOnlyInRecords_IsKeptAsUnknownGame()
        {
            // 登録が消えたゲームの記録も落とさない (回数は事実なので残す)。
            var data = new PlayStatsData
            {
                Games = new List<GameInfo>(),
                Plays = new List<PlayRecordEntry> { Play(99, Day2, 60) },
            };

            GameStats g = PlayStatsService.Compute(data, PlayStatsRange.All).Games.Single();
            Assert.False(g.KnownGame);
            Assert.Contains("99", g.Title);
            Assert.Equal(1, g.PlayCount);
        }

        // ==================================================================
        // プレイ時間
        // ==================================================================

        [Fact]
        public void Compute_MedianAndBuckets_UseSaneDurationsOnly()
        {
            // 閉じ忘れ (上限超過) と時計の巻き戻り (負) は時間統計から外すが、プレイ回数には残す。
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day2, 30),                                              // 1分未満
                    Play(1, Day2, 120),                                             // 1〜3分
                    Play(1, Day2, 400),                                             // 5〜10分
                    Play(1, Day2, (int)PlayStatsTuning.MaxSaneDurationSeconds + 1),  // 異常 (閉じ忘れ)
                    Play(1, Day2, -50),                                             // 異常 (時計巻き戻り)
                },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            GameStats g = snap.Games.Single();

            Assert.Equal(5, g.PlayCount);            // 回数は 5 のまま
            Assert.Equal(3, g.SaneDurationCount);    // 時間統計は 3 件
            Assert.Equal(120, g.MedianDurationSeconds);
            Assert.Equal(30 + 120 + 400, g.TotalDurationSeconds);
            Assert.Equal(2, snap.OutlierDurationCount);
            Assert.Equal(1, g.DurationBuckets[0]);   // 1分未満
            Assert.Equal(1, g.DurationBuckets[1]);   // 1〜3分
            Assert.Equal(1, g.DurationBuckets[3]);   // 5〜10分
        }

        [Fact]
        public void PlayTimeGapLabel_ComparesDeclaredBandAgainstMeasuredMedian()
        {
            // 申告 5〜15 分に対して実測 3 分なら「想定より短い」。制作者が一番見たい比較。
            Assert.Equal("想定より短い", PlayStatsService.PlayTimeGapLabel(2, 180));
            Assert.Equal("ほぼ想定通り", PlayStatsService.PlayTimeGapLabel(2, 600));
            Assert.Equal("想定より長い", PlayStatsService.PlayTimeGapLabel(1, 900));
            Assert.Null(PlayStatsService.PlayTimeGapLabel(null, 600));
            Assert.Null(PlayStatsService.PlayTimeGapLabel(2, null));
        }

        // ==================================================================
        // 期間フィルタ
        // ==================================================================

        [Fact]
        public void Compute_DayFilter_SelectsOnlyThatDay()
        {
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A") },
                Plays = new List<PlayRecordEntry> { Play(1, Day1, 60), Play(1, Day2, 60), Play(1, Day2, 60) },
                Surveys = new List<SurveyEntry> { Survey(1, Day1, 5), Survey(1, Day2, 3) },
            };

            Assert.Equal(3, PlayStatsService.Compute(data, PlayStatsRange.All).TotalPlays);
            Assert.Equal(1, PlayStatsService.Compute(data, PlayStatsRange.Day(Day1)).TotalPlays);
            Assert.Equal(2, PlayStatsService.Compute(data, PlayStatsRange.Day(Day2)).TotalPlays);
            Assert.Equal(5.0, PlayStatsService.Compute(data, PlayStatsRange.Day(Day1)).AverageRating.Value, 3);
        }

        [Fact]
        public void Compute_Range_IsInclusiveOnBothEndsAndToleratesReversedInput()
        {
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day1, 60),                       // 9/4
                    Play(1, Day2, 60),                       // 9/5
                    Play(1, Day2.AddDays(1), 60),            // 9/6
                },
            };

            // 両端を含む閉区間。
            Assert.Equal(2, PlayStatsService.Compute(
                data, PlayStatsRange.Between(Day1, Day2)).TotalPlays);

            // **逆順で渡されても入れ替えて扱う。** 画面で終了日を先に選ぶ操作は普通に起きるので、
            // そこで 0 件になるより期間として解釈する方がよい。
            Assert.Equal(2, PlayStatsService.Compute(
                data, PlayStatsRange.Between(Day2, Day1)).TotalPlays);

            // 片側だけの指定も通る。
            Assert.Equal(2, PlayStatsService.Compute(
                data, PlayStatsRange.Between(Day2, null)).TotalPlays);
            Assert.Equal(1, PlayStatsService.Compute(
                data, PlayStatsRange.Between(null, Day1)).TotalPlays);

            // 終了日の 23:59 の記録も落とさない (日付だけで判定する)。
            var late = new PlayStatsData
            {
                Games = data.Games,
                Plays = new List<PlayRecordEntry> { Play(1, Day2.Date.AddHours(23).AddMinutes(59), 30) },
            };
            Assert.Equal(1, PlayStatsService.Compute(late, PlayStatsRange.Day(Day2)).TotalPlays);
        }

        [Fact]
        public void PlayStatsRange_Label_ReadsAsAPeriod()
        {
            Assert.Equal("全期間", PlayStatsRange.All.Label);
            Assert.Equal("9月5日", PlayStatsRange.Day(Day2).Label);
            Assert.Equal("9月4日 〜 9月5日", PlayStatsRange.Between(Day1, Day2).Label);
            Assert.Equal("9月5日 以降", PlayStatsRange.Between(Day2, null).Label);
            Assert.Equal("9月4日 まで", PlayStatsRange.Between(null, Day1).Label);
        }

        // ==================================================================
        // ランキングの並び
        // ==================================================================

        [Fact]
        public void Presenter_RatingRanking_PushesLowSampleGamesBelowReliableOnes()
        {
            // 1 件の★5 が、20 件平均★4.5 のゲームより上に来てはいけない。
            var surveys = new List<SurveyEntry> { Survey(2, Day2, 5) };
            for (int i = 0; i < 20; i++) surveys.Add(Survey(1, Day2, i % 2 == 0 ? 5 : 4));

            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "たくさん回答"), Game(2, "b", "1件だけ") },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60), Play(2, Day2, 60) },
                Surveys = surveys,
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            RecordsViewModel vm = RecordsPresenter.Build(snap, RecordsMetric.Rating, RecordsGroupAxis.Genre);

            Assert.Equal("たくさん回答", vm.Games[0].Title);
            Assert.False(vm.Games[0].IsReference);
            Assert.Equal("1件だけ", vm.Games[1].Title);
            Assert.True(vm.Games[1].IsReference);   // 「参考」の印が付く
        }

        [Fact]
        public void Presenter_PlaysRanking_IsOrderedByPlayCount()
        {
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "少ない"), Game(2, "b", "多い") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day2, 60), Play(2, Day2, 60), Play(2, Day2, 60), Play(2, Day2, 60),
                },
            };

            RecordsViewModel vm = RecordsPresenter.Build(
                PlayStatsService.Compute(data, PlayStatsRange.All), RecordsMetric.Plays, RecordsGroupAxis.Genre);

            Assert.Equal("多い", vm.Games[0].Title);
            Assert.Equal(1, vm.Games[0].Rank);
            // 棒は px ではなく比率。窓が狭くても比が保たれる。
            Assert.True(vm.Games[0].BarStar.Value > vm.Games[1].BarStar.Value);
            Assert.Equal(1.0, vm.Games[0].BarStar.Value + vm.Games[0].RestStar.Value, 3);
        }

        // ==================================================================
        // 切り口別
        // ==================================================================

        [Fact]
        public void Compute_GroupByGenre_WeightsAverageByResponseCount()
        {
            // ゲーム平均の単純平均だと 1 回答のゲームが群平均を支配する。回答 1 件単位で重み付ける。
            var surveys = new List<SurveyEntry> { Survey(2, Day2, 1) };
            for (int i = 0; i < 9; i++) surveys.Add(Survey(1, Day2, 5));

            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A", "アクション"), Game(2, "b", "B", "アクション") },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60), Play(2, Day2, 60) },
                Surveys = surveys,
            };

            PlayStatsGroup g = PlayStatsService.Compute(data, PlayStatsRange.All).ByGenre.Single();
            Assert.Equal("アクション", g.Key);
            Assert.Equal(2, g.GameCount);
            Assert.Equal(10, g.SurveyCount);
            Assert.Equal((9 * 5 + 1) / 10.0, g.AverageRating.Value, 3);   // 4.6 (単純平均なら 3.0)
        }

        [Fact]
        public void Compute_GroupByDeveloperAndGrade_UsesDbDevelopers()
        {
            GameInfo a = Game(1, "a", "A");
            a.Developers = new List<DeveloperInfo>
            {
                new DeveloperInfo { LastName = "山田", FirstName = "太郎", Grade = "76" },
                new DeveloperInfo { LastName = "鈴木", FirstName = "花子", Grade = "77" },
            };
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { a },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60), Play(1, Day2, 60) },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);

            Assert.Equal(2, snap.ByDeveloper.Count);
            Assert.All(snap.ByDeveloper, g => Assert.Equal(2, g.PlayCount));   // 共作は両方に計上
            Assert.Contains(snap.ByDeveloper, g => g.Key == "山田 太郎");
            Assert.Equal(2, snap.ByGrade.Count);
            Assert.Contains(snap.ByGrade, g => g.Key == "76期生");
        }

        // ==================================================================
        // 概況 (ヒーロー数値 + メーター)
        // ==================================================================

        [Fact]
        public void Presenter_Meters_OnlyAppearWhenTheyHaveARealDenominator()
        {
            // 弧のメーターは「上限のうちどこまで」を意味する。分母が無いのに描くと、存在しない
            // 目標を発明して達成率に見せてしまう。分母が取れないメーターは枠ごと出さない。
            var noSurvey = new PlayStatsData
            {
                Games = new List<GameInfo>(),                       // 登録一覧が読めなかった状況
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60) },
            };

            RecordsViewModel vm = RecordsPresenter.Build(
                PlayStatsService.Compute(noSurvey, PlayStatsRange.All),
                RecordsMetric.Plays, RecordsGroupAxis.Genre);

            Assert.Null(vm.RatingMeter);      // アンケート 0 件 → 平均★の分子が無い
            Assert.Null(vm.CoverageMeter);    // 登録本数 0 → 「0 本中 1 種類」という嘘を出さない
            Assert.NotNull(vm.ResponseMeter); // プレイ回数は分母になる
            Assert.Equal(0, vm.ResponseMeter.Value);
            Assert.Equal(1, vm.ResponseMeter.Max);
        }

        [Fact]
        public void Presenter_Overview_UsesPlaysAsTheSingleHeroNumber()
        {
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A"), Game(2, "b", "B") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day2, 60), Play(1, Day2, 60), Play(2, Day2, 60),
                },
                Surveys = new List<SurveyEntry> { Survey(1, Day2, 4) },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            RecordsViewModel vm = RecordsPresenter.Build(snap, RecordsMetric.Plays, RecordsGroupAxis.Genre);

            Assert.Equal("3", vm.HeroValue);
            Assert.Equal(5, vm.RatingMeter.Max);                 // ★は 5 点満点
            Assert.Equal(3, vm.ResponseMeter.Max);               // 回答率の分母はプレイ回数
            Assert.Equal(1, vm.ResponseMeter.Value);
            Assert.Equal(2, vm.CoverageMeter.Max);               // 網羅率の分母は登録本数
            Assert.Equal(2, vm.CoverageMeter.Value);
            Assert.NotEmpty(vm.Strip);                           // 上限の無い量は帯に出す
        }

        [Fact]
        public void Compute_HourCounts_AggregateAcrossDays()
        {
            // 概況のスパークラインは期間全体を時間帯で束ねる (日ごとの内訳は Timelines が持つ)。
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day1.Date.AddHours(13), 0),
                    Play(1, Day2.Date.AddHours(13), 0),
                    Play(1, Day2.Date.AddHours(9), 0),
                },
            };

            int[] hours = PlayStatsService.Compute(data, PlayStatsRange.All).HourCounts;
            Assert.Equal(2, hours[13]);   // 日をまたいで同じ時刻に足し込む
            Assert.Equal(1, hours[9]);
            Assert.Equal(0, hours[0]);
        }

        // ==================================================================
        // 表彰
        // ==================================================================

        [Fact]
        public void Compute_RepeatPlay_CountsOnlySameGameOnSamePcWithinTheGap()
        {
            // 「終わってすぐ、同じ台でもう一度」だけを数える。別の台・別のゲーム・間が空いたものは
            // 数えない (数えると「その場の熱気」ではなく単なる人気を測ることになる)。
            long gap = PlayStatsTuning.RepeatGapSeconds;
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A"), Game(2, "b", "B") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day2, 60, "PC-1"),                                   // 12:00:00-12:01:00
                    Play(1, Day2.AddSeconds(60 + 30), 60, "PC-1"),               // 30 秒後に同じゲーム → 数える
                    Play(1, Day2.AddSeconds(60 + 30 + 60 + gap + 60), 60, "PC-1"),  // 間が空きすぎ → 数えない
                    Play(1, Day2, 60, "PC-2"),                                   // 別の台 → 数えない
                    Play(2, Day2.AddSeconds(60 + 10), 60, "PC-2"),               // 別のゲーム → 数えない
                },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            Assert.Equal(1, snap.Games.Single(g => g.GameNo == 1).RepeatPlayCount);
            Assert.Equal(0, snap.Games.Single(g => g.GameNo == 2).RepeatPlayCount);

            // **賞にはしない。** 1 回が短いゲームほど有利になる交絡があり (実データで中央プレイ時間と
            // 再プレイ率の相関 -0.547)、ゲーム内リトライを持つ作品は再起動が起きないので原理的に
            // 受賞できない。数値はゲーム詳細に出すだけに留める。
            Assert.DoesNotContain(snap.Awards, a => a.Title.Contains("もう一回"));
        }

        [Fact]
        public void Compute_Awards_PickTheTopOfEachAxis()
        {
            var surveys = new List<SurveyEntry>();
            for (int i = 0; i < 6; i++) surveys.Add(Survey(1, Day2, 3));   // よく遊ばれるが評価は普通
            for (int i = 0; i < 6; i++) surveys.Add(Survey(2, Day2, 5));   // 少数だが高評価

            var plays = new List<PlayRecordEntry>();
            for (int i = 0; i < 30; i++) plays.Add(Play(1, Day2, 60));
            for (int i = 0; i < 8; i++) plays.Add(Play(2, Day2, 600));

            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "みんなの定番"), Game(2, "b", "じっくり系") },
                Plays = plays,
                Surveys = surveys,
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);

            Assert.Equal("みんなの定番", Winner(snap, "最多プレイ賞"));
            Assert.Equal("じっくり系", Winner(snap, "満足度賞"));
            // のべ時間: 8 回 × 600 秒 = 80 分 > 30 回 × 60 秒 = 30 分。
            Assert.Equal("じっくり系", Winner(snap, "合計プレイ時間賞"));
        }

        [Fact]
        public void Compute_Awards_BreakTiesByTheLargerSample()
        {
            // ★5 率が同じなら回答が多い方。実データでも★5 率 0.80 が 4 本並び、ここで決着させないと
            // 「同点が多すぎる」で賞ごと消えていた。
            var surveys = new List<SurveyEntry>();
            for (int i = 0; i < 8; i++) surveys.Add(Survey(1, Day2, 5));   // 10 件中 8 件が★5
            for (int i = 0; i < 2; i++) surveys.Add(Survey(1, Day2, 4));
            for (int i = 0; i < 4; i++) surveys.Add(Survey(2, Day2, 5));   // 5 件中 4 件が★5 (同率 80%)
            surveys.Add(Survey(2, Day2, 4));

            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "回答が多い"), Game(2, "b", "回答が少ない") },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60), Play(2, Day2, 60) },
                Surveys = surveys,
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            PlayStatsAward award = snap.Awards.Single(a => a.Title == "満点賞");
            Assert.Equal("回答が多い", award.WinnerNames);
            Assert.False(award.IsTie);
        }

        [Fact]
        public void Compute_Awards_AreOmittedWhenNobodyQualifies()
        {
            // 回答が閾値に届かなければ満足度賞は出さない。「該当なし」の枠を並べない方が読みやすい。
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A") },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60) },
                Surveys = new List<SurveyEntry> { Survey(1, Day2, 5) },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            Assert.DoesNotContain(snap.Awards, a => a.Title == "満足度賞");
            Assert.Contains(snap.Awards, a => a.Title == "最多プレイ賞");   // 回数の賞は 1 回でも成立
        }

        [Fact]
        public void Compute_DeveloperAward_SkipsTheUnregisteredBucket()
        {
            // 「(制作者未登録)」が受賞すると表彰にならない。
            GameInfo named = Game(1, "a", "登録あり");
            named.Developers = new List<DeveloperInfo>
            {
                new DeveloperInfo { LastName = "山田", FirstName = "太郎", Grade = "76" },
            };
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { named, Game(2, "b", "登録なし") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day2, 60), Play(2, Day2, 60), Play(2, Day2, 60), Play(2, Day2, 60),
                },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            PlayStatsAward award = snap.Awards.Single(a => a.Title == "いちばん遊ばれた制作者");
            Assert.Equal("山田 太郎", award.WinnerNames);   // プレイ数では登録なしの方が多いが対象外
        }

        [Fact]
        public void Compute_DeveloperAward_KeepsWholeTeamEvenBeyondTheTieCap()
        {
            // 共作者は同じゲーム群を共有するので合計プレイ回数が**必ず一致する**。ゲームの賞の
            // 同点上限 (3) をそのまま当てると、4 人で作ったゲームが最多プレイだったときに賞が
            // 丸ごと消え、いちばん表彰したい相手を落とすことになる。全員を連名で出す。
            GameInfo team = Game(1, "a", "4 人でつくった");
            team.Developers = new List<DeveloperInfo>
            {
                new DeveloperInfo { LastName = "あ", FirstName = "一", Grade = "76" },
                new DeveloperInfo { LastName = "い", FirstName = "二", Grade = "76" },
                new DeveloperInfo { LastName = "う", FirstName = "三", Grade = "76" },
                new DeveloperInfo { LastName = "え", FirstName = "四", Grade = "76" },
            };
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { team },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60), Play(1, Day2, 60) },
            };

            PlayStatsAward award = PlayStatsService.Compute(data, PlayStatsRange.All).Awards
                .Single(a => a.Title == "いちばん遊ばれた制作者");

            Assert.True(award.IsTie);
            foreach (string who in new[] { "あ 一", "い 二", "う 三", "え 四" })
                Assert.Contains(who, award.WinnerNames);
        }

        [Fact]
        public void Compute_Awards_DropAnAwardWhenTooManyGamesTie()
        {
            // ゲーム側は逆。母数まで並んで 4 本が完全同率なら、その指標は差を付けられていない
            // = 賞として意味を成さないので出さない (共作の同点とは意味が違う)。
            var games = new List<GameInfo>();
            var plays = new List<PlayRecordEntry>();
            for (int i = 1; i <= 4; i++)
            {
                games.Add(Game(i, "g" + i, "同着" + i));
                plays.Add(Play(i, Day2, 60, "PC-1"));
            }

            PlayStatsSnapshot snap = PlayStatsService.Compute(
                new PlayStatsData { Games = games, Plays = plays }, null);

            Assert.DoesNotContain(snap.Awards, a => a.Title == "最多プレイ賞");
        }

        [Fact]
        public void Presenter_Highlights_ReportFirstLastAndBusiestPc()
        {
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "さいしょ"), Game(2, "b", "さいご") },
                Plays = new List<PlayRecordEntry>
                {
                    Play(1, Day2, 60, "PC-A"),
                    Play(2, Day2.AddHours(2), 60, "PC-B"),
                    Play(2, Day2.AddHours(1), 60, "PC-B"),
                },
            };

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
            Assert.Equal(1, snap.FirstPlayGameNo);
            Assert.Equal(2, snap.LastPlayGameNo);
            Assert.Equal("PC-B", snap.BusiestPc);
            Assert.Equal(2, snap.BusiestPcPlays);

            RecordsViewModel vm = RecordsPresenter.Build(snap, RecordsMetric.Plays, RecordsGroupAxis.Genre);
            Assert.Contains(vm.Highlights, h => h.Label == "最初のプレイ" && h.Value.Contains("さいしょ"));
            Assert.Contains(vm.Highlights, h => h.Label == "いちばん働いたPC" && h.Value.Contains("PC-B"));
        }

        private static string Winner(PlayStatsSnapshot snap, string title)
        {
            PlayStatsAward a = snap.Awards.SingleOrDefault(x => x.Title == title);
            return a == null ? null : a.WinnerNames;
        }

        // ==================================================================
        // 読み込み (I/O)
        // ==================================================================

        [Fact]
        public void Load_ReadsRecordsAndRecoversBrokenFilesFromFileName()
        {
            string root = Path.Combine(Path.GetTempPath(), "toneprism_stats_" + Guid.NewGuid().ToString("N"));
            try
            {
                string playDir = Path.Combine(root, "play_records", "2026-09-05");
                string surveyDir = Path.Combine(root, "surveys", "2026-09-05");
                Directory.CreateDirectory(playDir);
                Directory.CreateDirectory(surveyDir);

                long ts = Unix(Day2);
                File.WriteAllText(Path.Combine(playDir, ts + "-7-" + new string('a', 32) + ".json"),
                    "{\"created_at\":" + ts + ",\"start_time\":" + (ts - 120) + ",\"end_time\":" + ts
                    + ",\"game_no\":7,\"source_pc\":\"PC-A\",\"type\":\"play_record\"}");

                // 中身が壊れているが、ファイル名 (索引) は生きている → 回数だけ復元される。
                File.WriteAllText(Path.Combine(playDir, (ts + 1) + "-7-" + new string('b', 32) + ".json"),
                    "{ this is not json");

                File.WriteAllText(Path.Combine(surveyDir, ts + "-7-" + new string('c', 32) + ".json"),
                    "{\"created_at\":" + ts + ",\"game_no\":7,\"rating\":5,\"comment\":\"たのしかった\","
                    + "\"trigger\":\"game_end\",\"source_pc\":\"PC-A\",\"type\":\"survey\"}");

                // 全体アンケート (game_no = 0)
                File.WriteAllText(Path.Combine(surveyDir, ts + "-0-" + new string('d', 32) + ".json"),
                    "{\"created_at\":" + ts + ",\"game_no\":0,\"rating\":4,\"comment\":null,"
                    + "\"trigger\":\"launcher_end\",\"source_pc\":\"PC-A\",\"type\":\"survey\"}");

                // 範囲外の★は平均を汚すので落とす。
                File.WriteAllText(Path.Combine(surveyDir, ts + "-7-" + new string('e', 32) + ".json"),
                    "{\"created_at\":" + ts + ",\"game_no\":7,\"rating\":9,\"trigger\":\"game_end\"}");

                PlayStatsData data = PlayStatsLoader.Load(null, root);

                Assert.Equal(2, data.Plays.Count);
                Assert.Equal(1, data.RecoveredFromFileNameCount);
                Assert.Equal(2, data.Surveys.Count);       // ★9 は除外
                Assert.Equal(1, data.UnreadableFileCount);
                Assert.Single(data.AvailableDates);

                PlayStatsSnapshot snap = PlayStatsService.Compute(data, PlayStatsRange.All);
                Assert.Equal(2, snap.TotalPlays);
                Assert.Equal(1, snap.GameSurveys);
                Assert.Equal(1, snap.Overall.Count);
                // 復元した記録は長さが分からないので 0 秒扱い。中央値は {0, 120} の真ん中 = 60。
                // 回数は救いつつ時間は引っ張られるという trade-off をここで固定しておく。
                Assert.Equal(60, snap.Games.Single().MedianDurationSeconds);

                PlayStatsComment comment = snap.Comments.Single(c => !c.IsOverall);
                Assert.Equal("たのしかった", comment.Text);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* 後片付けの失敗は無視 */ }
            }
        }

        [Fact]
        public void Load_MissingFolder_ReportsInsteadOfThrowing()
        {
            PlayStatsData data = PlayStatsLoader.Load(null,
                Path.Combine(Path.GetTempPath(), "toneprism_missing_" + Guid.NewGuid().ToString("N")));

            Assert.True(data.ResponsesFolderMissing);
            Assert.Empty(data.Plays);
            Assert.Empty(data.Surveys);
        }
    }
}
