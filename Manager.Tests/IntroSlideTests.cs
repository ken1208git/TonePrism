using System;
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
    /// (#253) intro_slides スキーマ + `IntroSlideRepository` の round-trip / migration テスト。
    /// `DatabaseConnection(string)` seam で PathManager 非依存に一時 DB を回す (#239 基盤)。
    /// </summary>
    public class IntroSlideTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseConnection _conn;

        public IntroSlideTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "tp_introslide_" + Guid.NewGuid().ToString("N") + ".db");
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

        private void Exec(string sql)
        {
            using (var c = new SQLiteConnection(_conn.ConnectionString))
            {
                c.Open();
                using (var cmd = new SQLiteCommand(sql, c)) { cmd.ExecuteNonQuery(); }
            }
        }

        private long Scalar(string sql)
        {
            using (var c = new SQLiteConnection(_conn.ConnectionString))
            {
                c.Open();
                using (var cmd = new SQLiteCommand(sql, c)) { return Convert.ToInt64(cmd.ExecuteScalar()); }
            }
        }

        [Fact]
        public void FreshDb_ReachesV22_WithIntroSlidesTable()
        {
            var schema = new SchemaManager(_conn);
            Assert.Equal(22, schema.GetTargetDatabaseVersion());
            Assert.Equal(22, schema.GetActualDatabaseVersion());
            // intro_slides が存在し空で読める。
            Assert.Empty(new IntroSlideRepository(_conn).GetAll());
        }

        [Fact]
        public void IntroSlide_AddGetUpdateDelete_RoundTrips()
        {
            var repo = new IntroSlideRepository(_conn);

            var s1 = new IntroSlide { DisplayOrder = 0, BodyText = "ようこそ", ImagePath = "guide/welcome.png", IsVisible = true };
            var s2 = new IntroSlide { DisplayOrder = 1, BodyText = "注意事項", ImagePath = null, IsVisible = false }; // text-only
            repo.Add(s1);
            repo.Add(s2);
            Assert.True(s1.SlideId > 0);
            Assert.True(s2.SlideId > 0);

            var all = repo.GetAll();
            Assert.Equal(2, all.Count);
            // 表示順 (display_order) で返る。
            Assert.Equal("ようこそ", all[0].BodyText);
            Assert.Equal("guide/welcome.png", all[0].ImagePath);
            Assert.True(all[0].IsVisible);
            // text-only スライドは ImagePath が null。
            Assert.Null(all[1].ImagePath);
            Assert.False(all[1].IsVisible);

            // Update
            all[0].BodyText = "ようこそ（改）";
            repo.Update(all[0]);
            var reread = repo.GetAll();
            Assert.Equal("ようこそ（改）", reread[0].BodyText);

            // Delete
            repo.Delete(reread[0].SlideId);
            var afterDelete = repo.GetAll();
            Assert.Single(afterDelete);
            Assert.Equal("注意事項", afterDelete[0].BodyText);
        }

        [Fact]
        public void EmptyImagePath_NormalizedToNull()
        {
            var repo = new IntroSlideRepository(_conn);
            repo.Add(new IntroSlide { DisplayOrder = 0, BodyText = "x", ImagePath = "   " });
            Assert.Null(repo.GetAll().Single().ImagePath);
            // DB 上も NULL (空文字ではない)。
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM intro_slides WHERE image_path = ''"));
            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM intro_slides WHERE image_path IS NULL"));
        }

        [Fact]
        public void Migration_OldV20Db_ReachesCurrentTarget_PreservesData()
        {
            // 旧 v20 DB を再現 (intro_slides を落として user_version=20)、既存データ (games 1 行) を seed →
            // 再 InitializeDatabase で v20 → … → 現行ターゲット (v22) まで migration が走り、intro_slides が
            // 復活し、既存データが保持されることを検証。version は動的取得で schema bump 耐性を持たせる。
            Exec("DROP TABLE intro_slides");
            Exec("INSERT INTO games (game_id, title) VALUES ('preserve_me', '残るゲーム')");
            Exec("PRAGMA user_version = 20");
            Assert.Equal(20, new SchemaManager(_conn).GetActualDatabaseVersion());

            new SchemaManager(_conn).InitializeDatabase();

            var schema = new SchemaManager(_conn);
            Assert.Equal(schema.GetTargetDatabaseVersion(), schema.GetActualDatabaseVersion());
            // intro_slides 復活 + 既存ゲーム保持。
            Assert.Empty(new IntroSlideRepository(_conn).GetAll());
            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM games WHERE game_id = 'preserve_me'"));
        }
    }
}
