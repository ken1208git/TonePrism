using System;
using System.Collections.Generic;
using System.Data.SQLite;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Repositories
{
    /// <summary>
    /// 開発者情報のCRUD操作
    /// </summary>
    public class DeveloperRepository
    {
        private readonly DatabaseConnection _conn;

        public DeveloperRepository(DatabaseConnection conn)
        {
            _conn = conn;
        }

        public List<DeveloperInfo> GetByGameId(string gameId)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var developers = new List<DeveloperInfo>();

                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithWalMode(connection);

                    string query = "SELECT id, game_id, last_name, first_name, grade FROM developers WHERE game_id = @gameId AND version_id IS NULL ORDER BY id ASC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                developers.Add(ReadDeveloperInfo(reader));
                            }
                        }
                    }
                }

                return developers;
            });
        }

        public List<DeveloperInfo> GetByGameIdAndVersionId(string gameId, int versionId, SQLiteConnection connection)
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
                        developers.Add(ReadDeveloperInfo(reader));
                    }
                }
            }

            return developers;
        }

        private DeveloperInfo ReadDeveloperInfo(SQLiteDataReader reader)
        {
            return new DeveloperInfo
            {
                Id = Convert.ToInt32(reader["id"]),
                GameId = reader["game_id"].ToString(),
                LastName = reader["last_name"] is DBNull ? "" : reader["last_name"].ToString(),
                FirstName = reader["first_name"] is DBNull ? "" : reader["first_name"].ToString(),
                Grade = reader["grade"] is DBNull ? "" : reader["grade"].ToString()
            };
        }
    }
}
