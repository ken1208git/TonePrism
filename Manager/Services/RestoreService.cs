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
        private readonly SettingsRepository _settingsRepo;

        public RestoreService(DatabaseConnection conn, SettingsRepository settingsRepo)
        {
            _conn = conn;
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
        /// <summary>
        /// (#299 review round3 #3) 復元元 .db を `PRAGMA quick_check` で検証する。`Openable`=開けたか (= 切り詰め / 非 DB /
        /// ヘッダ破損なら false)、`QuickCheckResult`="ok" or エラー文字列 (Openable=false のとき null)。UI の事前確認と
        /// RestoreService の置換前 backstop の双方から呼ぶ (整合性ロジックを 1 箇所に集約)。read のみ・throw しない。
        /// </summary>
        public static (bool Openable, string QuickCheckResult) CheckIntegrity(string dbFilePath)
        {
            try
            {
                using (var conn = new SQLiteConnection($"Data Source={dbFilePath};Version=3;"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "PRAGMA quick_check;";
                        return (true, cmd.ExecuteScalar()?.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[RestoreService] 整合性チェックのため開けませんでした (壊れている可能性): " + dbFilePath + " : " + ex.Message);
                return (false, null);
            }
        }

        /// <param name="allowIntegrityWarnings">(round3 #3) true なら「open はできるが quick_check 非 ok」= 不健全な
        /// バックアップでも復元を続行する (UI が事前にユーザー確認を取った場合のみ true。open すらできない破損は force でも中止)。
        /// 既定 false = 非 ok は一律中止 (直接呼び出し / コピー破損 backstop 用の安全側)。</param>
        public string Restore(string backupFilePath, IProgress<ProgressInfo> progress, CancellationToken token, bool allowIntegrityWarnings = false)
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

            // 復元の監査は backup_log 廃止 (DB v19) に伴い専用行を持たない。復元のたびに作られる
            // safety_*.db (復元直前 DB スナップショット) が「いつ復元したか」の証跡を兼ねる。throw 経路には
            // Logger.Error を必ず挟んで例外詳細を残す (caller の ProcessingDialog catch は例外型情報を握って破棄するため)。
            string pcName = Environment.MachineName;
            long startedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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

            // (累積監査) File.Replace fallback で現 DB を削除した後に File.Move が失敗すると、現 DB も
            // tempPath (= 唯一の復元データ) も失われる致命的 path があった。その状態を外側 catch に伝え、
            // tempPath を削除させないための flag。
            bool tempPathIsLastResort = false;

            try
            {
                progress?.Report(new ProgressInfo(0, "復元の準備...", ""));
                token.ThrowIfCancellationRequested();

                // (累積監査 round 6 M12) 復元は (1) safety 退避 + (2) backup を tempPath にコピー + (3) File.Replace で
                // ディスクを最大 2 ファイル分消費する。展示 PC は小容量 SSD が多く、途中で容量が尽きると safety だけ
                // 書けて tempPath コピーが IOException で落ちる (DB は無傷だが中途半端な残骸 + 不親切なエラー)。
                // 事前に「復元元サイズ × 2 + 余裕」の空きを確認し、不足なら明確なメッセージで先に止める。
                try
                {
                    long needed = new FileInfo(backupFilePath).Length * 2L + 16L * 1024 * 1024; // ×2 (safety+temp) + 16MB 余裕
                    string root = Path.GetPathRoot(Path.GetFullPath(dbPath));
                    if (!string.IsNullOrEmpty(root))
                    {
                        var drive = new DriveInfo(root);
                        if (drive.IsReady && drive.AvailableFreeSpace < needed)
                        {
                            string msg = "ディスクの空き容量が不足しているため復元を中止しました。\n" +
                                "  必要: 約 " + (needed / (1024 * 1024)) + " MB / 空き: 約 " + (drive.AvailableFreeSpace / (1024 * 1024)) + " MB\n" +
                                "  不要なファイルを削除して空き容量を確保してから再試行してください。";
                            Logger.Error("[RestoreService] " + msg);
                            throw new IOException(msg);
                        }
                    }
                }
                catch (IOException) { throw; }
                catch (Exception spaceEx)
                {
                    // 空き容量チェック自体の失敗 (ネットワークドライブ等で DriveInfo 不能) は復元を止めない。
                    Logger.Warn("[RestoreService] 空き容量チェックに失敗 (チェックを skip して続行): " + spaceEx.Message);
                }

                // 退避フォルダを必ず作成
                if (!Directory.Exists(safetyDir))
                {
                    Directory.CreateDirectory(safetyDir);
                }

                // 1. 現DBを安全バックアップ（Online Backup API でライブコピー）
                // (追加精査 ⑥) BackupService と同様、yyyyMMdd_HHmmss の同 1 秒衝突を suffix で回避する。
                // 復元連打は UI 上 ProcessingDialog で block されるため発生確率は低いが、防御ラインとして
                // BackupService と同じ pattern に揃える。
                // (PR #236 レビュー対応 #4) safety にも実行 PC 名を埋め込む (auto/manual と同じ規約)。host 無しだと
                // 共有 backups/safety/ で 2 台が同一秒に復元した際、両者が同名 safety_<日時>.db を作って一方が他方を
                // Online Backup API で上書きしうる (衝突 suffix _N は自プロセス内のみで cross-PC を分離できない)。
                // host を入れると別 PC は baseName 自体が分離して衝突しない。BackupCatalogService の SafetyRegex /
                // ExtractHost は既に host セグメントを解釈できるため履歴表示も追従する。
                string safetyTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safetyHost = BackupService.SanitizeHostForFileName(pcName);
                string safetyBaseName = string.IsNullOrEmpty(safetyHost)
                    ? $"safety_{safetyTimestamp}"
                    : $"safety_{safetyTimestamp}_{safetyHost}";
                string safetyPath = Path.Combine(safetyDir, safetyBaseName + ".db");
                int safetyCollisionSuffix = 2;
                while (File.Exists(safetyPath))
                {
                    safetyPath = Path.Combine(safetyDir, $"{safetyBaseName}_{safetyCollisionSuffix}.db");
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

                // (#299 review #3 / round3) live を置換する前に復元元 (tempPath = local コピー) の整合性を quick_check で検証する。
                // 非ブロッキング化でバックアップ worker が DB コピーフェーズ中にプロセス強制終了 (「中止して閉じる」直後の
                // abrupt exit 等) すると、検証前の不完全な .db が backup_dest に残り、BackupCatalogService は list 時の
                // quick_check を省く (= 成功世代と区別不能) ため復元候補に出うる。ここで弾けば、その不完全 .db も SMB 越しの
                // コピー破損も含めて live DB を保護できる (この時点では File.Replace 前 = live 無傷、safety も退避済)。
                // (round3 #3) 復元は「最後の手段」なので段階を分ける: **open すらできない**破損 (切り詰め / 非 DB) は force でも
                // 中止 (置換しても使えない)。**open はできるが quick_check 非 ok** な「不健全」バックアップは、UI が事前に
                // ユーザー確認を取った場合 (allowIntegrityWarnings=true) のみ続行する (唯一のバックアップが少し不健全な災害時に
                // override を残す)。既定 false では非 ok 一律中止 (コピー破損 backstop / 直接呼び出しの安全側)。
                var integrity = CheckIntegrity(tempPath);
                if (!integrity.Openable)
                {
                    string msg = "復元元のバックアップが開けません (壊れている可能性)。別の世代を選んで再試行してください。現在のデータベースは変更していません。";
                    Logger.Error("[RestoreService] " + msg);
                    throw new InvalidOperationException(msg);
                }
                if (!string.Equals(integrity.QuickCheckResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowIntegrityWarnings)
                    {
                        string msg = "復元元のバックアップが整合性チェックに失敗しました (" + (integrity.QuickCheckResult ?? "実行不可")
                            + ")。別の世代を選んで再試行してください。現在のデータベースは変更していません。";
                        Logger.Error("[RestoreService] " + msg);
                        throw new InvalidOperationException(msg);
                    }
                    Logger.Warn("[RestoreService] 整合性チェックに問題のあるバックアップをユーザー確認のうえ復元します: " + integrity.QuickCheckResult);
                }

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
                        // (#299 review round4 H-1) 非ブロッキング化で、復元前に協調キャンセルしたバックアップ worker が live DB
                        // ハンドル (Online Backup の read) を解放しきる前に置換へ到達する狭い窓がある (worker の DB コピーはサブ秒で、
                        // 復元の safety 退避 + temp コピー + ClearAllPools の間に解放される公算が高いが同期保証は無い)。共有違反
                        // (IOException) は短くリトライして解放を待ってから fallback に落とす。
                        ReplaceWithSharingRetry(tempPath, dbPath);
                    }
                    catch (IOException replaceEx)
                    {
                        Logger.Warn("[RestoreService] File.Replace 失敗 (SMB/Junction 等の可能性)、Delete + Move 経路に fallback: " + replaceEx.Message);
                        FallbackDeleteAndMove(tempPath, dbPath, ref tempPathIsLastResort);
                    }
                    catch (UnauthorizedAccessException replaceEx)
                    {
                        Logger.Warn("[RestoreService] File.Replace 権限エラー、Delete + Move 経路に fallback: " + replaceEx.Message);
                        FallbackDeleteAndMove(tempPath, dbPath, ref tempPathIsLastResort);
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
                    // (round 5 M2) snapshot 取得時に他 PC が active な restore lock を保有していた場合、
                    // 復元すると NEW DB に他 PC 由来の lock 行が蘇る。自 PC の `ReleaseRestoreLock` は
                    // owner mismatch で no-op → 5 分 stale 失効まで全 write が block される UX 退行があった。
                    // 復元直後に所有者に関わらず lock 行を強制クリアする (自 PC lock は finally でも release されるが
                    // 二重 DELETE は no-op で害なし)。post-step なので失敗しても復元自体は成功扱いを継続。
                    try
                    {
                        _settingsRepo?.ForceClearRestoreLock();
                        Logger.Info("[RestoreService] (round 5 M2) snapshot 由来の restore_lock_owner 行を強制クリア");
                    }
                    catch (Exception clearEx)
                    {
                        Logger.Warn("[RestoreService] (round 5 M2) restore_lock_owner 強制クリア失敗 (5 分 stale で自動失効): " + clearEx.Message);
                    }

                    // 5. 退避フォルダのリテンション適用（最新を残して古いのから削除）
                    // (累積監査 round 6 High-10) 今回作成した safetyPath を除外対象として渡し、CreationTime の
                    // タイ順序揺れで「今まさにレポートで案内する safety」が間引かれる事故を防ぐ。
                    progress?.Report(new ProgressInfo(95, "退避ファイルの世代管理...", ""));
                    ApplySafetyRetention(safetyDir, DefaultSafetyRetentionCount, safetyPath);

                    progress?.Report(new ProgressInfo(100, "復元完了", dbPath));
                }
                catch (Exception postEx)
                {
                    Logger.Warn($"[RestoreService] (High-6) DB 置換後の post-step 例外を swallow (復元自体は成功): {postEx.Message}");
                }

                Logger.Info($"[RestoreService] 復元完了: source='{backupFilePath}', safety='{safetyPath}'");

                // 復元成功。safetyPath (復元直前に退避した DB スナップショット) を返す。
                // これ自体が復元の証跡を兼ねるため、別途の audit 行 INSERT は行わない (backup_log 廃止)。
                return safetyPath;
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[RestoreService] 復元キャンセル: source='{backupFilePath}'");
                // (累積監査 round 3) 一時ファイルが残置されると次回復元時 L154 の File.Delete で消えるが、
                // 復元失敗が連続する展示 PC (SSD 小容量) では disk 逼迫 → File.Copy 失敗の連鎖を誘発する。
                // catch 経路で必ず後始末する (両 catch で対称化、failure 自体は swallow して例外伝播を優先)。
                TryDeleteTempFile(tempPath);
                throw;
            }
            catch (Exception ex)
            {
                // (High-6) post-step 例外は内側 try で swallow 済のため、ここに到達する例外は通常 pre-replace 失敗
                // (= DB 無傷 + tempPath 残存) なので、tempPath 削除 + throw が正しい。
                Logger.Error($"[RestoreService] 復元失敗: source='{backupFilePath}'", ex);
                // (累積監査) ただし fallback の Move 失敗で現 DB を削除済の場合 (tempPathIsLastResort=true) は、
                // tempPath が唯一の復元データなので絶対に消さない。手動復旧 (.restore-tmp を toneprism.db に
                // リネーム) または退避フォルダの safety バックアップからの復元を案内する。
                if (tempPathIsLastResort)
                {
                    Logger.Error("[RestoreService] 現 DB の置換に失敗し DB ファイルが不在の可能性があります。復元データを残します: '"
                        + tempPath + "' → これを '" + dbPath + "' にリネームするか、退避フォルダの safety バックアップから復元してください。");
                    // (レビュー対応 #1) この経路は「現 DB を削除済み + 置換 Move 失敗」= toneprism.db が不在に
                    // なりうる最悪ケース。caller の汎用「復元失敗（詳細はログ）」メッセージに埋もれさせず、専用例外で
                    // 具体的な復旧手順を画面に出させる (例外 Message 自体を操作可能な案内文にする)。
                    throw new RestoreDbMissingException(
                        "復元中にデータベースの置き換えに失敗し、toneprism.db が一時的に失われた可能性があります。\n\n" +
                        "【復旧方法】次のいずれかを行ってください:\n" +
                        "  ① 「" + tempPath + "」を「" + dbPath + "」にリネームする\n" +
                        "  ② バックアップ画面の「復元」で退避ファイル (safety_*.db) から復元する\n\n" +
                        "復元データ (.restore-tmp) は安全のため残してあります。詳細はログを参照してください。",
                        ex);
                }
                TryDeleteTempFile(tempPath);
                throw;
            }
            finally
            {
                // (H5) advisory restore-lock を解放。lock 取得済の場合のみ delete (= 他 PC 保有 lock は触らない)。
                // ReleaseRestoreLock は SELECT → owner 完全一致判定 → exact DELETE で「自 PC 保有行のみ削除」を保証 (旧 LIKE 句の wildcard 巻き込みは撤去済、SettingsRepository 参照)。
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

        /// <summary>
        /// safety_*.db を新しい順に count 個まで残し、それより古いものを削除
        /// </summary>
        private static void ApplySafetyRetention(string safetyDir, int count, string currentSafetyPath = null)
        {
            try
            {
                var dir = new DirectoryInfo(safetyDir);
                if (!dir.Exists) return;

                // (累積監査 round 6 High-10) 今回作成した safety は復元レポートで「元に戻すならここ」と案内する
                // 対象。NTFS の CreationTime 解像度 (約 2 秒) で同一秒に複数 safety が並ぶと OrderByDescending の
                // タイ順序が不定になり、本来最新の今回 safety が Skip 境界に落ちて削除されうる。削除候補から
                // 明示的に除外して、案内した path がレポート表示時点で消えている事故を防ぐ。
                string normalizedCurrent = null;
                if (!string.IsNullOrEmpty(currentSafetyPath))
                {
                    try { normalizedCurrent = Path.GetFullPath(currentSafetyPath); }
                    catch { normalizedCurrent = currentSafetyPath; }
                }

                var oldFiles = dir.GetFiles("safety_*.db")
                    .Where(f =>
                    {
                        if (normalizedCurrent == null) return true;
                        string fp;
                        try { fp = Path.GetFullPath(f.FullName); } catch { fp = f.FullName; }
                        return !string.Equals(fp, normalizedCurrent, StringComparison.OrdinalIgnoreCase);
                    })
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

        /// <summary>
        /// (累積監査) File.Replace 失敗時の fallback。現 DB を削除してから tempPath を本来の位置へ Move する。
        /// File.Delete(dbPath) 成功後に File.Move が失敗すると、現 DB も tempPath も失われ DB ファイルが
        /// 不在になる致命的 path があった (= 旧実装は Move 失敗例外が外側 catch に伝播し、そこで唯一残っていた
        /// tempPath まで削除していた)。Delete 後の Move 失敗時は tempPathIsLastResort を true にして呼び出し側に
        /// 伝え、tempPath を保全 (= .restore-tmp → toneprism.db のリネームで手動復旧可能) させる。
        /// </summary>
        private static void FallbackDeleteAndMove(string tempPath, string dbPath, ref bool tempPathIsLastResort)
        {
            File.Delete(dbPath);
            try
            {
                File.Move(tempPath, dbPath);
            }
            catch
            {
                // dbPath は既に削除済 = point of no return を越えた。tempPath が唯一の復元データなので
                // 外側 catch で削除させない。
                tempPathIsLastResort = true;
                throw;
            }
        }

        /// <summary>(#299 review round4 H-1) `File.Replace` を共有違反 (IOException) で数回リトライしてから例外を伝播する。
        /// 非ブロッキング化で復元前に協調キャンセルしたバックアップ worker が live DB ハンドルを解放しきる前に置換へ到達する
        /// 狭い窓を、短い待機 (最大 ~800ms) で吸収する。最終試行の例外は caller の fallback (Delete+Move) へ伝播させる
        /// (= 永続的な IOException = 真の SMB/Junction 等は従来どおり fallback に流す。リトライは transient 共有違反のためだけ)。</summary>
        private static void ReplaceWithSharingRetry(string tempPath, string dbPath)
        {
            for (int i = 0; i < 4; i++)
            {
                try { File.Replace(tempPath, dbPath, null); return; }
                catch (IOException) { Thread.Sleep(200); } // 共有違反 = worker がまだ DB を解放中の可能性。待って再試行。
            }
            File.Replace(tempPath, dbPath, null); // 最終試行: 失敗したら例外を caller の fallback へ
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

    /// <summary>
    /// (レビュー対応 #1) 復元中に現 DB を削除済みで置換 Move も失敗し、toneprism.db が不在になりうる
    /// 最悪ケースを表す専用例外。caller (BackupSectionPanel) はこれを汎用エラーと区別し、Message に
    /// 入れた具体的な復旧手順をユーザーに提示する。
    /// </summary>
    public class RestoreDbMissingException : Exception
    {
        public RestoreDbMissingException(string message, Exception inner) : base(message, inner) { }
    }
}
