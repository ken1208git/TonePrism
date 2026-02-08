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
        private const int CurrentDbVersion = 1;

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
                                    controller_support INTEGER DEFAULT 0,
                                    thumbnail_path TEXT,
                                    background_path TEXT,
                                    executable_path TEXT,
                                    display_order INTEGER DEFAULT 0,
                                    is_visible INTEGER DEFAULT 1,
                                    controls TEXT,
                                    key_mapping TEXT
                                )";

                            using (var command = new SQLiteCommand(createGamesTable, connection, transaction))
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
                                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                                )";

                            using (var command = new SQLiteCommand(createDevelopersTable, connection, transaction))
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

                            // surveysテーブル作成（アンケート機能用）
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
                            // 既存のデータベースに対して新しいカラムがない場合に備えて確認を行う
                            MigrateDevelopersTable(connection, transaction);

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

                    string query = @"
                        SELECT 
                            game_id, title, description, release_year, genre,
                            min_players, max_players, difficulty, play_time, controller_support,
                            thumbnail_path, background_path, executable_path,
                            display_order, is_visible, controls, key_mapping
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
                                    ThumbnailPath = reader["thumbnail_path"] is DBNull ? null : reader["thumbnail_path"].ToString(),
                                    BackgroundPath = reader["background_path"] is DBNull ? null : reader["background_path"].ToString(),
                                    ExecutablePath = reader["executable_path"] is DBNull ? null : reader["executable_path"].ToString(),
                                    DisplayOrder = reader["display_order"] is DBNull ? (int?)null : Convert.ToInt32(reader["display_order"]),
                                    IsVisible = reader["is_visible"] is DBNull ? true : Convert.ToInt32(reader["is_visible"]) == 1,
                                    Controls = reader["controls"] is DBNull ? null : reader["controls"].ToString(),
                                    KeyMapping = reader["key_mapping"] is DBNull ? null : reader["key_mapping"].ToString()
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
                    
                    string query = "SELECT id, game_id, last_name, first_name, grade FROM developers WHERE game_id = @gameId ORDER BY id ASC";

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
                            min_players, max_players, difficulty, play_time, controller_support,
                            thumbnail_path, background_path, executable_path,
                            display_order, is_visible, controls, key_mapping
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
                                    ThumbnailPath = reader["thumbnail_path"] is DBNull ? null : reader["thumbnail_path"].ToString(),
                                    BackgroundPath = reader["background_path"] is DBNull ? null : reader["background_path"].ToString(),
                                    ExecutablePath = reader["executable_path"] is DBNull ? null : reader["executable_path"].ToString(),
                                    DisplayOrder = reader["display_order"] is DBNull ? (int?)null : Convert.ToInt32(reader["display_order"]),
                                    IsVisible = reader["is_visible"] is DBNull ? true : Convert.ToInt32(reader["is_visible"]) == 1,
                                    Controls = reader["controls"] is DBNull ? null : reader["controls"].ToString(),
                                    KeyMapping = reader["key_mapping"] is DBNull ? null : reader["key_mapping"].ToString()
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
                                    min_players, max_players, difficulty, play_time, controller_support,
                                    thumbnail_path, background_path, executable_path,
                                    display_order, is_visible, controls, key_mapping
                                ) VALUES (
                                    @gameId, @title, @description, @releaseYear, @genre,
                                    @minPlayers, @maxPlayers, @difficulty, @playTime, @controllerSupport,
                                    @thumbnailPath, @backgroundPath, @executablePath,
                                    @displayOrder, @isVisible, @controls, @keyMapping
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
                                    thumbnail_path = @thumbnailPath,
                                    background_path = @backgroundPath,
                                    executable_path = @executablePath,
                                    display_order = @displayOrder,
                                    is_visible = @isVisible,
                                    controls = @controls,
                                    key_mapping = @keyMapping
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
                                command.Parameters.AddWithValue("@thumbnailPath", game.ThumbnailPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@backgroundPath", game.BackgroundPath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@executablePath", game.ExecutablePath ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@displayOrder", game.DisplayOrder ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@isVisible", game.IsVisible ? 1 : 0);
                                command.Parameters.AddWithValue("@controls", game.Controls ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@keyMapping", game.KeyMapping ?? (object)DBNull.Value);

                                command.ExecuteNonQuery();
                            }

                            // 製作者情報の更新（一度削除して追加し直すのが簡単）
                            if (game.Developers != null)
                            {
                                // 既存の製作者情報を削除
                                using (var command = new SQLiteCommand("DELETE FROM developers WHERE game_id = @gameId", connection, transaction))
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
                    // if (currentVersion < 2)
                    // {
                    //    MigrateV1ToV2(connection, migTransaction);
                    //    currentVersion = 2;
                    // }

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
    }
}
