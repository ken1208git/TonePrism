using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#250 PR1) DB バックアップと同時に `games/` + `guide/` を**ハードリンク世代スナップショット**
    /// (rsync `--link-dest` 方式) として取得する service。
    ///
    /// 各世代は「前世代から不変のファイルはハードリンク (実体非複製)、新規/変更分だけ実コピー」で構成され、
    /// 実ディスク消費は ≈ ベースライン 1 本 + 版追加差分 で世代数に比例しない。各世代は独立・完全に復元可能
    /// (ハードリンク = 同一実体への対等なエントリのため、retention で古い世代を消しても新世代は無傷)。
    ///
    /// **best-effort**: スナップショット取得の失敗・キャンセルは throw せず <see cref="SnapshotResult"/> で返す。
    /// 呼び出し側 (BackupService) が DB バックアップの成否や last_backup_at を巻き戻さないことを保証する
    /// (= 完了済みの DB バックアップを守る)。
    /// </summary>
    public class AssetSnapshotService
    {
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settingsRepo;
        private readonly BackupService _backupService;

        /// <summary>size+mtime 一致判定の mtime 許容差 (秒)。rsync の --modify-window 相当。FAT の 2 秒粒度 / SMB 丸めを吸収。</summary>
        private const int MtimeToleranceSeconds = 2;
        private static readonly string[] SubFolders = { "games", "guide" };
        private const string MetaFileName = ".snapshot_meta.txt";
        private const string SnapshotRootName = "asset_snapshots";

        public AssetSnapshotService(DatabaseConnection conn, SettingsRepository settingsRepo, BackupService backupService)
        {
            _conn = conn;
            _settingsRepo = settingsRepo;
            _backupService = backupService;
        }

        /// <summary>`<backup_dest>/asset_snapshots/` の絶対パス。</summary>
        public string GetSnapshotRootDirectory()
            => Path.Combine(_backupService.GetEffectiveDestinationDirectory(), SnapshotRootName);

        /// <summary>
        /// games/ + guide/ を 1 世代取得する。DB バックアップ成功直後に**同一 timestamp / triggerType** で呼ばれる。
        /// best-effort (throw しない)。`asset_snapshot_enabled=false` なら Skipped。
        /// </summary>
        public SnapshotResult CreateSnapshot(string timestamp, string triggerType,
            IProgress<ProgressInfo> progress = null, CancellationToken token = default(CancellationToken))
            => CreateSnapshot(timestamp, triggerType, progress, token, null);

        /// <summary>テスト用 seam: capableOverride でハードリンク可否を強制注入 (null = 実プローブ)。</summary>
        internal SnapshotResult CreateSnapshot(string timestamp, string triggerType,
            IProgress<ProgressInfo> progress, CancellationToken token, bool? capableOverride)
        {
            string tmpDir = null;
            try
            {
                if (!IsEnabled())
                    return SnapshotResult.Skipped("asset_snapshot_enabled=false");

                string baseInstallDir = Path.GetDirectoryName(_conn.DbPath);
                bool anySource = SubFolders.Any(s => Directory.Exists(Path.Combine(baseInstallDir, s)));
                if (!anySource)
                    return SnapshotResult.Success(null, 0, 0, false); // games//guide/ がそもそも無い install

                string triggerDir = Path.Combine(GetSnapshotRootDirectory(), triggerType);
                Directory.CreateDirectory(triggerDir);
                CleanupStaleTempDirs(triggerDir);

                string host = BackupService.SanitizeHostForFileName(Environment.MachineName);
                string leaf = string.IsNullOrEmpty(host) ? timestamp : timestamp + "_" + host;
                string finalDir = ResolveUniqueFinalDir(triggerDir, leaf);
                tmpDir = Path.Combine(triggerDir, ".tmp_" + Path.GetFileName(finalDir));

                bool capable = capableOverride ?? HardLinkSupport.ProbeHardLinkSupport(triggerDir);
                if (!capable)
                    Logger.Warn("[AssetSnapshot] 保存先がハードリンク非対応のため全実コピーになります。容量増を避けるには asset_snapshot_retention_count を小さく: " + triggerDir);

                WarnIfLowDiskSpace(triggerDir);
                string baseDir = SelectBaseSnapshot(triggerDir);

                int total = SubFolders.Sum(s => SafeCountFiles(Path.Combine(baseInstallDir, s)));
                var stats = new Stats();

                Directory.CreateDirectory(tmpDir);
                foreach (var sub in SubFolders)
                {
                    string src = Path.Combine(baseInstallDir, sub);
                    if (!Directory.Exists(src)) continue;
                    string dst = Path.Combine(tmpDir, sub);
                    string bse = baseDir != null ? Path.Combine(baseDir, sub) : null;
                    CopyOrLinkTree(src, dst, bse, capable, progress, token, total, stats);
                }
                token.ThrowIfCancellationRequested();

                WriteMeta(tmpDir, stats, capable, triggerType);
                Directory.Move(tmpDir, finalDir); // 同一ボリューム rename = 事実上 atomic
                ApplyRetention(triggerType);

                Logger.Info(string.Format("[AssetSnapshot] 取得完了: {0} ({1} files, copied={2}, linked={3}, hardlink={4})",
                    finalDir, stats.FileCount, stats.Copied, stats.Linked, capable));
                return SnapshotResult.Success(finalDir, stats.FileCount, stats.Bytes, stats.Linked > 0);
            }
            catch (OperationCanceledException)
            {
                // キャンセルされても **完了済みの DB バックアップは守る** ため、ここでは再 throw せず Skipped を返す
                // (再 throw すると BackupService の OperationCanceled ハンドラが成功済みの .db を削除してしまう)。
                if (tmpDir != null) FolderDeletionService.TryDelete(tmpDir);
                Logger.Info("[AssetSnapshot] 取得がキャンセルされました (DB バックアップは保持)");
                return SnapshotResult.Skipped("キャンセル");
            }
            catch (Exception ex)
            {
                if (tmpDir != null) FolderDeletionService.TryDelete(tmpDir);
                Logger.Error("[AssetSnapshot] 取得に失敗しました (DB バックアップは保持)", ex);
                return SnapshotResult.Failed(ex.Message);
            }
        }

        /// <summary>最新の完成スナップショット 1 件 (auto/manual 横断)。無ければ null。UI 表示用。</summary>
        public AssetSnapshotInfo GetLatestSnapshot()
        {
            try
            {
                string root = GetSnapshotRootDirectory();
                if (!Directory.Exists(root)) return null;

                AssetSnapshotInfo best = null;
                foreach (var triggerType in new[] { "auto", "manual" })
                {
                    string triggerDir = Path.Combine(root, triggerType);
                    if (!Directory.Exists(triggerDir)) continue;
                    foreach (var dir in Directory.GetDirectories(triggerDir))
                    {
                        string name = Path.GetFileName(dir);
                        if (name.StartsWith(".")) continue; // 構築中 .tmp_* 除外
                        var info = BuildInfo(dir, triggerType, name);
                        if (best == null || string.CompareOrdinal(info.Timestamp, best.Timestamp) > 0)
                            best = info;
                    }
                }
                return best;
            }
            catch (Exception ex)
            {
                Logger.Warn("[AssetSnapshot] 最新スナップショット情報の取得に失敗: " + ex.Message);
                return null;
            }
        }

        // ---- 内部 ----

        private bool IsEnabled()
        {
            string v = _settingsRepo.GetString(SettingsKeys.AssetSnapshotEnabled, "true");
            return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>同一 trigger 内の直近の完成世代を base に選ぶ (`.` 始まり除外・名前降順 first)。無ければ null = 初回全コピー。</summary>
        private static string SelectBaseSnapshot(string triggerDir)
        {
            if (!Directory.Exists(triggerDir)) return null;
            return Directory.GetDirectories(triggerDir)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string ResolveUniqueFinalDir(string triggerDir, string leaf)
        {
            string candidate = Path.Combine(triggerDir, leaf);
            int suffix = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate))
            {
                candidate = Path.Combine(triggerDir, leaf + "_" + suffix);
                suffix++;
                if (suffix > 99)
                    throw new Exception("スナップショット世代名の衝突回避に失敗しました (同 1 秒に 100 件以上): " + triggerDir);
            }
            return candidate;
        }

        /// <summary>link-dest 本体。base に「相対パス+サイズ+mtime 一致」で存在すればハードリンク、無ければ実コピー。</summary>
        private void CopyOrLinkTree(string srcDir, string dstDir, string baseDir, bool capable,
            IProgress<ProgressInfo> progress, CancellationToken token, int total, Stats stats)
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(FileOperationService.EnsureLongPath(dstDir));

            foreach (string file in Directory.GetFiles(srcDir))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(file);
                string dst = Path.Combine(dstDir, name);
                string baseFile = baseDir != null ? Path.Combine(baseDir, name) : null;
                bool linked = false;
                long size = 0;
                try { size = new FileInfo(FileOperationService.EnsureLongPath(file)).Length; } catch { }

                if (capable && baseFile != null && FilesEquivalent(file, baseFile))
                {
                    try
                    {
                        HardLinkSupport.CreateHardLink(FileOperationService.EnsureLongPath(dst), FileOperationService.EnsureLongPath(baseFile));
                        linked = true;
                        stats.Linked++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("[AssetSnapshot] ハードリンク失敗→実コピー: " + dst + " : " + ex.Message);
                    }
                }

                if (!linked)
                {
                    try
                    {
                        File.Copy(FileOperationService.EnsureLongPath(file), FileOperationService.EnsureLongPath(dst), false);
                        stats.Copied++;
                    }
                    catch (PathTooLongException)
                    {
                        Logger.Warn("[AssetSnapshot] パスが長すぎてスキップ: " + file);
                        stats.Failed++;
                        continue;
                    }
                }

                stats.FileCount++;
                stats.Bytes += size;
                if (total > 0 && progress != null)
                {
                    int pct = (int)((double)stats.FileCount / total * 100);
                    if (pct > 100) pct = 100;
                    progress.Report(new ProgressInfo(pct, "アセットを保存中...", name));
                }
            }

            foreach (string subDir in Directory.GetDirectories(srcDir))
            {
                token.ThrowIfCancellationRequested();
                // symlink / junction は辿らない+コピーしない (無限ループ / コピー爆発防止)。
                FileAttributes attr;
                try { attr = File.GetAttributes(subDir); }
                catch { continue; }
                if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    Logger.Warn("[AssetSnapshot] reparse point をスキップ: " + subDir);
                    continue;
                }
                string folder = Path.GetFileName(subDir);
                CopyOrLinkTree(subDir, Path.Combine(dstDir, folder),
                    baseDir != null ? Path.Combine(baseDir, folder) : null, capable, progress, token, total, stats);
            }
        }

        /// <summary>base にハードリンクしてよいか (= 同一内容とみなせるか)。size + mtime(UTC, 2 秒許容)。</summary>
        private static bool FilesEquivalent(string srcFile, string baseFile)
        {
            try
            {
                string sb = FileOperationService.EnsureLongPath(baseFile);
                if (!File.Exists(sb)) return false;
                var src = new FileInfo(FileOperationService.EnsureLongPath(srcFile));
                var bse = new FileInfo(sb);
                if (src.Length != bse.Length) return false;
                return Math.Abs((src.LastWriteTimeUtc - bse.LastWriteTimeUtc).TotalSeconds) <= MtimeToleranceSeconds;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>auto 世代のみ retention 適用 (manual は温存)。古い世代の**ディレクトリエントリのみ**削除 (実体は共有新世代が残れば生存)。</summary>
        private void ApplyRetention(string triggerType)
        {
            if (!string.Equals(triggerType, "auto", StringComparison.OrdinalIgnoreCase)) return;
            int count = _settingsRepo.GetInt32(SettingsKeys.AssetSnapshotRetentionCount, SettingsKeys.DefaultAssetSnapshotRetentionCount);
            if (count <= 0) return;
            string autoDir = Path.Combine(GetSnapshotRootDirectory(), "auto");
            if (!Directory.Exists(autoDir)) return;

            var stale = Directory.GetDirectories(autoDir)
                .Where(d => !Path.GetFileName(d).StartsWith("."))
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .Skip(count)
                .ToList();
            foreach (var dir in stale)
            {
                var r = FolderDeletionService.TryDelete(dir);
                if (r.Success) Logger.Info("[AssetSnapshot] 古い世代を削除: " + dir);
                else Logger.Warn("[AssetSnapshot] 世代削除に失敗 (次回再試行): " + dir + " : " + r.ErrorMessage);
            }
        }

        /// <summary>前回中断の `.tmp_*` を 30 分超で回収する。</summary>
        private static void CleanupStaleTempDirs(string triggerDir)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(triggerDir, ".tmp_*"))
                {
                    try
                    {
                        if ((DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir)).TotalMinutes >= 30)
                            FolderDeletionService.TryDelete(dir);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static int SafeCountFiles(string dir)
        {
            try { return Directory.Exists(dir) ? FileOperationService.CountFiles(dir) : 0; }
            catch { return 0; }
        }

        private static void WarnIfLowDiskSpace(string triggerDir)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(triggerDir));
                if (string.IsNullOrEmpty(root)) return;
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace < 200L * 1024 * 1024) // 200MB
                    Logger.Warn("[AssetSnapshot] 保存先の空き容量が少ない可能性: " + root + " 残り " + (drive.AvailableFreeSpace / (1024 * 1024)) + "MB");
            }
            catch { /* 空き容量チェックは best-effort */ }
        }

        private static void WriteMeta(string snapshotDir, Stats stats, bool usedHardLinks, string triggerType)
        {
            try
            {
                string path = Path.Combine(snapshotDir, MetaFileName);
                File.WriteAllText(path, string.Join("\n", new[]
                {
                    "fileCount=" + stats.FileCount.ToString(CultureInfo.InvariantCulture),
                    "logicalBytes=" + stats.Bytes.ToString(CultureInfo.InvariantCulture),
                    "usedHardLinks=" + (usedHardLinks ? "true" : "false"),
                    "trigger=" + triggerType
                }));
            }
            catch (Exception ex)
            {
                Logger.Warn("[AssetSnapshot] meta 書込失敗 (無害): " + ex.Message);
            }
        }

        private static AssetSnapshotInfo BuildInfo(string dir, string triggerType, string name)
        {
            // name = "yyyyMMdd_HHmmss" or "yyyyMMdd_HHmmss_host" (+ optional _N collision)
            string ts = name.Length >= 15 ? name.Substring(0, 15) : name; // yyyyMMdd_HHmmss = 15 文字
            string host = name.Length > 16 ? name.Substring(16) : "";
            DateTime local;
            if (!DateTime.TryParseExact(ts, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
                local = DateTime.MinValue;

            var info = new AssetSnapshotInfo
            {
                Timestamp = ts,
                StartedAtLocal = local,
                TriggerType = triggerType,
                Host = host,
                DirectoryPath = dir
            };
            // meta があれば件数/サイズ/リンク有無を読む (無ければ 0/false)。
            try
            {
                string metaPath = Path.Combine(dir, MetaFileName);
                if (File.Exists(metaPath))
                {
                    foreach (var line in File.ReadAllLines(metaPath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq);
                        string v = line.Substring(eq + 1);
                        if (k == "fileCount") { int fc; int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out fc); info.FileCount = fc; }
                        else if (k == "logicalBytes") { long lb; long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out lb); info.LogicalBytes = lb; }
                        else if (k == "usedHardLinks") info.UsedHardLinks = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }
            return info;
        }

        private sealed class Stats
        {
            public int FileCount;
            public int Copied;
            public int Linked;
            public int Failed;
            public long Bytes;
        }
    }
}
