using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using TonePrism.Manager.Repositories;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#299 review #3) `RestoreService` の防御検証。非ブロッキング化でバックアップ worker が DB コピーフェーズ中に
    /// プロセス強制終了すると、検証前の不完全 .db が backup_dest に残り、`BackupCatalogService` は list 時の
    /// quick_check を省く (= 成功世代と区別不能) ため復元候補に出うる。復元は live を置換する **前** に quick_check で
    /// 弾き、現 DB を保護することを確認する (壊れた / 非 DB ファイル全般への防御でもある)。
    /// </summary>
    public class RestoreServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly DatabaseConnection _conn;
        private readonly SettingsRepository _settings;
        private readonly RestoreService _restore;

        public RestoreServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "tp_restore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            string dbPath = Path.Combine(_root, "toneprism.db");
            _conn = new DatabaseConnection(dbPath);
            new SchemaManager(_conn).InitializeDatabase();
            _settings = new SettingsRepository(_conn);
            _restore = new RestoreService(_conn, _settings);
        }

        public void Dispose()
        {
            try { SQLiteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void CheckIntegrity_ClassifiesValidAndGarbage()
        {
            // 有効 DB (fixture の live) → 開けて "ok"。
            var ok = RestoreService.CheckIntegrity(_conn.DbPath);
            Assert.True(ok.Openable);
            Assert.Equal("ok", ok.QuickCheckResult);

            // garbage → 開けない (Openable=false, 結果なし)。
            string garbage = Path.Combine(_root, "garbage.db");
            File.WriteAllBytes(garbage, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            var bad = RestoreService.CheckIntegrity(garbage);
            Assert.False(bad.Openable);
            Assert.Null(bad.QuickCheckResult);
        }

        [Fact]
        public void Restore_OpenableButUnhealthy_GatedByAllowIntegrityWarnings()
        {
            // (#299 review round3 #3) 「開けるが quick_check 非 ok」= 不健全なバックアップを作る: 有効 DB を作り大きめ blob で
            // 多数のページに膨らませてから、末尾のデータページを潰す (先頭ページ=ヘッダ/スキーマは無傷なので open は成功、
            // quick_check は破損を検出)。allowIntegrityWarnings が override を gate することを確認する。
            string backup = Path.Combine(_root, "auto_20260102_000000_HOST.db");
            var bc = new DatabaseConnection(backup);
            new SchemaManager(bc).InitializeDatabase();
            using (var conn = new SQLiteConnection(bc.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "CREATE TABLE _filler(id INTEGER PRIMARY KEY, b BLOB);";
                    cmd.ExecuteNonQuery();
                    cmd.CommandText = "INSERT INTO _filler(b) VALUES (zeroblob(200000));";
                    cmd.ExecuteNonQuery();
                }
            }
            SQLiteConnection.ClearAllPools();

            byte[] bytes = File.ReadAllBytes(backup);
            int start = Math.Max(4096, bytes.Length - 4096); // 末尾ページ (データ/overflow。スキーマは先頭側)
            for (int i = start; i < start + 512 && i < bytes.Length; i++) bytes[i] = 0xFF;
            File.WriteAllBytes(backup, bytes);

            var integ = RestoreService.CheckIntegrity(backup);
            Assert.True(integ.Openable);                    // ヘッダ無傷 → open 可
            Assert.NotEqual("ok", integ.QuickCheckResult);  // 末尾ページ破損 → 非 ok

            // 既定 (allowIntegrityWarnings=false) → 中止 (live 無傷)。
            Assert.Throws<InvalidOperationException>(() => _restore.Restore(backup, null, default(CancellationToken)));
            // ユーザー確認あり (true) → 続行 (safety を返す)。
            string safety = _restore.Restore(backup, null, default(CancellationToken), allowIntegrityWarnings: true);
            Assert.False(string.IsNullOrEmpty(safety));
        }

        [Fact]
        public void Restore_CorruptOrIncompleteBackup_Aborts_LiveDbUntouched()
        {
            // 「壊れた / 不完全な .db」を SQLite ヘッダでない garbage バイトで再現する。
            string badBackup = Path.Combine(_root, "auto_20260101_000000_HOST.db");
            File.WriteAllBytes(badBackup, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x42, 0x42, 0x42, 0x42 });

            // 復元は置換前 (File.Replace 前) に quick_check で弾き、InvalidOperationException で中止する。
            // 修正前は quick_check が無く、garbage を live に File.Replace して現 DB を破壊していた。
            Assert.Throws<InvalidOperationException>(() => _restore.Restore(badBackup, null, default(CancellationToken)));

            // 現 DB は無傷 = quick_check が "ok"。
            using (var conn = new SQLiteConnection(_conn.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA quick_check;";
                    Assert.Equal("ok", cmd.ExecuteScalar()?.ToString());
                }
            }
        }
    }
}
