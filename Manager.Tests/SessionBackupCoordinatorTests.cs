using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using TonePrism.Manager;
using TonePrism.Manager.Repositories;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#295) `SessionBackupCoordinator`（操作単位 / replace-in-session の自動バックアップ）の検証。
    /// 一時 DB + 一時 games/ + 既定 backup_dest で UI-free に `RunSessionBackup` を直接駆動する
    /// (`AssetSnapshotServiceTests` と同じ fixture 流儀)。
    /// </summary>
    public class SessionBackupCoordinatorTests : IDisposable
    {
        private readonly string _root;
        private readonly string _games;
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settings;
        private readonly BackupService _backup;
        private readonly AssetSnapshotService _asset;
        private readonly SessionBackupCoordinator _coord;

        public SessionBackupCoordinatorTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "tp_sbc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            string dbPath = Path.Combine(_root, "toneprism.db");
            _games = Path.Combine(_root, "games");
            _conn = new DatabaseConnection(dbPath);
            new SchemaManager(_conn).InitializeDatabase();
            _settings = new SettingsRepository(_conn);
            _backup = new BackupService(_conn, _settings);
            _asset = new AssetSnapshotService(_conn, _settings, _backup);
            _asset.GcGracePeriod = TimeSpan.Zero;
            _backup.AttachSnapshotService(_asset);
            _coord = new SessionBackupCoordinator(_backup);
        }

        public void Dispose()
        {
            try { SQLiteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private void WriteGame(string rel, string content)
        {
            string p = Path.Combine(_games, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, content);
        }

        private string Dest => _backup.GetEffectiveDestinationDirectory();
        private int AutoDbCount()
        {
            string d = Path.Combine(Dest, "auto");
            return Directory.Exists(d) ? Directory.GetFiles(d, "*.db").Length : 0;
        }
        private int AutoManifestCount()
        {
            string d = Path.Combine(Dest, "asset_snapshots", "auto");
            return Directory.Exists(d) ? Directory.GetFiles(d, "*.manifest").Length : 0;
        }
        private BackupResult Run(bool includeAssets) => _coord.RunSessionBackup(includeAssets, null, default(CancellationToken));

        [Fact]
        public void ReplaceInSession_KeepsOneGeneration()
        {
            WriteGame("g1/a.txt", "alpha");
            var r1 = Run(true);
            Assert.True(r1.IsSuccess);
            Assert.Equal(1, AutoDbCount());
            Assert.Equal(1, AutoManifestCount());

            // 同セッション内で再取得 → 前の自動世代 (.db + manifest) を消して上書き = 1 つのまま。
            WriteGame("g1/b.txt", "beta");
            var r2 = Run(true);
            Assert.True(r2.IsSuccess);
            Assert.Equal(1, AutoDbCount());
            Assert.Equal(1, AutoManifestCount());
        }

        [Fact]
        public void DbOnlyOperation_AddsNoAssetManifest_KeepsPriorManifest()
        {
            WriteGame("g1/a.txt", "alpha");
            Run(true);
            int manifestsBefore = AutoManifestCount(); // 1

            var r = Run(false); // DB-only (ストア/設定外 編集等)
            Assert.True(r.IsSuccess);
            Assert.Equal(1, AutoDbCount());                 // .db は置換されて 1
            Assert.Equal(manifestsBefore, AutoManifestCount()); // manifest は増えず、直前のアセット世代を温存
        }

        [Fact]
        public void Retention_CountsSessions()
        {
            _settings.SetInt32("backup_retention_count", 2);
            WriteGame("g1/a.txt", "x");
            // 3 セッション = coordinator を作り直して別 process run を emulate (各セッションの初回は前セッションを消さない)。
            for (int i = 0; i < 3; i++)
            {
                var c = new SessionBackupCoordinator(_backup);
                c.RunSessionBackup(true, null, default(CancellationToken));
            }
            Assert.Equal(2, AutoDbCount()); // retention で直近 2 セッションに間引かれる
        }

        [Fact]
        public void Disabled_Skips()
        {
            _settings.SetString(SettingsKeys.BackupAutoEnabled, "false");
            WriteGame("g1/a.txt", "x");
            var r = Run(true);
            Assert.True(r.IsSkipped);
            Assert.Equal(0, AutoDbCount());
        }

        [Fact]
        public void Failure_DoesNotThrow_ReturnsFailed()
        {
            // 保存先を「ファイルの下」に向けて Directory 作成を失敗させる (best-effort: throw せず Failed)。
            string blocker = Path.Combine(_root, "blocker");
            File.WriteAllText(blocker, "x");
            _settings.SetString("backup_destination_path", Path.Combine(blocker, "sub"));
            WriteGame("g1/a.txt", "x");
            var r = Run(true);
            Assert.True(r.IsFailed);
        }

        [Fact]
        public void AssetFailure_SurfacedAsWarning_NotGreenSuccess()
        {
            // (round2 #1) DB は成功でもゲーム本体 (games/guide) の控えが失敗したら「✓」緑ではなく警告にする退行防止。
            // asset_pool をファイルにして CreateSnapshot を失敗させる (DB バックアップ自体は成功)。
            string dest = _backup.GetEffectiveDestinationDirectory();
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "asset_pool"), "blocker");
            WriteGame("g1/a.txt", "x");

            var r = Run(true);
            Assert.True(r.IsSuccess);               // DB バックアップは成功
            Assert.NotNull(r.AssetSnapshot);
            Assert.True(r.AssetSnapshot.IsFailed);  // ゲーム本体の控えは失敗

            var line = SessionBackupCoordinator.DescribeResult(r);
            Assert.NotNull(line);
            Assert.False(line.Value.Ok);            // 緑「✓」ではなく警告
            Assert.Contains("ゲーム本体", line.Value.Message);
        }

        [Fact]
        public void AssetFailureInSession_KeepsPreviousManifest()
        {
            // (round3 High) 同一セッションで ①アセット取得成功 → ②アセット取得失敗 のとき、②が①の控えを消さないこと。
            WriteGame("g1/a.txt", "x");
            var r1 = Run(true);
            Assert.True(r1.AssetSnapshot != null && r1.AssetSnapshot.IsSuccess);
            Assert.Equal(1, AutoManifestCount()); // manifest_A

            // 2 回目: games/ を消して CreateSnapshot を SkippedAnomaly にする (履歴あり + sources 消失)。DB は成功。
            Directory.Delete(_games, true);
            var r2 = Run(true);
            Assert.True(r2.IsSuccess);                // DB バックアップは成功
            Assert.False(r2.AssetSnapshot.IsSuccess); // ゲーム本体の控えは失敗/異常
            Assert.Equal(1, AutoManifestCount());     // 前 manifest を消さず温存 = まだ 1 件 (旧実装は 0 に消えていた)
        }
    }
}
