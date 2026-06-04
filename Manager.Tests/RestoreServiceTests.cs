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
