using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using TonePrism.Manager.Repositories;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// バックアップファイルから toneprism.db を復元するサービス。
    /// 安全のため、復元前に現DBを backups/safety/safety_*.db として退避する。
    /// </summary>
    public class RestoreService
    {
        public const int DefaultSafetyRetentionCount = 10;

        private readonly DatabaseConnection _conn;
        private readonly BackupLogRepository _logRepo;
        private readonly SettingsRepository _settingsRepo;

        /// <summary>
        /// (H4) 直近の Restore 呼出しの開始 Unix 秒。caller (BackupSectionPanel) が成功時の audit 行を
        /// NEW DB (= InitializeDatabase 完了後の v17 schema) に INSERT する際の started_at として使う。
        /// </summary>
        public long LastRestoreStartedAt { get; private set; }

        public RestoreService(DatabaseConnection conn, BackupLogRepository logRepo, SettingsRepository settingsRepo)
        {
            _conn = conn;
            _logRepo = logRepo;
            _settingsRepo = settingsRepo;
        }

        /// <summary>
        /// 指定されたバックアップファイルから toneprism.db を復元する（atomic 復元）。
        /// 1. 現DBを backups/safety/safety_HHmmss.db として退避（Online Backup API でコピー）
        /// 2. バックアップを toneprism.db.restore-tmp に先にコピー（toneprism.db はまだ無傷）
        /// 3. 全 SQLite 接続プールをクリア
        ///   ─── ここから先はキャンセル不可（中断すると toneprism.db が一時的に欠落） ───
        /// 4. 旧 WAL/SHM を削除し、toneprism.db を tmp で置換（File.Replace で atomic）
        /// 5. backups/safety/ のリテンション適用（古いものから削除）
        ///
        /// 利点: コピー失敗・キャンセル発生時に toneprism.db が消えてしまう事故を回避
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

            // (監査ログ) 復元イベントの開始 / 各フェーズ / 完了を Logger.Info に残す。旧実装は本 service /
            // BackupSectionPanel ともに復元の audit trail を一切残しておらず、エラー時に MessageBox が
            // 「詳細はログを確認」と促すのに該当ログが空 という不整合があった。throw 経路には Logger.Error を
            // 必ず挟んで例外詳細を残す (caller の ProcessingDialog catch では例外型情報を握って破棄するため)。
            Logger.Info($"[RestoreService] 復元開始: source='{backupFilePath}', target='{dbPath}'");

            // (H4) backup_log に復元イベントの audit 行を残す。Logger.Info の file log だけだと
            // ローテーション / 別 PC コピー / 手動 truncate で消失するため、DB 行として永続化する。
            // trigger_type='restore' は v16 migration で CHECK 拡張済。file_path は復元元のバックアップを記録
            // (safety ファイルではなく source、= 「どのバックアップから復元したか」が監査の主目的)。
            string pcName = Environment.MachineName;
            long startedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long startedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long logId = 0;

            // (H5) LAN advisory lock を取得。他 PC が active な restore lock を保有していたら
            // user 操作意図に関わらず取得失敗 → throw して開始前に中止 (caller は BackupSectionPanel
            // の Abort 経路に流れる)。`File.Replace` の OS-level atomic 性は別物だが、coordination layer
            // として他 Manager の write 操作を SessionConflictHelper 経由で block する fence になる。
            if (_settingsRepo != null)
            {
                bool acquired = _settingsRepo.TryAcquireRestoreLock(
                    pcName, startedAtMs, Services.SettingsKeys.RestoreLockStaleThresholdMs,
                    out string activeOwner);
                if (!acquired)
                {
                    string msg = $"他 PC ({activeOwner}) が現在データベース復元中のため、復元を開始できません。完了後に再試行してください。";
                    Logger.Error("[RestoreService] " + msg);
                    throw new InvalidOperationException(msg);
                }
                Logger.Info("[RestoreService] restore advisory lock 取得: pcName=" + pcName);
            }

            try
            {
                logId = _logRepo.InsertInProgress(pcName, "restore", startedAt, backupFilePath);
            }
            catch (Exception logEx)
            {
                // audit 行の挿入失敗で復元自体を止めない (= audit 不能でも user の復元意図を尊重)。
                // Logger に warn を残して継続。
                Logger.Warn("[RestoreService] backup_log に restore audit 行を挿入できませんでした (復元処理は継続): " + logEx.Message);
            }

            try
            {
                progress?.Report(new ProgressInfo(0, "復元の準備...", ""));
                token.ThrowIfCancellationRequested();

                // 退避フォルダを必ず作成
                if (!Directory.Exists(safetyDir))
                {
                    Directory.CreateDirectory(safetyDir);
                }

                // 1. 現DBを安全バックアップ（Online Backup API でライブコピー）
                // (追加精査 ⑥) BackupService と同様、yyyyMMdd_HHmmss の同 1 秒衝突を suffix で回避する。
                // 復元連打は UI 上 ProcessingDialog で block されるため発生確率は低いが、防御ラインとして
                // BackupService と同じ pattern に揃える。
                string safetyTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safetyPath = Path.Combine(safetyDir, $"safety_{safetyTimestamp}.db");
                int safetyCollisionSuffix = 2;
                while (File.Exists(safetyPath))
                {
                    safetyPath = Path.Combine(safetyDir, $"safety_{safetyTimestamp}_{safetyCollisionSuffix}.db");
                    safetyCollisionSuffix++;
                    if (safetyCollisionSuffix > 99)
                    {
                        throw new Exception($"退避ファイル名の衝突回避に失敗しました (同 1 秒に 100 件以上の衝突): {safetyDir}");
                    }
                }
                progress?.Report(new ProgressInfo(10, "現在のデータベースを退避中...", safetyPath));

                if (File.Exists(dbPath))
                {
                    // (累積監査 round 4 Medium-18) BackupService と同じく `OpenConnectionWithJournalMode` 経由で
                    // PRAGMA 統一。busy_timeout=10000 で別 Manager 書込中の SQLITE_BUSY 即 throw を抑制する。
                    using (var sourceConn = new SQLiteConnection(_conn.ConnectionString))
                    {
                        _conn.OpenConnectionWithJournalMode(sourceConn);
                        using (var destConn = new SQLiteConnection($"Data Source={safetyPath};Version=3;"))
                        {
                            destConn.Open();
                            sourceConn.BackupDatabase(destConn, "main", "main", -1, null, 0);
                        }
                    }
                    Logger.Info($"[RestoreService] 現DB を退避しました: '{safetyPath}'");
                }
                else
                {
                    Logger.Warn($"[RestoreService] 現DB ('{dbPath}') が存在しないため退避を skip しました (新規 DB 復元 path)");
                }

                token.ThrowIfCancellationRequested();

                // 2. バックアップを一時ファイルへ先にコピー（toneprism.db はまだ無傷）。
                //    コピー中・コピー後にキャンセル/失敗が起きても toneprism.db は壊れない。
                progress?.Report(new ProgressInfo(40, "バックアップを準備中...", backupFilePath));
                if (File.Exists(tempPath)) File.Delete(tempPath); // 前回失敗時の残骸があれば消す
                File.Copy(backupFilePath, tempPath, false);

                token.ThrowIfCancellationRequested();

                // 3. 全 SQLite 接続プールをクリアし、ハンドルを解放
                progress?.Report(new ProgressInfo(60, "データベース接続を閉じています...", ""));
                SQLiteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // ─── ここから先はキャンセル不可。中断すると toneprism.db が一時的に欠落する ───
                // 4. WAL/SHM を削除し、tmp で toneprism.db を置換
                progress?.Report(new ProgressInfo(80, "既存ファイルを置き換え中...", ""));
                DeleteWithRetry(dbPath + "-wal");
                DeleteWithRetry(dbPath + "-shm");

                if (File.Exists(dbPath))
                {
                    // File.Replace は NTFS 上で atomic（ReplaceFile Win32 API）
                    // backupFileName=null で旧 toneprism.db のバックアップは作らない（safety で既に確保済み）
                    //
                    // (追加精査 ⑨) ReplaceFile は両 path が同一ボリュームでないと失敗する。SMB DFS /
                    // Junction Point / Symbolic Link が介在する構成では IOException で fail し得る。
                    // safety は既に取れているのでデータ消失は無いが、tempPath + WAL/SHM 削除済みの
                    // 中途半端な状態が残り手動復旧が要る。catch で Delete + Move 経路に fallback して
                    // 復元自体は通す (atomicity は落ちるが safety で担保済み)。
                    try
                    {
                        File.Replace(tempPath, dbPath, null);
                    }
                    catch (IOException replaceEx)
                    {
                        Logger.Warn("[RestoreService] File.Replace 失敗 (SMB/Junction 等の可能性)、Delete + Move 経路に fallback: " + replaceEx.Message);
                        File.Delete(dbPath);
                        File.Move(tempPath, dbPath);
                    }
                    catch (UnauthorizedAccessException replaceEx)
                    {
                        Logger.Warn("[RestoreService] File.Replace 権限エラー、Delete + Move 経路に fallback: " + replaceEx.Message);
                        File.Delete(dbPath);
                        File.Move(tempPath, dbPath);
                    }
                }
                else
                {
                    // 新規 DB の場合（通常想定外だが安全対策）
                    File.Move(tempPath, dbPath);
                }
                Logger.Info($"[RestoreService] toneprism.db を置換しました ('{backupFilePath}' → '{dbPath}')");

                // (累積監査 round 4 High-6) ─── point of no return: ここから先 (post-step) の例外は復元失敗扱い
                // しない ─── 旧実装は post-step (= ApplySafetyRetention の外側で起きた予期せぬ例外、
                // progress?.Report 中の throw 等) を外側 catch (Exception) で拾い、(a) MarkAuditFailed の UPDATE が
                // NEW DB の logId に届かず silent no-op、(b) caller の ProcessingDialog に throw して
                // 「復元失敗」MessageBox → ユーザーが二重復元する事故、の 2 つの誤動作があった。post-step を内側
                // try で囲み swallow + Logger.Warn にすることで外側 catch を pre-replace 失敗専用に絞り、
                // dbReplaceCompleted flag を持たずに同じ point-of-no-return 性質を達成する。
                try
                {
                    // 5. 退避フォルダのリテンション適用（最新を残して古いのから削除）
                    progress?.Report(new ProgressInfo(95, "退避ファイルの世代管理...", ""));
                    ApplySafetyRetention(safetyDir, DefaultSafetyRetentionCount);

                    progress?.Report(new ProgressInfo(100, "復元完了", dbPath));
                }
                catch (Exception postEx)
                {
                    Logger.Warn($"[RestoreService] (High-6) DB 置換後の post-step 例外を swallow (復元自体は成功): {postEx.Message}");
                }

                Logger.Info($"[RestoreService] 復元完了: source='{backupFilePath}', safety='{safetyPath}'");

                // (H4) 注意: 起動時に挿入した in_progress 行は OLD DB (= safety バックアップに保存される版)
                // にしかない。File.Replace 後の NEW DB には該当行が存在しないため、ここで MarkSuccess UPDATE
                // を打っても 0 行更新で silent no-op になる。NEW DB に成功の audit 行を残す責務は呼び出し側
                // (BackupSectionPanel) が InitializeDatabase 完了後 (= v17 schema 確定後) に
                // BackupLogRepository.LogRestoreSucceeded で別行を INSERT する形で担う。
                // ここでは safetyPath を返すのみ。
                LastRestoreStartedAt = startedAt;
                return safetyPath;
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[RestoreService] 復元キャンセル: source='{backupFilePath}'");
                MarkAuditFailed(logId, "ユーザーによりキャンセルされました");
                // (累積監査 round 3) 一時ファイルが残置されると次回復元時 L154 の File.Delete で消えるが、
                // 復元失敗が連続する展示 PC (SSD 小容量) では disk 逼迫 → File.Copy 失敗の連鎖を誘発する。
                // catch 経路で必ず後始末する (両 catch で対称化、failure 自体は swallow して例外伝播を優先)。
                TryDeleteTempFile(tempPath);
                throw;
            }
            catch (Exception ex)
            {
                // (High-6) post-step 例外は内側 try で swallow 済のため、ここに到達する例外は必ず pre-replace 失敗。
                // = 「DB は無傷 + safety は取れたかもしれない / tempPath 残存」状態なので、MarkAuditFailed + tempPath 削除 + throw が正しい。
                Logger.Error($"[RestoreService] 復元失敗: source='{backupFilePath}'", ex);
                MarkAuditFailed(logId, ex.Message);
                TryDeleteTempFile(tempPath);
                throw;
            }
            finally
            {
                // (H5) advisory restore-lock を解放。lock 取得済の場合のみ delete (= 他 PC 保有 lock は触らない)。
                // ReleaseRestoreLock は SQL の LIKE 句で「自 PC 保有行のみ削除」を保証している。
                if (_settingsRepo != null)
                {
                    try
                    {
                        _settingsRepo.ReleaseRestoreLock(pcName);
                        Logger.Info("[RestoreService] restore advisory lock 解放: pcName=" + pcName);
                    }
                    catch (Exception releaseEx)
                    {
                        Logger.Warn("[RestoreService] restore advisory lock 解放失敗 (stale lock 5 分後に自動失効): " + releaseEx.Message);
                    }
                }
            }
        }

        /// <summary>(H4) 失敗 / cancel 経路から audit 行を failed に確定する内部ヘルパ。</summary>
        private void MarkAuditFailed(long logId, string reason)
        {
            if (logId <= 0) return;
            try
            {
                long completedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _logRepo.MarkFailed(logId, reason ?? "", completedAt);
            }
            catch (Exception markEx)
            {
                Logger.Warn("[RestoreService] backup_log restore failed 記録に失敗: " + markEx.Message);
            }
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

        /// <summary>
        /// (累積監査 round 3) 復元失敗 / cancel 時に tempPath を best-effort で削除する。失敗は握り潰す
        /// (Logger.Warn のみ)、上位 catch の throw を優先する。tempPath が存在しないケースも no-op で安全。
        /// </summary>
        private static void TryDeleteTempFile(string tempPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                    Logger.Info($"[RestoreService] 一時ファイルを削除しました: '{tempPath}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[RestoreService] 一時ファイル削除失敗 (next attempt の L154 cleanup で再試行されます): '{tempPath}': {ex.Message}");
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
