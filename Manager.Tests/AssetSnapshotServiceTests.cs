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

        private void WriteGuideFile(string rel, string content)
        {
            string p = Path.Combine(_root, "guide", rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, content);
        }

        private SnapshotResult Snap(string trigger, string ts)
            => _svc.CreateSnapshot(ts, trigger, null, default(System.Threading.CancellationToken));

        private string PoolDir => Path.Combine(_backup.GetEffectiveDestinationDirectory(), "asset_pool");
        // blob は hex ハッシュ名。メタ (.poolsize) / 進行中 (.tmp_) などドット始まりは数えない。
        private int PoolBlobCount()
            => Directory.Exists(PoolDir)
                ? Directory.GetFiles(PoolDir, "*", SearchOption.AllDirectories).Count(f => !Path.GetFileName(f).StartsWith("."))
                : 0;
        private string ManifestDir(string trigger) => Path.Combine(_backup.GetEffectiveDestinationDirectory(), "asset_snapshots", trigger);
        private int ManifestCount(string trigger)
            => Directory.Exists(ManifestDir(trigger)) ? Directory.GetFiles(ManifestDir(trigger), "*.manifest").Length : 0;

        // ---- tests ----

        [Fact]
        public void SnapshotResult_Success_WithSkippedDirs_IsPartial()
        {
            // (round8 C1) 深部フォルダ列挙失敗を skip して完走した Success は IsPartial=true になり、件数を持つ。
            // 深部 I/O 失敗を unit で決定的に再現できないため、部分取得を UI/ログへ伝える契約 (factory) を固定する。
            Assert.False(SnapshotResult.Success("m", 10, 100, 50, 0).IsPartial);   // skip 0 = 完全
            var partial = SnapshotResult.Success("m", 10, 100, 50, 3);
            Assert.True(partial.IsPartial);
            Assert.Equal(3, partial.SkippedDirCount);
            Assert.True(partial.IsSuccess);                                        // 部分でも Success ではある
        }

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
        public void EmptyGamesAndGuide_NoHistory_Succeeds()
        {
            // 履歴が無い (まだ登録の無い install) → 静かに Success(0)
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsSuccess);
            Assert.Equal(0, r.FileCount);
        }

        [Fact]
        public void MissingSources_GamesRegisteredInDb_ReturnsSkipped()
        {
            // (round7 M-2) games/ も guide/ も無いが DB に games 登録済 → 「未登録の新規 install」でなく
            // 「SMB 不達等の異常」と判別し SkippedAnomaly。到達不能共有を unit で再現できないため
            // 「DB に games 有り ∧ フォルダ無し」で代理検証（判別軸 = DB games 件数が効くこと）。
            using (var c = new SQLiteConnection(_conn.ConnectionString))
            {
                c.Open();
                using (var cmd = new SQLiteCommand("INSERT INTO games (game_id, title) VALUES ('g1','T')", c))
                    cmd.ExecuteNonQuery();
            }
            var r = Snap("auto", "20260101_000001"); // games/ も guide/ も作らない
            Assert.True(r.IsSkipped);
            Assert.True(r.IsAnomaly);
        }

        [Fact]
        public void MissingSources_AfterHistory_ReturnsSkipped()
        {
            // (レビュー#1) 以前は控えがあったのに games/ も guide/ も見えない = SMB 不達等の異常。
            // silent Success でなく Skipped で通知する。
            WriteGameFile("g1/a.txt", "x");
            Snap("auto", "20260101_000001");      // manifest 作成
            Directory.Delete(_games, true);        // games/ 消失 (guide は元々無い)
            var r = Snap("auto", "20260101_000002");
            Assert.True(r.IsSkipped);
            Assert.True(r.IsAnomaly);
        }

        [Fact]
        public void AsymmetricMissing_GamesGoneGuideRemains_ReturnsSkipped()
        {
            // (レビュー M1) 非対称欠損: guide/ は残っているが games/ だけ消えた → guide-only manifest を黙って
            // Success で書かず、異常として世代まるごとスキップする (games blob の将来 GC を防ぐ)。
            WriteGameFile("g1/a.txt", "x");
            WriteGuideFile("slide.png", "img");
            Snap("auto", "20260101_000001");       // manifest に games/ と guide/ 両方
            Directory.Delete(_games, true);         // games/ だけ消失、guide/ は残る
            var r = Snap("auto", "20260101_000002");
            Assert.True(r.IsSkipped);
            Assert.True(r.IsAnomaly);
            Assert.Equal(1, ManifestCount("auto")); // 新しい (guide-only) manifest は書かれない
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

        [Fact]
        public void PoolBlob_StampedWithPlacementTime_NotSourceMtime()
        {
            // (レビュー#1) pool blob の mtime は「配置時刻」であるべき (元ファイルの古い mtime を継承すると
            // GC の grace が常に無効化される)。元ファイルの mtime を 2 年前にしても blob は直近時刻になること。
            WriteGameFile("g1/a.txt", "x");
            string srcFile = Path.Combine(_games, "g1", "a.txt");
            File.SetLastWriteTimeUtc(srcFile, DateTime.UtcNow.AddYears(-2));
            var r = Snap("auto", "20260101_000001");
            Assert.True(r.IsSuccess);
            string blob = Directory.GetFiles(PoolDir, "*", SearchOption.AllDirectories).First(f => !Path.GetFileName(f).StartsWith("."));
            Assert.True((DateTime.UtcNow - File.GetLastWriteTimeUtc(blob)).TotalMinutes < 5); // 古い mtime を継承していない
        }

        [Fact]
        public void Gc_Grace_KeepsRecentUnreferencedBlob()
        {
            // (レビュー#1) grace を効かせると、未参照になっても直近配置の blob は残る (= 並行/直近書込の保護)。
            _svc.GcGracePeriod = TimeSpan.FromHours(1);
            _settings.SetInt32(SettingsKeys.AssetSnapshotRetentionCount, 1);
            WriteGameFile("g1/a.txt", "v1"); Snap("auto", "20260101_000001");
            WriteGameFile("g1/a.txt", "v2"); Snap("auto", "20260101_000002"); // manifest1 が retention で削除 → v1 blob 未参照
            Assert.Equal(1, ManifestCount("auto"));
            Assert.Equal(2, PoolBlobCount()); // v1 は未参照だが直近配置なので grace で残る (v1 + v2)
        }
    }
}
