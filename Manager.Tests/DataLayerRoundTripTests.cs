using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
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
    }
}
