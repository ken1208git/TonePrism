using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using TonePrism.Manager;
using TonePrism.Manager.Models;
using TonePrism.Manager.Repositories;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#250 PR3b) `AssetSnapshotService.FindSnapshotForBackup` の検証。DB バックアップ世代 (.db) と
    /// アセット控え (.manifest) の **時刻ペアリング** ルール:「T (= .db 作成時刻) 以下で最大時刻の manifest、
    /// host 一致優先、tie-break はファイル名降順」を固める。manifest は header (META 行) だけ読むので
    /// 手書きで `asset_snapshots/&lt;trigger&gt;/&lt;ts&gt;_&lt;host&gt;.manifest` に 8 フィールド META を置く。
    /// </summary>
    public class AssetSnapshotPairingTests : IDisposable
    {
        private readonly string _root, _dbPath;
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settings;
        private readonly BackupService _backup;
        private readonly AssetSnapshotService _snap;

        public AssetSnapshotPairingTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "tp_apair_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _dbPath = Path.Combine(_root, "toneprism.db");
            _conn = new DatabaseConnection(_dbPath);
            new SchemaManager(_conn).InitializeDatabase();
            _settings = new SettingsRepository(_conn);
            _backup = new BackupService(_conn, _settings);
            _snap = new AssetSnapshotService(_conn, _settings, _backup);
        }

        public void Dispose()
        {
            try { SQLiteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        // ---- helpers ----
        private string SnapRoot => _snap.GetSnapshotRootDirectory();

        /// <summary>asset_snapshots/&lt;trigger&gt;/&lt;ts&gt;_&lt;host&gt;.manifest に 8 フィールド META を 1 行だけ書く
        /// (ペアリングは header しか見ないので entry 行は省略)。<paramref name="metaTimestamp"/> 既定は leaf の ts と同じ。</summary>
        private string WriteManifest(string trigger, string ts, string host, int fileCount = 1, string metaTimestamp = null)
        {
            string dir = Path.Combine(SnapRoot, trigger);
            Directory.CreateDirectory(dir);
            string leaf = string.IsNullOrEmpty(host) ? ts : ts + "_" + host;
            string path = Path.Combine(dir, leaf + ".manifest");
            string mts = metaTimestamp ?? ts;
            File.WriteAllText(path, $"META\t{mts}\t{host}\t{trigger}\t{fileCount}\t0\t0\t0\n");
            return path;
        }

        private static DateTime Local(string ts) =>
            DateTime.ParseExact(ts, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        // EnumerateManifests は ForceLongPath で `\\?\` プレフィックス付きパスを返すため、生のパス比較ではなく
        // ファイル名 (leaf は世代内で一意) で同一性を判定する。
        private static void AssertSameManifest(string expectedPath, AssetSnapshotInfo actual)
        {
            Assert.NotNull(actual);
            Assert.Equal(Path.GetFileName(expectedPath), Path.GetFileName(actual.ManifestPath));
        }

        // ---- tests ----

        [Fact]
        public void Picks_LatestAtOrBeforeT()
        {
            WriteManifest("auto", "20260101_000001", "PC1");
            string newer = WriteManifest("auto", "20260101_000003", "PC1");
            WriteManifest("auto", "20260101_000005", "PC1"); // T より後 → 除外される

            var r = _snap.FindSnapshotForBackup(Local("20260101_000004"), "PC1");
            AssertSameManifest(newer, r);
        }

        [Fact]
        public void ReplaceInSession_DbTimestampLaterThanManifest()
        {
            // replace-in-session: .db が manifest より後の時刻になる。T 以下で最大 = 直前フル世代の manifest を拾う。
            string manifest = WriteManifest("auto", "20260101_000001", "PC1");

            var r = _snap.FindSnapshotForBackup(Local("20260101_000500"), "PC1");
            AssertSameManifest(manifest, r);
        }

        [Fact]
        public void HostMatch_PreferredOverDifferentHost()
        {
            // 同時刻 (同秒) に 2 PC が控えた。.db を作った PC1 の控えを優先する。
            WriteManifest("auto", "20260101_000003", "PC2");
            string pc1 = WriteManifest("auto", "20260101_000003", "PC1");

            var r = _snap.FindSnapshotForBackup(Local("20260101_000004"), "PC1");
            AssertSameManifest(pc1, r);
        }

        [Fact]
        public void HostMatch_DoesNotBeatTemporallyCloserDifferentHost()
        {
            // (review #1) degraded ケース: T 直前は別 host(PC2)、それより古いものが同 host(PC1)。games/ は共有単一ツリー
            // なので「T の状態」に近いのは時間的に近い PC2。host 一致でも数世代前の PC1 を優先してはいけない
            // (古い世代で reconcile すると T 以降に増えたファイルを誤削除する)。host 優先は同秒 tie 限定。
            WriteManifest("auto", "20260101_000001", "PC1");                       // 古い・host 一致
            string closerDifferentHost = WriteManifest("auto", "20260101_000004", "PC2"); // T に近い・host 不一致

            var r = _snap.FindSnapshotForBackup(Local("20260101_000005"), "PC1");
            AssertSameManifest(closerDifferentHost, r);
        }

        [Fact]
        public void NoHostMatch_FallsBackToGlobalLatest()
        {
            WriteManifest("auto", "20260101_000001", "PC2");
            string latest = WriteManifest("auto", "20260101_000003", "PC3");

            // preferredHost=PC1 はどの manifest にも無い → 全体最新 (PC3 の 000003) にフォールバック。
            var r = _snap.FindSnapshotForBackup(Local("20260101_000004"), "PC1");
            AssertSameManifest(latest, r);
        }

        [Fact]
        public void NoManifestBeforeT_ReturnsNull()
        {
            WriteManifest("auto", "20260101_000010", "PC1"); // すべて T より後

            var r = _snap.FindSnapshotForBackup(Local("20260101_000005"), "PC1");
            Assert.Null(r);
        }

        [Fact]
        public void EmptyRoot_ReturnsNull()
        {
            var r = _snap.FindSnapshotForBackup(Local("20260101_000005"), "PC1");
            Assert.Null(r);
        }

        [Fact]
        public void SpansAutoAndManual()
        {
            WriteManifest("auto", "20260101_000001", "PC1");
            string manualNewer = WriteManifest("manual", "20260101_000004", "PC1");

            var r = _snap.FindSnapshotForBackup(Local("20260101_000005"), "PC1");
            AssertSameManifest(manualNewer, r); // manual 側がより新しければそれを採る (横断)
        }

        [Fact]
        public void TieBreak_SameSecond_FileNameDescending()
        {
            // 同秒・host 不一致 (preferredHost を空にして host 優先を無効化) → ファイル名降順で決定的に選ぶ。
            WriteManifest("auto", "20260101_000003", "AAA");
            string zzz = WriteManifest("auto", "20260101_000003", "ZZZ");

            var r = _snap.FindSnapshotForBackup(Local("20260101_000004"), "");
            AssertSameManifest(zzz, r); // "..._ZZZ.manifest" > "..._AAA.manifest"
        }

        [Fact]
        public void SkipsManifestWithUnparseableHeader()
        {
            // META の timestamp が壊れている (解釈不能) → 時刻信頼不可で除外、手前の正常世代を採る。
            WriteManifest("auto", "20260101_000009", "PC1", metaTimestamp: "NOT_A_TIMESTAMP");
            string good = WriteManifest("auto", "20260101_000002", "PC1");

            var r = _snap.FindSnapshotForBackup(Local("20260101_000010"), "PC1");
            AssertSameManifest(good, r);
        }

        // (#250 PR3b round3) safety_db の undo 用の完全一致ペアリング。
        [Fact]
        public void FindSafetyManifest_ExactMatch()
        {
            string m = WriteManifest("safety", "20260101_000005", "PC1");
            var r = _snap.FindSafetyManifestForBackup("20260101_000005", "PC1");
            AssertSameManifest(m, r);
        }

        [Fact]
        public void FindSafetyManifest_NoMatch_ReturnsNull()
        {
            WriteManifest("safety", "20260101_000005", "PC1");
            Assert.Null(_snap.FindSafetyManifestForBackup("20260101_000009", "PC1")); // 別 timestamp
            Assert.Null(_snap.FindSafetyManifestForBackup("20260101_000005", "PC2")); // 別 host
        }

        [Fact]
        public void FindSafetyManifest_IgnoresAutoAndManual()
        {
            // 同 ts/host が auto/manual にあっても safety フォルダ以外は見ない (誤ペア防止)。
            WriteManifest("auto", "20260101_000005", "PC1");
            WriteManifest("manual", "20260101_000005", "PC1");
            Assert.Null(_snap.FindSafetyManifestForBackup("20260101_000005", "PC1"));
        }

        // (#250 PR3b review #1) ペアリング許可は auto/manual のみ。safety/unknown は対の控えを持たないため DBのみ復元に倒す
        // (時刻 fallback で無関係な世代を拾い、undo (safety 復元) でゲームファイルを誤削除する事故を防ぐ gate)。
        [Theory]
        [InlineData("auto", true)]
        [InlineData("manual", true)]
        [InlineData("AUTO", true)]   // 大文字小文字非依存
        [InlineData("safety", false)]
        [InlineData("unknown", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void PairingEligibility_OnlyAutoAndManual(string triggerType, bool expected)
        {
            Assert.Equal(expected, RestorePairingPolicy.IsAssetPairingEligible(triggerType));
        }

        // (review round5 #2) ParseSafetyTimestamp ↔ RestoreService の safety 命名規約の invariant をピン留め。
        // RestoreService は `safety_{yyyyMMdd_HHmmss}[_host][_衝突suffix].db` を産む (RestoreService.cs)。これと
        // ズレると退避が silent に DBのみ degrade するため、実フォーマットの抽出を回帰で固定する。
        [Theory]
        [InlineData("C:/dest/backups/safety/safety_20260606_143022_PC1.db", "20260606_143022")] // host 付き (通常)
        [InlineData("safety_20260606_143022_PC1_2.db", "20260606_143022")]                      // 同秒衝突 suffix
        [InlineData("safety_20260606_143022.db", "20260606_143022")]                            // host 無し
        [InlineData("safety_20260606_143022_PC-A_B.db", "20260606_143022")]                     // sanitize 済 host (_→-)
        [InlineData("auto_20260606_143022_PC1.db", null)]                                       // safety 以外は対象外
        [InlineData("safety_2026_bad.db", null)]                                                // 形式不一致
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ParseSafetyTimestamp_MatchesRestoreServiceNaming(string safetyPath, string expected)
        {
            Assert.Equal(expected, RestorePairingPolicy.ParseSafetyTimestamp(safetyPath));
        }
    }
}
