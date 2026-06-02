using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#250 PR1) DB バックアップと同時に `games/` + `guide/` を **共有プール方式 (CAS / SHA-256)** で控える service。
    ///
    /// 中身ごとに `asset_pool/<hash>` に実体 1 個だけ置き、各世代は「相対パス→ハッシュ」の小さな manifest にする。
    /// 同じ中身は (別ゲーム間・版違い間でも) 1 個に集約されるため、ファイルサイズを単純合計するどんな仕組み
    /// (エクスプローラー / SMB サーバーの容量計算 / quota) でも**実サイズしか出ない** (= 削減が見える)。
    /// SHA-256 なので「中身が違うのに同じハッシュ」は起こらず、名前が同じでも中身が違えば必ず別保存される。
    /// コピーベースなので SMB 越し / 別ボリュームでも有効 (ハードリンクと違う)。
    ///
    /// **best-effort**: 失敗・キャンセルは throw せず <see cref="SnapshotResult"/> で返し、DB バックアップの成否・
    /// last_backup_at を壊さない (完了済み DB バックアップを守る)。
    /// </summary>
    public class AssetSnapshotService
    {
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settingsRepo;
        private readonly BackupService _backupService;

        private static readonly string[] SubFolders = { "games", "guide" };
        private const string PoolDirName = "asset_pool";
        private const string ManifestDirName = "asset_snapshots";
        private const string ManifestExt = ".manifest";
        private const string MetaLinePrefix = "META";
        /// <summary>(レビュー M1) pool 物理サイズのキャッシュ。UI から pool 全列挙 (SMB で重い) を避けるため、
        /// バックアップ時にバックグラウンドスレッドで算出してここに書き、UI は即時読みする。</summary>
        private const string PoolSizeFileName = ".poolsize";
        /// <summary>GC で未参照 pool ファイルを消すときの猶予 (直近書込/並行 backup のレース回避)。テストで 0 に上書き可。</summary>
        internal TimeSpan GcGracePeriod = TimeSpan.FromHours(1);

        public AssetSnapshotService(DatabaseConnection conn, SettingsRepository settingsRepo, BackupService backupService)
        {
            _conn = conn;
            _settingsRepo = settingsRepo;
            _backupService = backupService;
        }

        public string GetSnapshotRootDirectory() => Path.Combine(_backupService.GetEffectiveDestinationDirectory(), ManifestDirName);
        private string GetPoolRootDirectory() => Path.Combine(_backupService.GetEffectiveDestinationDirectory(), PoolDirName);

        /// <summary>games/ + guide/ を 1 世代取得する。DB バックアップ成功直後に同一 timestamp/trigger で best-effort 呼び出し。</summary>
        public SnapshotResult CreateSnapshot(string timestamp, string triggerType,
            IProgress<ProgressInfo> progress = null, CancellationToken token = default(CancellationToken))
        {
            string tmpManifest = null;
            try
            {
                if (!IsEnabled()) return SnapshotResult.Skipped("asset_snapshot_enabled=false");

                string baseInstallDir = Path.GetDirectoryName(_conn.DbPath);
                if (!SubFolders.Any(s => Directory.Exists(Path.Combine(baseInstallDir, s))))
                    return SnapshotResult.Success(null, 0, 0, 0); // games//guide/ が無い install

                string poolRoot = GetPoolRootDirectory();
                Directory.CreateDirectory(poolRoot);
                string manifestTriggerDir = Path.Combine(GetSnapshotRootDirectory(), triggerType);
                Directory.CreateDirectory(manifestTriggerDir);

                var cache = LoadHashCache();                 // 直近 manifest から relpath→(size,mtime,hash)
                var entries = new List<string>();
                var stats = new Stats();
                int total = SubFolders.Sum(s => SafeCountFiles(Path.Combine(baseInstallDir, s)));

                foreach (var sub in SubFolders)
                {
                    string src = Path.Combine(baseInstallDir, sub);
                    if (Directory.Exists(src))
                        WalkTree(src, sub, poolRoot, cache, entries, stats, progress, token, total);
                }
                token.ThrowIfCancellationRequested();

                // manifest を temp→rename で atomic 書き出し
                string host = BackupService.SanitizeHostForFileName(Environment.MachineName);
                string leaf = string.IsNullOrEmpty(host) ? timestamp : timestamp + "_" + host;
                string manifestPath = ResolveUniqueManifest(manifestTriggerDir, leaf);
                tmpManifest = manifestPath + ".tmp";
                WriteManifest(tmpManifest, timestamp, host, triggerType, stats, entries);
                if (File.Exists(manifestPath)) File.Delete(manifestPath);
                File.Move(tmpManifest, manifestPath);
                tmpManifest = null;

                ApplyRetentionAndGc(triggerType);

                Logger.Info(string.Format("[AssetSnapshot] 控え完了: {0} ({1} files / 論理 {2:F2}GB / 新規コピー {3:F2}MB)",
                    Path.GetFileName(manifestPath), stats.FileCount, stats.Bytes / 1073741824.0, stats.NewBytes / 1048576.0));
                return SnapshotResult.Success(manifestPath, stats.FileCount, stats.Bytes, stats.NewBytes);
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tmpManifest); // pool に途中コピーした実体は無参照のまま GC で回収される
                Logger.Info("[AssetSnapshot] 取得がキャンセルされました (DB バックアップは保持)");
                return SnapshotResult.Skipped("キャンセル");
            }
            catch (Exception ex)
            {
                TryDeleteFile(tmpManifest);
                Logger.Error("[AssetSnapshot] 取得に失敗しました (DB バックアップは保持)", ex);
                return SnapshotResult.Failed(ex.Message);
            }
        }

        /// <summary>最新の世代 1 件 (auto/manual 横断)。無ければ null。UI 用。</summary>
        public AssetSnapshotInfo GetLatestSnapshot()
        {
            try
            {
                var newest = EnumerateManifests().OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                return newest != null ? ReadManifestHeader(newest) : null;
            }
            catch (Exception ex)
            {
                Logger.Warn("[AssetSnapshot] 最新世代の取得に失敗: " + ex.Message);
                return null;
            }
        }

        /// <summary>アセットプールが実際に使っているディスク量 (= 重複排除後の物理サイズ)。UI 用。
        /// (レビュー M1) pool 全列挙は SMB で重く UI スレッドをフリーズさせうるので、バックアップ時に算出済の
        /// `.poolsize` キャッシュを即時読みする。無ければ 0 (= まだ取得していない)。</summary>
        public long GetPoolPhysicalBytes()
        {
            try
            {
                string sizeFile = FileOperationService.EnsureLongPath(Path.Combine(GetPoolRootDirectory(), PoolSizeFileName));
                if (!File.Exists(sizeFile)) return 0;
                long v;
                return long.TryParse(File.ReadAllText(sizeFile).Trim(), out v) ? v : 0;
            }
            catch { return 0; }
        }

        private void WritePoolSizeCache(long bytes)
        {
            try
            {
                string pool = GetPoolRootDirectory();
                if (!Directory.Exists(pool)) return;
                File.WriteAllText(FileOperationService.EnsureLongPath(Path.Combine(pool, PoolSizeFileName)),
                    bytes.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] poolsize キャッシュ書込失敗 (無害): " + ex.Message); }
        }

        /// <summary>pool 内の blob 実体合計 (メタ `.poolsize` と進行中 `.tmp_` は除外、レビュー L4)。バックグラウンドスレッド用。</summary>
        private long EnumeratePoolBytes()
        {
            string pool = GetPoolRootDirectory();
            if (!Directory.Exists(pool)) return 0;
            long sum = 0;
            foreach (var f in Directory.EnumerateFiles(pool, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (name == PoolSizeFileName || name.Contains(".tmp_")) continue;
                sum += SafeLen(f);
            }
            return sum;
        }

        // ---- 内部 ----

        private bool IsEnabled()
        {
            string v = _settingsRepo.GetString(SettingsKeys.AssetSnapshotEnabled, "true");
            return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class CacheEntry { public long Size; public long MtimeTicks; public string Hash; }

        /// <summary>直近 manifest を読み「relpath → (size, mtime, hash)」のキャッシュを作る。SMB で不変ファイルを再ハッシュしないため。</summary>
        private Dictionary<string, CacheEntry> LoadHashCache()
        {
            var cache = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
            try
            {
                var newest = EnumerateManifests().OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (newest == null) return cache;
                foreach (var line in File.ReadLines(newest))
                {
                    if (line.StartsWith(MetaLinePrefix + "\t")) continue;
                    var f = line.Split(new[] { '\t' }, 4);
                    if (f.Length < 4) continue;
                    long size, mt;
                    if (!long.TryParse(f[1], out size) || !long.TryParse(f[2], out mt)) continue;
                    cache[f[3]] = new CacheEntry { Hash = f[0], Size = size, MtimeTicks = mt };
                }
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] ハッシュキャッシュ読込失敗 (全ハッシュし直す): " + ex.Message); }
            return cache;
        }

        private void WalkTree(string srcDir, string relPrefix, string poolRoot,
            Dictionary<string, CacheEntry> cache, List<string> entries, Stats stats,
            IProgress<ProgressInfo> progress, CancellationToken token, int total)
        {
            token.ThrowIfCancellationRequested();
            // (レビュー#3) 列挙対象 dir も \\?\ で長パス対応にする (per-file だけでなく enumerate も)。EnsureLongPath は
            // 240 字未満だと \\?\ を付けないため、短い srcDir 配下に MAX_PATH 超のファイルがあると GetFiles 自体が
            // PathTooLong を投げ世代まるごと Failed になる。ForceLong で常に長パス列挙にし、失敗時もそのフォルダだけ
            // スキップ (best-effort、世代全体は落とさない)。
            string[] files;
            try { files = Directory.GetFiles(ForceLong(srcDir)); }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] ファイル列挙に失敗 (このフォルダをスキップ): " + srcDir + " : " + ex.Message); files = new string[0]; }
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                string safe = FileOperationService.EnsureLongPath(file);
                // (レビュー L3) ファイルの symlink/junction も辿らない (dir と同扱い、spec 整合)。リンク先を
                // ハッシュ/コピーすると意図せぬ実体を取り込むため。
                try { if ((File.GetAttributes(safe) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) { Logger.Warn("[AssetSnapshot] reparse file をスキップ: " + file); continue; } }
                catch { }
                string relpath = relPrefix + "/" + Path.GetFileName(file);
                long size = SafeLen(file);
                long mtime = File.GetLastWriteTimeUtc(safe).Ticks;

                // キャッシュ命中 (relpath+size+mtime 一致) かつ pool に実体があれば再ハッシュ・再読込しない。
                string hash;
                CacheEntry c;
                if (cache.TryGetValue(relpath, out c) && c.Size == size && c.MtimeTicks == mtime
                    && File.Exists(FileOperationService.EnsureLongPath(PoolPathFor(poolRoot, c.Hash))))
                {
                    hash = c.Hash;
                }
                else
                {
                    // (レビュー#4) ソースを 1 回だけ読み、ハッシュ計算と pool 配置を同時に行う (SMB の二重読込を回避)。
                    if (HashAndStore(safe, poolRoot, token, out hash)) stats.NewBytes += size;
                }

                entries.Add(hash + "\t" + size.ToString(CultureInfo.InvariantCulture) + "\t"
                    + mtime.ToString(CultureInfo.InvariantCulture) + "\t" + relpath);
                stats.FileCount++;
                stats.Bytes += size;
                if (total > 0 && progress != null)
                {
                    int pct = (int)((double)stats.FileCount / total * 100);
                    progress.Report(new ProgressInfo(pct > 100 ? 100 : pct, "アセットを控え中...", Path.GetFileName(file)));
                }
            }
            string[] dirs;
            try { dirs = Directory.GetDirectories(ForceLong(srcDir)); }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] サブフォルダ列挙に失敗 (スキップ): " + srcDir + " : " + ex.Message); dirs = new string[0]; }
            foreach (string subDir in dirs)
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attr;
                try { attr = File.GetAttributes(FileOperationService.EnsureLongPath(subDir)); } catch { continue; }
                if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    Logger.Warn("[AssetSnapshot] reparse point をスキップ: " + subDir);
                    continue;
                }
                WalkTree(subDir, relPrefix + "/" + Path.GetFileName(subDir), poolRoot, cache, entries, stats, progress, token, total);
            }
        }

        /// <summary>常に \\?\ を付けて長パス対応の列挙にする (EnsureLongPath は 240 字未満だと付けないため)。</summary>
        private static string ForceLong(string path)
            => path.StartsWith(@"\\?\") ? path : @"\\?\" + Path.GetFullPath(path);

        private static string PoolPathFor(string poolRoot, string hash)
            => Path.Combine(poolRoot, hash.Substring(0, 2), hash);

        /// <summary>
        /// (レビュー#4) ソースを 1 回だけ読み、SHA-256 を計算しつつ pool の temp に書き、内容ハッシュ名へ rename する。
        /// 既に同一中身が pool にあれば temp を捨てる (重複排除)。新規配置したら true (= NewBytes 計上)。チャンク読みで
        /// token を観測しキャンセルに反応できる。
        /// (レビュー#1) 配置直後に mtime を**配置時刻**に打つ。pool blob は content-addressed で mtime は内容と無関係。
        /// File.Copy だと元ファイルの古い mtime を継承し、GC の grace (直近書込保護) が常に無効化される (= 多ホスト並行
        /// backup で他ホストの取得中 blob を誤って GC しうる) ため、配置時刻を刻んで grace を機能させる。
        /// </summary>
        private bool HashAndStore(string safeSrc, string poolRoot, CancellationToken token, out string hash)
        {
            string tmp = Path.Combine(poolRoot, ".tmp_" + Guid.NewGuid().ToString("N"));
            string safeTmp = FileOperationService.EnsureLongPath(tmp);
            Directory.CreateDirectory(FileOperationService.EnsureLongPath(poolRoot));
            try
            {
                using (var sha = SHA256.Create())
                using (var src = new FileStream(safeSrc, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                using (var dst = new FileStream(safeTmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    byte[] buf = new byte[1 << 20];
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        sha.TransformBlock(buf, 0, n, null, 0);
                        dst.Write(buf, 0, n);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    var sb = new StringBuilder(sha.Hash.Length * 2);
                    foreach (var b in sha.Hash) sb.Append(b.ToString("x2"));
                    hash = sb.ToString();
                }

                string final = PoolPathFor(poolRoot, hash);
                string safeFinal = FileOperationService.EnsureLongPath(final);
                if (File.Exists(safeFinal)) { TryDeleteFile(tmp); return false; } // 既存 = 重複排除
                Directory.CreateDirectory(FileOperationService.EnsureLongPath(Path.GetDirectoryName(final)));
                try { File.Move(safeTmp, safeFinal); }
                catch (IOException) { TryDeleteFile(tmp); if (File.Exists(safeFinal)) return false; throw; }
                try { File.SetLastWriteTimeUtc(safeFinal, DateTime.UtcNow); } catch { } // grace 用に配置時刻を刻む
                return true;
            }
            catch
            {
                TryDeleteFile(tmp);
                throw;
            }
        }

        private static string ResolveUniqueManifest(string dir, string leaf)
        {
            string candidate = Path.Combine(dir, leaf + ManifestExt);
            int suffix = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(dir, leaf + "_" + suffix + ManifestExt);
                if (++suffix > 99) throw new Exception("manifest 名の衝突回避に失敗 (同 1 秒に 100 件以上): " + dir);
            }
            return candidate;
        }

        private static void WriteManifest(string path, string timestamp, string host, string trigger, Stats stats, List<string> entries)
        {
            var sb = new StringBuilder();
            sb.Append(MetaLinePrefix).Append('\t').Append(timestamp).Append('\t').Append(host).Append('\t')
              .Append(trigger).Append('\t').Append(stats.FileCount).Append('\t').Append(stats.Bytes).Append('\n');
            foreach (var e in entries) sb.Append(e).Append('\n');
            File.WriteAllText(FileOperationService.EnsureLongPath(path), sb.ToString(), new UTF8Encoding(false));
        }

        private IEnumerable<string> EnumerateManifests()
        {
            string root = GetSnapshotRootDirectory();
            foreach (var trigger in new[] { "auto", "manual" })
            {
                string dir = Path.Combine(root, trigger);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*" + ManifestExt)) yield return f;
            }
        }

        private static AssetSnapshotInfo ReadManifestHeader(string manifestPath)
        {
            var info = new AssetSnapshotInfo { ManifestPath = manifestPath };
            using (var r = new StreamReader(FileOperationService.EnsureLongPath(manifestPath)))
            {
                string first = r.ReadLine();
                if (first != null && first.StartsWith(MetaLinePrefix + "\t"))
                {
                    var f = first.Split('\t');
                    if (f.Length >= 6)
                    {
                        info.Timestamp = f[1];
                        info.Host = f[2];
                        info.TriggerType = f[3];
                        int fc; int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out fc); info.FileCount = fc;
                        long lb; long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out lb); info.LogicalBytes = lb;
                    }
                }
            }
            DateTime local;
            info.StartedAtLocal = (info.Timestamp != null && DateTime.TryParseExact(info.Timestamp, "yyyyMMdd_HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out local)) ? local : DateTime.MinValue;
            return info;
        }

        /// <summary>(レビュー L2) 全体を try/catch で best-effort 化し、retention/GC の例外で「manifest 書込済なのに
        /// 控え全体が Failed」と誤報告されるのを防ぐ。(レビュー M3) **GC は auto に限定**: auto は lease で多ホスト排他
        /// されるため並行 GC が起きない。manual manifest は retention 対象外で未参照 blob を生まないので GC 不要、
        /// サイズキャッシュだけ更新する。</summary>
        private void ApplyRetentionAndGc(string triggerType)
        {
            try
            {
                if (string.Equals(triggerType, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    int count = _settingsRepo.GetInt32(SettingsKeys.AssetSnapshotRetentionCount, SettingsKeys.DefaultAssetSnapshotRetentionCount);
                    if (count > 0)
                    {
                        string autoDir = Path.Combine(GetSnapshotRootDirectory(), "auto");
                        if (Directory.Exists(autoDir))
                        {
                            var stale = Directory.GetFiles(autoDir, "*" + ManifestExt)
                                .OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Skip(count).ToList();
                            foreach (var m in stale)
                            {
                                try { File.Delete(FileOperationService.EnsureLongPath(m)); Logger.Info("[AssetSnapshot] 古い世代(manifest)を削除: " + Path.GetFileName(m)); }
                                catch (Exception ex) { Logger.Warn("[AssetSnapshot] manifest 削除失敗: " + m + " : " + ex.Message); }
                            }
                        }
                    }
                    GarbageCollectPool(); // GC + .poolsize 更新
                }
                else
                {
                    WritePoolSizeCache(EnumeratePoolBytes()); // manual: GC せずサイズキャッシュのみ更新
                }
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] retention/GC 失敗 (無害、控え自体は成功): " + ex.Message); }
        }

        /// <summary>残る全 manifest が参照する hash 集合に無い pool ファイルを削除 (直近書込は grace で残す)。</summary>
        private void GarbageCollectPool()
        {
            try
            {
                string pool = GetPoolRootDirectory();
                if (!Directory.Exists(pool)) return;
                var referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (var manifest in EnumerateManifests())
                {
                    try
                    {
                        foreach (var line in File.ReadLines(manifest))
                        {
                            if (line.StartsWith(MetaLinePrefix + "\t")) continue;
                            int tab = line.IndexOf('\t');
                            if (tab > 0) referenced.Add(line.Substring(0, tab));
                        }
                    }
                    catch (Exception ex) { Logger.Warn("[AssetSnapshot] GC: manifest 読込失敗のためこの世代の参照は保守的に維持できず: " + manifest + " : " + ex.Message); return; }
                }
                DateTime cutoff = DateTime.UtcNow - GcGracePeriod;
                int removed = 0; long freed = 0; long surviving = 0;
                foreach (var f in Directory.EnumerateFiles(pool, "*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(f);
                    if (name == PoolSizeFileName) continue; // メタファイルは触らない・サイズにも数えない
                    bool isTmp = name.Contains(".tmp_");
                    long len = SafeLen(f);
                    // 参照されている blob は残す。進行中 temp は触らない。grace 超過の未参照/orphan-temp は削除。
                    if (!isTmp && referenced.Contains(name)) { surviving += len; continue; }
                    try
                    {
                        if (File.GetLastWriteTimeUtc(f) > cutoff) { if (!isTmp) surviving += len; continue; } // grace: 直近書込は残す
                        File.Delete(FileOperationService.EnsureLongPath(f));
                        removed++; freed += len;
                    }
                    catch (Exception ex) { Logger.Warn("[AssetSnapshot] GC: pool 削除失敗: " + f + " : " + ex.Message); if (!isTmp) surviving += len; }
                }
                if (removed > 0) Logger.Info(string.Format("[AssetSnapshot] GC: 未参照 {0} 件 / {1:F2}MB を解放", removed, freed / 1048576.0));
                WritePoolSizeCache(surviving); // (レビュー M1) UI 即時読み用にサイズを記録
            }
            catch (Exception ex) { Logger.Warn("[AssetSnapshot] GC 失敗 (無害、次回再試行): " + ex.Message); }
        }

        private static int SafeCountFiles(string dir)
        {
            try { return Directory.Exists(dir) ? FileOperationService.CountFiles(dir) : 0; }
            catch { return 0; }
        }

        private static long SafeLen(string path)
        {
            try { return new FileInfo(FileOperationService.EnsureLongPath(path)).Length; } catch { return 0; }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(FileOperationService.EnsureLongPath(path))) File.Delete(FileOperationService.EnsureLongPath(path)); }
            catch { }
        }

        private sealed class Stats
        {
            public int FileCount;
            public long Bytes;     // 論理合計
            public long NewBytes;  // 今回プールへ新規コピーした分
        }
    }
}
