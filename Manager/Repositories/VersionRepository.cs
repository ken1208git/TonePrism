using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Repositories
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
                    _conn.OpenConnectionWithJournalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            AddVersionRowInTransaction(connection, transaction, version);
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
        /// (M5) AddGameVersion (= game_versions INSERT + version 別 developers INSERT) を既存 transaction の
        /// 中で実行する内部 helper。`AddVersionAndActivate` で同一 transaction 内に Add + UpdateGame を
        /// atomically まとめるために抽出した。version.Id は INSERT 後に更新される。
        /// </summary>
        internal void AddVersionRowInTransaction(SQLiteConnection connection, SQLiteTransaction transaction, GameVersion version)
        {
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

            using (var command = new SQLiteCommand(query, connection, transaction))
            {
                SetVersionParameters(command, version);
                command.Parameters.AddWithValue("@registeredAt", version.RegisteredAt.ToString("yyyy-MM-dd HH:mm:ss"));

                long versionId = (long)command.ExecuteScalar();
                version.Id = (int)versionId;
            }

            InsertVersionDevelopers(connection, transaction, version);
        }

        /// <summary>
        /// (#209) 個別バージョン行を既存 transaction 内で削除する。version 別 developers は
        /// `developers.version_id` の FK `ON DELETE CASCADE` (schema v18) で自動削除される
        /// (接続時 `PRAGMA foreign_keys=ON`、DatabaseConnection.OpenConnectionWithJournalMode)。
        /// アクティブ版 (games.version) の付け替えは呼び出し側 (DatabaseManager) が同一 transaction 内で行う。
        /// </summary>
        internal void DeleteVersionRowInTransaction(SQLiteConnection connection, SQLiteTransaction transaction, int versionId)
        {
            using (var command = new SQLiteCommand("DELETE FROM game_versions WHERE id = @id", connection, transaction))
            {
                command.Parameters.AddWithValue("@id", versionId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// (#209) 指定 id のバージョン行が持つ version 文字列 (DB の真値) を既存 transaction 内で取得する。
        /// フォーム上で pending リネームされた in-memory 値ではなく **DB の確定値** で active 判定するために使う。
        /// 行が無ければ null。
        /// </summary>
        internal string GetVersionStringByIdInTransaction(SQLiteConnection connection, SQLiteTransaction transaction, int versionId)
        {
            using (var command = new SQLiteCommand("SELECT version FROM game_versions WHERE id = @id", connection, transaction))
            {
                command.Parameters.AddWithValue("@id", versionId);
                object result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : (string)result;
            }
        }

        /// <summary>
        /// (#209) 指定ゲームの「最新の残存版」(id DESC 先頭) の **id** を既存 transaction 内で取得する。
        /// GameRepository.GetAll の `COALESCE(version, ... id DESC LIMIT 1)` fallback と同じ「最新 = id 最大」基準。
        /// active 付け替え時に当該版を games 行へ full mirror するための id。版が 1 件も無ければ null。
        /// </summary>
        internal int? GetLatestRemainingVersionIdInTransaction(SQLiteConnection connection, SQLiteTransaction transaction, string gameId)
        {
            using (var command = new SQLiteCommand(
                "SELECT id FROM game_versions WHERE game_id = @gameId ORDER BY id DESC LIMIT 1", connection, transaction))
            {
                command.Parameters.AddWithValue("@gameId", gameId);
                object result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? (int?)null : Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// (#209 review P2) 指定ゲームの残存版数を既存 transaction 内で数える。「最後の 1 版は削除不可」を
        /// UI ガードだけでなく DB トランザクション内でも enforce するために使う (並行 Manager で UI ガードが
        /// stale になっても 0 版ゲームを作らない)。
        /// </summary>
        internal int CountVersionsInTransaction(SQLiteConnection connection, SQLiteTransaction transaction, string gameId)
        {
            using (var command = new SQLiteCommand(
                "SELECT COUNT(*) FROM game_versions WHERE game_id = @gameId", connection, transaction))
            {
                command.Parameters.AddWithValue("@gameId", gameId);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public void Update(GameVersion version)
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
                            UpdateVersionRow(connection, transaction, version);
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
        /// (#234 後続) 複数バージョンを単一トランザクションでまとめて更新する。
        ///
        /// 背景: game_versions(game_id, version) の UNIQUE INDEX (#234 ②) は immediate 制約のため、
        /// バージョン番号の「入れ替え」(v1↔v2) / 「玉突き」(A→B→C) / 「循環」では、行を 1 件ずつ
        /// 確定していくと「一瞬だけ同じ番号が 2 行存在する」中間状態が生じ、最終状態は一意でも
        /// 制約違反で throw していた (= EditGameForm でユーザーが画面上で番号を入れ替えてから保存すると
        /// 正当な操作が失敗する回帰)。
        ///
        /// 対策: (Phase 1) 対象全行の version を「絶対に実 version と被らない一意な一時値」へ退避してから、
        /// (Phase 2) 本番の全列を確定する。最終状態が一意である限り、各 UPDATE 実行時点で衝突相手が
        /// 存在しなくなるため制約違反が起きない。全体を 1 transaction で囲むので、途中失敗時は temp 値も
        /// 含め完全に rollback され、部分コミット / 一時値残留は発生しない (旧 per-call commit ループの
        /// 部分コミット問題も同時に解消)。
        /// </summary>
        public void UpdateMany(IEnumerable<GameVersion> versions)
        {
            var list = versions?.Where(v => v != null).ToList() ?? new List<GameVersion>();
            if (list.Count == 0) return;

            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            UpdateManyInTransaction(connection, transaction, list);
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
        /// (累積監査 round 4 High-2) UpdateMany の core logic を既存 transaction の中で実行する内部 helper。
        /// `DatabaseManager.UpdateVersionsAndGame` で同一 transaction 内に UpdateGameVersions + UpdateGame を
        /// atomically まとめるために抽出。Phase 1 (temp 退避) + Phase 2 (本番確定) の二段 sweep は UpdateMany と同一。
        /// </summary>
        internal void UpdateManyInTransaction(SQLiteConnection connection, SQLiteTransaction transaction, List<GameVersion> list)
        {
            if (list == null || list.Count == 0) return;

            // Phase 1: 全行の version を一意な一時値へ退避 (途中の UNIQUE 一時衝突を回避)。
            // temp 値は "__tmp_" prefix + id + GUID で、SemVer 形式の実 version とは絶対に
            // 一致しない (= 既存・本番いずれの値とも衝突しない)。
            foreach (var v in list)
            {
                using (var cmd = new SQLiteCommand(
                    "UPDATE game_versions SET version = @version WHERE id = @id", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@version", "__tmp_" + v.Id + "_" + Guid.NewGuid().ToString("N"));
                    cmd.Parameters.AddWithValue("@id", v.Id);
                    cmd.ExecuteNonQuery();
                }
            }

            // Phase 2: 本番の全列 (version 含む) + 製作者を確定。
            foreach (var v in list)
            {
                UpdateVersionRow(connection, transaction, v);
            }
        }

        /// <summary>
        /// 1 バージョン行の全列 UPDATE + 製作者の削除→再登録を、呼び出し側が用意した connection /
        /// transaction 上で実行する (commit はしない)。Update (単発) / UpdateMany (一括) で共通利用。
        /// </summary>
        private void UpdateVersionRow(SQLiteConnection connection, SQLiteTransaction transaction, GameVersion version)
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
        }

        public List<GameVersion> GetByGameId(string gameId)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var versions = new List<GameVersion>();

                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    // (#234) 並びは registered_at DESC ではなく id DESC。registered_at は秒精度文字列の
                    // ため同一秒内に追加された版でタイが起き「最新版」(FirstOrDefault) が非決定的になる。
                    // id は AUTOINCREMENT で挿入順=作成順を単調に表すため最新判定が安定し、GameRepository.GetAll
                    // の display_version (= 同じく id DESC LIMIT 1) とも整合する。
                    string query = @"
                        SELECT *
                        FROM game_versions
                        WHERE game_id = @gameId
                        ORDER BY id DESC";

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@gameId", gameId);
                        using (var reader = command.ExecuteReader())
                        {
                            // (perf) update_note 列の有無は結果セット全体で一定。旧実装は行ごとに
                            // reader.GetSchemaTable() (= DataTable を毎行アロケート) を呼んでおり、
                            // 版が多いゲーム × 全件走査 (復元整合性チェック等) で無駄が嵩んでいた。
                            // ループ前に 1 回だけ列存在を判定する。未 migrate DB を読む防御として
                            // presence check 自体は維持 (migration 後は常に存在)。
                            bool hasUpdateNote = false;
                            for (int ci = 0; ci < reader.FieldCount; ci++)
                            {
                                if (string.Equals(reader.GetName(ci), "update_note", StringComparison.OrdinalIgnoreCase))
                                {
                                    hasUpdateNote = true;
                                    break;
                                }
                            }
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
                                    UpdateNote = hasUpdateNote && !(reader["update_note"] is DBNull) ? reader["update_note"].ToString() : null,

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
                    // (累積監査 round 6 High-2) GameRepository.InsertDevelopers は `?? ""` で null を空文字に
                    // 正規化しているのに、本 version 別 INSERT 側は素通しで非対称だった。同じ user 入力が
                    // games 側 developers では "" / version 側 developers では NULL に乖離し、後段の集計 /
                    // 表示 (null と空文字を区別する経路) で不整合や NRE を招く。両者を `?? ""` で揃える。
                    devCmd.Parameters.AddWithValue("@gameId", version.GameId);
                    devCmd.Parameters.AddWithValue("@lastName", developer.LastName ?? "");
                    devCmd.Parameters.AddWithValue("@firstName", developer.FirstName ?? "");
                    devCmd.Parameters.AddWithValue("@grade", developer.Grade ?? "");
                    devCmd.Parameters.AddWithValue("@versionId", version.Id);
                    devCmd.ExecuteNonQuery();
                }
            }
        }
    }
}
