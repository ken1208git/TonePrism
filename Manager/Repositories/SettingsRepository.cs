using System;
using System.Data;
using System.Data.SQLite;

namespace TonePrism.Manager.Repositories
{
    /// <summary>
    /// settings テーブル（key TEXT PRIMARY KEY, value TEXT）への汎用アクセサ
    /// </summary>
    public class SettingsRepository
    {
        private readonly DatabaseConnection _conn;

        public SettingsRepository(DatabaseConnection conn)
        {
            _conn = conn;
        }

        public string GetString(string key, string defaultValue)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand("SELECT value FROM settings WHERE key = @key", connection))
                    {
                        cmd.Parameters.AddWithValue("@key", key);
                        var v = cmd.ExecuteScalar();
                        if (v == null || v == DBNull.Value) return defaultValue;
                        return v.ToString();
                    }
                }
            });
        }

        public void SetString(string key, string value)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "INSERT INTO settings (key, value) VALUES (@key, @value) " +
                        "ON CONFLICT(key) DO UPDATE SET value = @value", connection))
                    {
                        cmd.Parameters.AddWithValue("@key", key);
                        cmd.Parameters.AddWithValue("@value", value ?? "");
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        public long GetInt64(string key, long defaultValue)
        {
            string s = GetString(key, null);
            if (string.IsNullOrEmpty(s)) return defaultValue;
            return long.TryParse(s, out long result) ? result : defaultValue;
        }

        public void SetInt64(string key, long value)
        {
            SetString(key, value.ToString());
        }

        public int GetInt32(string key, int defaultValue)
        {
            string s = GetString(key, null);
            if (string.IsNullOrEmpty(s)) return defaultValue;
            return int.TryParse(s, out int result) ? result : defaultValue;
        }

        public void SetInt32(string key, int value)
        {
            SetString(key, value.ToString());
        }

        /// <summary>
        /// 自動バックアップ用の lease 取得。
        /// last_backup_at + intervalSeconds &lt;= now の場合のみ last_backup_at を now に更新して true を返す。
        /// 競合した場合は片方のみ true を返す（BEGIN IMMEDIATE による排他制御）。
        /// </summary>
        public bool TryAcquireBackupLease(long intervalSeconds, long nowUnixSeconds)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    // System.Data.SQLite では IsolationLevel.Serializable が BEGIN IMMEDIATE に対応
                    using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        long lastBackupAt = 0;
                        using (var cmd = new SQLiteCommand(
                            "SELECT value FROM settings WHERE key = 'last_backup_at'", connection, tx))
                        {
                            var v = cmd.ExecuteScalar();
                            if (v != null && v != DBNull.Value)
                                long.TryParse(v.ToString(), out lastBackupAt);
                        }

                        if (lastBackupAt + intervalSeconds > nowUnixSeconds)
                        {
                            tx.Rollback();
                            return false;
                        }

                        using (var cmd = new SQLiteCommand(
                            "UPDATE settings SET value = @v WHERE key = 'last_backup_at'", connection, tx))
                        {
                            cmd.Parameters.AddWithValue("@v", nowUnixSeconds.ToString());
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                        return true;
                    }
                }
            });
        }

        /// <summary>
        /// (H5) リストア advisory lock を取得する。BEGIN IMMEDIATE で 1 PC のみ取得可能。
        /// 既存 lock が自 PC 由来 / stale (= 5 分超過) なら上書き取得、それ以外の他 PC 由来 lock は拒否。
        /// 取得成功時の値形式: "<pcName>|<unixMs>"。
        /// </summary>
        /// <returns>取得成功なら true、他 PC が active lock 保持中なら false (+ ownerPcName を out 返却)。</returns>
        public bool TryAcquireRestoreLock(string pcName, long nowUnixMs, long staleThresholdMs, out string activeOwnerPcName)
        {
            string ownerOut = null;
            bool result = _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
                    {
                        string existing = null;
                        using (var cmd = new SQLiteCommand(
                            "SELECT value FROM settings WHERE key = '" + Services.SettingsKeys.RestoreLockOwner + "'", connection, tx))
                        {
                            var v = cmd.ExecuteScalar();
                            if (v != null && v != DBNull.Value) existing = v.ToString();
                        }

                        if (!string.IsNullOrEmpty(existing))
                        {
                            // 形式 "<pcName>|<unixMs>" を parse
                            int sep = existing.IndexOf('|');
                            if (sep > 0)
                            {
                                string ownerPc = existing.Substring(0, sep);
                                long.TryParse(existing.Substring(sep + 1), out long ownerMs);
                                bool isStale = nowUnixMs - ownerMs > staleThresholdMs;
                                bool isSelf = string.Equals(ownerPc, pcName, StringComparison.OrdinalIgnoreCase);
                                if (!isStale && !isSelf)
                                {
                                    ownerOut = ownerPc;
                                    tx.Rollback();
                                    return false;
                                }
                            }
                            // parse 失敗 / stale / self lock は上書き許可で fall-through
                        }

                        string newValue = (pcName ?? "") + "|" + nowUnixMs;
                        using (var cmd = new SQLiteCommand(
                            "INSERT INTO settings (key, value) VALUES (@k, @v) " +
                            "ON CONFLICT(key) DO UPDATE SET value = @v", connection, tx))
                        {
                            cmd.Parameters.AddWithValue("@k", Services.SettingsKeys.RestoreLockOwner);
                            cmd.Parameters.AddWithValue("@v", newValue);
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                        return true;
                    }
                }
            });
            activeOwnerPcName = ownerOut;
            return result;
        }

        /// <summary>
        /// (H5) リストア advisory lock を解除する。自 PC 保有の lock のみ解除、他 PC 保有 lock には触らない。
        /// </summary>
        public void ReleaseRestoreLock(string pcName)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "DELETE FROM settings WHERE key = @k AND value LIKE @prefix", connection))
                    {
                        cmd.Parameters.AddWithValue("@k", Services.SettingsKeys.RestoreLockOwner);
                        cmd.Parameters.AddWithValue("@prefix", (pcName ?? "") + "|%");
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// (H5) 他 PC が現在 active な restore lock を保有しているか check。
        /// </summary>
        /// <returns>他 PC 保有なら owner pcName、保有なし / 自 PC / stale なら null。</returns>
        public string GetActiveRestoreLockOwnerOrNull(string selfPcName, long nowUnixMs, long staleThresholdMs)
        {
            string existing = GetString(Services.SettingsKeys.RestoreLockOwner, null);
            if (string.IsNullOrEmpty(existing)) return null;
            int sep = existing.IndexOf('|');
            if (sep <= 0) return null;
            string ownerPc = existing.Substring(0, sep);
            if (!long.TryParse(existing.Substring(sep + 1), out long ownerMs)) return null;
            if (nowUnixMs - ownerMs > staleThresholdMs) return null;
            if (string.Equals(ownerPc, selfPcName, StringComparison.OrdinalIgnoreCase)) return null;
            return ownerPc;
        }
    }
}
