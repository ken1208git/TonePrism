namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// `settings` テーブル (汎用 K/V) で使う key 定数を集約する。
    ///
    /// SettingsRepository は ON CONFLICT(key) DO UPDATE で動く汎用 K/V store のため、新規 key の
    /// 追加に schema migration は不要 (CurrentDbVersion bump 不要)。本 class はリポジトリ全体で
    /// 同じ文字列キーを参照するための SoT (typo 防止 + grep 一発で全用例検出)。
    ///
    /// Phase 4 (#108) で導入: アップデート check の cache、skip 機能、background check の頻度
    /// 設定をすべて settings テーブル経由で永続化する (Manager 再起動を跨いでも保持されるよう)。
    /// </summary>
    internal static class SettingsKeys
    {
        // ----- backup 既存 (settings テーブルに既に存在) -----
        // SettingsRepository.TryAcquireBackupLease 内で hardcode された "last_backup_at"
        // および BackupSettingsForm 系で使う key。既存実装に合わせて参照しない (本クラスで
        // ラップしない方が既存コードを書き換える scope creep を防げる)。

        // ----- Phase 4 (#108) update flow -----

        /// <summary>
        /// 「このバージョンをスキップ」で記録される Bundle version (例: "0.3.0")。
        /// UpdateChecker.ShouldNotify は `latest > skipped` で再通知判定する (累積 skip ではない、
        /// = 次の release が出るまで黙る意味)。空文字 / 不在 = skip 履歴なし。
        /// </summary>
        // (#108 Phase 4 round 3 L-5) 判定は `latest != skipped` の厳密一致 (旧 `latest > skipped` から変更、
        // downgrade release 時の「すでに skip 済」誤表示解消)。同 tag のみ通知抑止、新 tag が出れば
        // 通知再開する自然な挙動。
        public const string UpdateSkippedVersion = "update_skipped_version";

        /// <summary>
        /// 直近の GitHub Releases API 呼び出し時刻 (Unix epoch milliseconds)。
        /// `UpdateCheckIntervalHours` を超過したら次回 fetch を許可する。
        /// 0 / 不在 = 一度も check していない (起動直後)。
        /// </summary>
        public const string UpdateCheckLastAtUnixMs = "update_check_last_at_unix_ms";

        /// <summary>
        /// 直近の API check 結果を JSON serialize した文字列 (UpdateCheckResult 相当)。
        /// 起動時に hydrate して UI に「最新版あり / なし」を即時表示する用途。
        /// API 失敗時 / cache 有効 case でも前回 cache を表示し続ける fault-tolerance に使う。
        /// 空文字 / 不在 = cache なし。
        /// </summary>
        public const string UpdateCheckCachedJson = "update_check_cached_json";

        /// <summary>
        /// バックグラウンド check の interval (時間単位、default 6)。学校 LAN で複数 PC が
        /// 同時に GitHub API を叩いて 60 req/hour rate limit に当たるのを mitigate。
        /// 設定値の override は Manager UI からは提供しない (運用上 default 固定で十分)。
        /// </summary>
        public const string UpdateCheckIntervalHours = "update_check_interval_hours";

        public const int DefaultUpdateCheckIntervalHours = 6;

        /// <summary>
        /// (#108 Phase 4 round 2 codex P2) 起動時通知 dialog を出した最後の tag 名。`UpdateAvailable`
        /// 検出時にこの値と比較し、同 tag なら通知 skip (= user が「いいえ」で延期した直後の再起動で
        /// 再度同じ dialog が出る UX を抑制)。新 release が出て tag が変われば自動的に notify 再開。
        /// 空文字 / 不在 = 通知履歴なし (= 通知する)。
        /// </summary>
        public const string UpdateNotifiedTag = "update_notified_tag";
    }
}
