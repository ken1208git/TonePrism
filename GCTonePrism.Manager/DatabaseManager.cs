using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// データベース管理クラス
    /// SQLiteデータベースの初期化、CRUD操作を提供
    /// </summary>
    public class DatabaseManager
    {
        private readonly string connectionString;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public DatabaseManager()
        {
            string dbPath = PathManager.DatabasePath;
            connectionString = $"Data Source={dbPath}";
        }

        /// <summary>
        /// データベースを初期化（テーブル作成）
        /// </summary>
        public void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // gamesテーブルを作成
                CreateGamesTable(connection);

                // developersテーブルを作成
                CreateDevelopersTable(connection);

                // play_recordsテーブルを作成
                CreatePlayRecordsTable(connection);

                // surveysテーブルを作成
                CreateSurveysTable(connection);

                // settingsテーブルを作成
                CreateSettingsTable(connection);

                Console.WriteLine("[DatabaseManager] データベース初期化完了");
            }
        }

        /// <summary>
        /// データベースをリセット（すべてのテーブルを削除して再作成）
        /// 警告: すべてのデータが削除されます（データベース情報とgamesフォルダ内のファイル）
        /// </summary>
        public void ResetDatabase()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // 外部キー制約を無効化
                using (var command = new SQLiteCommand("PRAGMA foreign_keys = OFF", connection))
                {
                    command.ExecuteNonQuery();
                }

                // すべてのテーブルを削除
                var dropTables = new[]
                {
                    "DROP TABLE IF EXISTS surveys",
                    "DROP TABLE IF EXISTS play_records",
                    "DROP TABLE IF EXISTS developers",
                    "DROP TABLE IF EXISTS games",
                    "DROP TABLE IF EXISTS settings"
                };

                foreach (var sql in dropTables)
                {
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                // 外部キー制約を再有効化
                using (var command = new SQLiteCommand("PRAGMA foreign_keys = ON", connection))
                {
                    command.ExecuteNonQuery();
                }

                // gamesフォルダの中身を削除（フォルダ自体は残す）
                string gamesFolder = PathManager.GamesFolder;
                if (System.IO.Directory.Exists(gamesFolder))
                {
                    try
                    {
                        // フォルダ内のすべてのサブフォルダとファイルを削除
                        var subDirectories = System.IO.Directory.GetDirectories(gamesFolder);
                        foreach (var subDir in subDirectories)
                        {
                            System.IO.Directory.Delete(subDir, true);
                        }
                        var files = System.IO.Directory.GetFiles(gamesFolder);
                        foreach (var file in files)
                        {
                            System.IO.File.Delete(file);
                        }
                        Console.WriteLine($"[DatabaseManager] gamesフォルダの中身を削除しました: {gamesFolder}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DatabaseManager] gamesフォルダの中身の削除に失敗しました: {ex.Message}");
                        // エラーが発生しても続行（フォルダが使用中の場合など）
                    }
                }

                // テーブルを再作成
                InitializeDatabase();

                Console.WriteLine("[DatabaseManager] データベースリセット完了");
            }
        }

        /// <summary>
        /// gamesテーブルを作成
        /// </summary>
        private void CreateGamesTable(SQLiteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS games (
                    game_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    release_year INTEGER,
                    genre TEXT,
                    min_players INTEGER,
                    max_players INTEGER,
                    difficulty INTEGER CHECK(difficulty >= 1 AND difficulty <= 3),
                    play_time INTEGER CHECK(play_time >= 1 AND play_time <= 3),
                    controller_support INTEGER DEFAULT 0,
                    thumbnail_path TEXT,
                    background_path TEXT,
                    executable_path TEXT NOT NULL,
                    display_order INTEGER,
                    is_visible INTEGER DEFAULT 1,
                    controls TEXT,
                    key_mapping TEXT
                )";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// developersテーブルを作成
        /// </summary>
        private void CreateDevelopersTable(SQLiteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS developers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT NOT NULL,
                    last_name TEXT NOT NULL,
                    first_name TEXT NOT NULL,
                    grade TEXT,
                    FOREIGN KEY (game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// play_recordsテーブルを作成
        /// </summary>
        private void CreatePlayRecordsTable(SQLiteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS play_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT NOT NULL,
                    play_count INTEGER DEFAULT 0,
                    total_play_time INTEGER DEFAULT 0,
                    last_played_at TEXT,
                    FOREIGN KEY (game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// surveysテーブルを作成
        /// </summary>
        private void CreateSurveysTable(SQLiteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS surveys (
                    id TEXT PRIMARY KEY,
                    game_id TEXT NOT NULL,
                    submitted_at TEXT NOT NULL,
                    responses TEXT,
                    FOREIGN KEY (game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// settingsテーブルを作成
        /// </summary>
        private void CreateSettingsTable(SQLiteConnection connection)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS settings (
                    id INTEGER PRIMARY KEY CHECK(id = 1),
                    color_theme TEXT,
                    launcher_settings TEXT,
                    filter_settings TEXT
                )";

            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }

            // デフォルト設定を挿入（存在しない場合のみ）
            var insertSql = @"
                INSERT OR IGNORE INTO settings (id, color_theme, launcher_settings, filter_settings)
                VALUES (1, '{}', '{}', '{}')";

            using (var command = new SQLiteCommand(insertSql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// データベースが存在するか確認
        /// </summary>
        public bool DatabaseExists()
        {
            return System.IO.File.Exists(PathManager.DatabasePath);
        }

        /// <summary>
        /// テーブルが存在するか確認
        /// </summary>
        public bool TablesExist()
        {
            if (!DatabaseExists()) return false;

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    var sql = "SELECT name FROM sqlite_master WHERE type='table' AND name='games'";
                    using (var command = new SQLiteCommand(sql, connection))
                    {
                        var result = command.ExecuteScalar();
                        return result != null;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        // ==================== ゲーム情報のCRUD操作 ====================

        /// <summary>
        /// すべてのゲーム情報を取得
        /// </summary>
        public List<GameInfo> GetAllGames()
        {
            var games = new List<GameInfo>();

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                var sql = "SELECT * FROM games ORDER BY display_order, title";
                using (var command = new SQLiteCommand(sql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var game = ReadGameFromReader(reader);
                        // 製作者情報を取得
                        game.Developers = GetDevelopersByGameId(connection, game.GameId);
                        games.Add(game);
                    }
                }
            }

            return games;
        }

        /// <summary>
        /// ゲームIDで特定のゲーム情報を取得
        /// </summary>
        public GameInfo GetGameById(string gameId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                var sql = "SELECT * FROM games WHERE game_id = @gameId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@gameId", gameId);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var game = ReadGameFromReader(reader);
                            // 製作者情報を取得
                            game.Developers = GetDevelopersByGameId(connection, game.GameId);
                            return game;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// ゲーム情報を追加
        /// </summary>
        public void AddGame(GameInfo game)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // ゲーム情報を挿入
                        var sql = @"
                            INSERT INTO games (
                                game_id, title, description, release_year, genre,
                                min_players, max_players, difficulty, play_time,
                                controller_support,
                                thumbnail_path, background_path, executable_path,
                                display_order, is_visible, controls, key_mapping
                            ) VALUES (
                                @gameId, @title, @description, @releaseYear, @genre,
                                @minPlayers, @maxPlayers, @difficulty, @playTime,
                                @controllerSupport,
                                @thumbnailPath, @backgroundPath, @executablePath,
                                @displayOrder, @isVisible, @controls, @keyMapping
                            )";

                        using (var command = new SQLiteCommand(sql, connection, transaction))
                        {
                            AddGameParameters(command, game);
                            command.ExecuteNonQuery();
                        }

                        // 製作者情報を挿入
                        if (game.Developers != null)
                        {
                            foreach (var developer in game.Developers)
                            {
                                AddDeveloper(connection, transaction, game.GameId, developer);
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// ゲーム情報を更新
        /// </summary>
        public void UpdateGame(GameInfo game)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // ゲーム情報を更新
                        var sql = @"
                            UPDATE games SET
                                title = @title,
                                description = @description,
                                release_year = @releaseYear,
                                genre = @genre,
                                min_players = @minPlayers,
                                max_players = @maxPlayers,
                                difficulty = @difficulty,
                                play_time = @playTime,
                                controller_support = @controllerSupport,
                                thumbnail_path = @thumbnailPath,
                                background_path = @backgroundPath,
                                executable_path = @executablePath,
                                display_order = @displayOrder,
                                is_visible = @isVisible,
                                controls = @controls,
                                key_mapping = @keyMapping
                            WHERE game_id = @gameId";

                        using (var command = new SQLiteCommand(sql, connection, transaction))
                        {
                            AddGameParameters(command, game);
                            command.ExecuteNonQuery();
                        }

                        // 既存の製作者情報を削除
                        DeleteDevelopersByGameId(connection, transaction, game.GameId);

                        // 新しい製作者情報を挿入
                        if (game.Developers != null)
                        {
                            foreach (var developer in game.Developers)
                            {
                                AddDeveloper(connection, transaction, game.GameId, developer);
                            }
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// ゲーム情報を削除（データベースとgamesフォルダの両方を削除）
        /// </summary>
        public void DeleteGame(string gameId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // FOREIGN KEY制約により、関連するdevelopers、play_records、surveysも自動削除される
                var sql = "DELETE FROM games WHERE game_id = @gameId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@gameId", gameId);
                    command.ExecuteNonQuery();
                }
            }

            // gamesフォルダ内の対応するゲームフォルダを削除
            string gameFolder = PathManager.GetGameFolder(gameId);
            if (System.IO.Directory.Exists(gameFolder))
            {
                try
                {
                    System.IO.Directory.Delete(gameFolder, true);
                    Console.WriteLine($"[DatabaseManager] ゲームフォルダを削除しました: {gameFolder}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DatabaseManager] ゲームフォルダの削除に失敗しました: {ex.Message}");
                    // エラーが発生しても続行（フォルダが使用中の場合など）
                    // ただし、データベースからは既に削除されているため、警告を出すべきかもしれない
                }
            }
        }

        // ==================== 製作者情報の操作 ====================

        /// <summary>
        /// ゲームIDで製作者情報を取得
        /// </summary>
        private List<DeveloperInfo> GetDevelopersByGameId(SQLiteConnection connection, string gameId)
        {
            var developers = new List<DeveloperInfo>();

            var sql = "SELECT * FROM developers WHERE game_id = @gameId";
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@gameId", gameId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        developers.Add(new DeveloperInfo
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("id")),
                            GameId = reader.GetString(reader.GetOrdinal("game_id")),
                            LastName = reader.GetString(reader.GetOrdinal("last_name")),
                            FirstName = reader.GetString(reader.GetOrdinal("first_name")),
                            Grade = reader.IsDBNull(reader.GetOrdinal("grade")) ? null : reader.GetString(reader.GetOrdinal("grade"))
                        });
                    }
                }
            }

            return developers;
        }

        /// <summary>
        /// 製作者情報を追加
        /// </summary>
        private void AddDeveloper(SQLiteConnection connection, SQLiteTransaction transaction, string gameId, DeveloperInfo developer)
        {
            var sql = @"
                INSERT INTO developers (game_id, last_name, first_name, grade)
                VALUES (@gameId, @lastName, @firstName, @grade)";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@gameId", gameId);
                command.Parameters.AddWithValue("@lastName", developer.LastName);
                command.Parameters.AddWithValue("@firstName", developer.FirstName);
                command.Parameters.AddWithValue("@grade", (object)developer.Grade ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// ゲームIDで製作者情報を削除
        /// </summary>
        private void DeleteDevelopersByGameId(SQLiteConnection connection, SQLiteTransaction transaction, string gameId)
        {
            var sql = "DELETE FROM developers WHERE game_id = @gameId";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("@gameId", gameId);
                command.ExecuteNonQuery();
            }
        }

        // ==================== ヘルパーメソッド ====================

        /// <summary>
        /// SqlDataReaderからGameInfoオブジェクトを読み取る
        /// </summary>
        private GameInfo ReadGameFromReader(SQLiteDataReader reader)
        {
            var game = new GameInfo
            {
                GameId = reader.GetString(reader.GetOrdinal("game_id")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                ReleaseYear = reader.IsDBNull(reader.GetOrdinal("release_year")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("release_year")),
                MinPlayers = reader.IsDBNull(reader.GetOrdinal("min_players")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("min_players")),
                MaxPlayers = reader.IsDBNull(reader.GetOrdinal("max_players")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("max_players")),
                Difficulty = reader.IsDBNull(reader.GetOrdinal("difficulty")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("difficulty")),
                PlayTime = reader.IsDBNull(reader.GetOrdinal("play_time")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("play_time")),
                ControllerSupport = reader.GetInt32(reader.GetOrdinal("controller_support")) == 1,
                ThumbnailPath = reader.IsDBNull(reader.GetOrdinal("thumbnail_path")) ? null : reader.GetString(reader.GetOrdinal("thumbnail_path")),
                BackgroundPath = reader.IsDBNull(reader.GetOrdinal("background_path")) ? null : reader.GetString(reader.GetOrdinal("background_path")),
                ExecutablePath = reader.GetString(reader.GetOrdinal("executable_path")),
                DisplayOrder = reader.IsDBNull(reader.GetOrdinal("display_order")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("display_order")),
                IsVisible = reader.GetInt32(reader.GetOrdinal("is_visible")) == 1,
                Controls = reader.IsDBNull(reader.GetOrdinal("controls")) ? null : reader.GetString(reader.GetOrdinal("controls")),
                KeyMapping = reader.IsDBNull(reader.GetOrdinal("key_mapping")) ? null : reader.GetString(reader.GetOrdinal("key_mapping"))
            };

            // ジャンルをJSON形式から配列に変換（簡易実装）
            if (!reader.IsDBNull(reader.GetOrdinal("genre")))
            {
                var genreStr = reader.GetString(reader.GetOrdinal("genre"));
                if (!string.IsNullOrWhiteSpace(genreStr))
                {
                    // JSON形式またはカンマ区切りをサポート（簡易実装）
                    game.Genre = new List<string>(genreStr.Split(','));
                }
            }

            return game;
        }

        /// <summary>
        /// SQLiteCommandにゲーム情報のパラメータを追加
        /// </summary>
        private void AddGameParameters(SQLiteCommand command, GameInfo game)
        {
            command.Parameters.AddWithValue("@gameId", game.GameId);
            command.Parameters.AddWithValue("@title", game.Title);
            command.Parameters.AddWithValue("@description", (object)game.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("@releaseYear", (object)game.ReleaseYear ?? DBNull.Value);
            command.Parameters.AddWithValue("@genre", game.Genre != null && game.Genre.Count > 0 ? string.Join(",", game.Genre) : (object)DBNull.Value);
            command.Parameters.AddWithValue("@minPlayers", (object)game.MinPlayers ?? DBNull.Value);
            command.Parameters.AddWithValue("@maxPlayers", (object)game.MaxPlayers ?? DBNull.Value);
            command.Parameters.AddWithValue("@difficulty", (object)game.Difficulty ?? DBNull.Value);
            command.Parameters.AddWithValue("@playTime", (object)game.PlayTime ?? DBNull.Value);
            command.Parameters.AddWithValue("@controllerSupport", game.ControllerSupport ? 1 : 0);
            command.Parameters.AddWithValue("@thumbnailPath", (object)game.ThumbnailPath ?? DBNull.Value);
            command.Parameters.AddWithValue("@backgroundPath", (object)game.BackgroundPath ?? DBNull.Value);
            command.Parameters.AddWithValue("@executablePath", game.ExecutablePath);
            command.Parameters.AddWithValue("@displayOrder", (object)game.DisplayOrder ?? DBNull.Value);
            command.Parameters.AddWithValue("@isVisible", game.IsVisible ? 1 : 0);
            command.Parameters.AddWithValue("@controls", (object)game.Controls ?? DBNull.Value);
            command.Parameters.AddWithValue("@keyMapping", (object)game.KeyMapping ?? DBNull.Value);
        }
    }
}

