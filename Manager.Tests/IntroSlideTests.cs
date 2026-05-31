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

        [Fact]
        public void MigrationV21ToV22_RealData_DropsDurationSec_PreservesRows()
        {
            // (#274 review #1) 本番 risk path の実データ round-trip: v21 の **duration_sec 付き** intro_slides に
            // 実データを入れ、InitializeDatabase で v21→v22 (MigrateV21ToV22 の recreate-and-copy) を走らせ、
            // duration_sec 列が消えつつ全行が保持されることを検証。v20 始点のテストでは MigrateV21ToV22 が
            // TableHasColumn 判定で no-op になり recreate 経路が未カバーだったため、本テストで明示的に走らせる。
            Exec("DROP TABLE intro_slides");
            Exec(@"CREATE TABLE intro_slides (
                slide_id INTEGER PRIMARY KEY AUTOINCREMENT,
                display_order INTEGER DEFAULT 0,
                body_text TEXT DEFAULT '',
                image_path TEXT,
                duration_sec INTEGER NOT NULL DEFAULT 5 CHECK(duration_sec BETWEEN 1 AND 60),
                is_visible INTEGER DEFAULT 1)");
            Exec("INSERT INTO intro_slides (display_order, body_text, image_path, duration_sec, is_visible) VALUES (0, 'スライド1', 'guide/a.png', 7, 1)");
            Exec("INSERT INTO intro_slides (display_order, body_text, image_path, duration_sec, is_visible) VALUES (1, 'スライド2', NULL, 3, 0)");
            Exec("PRAGMA user_version = 21");
            Assert.Equal(21, new SchemaManager(_conn).GetActualDatabaseVersion());

            new SchemaManager(_conn).InitializeDatabase();

            var schema = new SchemaManager(_conn);
            Assert.Equal(schema.GetTargetDatabaseVersion(), schema.GetActualDatabaseVersion()); // v22 到達
            // duration_sec 列が消えていること。
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM pragma_table_info('intro_slides') WHERE name = 'duration_sec'"));
            // 全行が保持され、内容 (slide_id 順序・本文・画像有無・表示状態) も無傷。
            var slides = new IntroSlideRepository(_conn).GetAll();
            Assert.Equal(2, slides.Count);
            Assert.Equal("スライド1", slides[0].BodyText);
            Assert.Equal("guide/a.png", slides[0].ImagePath);
            Assert.True(slides[0].IsVisible);
            Assert.Equal("スライド2", slides[1].BodyText);
            Assert.Null(slides[1].ImagePath);
            Assert.False(slides[1].IsVisible);
        }
    }
}
