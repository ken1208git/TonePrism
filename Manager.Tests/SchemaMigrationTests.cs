using System;
using System.Data.SQLite;
using System.IO;
using TonePrism.Manager;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#239 / F-1) 既存 DB の in-place スキーマ移行が子テーブルを CASCADE で消さないことの自動回帰テスト。
    /// 最高 blast-radius な v19 → v20 の `games` 親テーブル recreate を、実データ入りの v19 状態 DB で検証する。
    /// これまで手動 (sqlite3 + 実機 Manager) でしか確認できなかった F-1 を自動化し、net10 移行 (Phase 4) の
    /// data-core 再検証ゲートとしても効かせる。
    /// </summary>
    public class SchemaMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SchemaMigrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "tp_mig_" + Guid.NewGuid().ToString("N") + ".db");
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
        public void V19ToV20_GamesRecreate_PreservesChildRows_NoCascade()
        {
            BuildV19DbWithChildren(_dbPath, playTime: 2);

            // in-place migration (v19 → v20、games 親テーブル recreate を foreign_keys=OFF で実行)
            new SchemaManager(new DatabaseConnection(_dbPath)).InitializeDatabase();
            SQLiteConnection.ClearAllPools();

            using (var c = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                c.Open();
                // 全 migration 完走で現行ターゲット版に到達 (v19→v20 の games recreate + 以降の chain。
                // #253 で v21 追加)。schema bump で数値が変わっても壊れないよう target を動的取得して比較。
                Assert.Equal((long)new SchemaManager(new DatabaseConnection(_dbPath)).GetTargetDatabaseVersion(),
                    ScalarLong(c, "PRAGMA user_version"));
                // 子テーブルが CASCADE で巻き添え削除されていない
                Assert.Equal(1L, ScalarLong(c, "SELECT COUNT(*) FROM games"));
                Assert.Equal(2L, ScalarLong(c, "SELECT COUNT(*) FROM game_versions"));
                Assert.Equal(1L, ScalarLong(c, "SELECT COUNT(*) FROM developers"));
                // (#297) play_records は v23 で DROP されるため COUNT 検証は撤去。CASCADE 非発生の検証意図は
                // game_versions / developers の COUNT で担保される。
                // FK 整合: foreign_key_check が違反行を返さない
                using (var cmd = new SQLiteCommand("PRAGMA foreign_key_check", c))
                using (var r = cmd.ExecuteReader())
                {
                    Assert.False(r.Read(), "foreign_key_check に違反行が残っている");
                }
                // play_time CHECK が付与された
                var ddl = ScalarString(c, "SELECT sql FROM sqlite_master WHERE type='table' AND name='games'");
                Assert.Contains("play_time INTEGER CHECK", ddl, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void V19ToV20_OutOfRangePlayTime_SkipsAndStaysV19_WithoutCrashOrDataLoss()
        {
            BuildV19DbWithChildren(_dbPath, playTime: 5); // 範囲外 (1-3 でない)

            // 範囲外データ残存時は hard-fail せず skip + retry (user_version 据え置き、起動を止めない)
            var ex = Record.Exception(() => new SchemaManager(new DatabaseConnection(_dbPath)).InitializeDatabase());
            Assert.Null(ex); // クラッシュしない
            SQLiteConnection.ClearAllPools();

            using (var c = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                c.Open();
                Assert.Equal(19L, ScalarLong(c, "PRAGMA user_version")); // 据え置き (次回是正後に適用)
                Assert.Equal(2L, ScalarLong(c, "SELECT COUNT(*) FROM game_versions")); // 子は無事
            }
        }

        /// <summary>
        /// (#297) v22 状態 DB に surveys / play_records / launcher_surveys を空で作り、InitializeDatabase
        /// (v22→v23 migration) 後にこれら 3 テーブルが DROP され user_version が現行ターゲットに到達することを検証する。
        /// スキーマ撤去の自動回帰。
        /// </summary>
        [Fact]
        public void V22ToV23_DropsEventTables()
        {
            using (var c = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                c.Open();
                // FK 親の games + 撤去対象 3 テーブルを最小構成で作る (v22 相当)。
                Exec(c, "CREATE TABLE games (game_id TEXT PRIMARY KEY, title TEXT)");
                Exec(c, "CREATE TABLE play_records (id INTEGER PRIMARY KEY AUTOINCREMENT, game_id TEXT, start_time TEXT, end_time TEXT, play_duration INTEGER, player_count INTEGER, FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE)");
                Exec(c, "CREATE TABLE surveys (id INTEGER PRIMARY KEY AUTOINCREMENT, game_id TEXT, rating INTEGER, comment TEXT, created_at TEXT, FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE)");
                Exec(c, "CREATE TABLE launcher_surveys (id INTEGER PRIMARY KEY AUTOINCREMENT, rating INTEGER, favorite_game_id TEXT, comment TEXT, created_at TEXT, FOREIGN KEY(favorite_game_id) REFERENCES games(game_id) ON DELETE SET NULL)");
                Exec(c, "PRAGMA user_version=22");
            }

            new SchemaManager(new DatabaseConnection(_dbPath)).InitializeDatabase();
            SQLiteConnection.ClearAllPools();

            using (var c = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                c.Open();
                Assert.Equal((long)new SchemaManager(new DatabaseConnection(_dbPath)).GetTargetDatabaseVersion(),
                    ScalarLong(c, "PRAGMA user_version"));
                Assert.Equal(0L, ScalarLong(c, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('surveys','play_records','launcher_surveys')"));
                Assert.Equal("ok", ScalarString(c, "PRAGMA integrity_check"));
            }
        }

        /// <summary>
        /// (#297 review) versioning 導入前 (user_version=0) で物理的に surveys / play_records / launcher_surveys を
        /// 持つ旧 DB が、v0 fast-path で 3 テーブルを DROP し真の v23 へ到達することを検証する。v0 path は migration
        /// chain を通らず CurrentDbVersion を直接 stamp するため、MigrateV22ToV23 を明示適用しないとテーブルを
        /// 残したまま v23 を名乗る穴があった (本テストでその回帰を固定)。
        /// </summary>
        [Fact]
        public void V0FastPath_DropsEventTables_AndReachesV23()
        {
            // v19 形状 (games + 子テーブル) を作ってから user_version=0 へ落とし、撤去対象 3 テーブルを足す。
            // これで v0 path の各 retrofit (arguments / developers FK / play_time CHECK) を通しつつ
            // MigrateV22ToV23 の v0 適用を検証する。
            BuildV19DbWithChildren(_dbPath, playTime: 2);
            using (var c = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                c.Open();
                Exec(c, "CREATE TABLE play_records (id INTEGER PRIMARY KEY AUTOINCREMENT, game_id TEXT, start_time TEXT, end_time TEXT, play_duration INTEGER, player_count INTEGER, FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE)");
                Exec(c, "CREATE TABLE surveys (id INTEGER PRIMARY KEY AUTOINCREMENT, game_id TEXT, rating INTEGER, comment TEXT, created_at TEXT, FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE)");
                Exec(c, "CREATE TABLE launcher_surveys (id INTEGER PRIMARY KEY AUTOINCREMENT, rating INTEGER, favorite_game_id TEXT, comment TEXT, created_at TEXT, FOREIGN KEY(favorite_game_id) REFERENCES games(game_id) ON DELETE SET NULL)");
                Exec(c, "PRAGMA user_version=0"); // versioning 導入前
            }

            new SchemaManager(new DatabaseConnection(_dbPath)).InitializeDatabase();
            SQLiteConnection.ClearAllPools();

            using (var c = new SQLiteConnection($"Data Source={_dbPath};Version=3;"))
            {
                c.Open();
                Assert.Equal((long)new SchemaManager(new DatabaseConnection(_dbPath)).GetTargetDatabaseVersion(),
                    ScalarLong(c, "PRAGMA user_version"));
                Assert.Equal(0L, ScalarLong(c, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('surveys','play_records','launcher_surveys')"));
                Assert.Equal("ok", ScalarString(c, "PRAGMA integrity_check"));
                // 既存データ (games) は保持される。
                Assert.Equal(1L, ScalarLong(c, "SELECT COUNT(*) FROM games"));
            }
        }

        /// <summary>
        /// versioning 前の v19 状態 DB を raw SQL で構築する: `games` は v20 から play_time CHECK を除いた形、
        /// 子テーブルは FK ON DELETE CASCADE。1 game + 2 versions + 1 developer を投入。
        /// </summary>
        private static void BuildV19DbWithChildren(string dbPath, int playTime)
        {
            using (var c = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
            {
                c.Open();
                Exec(c, "PRAGMA foreign_keys=ON");
                Exec(c, @"CREATE TABLE games (
                    game_id TEXT PRIMARY KEY, title TEXT NOT NULL, description TEXT, release_year INTEGER,
                    genre TEXT, min_players INTEGER, max_players INTEGER,
                    difficulty INTEGER CHECK(difficulty BETWEEN 1 AND 3), play_time INTEGER,
                    controller_support INTEGER DEFAULT 0, supported_connection INTEGER DEFAULT 0,
                    thumbnail_path TEXT, background_path TEXT, executable_path TEXT,
                    display_order INTEGER DEFAULT 0, is_visible INTEGER DEFAULT 1,
                    controls TEXT, key_mapping TEXT, arguments TEXT, version TEXT)");
                Exec(c, @"CREATE TABLE game_versions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, game_id TEXT NOT NULL, version TEXT NOT NULL,
                    executable_path TEXT NOT NULL, arguments TEXT, description TEXT, title TEXT, genre TEXT,
                    min_players INTEGER, max_players INTEGER, difficulty INTEGER, play_time INTEGER,
                    controller_support INTEGER DEFAULT 0, supported_connection INTEGER DEFAULT 0,
                    thumbnail_path TEXT, background_path TEXT, update_note TEXT, registered_at TEXT NOT NULL,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE)");
                Exec(c, @"CREATE TABLE developers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, game_id TEXT, last_name TEXT, first_name TEXT,
                    grade TEXT, version_id INTEGER,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE,
                    FOREIGN KEY(version_id) REFERENCES game_versions(id) ON DELETE CASCADE)");

                Exec(c, $"INSERT INTO games (game_id, title, difficulty, play_time, version) VALUES ('g1','Game 1',2,{playTime},'1.1.0')");
                Exec(c, "INSERT INTO game_versions (game_id, version, executable_path, registered_at) VALUES ('g1','1.0.0','v1.0.0/g.exe','2026-01-01 00:00:00')");
                Exec(c, "INSERT INTO game_versions (game_id, version, executable_path, registered_at) VALUES ('g1','1.1.0','v1.1.0/g.exe','2026-01-02 00:00:00')");
                Exec(c, "INSERT INTO developers (game_id, last_name, first_name, grade) VALUES ('g1','山田','太郎','3')");

                Exec(c, "PRAGMA user_version=19");
            }
        }

        private static void Exec(SQLiteConnection c, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, c)) cmd.ExecuteNonQuery();
        }
        private static long ScalarLong(SQLiteConnection c, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, c)) return Convert.ToInt64(cmd.ExecuteScalar());
        }
        private static string ScalarString(SQLiteConnection c, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, c)) { var o = cmd.ExecuteScalar(); return o?.ToString() ?? ""; }
        }
    }
}
