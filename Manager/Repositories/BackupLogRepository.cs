using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text.RegularExpressions;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Repositories
{
    /// <summary>
    /// backup_log テーブルへのアクセサ
    /// </summary>
    public class BackupLogRepository
    {
        private readonly DatabaseConnection _conn;

        public BackupLogRepository(DatabaseConnection conn)
        {
            _conn = conn;
        }

        /// <summary>
        /// File.Exists 判定用にパスを解決する。relative_path があれば現在の toneprism.db ディレクトリと結合、
        /// 無ければ file_path をそのまま返す。プロジェクト移動後でも relative_path 経由で実体を発見できる。
        /// </summary>
        private string ResolvePathForExistsCheck(string filePath, string relativePath)
        {
            if (!string.IsNullOrEmpty(relativePath))
            {
                try
                {
                    string dbDir = System.IO.Path.GetDirectoryName(_conn.DbPath);
                    if (!string.IsNullOrEmpty(dbDir))
                        return System.IO.Path.GetFullPath(System.IO.Path.Combine(dbDir, relativePath));
                }
                catch { /* fallthrough to file_path */ }
            }
            return filePath;
        }

        /// <summary>
        /// 進行中レコードを挿入し、生成された id を返す。
        /// 予定ファイルパスを最初から保存することで、後で「実ファイルが存在するか」で
        /// リコンサイル（成功/失敗の確定）が可能になる。
        /// </summary>
        public long InsertInProgress(string pcName, string triggerType, long startedAtUnixSec, string plannedFilePath, string plannedRelativePath = null)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "INSERT INTO backup_log (started_at, pc_name, status, trigger_type, file_path, relative_path) " +
                        "VALUES (@started_at, @pc_name, 'in_progress', @trigger_type, @file_path, @relative_path)", connection))
                    {
                        cmd.Parameters.AddWithValue("@started_at", startedAtUnixSec);
                        cmd.Parameters.AddWithValue("@pc_name", pcName ?? "");
                        cmd.Parameters.AddWithValue("@trigger_type", triggerType);
                        cmd.Parameters.AddWithValue("@file_path", plannedFilePath ?? "");
                        cmd.Parameters.AddWithValue("@relative_path",
                            string.IsNullOrEmpty(plannedRelativePath) ? (object)DBNull.Value : plannedRelativePath);
                        cmd.ExecuteNonQuery();
                    }
                    return connection.LastInsertRowId;
                }
            });
        }

        public void MarkSuccess(long id, string filePath, long fileSizeBytes, long completedAtUnixSec, string relativePath = null)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "UPDATE backup_log SET status = 'success', file_path = @file_path, relative_path = @relative_path, " +
                        "file_size_bytes = @file_size, completed_at = @completed_at WHERE id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@file_path", filePath ?? "");
                        cmd.Parameters.AddWithValue("@relative_path",
                            string.IsNullOrEmpty(relativePath) ? (object)DBNull.Value : relativePath);
                        cmd.Parameters.AddWithValue("@file_size", fileSizeBytes);
                        cmd.Parameters.AddWithValue("@completed_at", completedAtUnixSec);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// (H4) 復元完了の audit 行を直接 INSERT する。InsertInProgress + MarkSuccess の組合せが復元では
        /// 機能しない (= 開始時の行は OLD DB、UPDATE 時は NEW DB で row id が一致しない) ため、NEW DB に
        /// 確定済の `success` (または `failed`) 行を 1 発で記録する用途。trigger_type='restore' は v16 で
        /// CHECK 拡張済のため、本 method は呼び出し側で InitializeDatabase (= migration 完了) 後に呼ぶ。
        /// </summary>
        public void LogRestoreCompleted(string pcName, long startedAtUnixSec, long completedAtUnixSec,
            string sourceBackupPath, long fileSizeBytes, string status, string errorMessage,
            string relativePath = null)
        {
            // (累積監査 round 4 Medium-19) relativePath 引数を追加。通常のバックアップ INSERT (InsertInProgress) は
            // 相対 path も記録するが、復元 audit は絶対 path のみだったため、プロジェクトを別ドライブに移動すると
            // 履歴行のリンクが切れる非対称があった。caller (BackupSectionPanel) は復元元バックアップの dbDir 基準
            // 相対 path を `BackupPathResolver.ToRelativeFromDbDir` で計算して渡す。null なら従来通り絶対のみ記録。
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "INSERT INTO backup_log (started_at, completed_at, pc_name, file_path, relative_path, " +
                        "file_size_bytes, status, error_message, trigger_type) " +
                        "VALUES (@started, @completed, @pc, @path, @relpath, @size, @status, @err, 'restore')",
                        connection))
                    {
                        cmd.Parameters.AddWithValue("@started", startedAtUnixSec);
                        cmd.Parameters.AddWithValue("@completed", completedAtUnixSec);
                        cmd.Parameters.AddWithValue("@pc", pcName ?? "");
                        cmd.Parameters.AddWithValue("@path", sourceBackupPath ?? "");
                        cmd.Parameters.AddWithValue("@relpath",
                            string.IsNullOrEmpty(relativePath) ? (object)DBNull.Value : relativePath);
                        cmd.Parameters.AddWithValue("@size", fileSizeBytes);
                        cmd.Parameters.AddWithValue("@status", status ?? "success");
                        cmd.Parameters.AddWithValue("@err",
                            string.IsNullOrEmpty(errorMessage) ? (object)DBNull.Value : errorMessage);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        public void MarkFailed(long id, string errorMessage, long completedAtUnixSec)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "UPDATE backup_log SET status = 'failed', error_message = @err, " +
                        "completed_at = @completed_at WHERE id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@err", errorMessage ?? "");
                        cmd.Parameters.AddWithValue("@completed_at", completedAtUnixSec);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// `backup_log` の各行を「実ファイルの有無」でリコンサイルする。
        ///
        /// `in_progress` 行：
        ///   - `file_path` が指すファイルが実在 → `success` に更新し、`file_size_bytes` を実ファイルサイズに
        ///   - 実在しない（または file_path が空） → `failed` に更新し、reasonIfMissing を記録
        ///
        /// `recoverFailedWithExistingFile = true` の場合、`failed` 行も対象：
        ///   - `file_path` が指すファイルが実在 → `success` に復活させる（auto-marked ゴースト由来の救済）
        ///   - ファイルが本当に無い → そのまま放置（実際のバックアップ失敗を保持）
        /// </summary>
        /// <param name="reasonIfMissing">in_progress でファイルが無かった場合の error_message</param>
        /// <param name="thresholdSeconds">この秒数より新しい行は対象外（null = 全件対象）</param>
        /// <param name="recoverFailedWithExistingFile">true なら failed 行も実ファイル存在で success に戻す</param>
        /// <returns>(成功化した数, 失敗化した数)</returns>
        public (int reconciledAsSuccess, int reconciledAsFailed) ReconcileInProgressEntries(
            string reasonIfMissing,
            long? thresholdSeconds = null,
            bool recoverFailedWithExistingFile = false)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int success = 0;
                int failed = 0;

                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    // 対象行を取得 (#126: relative_path も取得して両方で存在チェックする)
                    var inProgressTargets = new List<(long id, string filePath, string relativePath)>();
                    var failedRecoverableTargets = new List<(long id, string filePath, string relativePath)>();

                    string selectInProgressSql = "SELECT id, file_path, relative_path FROM backup_log WHERE status = 'in_progress'";
                    if (thresholdSeconds.HasValue)
                    {
                        selectInProgressSql += " AND started_at < @cutoff";
                    }
                    using (var cmd = new SQLiteCommand(selectInProgressSql, connection))
                    {
                        if (thresholdSeconds.HasValue)
                        {
                            cmd.Parameters.AddWithValue("@cutoff", now - thresholdSeconds.Value);
                        }
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                long id = Convert.ToInt64(reader["id"]);
                                string fp = reader["file_path"] is DBNull ? "" : reader["file_path"].ToString();
                                string rp = reader["relative_path"] is DBNull ? null : reader["relative_path"].ToString();
                                inProgressTargets.Add((id, fp, rp));
                            }
                        }
                    }

                    if (recoverFailedWithExistingFile)
                    {
                        // failed 行のうちファイルが実在するもの（auto-marked ゴースト救済）
                        // (#126: file_path だけでなく relative_path もチェック対象)
                        using (var cmd = new SQLiteCommand(
                            "SELECT id, file_path, relative_path FROM backup_log " +
                            "WHERE status = 'failed' AND " +
                            "((file_path IS NOT NULL AND file_path != '') OR (relative_path IS NOT NULL AND relative_path != ''))",
                            connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                long id = Convert.ToInt64(reader["id"]);
                                string fp = reader["file_path"] is DBNull ? "" : reader["file_path"].ToString();
                                string rp = reader["relative_path"] is DBNull ? null : reader["relative_path"].ToString();
                                failedRecoverableTargets.Add((id, fp, rp));
                            }
                        }
                    }

                    // in_progress 行のリコンサイル
                    foreach (var (id, filePath, relativePath) in inProgressTargets)
                    {
                        string resolvedPath = ResolvePathForExistsCheck(filePath, relativePath);
                        bool fileExists = !string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(resolvedPath);
                        if (fileExists)
                        {
                            long size = new System.IO.FileInfo(resolvedPath).Length;
                            using (var cmd = new SQLiteCommand(
                                "UPDATE backup_log SET status = 'success', file_size_bytes = @size, " +
                                "completed_at = @now WHERE id = @id", connection))
                            {
                                cmd.Parameters.AddWithValue("@size", size);
                                cmd.Parameters.AddWithValue("@now", now);
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.ExecuteNonQuery();
                            }
                            success++;
                        }
                        else
                        {
                            using (var cmd = new SQLiteCommand(
                                "UPDATE backup_log SET status = 'failed', error_message = @reason, " +
                                "completed_at = @now WHERE id = @id", connection))
                            {
                                cmd.Parameters.AddWithValue("@reason", reasonIfMissing ?? "");
                                cmd.Parameters.AddWithValue("@now", now);
                                cmd.Parameters.AddWithValue("@id", id);
                                cmd.ExecuteNonQuery();
                            }
                            failed++;
                        }
                    }

                    // failed 行の救済リコンサイル（ファイル実在のもののみ）
                    foreach (var (id, filePath, relativePath) in failedRecoverableTargets)
                    {
                        string resolvedPath = ResolvePathForExistsCheck(filePath, relativePath);
                        if (string.IsNullOrEmpty(resolvedPath) || !System.IO.File.Exists(resolvedPath))
                            continue; // ファイルが無いなら本当の失敗、そのまま
                        long size = new System.IO.FileInfo(resolvedPath).Length;
                        using (var cmd = new SQLiteCommand(
                            "UPDATE backup_log SET status = 'success', file_size_bytes = @size, " +
                            "error_message = NULL WHERE id = @id", connection))
                        {
                            cmd.Parameters.AddWithValue("@size", size);
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.ExecuteNonQuery();
                        }
                        success++;
                    }
                }
                return (success, failed);
            });
        }

        public BackupLogEntry GetLastSuccess()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    // (M6) File.Exists で実体存在を check しながら新しい順に走査。retention で物理削除済の DB 行が
                    // 残置されたケースで「最終バックアップ: ファイル無し」表示を防ぐ。最大 100 行までで打ち切り
                    // (= 通常は新しい順 1 件目で hit、disk 全滅の異常 case でも応答時間を bound する)。
                    // (累積監査 round 6 M7) 並び順は `id DESC` (= AUTOINCREMENT INSERT 順) を SoT にする。
                    // 旧実装の `started_at DESC` は各 PC の wall clock 依存で、時計が大きくズレた PC
                    // (NTP 未同期 / RTC 電池切れで未来日時) のバックアップが常に「最新」扱いで上に居座り、
                    // 時計正常な PC が取った直近バックアップが UI で見えなくなる。retention 側 (id DESC) と
                    // 揃えて時計非依存にする。
                    using (var cmd = new SQLiteCommand(
                        "SELECT id, started_at, completed_at, pc_name, file_path, relative_path, file_size_bytes, " +
                        "status, error_message, trigger_type FROM backup_log " +
                        "WHERE status = 'success' ORDER BY id DESC LIMIT 100", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var entry = ReadEntry(reader);
                            string resolved = ResolvePathForExistsCheck(entry.FilePath, entry.RelativePath);
                            if (!string.IsNullOrEmpty(resolved) && System.IO.File.Exists(resolved))
                                return entry;
                        }
                        return null;
                    }
                }
            });
        }

        /// <summary>
        /// (追加精査 ⑦) trigger_type='auto' AND status='success' の中で最新の行を返す。
        /// BackupSectionPanel の削除処理で last_backup_at の rewind 対象を決めるために使う。
        /// 並び順は `id DESC` (= INSERT 順、AUTOINCREMENT) で wall clock 不依存。
        /// 該当行が無ければ null を返す (= settings.last_backup_at を 0 に rewind すべきケース)。
        /// </summary>
        public BackupLogEntry GetLastAutoSuccess()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "SELECT id, started_at, completed_at, pc_name, file_path, relative_path, file_size_bytes, " +
                        "status, error_message, trigger_type FROM backup_log " +
                        "WHERE trigger_type = 'auto' AND status = 'success' " +
                        "ORDER BY id DESC LIMIT 1", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                            return ReadEntry(reader);
                        return null;
                    }
                }
            });
        }

        /// <summary>
        /// `file_path` が空のまま残っている `failed` 行について、バックアップフォルダ内の
        /// `toneprism_yyyyMMdd_HHmmss.db` 形式のファイル名と `started_at` を照合し、
        /// 一致するファイルが見つかれば `success` として復元する。
        ///
        /// 旧バージョンの Manager（`InsertInProgress` がファイルパスを記録していなかった頃）に
        /// 作られた行が、後のリストアでゴーストとして自動的に `failed` 化されたケースの救済用。
        /// </summary>
        /// <returns>復元された行数</returns>
        public int RecoverLegacyFailedEntriesByFolderScan(string backupFolder)
        {
            if (string.IsNullOrEmpty(backupFolder) || !System.IO.Directory.Exists(backupFolder))
                return 0;

            // ファイル一覧を timestamp → path のマップに
            // (追加精査 ⑥) 同 1 秒衝突回避の `_2` / `_3` suffix も受け入れる。fileMap の key は timestamp
            // 単位なので、同 1 秒に複数ファイルが存在する場合は後勝ち (この path は旧版救済専用で、新規
            // バックアップは InsertInProgress で file_path を記録するため通常はゼロ件)。
            var fileMap = new Dictionary<string, string>();
            var regex = new Regex(@"^toneprism_(\d{8})_(\d{6})(_\d+)?\.db$");
            try
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(backupFolder, "toneprism_*.db"))
                {
                    var name = System.IO.Path.GetFileName(file);
                    var match = regex.Match(name);
                    if (!match.Success) continue;
                    string ts = $"{match.Groups[1].Value}_{match.Groups[2].Value}";
                    fileMap[ts] = file;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[BackupLogRepository] フォルダスキャン失敗", ex);
                return 0;
            }
            if (fileMap.Count == 0) return 0;

            return _conn.ExecuteWithRetry(() =>
            {
                int recovered = 0;
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    // file_path が空の failed 行を取得
                    var targets = new List<(long id, long startedAt)>();
                    using (var cmd = new SQLiteCommand(
                        "SELECT id, started_at FROM backup_log " +
                        "WHERE status = 'failed' AND (file_path IS NULL OR file_path = '')", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            targets.Add((Convert.ToInt64(reader["id"]), Convert.ToInt64(reader["started_at"])));
                        }
                    }

                    foreach (var (id, startedAt) in targets)
                    {
                        var localTime = DateTimeOffset.FromUnixTimeSeconds(startedAt).LocalDateTime;
                        // タイミングずれ（DateTime.Now と UtcNow.ToUnixTimeSeconds の小さな差）に
                        // 対応するため ±1 秒の範囲でマッチさせる
                        for (int offset = -1; offset <= 1; offset++)
                        {
                            var candidate = localTime.AddSeconds(offset);
                            string ts = candidate.ToString("yyyyMMdd_HHmmss");
                            if (fileMap.TryGetValue(ts, out string filePath))
                            {
                                long size = new System.IO.FileInfo(filePath).Length;
                                using (var cmd = new SQLiteCommand(
                                    "UPDATE backup_log SET status = 'success', file_path = @fp, " +
                                    "file_size_bytes = @size, error_message = NULL WHERE id = @id", connection))
                                {
                                    cmd.Parameters.AddWithValue("@fp", filePath);
                                    cmd.Parameters.AddWithValue("@size", size);
                                    cmd.Parameters.AddWithValue("@id", id);
                                    cmd.ExecuteNonQuery();
                                }
                                recovered++;
                                break;
                            }
                        }
                    }
                }
                return recovered;
            });
        }

        /// <summary>
        /// 退避フォルダ内の `safety_yyyyMMdd_HHmmss.db` および
        /// 旧形式 `safety_before_restore_yyyyMMdd_HHmmss.db` のファイルを走査し、
        /// `backup_log` に未登録のものを `trigger_type='safety'`, `status='success'` で挿入する。
        /// `started_at` はファイル名のタイムスタンプ（ローカル時刻 → UTC秒）から復元する。
        /// </summary>
        /// <returns>新規登録された行数</returns>
        public int RegisterUnknownSafetyFiles(string safetyDir, string pcName)
        {
            if (string.IsNullOrEmpty(safetyDir) || !System.IO.Directory.Exists(safetyDir))
                return 0;

            return _conn.ExecuteWithRetry(() =>
            {
                int added = 0;
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    // 既に登録済みの safety 行の file_path をセットに集める
                    // (追加精査 ⑧) DB に入っている file_path は登録時の表記そのまま (例: PC-A は
                    // `C:\TonePrism\backups\safety\X.db`、PC-B は `\\srv\TonePrism\backups\safety\X.db`)。
                    // LAN 共有運用で UNC とドライブ文字が混在すると、同一物理ファイルが別 entry として
                    // 2 重登録される。`Path.GetFullPath` (= 絶対化 + `\\?\` プレフィックス除去 + 末尾区切り
                    // 整理) で正規化した path も併用して重複判定の精度を上げる。GetFullPath 不能な
                    // 不正 path は元の文字列のままセットに入れる (失敗側で hit させない方向)。
                    var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = new SQLiteCommand(
                        "SELECT file_path FROM backup_log " +
                        "WHERE trigger_type = 'safety' AND file_path IS NOT NULL AND file_path != ''",
                        connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string p = reader["file_path"].ToString();
                            existingPaths.Add(p);
                            try { existingPaths.Add(System.IO.Path.GetFullPath(p)); }
                            catch { /* 不正 path は元の文字列だけ登録 */ }
                        }
                    }

                    // (追加精査 ⑥) 同 1 秒衝突回避の `_2` / `_3` suffix も受け入れる (optional)。
                    var newRegex = new Regex(@"^safety_(\d{8})_(\d{6})(_\d+)?\.db$");
                    var legacyRegex = new Regex(@"^safety_before_restore_(\d{8})_(\d{6})(_\d+)?\.db$");

                    foreach (var file in System.IO.Directory.EnumerateFiles(safetyDir, "*.db"))
                    {
                        string name = System.IO.Path.GetFileName(file);
                        Match match = newRegex.Match(name);
                        if (!match.Success) match = legacyRegex.Match(name);
                        if (!match.Success) continue;
                        // (追加精査 ⑧) enumerate 由来の生 path と正規化 path の両方で重複判定。
                        if (existingPaths.Contains(file)) continue;
                        string normalizedFile = file;
                        try { normalizedFile = System.IO.Path.GetFullPath(file); }
                        catch { /* GetFullPath 失敗時は生 path のまま判定 */ }
                        if (!ReferenceEquals(file, normalizedFile) && existingPaths.Contains(normalizedFile)) continue;

                        string dateStr = match.Groups[1].Value;
                        string timeStr = match.Groups[2].Value;
                        if (!DateTime.TryParseExact($"{dateStr}_{timeStr}", "yyyyMMdd_HHmmss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeLocal,
                            out DateTime localTime))
                        {
                            continue;
                        }
                        long startedAt = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime)).ToUnixTimeSeconds();

                        long fileSize;
                        try
                        {
                            fileSize = new System.IO.FileInfo(file).Length;
                        }
                        catch
                        {
                            continue;
                        }

                        using (var insertCmd = new SQLiteCommand(
                            "INSERT INTO backup_log (started_at, completed_at, pc_name, file_path, " +
                            "file_size_bytes, status, trigger_type) " +
                            "VALUES (@started, @completed, @pc, @path, @size, 'success', 'safety')",
                            connection))
                        {
                            insertCmd.Parameters.AddWithValue("@started", startedAt);
                            insertCmd.Parameters.AddWithValue("@completed", startedAt);
                            insertCmd.Parameters.AddWithValue("@pc", pcName ?? "");
                            insertCmd.Parameters.AddWithValue("@path", file);
                            insertCmd.Parameters.AddWithValue("@size", fileSize);
                            insertCmd.ExecuteNonQuery();
                        }
                        added++;
                    }
                }
                return added;
            });
        }

        /// <summary>
        /// (累積監査 round 6 #7) バックアップ保存先フォルダ内の `toneprism_yyyyMMdd_HHmmss[_suffix].db` の
        /// うち `backup_log` に未登録のもの (= orphan ファイル) を `trigger_type='manual'`, `status='success'`
        /// で登録する。`RegisterUnknownSafetyFiles` の本体バックアップ版。
        ///
        /// 背景: バックアップ/復元は `toneprism.db` のみを対象とするため、別時点の DB を復元すると
        /// `backup_log` がスナップショット時点の古い状態に置き換わり、スナップショット後〜復元前に取られた
        /// 自動バックアップの **log 行だけが消えてファイル実体が残る** orphan が生じる。旧実装ではこの orphan は
        /// (a) 履歴グリッドに出ない (= グリッドは log 行を表示するため) ので運営から見えず、(b) retention は
        /// `backup_log` 駆動なので永久に削除対象外、という「見えないまま溜まり続ける」二重の死角だった。
        ///
        /// **安全方針 (= 自動削除しない)**: ファイル名から「手動取得か自動取得か」は区別不能なため、orphan を
        /// 自動 retention 対象にすると手動バックアップを誤削除しうる。そこで削除はせず、`trigger_type='manual'`
        /// (= retention 対象外 + 履歴/削除 UI に表示 + 復元可能) として **可視化** するに留める。これにより
        /// 「見えないまま溜まる」死角を解消し、不要ファイルは運営が履歴の「削除」から手動整理できる。
        /// </summary>
        /// <returns>新規登録された行数</returns>
        public int RegisterUnknownBackupFiles(string backupDir, string pcName)
        {
            if (string.IsNullOrEmpty(backupDir) || !System.IO.Directory.Exists(backupDir))
                return 0;

            return _conn.ExecuteWithRetry(() =>
            {
                int added = 0;
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    // 既に backup_log に登録済みの全 file_path をセットに集める (trigger_type 不問: 同一ファイルが
                    // auto/manual/safety/restore のどれで記録されていても二重登録を避ける)。UNC ↔ ドライブ文字の
                    // 表記揺れ対策に GetFullPath 正規化 path も併用 (RegisterUnknownSafetyFiles と同方針)。
                    var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // (累積監査 round 6 追補) file_path は PC ごとに UNC (`\\srv\...`) ↔ ドライブ文字 (`Z:\...`) で
                    // 表記が割れ、`GetFullPath` でも解決できない。SMB 共有運用 (本番) で backups フォルダを複数 PC が
                    // 共有すると、他 PC が登録済の auto バックアップを file_path 表記揺れで取りこぼし、manual として
                    // 重複登録 → その行は retention 対象外なので「auto を manual 化して世代管理から外す」副作用に
                    // なる。PC 非依存の relative_path (dbDir 基準) も dedup キーに併用してこの穴を塞ぐ。
                    var existingRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = new SQLiteCommand(
                        "SELECT file_path, relative_path FROM backup_log WHERE file_path IS NOT NULL AND file_path != ''",
                        connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string p = reader["file_path"].ToString();
                            existingPaths.Add(p);
                            try { existingPaths.Add(System.IO.Path.GetFullPath(p)); }
                            catch { /* 不正 path は元の文字列だけ登録 */ }
                            if (!(reader["relative_path"] is DBNull))
                            {
                                string rp = reader["relative_path"].ToString();
                                if (!string.IsNullOrEmpty(rp)) existingRelPaths.Add(rp);
                            }
                        }
                    }

                    // 旧レイアウト (移行前 = backupDir 直下の toneprism_*.db) と新レイアウト
                    // (<backupDir>/auto/auto_*.db, <backupDir>/manual/manual_*.db) の両方を走査する。
                    // 新レイアウトは「どのサブフォルダにあるか」で auto / manual を確定できるため、backup_log を
                    // 失った孤児でも種類を正しく復元でき、auto は世代管理 (retention) に戻る (Finding 4 根治)。
                    // 旧レイアウトの toneprism_*.db は種類不明 (auto/manual がファイル名から区別不能) なので、
                    // 従来通り安全側の manual で登録する (= 誤って世代管理対象にして手動バックアップを消さない)。
                    var legacyRegex = new Regex(@"^toneprism_(\d{8})_(\d{6})(_.+)?\.db$");
                    var autoRegex = new Regex(@"^auto_(\d{8})_(\d{6})(_.+)?\.db$");
                    var manualRegex = new Regex(@"^manual_(\d{8})_(\d{6})(_.+)?\.db$");
                    string dbDir = System.IO.Path.GetDirectoryName(_conn.DbPath);

                    added += RegisterUnknownInFolder(connection, backupDir, "toneprism_*.db", legacyRegex, "manual", pcName, dbDir, existingPaths, existingRelPaths);
                    added += RegisterUnknownInFolder(connection, System.IO.Path.Combine(backupDir, "auto"), "auto_*.db", autoRegex, "auto", pcName, dbDir, existingPaths, existingRelPaths);
                    added += RegisterUnknownInFolder(connection, System.IO.Path.Combine(backupDir, "manual"), "manual_*.db", manualRegex, "manual", pcName, dbDir, existingPaths, existingRelPaths);
                }
                return added;
            });
        }

        /// <summary>
        /// 1 つのフォルダを走査し、backup_log 未登録の `<searchPattern>` ファイルを `triggerType` + `status='success'`
        /// で登録する (`RegisterUnknownBackupFiles` の 1 フォルダ分の本体)。フォルダ位置で種類 (auto / manual) が
        /// 確定するため、種類を引数で受け取って INSERT する。重複判定は file_path (生 + GetFullPath 正規化) と
        /// PC 非依存の relative_path (dbDir 基準) の併用。登録した path は同一 run 内の二重登録防止のため
        /// existing セットにも追加する。フォルダ不在なら 0。
        /// </summary>
        private int RegisterUnknownInFolder(
            SQLiteConnection connection, string folder, string searchPattern, Regex regex,
            string triggerType, string pcName, string dbDir,
            HashSet<string> existingPaths, HashSet<string> existingRelPaths)
        {
            if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder)) return 0;

            int added = 0;
            foreach (var file in System.IO.Directory.EnumerateFiles(folder, searchPattern))
            {
                string name = System.IO.Path.GetFileName(file);
                Match match = regex.Match(name);
                if (!match.Success) continue;

                if (existingPaths.Contains(file)) continue;
                string normalizedFile = file;
                try { normalizedFile = System.IO.Path.GetFullPath(file); }
                catch { /* GetFullPath 失敗時は生 path のまま判定 */ }
                if (!ReferenceEquals(file, normalizedFile) && existingPaths.Contains(normalizedFile)) continue;

                // プロジェクト移動耐性のため相対 path を計算 (InsertInProgress と同方針) + PC 非依存の
                // dedup キーとしても使う。relative_path が既存行と一致すれば (file_path 表記揺れで上の
                // file_path 判定をすり抜けていても) 重複として skip する。
                string relativePath = BackupPathResolver.ToRelativeFromDbDir(file, dbDir);
                if (!string.IsNullOrEmpty(relativePath) && existingRelPaths.Contains(relativePath)) continue;

                string dateStr = match.Groups[1].Value;
                string timeStr = match.Groups[2].Value;
                if (!DateTime.TryParseExact($"{dateStr}_{timeStr}", "yyyyMMdd_HHmmss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out DateTime localTime))
                {
                    continue;
                }
                long startedAt = new DateTimeOffset(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime)).ToUnixTimeSeconds();

                long fileSize;
                try { fileSize = new System.IO.FileInfo(file).Length; }
                catch { continue; }

                using (var insertCmd = new SQLiteCommand(
                    "INSERT INTO backup_log (started_at, completed_at, pc_name, file_path, relative_path, " +
                    "file_size_bytes, status, trigger_type) " +
                    "VALUES (@started, @completed, @pc, @path, @relpath, @size, 'success', @trigger)",
                    connection))
                {
                    insertCmd.Parameters.AddWithValue("@started", startedAt);
                    insertCmd.Parameters.AddWithValue("@completed", startedAt);
                    insertCmd.Parameters.AddWithValue("@pc", pcName ?? "");
                    insertCmd.Parameters.AddWithValue("@path", file);
                    insertCmd.Parameters.AddWithValue("@relpath",
                        string.IsNullOrEmpty(relativePath) ? (object)DBNull.Value : relativePath);
                    insertCmd.Parameters.AddWithValue("@size", fileSize);
                    insertCmd.Parameters.AddWithValue("@trigger", triggerType);
                    insertCmd.ExecuteNonQuery();
                }

                // 同一 run 内 (= 後続フォルダ走査) での二重登録防止のため existing セットにも反映。
                existingPaths.Add(file);
                if (!ReferenceEquals(file, normalizedFile)) existingPaths.Add(normalizedFile);
                if (!string.IsNullOrEmpty(relativePath)) existingRelPaths.Add(relativePath);
                added++;
            }
            return added;
        }

        /// <summary>
        /// 指定ステータスのレコードを全件取得 (#126: failed 自動掃除等で利用)
        /// </summary>
        public List<BackupLogEntry> GetByStatus(string status)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var list = new List<BackupLogEntry>();
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "SELECT id, started_at, completed_at, pc_name, file_path, relative_path, file_size_bytes, " +
                        "status, error_message, trigger_type FROM backup_log " +
                        "WHERE status = @status ORDER BY started_at DESC", connection))
                    {
                        cmd.Parameters.AddWithValue("@status", status);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                list.Add(ReadEntry(reader));
                        }
                    }
                }
                return list;
            });
        }

        /// <summary>
        /// id 指定でレコードを削除 (#126: 個別削除 UI / failed 自動掃除で利用)
        /// </summary>
        public void DeleteById(long id)
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "DELETE FROM backup_log WHERE id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
            });
        }

        /// <summary>
        /// 自動バックアップで成功した行のうち、新しい順に `keepCount` 件を除いた残り (= retention で削除対象に
        /// すべき行) を返す (#235)。
        ///
        /// **trigger_type='auto' AND status='success'** のみが対象。`manual` (手動取得) / `safety` (復元前の自動退避) /
        /// `failed` (失敗履歴) は絶対に retention 対象にしない。旧実装は backups フォルダ内の
        /// `toneprism_*.db` ファイル名パターンだけで判定していたため、手動取得分も自動 retention で
        /// silent に削除されていた。
        ///
        /// `keepCount &lt;= 0` の場合は空リストを返す (= retention 無効、削除しない)。
        /// </summary>
        public List<BackupLogEntry> GetAutoSuccessRetentionTargets(int keepCount)
        {
            if (keepCount <= 0) return new List<BackupLogEntry>();
            return _conn.ExecuteWithRetry(() =>
            {
                var list = new List<BackupLogEntry>();
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    // 新しい順に並べて keepCount 件を skip した残りを返す。
                    // ORDER BY は `id DESC` (= AUTOINCREMENT INSERT 順) を SoT にする。`started_at` は
                    // 各 PC の wall clock 由来 (DateTimeOffset.UtcNow.ToUnixTimeSeconds) なので、LAN 上で
                    // 時計がズレた PC が混ざる (NTP 同期前 / RTC 電池切れ等) と「後から取られたバックアップ」
                    // が古い扱いで先に削除される逆転が起きる。id は SQLite が INSERT した瞬間に振る単調
                    // 増加値なので、書き込んだ瞬間の真の時系列順序 (= 同一 DB に対する write は排他なので
                    // serialise される) を時計に依存せず復元できる。
                    // WHERE 句で trigger_type='auto' AND status='success' に絞っているため、ORDER 変更は
                    // 手動 / safety / failed の保護 (= 削除対象外) には影響しない。
                    using (var cmd = new SQLiteCommand(
                        "SELECT id, started_at, completed_at, pc_name, file_path, relative_path, file_size_bytes, " +
                        "status, error_message, trigger_type FROM backup_log " +
                        "WHERE trigger_type = 'auto' AND status = 'success' " +
                        "ORDER BY id DESC " +
                        "LIMIT -1 OFFSET @offset", connection))
                    {
                        cmd.Parameters.AddWithValue("@offset", keepCount);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                list.Add(ReadEntry(reader));
                        }
                    }
                }
                return list;
            });
        }

        public List<BackupLogEntry> GetRecent(int limit)
        {
            return _conn.ExecuteWithRetry(() =>
            {
                var list = new List<BackupLogEntry>();
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    using (var cmd = new SQLiteCommand(
                        "SELECT id, started_at, completed_at, pc_name, file_path, relative_path, file_size_bytes, " +
                        "status, error_message, trigger_type FROM backup_log " +
                        "ORDER BY started_at DESC LIMIT @limit", connection))
                    {
                        cmd.Parameters.AddWithValue("@limit", limit);
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                list.Add(ReadEntry(reader));
                        }
                    }
                }
                return list;
            });
        }

        private static BackupLogEntry ReadEntry(SQLiteDataReader reader)
        {
            return new BackupLogEntry
            {
                Id = Convert.ToInt64(reader["id"]),
                StartedAt = Convert.ToInt64(reader["started_at"]),
                CompletedAt = reader["completed_at"] is DBNull ? (long?)null : Convert.ToInt64(reader["completed_at"]),
                PcName = reader["pc_name"] is DBNull ? "" : reader["pc_name"].ToString(),
                FilePath = reader["file_path"] is DBNull ? "" : reader["file_path"].ToString(),
                RelativePath = reader["relative_path"] is DBNull ? null : reader["relative_path"].ToString(),
                FileSizeBytes = reader["file_size_bytes"] is DBNull ? (long?)null : Convert.ToInt64(reader["file_size_bytes"]),
                Status = reader["status"].ToString(),
                ErrorMessage = reader["error_message"] is DBNull ? "" : reader["error_message"].ToString(),
                TriggerType = reader["trigger_type"].ToString()
            };
        }
    }
}
