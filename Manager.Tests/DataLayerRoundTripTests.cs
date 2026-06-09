using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using TonePrism.Manager;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#239) データ層の round-trip / スキーマ初期化テスト。
    /// 一時 DB を `DatabaseConnection(string dbPath)` で指して、PathManager のプロジェクトルート検出に
    /// 依存せず SchemaManager / repository を単体で回す。
    /// </summary>
    public class DataLayerRoundTripTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseConnection _conn;

        public DataLayerRoundTripTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "tp_test_" + Guid.NewGuid().ToString("N") + ".db");
            _conn = new DatabaseConnection(_dbPath);
            new SchemaManager(_conn).InitializeDatabase();
        }

        public void Dispose()
        {
            try { SQLiteConnection.ClearAllPools(); } catch { /* ignore */ }
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void FreshDb_InitializeDatabase_ReachesTargetVersion()
        {
            var schema = new SchemaManager(_conn);
            Assert.Equal(schema.GetTargetDatabaseVersion(), schema.GetActualDatabaseVersion());
            Assert.True(schema.TablesExist());
        }

        [Fact]
        public void GameRepository_AddThenGetById_RoundTripsCoreFields()
        {
            var devRepo = new DeveloperRepository(_conn);
            var gameRepo = new GameRepository(_conn, devRepo);

            var game = new GameInfo
            {
                GameId = "round_trip_test",
                Title = "ラウンドトリップ確認",
                Description = "説明",
                ReleaseYear = 2026,
                MinPlayers = 1,
                MaxPlayers = 2,
                Difficulty = 2,
                PlayTime = 3,
                ControllerSupport = true,
                SupportedConnection = 1,
                IsVisible = true,
                Version = "v1.0.0",
                Genre = new List<string> { "アクション" },
                Developers = new List<DeveloperInfo>(),
            };
            gameRepo.Add(game);

            var read = gameRepo.GetById("round_trip_test");

            Assert.NotNull(read);
            Assert.Equal(game.GameId, read.GameId);
            Assert.Equal(game.Title, read.Title);
            Assert.Equal(game.Description, read.Description);
            Assert.Equal(game.ReleaseYear, read.ReleaseYear);
            Assert.Equal(game.MinPlayers, read.MinPlayers);
            Assert.Equal(game.MaxPlayers, read.MaxPlayers);
            Assert.Equal(game.Difficulty, read.Difficulty);
            Assert.Equal(game.PlayTime, read.PlayTime);
            Assert.True(read.ControllerSupport);
            Assert.Equal(game.SupportedConnection, read.SupportedConnection);
            Assert.Equal("v1.0.0", read.Version);
            Assert.Contains("アクション", read.Genre);
        }

        [Fact]
        public void GameRepository_UpdateGameId_RenamesWithoutTouchingDroppedTables()
        {
            // (#297 / DB v23) UpdateGameId の子テーブル更新は play_records / surveys / launcher_surveys を
            // 参照してはならない。これらは v23 で DROP 済みなので、UPDATE 文が残っていると rename が
            // 「no such table」で落ちる。fresh v23 DB で rename が例外なく完走し、生き残った子テーブル
            // (developers) も新 game_id へ追従することで回帰を固定する。
            var devRepo = new DeveloperRepository(_conn);
            var gameRepo = new GameRepository(_conn, devRepo);

            var game = new GameInfo
            {
                GameId = "old_id",
                Title = "リネーム前",
                IsVisible = true,
                Genre = new List<string>(),
                Developers = new List<DeveloperInfo>
                {
                    new DeveloperInfo { LastName = "山田", FirstName = "太郎", Grade = "3" },
                },
            };
            gameRepo.Add(game);

            // 旧コードは play_records への UPDATE で no such table を投げていた。例外なく完走すること。
            var ex = Record.Exception(() => gameRepo.UpdateGameId("old_id", "new_id"));
            Assert.Null(ex);

            Assert.Null(gameRepo.GetById("old_id"));
            var renamed = gameRepo.GetById("new_id");
            Assert.NotNull(renamed);
            Assert.Equal("リネーム前", renamed.Title);
            // 子テーブル (developers) が新 game_id へ追従している。
            Assert.Single(renamed.Developers);
            Assert.Equal("山田", renamed.Developers[0].LastName);
        }

        [Fact]
        public void GameRepository_NullReleaseYear_AndBlankOrTeacherGrade_RoundTrip()
        {
            // (#313) リリース年 null（不明）と grade ""（不明）/ "0"（教員）/ "50"（50期生）が
            // round-trip で維持されることを固定。特に grade="" が "0"(教員) に化けない・release_year=null が
            // 現在年で silent 上書きされないこと（チェックボックス方式の DB 表現）を保証する。
            var devRepo = new DeveloperRepository(_conn);
            var gameRepo = new GameRepository(_conn, devRepo);

            var game = new GameInfo
            {
                GameId = "nullable_year_grade",
                Title = "不明値テスト",
                ReleaseYear = null, // 不明
                IsVisible = true,
                Version = "v1.0.0",
                Genre = new List<string>(),
                Developers = new List<DeveloperInfo>
                {
                    new DeveloperInfo { LastName = "不明", FirstName = "期生", Grade = "" },  // 不明
                    new DeveloperInfo { LastName = "先生", FirstName = "甲", Grade = "0" },   // 教員
                    new DeveloperInfo { LastName = "部員", FirstName = "乙", Grade = "50" },  // 50期生
                },
            };
            gameRepo.Add(game);

            var read = gameRepo.GetById("nullable_year_grade");
            Assert.NotNull(read);
            Assert.Null(read.ReleaseYear); // 不明 = null が維持（現在年で上書きされない）

            var grades = read.Developers.ToDictionary(d => d.LastName, d => d.Grade);
            Assert.Equal("", grades["不明"]);   // 空欄=不明 が "0"(教員) に化けない
            Assert.Equal("0", grades["先生"]);  // 教員
            Assert.Equal("50", grades["部員"]); // 50期生
        }

        [Fact]
        public void StoreSection_SwapDisplayOrder_SwapsAtomically()
        {
            // 並び替えの atomic swap (half-write 解消、store 側) を検証。
            var repo = new StoreSectionRepository(_conn);
            var a = new StoreSectionInfo { Title = "A", SectionSource = "manual", DisplayOrder = 0, MaxDisplayCount = 5, IsVisible = true };
            var b = new StoreSectionInfo { Title = "B", SectionSource = "manual", DisplayOrder = 1, MaxDisplayCount = 5, IsVisible = true };
            repo.Add(a);
            repo.Add(b);

            // a→b の order(1)、b→a の order(0) に入れ替え。
            repo.SwapDisplayOrder(a.SectionId, b.DisplayOrder, b.SectionId, a.DisplayOrder);

            var all = repo.GetAll(); // display_order 昇順
            Assert.Equal("B", all[0].Title); // order 0 が B
            Assert.Equal("A", all[1].Title); // order 1 が A
        }

        [Fact]
        public void StoreSection_SwapDisplayOrder_MissingRow_ThrowsInsteadOfSilentNoOp()
        {
            // (#275 review #1) 片方の section_id が存在しないと 2 行更新できず throw (fail-loud)。
            var repo = new StoreSectionRepository(_conn);
            var a = new StoreSectionInfo { Title = "A", SectionSource = "manual", DisplayOrder = 0, MaxDisplayCount = 5, IsVisible = true };
            repo.Add(a);
            Assert.Throws<InvalidOperationException>(() => repo.SwapDisplayOrder(a.SectionId, 1, 999999, 0));
        }
    }
}
