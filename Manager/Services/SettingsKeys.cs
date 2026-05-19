namespace TonePrism.Manager.Services
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
        // および backup 設定系 (`backup_destination_path` / `backup_auto_interval_hours` /
        // `backup_retention_count`) で使う key。既存実装に合わせて参照しない (本クラスで
        // ラップしない方が既存コードを書き換える scope creep を防げる)。
        // (#170 followup) 旧 BackupSettingsForm modal は廃止、設定タブ内 grpBackup section に inline 統合
        // (SettingsSectionPanel.LoadBackupSettings + grpBackup の per-control event handler 群:
        //  TxtBackupDest_Leave / NumBackupInterval_ValueChanged / CmbBackupIntervalUnit_SelectedIndexChanged
        //  / NumBackupRetention_ValueChanged / ChkBackupAutoEnabled_CheckedChanged)。
        //  round 1 で「保存ボタン廃止 + per-control immediate save」に方針転換、btnBackupSave は実体なし。

        // ----- Phase 4 (#108) update flow -----

        /// <summary>
        /// 「このバージョンをスキップ」で記録される Bundle version (例: "0.3.0")。
        /// (#108 Phase 4 round 3 L-5 / round 6 H-1 docstring 反映) UpdateChecker.ShouldNotify は
        /// `latest != skipped` の **厳密一致比較** で再通知判定する (同 tag のみ通知抑止、新 tag が
        /// 出れば通知再開)。`latest < skipped` の downgrade release も「同 tag でなければ新 release
        /// 扱い」で通知する自然な挙動。空文字 / 不在 = skip 履歴なし。
        ///
        /// 旧 docstring は `latest > skipped` の累積 skip semantic を主張していたが round 3 L-5 で実装
        /// 変更済、本 round 6 H-1 で XML doc を実装と同期 (IntelliSense / hover tooltip が authoritative
        /// な契約として読まれるため non-XML コメントだけでは不十分)。
        /// </summary>
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

        // ----- (#170 followup) Logger retention / destination -----

        /// <summary>
        /// ログファイル保存日数 (default 30)。`Logger.CleanupOldLogs(days)` が起動時 1 回読込み、
        /// 該当日数より古い `manager_*.log` を mtime 基準で削除。設定 UI (SettingsSectionPanel の
        /// grpLog) から変更可、**反映は次回 Manager 起動時** (= 設定 UI 上に明示ラベル)。
        /// 旧実装は `Logger.cs:RetentionDays = 30` の hardcode、本 followup で K/V 化。
        /// </summary>
        public const string LogRetentionDays = "log_retention_days";

        public const int DefaultLogRetentionDays = 30;

        /// <summary>
        /// (#170 followup round 1) ログファイル保存先 directory の絶対 path (空文字 / 不在 = default の
        /// `&lt;project_root&gt;/logs/manager/` を使用)。設定 UI から変更可、**反映は次回 Manager 起動時**。
        /// バックアップ保存先 (`backup_destination_path`) と同 semantic、Program.Main で Logger.Initialize 前に
        /// SQLite 直接 read で取り出して Logger に渡す (= Logger は SettingsRepository に依存しない invariant を維持)。
        /// </summary>
        public const string LogDestinationPath = "log_destination_path";

        // ----- (#170 followup round 2) 自動バックアップの有効/無効 -----

        /// <summary>
        /// 自動バックアップを実行するかどうか ("true"/"false")。default "true" (= 有効、既存挙動)。
        /// UI 設定タブ「バックアップ」section の checkbox から変更可、UI 上で OFF にすると自動 backup の
        /// 起動時 trigger を完全に skip する (= 手動バックアップは引き続き使える)。
        ///
        /// (#170 followup round 2 review L-2) **比較は `"false"` 厳密一致 (case-insensitive)、それ以外
        /// (空 / "true" / unknown / "1" / "0" 等) はすべて enabled 扱い**。手動 DB 編集で誤った値を入れても
        /// default "true" semantic に safely fall through する設計、UI からは必ず "true"/"false" のみ書込まれる。
        /// </summary>
        public const string BackupAutoEnabled = "backup_auto_enabled";

        // ----- (#170 followup round 1) バックアップ自動間隔の表示単位 -----

        /// <summary>
        /// 自動バックアップ間隔の表示単位。"hours" or "days"。default "hours"。UI ComboBox で選択、
        /// **DB に保存される間隔値 (`backup_auto_interval_hours`) は常に時間単位**で BackupService 既存実装と互換、
        /// 本 key は UI 側の表示ヒントのみで runtime logic には影響しない。
        /// </summary>
        public const string BackupAutoIntervalUnit = "backup_auto_interval_unit";

        public const string BackupAutoIntervalUnitHours = "hours";
        public const string BackupAutoIntervalUnitDays = "days";
    }
}
