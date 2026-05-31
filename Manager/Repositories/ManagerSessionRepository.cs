using System;
using System.Collections.Generic;
using System.Data.SQLite;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Repositories
{
    /// <summary>
    /// (#179) `manager_sessions` table への CRUD アクセサ。Manager の LAN-wide 同時起動検出に使う。
    /// schema は SPECIFICATION.md §7.3 参照、heartbeat / stale cleanup の仕様は §3.8 参照。
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
        /// `last_heartbeat_at_unix_ms < threshold` の row を DELETE する**閾値中立**の汎用メソッド。
        /// 削除ポリシー (どの古さで消すか) は caller が閾値で決める。(#271 review L2) メソッド名を "Stale" に
        /// しないのは footgun 回避: stale 閾値 (60秒) を渡すと clock skew で生存中の遠隔 Manager row を消し
        /// DB 破損に繋がるため、**stale 閾値を渡してはならない**。唯一の caller (`ManagerSessionService.Initialize`)
        /// は「放置 (abandoned)」閾値 (= now − 1 日) を渡す。自 crash 残骸は `UpsertSelfSession` の INSERT OR REPLACE
        /// で上書き回収、検出側は query 時 60 秒閾値で stale 除外。heartbeat thread はこれを呼ばない。
        /// </summary>
        /// <param name="thresholdUnixMs">この時刻より前の heartbeat の row を削除 (#271 では now − 1 日 の abandoned 閾値)。</param>
        /// <returns>削除した row 数。</returns>
        public int DeleteSessionsOlderThan(long thresholdUnixMs)
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
                        cmd.Parameters.AddWithValue("@threshold", thresholdUnixMs);
                        return cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// self row を INSERT OR REPLACE で登録 (同 pc_name 既存なら上書き)。
        /// 起動時に 1 度だけ呼ぶ。Named Mutex で同 PC 重複起動は block されている前提のため、
        /// 「同 pc_name 既存」は通常 crash 残骸 (= 前回 session の row) で、本 INSERT OR REPLACE が
        /// そのまま上書き回収する (#271 で起動時 cleanup は 1 日 abandoned 化したので、60 秒程度の残骸は
        /// cleanup ではなく本 UPSERT で上書きされる)。または manual INSERT (test) ケース。
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
        /// self row の heartbeat を update。heartbeat thread が周期実行する。
        /// (round 3 H-2 fix) 旧実装は `UPDATE WHERE pc_name = @pc_name` で row 不在時に silent no-op
        /// だったため、自 row が物理 DELETE された case で **以降の heartbeat がすべて silent 空振り、自 PC が
        /// 他 PC から永久不可視化** する path があった。`INSERT OR REPLACE` (UPSERT) で row 不在時も自動で
        /// 再 INSERT する形に変更、reanimate 可能に。
        /// (#271) なお「別 PC 起動の cleanup (`DeleteSessionsOlderThan`) が 30 秒遅延した自 row を消す」という旧トリガは、
        /// cleanup を 1 日 abandoned 閾値に変えたため通常は発生しない (UPSERT reanimate は手動削除等への safety net)。
        /// </summary>
        /// <param name="info">self session info (UpsertSelfSession と同じ 5 field、heartbeat 用に毎回新規 instance)。</param>
        public void UpsertHeartbeat(ManagerSessionInfo info)
        {
            // 実装は UpsertSelfSession と同 SQL (INSERT OR REPLACE 5 field 全部 set) を再利用。
            // 別 method 名で意図 (= 起動時 1 度 vs heartbeat 周期) を区別するが、SQL は共通の atomic upsert。
            UpsertSelfSession(info);
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
