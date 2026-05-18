using System;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// (#179 PR3b) Launcher の heartbeat JSON file 1 件を表す軽量 DTO。
    /// `LauncherSessionService.DetectActiveLauncherSessions` の返り値要素として使われる。
    /// `ManagerSessionInfo` と対称的な構造で、Manager / Launcher 両 session を
    /// `SessionConflictDialog` で merge 表示する基盤を提供する。
    ///
    /// JSON schema は SPECIFICATION.md §3.8.7.2 で literal 定義済 (= SoT):
    /// ```json
    /// {
    ///   "pc_name": "PC-A",
    ///   "started_at_unix_ms": 1715379600000,
    ///   "last_heartbeat_at_unix_ms": 1715379630000,
    ///   "pid": 12345,
    ///   "launcher_version": "0.5.18"
    /// }
    /// ```
    /// </summary>
    public sealed class LauncherSessionInfo
    {
        /// <summary>PC 名 (= JSON 内 `pc_name`、Launcher 側 `COMPUTERNAME` 環境変数)。</summary>
        public string PcName { get; set; }

        /// <summary>session 開始時刻 (UTC Unix epoch ms)。</summary>
        public long StartedAtUnixMs { get; set; }

        /// <summary>最終 heartbeat 時刻 (UTC Unix epoch ms)。stale 判定の primary baseline。</summary>
        public long LastHeartbeatAtUnixMs { get; set; }

        /// <summary>
        /// Launcher process ID。stale 判定には使用しないが (round 3 L-2 で導入):
        /// `SessionConflictDialog.Show` の Logger trail で `pc=PC-B pid=12345 ver=0.5.18` 形式で出力、
        /// log 解析時に「自 PC 検出 = 自 `Process.GetCurrentProcess().Id` との一致」判定で同 PC 上の
        /// Launcher を識別可能化する。dialog body (= user 視点) には pid は出さない (= 部員視点で
        /// 意味なし、log のみ)。
        /// </summary>
        public long Pid { get; set; }

        /// <summary>Launcher version (例: "0.5.18")。`SessionConflictDialog` 表示で使用。</summary>
        public string LauncherVersion { get; set; }

        /// <summary>
        /// 最終 heartbeat から経過した秒数 (UI 表示用)。`ManagerSessionInfo` と対称 helper。
        /// 負値の場合は 0 にクランプ (clock skew tolerance)。
        /// </summary>
        public int SecondsSinceLastHeartbeat(long nowUnixMs)
        {
            long deltaMs = nowUnixMs - LastHeartbeatAtUnixMs;
            if (deltaMs < 0) deltaMs = 0;
            return (int)(deltaMs / 1000);
        }
    }
}
