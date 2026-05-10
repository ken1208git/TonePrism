using System;
using System.Collections.Generic;
using System.Data.SQLite;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Repositories
{
    /// <summary>
    /// ストアセクションのCRUD操作
    /// </summary>
    public class StoreSectionRepository
    {
        private readonly DatabaseConnection _conn;

        public StoreSectionRepository(DatabaseConnection conn)
        {
            _conn = conn;
        }

        public List<StoreSectionInfo> GetAll()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var sections = new List<StoreSectionInfo>();

                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    string query = @"
                        SELECT section_id, title, section_type, section_source,
                               display_order, max_display_count, is_visible
                        FROM store_sections
                        ORDER BY display_order ASC, section_id ASC";

                    using (var command = new SQLiteCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var section = ReadSectionInfo(reader);
                            section.Games = GetGamesBySectionId(section.SectionId, connection, section);
                            sections.Add(section);
                        }
                    }
                }

                return sections;
            });
        }

        public StoreSectionInfo GetById(int sectionId)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    string query = @"
                        SELECT section_id, title, section_type, section_source,
                               display_order, max_display_count, is_visible
                        FROM store_sections
                        WHERE section_id = @sectionId";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@sectionId", sectionId);

                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var section = ReadSectionInfo(reader);
                                section.Games = GetGamesBySectionId(sectionId, connection, section);
                                return section;
                            }
                        }
                    }
                }

                return null;
            });
        }

        public void Add(StoreSectionInfo section)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string insertSql = @"
                                INSERT INTO store_sections
                                    (title, section_type, section_source, display_order, max_display_count, is_visible)
                                VALUES
                                    (@title, @sectionType, @sectionSource, @displayOrder, @maxDisplayCount, @isVisible)";

                            using (var command = new SQLiteCommand(insertSql, connection, transaction))
                            {
                                SetSectionParameters(command, section);
                                command.ExecuteNonQuery();
                                section.SectionId = (int)connection.LastInsertRowId;
                            }

                            // 手動セクションの場合、ゲーム紐付けを登録
                            if (section.SectionSource == "manual" && section.Games != null)
                            {
                                InsertSectionGames(connection, transaction, section.SectionId, section.Games, section.GameDisplayTexts);
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

        public void Update(StoreSectionInfo section)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            string updateSql = @"
                                UPDATE store_sections SET
                                    title = @title,
                                    section_type = @sectionType,
                                    section_source = @sectionSource,
                                    display_order = @displayOrder,
                                    max_display_count = @maxDisplayCount,
                                    is_visible = @isVisible
                                WHERE section_id = @sectionId";

                            using (var command = new SQLiteCommand(updateSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@sectionId", section.SectionId);
                                SetSectionParameters(command, section);
                                command.ExecuteNonQuery();
                            }

                            // ゲーム紐付けを再構築
                            string deleteSql = "DELETE FROM store_section_games WHERE section_id = @sectionId";
                            using (var command = new SQLiteCommand(deleteSql, connection, transaction))
                            {
                                command.Parameters.AddWithValue("@sectionId", section.SectionId);
                                command.ExecuteNonQuery();
                            }

                            if (section.SectionSource == "manual" && section.Games != null)
                            {
                                InsertSectionGames(connection, transaction, section.SectionId, section.Games, section.GameDisplayTexts);
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

        public void Delete(int sectionId)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    string deleteSql = "DELETE FROM store_sections WHERE section_id = @sectionId";
                    using (var command = new SQLiteCommand(deleteSql, connection))
                    {
                        command.Parameters.AddWithValue("@sectionId", sectionId);
                        command.ExecuteNonQuery();
                    }
                }
            });
        }

        public int GetMaxDisplayOrder()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    string query = "SELECT COALESCE(MAX(display_order), -1) FROM store_sections";
                    using (var command = new SQLiteCommand(query, connection))
                    {
                        return Convert.ToInt32(command.ExecuteScalar());
                    }
                }
            });
        }

        private List<GameInfo> GetGamesBySectionId(int sectionId, SQLiteConnection connection, StoreSectionInfo section = null)
        {
            var games = new List<GameInfo>();

            string query = @"
                SELECT g.game_id, g.title, g.thumbnail_path, g.background_path, ssg.display_text
                FROM store_section_games ssg
                JOIN games g ON ssg.game_id = g.game_id
                WHERE ssg.section_id = @sectionId
                ORDER BY ssg.display_order ASC";

            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@sectionId", sectionId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var game = new GameInfo
                        {
                            GameId = reader["game_id"].ToString(),
                            Title = reader["title"].ToString(),
                            ThumbnailPath = reader["thumbnail_path"] is DBNull ? null : reader["thumbnail_path"].ToString(),
                            BackgroundPath = reader["background_path"] is DBNull ? null : reader["background_path"].ToString()
                        };
                        games.Add(game);

                        // display_textをセクションに紐付け
                        if (section != null)
                        {
                            string displayText = reader["display_text"] is DBNull ? "" : reader["display_text"].ToString();
                            if (!string.IsNullOrEmpty(displayText))
                            {
                                section.GameDisplayTexts[game.GameId] = displayText;
                            }
                        }
                    }
                }
            }

            return games;
        }

        private void InsertSectionGames(SQLiteConnection connection, SQLiteTransaction transaction,
            int sectionId, List<GameInfo> games, Dictionary<string, string> displayTexts = null)
        {
            string insertSql = @"
                INSERT INTO store_section_games (section_id, game_id, display_order, display_text)
                VALUES (@sectionId, @gameId, @displayOrder, @displayText)";

            for (int i = 0; i < games.Count; i++)
            {
                string displayText = "";
                if (displayTexts != null && displayTexts.ContainsKey(games[i].GameId))
                {
                    displayText = displayTexts[games[i].GameId];
                }

                using (var command = new SQLiteCommand(insertSql, connection, transaction))
                {
                    command.Parameters.AddWithValue("@sectionId", sectionId);
                    command.Parameters.AddWithValue("@gameId", games[i].GameId);
                    command.Parameters.AddWithValue("@displayOrder", i);
                    command.Parameters.AddWithValue("@displayText", displayText);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void SetSectionParameters(SQLiteCommand command, StoreSectionInfo section)
        {
            command.Parameters.AddWithValue("@title", section.Title ?? "");
            command.Parameters.AddWithValue("@sectionType", section.SectionType);
            command.Parameters.AddWithValue("@sectionSource", section.SectionSource ?? "manual");
            command.Parameters.AddWithValue("@displayOrder", section.DisplayOrder);
            command.Parameters.AddWithValue("@maxDisplayCount", section.MaxDisplayCount);
            command.Parameters.AddWithValue("@isVisible", section.IsVisible ? 1 : 0);
        }

        private StoreSectionInfo ReadSectionInfo(SQLiteDataReader reader)
        {
            return new StoreSectionInfo
            {
                SectionId = Convert.ToInt32(reader["section_id"]),
                Title = reader["title"].ToString(),
                SectionType = Convert.ToInt32(reader["section_type"]),
                SectionSource = reader["section_source"] is DBNull ? "manual" : reader["section_source"].ToString(),
                DisplayOrder = Convert.ToInt32(reader["display_order"]),
                MaxDisplayCount = Convert.ToInt32(reader["max_display_count"]),
                IsVisible = Convert.ToInt32(reader["is_visible"]) == 1
            };
        }
    }
}
