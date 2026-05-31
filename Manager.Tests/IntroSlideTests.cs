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
        public void FreshDb_ReachesV21_WithIntroSlidesTable()
        {
            var schema = new SchemaManager(_conn);
            Assert.Equal(21, schema.GetTargetDatabaseVersion());
            Assert.Equal(21, schema.GetActualDatabaseVersion());
            // intro_slides が存在し空で読める。
            Assert.Empty(new IntroSlideRepository(_conn).GetAll());
        }

        [Fact]
        public void IntroSlide_AddGetUpdateDelete_RoundTrips()
        {
            var repo = new IntroSlideRepository(_conn);

            var s1 = new IntroSlide { DisplayOrder = 0, BodyText = "ようこそ", ImagePath = "guide/welcome.png", DurationSec = 7, IsVisible = true };
            var s2 = new IntroSlide { DisplayOrder = 1, BodyText = "注意事項", ImagePath = null, DurationSec = 5, IsVisible = false }; // text-only
            repo.Add(s1);
            repo.Add(s2);
            Assert.True(s1.SlideId > 0);
            Assert.True(s2.SlideId > 0);

            var all = repo.GetAll();
            Assert.Equal(2, all.Count);
            // 表示順 (display_order) で返る。
            Assert.Equal("ようこそ", all[0].BodyText);
            Assert.Equal("guide/welcome.png", all[0].ImagePath);
            Assert.Equal(7, all[0].DurationSec);
            Assert.True(all[0].IsVisible);
            // text-only スライドは ImagePath が null。
            Assert.Null(all[1].ImagePath);
            Assert.False(all[1].IsVisible);

            // Update
            all[0].BodyText = "ようこそ（改）";
            all[0].DurationSec = 10;
            repo.Update(all[0]);
            var reread = repo.GetAll();
            Assert.Equal("ようこそ（改）", reread[0].BodyText);
            Assert.Equal(10, reread[0].DurationSec);

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
            repo.Add(new IntroSlide { DisplayOrder = 0, BodyText = "x", ImagePath = "   ", DurationSec = 5 });
            Assert.Null(repo.GetAll().Single().ImagePath);
            // DB 上も NULL (空文字ではない)。
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM intro_slides WHERE image_path = ''"));
            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM intro_slides WHERE image_path IS NULL"));
        }

        [Fact]
        public void DurationCheck_RejectsOutOfRange()
        {
            var repo = new IntroSlideRepository(_conn);
            // CHECK(duration_sec BETWEEN 1 AND 60) 違反は SQLiteException (Constraint)。
            Assert.Throws<SQLiteException>(() => repo.Add(new IntroSlide { DisplayOrder = 0, BodyText = "x", DurationSec = 999 }));
            Assert.Throws<SQLiteException>(() => repo.Add(new IntroSlide { DisplayOrder = 0, BodyText = "x", DurationSec = 0 }));
        }

        [Fact]
        public void MigrationV20ToV21_AddsIntroSlides_PreservesExistingData()
        {
            // v21 の DB から intro_slides を落とし user_version を 20 に戻して「旧 v20 DB」を再現、
            // 既存データ (games の 1 行) を seed → 再 InitializeDatabase で v20→v21 migration が走り、
            // intro_slides が復活し、既存データが保持され、user_version が 21 になることを検証。
            Exec("DROP TABLE intro_slides");
            Exec("INSERT INTO games (game_id, title) VALUES ('preserve_me', '残るゲーム')");
            Exec("PRAGMA user_version = 20");
            Assert.Equal(20, new SchemaManager(_conn).GetActualDatabaseVersion());

            new SchemaManager(_conn).InitializeDatabase();

            Assert.Equal(21, new SchemaManager(_conn).GetActualDatabaseVersion());
            // intro_slides 復活 + 既存ゲーム保持。
            Assert.Empty(new IntroSlideRepository(_conn).GetAll());
            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM games WHERE game_id = 'preserve_me'"));
        }
    }
}
