using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using TonePrism.Manager;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#250 PR1) `AssetSnapshotService`（共有プール / SHA-256）の検証。一時 DB + 一時 games/guide +
    /// 既定 backup_dest (`&lt;tmp&gt;/backups`) で純単体実行。
    /// </summary>
    public class AssetSnapshotServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _dbPath;
        private readonly string _games;
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settings;
        private readonly BackupService _backup;
        private readonly AssetSnapshotService _svc;

        public AssetSnapshotServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "tp_snap_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _dbPath = Path.Combine(_root, "toneprism.db");
            _games = Path.Combine(_root, "games");
            _conn = new DatabaseConnection(_dbPath);
            new SchemaManager(_conn).InitializeDatabase();
            _settings = new SettingsRepository(_conn);
            _backup = new BackupService(_conn, _settings);
            _svc = new AssetSnapshotService(_conn, _settings, _backup);
            _svc.GcGracePeriod = TimeSpan.Zero; // テストでは grace を無効化して GC を即時検証
        }

        public void Dispose()
        {
            try { SQLiteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        // ---- helpers ----
        private void WriteGameFile(string rel, string content)
        {
            string p = Path.Combine(_games, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, content);
        }

        private SnapshotResult Snap(string trigger, string ts)
            => _svc.CreateSnapshot(ts, trigger, null, default(System.Threading.CancellationToken));

        private string PoolDir => Path.Combine(_backup.GetEffectiveDestinationDirectory(), "asset_pool");
        private int PoolBlobCount()
            => Directory.Exists(PoolDir)
                ? Directory.GetFiles(PoolDir, "*", SearchOption.AllDirectories).Count(f => !Path.GetFileName(f).Contains(".tmp_"))
                : 0;
        private string ManifestDir(string trigger) => Path.Combine(_backup.GetEffectiveDestinationDirectory(), "asset_snapshots", trigger);
        private int ManifestCount(string trigger)
            => Directory.Exists(ManifestDir(trigger)) ? Directory.GetFiles(ManifestDir(trigger), "*.manifest").Length : 0;

        // ---- tests ----

        [Fact]
        public void FirstSnapshot_CopiesUniqueContentToPool()
        {
            WriteGameFile("g1/a.txt", "alpha");
            WriteGameFile("g1/b.txt", "beta");
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsSuccess);
            Assert.Equal(2, r.FileCount);
            Assert.Equal(2, PoolBlobCount());           // 2 つの異なる中身
            Assert.True(r.NewBytesCopied > 0);
            Assert.Equal(1, ManifestCount("auto"));
        }

        [Fact]
        public void SameContent_DifferentPaths_StoredOnce()
        {
            // 別ゲーム・別名でも中身が同じなら pool には 1 個 (CAS の肝)
            WriteGameFile("g1/data.bin", "SHARED-RUNTIME");
            WriteGameFile("g2/other-name.bin", "SHARED-RUNTIME");
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsSuccess);
            Assert.Equal(2, r.FileCount);               // 目録は 2 エントリ
            Assert.Equal(1, PoolBlobCount());           // 実体は 1 個に集約
        }

        [Fact]
        public void DifferentContent_SameName_StoredSeparately()
        {
            // 名前が同じでも中身が違えば必ず別保存 (ユーザー懸念の核心)
            WriteGameFile("g1/data.dat", "CONTENT-A");
            WriteGameFile("g2/data.dat", "CONTENT-B-different");
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsSuccess);
            Assert.Equal(2, PoolBlobCount());           // 中身が違う → 2 個
        }

        [Fact]
        public void SecondSnapshot_Unchanged_AddsNoNewBytes()
        {
            WriteGameFile("g1/a.txt", "alpha");
            var r1 = Snap("auto", "20260101_000001");
            int poolAfter1 = PoolBlobCount();
            var r2 = Snap("auto", "20260101_000002"); // 不変
            Assert.True(r2.IsSuccess);
            Assert.Equal(0, r2.NewBytesCopied);         // 既に pool にあるので新規コピー無し
            Assert.Equal(poolAfter1, PoolBlobCount());  // pool は増えない
            Assert.Equal(2, ManifestCount("auto"));     // 目録は 2 世代
        }

        [Fact]
        public void ChangedContent_AddsNewBlob()
        {
            WriteGameFile("g1/a.txt", "old");
            Snap("auto", "20260101_000001");
            WriteGameFile("g1/a.txt", "new-and-different"); // 中身変更 (mtime も変わる)
            var r2 = Snap("auto", "20260101_000002");
            Assert.True(r2.IsSuccess);
            Assert.True(r2.NewBytesCopied > 0);
            Assert.Equal(2, PoolBlobCount());           // 旧 + 新 の 2 個 (旧は retention 内なので残る)
        }

        [Fact]
        public void Retention_PrunesOldManifests_AndGcRemovesUnreferenced()
        {
            _settings.SetInt32(SettingsKeys.AssetSnapshotRetentionCount, 2);
            // 各世代で中身を変える → 古い blob が未参照になる
            WriteGameFile("g1/a.txt", "v1"); Snap("auto", "20260101_000001");
            WriteGameFile("g1/a.txt", "v2"); Snap("auto", "20260101_000002");
            WriteGameFile("g1/a.txt", "v3"); Snap("auto", "20260101_000003");
            Assert.Equal(2, ManifestCount("auto"));     // 最古 manifest が削除され 2 世代
            Assert.Equal(2, PoolBlobCount());           // v1 の blob が GC され v2/v3 の 2 個
        }

        [Fact]
        public void Retention_KeepsManualManifests()
        {
            _settings.SetInt32(SettingsKeys.AssetSnapshotRetentionCount, 1);
            WriteGameFile("g1/a.txt", "x");
            Snap("manual", "20260101_000001");
            Snap("manual", "20260101_000002");
            Assert.Equal(2, ManifestCount("manual"));   // manual は retention 対象外
        }

        [Fact]
        public void Disabled_ReturnsSkipped()
        {
            _settings.SetString(SettingsKeys.AssetSnapshotEnabled, "false");
            WriteGameFile("g1/a.txt", "x");
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsSkipped);
            Assert.Equal(0, PoolBlobCount());
        }

        [Fact]
        public void EmptyGamesAndGuide_Succeeds()
        {
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsSuccess);
            Assert.Equal(0, r.FileCount);
        }

        [Fact]
        public void Failure_DoesNotThrow_ReturnsFailed()
        {
            // asset_pool をファイルにして pool ディレクトリ作成を失敗させる
            string dest = _backup.GetEffectiveDestinationDirectory();
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "asset_pool"), "blocker");
            WriteGameFile("g1/a.txt", "x");
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsFailed); // throw せず Failed
        }
    }
}
