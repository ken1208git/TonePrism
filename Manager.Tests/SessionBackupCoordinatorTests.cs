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
        public void Retention_MultiOpSession_KeepsPastSessions()
        {
            // (round6 High) 同一セッションで複数操作しても retention が「直近 N セッション」を保つこと。
            // バグ: ApplyRetention が replace-in-session の前世代削除より前に走り、「これから coordinator が消す前世代」を
            // 母数に数えて過去セッションを 1 件ずつ余計に削っていた (1 セッション K 操作で過去 K-1 世代が消失)。
            // 文化祭準備のように 1 起動で多数のゲームを追加する大編集セッションで、約束の retention 世代数が静かに崩れる。
            _settings.SetInt32("backup_retention_count", 2);
            // 過去 2 セッション分の .db を「古い日時」で seed (retention は名前降順 = 日時順。内容は読まないのでダミー可)。
            string autoDir = Path.Combine(Dest, "auto");
            Directory.CreateDirectory(autoDir);
            File.WriteAllText(Path.Combine(autoDir, "auto_20260101_000001_OLD.db"), "old1");
            File.WriteAllText(Path.Combine(autoDir, "auto_20260102_000001_OLD.db"), "old2");

            // 1 セッション (同一 coordinator) で DB-only 操作を 3 回。各回が今日の日時 .db を書き、前回の .db を replace する。
            WriteGame("g1/a.txt", "x");
            for (int i = 0; i < 3; i++) Assert.True(Run(false).IsSuccess);

            // retention=2 → 「直近 2 セッション」= 過去最新 (old2) + 現在セッションの最終世代 = 2 件。バグ時は現在 1 件のみ。
            Assert.Equal(2, AutoDbCount());
        }

        [Fact]
        public void Retention_NonNormalizedDest_StillExcludesPrevGeneration()
        {
            // (round8 #2) backup_destination_path が非正規化 (末尾 "." 等) でも retention 母数除外が効くこと。
            // full-path 完全一致だと DirectoryInfo.FullName (正規化済) と excludePath (raw configured 由来) がズレて
            // 除外が外れ round6 の過剰削除バグが silent 再発する。ファイル名比較なら正規化差に無依存。
            string dst = Path.Combine(_root, "dst");
            Directory.CreateDirectory(dst);
            _settings.SetString("backup_destination_path", dst + Path.DirectorySeparatorChar + "."); // 非正規化 "<dst>\."
            _settings.SetInt32("backup_retention_count", 2);
            string autoDir = Path.Combine(dst, "auto");
            Directory.CreateDirectory(autoDir);
            File.WriteAllText(Path.Combine(autoDir, "auto_20260101_000001_OLD.db"), "old1");
            File.WriteAllText(Path.Combine(autoDir, "auto_20260102_000001_OLD.db"), "old2");

            WriteGame("g1/a.txt", "x");
            for (int i = 0; i < 3; i++) Assert.True(Run(false).IsSuccess);

            // 直近 2 セッション (過去最新 old2 + 現在セッション最終) = 2 件。バグ時は 1 件に崩れる。
            Assert.Equal(2, Directory.GetFiles(autoDir, "*.db").Length);
        }

        [Fact]
        public void AssetRetention_MultiOpSession_KeepsPastManifests()
        {
            // (round6 High) アセット側 (manifest) も同型。複数アセット操作セッションで過去 manifest を過剰に消さないこと。
            // 過剰削除された manifest が参照していた pool blob は GC mark-sweep で道連れに消えるため、復元素材の喪失が重い。
            _settings.SetInt32("backup_retention_count", 2);
            string manAutoDir = Path.Combine(Dest, "asset_snapshots", "auto");
            Directory.CreateDirectory(manAutoDir);
            File.WriteAllText(Path.Combine(manAutoDir, "20260101_000001_OLD.manifest"), "#meta\n");
            File.WriteAllText(Path.Combine(manAutoDir, "20260102_000001_OLD.manifest"), "#meta\n");

            WriteGame("g1/a.txt", "x");
            for (int i = 0; i < 3; i++)
            {
                WriteGame("g1/b" + i + ".txt", "y" + i); // 各回 games/ を変えてアセット取得を走らせる
                Assert.True(Run(true).IsSuccess);
            }
            Assert.Equal(2, AutoManifestCount()); // old2 + 現在セッションの最終 manifest = 2。バグ時は 1。
        }

        [Fact]
        public void TopLevelCancel_AssetOp_SurfacedAsWarning_NotSilent()
        {
            // (round7 #3) アセット操作の DB フェーズ中キャンセル (top-level Skipped("キャンセル")) を完全 silent にしない。
            var cancelled = BackupResult.Skipped("キャンセル");
            var line = SessionBackupCoordinator.DescribeResult(cancelled, assetsRequested: true);
            Assert.NotNull(line);
            Assert.False(line.Value.Ok);   // 警告 (silent でない)
            // DB-only (assetsRequested=false) の Skipped は従来どおり沈黙。
            Assert.Null(SessionBackupCoordinator.DescribeResult(cancelled, assetsRequested: false));
        }

        [Fact]
        public void RestoreLockDeferral_SurfacedAsWarning_NotSilent()
        {
            // (round6 Medium #3) 他 PC が復元中で session backup が延期されたとき、完全 silent にせず「まだ控えていない」と
            // 知らせる。旧 StartAutoBackupIfDue は indicator をクリアしていたが、新方式では Skipped が DescribeResult で
            // null になり何も出ず、復元ウィンドウ中の編集が未控えのまま気づかれない穴があった。
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _settings.SetString(SettingsKeys.RestoreLockOwner, "OTHER_PC|" + nowMs);
            WriteGame("g1/a.txt", "x");

            var r = Run(true);
            Assert.True(r.IsSkipped);   // 延期 (DB バックアップは走っていない)
            Assert.True(r.IsDeferred);  // 通常の Skipped と区別
            Assert.Equal(0, AutoDbCount());

            var line = SessionBackupCoordinator.DescribeResult(r, assetsRequested: true);
            Assert.NotNull(line);          // silent (null) ではない
            Assert.False(line.Value.Ok);   // 警告
        }

        [Fact]
        public void AssetSnapshot_ExcludesPendingDeleteFolder()
        {
            // (round9 L2) ゲーム削除の retry 退避フォルダ games/{id}.pending-delete-{guid} は、物理削除を諦めて games/ 配下に
            // 残っても snapshot に取り込まない (削除したはずのゲーム実体が manifest に復活する混乱を防ぐ)。
            WriteGame("g1/a.txt", "alpha");
            WriteGame("g1.pending-delete-deadbeefdeadbeef/old.txt", "orphan"); // 削除退避フォルダの残骸
            Assert.True(Run(true).IsSuccess);

            string manAutoDir = Path.Combine(Dest, "asset_snapshots", "auto");
            string[] manifests = Directory.GetFiles(manAutoDir, "*.manifest");
            Assert.Single(manifests);
            string body = File.ReadAllText(manifests[0]);
            Assert.Contains("g1/a.txt", body);          // 通常のゲームは控える
            Assert.DoesNotContain("pending-delete", body); // 退避フォルダは除外
        }

        [Fact]
        public void Coalesce_RequestDuringRun_RunsOnceMoreWithAccumulatedAssets()
        {
            // (#299) 実行中に来た変更はコアレスされ、終了後に蓄積分で 1 回だけ走る。スレッド無しで状態遷移を直接検証。
            // 1 件目: idle → 起動 (DB-only)
            Assert.True(_coord.TryStartRun(requestIncludeAssets: false, out bool runIA));
            Assert.False(runIA);                 // 今回は DB-only
            Assert.True(_coord.IsBackupRunning);

            // 実行中に 2 件 (DB-only + アセット) が来る → どちらもコアレス (新 worker は起動しない＝false)
            Assert.False(_coord.TryStartRun(false, out _));
            Assert.False(_coord.TryStartRun(true, out _));

            // 1 回目終了 → dirty なので蓄積分でもう 1 回。アセットが OR 蓄積されているので includeAssets=true。
            Assert.True(_coord.TryContinue(out bool nextIA));
            Assert.True(nextIA);

            // 2 回目終了 → dirty なし → 停止。
            Assert.False(_coord.TryContinue(out _));
            Assert.False(_coord.IsBackupRunning);
        }

        [Fact]
        public void Coalesce_IdleAfterCycle_StartsFresh()
        {
            // 1 サイクル回して idle に戻ったら、次の要求でまた起動できる (pending は消費済でリセット)。
            Assert.True(_coord.TryStartRun(true, out _));
            Assert.False(_coord.TryContinue(out _));   // dirty なし → 停止
            Assert.False(_coord.IsBackupRunning);

            Assert.True(_coord.TryStartRun(false, out bool runIA)); // 再起動できる
            Assert.False(runIA);                                    // 前サイクルのアセット pending は持ち越さない
            Assert.True(_coord.IsBackupRunning);
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

            var line = SessionBackupCoordinator.DescribeResult(r, assetsRequested: true);
            Assert.NotNull(line);
            Assert.False(line.Value.Ok);            // 緑「✓」ではなく警告
            Assert.Contains("ゲームファイル", line.Value.Message);
        }

        [Fact]
        public void AssetRequestedButSkipped_SurfacedAsWarning()
        {
            // (round4 #1) ゲーム本体の控えを要求した操作で、アセット取得が Skipped (ユーザーのキャンセル相当) なら
            // 緑「✓」にしない。キャンセルを単体で再現しにくいので、同じ「非成功 Skipped」を生む asset 無効化で代理検証。
            _settings.SetString(SettingsKeys.AssetSnapshotEnabled, "false");
            WriteGame("g1/a.txt", "x");
            var r = Run(true);
            Assert.True(r.IsSuccess);                 // DB バックアップは成功
            Assert.NotNull(r.AssetSnapshot);
            Assert.True(r.AssetSnapshot.IsSkipped);   // ゲーム本体は Skipped (非成功)
            Assert.False(r.AssetSnapshot.IsSuccess);

            var line = SessionBackupCoordinator.DescribeResult(r, assetsRequested: true);
            Assert.NotNull(line);
            Assert.False(line.Value.Ok);              // 緑「✓」ではなく警告 (キャンセルで緑✓になる退行の防止)
        }

        [Fact]
        public void AssetFailure_ThenDbOnly_KeepsSessionUnhealthy_RecoversOnAssetSuccess()
        {
            // (round5 #1) アセット操作が失敗すると sticky 警告が出るが、続く DB-only 成功が緑✓でそれを上書き消去して
            // しまう穴を塞ぐ。coordinator がセッションの「ゲーム本体控え未完了」状態を保持し、回復まで緑✓を出さない。
            string dest = _backup.GetEffectiveDestinationDirectory();
            Directory.CreateDirectory(dest);
            string poolBlocker = Path.Combine(dest, "asset_pool");
            File.WriteAllText(poolBlocker, "blocker"); // CreateSnapshot を失敗させる (DB バックアップ自体は成功)
            WriteGame("g1/a.txt", "x");

            var r1 = Run(true);
            Assert.True(r1.IsSuccess);                 // DB は成功
            Assert.False(r1.AssetSnapshot.IsSuccess);  // ゲーム本体の控えは失敗
            Assert.True(_coord.SessionAssetCaptureFailed); // セッションは「未控え」

            // DB-only 操作 (asset_pool ブロックは DB バックアップに無関係) は成功するが、ゲーム本体は依然未控え。
            var r2 = Run(false);
            Assert.True(r2.IsSuccess);
            Assert.True(_coord.SessionAssetCaptureFailed); // DB-only では回復しない (= 後続も緑✓にしない根拠)

            // ブロックを外してアセット操作が成功すると回復する。
            File.Delete(poolBlocker);
            var r3 = Run(true);
            Assert.True(r3.AssetSnapshot != null && r3.AssetSnapshot.IsSuccess);
            Assert.False(_coord.SessionAssetCaptureFailed); // 回復
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
