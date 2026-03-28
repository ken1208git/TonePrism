using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Repositories
{
    /// <summary>
    /// ゲーム情報のCRUD操作
    /// </summary>
    public class GameRepository
    {
        private readonly DatabaseConnection _conn;
        private readonly DeveloperRepository _devRepo;

        public GameRepository(DatabaseConnection conn, DeveloperRepository devRepo)
        {
            _conn = conn;
            _devRepo = devRepo;
        }

        public List<GameInfo> GetAll()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var games = new List<GameInfo>();

                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

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
                                var game = ReadGameInfo(reader, "display_version");
                                game.Developers = _devRepo.GetByGameId(game.GameId);
                                games.Add(game);
                            }
                        }
                    }
                }

                return games;
            });
        }

        public GameInfo GetById(string gameId)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

                    string query = @"
                        SELECT
                            game_id, title, description, release_year, genre,
                            min_players, max_players, difficulty, play_time, controller_support, supported_connection,
                            thumbnail_path, background_path, executable_path,
                            display_order, is_visible, controls, key_mapping, arguments, version
                        FROM games
                        WHERE game_id = @gameId";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var game = ReadGameInfo(reader, "version");
                                game.Developers = _devRepo.GetByGameId(game.GameId);
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
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);
                    using (var command = new SQLiteCommand("SELECT MIN(display_order) FROM games", connection))
                    {
                        var result = command.ExecuteScalar();
                        return result is DBNull ? 0 : Convert.ToInt32(result);
                    }
                }
            });
        }

        public void Add(GameInfo game)
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
                            string insertGame = @"
                                INSERT INTO games (
                                    game_id, title, description, release_year, genre,
                                    min_players, max_players, difficulty, play_time, controller_support, supported_connection,
                                    thumbnail_path, background_path, executable_path,
                                    display_order, is_visible, controls, key_mapping, arguments, version
                                ) VALUES (
                                    @gameId, @title, @description, @releaseYear, @genre,
                                    @minPlayers, @maxPlayers, @difficulty, @playTime, @controllerSupport, @supportedConnection,
                                    @thumbnailPath, @backgroundPath, @executablePath,
                                    @displayOrder, @isVisible, @controls, @keyMapping, @arguments, @version
                                )";

                            using (var command = new SQLiteCommand(insertGame, connection, transaction))
                            {
                                SetGameParameters(command, game);
                                command.ExecuteNonQuery();
                            }

                            InsertDevelopers(connection, transaction, game.GameId, game.Developers);

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

        public void Update(GameInfo game)
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
                                SetGameParameters(command, game);
                                command.ExecuteNonQuery();
                            }

                            if (game.Developers != null)
                            {
                                using (var command = new SQLiteCommand("DELETE FROM developers WHERE game_id = @gameId AND version_id IS NULL", connection, transaction))
                                {
                                    command.Parameters.AddWithValue("@gameId", game.GameId);
                                    command.ExecuteNonQuery();
                                }

                                InsertDevelopers(connection, transaction, game.GameId, game.Developers);
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

        public void Delete(string gameId)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

                    string deleteGame = "DELETE FROM games WHERE game_id = @gameId";

                    using (var command = new SQLiteCommand(deleteGame, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        command.ExecuteNonQuery();
                    }
                }
            });
        }

        public void UpdateGameId(string oldId, string newId)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

                    // 重複チェック（トランザクション前）
                    using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM games WHERE game_id = @newId", connection))
                    {
                        cmd.Parameters.AddWithValue("@newId", newId);
                        long count = (long)cmd.ExecuteScalar();
                        if (count > 0)
                            throw new InvalidOperationException($"ゲームID「{newId}」は既に使用されています。");
                    }

                    // PRAGMA foreign_keys はトランザクション外でのみ変更可能
                    using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = OFF", connection))
                        cmd.ExecuteNonQuery();

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 子テーブルのgame_idを更新
                            string[] childTables = { "game_versions", "developers", "game_genres", "play_records", "surveys", "store_section_games" };
                            foreach (var table in childTables)
                            {
                                using (var cmd = new SQLiteCommand($"UPDATE {table} SET game_id = @newId WHERE game_id = @oldId", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@newId", newId);
                                    cmd.Parameters.AddWithValue("@oldId", oldId);
                                    cmd.ExecuteNonQuery();
                                }
                            }

                            // launcher_surveys の favorite_game_id を更新
                            using (var cmd = new SQLiteCommand("UPDATE launcher_surveys SET favorite_game_id = @newId WHERE favorite_game_id = @oldId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@newId", newId);
                                cmd.Parameters.AddWithValue("@oldId", oldId);
                                cmd.ExecuteNonQuery();
                            }

                            // メインテーブルの主キーを更新
                            using (var cmd = new SQLiteCommand("UPDATE games SET game_id = @newId WHERE game_id = @oldId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@newId", newId);
                                cmd.Parameters.AddWithValue("@oldId", oldId);
                                cmd.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }

                    // 外部キー制約を再有効化
                    using (var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON", connection))
                        cmd.ExecuteNonQuery();
                }
            });
        }

        private GameInfo ReadGameInfo(SQLiteDataReader reader, string versionColumnName)
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
                Version = reader[versionColumnName] is DBNull ? null : reader[versionColumnName].ToString()
            };

            string genreStr = reader["genre"] is DBNull ? null : reader["genre"].ToString();
            if (!string.IsNullOrEmpty(genreStr))
            {
                game.Genre = genreStr.Split(',').Select(g => g.Trim()).ToList();
            }
            else
            {
                game.Genre = new List<string>();
            }

            // GetAll uses display_version alias which doesn't include arguments
            try
            {
                game.Arguments = reader["arguments"] is DBNull ? null : reader["arguments"].ToString();
            }
            catch { }

            return game;
        }

        private void SetGameParameters(SQLiteCommand command, GameInfo game)
        {
            command.Parameters.AddWithValue("@gameId", game.GameId);
            command.Parameters.AddWithValue("@title", game.Title);
            command.Parameters.AddWithValue("@description", game.Description ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@releaseYear", game.ReleaseYear ?? (object)DBNull.Value);

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

            if (game.DisplayOrder.HasValue)
            {
                command.Parameters.AddWithValue("@displayOrder", game.DisplayOrder.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@displayOrder", 0);
            }

            command.Parameters.AddWithValue("@isVisible", game.IsVisible ? 1 : 0);
            command.Parameters.AddWithValue("@controls", game.Controls ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@keyMapping", game.KeyMapping ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@arguments", game.Arguments ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@version", game.Version ?? (object)DBNull.Value);
        }

        private void InsertDevelopers(SQLiteConnection connection, SQLiteTransaction transaction, string gameId, List<DeveloperInfo> developers)
        {
            if (developers == null || developers.Count == 0) return;

            string insertDeveloper = @"
                INSERT INTO developers (game_id, last_name, first_name, grade)
                VALUES (@gameId, @lastName, @firstName, @grade)";

            foreach (var developer in developers)
            {
                using (var command = new SQLiteCommand(insertDeveloper, connection, transaction))
                {
                    command.Parameters.AddWithValue("@gameId", gameId);
                    command.Parameters.AddWithValue("@lastName", developer.LastName ?? "");
                    command.Parameters.AddWithValue("@firstName", developer.FirstName ?? "");
                    command.Parameters.AddWithValue("@grade", developer.Grade ?? "");

                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
