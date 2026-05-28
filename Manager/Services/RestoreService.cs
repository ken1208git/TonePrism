using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;

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

        public RestoreService(DatabaseConnection conn)
        {
            _conn = conn;
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
                    using (var sourceConn = new SQLiteConnection(_conn.ConnectionString))
                    {
                        sourceConn.Open();
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

                // 5. 退避フォルダのリテンション適用（最新を残して古いのから削除）
                progress?.Report(new ProgressInfo(95, "退避ファイルの世代管理...", ""));
                ApplySafetyRetention(safetyDir, DefaultSafetyRetentionCount);

                progress?.Report(new ProgressInfo(100, "復元完了", dbPath));
                Logger.Info($"[RestoreService] 復元完了: source='{backupFilePath}', safety='{safetyPath}'");
                return safetyPath;
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"[RestoreService] 復元キャンセル: source='{backupFilePath}'");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"[RestoreService] 復元失敗: source='{backupFilePath}'", ex);
                throw;
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
