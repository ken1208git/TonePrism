using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// データベースバックアップ機能のドメインロジック。
    /// SQLite Online Backup API を用いて toneprism.db のスナップショットを取得し、
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
        /// (#170 followup round 3 review L-4) 「自動バックアップが UI 上で有効か」の判定 SoT helper。
        /// `IsAutoBackupDue` と `RunAutoBackupIfDue` の両方で参照、`"false"` 厳密一致 (case-insensitive) で
        /// disabled、それ以外 (空 / "true" / unknown) はすべて enabled 扱い。
        /// 旧実装は両関数で同じ判定 string を重複記述しており、将来 enum 化等で片方更新漏れの drift 路があった。
        /// </summary>
        private bool IsAutoBackupEnabled()
        {
            string enabledStr = _settingsRepo.GetString(SettingsKeys.BackupAutoEnabled, "true");
            return !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 自動バックアップを走らせるべきかチェック（参照のみ、副作用なし）。
        /// 実際の実行時には RunAutoBackupIfDue を使う（lease 取得を含む）。
        /// </summary>
        public bool IsAutoBackupDue()
        {
            // (#170 followup round 2 review #1+#2) 自動バックアップ無効化 checkbox。UI 設定タブから OFF
            // にされていたら due 判定を常に false (= 起動時 trigger 完全 skip、手動バックアップは引き続き可)。
            // 旧実装は `RunAutoBackupIfDue` 側のみ gate を入れ、IsAutoBackupDue は disable flag を見ていない
            // 非対称設計だった。それにより MainForm.StartAutoBackupIfDue が「Due だから走らせる」と判断して
            // 「実行中...」indicator を出した直後、RunAutoBackupIfDue が IsSkipped を返して MainForm の
            // IsSkipped 分岐は no-op、indicator が「実行中...」のまま永久残留する bug があった。
            // 本 fix で IsAutoBackupDue 側も同 gate を持たせて「Due だけど skip」の不整合 path を構造閉鎖、
            // CHANGELOG `## Manager v0.13.0` の「IsAutoBackupDue / RunAutoBackupIfDue 両方 skip」記述とも
            // 整合させる。判定 string は round 3 L-4 で IsAutoBackupEnabled helper に集約。
            if (!IsAutoBackupEnabled()) return false;
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
            // (#170 followup round 2) 自動バックアップ無効化 checkbox。OFF なら起動時 trigger を skip。
            // (round 3 L-4) 判定 string は IsAutoBackupEnabled helper に集約。
            // (round 4 review L-2) 現状の唯一の caller `MainForm.StartAutoBackupIfDue` は事前に
            // `IsAutoBackupDue` で同 check を通過するため本 path は production code から到達不能 (= dead path)。
            // 将来 RunAutoBackupIfDue を IsAutoBackupDue 経由なしで直接 caller が増えた時の defensive gate
            // として残置 + Skipped message も将来 user 視点で見せる用途で維持。
            if (!IsAutoBackupEnabled())
            {
                return BackupResult.Skipped("自動バックアップが無効に設定されています (設定タブから有効化可能)");
            }
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
            //
            // (追加精査 ⑥) yyyyMMdd_HHmmss は 1 秒粒度なので、同 1 秒に複数 PC が auto/manual を発火すると
            // ファイル名衝突が起きる。SQLiteConnection に既存パスを渡すと BackupDatabase が destination の
            // tables 全置換で silent 上書きとなり、前のバックアップが破壊される。File.Exists で衝突 check し、
            // 衝突時は _2 / _3 ... の suffix を付与。100 件衝突はあり得ないので safety limit を入れて throw。
            // 既存ファイル名 (yyyyMMdd_HHmmss.db) との互換性のため、衝突時のみ suffix を追加する形式とする
            // (BackupLogRepository.RecoverLegacyFailedEntriesByFolderScan 等の旧版救済 regex に影響しない)。
            string destinationDir = GetEffectiveDestinationDirectory();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"toneprism_{timestamp}.db";
            string destinationPath = Path.Combine(destinationDir, fileName);
            int collisionSuffix = 2;
            while (File.Exists(destinationPath))
            {
                fileName = $"toneprism_{timestamp}_{collisionSuffix}.db";
                destinationPath = Path.Combine(destinationDir, fileName);
                collisionSuffix++;
                if (collisionSuffix > 99)
                {
                    throw new Exception($"バックアップファイル名の衝突回避に失敗しました (同 1 秒に 100 件以上の衝突): {destinationDir}");
                }
            }

            // プロジェクト移動耐性のため、toneprism.db のあるディレクトリからの相対パスも記録 (#126)
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
        /// 設定された世代数を超える古い**自動**バックアップファイルを削除する (#235)。
        ///
        /// **trigger_type='auto' AND status='success'** の行に紐づくファイルのみが対象。手動取得 (manual) /
        /// 復元前の自動退避 (safety) / 失敗履歴 (failed) は絶対に削除しない。旧実装はファイル名パターン
        /// `toneprism_*.db` だけで判定していたため、ファイル名から区別できない手動取得分も silent に消えていた。
        ///
        /// 本実装は backup_log を SoT にする (= DB に登録されていないファイルは触らない、= 自動 retention は
        /// 「自分が取った自動バックアップだけ整理する」最小権限で動く)。DB 行は削除しない (= 既存挙動互換、
        /// BackupSectionPanel の表示は File.Exists フィルタで自然に hide される)。
        ///
        /// destinationDir 引数は将来「設定で保存先を変えた直後の旧フォルダ清掃」等の拡張のため受けるが、
        /// 現状は backup_log の file_path / relative_path が SoT なので未使用。
        /// </summary>
        private void ApplyRetention(string destinationDir)
        {
            int retentionCount = _settingsRepo.GetInt32("backup_retention_count", 30);
            if (retentionCount <= 0) return;

            try
            {
                var targets = _logRepo.GetAutoSuccessRetentionTargets(retentionCount);
                if (targets.Count == 0) return;

                string dbPath = _conn.DbPath;
                foreach (var entry in targets)
                {
                    string resolvedPath = BackupPathResolver.ResolveAbsolutePath(entry, dbPath);
                    if (string.IsNullOrEmpty(resolvedPath)) continue;
                    if (!File.Exists(resolvedPath))
                    {
                        // 既に消えている (= 手動で削除された等)。DB 行はそのまま残置 (表示は File.Exists で hide)。
                        continue;
                    }

                    try
                    {
                        File.Delete(resolvedPath);
                        Logger.Info($"[BackupService] 古い自動バックアップを削除 (#235 trigger_type=auto に限定): {resolvedPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[BackupService] 削除失敗 {resolvedPath}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[BackupService] リテンション処理失敗", ex);
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
                        Logger.Warn($"[BackupService] スキップ（既存）: {file.Name}");
                        continue;
                    }
                    file.MoveTo(destPath);
                    moved++;
                    Logger.Info($"[BackupService] 退避ファイル移動: {file.Name} → backups/safety/");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[BackupService] 退避ファイル移動失敗 {file.Name}", ex);
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
            return dir.GetFiles("toneprism_*.db")
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
