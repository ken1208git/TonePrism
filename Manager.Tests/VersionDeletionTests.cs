using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using TonePrism.Manager;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#209) 個別バージョン削除 (DatabaseManager.DeleteGameVersionAndReassignActive) の統合テスト。
    /// 一時 DB で「版行削除」「アクティブ (games.version) の自動付け替え」「version 別 developers の cascade 削除」を検証する。
    /// DataLayerRoundTripTests と同じ #239 方針 (PathManager 非依存の一時 DB)。
    /// </summary>
    public class VersionDeletionTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseConnection _conn;
        private readonly DatabaseManager _db;
        private readonly string _tmpDir; // GameVersionDeletionService のフォルダ操作検証用の一時ディレクトリ。

        public VersionDeletionTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "tp_vdel_" + Guid.NewGuid().ToString("N") + ".db");
            _conn = new DatabaseConnection(_dbPath);
            new SchemaManager(_conn).InitializeDatabase();
            _db = new DatabaseManager(_conn);
            _tmpDir = Path.Combine(Path.GetTempPath(), "tp_vdel_dir_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            try { SQLiteConnection.ClearAllPools(); } catch { /* ignore */ }
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
            }
            try { if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true); } catch { /* ignore */ }
        }

        private void SeedGame(string gameId, string activeVersion)
        {
            _db.AddGame(new GameInfo
            {
                GameId = gameId,
                Title = gameId,
                Version = activeVersion,
                Genre = new List<string>(),
                Developers = new List<DeveloperInfo>(),
                IsVisible = true,
            });
        }

        private GameVersion AddVersion(string gameId, string version, params string[] developerLastNames)
        {
            var devs = new List<DeveloperInfo>();
            foreach (var n in developerLastNames) devs.Add(new DeveloperInfo { GameId = gameId, LastName = n });
            var v = new GameVersion
            {
                GameId = gameId,
                Version = version,
                ExecutablePath = version + "/game.exe",
                Genre = new List<string>(),
                Developers = devs,
            };
            _db.AddGameVersion(v); // AddVersionRowInTransaction が v.Id を採番する
            return v;
        }

        private string ReadGamesVersion(string gameId)
        {
            using (var c = new SQLiteConnection(_conn.ConnectionString))
            {
                c.Open();
                using (var cmd = new SQLiteCommand("SELECT version FROM games WHERE game_id = @g", c))
                {
                    cmd.Parameters.AddWithValue("@g", gameId);
                    object r = cmd.ExecuteScalar();
                    return r == null || r == DBNull.Value ? null : (string)r;
                }
            }
        }

        private int CountDevelopersForVersion(int versionId)
        {
            using (var c = new SQLiteConnection(_conn.ConnectionString))
            {
                c.Open();
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM developers WHERE version_id = @v", c))
                {
                    cmd.Parameters.AddWithValue("@v", versionId);
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
        }

        [Fact]
        public void DeleteNonActiveVersion_RemovesRow_KeepsActive()
        {
            SeedGame("g1", "v1.1.0");
            var v1 = AddVersion("g1", "v1.0.0");
            AddVersion("g1", "v1.1.0");

            string newActive = _db.DeleteGameVersionAndReassignActive("g1", v1.Id); // 非アクティブを削除

            var remaining = _db.GetGameVersions("g1");
            Assert.Single(remaining);
            Assert.Equal("v1.1.0", remaining[0].Version);
            Assert.Equal("v1.1.0", ReadGamesVersion("g1")); // active は据え置き
            Assert.Equal("v1.1.0", newActive);
        }

        [Fact]
        public void DeleteActiveVersion_ReassignsToRemaining()
        {
            SeedGame("g2", "v1.1.0");
            AddVersion("g2", "v1.0.0");
            var v2 = AddVersion("g2", "v1.1.0");

            string newActive = _db.DeleteGameVersionAndReassignActive("g2", v2.Id); // active を削除

            var remaining = _db.GetGameVersions("g2");
            Assert.Single(remaining);
            Assert.Equal("v1.0.0", remaining[0].Version);
            Assert.Equal("v1.0.0", ReadGamesVersion("g2")); // 残り版へ付け替え
            Assert.Equal("v1.0.0", newActive);
        }

        [Fact]
        public void DeleteActiveVersion_ReassignsToLatestByIdDesc()
        {
            SeedGame("g3", "v2.0.0");
            AddVersion("g3", "v1.0.0");
            var v2 = AddVersion("g3", "v2.0.0");
            AddVersion("g3", "v3.0.0");

            string newActive = _db.DeleteGameVersionAndReassignActive("g3", v2.Id); // 真ん中 (active) を削除

            Assert.Equal(2, _db.GetGameVersions("g3").Count);
            Assert.Equal("v3.0.0", ReadGamesVersion("g3")); // 残り最新 (id 最大 = v3) へ
            Assert.Equal("v3.0.0", newActive);
        }

        [Fact]
        public void DeleteVersion_CascadeDeletesOnlyThatVersionsDevelopers()
        {
            SeedGame("g4", "v1.1.0");
            var v1 = AddVersion("g4", "v1.0.0", "山田", "佐藤");
            var v2 = AddVersion("g4", "v1.1.0", "鈴木");

            Assert.Equal(2, CountDevelopersForVersion(v1.Id));
            Assert.Equal(1, CountDevelopersForVersion(v2.Id));

            _db.DeleteGameVersionAndReassignActive("g4", v1.Id);

            Assert.Equal(0, CountDevelopersForVersion(v1.Id)); // version_id FK cascade で消える
            Assert.Equal(1, CountDevelopersForVersion(v2.Id)); // 他版の developers は残る
        }

        // ---- GameVersionDeletionService (フォルダ + DB の 3-phase) の統合テスト (#209 review finding 4) ----
        // 注: DB 失敗時の rollback 経路 (DbFailedRolledBack / DbFailedRollbackAlsoFailed) は DB 例外を決定的に
        //     起こせないため未カバー。正常系・フォルダ不在系・active 付け替えの戻り値を実フォルダ + 一時 DB で検証する。

        private string CreateVersionFolderWithFile(string leaf)
        {
            string dir = Path.Combine(_tmpDir, leaf);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "game.exe"), "dummy");
            return dir;
        }

        [Fact]
        public void Service_Delete_Success_RemovesFolderAndRow()
        {
            SeedGame("sg1", "v1.1.0");
            var v1 = AddVersion("sg1", "v1.0.0");
            AddVersion("sg1", "v1.1.0");
            string folder = CreateVersionFolderWithFile("sg1_v1.0.0");

            var result = GameVersionDeletionService.Delete(_db, "sg1", v1.Id, folder);

            Assert.Equal(GameVersionDeletionService.Outcome.Success, result.Outcome);
            Assert.False(Directory.Exists(folder));          // Phase 1 退避 → Phase 3 物理削除済
            Assert.Empty(Directory.GetDirectories(_tmpDir));  // .pending-delete も残らない
            Assert.Single(_db.GetGameVersions("sg1"));        // DB 行も削除
            Assert.Equal("v1.1.0", ReadGamesVersion("sg1"));  // 非アクティブ削除 → active 据え置き
        }

        [Fact]
        public void Service_Delete_FolderAbsent_StillRemovesRow()
        {
            SeedGame("sg2", "v1.1.0");
            var v1 = AddVersion("sg2", "v1.0.0");
            AddVersion("sg2", "v1.1.0");
            string folder = Path.Combine(_tmpDir, "does_not_exist_v1.0.0"); // 作らない

            var result = GameVersionDeletionService.Delete(_db, "sg2", v1.Id, folder);

            Assert.Equal(GameVersionDeletionService.Outcome.Success, result.Outcome);
            Assert.Single(_db.GetGameVersions("sg2")); // フォルダ不在でも DB は削除
        }

        [Fact]
        public void Service_DeleteActive_ReassignsAndReturnsNewActive()
        {
            SeedGame("sg3", "v1.1.0");
            AddVersion("sg3", "v1.0.0");
            var v2 = AddVersion("sg3", "v1.1.0");
            string folder = CreateVersionFolderWithFile("sg3_v1.1.0");

            var result = GameVersionDeletionService.Delete(_db, "sg3", v2.Id, folder); // active を削除

            Assert.Equal(GameVersionDeletionService.Outcome.Success, result.Outcome);
            Assert.Equal("v1.0.0", result.NewActiveVersion);  // service 戻り値 = DB 真値
            Assert.Equal("v1.0.0", ReadGamesVersion("sg3"));  // DB も付け替え済
            Assert.False(Directory.Exists(folder));
        }
    }
}
