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
    /// (#271) `ManagerSessionService` の clock drift 対策テスト。
    ///
    /// 検証: (1) 起動時 cleanup は「stale だが放置ではない (60秒〜1日未満)」他 PC row を **削除しない**
    /// (= clock skew で生存中の遠隔 Manager row を消して DB 破損に倒れる経路を塞いだか)、(2) 1 日超の
    /// abandoned row は削除する、(3) 他 PC 検出は 60 秒閾値で stale を除外する。
    /// 一時 DB に直接 row を seed し、`DatabaseConnection(string)` seam で PathManager 非依存に回す。
    /// </summary>
    public class ManagerSessionDriftTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseConnection _conn;
        private readonly ManagerSessionRepository _repo;
        private ManagerSessionService _svc;

        public ManagerSessionDriftTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "tp_mgrsess_" + Guid.NewGuid().ToString("N") + ".db");
            _conn = new DatabaseConnection(_dbPath);
            new SchemaManager(_conn).InitializeDatabase();
            _repo = new ManagerSessionRepository(_conn);
        }

        public void Dispose()
        {
            try { _svc?.Shutdown(); } catch { /* ignore */ }
            try { SQLiteConnection.ClearAllPools(); } catch { /* ignore */ }
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
            }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void Seed(string pcName, long lastHeartbeatMs)
        {
            _repo.UpsertSelfSession(new ManagerSessionInfo
            {
                PcName = pcName,
                StartedAtUnixMs = lastHeartbeatMs,
                LastHeartbeatAtUnixMs = lastHeartbeatMs,
                Pid = 999,
                ManagerVersion = "0.0.0",
            });
        }

        private bool RowExists(string pcName)
        {
            using (var c = new SQLiteConnection(_conn.ConnectionString))
            {
                c.Open();
                using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM manager_sessions WHERE pc_name = @p", c))
                {
                    cmd.Parameters.AddWithValue("@p", pcName);
                    return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
                }
            }
        }

        [Fact]
        public void Cleanup_KeepsStaleButRecentOtherRow_NoDestructiveDelete()
        {
            // 90 秒 stale (検出閾値 60 秒は超えるが abandoned ではない) の他 PC row。
            // clock skew で生きている遠隔 Manager がこう見えうる → cleanup で消してはいけない (#271 の核心)。
            Seed("OTHER-RECENT", NowMs() - 90_000);
            _svc = new ManagerSessionService(_repo);
            _svc.Initialize();
            _svc.Shutdown();
            Assert.True(RowExists("OTHER-RECENT"),
                "60秒〜1日未満の他PC row は cleanup で削除されてはいけない (生存中の遠隔 Manager を消す経路を塞ぐ)");
        }

        [Fact]
        public void Cleanup_DeletesAbandonedOtherRow()
        {
            // 2 日前 = 明らかに放置された row は cleanup で削除されてよい (table 肥大防止)。
            Seed("OTHER-OLD", NowMs() - 2L * 86_400_000L);
            _svc = new ManagerSessionService(_repo);
            _svc.Initialize();
            _svc.Shutdown();
            Assert.False(RowExists("OTHER-OLD"),
                "1日超の abandoned row は cleanup で削除されるべき");
        }

        [Fact]
        public void Detection_Uses60sThreshold()
        {
            // 45 秒 = 60 秒閾値内で active (旧 30 秒なら stale)、90 秒 = stale。
            Seed("OTHER-45", NowMs() - 45_000);
            Seed("OTHER-90", NowMs() - 90_000);
            _svc = new ManagerSessionService(_repo);
            _svc.Initialize();
            var others = _svc.DetectOtherActiveSessions();
            _svc.Shutdown();
            Assert.Contains(others, s => s.PcName == "OTHER-45");
            Assert.DoesNotContain(others, s => s.PcName == "OTHER-90");
        }
    }
}
