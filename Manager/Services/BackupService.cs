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
        private readonly SettingsRepository _settingsRepo;

        public BackupService(DatabaseConnection conn, SettingsRepository settingsRepo)
        {
            _conn = conn;
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

            if (!_settingsRepo.TryAcquireBackupLease(intervalSeconds, now, out long previousLastBackupAt))
            {
                return BackupResult.Skipped("自動バックアップは既に他のManagerで実行されました（または間隔未到達）");
            }
            return RunBackupCore(TriggerAuto, progress, token, leaseAlreadyAcquired: true, leasePreviousValue: previousLastBackupAt);
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

        private BackupResult RunBackupCore(string triggerType, IProgress<ProgressInfo> progress, CancellationToken token, bool leaseAlreadyAcquired, long leasePreviousValue = 0)
        {
            string host = SanitizeHostForFileName(Environment.MachineName);

            // auto / manual を種類ごとのサブフォルダに分け、ファイル名も `<種類>_<日時>_<host>.db` に統一する
            // (退避 safety_*.db と同じ流儀)。フォルダ位置で種類が、ファイル名 host で実行 PC が確定するため、
            // backup_log を持たずとも BackupCatalogService が履歴を完全に復元でき、auto の世代管理 (retention)
            // も正しく効く。triggerType は RunAutoBackupIfDue / RunManualBackup から "auto" / "manual" のみが
            // 渡るため、そのままサブフォルダ名 + ファイル名接頭辞として使える。
            string destinationDir = Path.Combine(GetEffectiveDestinationDirectory(), triggerType);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = string.IsNullOrEmpty(host)
                ? $"{triggerType}_{timestamp}"
                : $"{triggerType}_{timestamp}_{host}";
            string destinationPath = Path.Combine(destinationDir, baseName + ".db");
            // 同一 PC が同 1 秒に複数回発火した場合のみ `_2` / `_3` ... を付ける。別 PC は host が違うので
            // baseName 自体が分離して衝突しない (旧実装の pcName-mix 衝突回避を host 常時埋め込みで一本化)。
            // SQLiteConnection に既存パスを渡すと BackupDatabase が destination の tables を全置換して前の
            // バックアップを silent 破壊するため File.Exists で衝突を避ける。100 件衝突は safety limit で throw。
            int collisionSuffix = 2;
            while (File.Exists(destinationPath))
            {
                destinationPath = Path.Combine(destinationDir, $"{baseName}_{collisionSuffix}.db");
                collisionSuffix++;
                if (collisionSuffix > 99)
                {
                    throw new Exception($"バックアップファイル名の衝突回避に失敗しました (同 1 秒に 100 件以上の衝突): {destinationDir}");
                }
            }

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

                // (Finding #5) success 記録の前に出力 DB を検証する。BackupDatabase が例外を投げずに戻っても、
                // 出力が空 / 不完全なら success として記録すると「最終バックアップ」表示や復元候補に中身のない
                // バックアップが正常として残り、後日それを復元すると空 DB を掴む最悪事故になる。(a) ファイルが
                // 存在しサイズ > 0、(b) PRAGMA quick_check が "ok"、の両方を満たさなければ例外を投げ、下の
                // catch で failed 記録 + ファイル削除 + lease 巻き戻しに流す。
                if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length == 0)
                {
                    throw new Exception("バックアップファイルが作成されていない、または空です: " + destinationPath);
                }
                VerifyBackupIntegrity(destinationPath);
                // VerifyBackupIntegrity が開いた read 接続が稀に sibling を残す版があるため再掃除。
                TryDeleteIfExists(destinationPath + "-wal");
                TryDeleteIfExists(destinationPath + "-shm");
                TryDeleteIfExists(destinationPath + "-journal");

                token.ThrowIfCancellationRequested();

                long fileSize = 0;
                if (File.Exists(destinationPath))
                {
                    fileSize = new FileInfo(destinationPath).Length;
                }

                // 手動の場合も last_backup_at を更新（自動バックアップが続けて走らないように）。
                if (!leaseAlreadyAcquired)
                {
                    _settingsRepo.SetInt64("last_backup_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                }

                // リテンションは成功時のみ適用
                progress?.Report(new ProgressInfo(95, "古いバックアップを整理中...", ""));
                ApplyRetention();

                progress?.Report(new ProgressInfo(100, "バックアップ完了", destinationPath));
                return BackupResult.Success(destinationPath, fileSize);
            }
            catch (OperationCanceledException)
            {
                // 中途半端なファイルを削除
                TryDeleteIfExists(destinationPath);
                RollbackLeaseOnFailure(leaseAlreadyAcquired, leasePreviousValue);
                throw;
            }
            catch (Exception ex)
            {
                // (失敗履歴は backup_log 廃止に伴い Logger のみに残す)
                Logger.Error("[BackupService] バックアップに失敗しました: " + destinationPath, ex);
                TryDeleteIfExists(destinationPath);
                RollbackLeaseOnFailure(leaseAlreadyAcquired, leasePreviousValue);
                return BackupResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// (累積監査) auto バックアップが失敗 / キャンセルした場合、lease で前進させた last_backup_at を
        /// 取得前の値へ巻き戻す。これにより次回 (起動時) の due 判定で再試行される (旧実装は失敗しても
        /// last_backup_at が now に前進したままで、次の interval まで再試行されなかった)。手動経路
        /// (leaseAlreadyAcquired=false) は lease を取らないため no-op。自動バックアップの起動は
        /// MainForm.StartAutoBackupIfDue で「起動時 1 回」のため、巻き戻しても連射 (= 警告 modal の連発) には
        /// ならない。巻き戻し自体の失敗は握り潰す (最悪でも従来挙動 = 次 interval 待ちに戻るだけ)。
        /// </summary>
        /// <summary>
        /// (Finding #5) 作成したバックアップ DB を `PRAGMA quick_check` で検証する。"ok" 以外なら例外を投げ、
        /// caller の catch で failed 記録 + ファイル削除 + lease 巻き戻しに流す (= 壊れた / 不完全なバックアップを
        /// success として残さない)。検証は read のみ。書き込まないが、念のため caller 側で検証後に
        /// -wal/-shm/-journal sibling を再掃除する。quick_check は integrity_check より軽量で、
        /// ページ単位の連結・B-tree 構造の妥当性を確認する (索引の中身までは見ないが、空 / 切り詰め /
        /// 途中切れの検出には十分)。
        /// </summary>
        private void VerifyBackupIntegrity(string path)
        {
            string result = null;
            using (var conn = new SQLiteConnection($"Data Source={path};Version=3;"))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA quick_check;";
                    var scalar = cmd.ExecuteScalar();
                    result = scalar?.ToString();
                }
            }
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("バックアップ DB の整合性チェック (quick_check) に失敗しました: " + (result ?? "(結果なし)"));
            }
        }

        private void RollbackLeaseOnFailure(bool leaseAlreadyAcquired, long leasePreviousValue)
        {
            if (!leaseAlreadyAcquired) return;
            try
            {
                _settingsRepo.SetInt64("last_backup_at", leasePreviousValue);
                Logger.Info("[BackupService] auto バックアップ失敗/キャンセルのため last_backup_at を巻き戻し (次回起動で再試行): " + leasePreviousValue);
            }
            catch (Exception rbEx)
            {
                Logger.Warn("[BackupService] last_backup_at の巻き戻しに失敗 (次回は通常 interval 待ち): " + rbEx.Message);
            }
        }

        /// <summary>
        /// 設定された世代数を超える古い**自動**バックアップファイルを削除する (#235)。
        ///
        /// auto は `&lt;保存先&gt;/auto/auto_&lt;yyyyMMdd&gt;_&lt;HHmmss&gt;[_host].db` に保存され、固定幅ゼロ埋めの
        /// タイムスタンプが接頭辞の直後に来るため、**ファイル名の降順 = 取得時刻の新しい順** になる
        /// (wall-clock のパース不要 = 時計依存の順序逆転を避けつつ自己完結)。新しい順に keep 件を残し、残りを
        /// 物理削除する。手動 (manual) / 復元前退避 (safety) は別フォルダなので構造的に削除対象外。
        ///
        /// backup_log 廃止 (DB v19) に伴い、旧実装の「DB 行を SoT に retention」「DB 行削除失敗時の failed 格下げ」
        /// は不要になった。並行 Manager が同時 retention で同一ファイルを先に消しても、File.Delete の失敗は
        /// 握って continue する (次回も同じ対象なので冪等)。
        /// </summary>
        private void ApplyRetention()
        {
            int retentionCount = _settingsRepo.GetInt32("backup_retention_count", 30);
            if (retentionCount <= 0) return;

            try
            {
                string autoDir = Path.Combine(GetEffectiveDestinationDirectory(), "auto");
                if (!Directory.Exists(autoDir)) return;

                var targets = new DirectoryInfo(autoDir)
                    .GetFiles("auto_*.db")
                    .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Skip(retentionCount)
                    .ToList();

                foreach (var f in targets)
                {
                    try
                    {
                        f.Delete();
                        Logger.Info($"[BackupService] 古い自動バックアップを削除 (#235 retention): {f.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[BackupService] 自動バックアップ削除失敗 {f.FullName}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[BackupService] リテンション処理失敗", ex);
            }
        }

        /// <summary>
        /// ホスト名をバックアップファイル名へ埋め込めるよう正規化する。ファイル名禁止文字を除去し、
        /// フィールド区切りに使う `_` は `-` に置換する (host 内 `_` を潰すことで、BackupCatalogService の
        /// 「末尾 `_&lt;数値&gt;` = 衝突連番」解釈との曖昧性をゼロにする)。全除去で空になった場合は host なしに
        /// fall back する (ファイル名は `&lt;種類&gt;_&lt;日時&gt;.db` 形式)。
        /// </summary>
        private static string SanitizeHostForFileName(string host)
        {
            if (string.IsNullOrEmpty(host)) return "";
            var sb = new System.Text.StringBuilder(host.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in host)
            {
                if (c == '_') { sb.Append('-'); continue; }
                if (Array.IndexOf(invalid, c) >= 0) continue; // 禁止文字は除去
                sb.Append(c);
            }
            return sb.ToString();
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
