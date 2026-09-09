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

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, null);

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

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, null);

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

            GameStats g = PlayStatsService.Compute(data, null).Games.Single();
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

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, null);
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

            Assert.Equal(3, PlayStatsService.Compute(data, null).TotalPlays);
            Assert.Equal(1, PlayStatsService.Compute(data, Day1.Date).TotalPlays);
            Assert.Equal(2, PlayStatsService.Compute(data, Day2.Date).TotalPlays);
            Assert.Equal(5.0, PlayStatsService.Compute(data, Day1.Date).AverageRating.Value, 3);
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

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, null);
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
                PlayStatsService.Compute(data, null), RecordsMetric.Plays, RecordsGroupAxis.Genre);

            Assert.Equal("多い", vm.Games[0].Title);
            Assert.Equal(1, vm.Games[0].Rank);
            Assert.True(vm.Games[0].BarWidth > vm.Games[1].BarWidth);
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

            PlayStatsGroup g = PlayStatsService.Compute(data, null).ByGenre.Single();
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

            PlayStatsSnapshot snap = PlayStatsService.Compute(data, null);

            Assert.Equal(2, snap.ByDeveloper.Count);
            Assert.All(snap.ByDeveloper, g => Assert.Equal(2, g.PlayCount));   // 共作は両方に計上
            Assert.Contains(snap.ByDeveloper, g => g.Key == "山田 太郎");
            Assert.Equal(2, snap.ByGrade.Count);
            Assert.Contains(snap.ByGrade, g => g.Key == "76期生");
        }

        // ==================================================================
        // 注記
        // ==================================================================

        [Fact]
        public void Presenter_Notes_AlwaysMentionTheStarThreeInitialFocus()
        {
            // (#463) ★3 に初期カーソルが当たる導線はデータに関係なく常にあるので、注記も常に出す。
            // 「★3 が★4 を上回ったときだけ警告カードを出す」作りは、結果を見せる場で前置きが主役に
            // なってしまうため撤去した。ここが消えると注意書きが無いまま数字だけが独り歩きする。
            var data = new PlayStatsData
            {
                Games = new List<GameInfo> { Game(1, "a", "A") },
                Plays = new List<PlayRecordEntry> { Play(1, Day2, 60) },
                Surveys = new List<SurveyEntry> { Survey(1, Day2, 5) },
            };

            RecordsViewModel vm = RecordsPresenter.Build(
                PlayStatsService.Compute(data, null), RecordsMetric.Plays, RecordsGroupAxis.Genre);

            Assert.Contains(vm.Notes, n => n.Contains("★3"));
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

                PlayStatsSnapshot snap = PlayStatsService.Compute(data, null);
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
