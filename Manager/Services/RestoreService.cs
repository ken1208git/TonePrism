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
        /// 指定されたバックアップファイルから prism.db を復元する（atomic 復元）。
        /// 1. 現DBを backups/safety/safety_HHmmss.db として退避（Online Backup API でコピー）
        /// 2. バックアップを prism.db.restore-tmp に先にコピー（prism.db はまだ無傷）
        /// 3. 全 SQLite 接続プールをクリア
        ///   ─── ここから先はキャンセル不可（中断すると prism.db が一時的に欠落） ───
        /// 4. 旧 WAL/SHM を削除し、prism.db を tmp で置換（File.Replace で atomic）
        /// 5. backups/safety/ のリテンション適用（古いものから削除）
        ///
        /// 利点: コピー失敗・キャンセル発生時に prism.db が消えてしまう事故を回避
        /// （Codex P1 指摘 "Disallow cancellation after deleting active DB files" 対応）
        /// </summary>
        /// <returns>退避された安全バックアップのフルパス</returns>
        public string Restore(string backupFilePath, IProgress<ProgressInfo> progress, CancellationToken token)
        {
            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("指定されたバックアップファイルが存在しません", backupFilePath);

            string dbPath = _conn.DbPath;
            string dbDir = Path.GetDirectoryName(dbPath);
            string safetyDir = Path.Combine(dbDir, "backups", "safety");
            string tempPath = dbPath + ".restore-tmp";

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

            // 2. バックアップを一時ファイルへ先にコピー（prism.db はまだ無傷）。
            //    コピー中・コピー後にキャンセル/失敗が起きても prism.db は壊れない。
            progress?.Report(new ProgressInfo(40, "バックアップを準備中...", backupFilePath));
            if (File.Exists(tempPath)) File.Delete(tempPath); // 前回失敗時の残骸があれば消す
            File.Copy(backupFilePath, tempPath, false);

            token.ThrowIfCancellationRequested();

            // 3. 全 SQLite 接続プールをクリアし、ハンドルを解放
            progress?.Report(new ProgressInfo(60, "データベース接続を閉じています...", ""));
            SQLiteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // ─── ここから先はキャンセル不可。中断すると prism.db が一時的に欠落する ───
            // 4. WAL/SHM を削除し、tmp で prism.db を置換
            progress?.Report(new ProgressInfo(80, "既存ファイルを置き換え中...", ""));
            DeleteWithRetry(dbPath + "-wal");
            DeleteWithRetry(dbPath + "-shm");

            if (File.Exists(dbPath))
            {
                // File.Replace は NTFS 上で atomic（ReplaceFile Win32 API）
                // backupFileName=null で旧 prism.db のバックアップは作らない（safety で既に確保済み）
                File.Replace(tempPath, dbPath, null);
            }
            else
            {
                // 新規 DB の場合（通常想定外だが安全対策）
                File.Move(tempPath, dbPath);
            }

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
                        Logger.Info($"[RestoreService] 古い退避ファイル削除: {f.Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[RestoreService] 退避ファイル削除失敗 {f.Name}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[RestoreService] 退避リテンション処理失敗", ex);
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
