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
        // (#250 PR1) games/ + guide/ のアセットスナップショット。循環依存回避のため後付け注入 (AttachSnapshotService)。
        private AssetSnapshotService _assetSnapshotService;

        public BackupService(DatabaseConnection conn, SettingsRepository settingsRepo)
        {
            _conn = conn;
            _settingsRepo = settingsRepo;
        }

        /// <summary>(#250) DatabaseManager から AssetSnapshotService を後付け注入する (BackupService 生成後に作るため)。</summary>
        public void AttachSnapshotService(AssetSnapshotService snapshotService)
        {
            _assetSnapshotService = snapshotService;
        }

        /// <summary>(#250 レビュー Low) 内側の 0-100% を外側の [lo,hi]% にマップして親 progress に流す薄い adapter。
        /// アセット段 (最も重い) にバーの大半 [lo,hi] を割り当て、ファイル単位で動かすために使う (round9 UI)。</summary>
        private sealed class RangeProgress : IProgress<ProgressInfo>
        {
            private readonly IProgress<ProgressInfo> _inner;
            private readonly int _lo, _span;
            private readonly string _message;
            public RangeProgress(IProgress<ProgressInfo> inner, int lo, int hi, string message)
            { _inner = inner; _lo = lo; _span = Math.Max(0, hi - lo); _message = message; }
            public void Report(ProgressInfo value)
            {
                int inner = value != null ? value.Percentage : 0;
                if (inner < 0) inner = 0;
                if (inner > 100) inner = 100;
                int mapped = _lo + (int)((long)inner * _span / 100);
                _inner.Report(new ProgressInfo(mapped, _message, value != null ? value.Detail : ""));
            }
        }

        /// <summary>
        /// 「自動バックアップが UI 上で有効か」の判定 SoT helper。`backup_auto_enabled` が `"false"` 厳密一致
        /// (case-insensitive) で disabled、それ以外 (空 / "true" / unknown) はすべて enabled 扱い。
        /// (#295) 旧 IsAutoBackupDue / RunAutoBackupIfDue (起動時の時間間隔トリガ) は撤去。操作単位トリガの
        /// <see cref="SessionBackupCoordinator"/> がこの enable を gate に使う。
        /// </summary>
        public bool IsAutoBackupEnabled()
        {
            string enabledStr = _settingsRepo.GetString(SettingsKeys.BackupAutoEnabled, "true");
            return !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);
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
        /// (#295) 操作単位の自動バックアップ実行。<see cref="SessionBackupCoordinator"/> から、データ変更操作の
        /// 成功直後に呼ばれる。起動時の時間間隔トリガと lease は廃止 (操作単位なので interval / 多ホスト直列化は
        /// 不要、同時編集は SessionConflictHelper が警告)。<paramref name="includeAssets"/>=false の DB-only 操作では
        /// 重い games/guide 走査を skip する。enable gate は coordinator 側 (<see cref="IsAutoBackupEnabled"/>)。
        /// </summary>
        public BackupResult RunSessionBackup(bool includeAssets, IProgress<ProgressInfo> progress, CancellationToken token,
            string replacedDbPath = null, string replacedManifestPath = null)
        {
            // (round 5 H2) 他 PC が復元中 (File.Replace 中の DB を Online Backup で読むと partial/corrupt) なら延期。
            if (_settingsRepo != null)
            {
                string lockOwner = _settingsRepo.GetActiveRestoreLockOwnerOrNull(
                    Environment.MachineName,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    SettingsKeys.RestoreLockStaleThresholdMs);
                if (!string.IsNullOrEmpty(lockOwner))
                {
                    Logger.Info("[BackupService] (#295) 他 PC (" + lockOwner + ") が復元中のため自動バックアップを延期");
                    return BackupResult.Deferred("他 PC (" + lockOwner + ") が復元中のため今回の変更はまだバックアップされていません（復元完了後にもう一度操作すると控えられます）");
                }
            }
            // (round6 High) replace-in-session でこの直後に coordinator が消す前世代の .db / .manifest を retention の母数
            // から除外させる (= 直近 N セッション保持が複数操作で崩れない)。manual / 起動時経路は null (= 除外なし)。
            return RunBackupCore(TriggerAuto, progress, token, includeAssets, replacedDbPath, replacedManifestPath);
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
            return RunBackupCore(TriggerManual, progress, token);
        }

        private BackupResult RunBackupCore(string triggerType, IProgress<ProgressInfo> progress, CancellationToken token,
            bool includeAssets = true, string replacedDbPath = null, string replacedManifestPath = null)
        {
            string host = SanitizeHostForFileName(Environment.MachineName);

            // auto / manual を種類ごとのサブフォルダに分け、ファイル名も `<種類>_<日時>_<host>.db` に統一する
            // (退避 safety_*.db と同じ流儀)。フォルダ位置で種類が、ファイル名 host で実行 PC が確定するため、
            // backup_log を持たずとも BackupCatalogService が履歴を完全に復元でき、auto の世代管理 (retention)
            // も正しく効く。triggerType は RunSessionBackup / RunManualBackup から "auto" / "manual" のみが
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
                                // (round9 UI) DB コピー (124KB級で一瞬) はバーの 0-10% に圧縮し、残り 10-99% を
                                // 重いアセット取得 (初回 ~6GB) に割り当てる。旧実装は DB に 0-100% を与え、その後
                                // retention で 95% に逆戻り→アセットを 95-99% に圧縮しており、一番長いアセット段が
                                // バーの 4% しか動かず「95% で固まって遅い」ように見えていた。
                                progress?.Report(new ProgressInfo(
                                    percent / 10,
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
                // catch で failed 記録 + ファイル削除に流す。
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

                // (#295) last_backup_at はトリガ gate ではなくなったが、履歴 / 「最終バックアップ」表示用に更新する。
                _settingsRepo.SetInt64("last_backup_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                // リテンションは成功時のみ適用 (round6 High: replace 対象の前世代を母数から除外)
                progress?.Report(new ProgressInfo(10, "古いバックアップを整理中...", ""));
                ApplyRetention(replacedDbPath);

                // (#250 PR1) DB バックアップ成功確定後に games/ + guide/ を同一 timestamp/triggerType で best-effort 取得。
                // **失敗・キャンセルしても DB バックアップの成否・last_backup_at は一切壊さない** (last_backup_at 更新は
                // この呼び出しより前の SetInt64("last_backup_at", ...) で完了済、AssetSnapshotService は throw しない契約)。
                // 世代名 timestamp は .db ファイル名と対応する。
                // (#295) includeAssets=false の DB-only 操作 (ストア/スライド文字編集等) では重い games/guide 走査を
                // skip し、DB だけ控える。games/guide を変える操作のみ includeAssets=true でアセットも取得する。
                Models.SnapshotResult assetSnap = null;
                if (includeAssets && _assetSnapshotService != null)
                {
                    // (round9 UI) アセット取得が最も重い (初回 ~6GB の SMB 読込) ので、バーの大半 (10-99%) を
                    // 割り当ててファイル単位で動かす (lblDetail にファイル名が流れる)。DB コピー (0-10%) + retention (10%) は
                    // 一瞬。旧実装は 95-99% に圧縮しており「95% で固まって遅い」ように見える主因だった。内側 0-100 を 10-99 にマップ。
                    var assetProgress = progress != null ? new RangeProgress(progress, 10, 99, "ゲーム本体をバックアップ中...") : null;
                    assetSnap = _assetSnapshotService.CreateSnapshot(timestamp, triggerType, assetProgress, token, replacedManifestPath);
                    if (assetSnap.IsFailed)
                        Logger.Warn("[BackupService] アセット控え取得失敗 (DB バックアップは成功): " + assetSnap.Message);
                }

                progress?.Report(new ProgressInfo(100, "バックアップ完了", destinationPath));
                // (レビュー M2) アセット控えの結果を UI へ持ち回る (失敗/異常を成功ダイアログに併記するため)。
                var result = BackupResult.Success(destinationPath, fileSize);
                result.AssetSnapshot = assetSnap;
                return result;
            }
            catch (OperationCanceledException)
            {
                // 中途半端なファイルを削除
                TryDeleteIfExists(destinationPath);
                throw;
            }
            catch (Exception ex)
            {
                // (失敗履歴は backup_log 廃止に伴い Logger のみに残す)
                Logger.Error("[BackupService] バックアップに失敗しました: " + destinationPath, ex);
                TryDeleteIfExists(destinationPath);
                return BackupResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// (Finding #5) 作成したバックアップ DB を `PRAGMA quick_check` で検証する。"ok" 以外なら例外を投げ、
        /// caller の catch で failed 記録 + ファイル削除に流す (= 壊れた / 不完全なバックアップを
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
        private void ApplyRetention(string excludePath = null)
        {
            int retentionCount = _settingsRepo.GetInt32("backup_retention_count", 30);
            if (retentionCount <= 0) return;

            try
            {
                string autoDir = Path.Combine(GetEffectiveDestinationDirectory(), "auto");
                if (!Directory.Exists(autoDir)) return;

                var candidates = new DirectoryInfo(autoDir).GetFiles("auto_*.db").AsEnumerable();
                // (round6 High) replace-in-session で coordinator がこの直後に消す前世代 (excludePath) を retention の母数から
                // 除外する。含めると「これから消す 1 件」を数えて本来残すべき過去セッションを 1 件余計に削り、「直近 N
                // セッション保持」が 1 セッション内の操作回数ぶん崩れる (K 操作で過去 K-1 世代が消失)。除外した世代は
                // coordinator が確実に消すので二重カウントにはならない。
                // (round8 #2) 比較は **ファイル名** で行う (アセット側 ApplyRetentionAndGc と対称)。full-path 完全一致だと、
                // backup_destination_path が非正規化 (末尾区切り / "." / ".." 混入) の場合に DirectoryInfo.FullName (正規化済)
                // と excludePath (GetEffectiveDestinationDirectory は raw configured を返す) が一致せず除外が外れ、round6 の
                // 過剰削除バグが silent 再発しうる。.db 名は auto dir 内で衝突 suffix により一意なのでファイル名比較で過不足なし。
                string excludeName = string.IsNullOrEmpty(excludePath) ? null : Path.GetFileName(excludePath);
                if (excludeName != null)
                    candidates = candidates.Where(f => !string.Equals(f.Name, excludeName, StringComparison.OrdinalIgnoreCase));

                var targets = candidates
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
        // (PR #236 レビュー対応 #4) RestoreService の safety 命名でも流用するため internal 化。
        internal static string SanitizeHostForFileName(string host)
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
        /// <summary>(#250) この DB バックアップに同梱したアセット控えの結果 (best-effort)。null = 機能未注入。
        /// UI が成功/失敗/異常を併記するため持ち回る。(レビュー L1) 他プロパティと同様に外部からは不変にし、
        /// 代入は同一アセンブリの RunBackupCore のみ (internal set)。</summary>
        public Models.SnapshotResult AssetSnapshot { get; internal set; }

        /// <summary>(round6 Medium #3) restore-lock 等で「試行せず延期した」Skipped か。通常の Skipped (キャンセル /
        /// 無効) は false。true のものはユーザーに「変更はまだ控えられていない」旨を知らせる (完全 silent にしない)。</summary>
        public bool IsDeferred { get; private set; }

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
        /// <summary>(round6 Medium #3) restore-lock 等で延期した Skipped。`IsDeferred=true` でユーザーに通知させる。</summary>
        public static BackupResult Deferred(string reason)
        {
            return new BackupResult { Kind = ResultKind.SkippedKind, Message = reason, IsDeferred = true };
        }
        public static BackupResult Failed(string errorMessage)
        {
            return new BackupResult { Kind = ResultKind.FailedKind, Message = errorMessage };
        }
    }
}
