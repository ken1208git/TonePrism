using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// バックアップファイルから prism.db を復元するサービス。
    /// 安全のため、復元前に現DBを backups/safety/safety_*.db として退避する。
    /// </summary>
    public class RestoreService
    {
        public const int DefaultSafetyRetentionCount = 10;

        private readonly DatabaseConnection _conn;

        public RestoreService(DatabaseConnection conn)
        {
            _conn = conn;
        }

        /// <summary>
        /// 指定されたバックアップファイルから prism.db を復元する。
        /// 1. 現DBを backups/safety/safety_HHmmss.db として退避（Online Backup API でコピー）
        /// 2. 全 SQLite 接続プールをクリア
        /// 3. prism.db / .db-wal / .db-shm を削除
        /// 4. バックアップファイルを prism.db としてコピー
        /// 5. backups/safety/ のリテンション適用（古いものから削除）
        /// </summary>
        /// <returns>退避された安全バックアップのフルパス</returns>
        public string Restore(string backupFilePath, IProgress<ProgressInfo> progress, CancellationToken token)
        {
            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("指定されたバックアップファイルが存在しません", backupFilePath);

            string dbPath = _conn.DbPath;
            string dbDir = Path.GetDirectoryName(dbPath);
            string safetyDir = Path.Combine(dbDir, "backups", "safety");

            progress?.Report(new ProgressInfo(0, "復元の準備...", ""));
            token.ThrowIfCancellationRequested();

            // 退避フォルダを必ず作成
            if (!Directory.Exists(safetyDir))
            {
                Directory.CreateDirectory(safetyDir);
            }

            // 1. 現DBを安全バックアップ（Online Backup API でライブコピー）
            string safetyPath = Path.Combine(safetyDir, $"safety_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            progress?.Report(new ProgressInfo(10, "現在のデータベースを退避中...", safetyPath));

            if (File.Exists(dbPath))
            {
                using (var sourceConn = new SQLiteConnection(_conn.ConnectionString))
                {
                    sourceConn.Open();
                    using (var destConn = new SQLiteConnection($"Data Source={safetyPath};Version=3;"))
                    {
                        destConn.Open();
                        sourceConn.BackupDatabase(destConn, "main", "main", -1, null, 0);
                    }
                }
            }

            token.ThrowIfCancellationRequested();

            // 2. 全 SQLite 接続プールをクリアし、ハンドルを解放
            progress?.Report(new ProgressInfo(40, "データベース接続を閉じています...", ""));
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            token.ThrowIfCancellationRequested();

            // 3. 現DBファイルおよび WAL/SHM を削除
            progress?.Report(new ProgressInfo(55, "既存ファイルを削除中...", ""));
            DeleteWithRetry(dbPath);
            DeleteWithRetry(dbPath + "-wal");
            DeleteWithRetry(dbPath + "-shm");

            token.ThrowIfCancellationRequested();

            // 4. バックアップファイルを prism.db としてコピー
            progress?.Report(new ProgressInfo(75, "バックアップを復元中...", backupFilePath));
            File.Copy(backupFilePath, dbPath, false);

            // 5. 退避フォルダのリテンション適用（最新を残して古いのから削除）
            progress?.Report(new ProgressInfo(95, "退避ファイルの世代管理...", ""));
            ApplySafetyRetention(safetyDir, DefaultSafetyRetentionCount);

            progress?.Report(new ProgressInfo(100, "復元完了", dbPath));
            return safetyPath;
        }

        /// <summary>
        /// safety_*.db を新しい順に count 個まで残し、それより古いものを削除
        /// </summary>
        private static void ApplySafetyRetention(string safetyDir, int count)
        {
            try
            {
                var dir = new DirectoryInfo(safetyDir);
                if (!dir.Exists) return;

                var oldFiles = dir.GetFiles("safety_*.db")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(count)
                    .ToList();

                foreach (var f in oldFiles)
                {
                    try
                    {
                        f.Delete();
                        Console.WriteLine($"[RestoreService] 古い退避ファイル削除: {f.Name}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RestoreService] 退避ファイル削除失敗 {f.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RestoreService] 退避リテンション処理失敗: {ex.Message}");
            }
        }

        private static void DeleteWithRetry(string path)
        {
            if (!File.Exists(path)) return;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(200);
                }
            }
            // 5回試して駄目ならもう一度試して例外を伝播
            File.Delete(path);
        }
    }
}
