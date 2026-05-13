using System;
using System.Data;
using System.Data.SQLite;

namespace GCTonePrism.Manager.Repositories
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
    }
}
