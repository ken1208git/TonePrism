using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.IO;
using GCTonePrism.Manager.Models;
using System.Threading;

namespace GCTonePrism.Manager
{
    public class DatabaseManager
    {
        private string connectionString;
        private string dbPath;

        // 現在のデータベースバージョン
        // 構造変更があるたびにインクリメントする
        private const int CurrentDbVersion = 6;

        public DatabaseManager()
        {
            dbPath = PathManager.DatabasePath;
            connectionString = $"Data Source={dbPath};Version=3;";
        }


        public bool DatabaseExists()
        {
            return File.Exists(dbPath);
        }

        public bool TablesExist()
        {
            if (!DatabaseExists()) return false;

            return ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);
                    
                    // gamesテーブルの存在確認
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

        /// <summary>
        /// WALモードを有効にして接続を開く
        /// </summary>
        private void OpenConnectionWithWalMode(SQLiteConnection connection)
        {
            connection.Open();
            
            // WALモードを有効化（並行アクセス性能向上）
            using (var command = new SQLiteCommand("PRAGMA journal_mode=WAL;", connection))
            {
                command.ExecuteNonQuery();
            }
            
            // 同期モードをNORMALに設定（パフォーマンス向上と安全性のバランス）
            using (var command = new SQLiteCommand("PRAGMA synchronous=NORMAL;", connection))
            {
                command.ExecuteNonQuery();
            }
            
            // 外部キー制約を有効化
            using (var command = new SQLiteCommand("PRAGMA foreign_keys=ON;", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// データベース操作をリトライ付きで実行するヘルパーメソッド
        /// </summary>
        private T ExecuteWithRetry<T>(Func<T> action, int maxRetries = 3, int delayMs = 100)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return action();
                }
                catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Busy || ex.ResultCode == SQLiteErrorCode.Locked)
                {
                    if (i == maxRetries - 1) throw; // 最後の試行で失敗した場合は例外を投げる
                    Thread.Sleep(delayMs);
                }
            }
            return default(T); // ここには到達しないはず
        }

        /// <summary>
        /// データベース操作をリトライ付きで実行するヘルパーメソッド（戻り値なし）
        /// </summary>
        private void ExecuteWithRetry(Action action, int maxRetries = 3, int delayMs = 100)
        {
            ExecuteWithRetry<object>(() => { action(); return null; }, maxRetries, delayMs);
        }

        /// <summary>
        /// 現在のデータベースバージョン設定値を取得（アプリが期待するバージョン）
        /// </summary>
        public int GetTargetDatabaseVersion()
        {
            return CurrentDbVersion;
        }

        /// <summary>
        /// 実際のデータベースファイル内のバージョンを取得
        /// </summary>
        public int GetActualDatabaseVersion()
        {
            return ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);
                    return GetDbVersion(connection);
                }
            });
        }

        public void InitializeDatabase()
        {
            // 既存の接続が残っている可能性があるため、GCを強制実行して解放を試みる
            GC.Collect();
            GC.WaitForPendingFinalizers();

            ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
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
                                // カラムが存在するかチェック
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

                            // game_genresテーブル作成（ジャンル正規化用）
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

                            // surveysテーブル作成（ゲーム個別アンケート用）
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

                            // launcher_surveysテーブル作成（全体アンケート用）
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
                            
                            // settingsテーブル作成（アプリ設定用）
                            string createSettingsTable = @"
                                CREATE TABLE IF NOT EXISTS settings (
                                    key TEXT PRIMARY KEY,
                                    value TEXT
                                )";

                            using (var command = new SQLiteCommand(createSettingsTable, connection, transaction))
                            {
                                command.ExecuteNonQuery();
                            }

                            // developersテーブルのカラム追加について確認
                            MigrateDevelopersTable(connection, transaction);

                            // gamesテーブルのカラム追加について確認
                            Console.WriteLine("[DatabaseManager] Calling MigrateGamesTable...");
                            MigrateGamesTable(connection, transaction);

                            // surveysテーブルのカラム追加について確認
                            MigrateSurveysTable(connection, transaction);

                            // game_versionsテーブルのカラム追加について確認
                            MigrateGameVersionsTable(connection, transaction);

                            // データベースバージョンのチェックとマイグレーション
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
        
        /// <summary>
        /// developersテーブルの構造を確認し、必要なカラムがない場合は追加する
        /// </summary>
        private void MigrateDevelopersTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 現在のカラム情報を取得
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

            // last_nameカラムがない場合
            if (!columns.Contains("last_name"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN last_name TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }

            // first_nameカラムがない場合
            if (!columns.Contains("first_name"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN first_name TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
            
            // gradeカラムがない場合
            if (!columns.Contains("grade"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN grade TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
            
            // nameカラムが存在し、last_name/first_nameが空の場合のデータ移行は
            // 複雑さを避けるため、ここでは行わない（新規実装を優先）
        }

        /// <summary>
        /// gamesテーブルの構造を確認し、必要なカラムがない場合は追加する
        /// </summary>
        private void MigrateGamesTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 現在のカラム情報を取得
            List<string> columns = new List<string>();
            using (var command = new SQLiteCommand("PRAGMA table_info(games)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string colName = reader["name"].ToString();
                        columns.Add(colName);
                        // Console.WriteLine($"[DatabaseManager] Game column found: {colName}");
                    }
                }
            }

            Console.WriteLine($"[DatabaseManager] Current columns in games: {string.Join(", ", columns)}");
            
            // supported_connectionカラムがない場合
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

            // versionカラムがない場合（表示用の選択中バージョン）
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

        /// <summary>
        /// surveysテーブルの構造を確認する（今回はシンプル化のため、追加カラムロジックは削除）
        /// 将来的な拡張のためにメソッド自体は残しておく
        /// </summary>
        private void MigrateSurveysTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 必要に応じてマイグレーションロジックをここに記述
        }

        /// <summary>
        /// game_versionsテーブルの構造を確認し、必要なカラムがない場合は追加する
        /// </summary>
        private void MigrateGameVersionsTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // テーブルが存在しない場合はスキップ
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

            // 現在のカラム情報を取得
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
            
            // argumentsカラムがない場合は追加
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

        public void ResetDatabase()
        {
            // 既存の接続が残っている可能性があるため、GCを強制実行して解放を試みる
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // データベースファイルを削除
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // ファイルがロックされている場合はリトライ
                    Thread.Sleep(500);
                    File.Delete(dbPath);
                }
            }

            // 再初期化
            InitializeDatabase();
        }

        public List<GameInfo> GetAllGames()
        {
            return ExecuteWithRetry(() =>
            {
                var games = new List<GameInfo>();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    // versionカラムを優先して取得。NULLの場合はサブクエリで最新を取得（バックワード互換性）
                    string query = @"
                        SELECT 
                            game_id, title, description, release_year, genre,
                            min_players, max_players, difficulty, play_time, controller_support, supported_connection,
                            thumbnail_path, background_path, executable_path,
                            display_order, is_visible, controls, key_mapping, arguments,
                            COALESCE(version, (SELECT version FROM game_versions WHERE game_versions.game_id = games.game_id ORDER BY id DESC LIMIT 1)) AS display_version
                        FROM games
                        ORDER BY display_order ASC, title ASC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var game = new GameInfo
                                {
                                    GameId = reader["game_id"].ToString(),
                                    Title = reader["title"].ToString(),
                                    Description = reader["description"] is DBNull ? null : reader["description"].ToString(),
                                    ReleaseYear = reader["release_year"] is DBNull ? (int?)null : Convert.ToInt32(reader["release_year"]),
                                    MinPlayers = reader["min_players"] is DBNull ? (int?)null : Convert.ToInt32(reader["min_players"]),
                                    MaxPlayers = reader["max_players"] is DBNull ? (int?)null : Convert.ToInt32(reader["max_players"]),
                                    Difficulty = reader["difficulty"] is DBNull ? (int?)null : Convert.ToInt32(reader["difficulty"]),
                                    PlayTime = reader["play_time"] is DBNull ? (int?)null : Convert.ToInt32(reader["play_time"]),
                                    ControllerSupport = reader["controller_support"] is DBNull ? false : Convert.ToInt32(reader["controller_support"]) == 1,
                                    SupportedConnection = reader["supported_connection"] is DBNull ? 0 : Convert.ToInt32(reader["supported_connection"]),
                                    ThumbnailPath = reader["thumbnail_path"] is DBNull ? null : reader["thumbnail_path"].ToString(),
                                    BackgroundPath = reader["background_path"] is DBNull ? null : reader["background_path"].ToString(),
                                    ExecutablePath = reader["executable_path"] is DBNull ? null : reader["executable_path"].ToString(),
                                    DisplayOrder = reader["display_order"] is DBNull ? (int?)null : Convert.ToInt32(reader["display_order"]),
                                    IsVisible = reader["is_visible"] is DBNull ? true : Convert.ToInt32(reader["is_visible"]) == 1,
                                    Controls = reader["controls"] is DBNull ? null : reader["controls"].ToString(),
                                    KeyMapping = reader["key_mapping"] is DBNull ? null : reader["key_mapping"].ToString(),
                                    Version = reader["display_version"] is DBNull ? null : reader["display_version"].ToString()
                                };

                                // ジャンルの処理
                                string genreStr = reader["genre"] is DBNull ? null : reader["genre"].ToString();
                                if (!string.IsNullOrEmpty(genreStr))
                                {
                                    game.Genre = genreStr.Split(',').Select(g => g.Trim()).ToList();
                                }
                                else
                                {
                                    game.Genre = new List<string>();
                                }

                                game.Arguments = reader["arguments"] is DBNull ? null : reader["arguments"].ToString();

                                // 製作者情報の取得
                                game.Developers = GetDeveloperInfosByGameId(game.GameId);
                                
                                games.Add(game);
                            }
                        }
                    }
                }

                return games;
            });
        }


        
        // 開発者情報の詳細を取得するメソッド
        public List<DeveloperInfo> GetDeveloperInfosByGameId(string gameId)
        {
            return ExecuteWithRetry(() =>
            {
                var developers = new List<DeveloperInfo>();
                
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);
                    
                    string query = "SELECT id, game_id, last_name, first_name, grade FROM developers WHERE game_id = @gameId AND version_id IS NULL ORDER BY id ASC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                developers.Add(new DeveloperInfo
                                {
                                    Id = Convert.ToInt32(reader["id"]),
                                    GameId = reader["game_id"].ToString(),
                                    LastName = reader["last_name"] is DBNull ? "" : reader["last_name"].ToString(),
                                    FirstName = reader["first_name"] is DBNull ? "" : reader["first_name"].ToString(),
                                    Grade = reader["grade"] is DBNull ? "" : reader["grade"].ToString()
                                });
                            }
                        }
                    }
                }

                return developers;
            });
        }

        public List<DeveloperInfo> GetDeveloperInfosByGameIdAndVersionId(string gameId, int versionId, SQLiteConnection connection)
        {
            var developers = new List<DeveloperInfo>();
            
            string query = "SELECT id, game_id, last_name, first_name, grade FROM developers WHERE game_id = @gameId AND version_id = @versionId ORDER BY id ASC";

            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@gameId", gameId);
                command.Parameters.AddWithValue("@versionId", versionId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        developers.Add(new DeveloperInfo
                        {
                            Id = Convert.ToInt32(reader["id"]),
                            GameId = reader["game_id"].ToString(),
                            LastName = reader["last_name"] is DBNull ? "" : reader["last_name"].ToString(),
                            FirstName = reader["first_name"] is DBNull ? "" : reader["first_name"].ToString(),
                            Grade = reader["grade"] is DBNull ? "" : reader["grade"].ToString()
                        });
                    }
                }
            }

            return developers;
        }

        public GameInfo GetGameById(string gameId)
        {
            return ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    string query = @"
                        SELECT 
                            game_id, title, description, release_year, genre,
                            min_players, max_players, difficulty, play_time, controller_support, supported_connection,
                            thumbnail_path, background_path, executable_path,
                            display_order, is_visible, controls, key_mapping, version
                        FROM games
                        WHERE game_id = @gameId";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var game = new GameInfo
                                {
                                    GameId = reader["game_id"].ToString(),
                                    Title = reader["title"].ToString(),
                                    Description = reader["description"] is DBNull ? null : reader["description"].ToString(),
                                    ReleaseYear = reader["release_year"] is DBNull ? (int?)null : Convert.ToInt32(reader["release_year"]),
                                    MinPlayers = reader["min_players"] is DBNull ? (int?)null : Convert.ToInt32(reader["min_players"]),
                                    MaxPlayers = reader["max_players"] is DBNull ? (int?)null : Convert.ToInt32(reader["max_players"]),
                                    Difficulty = reader["difficulty"] is DBNull ? (int?)null : Convert.ToInt32(reader["difficulty"]),
                                    PlayTime = reader["play_time"] is DBNull ? (int?)null : Convert.ToInt32(reader["play_time"]),
                                    ControllerSupport = reader["controller_support"] is DBNull ? false : Convert.ToInt32(reader["controller_support"]) == 1,
                                    SupportedConnection = reader["supported_connection"] is DBNull ? 0 : Convert.ToInt32(reader["supported_connection"]),
                                    ThumbnailPath = reader["thumbnail_path"] is DBNull ? null : reader["thumbnail_path"].ToString(),
                                    BackgroundPath = reader["background_path"] is DBNull ? null : reader["background_path"].ToString(),
                                    ExecutablePath = reader["executable_path"] is DBNull ? null : reader["executable_path"].ToString(),
                                    DisplayOrder = reader["display_order"] is DBNull ? (int?)null : Convert.ToInt32(reader["display_order"]),
                                    IsVisible = reader["is_visible"] is DBNull ? true : Convert.ToInt32(reader["is_visible"]) == 1,
                                    Controls = reader["controls"] is DBNull ? null : reader["controls"].ToString(),
                                    KeyMapping = reader["key_mapping"] is DBNull ? null : reader["key_mapping"].ToString(),
                                    Version = reader["version"] is DBNull ? null : reader["version"].ToString()
                                };

                                // ジャンルの処理
                                string genreStr = reader["genre"] is DBNull ? null : reader["genre"].ToString();
                                if (!string.IsNullOrEmpty(genreStr))
                                {
                                    game.Genre = genreStr.Split(',').Select(g => g.Trim()).ToList();
                                }
                                else
                                {
                                    game.Genre = new List<string>();
                                }

                                // 製作者情報の取得
                                game.Developers = GetDeveloperInfosByGameId(game.GameId);

                                return game;
                            }
                        }
                    }
                }

                return null;
            });
        }

        public int GetMinDisplayOrder()
        {
            return ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);
                    using (var command = new SQLiteCommand("SELECT MIN(display_order) FROM games", connection))
                    {
                        var result = command.ExecuteScalar();
                        return result is DBNull ? 0 : Convert.ToInt32(result);
                    }
                }
            });
        }

        public void AddGame(GameInfo game)
        {
            ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // ゲーム情報の追加
                            string insertGame = @"
                                INSERT INTO games (
                                    game_id, title, description, release_year, genre,
                                    min_players, max_players, difficulty, play_time, controller_support, supported_connection,
                                    thumbnail_path, background_path, executable_path,
                                    display_order, is_visible, controls, key_mapping, version
                                ) VALUES (
                                    @gameId, @title, @description, @releaseYear, @genre,
                                    @minPlayers, @maxPlayers, @difficulty, @playTime, @controllerSupport, @supportedConnection,
                                    @thumbnailPath, @backgroundPath, @executablePath,
                                    @displayOrder, @isVisible, @controls, @keyMapping, @version
                                )";

                            using (var command = new SQLiteCommand(insertGame, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@gameId", game.GameId);
                                command.Parameters.AddWithValue("@title", game.Title);
                                command.Parameters.AddWithValue("@description", game.Description ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@releaseYear", game.ReleaseYear ?? (object)DBNull.Value);

                                // ジャンルをカンマ区切り文字列として保存
                                string genreStr = (game.Genre != null && game.Genre.Count > 0) ? string.Join(",", game.Genre) : null;
                                command.Parameters.AddWithValue("@genre", genreStr ?? (object)DBNull.Value);

                                command.Parameters.AddWithValue("@minPlayers", game.MinPlayers ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@maxPlayers", game.MaxPlayers ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@difficulty", game.Difficulty ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@playTime", game.PlayTime ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@controllerSupport", game.ControllerSupport ? 1 : 0);
                                command.Parameters.AddWithValue("@supportedConnection", game.SupportedConnection);
                                command.Parameters.AddWithValue("@thumbnailPath", game.ThumbnailPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@backgroundPath", game.BackgroundPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@executablePath", game.ExecutablePath ?? (object)DBNull.Value);
                                
                                // DisplayOrderが指定されている場合はそれを使用、そうでなければ末尾に追加
                                if (game.DisplayOrder.HasValue)
                                {
                                    command.Parameters.AddWithValue("@displayOrder", game.DisplayOrder.Value);
                                }
                                else
                                {
                                    // デフォルト動作のSQL（SELECT COALESCE...）をここで再現するのは難しいので、
                                    // 末尾に追加する場合は別途クエリを発行するか、ここで計算する必要がある。
                                    // 簡略化のため、常に値を設定することを推奨するが、
                                    // ここでは一旦、NULLの場合は既存の最大値+1とするロジックを入れる
                                    // しかしパラメータ化クエリ内でサブクエリを使う方が安全
                                    // 上記SQL文内の @displayOrder をサブクエリに置換するのは面倒なので、
                                    // 事前に計算するか、AddGameForm側で設定されていることを前提とする。
                                    // AddGameFormでは設定されているので、ここはパラメータとして渡す。
                                    // 万が一nullの場合は0とする。
                                    command.Parameters.AddWithValue("@displayOrder", 0); 
                                    // 注意: 元のコードの (SELECT COALESCE(MAX(display_order), -1) + 1 FROM games) ロジックは
                                    // AddGameForm側で制御されているため不要になったと判断
                                }
                                
                                command.Parameters.AddWithValue("@isVisible", game.IsVisible ? 1 : 0);
                                command.Parameters.AddWithValue("@controls", game.Controls ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@keyMapping", game.KeyMapping ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@version", game.Version ?? (object)DBNull.Value); // バージョン追加
                                
                                command.ExecuteNonQuery();
                            }

                            // 製作者情報の追加
                            if (game.Developers != null && game.Developers.Count > 0)
                            {
                                string insertDeveloper = @"
                                    INSERT INTO developers (game_id, last_name, first_name, grade)
                                    VALUES (@gameId, @lastName, @firstName, @grade)";

                                foreach (var developer in game.Developers)
                                {
                                    using (var command = new SQLiteCommand(insertDeveloper, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@gameId", game.GameId);
                                        command.Parameters.AddWithValue("@lastName", developer.LastName ?? "");
                                        command.Parameters.AddWithValue("@firstName", developer.FirstName ?? "");
                                        command.Parameters.AddWithValue("@grade", developer.Grade ?? "");
                                        
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }

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

        public void UpdateGame(GameInfo game)
        {
            ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // ゲーム情報の更新
                            string updateGame = @"
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
                                    supported_connection = @supportedConnection,
                                    thumbnail_path = @thumbnailPath,
                                    background_path = @backgroundPath,
                                    executable_path = @executablePath,
                                    display_order = @displayOrder,
                                    is_visible = @isVisible,
                                    controls = @controls,
                                    key_mapping = @keyMapping,
                                    arguments = @arguments,
                                    version = @version
                                WHERE game_id = @gameId";

                            using (var command = new SQLiteCommand(updateGame, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@gameId", game.GameId);
                                command.Parameters.AddWithValue("@title", game.Title);
                                command.Parameters.AddWithValue("@description", game.Description ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@releaseYear", game.ReleaseYear ?? (object)DBNull.Value);
                                
                                // ジャンルをカンマ区切り文字列として保存
                                string genreStr = (game.Genre != null && game.Genre.Count > 0) ? string.Join(",", game.Genre) : null;
                                command.Parameters.AddWithValue("@genre", genreStr ?? (object)DBNull.Value);

                                command.Parameters.AddWithValue("@minPlayers", game.MinPlayers ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@maxPlayers", game.MaxPlayers ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@difficulty", game.Difficulty ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@playTime", game.PlayTime ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@controllerSupport", game.ControllerSupport ? 1 : 0);
                                command.Parameters.AddWithValue("@supportedConnection", game.SupportedConnection);
                                command.Parameters.AddWithValue("@thumbnailPath", game.ThumbnailPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@backgroundPath", game.BackgroundPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@executablePath", game.ExecutablePath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@displayOrder", game.DisplayOrder ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@isVisible", game.IsVisible ? 1 : 0);
                                command.Parameters.AddWithValue("@controls", game.Controls ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@keyMapping", game.KeyMapping ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@arguments", game.Arguments ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@version", game.Version ?? (object)DBNull.Value);

                                command.ExecuteNonQuery();
                            }

                            // 製作者情報の更新（一度削除して追加し直すのが簡単）
                            if (game.Developers != null)
                            {
                                // 既存の製作者情報を削除（バージョン固有のものは残す）
                                using (var command = new SQLiteCommand("DELETE FROM developers WHERE game_id = @gameId AND version_id IS NULL", connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@gameId", game.GameId);
                                    command.ExecuteNonQuery();
                                }

                                // 新しい製作者情報を追加
                                string insertDeveloper = @"
                                    INSERT INTO developers (game_id, last_name, first_name, grade)
                                    VALUES (@gameId, @lastName, @firstName, @grade)";

                                foreach (var developer in game.Developers)
                                {
                                    using (var command = new SQLiteCommand(insertDeveloper, connection, transaction))
                                    {
                                        command.Parameters.AddWithValue("@gameId", game.GameId);
                                        command.Parameters.AddWithValue("@lastName", developer.LastName ?? "");
                                        command.Parameters.AddWithValue("@firstName", developer.FirstName ?? "");
                                        command.Parameters.AddWithValue("@grade", developer.Grade ?? "");
                                        
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }

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

        public void DeleteGame(string gameId)
        {
            ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    // 外部キー制約設定により、games削除時に自動的にdevelopersなども削除されるはずだが、
                    // 念のため明示的に削除してもよい
                    string deleteGame = "DELETE FROM games WHERE game_id = @gameId";

                    using (var command = new SQLiteCommand(deleteGame, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        command.ExecuteNonQuery();
                    }
                }
            });
        }
        
        public static string GetUserFriendlyErrorMessage(SQLiteException ex)
        {
            switch (ex.ResultCode)
            {
                case SQLiteErrorCode.Constraint:
                    if (ex.Message.Contains("UNIQUE constraint failed"))
                        return "ユニーク制約違反です。すでに存在するIDを使用している可能性があります。";
                    return "データベースの制約に違反しています。";
                case SQLiteErrorCode.Locked:
                case SQLiteErrorCode.Busy:
                    return "データベースがロックされています。他のアプリケーションが使用中か確認してください。";
                case SQLiteErrorCode.ReadOnly:
                    return "データベースは読み取り専用です。書き込み権限を確認してください。";
                case SQLiteErrorCode.Corrupt:
                    return "データベースファイルが破損しています。";
                case SQLiteErrorCode.Full:
                    return "ディスク容量が不足しています。";
                default:
                    return $"データベースエラーが発生しました (Code: {ex.ResultCode}): {ex.Message}";
            }
        }

        /// <summary>
        /// データベースのバージョンチェックとマイグレーションを実行
        /// </summary>
        private void CheckAndMigrateDatabase(SQLiteConnection connection, SQLiteTransaction transaction = null)
        {
            int currentVersion = GetDbVersion(connection, transaction);
            Console.WriteLine($"[DatabaseManager] 現在のDBバージョン: {currentVersion}, 最新バージョン: {CurrentDbVersion}");

            // バージョンが0の場合（新規作成時など）、最新バージョンを設定
            if (currentVersion == 0)
            {
                SetDbVersion(connection, CurrentDbVersion, transaction);
                return;
            }

            // マイグレーションが必要な場合
            if (currentVersion < CurrentDbVersion)
            {
                Console.WriteLine($"[DatabaseManager] マイグレーションを開始します: v{currentVersion} -> v{CurrentDbVersion}");
                
                // トランザクションが渡されていない場合は新規作成
                bool localTransaction = (transaction == null);
                SQLiteTransaction migTransaction = transaction;

                // 外部からトランザクションが渡されていない場合のみ、ここで開始
                if (localTransaction)
                {
                    migTransaction = connection.BeginTransaction();
                }

                try
                {
                    // バージョンごとにマイグレーションを実行
                    // 例: v1 -> v2
                    if (currentVersion < 2)
                    {
                       MigrateV1ToV2(connection, migTransaction);
                       currentVersion = 2;
                    }

                    // v2 -> v3
                    if (currentVersion < 3)
                    {
                       MigrateV2ToV3(connection, migTransaction);
                       currentVersion = 3;
                    }

                    // v3 -> v4 (マイグレーション漏れの修正)
                    if (currentVersion < 4)
                    {
                        MigrateV3ToV4(connection, migTransaction);
                        currentVersion = 4;
                    }

                    // v4 -> v5 (1.0.0の説明文をクリア)
                    if (currentVersion < 5)
                    {
                        MigrateV4ToV5(connection, migTransaction);
                        currentVersion = 5;
                    }

                    // v5 -> v6 (update_noteカラム追加)
                    if (currentVersion < 6)
                    {
                        MigrateV5ToV6(connection, migTransaction);
                        currentVersion = 6;
                    }

                    // 最新バージョンに更新
                    SetDbVersion(connection, CurrentDbVersion, migTransaction);

                    // ローカルトランザクションの場合はコミット
                    if (localTransaction)
                    {
                        migTransaction.Commit();
                    }
                    
                    Console.WriteLine("[DatabaseManager] マイグレーションが完了しました");
                }
                catch (Exception ex)
                {
                    // ローカルトランザクションの場合はロールバック
                    if (localTransaction)
                    {
                        migTransaction.Rollback();
                    }
                    
                    Console.WriteLine($"[DatabaseManager] マイグレーションに失敗しました: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// データベースのバージョンを取得
        /// </summary>
        private int GetDbVersion(SQLiteConnection connection, SQLiteTransaction transaction = null)
        {
            using (var command = new SQLiteCommand("PRAGMA user_version", connection, transaction))
            {
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// データベースのバージョンを設定
        /// </summary>
        private void SetDbVersion(SQLiteConnection connection, int version, SQLiteTransaction transaction = null)
        {
            var sql = $"PRAGMA user_version = {version}";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
            Console.WriteLine($"[DatabaseManager] データベースバージョンを {version} に更新しました");
        }

        /// <summary>
        /// v1からv2へのマイグレーション（アンケート機能の刷新）
        /// surveysテーブルの再作成（カラム構成変更のため）
        /// launcher_surveysテーブルの作成はInitializeDatabaseで行われるが、念のためここでもCREATE文を実行するか、ドロップして再作成を促す
        /// </summary>
        private void MigrateV1ToV2(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V1 -> V2");
            
            // 旧surveysテーブルを削除（データはリセット）
            string dropSurveys = "DROP TABLE IF EXISTS surveys";
            using (var command = new SQLiteCommand(dropSurveys, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // launcher_surveysも念のため削除
            string dropLauncherSurveys = "DROP TABLE IF EXISTS launcher_surveys";
            using (var command = new SQLiteCommand(dropLauncherSurveys, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // 新しいスキーマで再作成
            // surveysテーブル作成（ゲーム個別アンケート用）
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

            // launcher_surveysテーブル作成（全体アンケート用）
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

            // game_genresテーブル作成とデータ移行
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

            // 既存のgamesテーブルからジャンルデータを移行
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
                            // カンマ区切りまたはJSON配列を想定してパース
                            // 簡易的にカンマ区切りとして処理（JSONの場合も含む文字が含まれるため、クリーニングが必要かもしれないが、
                            // 現状のデータ登録ロジックに合わせてカンマで分割）
                            // 既にJSONで入っている場合はパースが必要だが、ここではシンプルにカンマ分割とトリムを行う
                            // 注: 本来は完全なJSONパースが望ましいが、C#標準ライブラリだけで簡易に行う
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

            // gamesテーブルにsupported_connectionカラムを追加
            // 注: カラムが存在しない場合のみ追加したいが、SQliteのALTER TABLE ADD COLUMNはIF NOT EXISTSをサポートしていない場合がある
            // しかし、MigrateV1ToV2は一度しか呼ばれない前提なので、単純に実行してエラーをキャッチするか、PRAGMAで確認する
            // ここではPRAGMA確認を行う
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

        /// <summary>
        /// v2からv3へのマイグレーション（詳細なバージョン管理対応）
        /// game_versionsへのカラム追加、developersへのversion_id追加、既存データの移行
        /// </summary>
        private void MigrateV2ToV3(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V2 -> V3");

            // 1. game_versionsテーブルにカラムを追加
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

            // 2. developersテーブルにversion_idカラムを追加
            try {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN version_id INTEGER", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            } catch (Exception ex) {
                Console.WriteLine($"[DatabaseManager] Warning adding version_id to developers: {ex.Message}");
            }

            // 3. 既存のバージョンデータに親ゲームの情報をコピー
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

        /// <summary>
        /// v3からv4へのマイグレーション
        /// V2->V3移行時に漏れていた「バージョン情報が存在しないゲーム」に対して初期バージョンを作成する
        /// </summary>
        private void MigrateV3ToV4(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Console.WriteLine("[DatabaseManager] Executing migration V3 -> V4 (Fixing missing versions)");

            // バージョン情報が存在しないゲームを取得
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
                 // 初期バージョン(1.0.0)を作成
                 // gamesテーブルの情報をそのままコピー
                 // descriptionはv2ではUpdateNoteとして使われていたが、ユーザー要望によりGame Descriptionとして扱うようになったため、
                 // ここではgames.descriptionをgame_versions.description(Game Description)にコピーする。
                 // Update Noteとしては空になるか、既存の説明が入る。
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

        /// <summary>
        /// v4からv5へのマイグレーション
        /// バージョン1.0.0の説明文（更新内容）を空にする（コピペだと紛らわしいため）
        /// </summary>
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


        /// <summary>
        /// v5からv6へのマイグレーション
        /// game_versionsテーブルにupdate_noteカラムを追加
        /// </summary>
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
        /// <summary>
        /// ゲームバージョンを追加する
        /// </summary>
        public void AddGameVersion(GameVersion version)
        {
            ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    string query = @"
                        INSERT INTO game_versions (
                            game_id, version, executable_path, arguments, description, update_note,
                            title, genre, min_players, max_players, difficulty, play_time, 
                            controller_support, supported_connection, thumbnail_path, background_path,
                            registered_at
                        ) VALUES (
                            @gameId, @version, @executablePath, @arguments, @description, @updateNote,
                            @title, @genre, @minPlayers, @maxPlayers, @difficulty, @playTime, 
                            @controllerSupport, @supportedConnection, @thumbnailPath, @backgroundPath,
                            @registeredAt
                        );
                        SELECT last_insert_rowid();";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", version.GameId);
                        command.Parameters.AddWithValue("@version", version.Version);
                        command.Parameters.AddWithValue("@executablePath", version.ExecutablePath);
                        command.Parameters.AddWithValue("@arguments", version.Arguments ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@description", version.Description ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@updateNote", version.UpdateNote ?? (object)DBNull.Value);
                        
                        command.Parameters.AddWithValue("@title", version.Title);
                        // ジャンルをカンマ区切り文字列として保存
                        string genreStr = (version.Genre != null && version.Genre.Count > 0) ? string.Join(",", version.Genre) : null;
                        command.Parameters.AddWithValue("@genre", genreStr ?? (object)DBNull.Value);

                        command.Parameters.AddWithValue("@minPlayers", version.MinPlayers ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@maxPlayers", version.MaxPlayers ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@difficulty", version.Difficulty ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@playTime", version.PlayTime ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@controllerSupport", version.ControllerSupport ? 1 : 0);
                        command.Parameters.AddWithValue("@supportedConnection", version.SupportedConnection);
                        command.Parameters.AddWithValue("@thumbnailPath", version.ThumbnailPath ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@backgroundPath", version.BackgroundPath ?? (object)DBNull.Value);

                        command.Parameters.AddWithValue("@registeredAt", version.RegisteredAt.ToString("yyyy-MM-dd HH:mm:ss"));

                        // バージョンIDを取得して設定
                        long versionId = (long)command.ExecuteScalar();
                        version.Id = (int)versionId;

                        // 製作者情報の追加（バージョン紐付け）
                        if (version.Developers != null && version.Developers.Count > 0)
                        {
                            string insertDeveloper = @"
                                INSERT INTO developers (game_id, last_name, first_name, grade, version_id)
                                VALUES (@gameId, @lastName, @firstName, @grade, @versionId)";

                            foreach (var developer in version.Developers)
                            {
                                using (var devCmd = new SQLiteCommand(insertDeveloper, connection))
                                {
                                    devCmd.Parameters.AddWithValue("@gameId", version.GameId);
                                    devCmd.Parameters.AddWithValue("@lastName", developer.LastName);
                                    devCmd.Parameters.AddWithValue("@firstName", developer.FirstName);
                                    devCmd.Parameters.AddWithValue("@grade", developer.Grade);
                                    devCmd.Parameters.AddWithValue("@versionId", version.Id);
                                    devCmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
            });

        }

        /// <summary>
        /// ゲームバージョン情報を更新する
        /// </summary>
        public void UpdateGameVersion(GameVersion version)
        {
            ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string updateSql = @"
                                UPDATE game_versions SET 
                                    version = @version,
                                    executable_path = @executablePath,
                                    arguments = @arguments,
                                    description = @description,
                                    update_note = @updateNote,
                                    title = @title,
                                    genre = @genre,
                                    min_players = @minPlayers,
                                    max_players = @maxPlayers,
                                    difficulty = @difficulty,
                                    play_time = @playTime,
                                    controller_support = @controllerSupport,
                                    supported_connection = @supportedConnection,
                                    thumbnail_path = @thumbnailPath,
                                    background_path = @backgroundPath,
                                    registered_at = @registeredAt
                                WHERE id = @id";

                            using (var command = new SQLiteCommand(updateSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@version", version.Version);
                                command.Parameters.AddWithValue("@executablePath", version.ExecutablePath);
                                command.Parameters.AddWithValue("@arguments", version.Arguments ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@description", version.Description ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@updateNote", version.UpdateNote ?? (object)DBNull.Value);
                                
                                command.Parameters.AddWithValue("@title", version.Title);
                                string genreStr = (version.Genre != null && version.Genre.Count > 0) ? string.Join(",", version.Genre) : null;
                                command.Parameters.AddWithValue("@genre", genreStr ?? (object)DBNull.Value);

                                command.Parameters.AddWithValue("@minPlayers", version.MinPlayers ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@maxPlayers", version.MaxPlayers ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@difficulty", version.Difficulty ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@playTime", version.PlayTime ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@controllerSupport", version.ControllerSupport ? 1 : 0);
                                command.Parameters.AddWithValue("@supportedConnection", version.SupportedConnection);
                                command.Parameters.AddWithValue("@thumbnailPath", version.ThumbnailPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@backgroundPath", version.BackgroundPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@registeredAt", version.RegisteredAt.ToString("yyyy-MM-dd HH:mm:ss"));
                                
                                command.Parameters.AddWithValue("@id", version.Id);

                                command.ExecuteNonQuery();
                            }

                            // 製作者情報の更新（削除して再登録）
                            string deleteDevs = "DELETE FROM developers WHERE version_id = @versionId";
                            using (var cmd = new SQLiteCommand(deleteDevs, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@versionId", version.Id);
                                cmd.ExecuteNonQuery();
                            }

                            if (version.Developers != null && version.Developers.Count > 0)
                            {
                                string insertDeveloper = @"
                                    INSERT INTO developers (game_id, last_name, first_name, grade, version_id)
                                    VALUES (@gameId, @lastName, @firstName, @grade, @versionId)";

                                foreach (var developer in version.Developers)
                                {
                                    using (var devCmd = new SQLiteCommand(insertDeveloper, connection, transaction))
                                    {
                                        devCmd.Parameters.AddWithValue("@gameId", version.GameId);
                                        devCmd.Parameters.AddWithValue("@lastName", developer.LastName);
                                        devCmd.Parameters.AddWithValue("@firstName", developer.FirstName);
                                        devCmd.Parameters.AddWithValue("@grade", developer.Grade);
                                        devCmd.Parameters.AddWithValue("@versionId", version.Id);
                                        devCmd.ExecuteNonQuery();
                                    }
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
            });
        }

        /// <summary>
        /// ゲームの全バージョン履歴を取得する
        /// </summary>
        public List<GameVersion> GetGameVersions(string gameId)
        {
            return ExecuteWithRetry(() =>
            {
                var versions = new List<GameVersion>();

                using (var connection = new SQLiteConnection(connectionString))
                {
                    OpenConnectionWithWalMode(connection);

                    string query = @"
                        SELECT *
                        FROM game_versions
                        WHERE game_id = @gameId
                        ORDER BY registered_at DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var version = new GameVersion
                                {
                                    Id = Convert.ToInt32(reader["id"]),
                                    GameId = reader["game_id"].ToString(),
                                    Version = reader["version"].ToString(),
                                    ExecutablePath = reader["executable_path"].ToString(),
                                    Arguments = reader["arguments"] is DBNull ? null : reader["arguments"].ToString(),
                                    Description = reader["description"] is DBNull ? null : reader["description"].ToString(),
                                    UpdateNote = reader.GetSchemaTable().Select("ColumnName = 'update_note'").Length > 0 && !(reader["update_note"] is DBNull) ? reader["update_note"].ToString() : null,
                                    
                                    Title = reader["title"] is DBNull ? null : reader["title"].ToString(),
                                    MinPlayers = reader["min_players"] is DBNull ? (int?)null : Convert.ToInt32(reader["min_players"]),
                                    MaxPlayers = reader["max_players"] is DBNull ? (int?)null : Convert.ToInt32(reader["max_players"]),
                                    Difficulty = reader["difficulty"] is DBNull ? (int?)null : Convert.ToInt32(reader["difficulty"]),
                                    PlayTime = reader["play_time"] is DBNull ? (int?)null : Convert.ToInt32(reader["play_time"]),
                                    ControllerSupport = reader["controller_support"] is DBNull ? false : Convert.ToInt32(reader["controller_support"]) == 1,
                                    SupportedConnection = reader["supported_connection"] is DBNull ? 0 : Convert.ToInt32(reader["supported_connection"]),
                                    ThumbnailPath = reader["thumbnail_path"] is DBNull ? null : reader["thumbnail_path"].ToString(),
                                    BackgroundPath = reader["background_path"] is DBNull ? null : reader["background_path"].ToString(),
                                    
                                    RegisteredAt = DateTime.Parse(reader["registered_at"].ToString())
                                };

                                // ジャンルの処理
                                string genreStr = reader["genre"] is DBNull ? null : reader["genre"].ToString();
                                if (!string.IsNullOrEmpty(genreStr))
                                {
                                    version.Genre = genreStr.Split(',').Select(g => g.Trim()).ToList();
                                }
                                
                                // バージョン別製作者情報の取得
                                version.Developers = GetDeveloperInfosByGameIdAndVersionId(version.GameId, version.Id, connection);

                                versions.Add(version);


                            }
                        }
                    }
                }

                return versions;
            });
        }

        /// <summary>
        /// 最新のバージョンを取得する
        /// </summary>
        public GameVersion GetLatestVersion(string gameId)
        {
            var versions = GetGameVersions(gameId);
            return versions.FirstOrDefault();
        }
    }
}
