using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// テーブル作成・スキーママイグレーション・バージョン管理
    /// </summary>
    public class SchemaManager
    {
        private readonly DatabaseConnection _conn;

        // 現在のデータベースバージョン
        // 構造変更があるたびにインクリメントする
        // v11: SPEC v1.5.1 (2026-03-28) で変更された surveys / play_records スキーマの drift 修正（v0.8.1）
        private const int CurrentDbVersion = 11;

        public SchemaManager(DatabaseConnection conn)
        {
            _conn = conn;
        }

        public int GetTargetDatabaseVersion()
        {
            return CurrentDbVersion;
        }

        public int GetActualDatabaseVersion()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);
                    return GetDbVersion(connection);
                }
            });
        }

        public bool TablesExist()
        {
            if (!_conn.DatabaseExists()) return false;

            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

                    using (var command = new SQLiteCommand(
                        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='games'",
                        connection))
                    {
                        long count = (long)command.ExecuteScalar();
                        return count > 0;
                    }
                }
            });
        }

        public void InitializeDatabase()
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            CreateTables(connection, transaction);
                            MigrateDevelopersTable(connection, transaction);
                            Console.WriteLine("[DatabaseManager] Calling MigrateGamesTable...");
                            MigrateGamesTable(connection, transaction);
                            MigrateSurveysTable(connection, transaction);
                            MigrateGameVersionsTable(connection, transaction);
                            CheckAndMigrateDatabase(connection, transaction);

                            // 全マイグレーション完了後にスキーマ整合性を検証する。
                            // drift があった場合でも例外は投げず警告ログのみ。
                            // （AGENTS.md "Database Schema Management" 参照）
                            VerifySchema(connection, transaction);

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            });
        }

        public void ResetDatabase()
        {
            string dbPath = _conn.DbPath;
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                    File.Delete(dbPath);
                }
            }

            InitializeDatabase();
        }

        private void CreateTables(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // gamesテーブル作成
            string createGamesTable = @"
                CREATE TABLE IF NOT EXISTS games (
                    game_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    release_year INTEGER,
                    genre TEXT,
                    min_players INTEGER,
                    max_players INTEGER,
                    difficulty INTEGER CHECK(difficulty BETWEEN 1 AND 3),
                    play_time INTEGER,
                    controller_support INTEGER DEFAULT 0,
                    supported_connection INTEGER DEFAULT 0,
                    thumbnail_path TEXT,
                    background_path TEXT,
                    executable_path TEXT,
                    display_order INTEGER DEFAULT 0,
                    is_visible INTEGER DEFAULT 1,
                    controls TEXT,
                    key_mapping TEXT,
                    arguments TEXT,
                    version TEXT
                )";

            using (var command = new SQLiteCommand(createGamesTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // 既存のgamesテーブルにargumentsカラムがない場合は追加（マイグレーション）
            try
            {
                using (var command = new SQLiteCommand("PRAGMA table_info(games)", connection, transaction))
                {
                    bool hasArgumentsColumn = false;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader["name"].ToString() == "arguments")
                            {
                                hasArgumentsColumn = true;
                                break;
                            }
                        }
                    }

                    if (!hasArgumentsColumn)
                    {
                        using (var alterCommand = new SQLiteCommand("ALTER TABLE games ADD COLUMN arguments TEXT", connection, transaction))
                        {
                            alterCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Migration failed (arguments): {ex.Message}");
            }

            // game_versionsテーブル作成
            string createGameVersionsTable = @"
                CREATE TABLE IF NOT EXISTS game_versions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT NOT NULL,
                    version TEXT NOT NULL,
                    executable_path TEXT NOT NULL,
                    arguments TEXT,
                    description TEXT,
                    title TEXT,
                    genre TEXT,
                    min_players INTEGER,
                    max_players INTEGER,
                    difficulty INTEGER,
                    play_time INTEGER,
                    controller_support INTEGER DEFAULT 0,
                    supported_connection INTEGER DEFAULT 0,
                    thumbnail_path TEXT,
                    background_path TEXT,
                    update_note TEXT,
                    registered_at TEXT NOT NULL,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createGameVersionsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // developersテーブル作成
            string createDevelopersTable = @"
                CREATE TABLE IF NOT EXISTS developers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    last_name TEXT,
                    first_name TEXT,
                    grade TEXT,
                    version_id INTEGER,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createDevelopersTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // game_genresテーブル作成
            string createGameGenresTable = @"
                CREATE TABLE IF NOT EXISTS game_genres (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    genre TEXT,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createGameGenresTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // play_recordsテーブル作成（MigrateV10ToV11 でも再利用するため helper メソッド化）
            CreatePlayRecordsTable(connection, transaction);

            // surveysテーブル作成（MigrateV10ToV11 でも再利用するため helper メソッド化）
            CreateSurveysTable(connection, transaction);

            // launcher_surveysテーブル作成
            string createLauncherSurveysTable = @"
                CREATE TABLE IF NOT EXISTS launcher_surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    favorite_game_id TEXT,
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(favorite_game_id) REFERENCES games(game_id) ON DELETE SET NULL
                )";

            using (var command = new SQLiteCommand(createLauncherSurveysTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // settingsテーブル作成
            string createSettingsTable = @"
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT
                )";

            using (var command = new SQLiteCommand(createSettingsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // settings テーブルが古いスキーマ（id / color_theme / launcher_settings / filter_settings の単一行型）の場合、
            // KVS スキーマへ移行する。SPECIFICATION 1.3.1 (2026-02-08) で KVS 化されたが、
            // 既存DB向けマイグレーションが実装されていなかったため Manager v0.8.0 でフォローする。
            EnsureSettingsTableIsKvsSchema(connection, transaction);

            // store_sectionsテーブル作成
            string createStoreSectionsTable = @"
                CREATE TABLE IF NOT EXISTS store_sections (
                    section_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    section_type INTEGER DEFAULT 0,
                    section_source TEXT DEFAULT 'manual',
                    display_order INTEGER DEFAULT 0,
                    max_display_count INTEGER DEFAULT 5,
                    is_visible INTEGER DEFAULT 1
                )";

            using (var command = new SQLiteCommand(createStoreSectionsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // store_section_gamesテーブル作成
            string createStoreSectionGamesTable = @"
                CREATE TABLE IF NOT EXISTS store_section_games (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    section_id INTEGER NOT NULL,
                    game_id TEXT NOT NULL,
                    display_order INTEGER DEFAULT 0,
                    display_text TEXT DEFAULT '',
                    FOREIGN KEY(section_id) REFERENCES store_sections(section_id) ON DELETE CASCADE,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE,
                    UNIQUE(section_id, game_id)
                )";

            using (var command = new SQLiteCommand(createStoreSectionGamesTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // backup_logテーブル作成（v9 で追加）
            CreateBackupLogTable(connection, transaction);

            // 新規DB向けにバックアップ関連の設定デフォルト値を投入
            InsertBackupDefaults(connection, transaction);
        }

        /// <summary>
        /// settings テーブルが古いスキーマの場合、KVS スキーマへ移行する。
        /// 古いスキーマ（id / color_theme / launcher_settings / filter_settings 等）には
        /// 実コードからの読み書きが存在しなかったため、データロスは発生しない。
        /// 念のため `settings_legacy_v8_or_earlier` としてリネームしてから新規作成する。
        /// </summary>
        private void EnsureSettingsTableIsKvsSchema(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 1. settings テーブルが存在するか
            bool settingsExists;
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='settings'",
                connection, transaction))
            {
                long count = (long)cmd.ExecuteScalar();
                settingsExists = count > 0;
            }
            if (!settingsExists)
            {
                // 直前の CREATE TABLE IF NOT EXISTS で必ず作成されているはずだが、念のため。
                return;
            }

            // 2. 'key' カラムがあるか
            bool hasKeyColumn = false;
            using (var cmd = new SQLiteCommand("PRAGMA table_info(settings)", connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == "key")
                    {
                        hasKeyColumn = true;
                        break;
                    }
                }
            }

            if (hasKeyColumn) return;

            // 3. 古いスキーマ → リネームして新規作成
            Console.WriteLine("[DatabaseManager] settings テーブルが古いスキーマです。KVS方式に移行します。");

            // 既に legacy テーブルが残っていたら削除（過去に失敗した移行の残骸を掃除）
            using (var cmd = new SQLiteCommand(
                "DROP TABLE IF EXISTS settings_legacy_v8_or_earlier", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "ALTER TABLE settings RENAME TO settings_legacy_v8_or_earlier", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT)", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine("[DatabaseManager] settings テーブルを KVS 方式で再作成しました。" +
                              "旧データは settings_legacy_v8_or_earlier に保管されています。");
        }

        /// <summary>
        /// backup_log テーブルを作成（IF NOT EXISTS で冪等）。
        /// trigger_type は 'manual' / 'auto' / 'safety' の3種。
        /// </summary>
        private void CreateBackupLogTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS backup_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at INTEGER NOT NULL,
                    completed_at INTEGER,
                    pc_name TEXT NOT NULL,
                    file_path TEXT,
                    file_size_bytes INTEGER,
                    status TEXT NOT NULL CHECK (status IN ('in_progress','success','failed')),
                    error_message TEXT,
                    trigger_type TEXT NOT NULL CHECK (trigger_type IN ('manual','auto','safety'))
                )";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// settings テーブルにバックアップ関連のデフォルトキーを INSERT OR IGNORE で投入
        /// </summary>
        private void InsertBackupDefaults(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string[][] defaults = new[]
            {
                new[] { "last_backup_at", "0" },
                new[] { "backup_destination_path", "" },
                new[] { "backup_auto_interval_hours", "24" },
                new[] { "backup_retention_count", "30" }
            };

            foreach (var kv in defaults)
            {
                using (var command = new SQLiteCommand(
                    "INSERT OR IGNORE INTO settings (key, value) VALUES (@key, @value)",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@key", kv[0]);
                    command.Parameters.AddWithValue("@value", kv[1]);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void MigrateDevelopersTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            List<string> columns = new List<string>();
            using (var command = new SQLiteCommand("PRAGMA table_info(developers)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString());
                    }
                }
            }

            if (!columns.Contains("last_name"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN last_name TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (!columns.Contains("first_name"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN first_name TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (!columns.Contains("grade"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN grade TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void MigrateGamesTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            List<string> columns = new List<string>();
            using (var command = new SQLiteCommand("PRAGMA table_info(games)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string colName = reader["name"].ToString();
                        columns.Add(colName);
                    }
                }
            }

            Console.WriteLine($"[DatabaseManager] Current columns in games: {string.Join(", ", columns)}");

            if (!columns.Contains("supported_connection"))
            {
                Console.WriteLine("[DatabaseManager] 'supported_connection' column missing. Adding...");
                using (var command = new SQLiteCommand("ALTER TABLE games ADD COLUMN supported_connection INTEGER DEFAULT 0", connection, transaction))
                {
                    command.ExecuteNonQuery();
                    Console.WriteLine("[DatabaseManager] 'supported_connection' column added successfully.");
                }
            }
            else
            {
                Console.WriteLine("[DatabaseManager] 'supported_connection' column already exists.");
            }

            if (!columns.Contains("version"))
            {
                Console.WriteLine("[DatabaseManager] 'version' column missing. Adding...");
                using (var command = new SQLiteCommand("ALTER TABLE games ADD COLUMN version TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                    Console.WriteLine("[DatabaseManager] 'version' column added successfully.");
                }
            }
        }

        private void MigrateSurveysTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 将来的な拡張のためにメソッドを残す
        }

        private void MigrateGameVersionsTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            using (var checkCommand = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='game_versions'",
                connection, transaction))
            {
                var result = checkCommand.ExecuteScalar();
                if (result == null)
                {
                    Console.WriteLine("[DatabaseManager] game_versions table does not exist. Skipping migration.");
                    return;
                }
            }

            List<string> columns = new List<string>();
            using (var command = new SQLiteCommand("PRAGMA table_info(game_versions)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string colName = reader["name"].ToString();
                        columns.Add(colName);
                    }
                }
            }

            Console.WriteLine($"[DatabaseManager] Current columns in game_versions: {string.Join(", ", columns)}");

            if (!columns.Contains("arguments"))
            {
                Console.WriteLine("[DatabaseManager] 'arguments' column missing in game_versions. Adding...");
                using (var command = new SQLiteCommand("ALTER TABLE game_versions ADD COLUMN arguments TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                    Console.WriteLine("[DatabaseManager] 'arguments' column added to game_versions successfully.");
                }
            }
            else
            {
                Console.WriteLine("[DatabaseManager] 'arguments' column already exists in game_versions.");
            }
        }

        private void CheckAndMigrateDatabase(SQLiteConnection connection, SQLiteTransaction transaction = null)
        {
            int currentVersion = GetDbVersion(connection, transaction);
            Console.WriteLine($"[DatabaseManager] 現在のDBバージョン: {currentVersion}, 最新バージョン: {CurrentDbVersion}");

            if (currentVersion == 0)
            {
                SetDbVersion(connection, CurrentDbVersion, transaction);
                return;
            }

            if (currentVersion < CurrentDbVersion)
            {
                Console.WriteLine($"[DatabaseManager] マイグレーションを開始します: v{currentVersion} -> v{CurrentDbVersion}");

                bool localTransaction = (transaction == null);
                SQLiteTransaction migTransaction = transaction;

                if (localTransaction)
                {
                    migTransaction = connection.BeginTransaction();
                }

                try
                {
                    if (currentVersion < 2)
                    {
                        MigrateV1ToV2(connection, migTransaction);
                        currentVersion = 2;
                    }

                    if (currentVersion < 3)
                    {
                        MigrateV2ToV3(connection, migTransaction);
                        currentVersion = 3;
                    }

                    if (currentVersion < 4)
                    {
                        MigrateV3ToV4(connection, migTransaction);
                        currentVersion = 4;
                    }

                    if (currentVersion < 5)
                    {
                        MigrateV4ToV5(connection, migTransaction);
                        currentVersion = 5;
                    }

                    if (currentVersion < 6)
                    {
                        MigrateV5ToV6(connection, migTransaction);
                        currentVersion = 6;
                    }

                    if (currentVersion < 7)
                    {
                        MigrateV6ToV7(connection, migTransaction);
                        currentVersion = 7;
                    }

                    if (currentVersion < 8)
                    {
                        MigrateV7ToV8(connection, migTransaction);
                        currentVersion = 8;
                    }

                    if (currentVersion < 9)
                    {
                        MigrateV8ToV9(connection, migTransaction);
                        currentVersion = 9;
                    }

                    if (currentVersion < 10)
                    {
                        MigrateV9ToV10(connection, migTransaction);
                        currentVersion = 10;
                    }

                    if (currentVersion < 11)
                    {
                        MigrateV10ToV11(connection, migTransaction);
                        currentVersion = 11;
                    }

                    SetDbVersion(connection, CurrentDbVersion, migTransaction);

                    if (localTransaction)
                    {
                        migTransaction.Commit();
                    }

                    Console.WriteLine("[DatabaseManager] マイグレーションが完了しました");
                }
                catch (Exception ex)
                {
                    if (localTransaction)
                    {
                        migTransaction.Rollback();
                    }

                    Console.WriteLine($"[DatabaseManager] マイグレーションに失敗しました: {ex.Message}");
                    throw;
                }
            }
        }

        private int GetDbVersion(SQLiteConnection connection, SQLiteTransaction transaction = null)
        {
            using (var command = new SQLiteCommand("PRAGMA user_version", connection, transaction))
            {
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result);
            }
        }

        private void SetDbVersion(SQLiteConnection connection, int version, SQLiteTransaction transaction = null)
        {
            var sql = $"PRAGMA user_version = {version}";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
            Console.WriteLine($"[DatabaseManager] データベースバージョンを {version} に更新しました");
        }

        private void MigrateV1ToV2(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V1 -> V2");

            string dropSurveys = "DROP TABLE IF EXISTS surveys";
            using (var command = new SQLiteCommand(dropSurveys, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string dropLauncherSurveys = "DROP TABLE IF EXISTS launcher_surveys";
            using (var command = new SQLiteCommand(dropLauncherSurveys, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createSurveysTable = @"
                CREATE TABLE IF NOT EXISTS surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createSurveysTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createLauncherSurveysTable = @"
                CREATE TABLE IF NOT EXISTS launcher_surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    favorite_game_id TEXT,
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(favorite_game_id) REFERENCES games(game_id) ON DELETE SET NULL
                )";

            using (var command = new SQLiteCommand(createLauncherSurveysTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createGameGenresTable = @"
                CREATE TABLE IF NOT EXISTS game_genres (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    genre TEXT,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createGameGenresTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            Console.WriteLine("[DatabaseManager] Migrating genres from games table to game_genres table...");
            string selectGames = "SELECT game_id, genre FROM games";
            using (var command = new SQLiteCommand(selectGames, connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string gameId = reader["game_id"].ToString();
                        string genreStr = reader["genre"] is DBNull ? "" : reader["genre"].ToString();

                        if (!string.IsNullOrEmpty(genreStr))
                        {
                            var genres = genreStr.Split(new[] { ',', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(g => g.Trim())
                                                 .Where(g => !string.IsNullOrEmpty(g) && g != ",");

                            foreach (var genre in genres)
                            {
                                string insertGenre = "INSERT INTO game_genres (game_id, genre) VALUES (@gameId, @genre)";
                                using (var insertCmd = new SQLiteCommand(insertGenre, connection, transaction))
                                {
                                    insertCmd.Parameters.AddWithValue("@gameId", gameId);
                                    insertCmd.Parameters.AddWithValue("@genre", genre);
                                    insertCmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
            }

            bool hasSupportedConnection = false;
            using (var command = new SQLiteCommand("PRAGMA table_info(games)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader["name"].ToString() == "supported_connection")
                        {
                            hasSupportedConnection = true;
                            break;
                        }
                    }
                }
            }

            if (!hasSupportedConnection)
            {
                string addColumn = "ALTER TABLE games ADD COLUMN supported_connection INTEGER DEFAULT 0";
                using (var command = new SQLiteCommand(addColumn, connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void MigrateV2ToV3(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V2 -> V3");

            string[] newColumns = {
                "title TEXT", "genre TEXT",
                "min_players INTEGER", "max_players INTEGER",
                "difficulty INTEGER", "play_time INTEGER",
                "controller_support INTEGER DEFAULT 0", "supported_connection INTEGER DEFAULT 0",
                "thumbnail_path TEXT", "background_path TEXT"
            };

            foreach (var col in newColumns)
            {
                try {
                    using (var command = new SQLiteCommand($"ALTER TABLE game_versions ADD COLUMN {col}", connection, transaction))
                    {
                        command.ExecuteNonQuery();
                    }
                } catch (Exception ex) {
                    Console.WriteLine($"[DatabaseManager] Warning adding column to game_versions: {ex.Message}");
                }
            }

            try {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN version_id INTEGER", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            } catch (Exception ex) {
                Console.WriteLine($"[DatabaseManager] Warning adding version_id to developers: {ex.Message}");
            }

            var versionsToUpdate = new List<dynamic>();
            using (var command = new SQLiteCommand("SELECT id, game_id FROM game_versions", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        versionsToUpdate.Add(new { Id = Convert.ToInt32(reader["id"]), GameId = reader["game_id"].ToString() });
                    }
                }
            }

            foreach (var v in versionsToUpdate)
            {
                string getGameSql = "SELECT * FROM games WHERE game_id = @gameId";
                using (var cmd = new SQLiteCommand(getGameSql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@gameId", v.GameId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string updateSql = @"
                                UPDATE game_versions SET
                                    title = @title, genre = @genre,
                                    min_players = @minPlayers, max_players = @maxPlayers,
                                    difficulty = @difficulty, play_time = @playTime,
                                    controller_support = @controllerSupport, supported_connection = @supportedConnection,
                                    thumbnail_path = @thumbnailPath, background_path = @backgroundPath
                                WHERE id = @id";

                            using (var updateCmd = new SQLiteCommand(updateSql, connection, transaction))
                            {
                                updateCmd.Parameters.AddWithValue("@title", reader["title"]);
                                updateCmd.Parameters.AddWithValue("@genre", reader["genre"]);
                                updateCmd.Parameters.AddWithValue("@minPlayers", reader["min_players"]);
                                updateCmd.Parameters.AddWithValue("@maxPlayers", reader["max_players"]);
                                updateCmd.Parameters.AddWithValue("@difficulty", reader["difficulty"]);
                                updateCmd.Parameters.AddWithValue("@playTime", reader["play_time"]);
                                updateCmd.Parameters.AddWithValue("@controllerSupport", reader["controller_support"]);
                                updateCmd.Parameters.AddWithValue("@supportedConnection", reader["supported_connection"]);
                                updateCmd.Parameters.AddWithValue("@thumbnailPath", reader["thumbnail_path"]);
                                updateCmd.Parameters.AddWithValue("@backgroundPath", reader["background_path"]);
                                updateCmd.Parameters.AddWithValue("@id", v.Id);
                                updateCmd.ExecuteNonQuery();
                            }

                            CopyDevelopersToVersion(connection, transaction, v.GameId, v.Id);
                        }
                    }
                }
            }

            Console.WriteLine("[DatabaseManager] Migration V2 -> V3 completed.");
        }

        private void MigrateV3ToV4(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V3 -> V4 (Fixing missing versions)");

            var gamesWithoutVersions = new List<string>();
            string findOrphanedGames = @"
                SELECT g.game_id
                FROM games g
                LEFT JOIN game_versions v ON g.game_id = v.game_id
                WHERE v.id IS NULL";

            using (var command = new SQLiteCommand(findOrphanedGames, connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    gamesWithoutVersions.Add(reader["game_id"].ToString());
                }
            }

            if (gamesWithoutVersions.Count == 0)
            {
                Console.WriteLine("[DatabaseManager] No orphaned games found. Skipping fix.");
                return;
            }

            Console.WriteLine($"[DatabaseManager] Found {gamesWithoutVersions.Count} games without versions. Creating default 1.0.0 versions...");

            foreach (string gameId in gamesWithoutVersions)
            {
                string createVersionSql = @"
                    INSERT INTO game_versions (
                        game_id, version, executable_path,
                        title, genre, min_players, max_players,
                        difficulty, play_time, controller_support, supported_connection,
                        thumbnail_path, background_path, registered_at, description
                    )
                    SELECT
                        game_id, '1.0.0', executable_path,
                        title, genre, min_players, max_players,
                        difficulty, play_time, controller_support, supported_connection,
                        thumbnail_path, background_path, CURRENT_TIMESTAMP, NULL
                    FROM games
                    WHERE game_id = @gameId";

                long newVersionId;
                using (var cmd = new SQLiteCommand(createVersionSql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@gameId", gameId);
                    cmd.ExecuteNonQuery();
                    newVersionId = connection.LastInsertRowId;
                }

                CopyDevelopersToVersion(connection, transaction, gameId, (int)newVersionId);
            }
        }

        private void MigrateV4ToV5(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V4 -> V5 (Clearing description for v1.0.0)");

            string clearDescriptionSql = @"
                UPDATE game_versions
                SET description = NULL
                WHERE version = '1.0.0'";

            using (var command = new SQLiteCommand(clearDescriptionSql, connection, transaction))
            {
                int rows = command.ExecuteNonQuery();
                Console.WriteLine($"[DatabaseManager] Cleared description for {rows} version records (v1.0.0).");
            }
        }

        private void MigrateV5ToV6(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V5 -> V6 (Adding update_note column)");
            string sql = "ALTER TABLE game_versions ADD COLUMN update_note TEXT";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private void MigrateV6ToV7(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V6 -> V7 (Adding store_sections and store_section_games tables)");

            string createStoreSectionsTable = @"
                CREATE TABLE IF NOT EXISTS store_sections (
                    section_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    section_type INTEGER DEFAULT 0,
                    section_source TEXT DEFAULT 'manual',
                    display_order INTEGER DEFAULT 0,
                    max_display_count INTEGER DEFAULT 5,
                    is_visible INTEGER DEFAULT 1
                )";

            using (var command = new SQLiteCommand(createStoreSectionsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createStoreSectionGamesTable = @"
                CREATE TABLE IF NOT EXISTS store_section_games (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    section_id INTEGER NOT NULL,
                    game_id TEXT NOT NULL,
                    display_order INTEGER DEFAULT 0,
                    FOREIGN KEY(section_id) REFERENCES store_sections(section_id) ON DELETE CASCADE,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE,
                    UNIQUE(section_id, game_id)
                )";

            using (var command = new SQLiteCommand(createStoreSectionGamesTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private void MigrateV7ToV8(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V7 -> V8 (Adding display_text to store_section_games)");

            try
            {
                string sql = "ALTER TABLE store_section_games ADD COLUMN display_text TEXT DEFAULT ''";
                using (var command = new SQLiteCommand(sql, connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // カラムが既に存在する場合はスキップ
                Console.WriteLine($"[DatabaseManager] Warning adding display_text: {ex.Message}");
            }
        }

        private void MigrateV8ToV9(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V8 -> V9 (Adding backup_log table and backup-related settings)");

            // backup_log テーブル作成（CreateTables 側でも IF NOT EXISTS で作成されるが、明示的に呼ぶ）
            CreateBackupLogTable(connection, transaction);

            // バックアップ関連の設定デフォルトを投入
            InsertBackupDefaults(connection, transaction);

            Console.WriteLine("[DatabaseManager] Migration V8 -> V9 completed.");
        }

        private void MigrateV9ToV10(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V9 -> V10 (Extending backup_log.trigger_type CHECK to allow 'safety')");

            // SQLite の CHECK 制約は ALTER TABLE で変更できないため、テーブルを作り直す。
            // 既存行は trigger_type が 'manual' / 'auto' のみなので新CHECKに違反しない。
            string createNew = @"
                CREATE TABLE backup_log_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at INTEGER NOT NULL,
                    completed_at INTEGER,
                    pc_name TEXT NOT NULL,
                    file_path TEXT,
                    file_size_bytes INTEGER,
                    status TEXT NOT NULL CHECK (status IN ('in_progress','success','failed')),
                    error_message TEXT,
                    trigger_type TEXT NOT NULL CHECK (trigger_type IN ('manual','auto','safety'))
                )";
            using (var cmd = new SQLiteCommand(createNew, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // データを丸ごとコピー（id を維持するため列を明示）
            using (var cmd = new SQLiteCommand(
                "INSERT INTO backup_log_new (id, started_at, completed_at, pc_name, file_path, " +
                "file_size_bytes, status, error_message, trigger_type) " +
                "SELECT id, started_at, completed_at, pc_name, file_path, " +
                "file_size_bytes, status, error_message, trigger_type FROM backup_log",
                connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand("DROP TABLE backup_log", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "ALTER TABLE backup_log_new RENAME TO backup_log", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine("[DatabaseManager] Migration V9 -> V10 completed.");
        }

        /// <summary>
        /// V10 -> V11: surveys / play_records スキーマ drift 修正
        /// SPEC v1.5.1 (2026-03-28) で surveys（JSON 形式 → ★評価+コメント）、
        /// play_records（累計方式 → イベントログ方式）に変更されたが、対応する
        /// マイグレーションが書かれていなかったため、CREATE TABLE IF NOT EXISTS の
        /// 仕様により旧スキーマのテーブルが温存されていた。本マイグレーションで修正。
        ///
        /// データがあるテーブルは破壊しないため、空テーブルのみ DROP & CREATE する。
        /// データがある場合は警告ログのみ出して手動対応に委ねる（本プロジェクトの
        /// 実環境では空テーブルのみ確認済み）。
        /// </summary>
        private void MigrateV10ToV11(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V10 -> V11 (Fix surveys/play_records schema drift from SPEC v1.5.1)");

            FixSurveysSchemaDrift(connection, transaction);
            FixPlayRecordsSchemaDrift(connection, transaction);

            Console.WriteLine("[DatabaseManager] Migration V10 -> V11 completed.");
        }

        /// <summary>
        /// surveys テーブルが旧 JSON 形式スキーマ（submitted_at / responses 列を持つ）の場合、
        /// 新スキーマ（rating / comment / created_at）へ修正する。
        /// データが残存している場合は例外を投げる（Codex P1 指摘 "Avoid marking DB v11 when drift migration is skipped" 対応）。
        /// </summary>
        private void FixSurveysSchemaDrift(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 旧スキーマ判定: 'submitted_at' 列が存在するか
            bool isOldSchema = TableHasColumn(connection, transaction, "surveys", "submitted_at");

            if (!isOldSchema)
            {
                Console.WriteLine("[DatabaseManager] surveys は新スキーマです。マイグレーション不要。");
                return;
            }

            long rowCount = GetTableRowCount(connection, transaction, "surveys");
            Console.WriteLine($"[DatabaseManager] surveys に旧スキーマを検出 (行数: {rowCount})");

            if (rowCount > 0)
            {
                // データを失わずに旧 JSON 形式 → 新 ★評価+コメント形式 へ自動変換するロジックは
                // 未実装（旧 responses JSON のスキーマが不定でリスクが高いため）。
                // 例外を投げてマイグレーション全体を rollback し、user_version を 10 のまま保持する。
                // これにより次回起動時にも migration が再試行される。
                // ユーザーは tools/sqlite3/sqlite3.exe で旧データを確認・退避してから手動移行すること。
                throw new InvalidOperationException(
                    $"surveys テーブルに旧スキーマ（{rowCount} 行のデータ）が残存しています。" +
                    "データ損失防止のため自動マイグレーションを中止しました。" +
                    "tools/sqlite3/sqlite3.exe で旧データを確認・退避してから手動で新スキーマへ移行してください。" +
                    "user_version は 10 のまま保持されるため、対応後の再起動でマイグレーションが再試行されます。");
            }

            using (var cmd = new SQLiteCommand("DROP TABLE surveys", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            CreateSurveysTable(connection, transaction);
            Console.WriteLine("[DatabaseManager] surveys を新スキーマで再作成しました。");
        }

        /// <summary>
        /// play_records テーブルが旧累計方式スキーマ（play_count / total_play_time 列を持つ）の場合、
        /// 新イベントログ方式スキーマ（start_time / end_time / play_duration / player_count）へ修正する。
        /// データが残存している場合は例外を投げる（Codex P1 指摘 "Avoid marking DB v11 when drift migration is skipped" 対応）。
        /// </summary>
        private void FixPlayRecordsSchemaDrift(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 旧スキーマ判定: 'play_count' 列が存在するか
            bool isOldSchema = TableHasColumn(connection, transaction, "play_records", "play_count");

            if (!isOldSchema)
            {
                Console.WriteLine("[DatabaseManager] play_records は新スキーマです。マイグレーション不要。");
                return;
            }

            long rowCount = GetTableRowCount(connection, transaction, "play_records");
            Console.WriteLine($"[DatabaseManager] play_records に旧スキーマを検出 (行数: {rowCount})");

            if (rowCount > 0)
            {
                // 旧累計方式（1ゲーム1行で play_count / total_play_time を累計）→
                // 新イベントログ方式（1プレイ1行で start_time / end_time / play_duration）の
                // 自動変換は元情報が失われているため不可能（累計値から個別プレイの時刻は復元できない）。
                // 例外を投げてマイグレーション全体を rollback し、user_version を 10 のまま保持する。
                // これにより次回起動時にも migration が再試行される。
                // ユーザーは tools/sqlite3/sqlite3.exe で累計値を退避してから手動移行すること。
                throw new InvalidOperationException(
                    $"play_records テーブルに旧累計方式スキーマ（{rowCount} 行のデータ）が残存しています。" +
                    "累計値から個別プレイ記録は復元できないため自動マイグレーションを中止しました。" +
                    "tools/sqlite3/sqlite3.exe で累計値を退避してから手動で新スキーマへ移行してください。" +
                    "user_version は 10 のまま保持されるため、対応後の再起動でマイグレーションが再試行されます。");
            }

            using (var cmd = new SQLiteCommand("DROP TABLE play_records", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            CreatePlayRecordsTable(connection, transaction);
            Console.WriteLine("[DatabaseManager] play_records を新スキーマで再作成しました。");
        }

        /// <summary>
        /// surveys テーブル作成（CreateTables / MigrateV10ToV11 共通）
        /// </summary>
        private static void CreateSurveysTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";
            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// play_records テーブル作成（CreateTables / MigrateV10ToV11 共通）
        /// </summary>
        private static void CreatePlayRecordsTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS play_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    start_time TEXT,
                    end_time TEXT,
                    play_duration INTEGER,
                    player_count INTEGER,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";
            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 指定テーブルに指定列が存在するかチェック（PRAGMA table_info 経由）
        /// </summary>
        private static bool TableHasColumn(SQLiteConnection connection, SQLiteTransaction transaction, string tableName, string columnName)
        {
            using (var cmd = new SQLiteCommand($"PRAGMA table_info({tableName})", connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == columnName) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 指定テーブルの行数を取得（COUNT(*)）
        /// </summary>
        private static long GetTableRowCount(SQLiteConnection connection, SQLiteTransaction transaction, string tableName)
        {
            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM {tableName}", connection, transaction))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private void CopyDevelopersToVersion(SQLiteConnection connection, SQLiteTransaction transaction, string gameId, int versionId)
        {
            string insertSql = @"
                INSERT INTO developers (game_id, last_name, first_name, grade, version_id)
                SELECT game_id, last_name, first_name, grade, @versionId
                FROM developers
                WHERE game_id = @gameId AND version_id IS NULL";

            using (var command = new SQLiteCommand(insertSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@gameId", gameId);
                command.Parameters.AddWithValue("@versionId", versionId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 各テーブルが持つべき列名一覧（VerifySchema で使用）。
        /// SchemaManager.CreateTables() および各 MigrateVxToVy で作る最終形と一致させること。
        /// スキーマ変更時はこの定義も同時に更新する（AGENTS.md "Database Schema Management" 参照）。
        /// </summary>
        private static readonly Dictionary<string, string[]> ExpectedSchema = new Dictionary<string, string[]>
        {
            { "games", new[] { "game_id", "title", "description", "release_year", "genre", "min_players", "max_players", "difficulty", "play_time", "controller_support", "supported_connection", "thumbnail_path", "background_path", "executable_path", "display_order", "is_visible", "controls", "key_mapping", "arguments", "version" } },
            { "game_versions", new[] { "id", "game_id", "version", "executable_path", "arguments", "description", "title", "genre", "min_players", "max_players", "difficulty", "play_time", "controller_support", "supported_connection", "thumbnail_path", "background_path", "update_note", "registered_at" } },
            { "developers", new[] { "id", "game_id", "last_name", "first_name", "grade", "version_id" } },
            { "game_genres", new[] { "id", "game_id", "genre" } },
            { "play_records", new[] { "id", "game_id", "start_time", "end_time", "play_duration", "player_count" } },
            { "surveys", new[] { "id", "game_id", "rating", "comment", "created_at" } },
            { "launcher_surveys", new[] { "id", "rating", "favorite_game_id", "comment", "created_at" } },
            { "settings", new[] { "key", "value" } },
            { "store_sections", new[] { "section_id", "title", "section_type", "section_source", "display_order", "max_display_count", "is_visible" } },
            { "store_section_games", new[] { "id", "section_id", "game_id", "display_order", "display_text" } },
            { "backup_log", new[] { "id", "started_at", "completed_at", "pc_name", "file_path", "file_size_bytes", "status", "error_message", "trigger_type" } },
        };

        /// <summary>
        /// 全テーブルのスキーマが ExpectedSchema と一致するか検証し、不一致があればログ出力する。
        /// CreateTables() / マイグレーション完了後に呼び出すことを想定（InitializeDatabase 末尾）。
        /// drift があった場合でも例外は投げず、警告ログのみ。アプリ動作はそのまま継続する。
        /// </summary>
        /// <returns>すべてのテーブルが期待通り = true、1 つでも drift があれば false</returns>
        private bool VerifySchema(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            int driftCount = 0;
            foreach (var pair in ExpectedSchema)
            {
                if (!VerifyTableColumns(connection, transaction, pair.Key, pair.Value))
                {
                    driftCount++;
                }
            }

            if (driftCount > 0)
            {
                Console.WriteLine($"[VerifySchema] {driftCount} 個のテーブルでスキーマ drift を検出しました。AGENTS.md の Database Schema Management セクションを参照して対応してください。");
                return false;
            }

            Console.WriteLine($"[VerifySchema] 全 {ExpectedSchema.Count} テーブルのスキーマ整合性 OK");
            return true;
        }

        /// <summary>
        /// 指定テーブルの列名一覧が期待値と一致するか検証する。
        /// 不足列・余分列があればログ出力する。
        /// </summary>
        private static bool VerifyTableColumns(SQLiteConnection connection, SQLiteTransaction transaction, string tableName, string[] expectedColumns)
        {
            var actualColumns = new HashSet<string>();
            using (var cmd = new SQLiteCommand($"PRAGMA table_info({tableName})", connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    actualColumns.Add(reader["name"].ToString());
                }
            }

            if (actualColumns.Count == 0)
            {
                Console.WriteLine($"[VerifySchema] WARNING: テーブル '{tableName}' が存在しません。");
                return false;
            }

            var expectedSet = new HashSet<string>(expectedColumns);
            var missing = new List<string>();
            foreach (var col in expectedColumns)
            {
                if (!actualColumns.Contains(col)) missing.Add(col);
            }
            var extra = new List<string>();
            foreach (var col in actualColumns)
            {
                if (!expectedSet.Contains(col)) extra.Add(col);
            }

            if (missing.Count == 0 && extra.Count == 0)
            {
                return true;
            }

            Console.WriteLine($"[VerifySchema] WARNING: テーブル '{tableName}' のスキーマが期待値と一致しません");
            if (missing.Count > 0)
            {
                Console.WriteLine($"  期待されるが存在しない列: {string.Join(", ", missing)}");
            }
            if (extra.Count > 0)
            {
                Console.WriteLine($"  期待されない余分な列: {string.Join(", ", extra)}");
            }
            return false;
        }
    }
}
