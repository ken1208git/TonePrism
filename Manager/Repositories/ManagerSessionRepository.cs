using System;
using System.Collections.Generic;
using System.Data.SQLite;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Repositories
{
    /// <summary>
    /// (#179) `manager_sessions` table への CRUD アクセサ。Manager の LAN-wide 同時起動検出に使う。
    /// schema は SPECIFICATION.md §7.3 参照、heartbeat / stale cleanup の仕様は §3.X 参照。
    ///
    /// 全 method は `DatabaseConnection.ExecuteWithRetry` で wrap、SMB BUSY/LOCKED 競合に対応。
    /// `pc_name` PRIMARY KEY のため同 PC は 1 row のみ (重複起動は Named Mutex で物理 block、
    /// `Program.cs` 参照)。
    /// </summary>
    public class ManagerSessionRepository
    {
        private readonly DatabaseConnection _conn;

        public ManagerSessionRepository(DatabaseConnection conn)
        {
            _conn = conn;
        }

        /// <summary>
        /// stale row (= `last_heartbeat_at_unix_ms < threshold`) を DELETE。
        /// 起動時 self row INSERT 前 + heartbeat 周期 check 前に呼ぶことで crash 残骸を自動 cleanup。
        /// </summary>
        /// <param name="staleThresholdUnixMs">この時刻より前の heartbeat の row を削除。</param>
        /// <returns>削除した row 数。</returns>
        public int DeleteStaleSessions(long staleThresholdUnixMs)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "DELETE FROM manager_sessions WHERE last_heartbeat_at_unix_ms < @threshold",
                        connection))
                    {
                        cmd.Parameters.AddWithValue("@threshold", staleThresholdUnixMs);
                        return cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// self row を INSERT OR REPLACE で登録 (同 pc_name 既存なら上書き)。
        /// 起動時に 1 度だけ呼ぶ。Named Mutex で同 PC 重複起動は block されている前提のため、
        /// 「同 pc_name 既存」は通常 crash 残骸 (= 30 秒以上前の heartbeat) で `DeleteStaleSessions`
        /// 後の no-op、または manual INSERT (test) ケース。
        /// </summary>
        public void UpsertSelfSession(ManagerSessionInfo session)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "INSERT OR REPLACE INTO manager_sessions " +
                        "(pc_name, started_at_unix_ms, last_heartbeat_at_unix_ms, pid, manager_version) " +
                        "VALUES (@pc_name, @started_at, @last_heartbeat, @pid, @manager_version)",
                        connection))
                    {
                        cmd.Parameters.AddWithValue("@pc_name", session.PcName);
                        cmd.Parameters.AddWithValue("@started_at", session.StartedAtUnixMs);
                        cmd.Parameters.AddWithValue("@last_heartbeat", session.LastHeartbeatAtUnixMs);
                        cmd.Parameters.AddWithValue("@pid", session.Pid);
                        cmd.Parameters.AddWithValue("@manager_version", session.ManagerVersion ?? "");
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// self row の `last_heartbeat_at_unix_ms` のみを update。heartbeat thread が周期実行する。
        /// row 不在 (= 別 process が DELETE した case) でも no-op で済む UPDATE 文。
        /// </summary>
        public void UpdateHeartbeat(string pcName, long heartbeatUnixMs)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "UPDATE manager_sessions SET last_heartbeat_at_unix_ms = @heartbeat WHERE pc_name = @pc_name",
                        connection))
                    {
                        cmd.Parameters.AddWithValue("@pc_name", pcName);
                        cmd.Parameters.AddWithValue("@heartbeat", heartbeatUnixMs);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// self row を DELETE。shutdown 時に呼ぶ (clean exit の trail)。
        /// </summary>
        public void DeleteSelfSession(string pcName)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "DELETE FROM manager_sessions WHERE pc_name = @pc_name",
                        connection))
                    {
                        cmd.Parameters.AddWithValue("@pc_name", pcName);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// self 以外の active (= heartbeat が stale でない) session を一覧。
        /// 起動時 check + 編集操作前 check の SoT。
        /// </summary>
        /// <param name="selfPcName">除外する self の pc_name。</param>
        /// <param name="staleThresholdUnixMs">この時刻以上の heartbeat を持つ row のみ返却。</param>
        public IReadOnlyList<ManagerSessionInfo> SelectOtherActiveSessions(string selfPcName, long staleThresholdUnixMs)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var list = new List<ManagerSessionInfo>();
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "SELECT pc_name, started_at_unix_ms, last_heartbeat_at_unix_ms, pid, manager_version " +
                        "FROM manager_sessions " +
                        "WHERE pc_name != @self_pc_name AND last_heartbeat_at_unix_ms >= @threshold " +
                        "ORDER BY last_heartbeat_at_unix_ms DESC",
                        connection))
                    {
                        cmd.Parameters.AddWithValue("@self_pc_name", selfPcName ?? "");
                        cmd.Parameters.AddWithValue("@threshold", staleThresholdUnixMs);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                list.Add(new ManagerSessionInfo
                                {
                                    PcName                = reader["pc_name"].ToString(),
                                    StartedAtUnixMs       = Convert.ToInt64(reader["started_at_unix_ms"]),
                                    LastHeartbeatAtUnixMs = Convert.ToInt64(reader["last_heartbeat_at_unix_ms"]),
                                    Pid                   = Convert.ToInt64(reader["pid"]),
                                    ManagerVersion        = reader["manager_version"].ToString(),
                                });
                            }
                        }
                    }
                }
                return (IReadOnlyList<ManagerSessionInfo>)list;
            });
        }
    }
}
