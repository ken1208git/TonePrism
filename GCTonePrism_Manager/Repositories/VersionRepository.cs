using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Repositories
{
    /// <summary>
    /// ゲームバージョンのCRUD操作
    /// </summary>
    public class VersionRepository
    {
        private readonly DatabaseConnection _conn;
        private readonly DeveloperRepository _devRepo;

        public VersionRepository(DatabaseConnection conn, DeveloperRepository devRepo)
        {
            _conn = conn;
            _devRepo = devRepo;
        }

        public void Add(GameVersion version)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

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

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            using (var command = new SQLiteCommand(query, connection, transaction))
                            {
                                SetVersionParameters(command, version);
                                command.Parameters.AddWithValue("@registeredAt", version.RegisteredAt.ToString("yyyy-MM-dd HH:mm:ss"));

                                long versionId = (long)command.ExecuteScalar();
                                version.Id = (int)versionId;
                            }

                            InsertVersionDevelopers(connection, transaction, version);

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

        public void Update(GameVersion version)
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
                                SetVersionParameters(command, version);
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

                            InsertVersionDevelopers(connection, transaction, version);

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

        public List<GameVersion> GetByGameId(string gameId)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var versions = new List<GameVersion>();

                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

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

                                string genreStr = reader["genre"] is DBNull ? null : reader["genre"].ToString();
                                if (!string.IsNullOrEmpty(genreStr))
                                {
                                    version.Genre = genreStr.Split(',').Select(g => g.Trim()).ToList();
                                }

                                version.Developers = _devRepo.GetByGameIdAndVersionId(version.GameId, version.Id, connection);

                                versions.Add(version);
                            }
                        }
                    }
                }

                return versions;
            });
        }

        public GameVersion GetLatest(string gameId)
        {
            var versions = GetByGameId(gameId);
            return versions.FirstOrDefault();
        }

        private void SetVersionParameters(SQLiteCommand command, GameVersion version)
        {
            command.Parameters.AddWithValue("@gameId", version.GameId);
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
        }

        private void InsertVersionDevelopers(SQLiteConnection connection, SQLiteTransaction transaction, GameVersion version)
        {
            if (version.Developers == null || version.Developers.Count == 0) return;

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
    }
}
