using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#250 PR3) アセット復元エンジン。manifest (relpath→hash) を読み、共有プール (asset_pool/&lt;hash&gt;) の実体を
    /// live の games/+guide/ へ書き戻して、ツリーを manifest と完全一致させる (= <see cref="AssetSnapshotService"/>.
    /// CreateSnapshot の逆操作)。
    ///
    /// **reconcile-in-place**: live が既に一致するファイル (size+mtime 一致) は再コピーせず skip し (SMB 再読込回避)、
    /// 違う/欠落だけ pool から copy、manifest に無い余剰 live は削除する。**コピーを削除より先**に行い、pool は内容
    /// アドレスで不変なので途中中断でも「置換前に live を消さない」を担保する。
    ///
    /// **best-effort**: per-file 失敗 (pool blob 不在・I/O・不正 relpath) は throw せず <see cref="AssetRestoreResult"/> に
    /// 集計し、live を壊さない。全体失敗 (manifest 不在/読めない・空ガード発動) のみ Failed を返す。
    ///
    /// **安全**: relpath は games//guide/ 配下に限定 (パストラバーサル拒否で install dir 外への書込/削除を防ぐ)、
    /// reparse point / .pending-delete-* は削除対象から除外 (外部実体・ゲーム削除 retry を壊さない、snapshot 走査と対称)。
    ///
    /// フォーマット (manifest 行 / pool パス) の解釈は <see cref="AssetSnapshotService"/> の internal を共用 (SoT 一本化)。
    /// </summary>
    public class AssetRestoreService
    {
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settingsRepo;
        private readonly BackupService _backupService;

        private static readonly string[] SubFolders = { "games", "guide" };

        public AssetRestoreService(DatabaseConnection conn, SettingsRepository settingsRepo, BackupService backupService)
        {
            _conn = conn;
            _settingsRepo = settingsRepo;
            _backupService = backupService;
        }

        private string GetPoolRootDirectory() => Path.Combine(_backupService.GetEffectiveDestinationDirectory(), "asset_pool");

        /// <summary>
        /// live の games/+guide/ を <paramref name="manifestPath"/> の内容と完全一致させる。実体は pool から取得。
        /// per-file は never throw。manifest 不在/読めない・空ガード発動は <see cref="AssetRestoreResult"/>.Failed。
        /// </summary>
        /// <param name="allowEmpty">エントリ 0 件の manifest で非空 live を空にすることを許す (既定 false=暴発防止ガード)。</param>
        /// <param name="preferContentFromDir">(PR3b 予約・PR3a 未使用) rename-retreat の退避 dir。指定時は不変ファイルを
        /// pool でなくここから copy して SMB 再読込を減らす最適化用。PR3a では常に pool から copy する。</param>
        public AssetRestoreResult RestoreFromManifest(
            string manifestPath,
            IProgress<ProgressInfo> progress = null,
            CancellationToken token = default(CancellationToken),
            bool allowEmpty = false,
            string preferContentFromDir = null)
        {
            try
            {
                string baseInstallDir = Path.GetDirectoryName(_conn.DbPath);
                string poolRoot = GetPoolRootDirectory();

                if (string.IsNullOrEmpty(manifestPath) || !File.Exists(FileOperationService.EnsureLongPath(manifestPath)))
                    return AssetRestoreResult.Failed("復元元の目録 (manifest) が見つかりません: " + (manifestPath ?? "(null)"));
                token.ThrowIfCancellationRequested();

                // 1. manifest を解析。wantedRel = 残すべき relpath 集合 (case-insensitive)。不正 relpath は拒否してカウント。
                var entries = new List<AssetSnapshotService.ManifestEntry>();
                var wantedRel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int failed = 0;
                try
                {
                    foreach (var line in File.ReadLines(FileOperationService.EnsureLongPath(manifestPath)))
                    {
                        if (!AssetSnapshotService.TryParseManifestEntryLine(line, out AssetSnapshotService.ManifestEntry e)) continue;
                        if (!IsRelPathSafe(e.RelPath))
                        {
                            failed++;
                            Logger.Warn("[AssetRestore] 不正な relpath を拒否 (games//guide/ 外への書込防止): " + e.RelPath);
                            continue;
                        }
                        entries.Add(e);
                        wantedRel.Add(e.RelPath);
                    }
                }
                catch (Exception ex)
                {
                    return AssetRestoreResult.Failed("目録の読込に失敗しました: " + ex.Message);
                }
                token.ThrowIfCancellationRequested();

                // 2. 空ガード: 有効エントリ 0 件で非空 live を空にしようとしたら中止 (空 manifest 暴発で games/ 全消去を防ぐ)。
                if (entries.Count == 0 && !allowEmpty && LiveTreeHasAnyFile(baseInstallDir))
                    return AssetRestoreResult.Failed("目録が空です。ゲームファイルを全消去する復元を避けるため中止しました。");

                int copied = 0, skipped = 0, deleted = 0;
                var missingBlobs = new List<string>();
                int total = entries.Count, done = 0;

                // 3. 各エントリを materialize (copy-if-changed)。コピーは削除より先 (live を消す前に置換を揃える)。
                foreach (var e in entries)
                {
                    token.ThrowIfCancellationRequested();
                    string liveFull = Path.Combine(baseInstallDir, e.RelPath.Replace('/', Path.DirectorySeparatorChar));
                    string safeLive = FileOperationService.EnsureLongPath(liveFull);
                    try
                    {
                        if (FileExistsAndMatches(safeLive, e.Size, e.MtimeTicks))
                        {
                            skipped++;
                        }
                        else
                        {
                            string blob = FileOperationService.EnsureLongPath(AssetSnapshotService.PoolPathFor(poolRoot, e.Hash));
                            if (!File.Exists(blob))
                            {
                                failed++;
                                missingBlobs.Add(e.RelPath);
                                Logger.Error("[AssetRestore] pool に実体が無く復元できません (live は保持): " + e.RelPath + " (hash=" + e.Hash + ")");
                            }
                            else
                            {
                                string parent = Path.GetDirectoryName(liveFull);
                                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(FileOperationService.EnsureLongPath(parent));
                                File.Copy(blob, safeLive, true);
                                // 復元後 mtime を manifest 値に刻む (= 次回 snapshot の cache hit + 再復元時の skip を成立させる。
                                // HashAndStore の配置時刻トリックの逆)。失敗しても復元自体は成功扱い。
                                try { File.SetLastWriteTimeUtc(safeLive, new DateTime(e.MtimeTicks, DateTimeKind.Utc)); }
                                catch (Exception ex) { Logger.Warn("[AssetRestore] 復元ファイルの mtime スタンプに失敗 (次回再ハッシュの恐れ): " + e.RelPath + " : " + ex.Message); }
                                copied++;
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failed++;
                        Logger.Warn("[AssetRestore] ファイル復元に失敗 (skip): " + e.RelPath + " : " + ex.Message);
                    }
                    done++;
                    if (progress != null && total > 0)
                    {
                        int pct = (int)((double)done / total * 100);
                        progress.Report(new ProgressInfo(pct > 100 ? 100 : pct, "ゲームファイルを復元中...", e.RelPath));
                    }
                }

                // 4. 余剰削除: manifest に無い live ファイルを消してツリーを完全一致させる。
                foreach (var sub in SubFolders)
                {
                    string subRoot = Path.Combine(baseInstallDir, sub);
                    if (!Directory.Exists(FileOperationService.EnsureLongPath(subRoot))) continue;
                    foreach (var liveFile in EnumerateLiveFiles(subRoot))
                    {
                        token.ThrowIfCancellationRequested();
                        string rel = sub + "/" + RelativeUnder(subRoot, liveFile);
                        if (wantedRel.Contains(rel)) continue;
                        try
                        {
                            File.Delete(FileOperationService.EnsureLongPath(liveFile));
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Logger.Warn("[AssetRestore] 余剰ファイルの削除に失敗 (skip): " + liveFile + " : " + ex.Message);
                        }
                    }
                }

                // 5. 空 dir を best-effort 掃除 (games//guide/ root 自体は残す)。
                foreach (var sub in SubFolders)
                    PruneEmptyDirs(Path.Combine(baseInstallDir, sub));

                Logger.Info(string.Format("[AssetRestore] 復元完了: copied={0} skipped={1} deleted={2} failed={3} (manifest={4})",
                    copied, skipped, deleted, failed, Path.GetFileName(manifestPath)));
                return AssetRestoreResult.Success(copied, skipped, deleted, failed, missingBlobs);
            }
            catch (OperationCanceledException)
            {
                Logger.Info("[AssetRestore] 復元がキャンセルされました (ツリーは途中状態)");
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("[AssetRestore] 復元に失敗しました", ex);
                return AssetRestoreResult.Failed(ex.Message);
            }
        }

        // ---- helpers ----

        /// <summary>relpath を games//guide/ 配下のみに限定し、install dir 外への書込/削除を防ぐ。
        /// 拒否条件: 先頭 segment が games//guide/ でない / `..` segment / rooted / `:` 含む (ドライブ/ADS)。</summary>
        private static bool IsRelPathSafe(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            if (rel.IndexOf(':') >= 0) return false;
            string norm = rel.Replace('\\', '/');
            if (norm.StartsWith("/")) return false;
            if (Path.IsPathRooted(norm)) return false;
            var segs = norm.Split('/');
            if (segs.Length < 2) return false; // 少なくとも "games/<x>" の 2 segment
            if (!string.Equals(segs[0], "games", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(segs[0], "guide", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var seg in segs)
                if (seg == ".." || seg.Length == 0) return false;
            return true;
        }

        private static bool FileExistsAndMatches(string safeLive, long size, long mtimeTicks)
        {
            try
            {
                if (!File.Exists(safeLive)) return false;
                return new FileInfo(safeLive).Length == size && File.GetLastWriteTimeUtc(safeLive).Ticks == mtimeTicks;
            }
            catch { return false; }
        }

        private static bool LiveTreeHasAnyFile(string baseInstallDir)
        {
            foreach (var sub in SubFolders)
            {
                string subRoot = Path.Combine(baseInstallDir, sub);
                if (Directory.Exists(FileOperationService.EnsureLongPath(subRoot)) && EnumerateLiveFiles(subRoot).Any())
                    return true;
            }
            return false;
        }

        /// <summary>games//guide/ 配下の全ファイルを再帰列挙 (正規化済 full path)。reparse point dir と .pending-delete-*
        /// dir は降りない。yield を try/catch の外に出すため列挙は <see cref="SafeGetFiles"/>/<see cref="SafeGetDirs"/> 経由。</summary>
        private static IEnumerable<string> EnumerateLiveFiles(string root)
        {
            foreach (var f in SafeGetFiles(root)) yield return f;
            foreach (var d in SafeGetDirs(root))
            {
                string name = Path.GetFileName(d);
                if (name != null && name.IndexOf(".pending-delete-", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (IsReparsePoint(d)) continue;
                foreach (var sub in EnumerateLiveFiles(d)) yield return sub;
            }
        }

        private static string[] SafeGetFiles(string root)
        {
            try { return Directory.GetFiles(FileOperationService.ForceLongPath(root)).Select(FileOperationService.NormalizePath).ToArray(); }
            catch (Exception ex) { Logger.Warn("[AssetRestore] ファイル列挙に失敗 (skip): " + root + " : " + ex.Message); return new string[0]; }
        }

        private static string[] SafeGetDirs(string root)
        {
            try { return Directory.GetDirectories(FileOperationService.ForceLongPath(root)).Select(FileOperationService.NormalizePath).ToArray(); }
            catch { return new string[0]; }
        }

        private static bool IsReparsePoint(string dir)
        {
            try { return (File.GetAttributes(FileOperationService.EnsureLongPath(dir)) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint; }
            catch { return false; }
        }

        /// <summary>root 直下からの相対パスを forward-slash で返す。</summary>
        private static string RelativeUnder(string root, string fullFile)
        {
            string nr = FileOperationService.NormalizePath(root).TrimEnd('\\', '/');
            string nf = FileOperationService.NormalizePath(fullFile);
            string rel = (nf.Length > nr.Length && nf.StartsWith(nr, StringComparison.OrdinalIgnoreCase))
                ? nf.Substring(nr.Length).TrimStart('\\', '/')
                : Path.GetFileName(nf);
            return rel.Replace('\\', '/');
        }

        /// <summary>配下の空 dir を bottom-up で best-effort 削除 (引数 dir 自体は残す)。</summary>
        private static void PruneEmptyDirs(string dir)
        {
            try
            {
                if (!Directory.Exists(FileOperationService.EnsureLongPath(dir))) return;
                foreach (var d in SafeGetDirs(dir))
                {
                    PruneEmptyDirs(d);
                    try
                    {
                        if (Directory.GetFileSystemEntries(FileOperationService.ForceLongPath(d)).Length == 0)
                            Directory.Delete(FileOperationService.EnsureLongPath(d));
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
