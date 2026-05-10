using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Repositories;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// データベースバックアップ機能のドメインロジック。
    /// SQLite Online Backup API を用いて prism.db のスナップショットを取得し、
    /// backup_log への記録、世代管理（リテンション）、自動バックアップの lease 取得を担う。
    /// </summary>
    public class BackupService
    {
        public const string TriggerManual = "manual";
        public const string TriggerAuto = "auto";

        private readonly DatabaseConnection _conn;
        private readonly BackupLogRepository _logRepo;
        private readonly SettingsRepository _settingsRepo;

        public BackupService(DatabaseConnection conn, BackupLogRepository logRepo, SettingsRepository settingsRepo)
        {
            _conn = conn;
            _logRepo = logRepo;
            _settingsRepo = settingsRepo;
        }

        /// <summary>
        /// 自動バックアップを走らせるべきかチェック（参照のみ、副作用なし）。
        /// 実際の実行時には RunAutoBackupIfDue を使う（lease 取得を含む）。
        /// </summary>
        public bool IsAutoBackupDue()
        {
            int intervalHours = _settingsRepo.GetInt32("backup_auto_interval_hours", 24);
            long lastBackupAt = _settingsRepo.GetInt64("last_backup_at", 0);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return lastBackupAt + (long)intervalHours * 3600 <= now;
        }

        /// <summary>
        /// 設定された保存先パスを返す。空ならデフォルト（DBフォルダ/backups/）を返す。
        /// </summary>
        public string GetEffectiveDestinationDirectory()
        {
            string configured = _settingsRepo.GetString("backup_destination_path", "");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
            string dbDir = Path.GetDirectoryName(_conn.DbPath);
            return Path.Combine(dbDir, "backups");
        }

        /// <summary>
        /// 自動バックアップ実行（lease 取得失敗時はスキップ）
        /// </summary>
        public BackupResult RunAutoBackupIfDue(IProgress<ProgressInfo> progress, CancellationToken token)
        {
            int intervalHours = _settingsRepo.GetInt32("backup_auto_interval_hours", 24);
            long intervalSeconds = (long)intervalHours * 3600;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            if (!_settingsRepo.TryAcquireBackupLease(intervalSeconds, now))
            {
                return BackupResult.Skipped("自動バックアップは既に他のManagerで実行されました（または間隔未到達）");
            }
            return RunBackupCore(TriggerAuto, progress, token, leaseAlreadyAcquired: true);
        }

        /// <summary>
        /// 手動バックアップ実行（lease チェックなし、即時実行）
        /// </summary>
        public BackupResult RunManualBackup(IProgress<ProgressInfo> progress, CancellationToken token)
        {
            return RunBackupCore(TriggerManual, progress, token, leaseAlreadyAcquired: false);
        }

        private BackupResult RunBackupCore(string triggerType, IProgress<ProgressInfo> progress, CancellationToken token, bool leaseAlreadyAcquired)
        {
            string pcName = Environment.MachineName;
            long startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // ファイルパスを先に確定させ、in_progress 行に最初から記録する。
            // こうすることで、後で「ファイル存在の有無」で行をリコンサイルできる。
            string destinationDir = GetEffectiveDestinationDirectory();
            string fileName = $"prism_{DateTime.Now:yyyyMMdd_HHmmss}.db";
            string destinationPath = Path.Combine(destinationDir, fileName);

            // プロジェクト移動耐性のため、prism.db のあるディレクトリからの相対パスも記録 (#126)
            // dbDir 配下に無い destinationDir (ユーザー指定の絶対パス等) では null になり、
            // 表示時は file_path にフォールバックされる。
            string dbDir = Path.GetDirectoryName(_conn.DbPath);
            string relativePath = BackupPathResolver.ToRelativeFromDbDir(destinationPath, dbDir);

            long logId = _logRepo.InsertInProgress(pcName, triggerType, startedAt, destinationPath, relativePath);

            try
            {
                progress?.Report(new ProgressInfo(0, "バックアップ準備中...", destinationPath));

                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                token.ThrowIfCancellationRequested();

                // SQLite Online Backup API でコピー
                using (var sourceConn = new SQLiteConnection(_conn.ConnectionString))
                {
                    sourceConn.Open();
                    using (var destConn = new SQLiteConnection($"Data Source={destinationPath};Version=3;"))
                    {
                        destConn.Open();
                        sourceConn.BackupDatabase(destConn, "main", "main", -1,
                            (source, sourceName, dest, destName, pages, remainingPages, totalPages, retry) =>
                            {
                                if (token.IsCancellationRequested) return false;

                                int percent = 0;
                                if (totalPages > 0)
                                {
                                    int copied = totalPages - remainingPages;
                                    percent = (int)(((double)copied / totalPages) * 100);
                                    if (percent < 0) percent = 0;
                                    if (percent > 100) percent = 100;
                                }
                                progress?.Report(new ProgressInfo(
                                    percent,
                                    "バックアップ中...",
                                    $"{totalPages - remainingPages}/{totalPages} ページ"));
                                return true;
                            },
                            100);
                    }
                }

                token.ThrowIfCancellationRequested();

                long fileSize = 0;
                if (File.Exists(destinationPath))
                {
                    fileSize = new FileInfo(destinationPath).Length;
                }

                long completedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _logRepo.MarkSuccess(logId, destinationPath, fileSize, completedAt, relativePath);

                // 手動の場合も last_backup_at を更新（自動バックアップが続けて走らないように）
                if (!leaseAlreadyAcquired)
                {
                    _settingsRepo.SetInt64("last_backup_at", completedAt);
                }

                // リテンションは成功時のみ適用
                progress?.Report(new ProgressInfo(95, "古いバックアップを整理中...", ""));
                ApplyRetention(destinationDir);

                progress?.Report(new ProgressInfo(100, "バックアップ完了", destinationPath));
                return BackupResult.Success(destinationPath, fileSize);
            }
            catch (OperationCanceledException)
            {
                long completedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _logRepo.MarkFailed(logId, "ユーザーによりキャンセルされました", completedAt);
                // 中途半端なファイルを削除
                TryDeleteIfExists(destinationPath);
                throw;
            }
            catch (Exception ex)
            {
                long completedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _logRepo.MarkFailed(logId, ex.Message, completedAt);
                TryDeleteIfExists(destinationPath);
                return BackupResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// 設定された世代数を超える古いバックアップファイルを削除
        /// </summary>
        private void ApplyRetention(string destinationDir)
        {
            int retentionCount = _settingsRepo.GetInt32("backup_retention_count", 30);
            if (retentionCount <= 0) return;

            try
            {
                var dir = new DirectoryInfo(destinationDir);
                if (!dir.Exists) return;

                var oldFiles = dir.GetFiles("prism_*.db")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(retentionCount)
                    .ToList();

                foreach (var f in oldFiles)
                {
                    try
                    {
                        f.Delete();
                        Console.WriteLine($"[BackupService] 古いバックアップを削除: {f.Name}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BackupService] 削除失敗 {f.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackupService] リテンション処理失敗: {ex.Message}");
            }
        }

        private static void TryDeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// 旧バージョン（v0.8.0 リリース直前まで）でプロジェクトルート直下に作られていた
        /// `safety_before_restore_yyyyMMdd_HHmmss.db` ファイルを `backups/safety/` 配下に移動する。
        /// 既に同名ファイルが移動先にあればスキップ（ファイルシステム的に安全）。
        /// このメソッドは Manager 起動時に1回呼ぶことを想定（idempotent）。
        /// </summary>
        /// <returns>移動した件数</returns>
        public static int MigrateLegacySafetyFilesToSafetyFolder(string dbDir)
        {
            if (string.IsNullOrEmpty(dbDir) || !Directory.Exists(dbDir)) return 0;

            string safetyDir = Path.Combine(dbDir, "backups", "safety");
            var legacyRegex = new Regex(@"^safety_before_restore_\d{8}_\d{6}\.db$");

            var legacyFiles = new DirectoryInfo(dbDir)
                .EnumerateFiles("safety_before_restore_*.db")
                .Where(f => legacyRegex.IsMatch(f.Name))
                .ToList();
            if (legacyFiles.Count == 0) return 0;

            if (!Directory.Exists(safetyDir))
            {
                Directory.CreateDirectory(safetyDir);
            }

            int moved = 0;
            foreach (var file in legacyFiles)
            {
                try
                {
                    string destPath = Path.Combine(safetyDir, file.Name);
                    if (File.Exists(destPath))
                    {
                        Console.WriteLine($"[BackupService] スキップ（既存）: {file.Name}");
                        continue;
                    }
                    file.MoveTo(destPath);
                    moved++;
                    Console.WriteLine($"[BackupService] 退避ファイル移動: {file.Name} → backups/safety/");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BackupService] 退避ファイル移動失敗 {file.Name}: {ex.Message}");
                }
            }
            return moved;
        }

        /// <summary>
        /// 退避（safety）バックアップ用フォルダのフルパスを返す。
        /// `<DBフォルダ>/backups/safety/` に固定。
        /// </summary>
        public string GetSafetyDirectory()
        {
            string dbDir = Path.GetDirectoryName(_conn.DbPath);
            return Path.Combine(dbDir, "backups", "safety");
        }

        /// <summary>
        /// バックアップディレクトリ内のファイル一覧（履歴UIから「ディスク上の実体」を見る用）
        /// </summary>
        public List<FileInfo> ListBackupFiles()
        {
            var dir = new DirectoryInfo(GetEffectiveDestinationDirectory());
            if (!dir.Exists) return new List<FileInfo>();
            return dir.GetFiles("prism_*.db")
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();
        }
    }

    /// <summary>
    /// バックアップ実行結果
    /// </summary>
    public class BackupResult
    {
        public enum ResultKind { SuccessKind, SkippedKind, FailedKind }

        public ResultKind Kind { get; private set; }
        public string FilePath { get; private set; }
        public long FileSizeBytes { get; private set; }
        public string Message { get; private set; }

        public bool IsSuccess { get { return Kind == ResultKind.SuccessKind; } }
        public bool IsSkipped { get { return Kind == ResultKind.SkippedKind; } }
        public bool IsFailed { get { return Kind == ResultKind.FailedKind; } }

        public static BackupResult Success(string filePath, long fileSizeBytes)
        {
            return new BackupResult { Kind = ResultKind.SuccessKind, FilePath = filePath, FileSizeBytes = fileSizeBytes };
        }
        public static BackupResult Skipped(string reason)
        {
            return new BackupResult { Kind = ResultKind.SkippedKind, Message = reason };
        }
        public static BackupResult Failed(string errorMessage)
        {
            return new BackupResult { Kind = ResultKind.FailedKind, Message = errorMessage };
        }
    }
}
