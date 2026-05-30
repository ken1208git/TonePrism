using System;
using System.IO;
using System.Linq;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#269) `LauncherSessionService` の stale 判定 clock-drift 耐性テスト。
    ///
    /// 検証対象は primary path の `max(JSON last_heartbeat_at_unix_ms, file mtime)` ロジックと
    /// 60 秒閾値。json (書き手自己申告時計) と mtime (SMB サーバ単一時計) の弱点補完を、
    /// 一時フォルダに session JSON を書いて `File.SetLastWriteTimeUtc` で mtime を制御して再現する。
    /// </summary>
    public class LauncherStaleDetectionTests : IDisposable
    {
        private readonly string _folder;
        private readonly LauncherSessionService _service;

        public LauncherStaleDetectionTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "tp_lsess_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _service = new LauncherSessionService(_folder);
            _service.Initialize();
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { /* ignore */ }
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>session JSON を書き、mtime を任意時刻に設定する。</summary>
        private void WriteSession(string pcName, long heartbeatUnixMs, DateTime mtimeUtc)
        {
            string path = Path.Combine(_folder, pcName + ".json");
            string json =
                "{\"pc_name\":\"" + pcName + "\"," +
                "\"started_at_unix_ms\":" + heartbeatUnixMs + "," +
                "\"last_heartbeat_at_unix_ms\":" + heartbeatUnixMs + "," +
                "\"pid\":12345,\"launcher_version\":\"0.5.18\"}";
            File.WriteAllText(path, json);
            File.SetLastWriteTimeUtc(path, mtimeUtc);
        }

        private bool Detected(string pcName)
        {
            return _service.DetectActiveLauncherSessions().Any(s => s.PcName == pcName);
        }

        [Fact]
        public void JsonOld_MtimeFresh_KeptActive_ClockDriftDefended()
        {
            // 書き手 PC の時計が 120 秒遅れ → json は古く見えるが、mtime (サーバ時計) は fresh。
            // max(old json, fresh mtime) = fresh ⇒ active を維持 (= 生存中 Launcher を誤って除外しない)。
            long oldJsonMs = NowMs() - 120_000;
            WriteSession("PC-DRIFT", oldJsonMs, DateTime.UtcNow);
            var session = _service.DetectActiveLauncherSessions().FirstOrDefault(s => s.PcName == "PC-DRIFT");
            Assert.NotNull(session); // active 維持 (誤って除外しない)
            // (#269 review #3) effective = max(json, mtime) が表示用 LastHeartbeatAtUnixMs に入ることを検証。
            // json は 120 秒前だが mtime は fresh なので、effective は古い json 値より十分新しい (mtime 寄り) はず。
            Assert.True(session.LastHeartbeatAtUnixMs > oldJsonMs + 60_000,
                "LastHeartbeatAtUnixMs は effective (max=mtime 寄り) になるべき (古い json 値のままではない)");
        }

        [Fact]
        public void JsonFresh_MtimeOld_KeptActive_SmbCacheLagDefended()
        {
            // mtime が SMB directory cache 遅延で 120 秒古く見えるが、json は fresh。
            // max(fresh json, old mtime) = fresh ⇒ active を維持 (= 元々 json を primary にした理由を保持)。
            WriteSession("PC-CACHE", NowMs(), DateTime.UtcNow.AddSeconds(-120));
            Assert.True(Detected("PC-CACHE"), "mtime が cache 遅延で古くても json が救って active 維持されるべき");
        }

        [Fact]
        public void JsonOld_MtimeOld_DetectedStale_NotReturned()
        {
            // json も mtime も 120 秒古い = 本当に死んでいる session ⇒ stale として除外。
            WriteSession("PC-DEAD", NowMs() - 120_000, DateTime.UtcNow.AddSeconds(-120));
            Assert.False(Detected("PC-DEAD"), "json/mtime 双方が古い真に死んだ session は stale として除外されるべき");
        }

        [Fact]
        public void WithinWidenedThreshold_45s_KeptActive()
        {
            // 45 秒経過 = 旧 30 秒閾値なら stale だが、#269 の 60 秒閾値では active。
            WriteSession("PC-45S", NowMs() - 45_000, DateTime.UtcNow.AddSeconds(-45));
            Assert.True(Detected("PC-45S"), "45 秒は 60 秒閾値の内側なので active 維持されるべき (旧 30 秒なら stale)");
        }
    }
}
