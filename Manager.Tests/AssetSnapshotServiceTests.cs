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
    /// (#250 PR1) `AssetSnapshotService` の検証。%TEMP% が NTFS 前提 (ハードリンク共有の確認に必要)。
    /// 一時 DB + 一時 games/guide + 既定 backup_dest (`<tmp>/backups`) で純単体実行。
    /// </summary>
    public class AssetSnapshotServiceTests : IDisposable
    {
        private readonly string _root;       // <tmp>/tp_snap_xxx
        private readonly string _dbPath;     // <root>/toneprism.db
        private readonly string _games;      // <root>/games
        private readonly string _guide;      // <root>/guide
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
            _guide = Path.Combine(_root, "guide");

            _conn = new DatabaseConnection(_dbPath);
            new SchemaManager(_conn).InitializeDatabase(); // settings テーブル等を作る
            _settings = new SettingsRepository(_conn);
            _backup = new BackupService(_conn, _settings);
            _svc = new AssetSnapshotService(_conn, _settings, _backup);
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

        private SnapshotResult Snap(string trigger, string ts, bool? capable)
            => _svc.CreateSnapshot(ts, trigger, null, default(System.Threading.CancellationToken), capable);

        private string SnapFile(SnapshotResult r, string rel)
            => Path.Combine(r.DirectoryPath, "games", rel.Replace('/', Path.DirectorySeparatorChar));

        private string AutoDir => Path.Combine(_svc.GetSnapshotRootDirectory(), "auto");

        // ---- tests ----

        [Fact]
        public void FirstSnapshot_NoBase_CopiesAllContents()
        {
            WriteGameFile("g1/exe.txt", "hello");
            var r = Snap("auto", "20260101_000001", true);
            Assert.True(r.IsSuccess);
            Assert.Equal(1, r.FileCount);
            Assert.Equal("hello", File.ReadAllText(SnapFile(r, "g1/exe.txt")));
        }

        [Fact]
        public void SecondSnapshot_Unchanged_SharesViaHardLink()
        {
            WriteGameFile("g1/exe.txt", "data");
            var r1 = Snap("auto", "20260101_000001", true);
            var r2 = Snap("auto", "20260101_000002", true); // 不変
            Assert.True(r1.IsSuccess && r2.IsSuccess);
            Assert.True(HardLinkSupport.AreSameFile(SnapFile(r1, "g1/exe.txt"), SnapFile(r2, "g1/exe.txt")));
        }

        [Fact]
        public void ChangedFile_IsRealCopy_NotLinked()
        {
            WriteGameFile("g1/exe.txt", "old");
            var r1 = Snap("auto", "20260101_000001", true);
            WriteGameFile("g1/exe.txt", "new-and-longer"); // サイズも変わる
            var r2 = Snap("auto", "20260101_000002", true);
            Assert.False(HardLinkSupport.AreSameFile(SnapFile(r1, "g1/exe.txt"), SnapFile(r2, "g1/exe.txt")));
            Assert.Equal("old", File.ReadAllText(SnapFile(r1, "g1/exe.txt"))); // 旧世代は不変
            Assert.Equal("new-and-longer", File.ReadAllText(SnapFile(r2, "g1/exe.txt")));
        }

        [Fact]
        public void Retention_DeletesOldestAutoDirs()
        {
            _settings.SetInt32(SettingsKeys.AssetSnapshotRetentionCount, 2);
            WriteGameFile("g1/exe.txt", "x");
            Snap("auto", "20260101_000001", true);
            Snap("auto", "20260101_000002", true);
            Snap("auto", "20260101_000003", true);
            var dirs = Directory.GetDirectories(AutoDir).Select(Path.GetFileName).Where(n => !n.StartsWith(".")).ToList();
            Assert.Equal(2, dirs.Count);
            Assert.DoesNotContain(dirs, n => n.StartsWith("20260101_000001"));
            Assert.Contains(dirs, n => n.StartsWith("20260101_000003"));
        }

        [Fact]
        public void Retention_KeepsManual()
        {
            _settings.SetInt32(SettingsKeys.AssetSnapshotRetentionCount, 1);
            WriteGameFile("g1/exe.txt", "x");
            Snap("manual", "20260101_000001", true);
            Snap("manual", "20260101_000002", true);
            string manualDir = Path.Combine(_svc.GetSnapshotRootDirectory(), "manual");
            var dirs = Directory.GetDirectories(manualDir).Select(Path.GetFileName).Where(n => !n.StartsWith(".")).ToList();
            Assert.Equal(2, dirs.Count); // manual は retention 対象外
        }

        [Fact]
        public void Disabled_ReturnsSkipped_NoSnapshotDir()
        {
            _settings.SetString(SettingsKeys.AssetSnapshotEnabled, "false");
            WriteGameFile("g1/exe.txt", "x");
            var r = Snap("auto", "20260101_000001", true);
            Assert.True(r.IsSkipped);
            Assert.False(Directory.Exists(_svc.GetSnapshotRootDirectory()));
        }

        [Fact]
        public void EmptyGamesAndGuide_Succeeds()
        {
            // games//guide/ を作らない
            var r = Snap("auto", "20260101_000001", true);
            Assert.True(r.IsSuccess);
            Assert.Equal(0, r.FileCount);
        }

        [Fact]
        public void InterruptedTmp_IsCleaned_AndNotBase()
        {
            WriteGameFile("g1/exe.txt", "data");
            string autoDir = AutoDir;
            Directory.CreateDirectory(autoDir);
            string stale = Path.Combine(autoDir, ".tmp_stale");
            Directory.CreateDirectory(stale);
            Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-31)); // 30 分超
            var r = Snap("auto", "20260101_000001", true);
            Assert.True(r.IsSuccess);
            Assert.False(Directory.Exists(stale)); // 回収された
        }

        [Fact]
        public void SnapshotFailure_DoesNotThrow_ReturnsFailed()
        {
            // asset_snapshots をファイルにして triggerDir 作成を失敗させる
            string dest = _backup.GetEffectiveDestinationDirectory();
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "asset_snapshots"), "blocker");
            WriteGameFile("g1/exe.txt", "x");
            var r = Snap("auto", "20260101_000001", true);
            Assert.True(r.IsFailed); // throw せず Failed
        }

        [Fact]
        public void FallbackWhenNotCapable_AllRealCopy()
        {
            WriteGameFile("g1/exe.txt", "data");
            var r1 = Snap("auto", "20260101_000001", false); // ハードリンク不可を注入
            var r2 = Snap("auto", "20260101_000002", false);
            Assert.True(r1.IsSuccess && r2.IsSuccess);
            // 全実コピー = 別実体
            Assert.False(HardLinkSupport.AreSameFile(SnapFile(r1, "g1/exe.txt"), SnapFile(r2, "g1/exe.txt")));
            Assert.Equal("data", File.ReadAllText(SnapFile(r2, "g1/exe.txt")));
        }
    }
}
