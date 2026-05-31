using System;
using System.Collections.Generic;
using System.Data.SQLite;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Repositories
{
    /// <summary>
    /// (#253) イントロガイドのスライド (`intro_slides`) の CRUD。
    /// `StoreSectionRepository` と同流儀 (`ExecuteWithRetry` + `OpenConnectionWithJournalMode`)。
    /// 画像実体は `guide/` にファイル別管理し、本リポジトリは相対パス文字列のみ扱う。
    /// </summary>
    public class IntroSlideRepository
    {
        private readonly DatabaseConnection _conn;

        public IntroSlideRepository(DatabaseConnection conn)
        {
            _conn = conn;
        }

        /// <summary>全スライドを表示順 (display_order 昇順 → slide_id 昇順) で取得。</summary>
        public List<IntroSlide> GetAll()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var slides = new List<IntroSlide>();
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    string query = @"
                        SELECT slide_id, display_order, body_text, image_path, is_visible
                        FROM intro_slides
                        ORDER BY display_order ASC, slide_id ASC";
                    using (var command = new SQLiteCommand(query, connection))
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            slides.Add(ReadSlide(reader));
                        }
                    }
                }
                return slides;
            });
        }

        /// <summary>新規スライドを INSERT。成功時 `slide.SlideId` に採番された ID を書き戻す。</summary>
        public void Add(IntroSlide slide)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    string insertSql = @"
                        INSERT INTO intro_slides
                            (display_order, body_text, image_path, is_visible)
                        VALUES
                            (@displayOrder, @bodyText, @imagePath, @isVisible)";
                    using (var command = new SQLiteCommand(insertSql, connection))
                    {
                        SetSlideParameters(command, slide);
                        command.ExecuteNonQuery();
                        slide.SlideId = (int)connection.LastInsertRowId;
                    }
                }
            });
        }

        /// <summary>既存スライドを slide_id で UPDATE。</summary>
        public void Update(IntroSlide slide)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    string updateSql = @"
                        UPDATE intro_slides SET
                            display_order = @displayOrder,
                            body_text     = @bodyText,
                            image_path    = @imagePath,
                            is_visible    = @isVisible
                        WHERE slide_id = @slideId";
                    using (var command = new SQLiteCommand(updateSql, connection))
                    {
                        command.Parameters.AddWithValue("@slideId", slide.SlideId);
                        SetSlideParameters(command, slide);
                        command.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>slide_id で DELETE。画像ファイル (`guide/`) の物理削除は caller (Manager パネル) の責務。</summary>
        public void Delete(int slideId)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var command = new SQLiteCommand("DELETE FROM intro_slides WHERE slide_id = @slideId", connection))
                    {
                        command.Parameters.AddWithValue("@slideId", slideId);
                        command.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>末尾追加用に現在の最大 display_order を取得 (空テーブルなら -1)。</summary>
        public int GetMaxDisplayOrder()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var command = new SQLiteCommand("SELECT COALESCE(MAX(display_order), -1) FROM intro_slides", connection))
                    {
                        return Convert.ToInt32(command.ExecuteScalar());
                    }
                }
            });
        }

        /// <summary>
        /// 2 スライドの display_order を **1 transaction で**入れ替える (#274 review #2)。並び替えで
        /// UpdateSlide を 2 回別々に投げると、片方成功・片方失敗で両者が同じ display_order になる half-write が
        /// 起きうるため、atomic な swap を提供する。
        /// </summary>
        public void SwapDisplayOrder(int slideIdA, int orderA, int slideIdB, int orderB)
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
                            using (var cmd = new SQLiteCommand("UPDATE intro_slides SET display_order = @o WHERE slide_id = @id", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@o", orderA);
                                cmd.Parameters.AddWithValue("@id", slideIdA);
                                cmd.ExecuteNonQuery();
                            }
                            using (var cmd = new SQLiteCommand("UPDATE intro_slides SET display_order = @o WHERE slide_id = @id", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@o", orderB);
                                cmd.Parameters.AddWithValue("@id", slideIdB);
                                cmd.ExecuteNonQuery();
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

        private void SetSlideParameters(SQLiteCommand command, IntroSlide slide)
        {
            command.Parameters.AddWithValue("@displayOrder", slide.DisplayOrder);
            command.Parameters.AddWithValue("@bodyText", slide.BodyText ?? "");
            // 空/空白の画像パスは DB 上 null に正規化 (AGENTS「空→null 等、フォーム間で揃える」)。
            command.Parameters.AddWithValue("@imagePath",
                string.IsNullOrWhiteSpace(slide.ImagePath) ? (object)DBNull.Value : slide.ImagePath);
            command.Parameters.AddWithValue("@isVisible", slide.IsVisible ? 1 : 0);
        }

        private IntroSlide ReadSlide(SQLiteDataReader reader)
        {
            return new IntroSlide
            {
                SlideId = Convert.ToInt32(reader["slide_id"]),
                DisplayOrder = Convert.ToInt32(reader["display_order"]),
                BodyText = reader["body_text"] is DBNull ? "" : reader["body_text"].ToString(),
                ImagePath = reader["image_path"] is DBNull ? null : reader["image_path"].ToString(),
                IsVisible = Convert.ToInt32(reader["is_visible"]) == 1,
            };
        }
    }
}
