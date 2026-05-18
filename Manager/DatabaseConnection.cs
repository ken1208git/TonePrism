using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;

namespace TonePrism.Manager
{
    /// <summary>
    /// SQLite接続管理・WALモード・リトライロジック
    /// </summary>
    public class DatabaseConnection
    {
        private readonly string connectionString;
        private readonly string dbPath;

        public string ConnectionString => connectionString;
        public string DbPath => dbPath;

        public DatabaseConnection()
        {
            dbPath = PathManager.DatabasePath;
            // SMB ネットワーク共有上での運用安全性のため journal_mode=DELETE を使用 (#103)
            // Busy Timeout はライブラリ側にもフォールバックとして指定する
            connectionString = $"Data Source={dbPath};Version=3;Busy Timeout=10000;";
        }

        public bool DatabaseExists()
        {
            return File.Exists(dbPath);
        }

        /// <summary>
        /// ジャーナルモードと PRAGMA を設定して接続を開く
        /// SMB 共有上では WAL モードが動作保証外のため DELETE モードを使用 (#103)
        /// </summary>
        public void OpenConnectionWithJournalMode(SQLiteConnection connection)
        {
            connection.Open();

            using (var command = new SQLiteCommand("PRAGMA journal_mode=DELETE;", connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("PRAGMA busy_timeout=10000;", connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("PRAGMA synchronous=NORMAL;", connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("PRAGMA foreign_keys=ON;", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// データベース操作をリトライ付きで実行するヘルパーメソッド
        /// </summary>
        public T ExecuteWithRetry<T>(Func<T> action, int maxRetries = 3, int delayMs = 100)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return action();
                }
                catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Busy || ex.ResultCode == SQLiteErrorCode.Locked)
                {
                    if (i == maxRetries - 1) throw;
                    Thread.Sleep(delayMs);
                }
            }
            return default(T);
        }

        /// <summary>
        /// データベース操作をリトライ付きで実行するヘルパーメソッド（戻り値なし）
        /// </summary>
        public void ExecuteWithRetry(Action action, int maxRetries = 3, int delayMs = 100)
        {
            ExecuteWithRetry<object>(() => { action(); return null; }, maxRetries, delayMs);
        }

        /// <summary>
        /// SQLiteエラーのユーザー向けメッセージ変換
        /// </summary>
        public static string GetUserFriendlyErrorMessage(SQLiteException ex)
        {
            switch (ex.ResultCode)
            {
                case SQLiteErrorCode.Constraint:
                    if (ex.Message.Contains("UNIQUE constraint failed"))
                        return "ユニーク制約違反です。すでに存在するIDを使用している可能性があります。";
                    return "データベースの制約に違反しています。";
                case SQLiteErrorCode.Locked:
                case SQLiteErrorCode.Busy:
                    return "データベースがロックされています。他のアプリケーションが使用中か確認してください。";
                case SQLiteErrorCode.ReadOnly:
                    return "データベースは読み取り専用です。書き込み権限を確認してください。";
                case SQLiteErrorCode.Corrupt:
                    return "データベースファイルが破損しています。";
                case SQLiteErrorCode.Full:
                    return "ディスク容量が不足しています。";
                default:
                    return $"データベースエラーが発生しました (Code: {ex.ResultCode}): {ex.Message}";
            }
        }
    }
}
