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
        private const int CurrentDbVersion = 6;

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
                    difficulty INTEGER CHECK(difficulty BETWEEN 1 AND 5),
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
                    arguments TEXT
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

            // play_recordsテーブル作成
            string createPlayRecordsTable = @"
                CREATE TABLE IF NOT EXISTS play_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    start_time TEXT,
                    end_time TEXT,
                    play_duration INTEGER,
                    player_count INTEGER,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createPlayRecordsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // surveysテーブル作成
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
    }
}
