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
            // (累積監査 round 6 M5) 時計異常 (未来日時) で last_backup_at が未来値になっていると
            // due 判定が恒偽になり自動バックアップが永久 skip される。TryAcquireBackupLease 側と
            // 同じ sanity を入れ、1 日以上未来の値は 0 扱いで「due」と判定して取得を促す。
            if (lastBackupAt > now + 86400)
            {
                lastBackupAt = 0;
            }
            return lastBackupAt + (long)intervalHours * 3600 <= now;
        }

        /// <summary>
        /// 設定された保存先パスを返す。空ならデフォルト（DBフォルダ/backups/）を返す。
        /// (累積監査 round 4 Medium-17) 危険 path (`C:\Windows` 配下 等) を fence する軽い guard を入れて、user の
        /// path 誤入力でバックアップが OS 重要領域に散布される運用事故を防ぐ。実害は OS 権限で大半 block されるが
        /// 警告 path も書込可能領域 (`%APPDATA%` 等) には届くため、Logger.Warn + デフォルトへの fall-back で
        /// silent な誤誘導を避ける。判定は最小: drive root 直下 + `%WinDir%` 配下 + 解決失敗のみ。
        /// </summary>
        public string GetEffectiveDestinationDirectory()
        {
            string configured = _settingsRepo.GetString("backup_destination_path", "");
            string dbDir = Path.GetDirectoryName(_conn.DbPath);
            string defaultDir = Path.Combine(dbDir, "backups");
            if (string.IsNullOrWhiteSpace(configured)) return defaultDir;

            string resolved;
            try { resolved = Path.GetFullPath(configured); }
            catch (Exception ex)
            {
                Logger.Warn("[BackupService] (M17) backup_destination_path の正規化に失敗、デフォルトに fall-back: " + configured + " err=" + ex.Message);
                return defaultDir;
            }

            // drive root 直下 (= "C:\backups" の正規形が "C:\" になるケース) は除外。
            string rootPath = Path.GetPathRoot(resolved);
            if (!string.IsNullOrEmpty(rootPath) && string.Equals(resolved.TrimEnd(Path.DirectorySeparatorChar), rootPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn("[BackupService] (M17) backup_destination_path が drive root 直下のため危険、デフォルトに fall-back: " + resolved);
                return defaultDir;
            }

            // %WinDir% (= C:\Windows 等) 配下は除外。OS 重要領域へのバックアップ散布を防ぐ。
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir))
            {
                string winDirSep = winDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (resolved.StartsWith(winDirSep, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(resolved.TrimEnd(Path.DirectorySeparatorChar), winDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn("[BackupService] (M17) backup_destination_path が %WinDir% 配下のため除外、デフォルトに fall-back: " + resolved);
                    return defaultDir;
                }
            }

            return configured;
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

            // (round 5 H2) round 2 H5 で導入した「他 PC が復元中なら write をブロック」する advisory lock の
            // 横展開漏れ補正。旧実装は MainForm.CheckSessionConflictBeforeWrite (user 操作経路のみ) で
            // GetActiveRestoreLockOwnerOrNull を check しており、起動時の自動バックアップ path は素通し
            // だった。PC-A の File.Replace 中に PC-B の Manager が起動 → 自動バックアップが SQLite Online
            // Backup API で File.Replace 中の DB を読みに行く → 出力 backup が partial / corrupt になり、
            // 後日それを復元すると最新データ消失する致命的 race があった。auto path にも同 gate を入れて
            // 構造的に防ぐ。
            if (_settingsRepo != null)
            {
                string lockOwner = _settingsRepo.GetActiveRestoreLockOwnerOrNull(
                    Environment.MachineName,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    SettingsKeys.RestoreLockStaleThresholdMs);
                if (!string.IsNullOrEmpty(lockOwner))
                {
                    Logger.Info("[BackupService] (round 5 H2) 他 PC (" + lockOwner + ") が復元中のため自動バックアップを延期");
                    return BackupResult.Skipped("他 PC (" + lockOwner + ") が復元中のため自動バックアップを延期しました");
                }
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
            // (round 5 H2 defense-in-depth) 手動経路は通常 UI 側 SessionConflictHelper.CheckBeforeWrite で
            // 既に lock 確認済だが、Manager 外から direct 呼出される将来の caller のために二段目 fence。
            if (_settingsRepo != null)
            {
                string lockOwner = _settingsRepo.GetActiveRestoreLockOwnerOrNull(
                    Environment.MachineName,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    SettingsKeys.RestoreLockStaleThresholdMs);
                if (!string.IsNullOrEmpty(lockOwner))
                {
                    Logger.Warn("[BackupService] (round 5 H2) 他 PC (" + lockOwner + ") が復元中のため手動バックアップを中止");
                    return BackupResult.Skipped("他 PC (" + lockOwner + ") が復元中のためバックアップを開始できません。完了後に再試行してください");
                }
            }
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
            // (L) 2 PC が SMB 共有経由で同 1 秒に backup を発火した場合、双方 File.Exists=false で同 path に書き込み
            // 出力ファイル破損する LAN race を緩和。collision 検出時の suffix に PC 名を mix することで、
            // 「同 PC 内連射 = `_2` `_3` ...」と「別 PC 由来 = `_<pcName>`」を分離。pcName が空文字の defensive
            // case は従来の数値 suffix に fall through。
            int collisionSuffix = 2;
            while (File.Exists(destinationPath))
            {
                string suffix = string.IsNullOrEmpty(pcName)
                    ? collisionSuffix.ToString()
                    : (collisionSuffix == 2 ? pcName : pcName + "_" + (collisionSuffix - 1));
                fileName = $"toneprism_{timestamp}_{suffix}.db";
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
                // (累積監査 round 4 Medium-18) source 接続は `OpenConnectionWithJournalMode` で
                // `journal_mode=DELETE` / `busy_timeout=10000` / `foreign_keys=ON` / `synchronous=NORMAL` を確実に
                // 適用する。Online Backup API は journal mode 非依存だが、別 Manager / Launcher が書込中の場合に
                // sourceConn.Open() が `SQLITE_BUSY` で即 throw する確率を下げる (busy_timeout=10000 で 10s 待機) +
                // 将来「Backup 取得直前にチェック SQL」等の拡張で FK off の silent drift を防ぐ convention 統一。
                using (var sourceConn = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(sourceConn);
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

                // (累積監査 round 6 M9) Online Backup API の dest 接続が close 時に -wal / -shm / -journal の
                // sibling を残すケースが System.Data.SQLite の版によって報告されている。本体 .db は単独で完結
                // すべきで、sibling が残ると user がバックアップファイルを別フォルダへ手動 move した際に
                // 置き去りになり、復元時に古い journal が誤適用されて内容が巻き戻る稀な事故につながる。
                // backup 完了直後 (両接続 close 後) に sibling を best-effort で掃除する。
                TryDeleteIfExists(destinationPath + "-wal");
                TryDeleteIfExists(destinationPath + "-shm");
                TryDeleteIfExists(destinationPath + "-journal");

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
                        // (M6) DB 行も同時削除。旧実装は file のみ削除して DB 行を残置、UI で「最終バックアップ:
                        // ファイル無し」表示や RestoreConfirmForm の選択候補に残る不整合があった。
                        try { _logRepo.DeleteById(entry.Id); }
                        catch (Exception delEx)
                        {
                            Logger.Warn($"[BackupService] (M6) backup_log 行削除失敗 id={entry.Id}: " + delEx.Message);
                            // (累積監査 round 3 / #10) 物理 file は削除成功・DB 行 delete だけ失敗するケース、
                            // 旧実装は Logger.Warn で swallow するだけだったため次回の GetAutoSuccessRetentionTargets
                            // が当該行を「auto+success の 1 件」として count に含めてしまい、retention 件数が
                            // 1 件ずつ目減りする silent drift を起こしていた。failed に格下げて (status='failed')
                            // 以降の count から外し、retention が想定 keep 件数で運用される状態を維持する。
                            // status='failed' 化は audit trail としても残る (= 「実体削除済、DB delete fail で
                            // failed 化」の trail)。本格下げ自体が更に失敗した場合は Logger.Error のみで継続 (=
                            // 起動 / 後続バックアップは止めない、ApplyRetention は best-effort セマンティクス)。
                            try
                            {
                                long completedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                _logRepo.MarkFailed(entry.Id,
                                    "retention 削除中に DB 行削除失敗 (実体は削除済): " + delEx.Message,
                                    completedAt);
                                Logger.Info($"[BackupService] (#10) DB delete 失敗行を failed に格下げ: id={entry.Id}");
                            }
                            catch (Exception markEx)
                            {
                                Logger.Error($"[BackupService] (#10) failed への格下げにも失敗: id={entry.Id}", markEx);
                            }
                        }
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
                    // (累積監査 round 4 Medium-20) 衝突時 skip すると旧 path のファイルがプロジェクトルート直下に
                    // 永久残置し、起動毎に warn が連発 + user が「これは何？」と削除して safety を失う事故になる。
                    // 衝突時は suffix (`_dup_N`) を付けて必ず移動完了させる契約に変える。
                    if (File.Exists(destPath))
                    {
                        string baseName = Path.GetFileNameWithoutExtension(file.Name);
                        string ext = Path.GetExtension(file.Name);
                        int dupSuffix = 1;
                        while (File.Exists(destPath) && dupSuffix < 100)
                        {
                            destPath = Path.Combine(safetyDir, $"{baseName}_dup_{dupSuffix}{ext}");
                            dupSuffix++;
                        }
                        if (File.Exists(destPath))
                        {
                            Logger.Warn($"[BackupService] (M20) 退避ファイル移動 skip (100 件以上の dup_N 衝突): {file.Name}");
                            continue;
                        }
                        Logger.Warn($"[BackupService] (M20) 退避ファイル名衝突を suffix で回避: {file.Name} → {Path.GetFileName(destPath)}");
                    }
                    file.MoveTo(destPath);
                    moved++;
                    Logger.Info($"[BackupService] 退避ファイル移動: {file.Name} → backups/safety/{Path.GetFileName(destPath)}");
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
