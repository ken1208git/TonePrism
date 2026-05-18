using System;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// (#179) `manager_sessions` table の row 1 件を表す軽量 DTO。
    /// `ManagerSessionService.DetectOtherActiveSessions` の返り値要素として使う。
    /// schema は SPECIFICATION.md §7.3 / §3.8 参照。
    /// </summary>
    public sealed class ManagerSessionInfo
    {
        /// <summary>PC 名 (= `Environment.MachineName`)。</summary>
        public string PcName { get; set; }

        /// <summary>session 開始時刻 (UTC Unix epoch ms)。</summary>
        public long StartedAtUnixMs { get; set; }

        /// <summary>最終 heartbeat 時刻 (UTC Unix epoch ms)。</summary>
        public long LastHeartbeatAtUnixMs { get; set; }

        /// <summary>process ID (= `Process.GetCurrentProcess().Id`)。</summary>
        public long Pid { get; set; }

        /// <summary>Manager の version (例: "0.10.0")。</summary>
        public string ManagerVersion { get; set; }

        /// <summary>最終 heartbeat から経過した秒数 (UI 表示用、現在時刻を caller が指定)。</summary>
        public int SecondsSinceLastHeartbeat(long nowUnixMs)
        {
            long deltaMs = nowUnixMs - LastHeartbeatAtUnixMs;
            if (deltaMs < 0) deltaMs = 0;
            return (int)(deltaMs / 1000);
        }
    }
}
