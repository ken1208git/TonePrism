using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using TonePrism.Manager;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#250 PR3a) `AssetRestoreService` の検証。`AssetSnapshotService` で manifest+pool を作り、
    /// `AssetRestoreService` で live games/guide を manifest と一致させる reconcile-in-place を確認する。
    /// </summary>
    public class AssetRestoreServiceTests : IDisposable
    {
        private readonly string _root, _dbPath, _games, _guide;
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settings;
        private readonly BackupService _backup;
        private readonly AssetSnapshotService _snap;
        private readonly AssetRestoreService _restore;

        public AssetRestoreServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "tp_arestore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _dbPath = Path.Combine(_root, "toneprism.db");
            _games = Path.Combine(_root, "games");
            _guide = Path.Combine(_root, "guide");
            _conn = new DatabaseConnection(_dbPath);
            new SchemaManager(_conn).InitializeDatabase();
            _settings = new SettingsRepository(_conn);
            _backup = new BackupService(_conn, _settings);
            _snap = new AssetSnapshotService(_conn, _settings, _backup);
            _snap.GcGracePeriod = TimeSpan.Zero;
            _restore = new AssetRestoreService(_conn, _settings, _backup);
        }

        public void Dispose()
        {
            try { SQLiteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        // ---- helpers ----
        private void WriteGame(string rel, string content) => WriteUnder(_games, rel, content);
        private void WriteGuide(string rel, string content) => WriteUnder(_guide, rel, content);
        private static void WriteUnder(string root, string rel, string content)
        {
            string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, content);
        }
        private string GameFull(string rel) => Path.Combine(_games, rel.Replace('/', Path.DirectorySeparatorChar));
        private string GuideFull(string rel) => Path.Combine(_guide, rel.Replace('/', Path.DirectorySeparatorChar));
        private string PoolDir => Path.Combine(_backup.GetEffectiveDestinationDirectory(), "asset_pool");

        private string Snap(string ts = "20260101_000001")
        {
            var r = _snap.CreateSnapshot(ts, "auto", null, default(CancellationToken));
            Assert.True(r.IsSuccess, "setup snapshot must succeed: " + r.Message);
            return r.ManifestPath;
        }

        // manifest から relpath の hash を引く (`<hash>\t<size>\t<mtime>\t<relpath>`、META 行は f[0]=="META")。
        private static string ManifestHashFor(string manifestPath, string relpath)
        {
            string line = File.ReadLines(manifestPath).FirstOrDefault(l =>
            {
                var f = l.Split('\t');
                return f.Length >= 4 && f[0] != "META" && f[3] == relpath;
            });
            Assert.NotNull(line);
            return line.Split('\t')[0];
        }
        private string PoolBlobPath(string hash) => Path.Combine(PoolDir, hash.Substring(0, 2), hash);

        // ---- tests ----

        [Fact]
        public void RoundTrip_ReproducesTree()
        {
            WriteGame("g1/a.txt", "alpha");
            WriteGame("g1/sub/b.bin", "beta-content");
            WriteGuide("intro.png", "imgdata");
            string manifest = Snap();

            File.WriteAllText(GameFull("g1/a.txt"), "MUTATED"); // 内容変更
            File.Delete(GuideFull("intro.png"));                // 削除

            var r = _restore.RestoreFromManifest(manifest);
            Assert.True(r.IsSuccess);
            Assert.False(r.IsFailed);
            Assert.True(r.CopiedCount > 0);
            Assert.Equal("alpha", File.ReadAllText(GameFull("g1/a.txt")));
            Assert.Equal("beta-content", File.ReadAllText(GameFull("g1/sub/b.bin")));
            Assert.Equal("imgdata", File.ReadAllText(GuideFull("intro.png")));
        }

        [Fact]
        public void Restore_DeletesExtraLiveFiles()
        {
            WriteGame("g1/a.txt", "alpha");
            string manifest = Snap();
            WriteGame("g1/EXTRA.bin", "junk"); // manifest に無い余剰

            var r = _restore.RestoreFromManifest(manifest);
            Assert.True(r.IsSuccess);
            Assert.True(r.DeletedCount >= 1);
            Assert.False(File.Exists(GameFull("g1/EXTRA.bin")));
            Assert.True(File.Exists(GameFull("g1/a.txt")));
        }

        [Fact]
        public void Restore_SkipsUnchanged_NoRecopy()
        {
            WriteGame("g1/a.txt", "alpha");
            WriteGuide("x.png", "img");
            string manifest = Snap();

            var r1 = _restore.RestoreFromManifest(manifest); // live==manifest なので全 skip
            Assert.Equal(0, r1.CopiedCount);
            Assert.True(r1.SkippedCount>= 2);

            var r2 = _restore.RestoreFromManifest(manifest); // 2 回目も全 skip (mtime 据え置き)
            Assert.Equal(0, r2.CopiedCount);
        }

        [Fact]
        public void Restore_RecreatesDeletedFile()
        {
            WriteGame("g1/a.txt", "alpha");
            WriteGame("g1/b.txt", "beta");
            string manifest = Snap();
            File.Delete(GameFull("g1/b.txt"));

            var r = _restore.RestoreFromManifest(manifest);
            Assert.True(r.IsSuccess);
            Assert.True(r.CopiedCount >= 1);
            Assert.Equal("beta", File.ReadAllText(GameFull("g1/b.txt")));
        }

        [Fact]
        public void Restore_MissingPoolBlob_PartialNotCrash_KeepsLive()
        {
            WriteGame("g1/a.txt", "alpha");
            WriteGame("g1/b.txt", "beta");
            string manifest = Snap();

            File.WriteAllText(GameFull("g1/b.txt"), "CHANGED"); // コピー要にする (size/mtime 変化)
            string bHash = ManifestHashFor(manifest, "games/g1/b.txt");
            File.Delete(PoolBlobPath(bHash));                  // b の pool blob を消す

            var r = _restore.RestoreFromManifest(manifest);
            Assert.True(r.IsSuccess);        // クラッシュしない・全体成功
            Assert.True(r.IsPartial);        // per-file 失敗あり
            Assert.True(r.FailedCount >= 1);
            Assert.Contains("games/g1/b.txt", r.MissingBlobRelPaths);
            Assert.True(File.Exists(GameFull("g1/b.txt")));                    // live は保持
            Assert.Equal("CHANGED", File.ReadAllText(GameFull("g1/b.txt")));   // 触っていない
            Assert.Equal("alpha", File.ReadAllText(GameFull("g1/a.txt")));     // 他は復元/skip
        }

        [Fact]
        public void Restore_MissingPoolBlob_DoesNotDeleteLiveFile()
        {
            WriteGame("g1/a.txt", "alpha");
            string manifest = Snap();
            File.WriteAllText(GameFull("g1/a.txt"), "X"); // コピー要
            string h = ManifestHashFor(manifest, "games/g1/a.txt");
            File.Delete(PoolBlobPath(h));

            var r = _restore.RestoreFromManifest(manifest);
            Assert.True(File.Exists(GameFull("g1/a.txt"))); // 保持 (削除しない)
            Assert.True(r.FailedCount >= 1);
            Assert.Equal(0, r.DeletedCount);
        }

        [Fact]
        public void Restore_PathTraversalRelpath_Rejected()
        {
            WriteGame("g1/a.txt", "alpha");
            string manifest = Snap();
            string h = ManifestHashFor(manifest, "games/g1/a.txt");
            File.AppendAllText(manifest, h + "\t5\t" + DateTime.UtcNow.Ticks + "\tgames/../evil.txt\n");

            var r = _restore.RestoreFromManifest(manifest);
            Assert.True(r.FailedCount >= 1);
            Assert.False(File.Exists(Path.Combine(_root, "evil.txt"))); // install dir 外に作られない
        }

        [Fact]
        public void Restore_EmptyManifest_GuardedByDefault_AllowedWithFlag()
        {
            WriteGame("g1/a.txt", "alpha");
            string dir = Path.Combine(_backup.GetEffectiveDestinationDirectory(), "asset_snapshots", "auto");
            Directory.CreateDirectory(dir);
            string manifest = Path.Combine(dir, "20260101_000000.manifest");
            File.WriteAllText(manifest, "META\t20260101_000000\tHOST\tauto\t0\t0\n"); // META のみ (0 エントリ)

            var r = _restore.RestoreFromManifest(manifest);     // 既定: 非空 live をガードで Failed
            Assert.True(r.IsFailed);
            Assert.True(File.Exists(GameFull("g1/a.txt")));

            var r2 = _restore.RestoreFromManifest(manifest, allowEmpty: true); // 明示時は空にする
            Assert.True(r2.IsSuccess);
            Assert.False(File.Exists(GameFull("g1/a.txt")));
        }

        [Fact]
        public void Restore_CaseInsensitive_DoesNotDeleteMatchingFile()
        {
            WriteGame("g1/a.txt", "alpha");
            string manifest = Snap();
            // live を A.TXT に (case 差)
            File.Move(GameFull("g1/a.txt"), GameFull("g1/a.txt") + ".tmp");
            File.Move(GameFull("g1/a.txt") + ".tmp", GameFull("g1/A.TXT"));

            var r = _restore.RestoreFromManifest(manifest);
            Assert.Equal(0, r.DeletedCount); // OrdinalIgnoreCase で一致 → 余剰扱いしない
            Assert.Equal("alpha", File.ReadAllText(GameFull("g1/a.txt"))); // 内容保持 (case-insensitive read)
        }

        [Fact]
        public void Restore_GuideEmptyInManifest_ClearsGuide_KeepsGames()
        {
            WriteGame("g1/a.txt", "alpha");
            WriteGuide("x.png", "img");
            string manifest = Snap();
            // guide エントリを除いた games-only manifest を作る
            var lines = File.ReadAllLines(manifest).Where(l => !l.Contains("\tguide/")).ToArray();
            string gamesOnly = manifest + ".gamesonly";
            File.WriteAllLines(gamesOnly, lines);

            var r = _restore.RestoreFromManifest(gamesOnly);
            Assert.True(r.IsSuccess);
            Assert.False(File.Exists(GuideFull("x.png"))); // guide は消える
            Assert.True(File.Exists(GameFull("g1/a.txt"))); // games は保持
        }

        [Fact]
        public void Restore_PreCancelledToken_Throws()
        {
            WriteGame("g1/a.txt", "alpha");
            string manifest = Snap();
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => _restore.RestoreFromManifest(manifest, null, cts.Token));
        }

        [Fact]
        public void Restore_ManifestNotFound_ReturnsFailed_NoThrow()
        {
            var r = _restore.RestoreFromManifest(Path.Combine(_root, "nope.manifest"));
            Assert.True(r.IsFailed);
        }
    }
}
