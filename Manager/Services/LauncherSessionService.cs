using System;
using System.Collections.Generic;
using System.IO;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#179 PR3b) Launcher の LAN-wide session を JSON drop folder 経由で検出する service。
    ///
    /// **責務**: `&lt;base&gt;/responses/launcher_sessions/&lt;pc_name&gt;.json` を on-demand polling で
    /// 読込、stale (= `now - max(json_heartbeat, file_mtime) &gt;= 60000 ms`、#269) を除外した active
    /// session list を返す。`ManagerSessionService` (DB-based) と非対称の polling-only 設計。
    ///
    /// **動作**:
    /// - `Initialize()`: directory 不在時の自動作成 + `_initialized = true`
    /// - `DetectActiveLauncherSessions()`: 検出 trigger (= Manager 起動時 + 編集操作前) 呼出ごとに
    ///   directory を re-scan、各 JSON file を parse して stale 除外
    /// - `Shutdown()`: no-op (polling-only、Manager 側 self file なし、disk 上 Launcher session file は
    ///   Launcher 自身の責務 = `Launcher/scripts/session_heartbeat.gd` の `NOTIFICATION_PREDELETE`)
    ///
    /// **stale 判定 baseline** (SPEC §3.8.7.3、#269 で clock drift 耐性化):
    /// - **primary**: `max(JSON last_heartbeat_at_unix_ms, file mtime)` の **新しい方** で判定。json は
    ///   書込側 Launcher の自己申告時計 (PC 間 clock drift を被る)、mtime は SMB サーバの単一時計
    ///   (cache 遅延 ~10 秒を被る) で、**互いの弱点を補完** (どちらか fresh なら active)。
    /// - **fallback** (JSON parse 失敗 / field 欠落): file mtime 単独で判定。
    /// - 閾値 60 秒 (= heartbeat 10 秒 ×6)。残留リスク = 読み手 PC 自身の時計が大幅に進む単機ケースのみ
    ///   (両者とも古く見え過小警告)、運用での NTP 同期 + 安全側閾値で許容。
    ///
    /// **fail-soft 戦略**:
    /// - directory 不在: 自動作成、失敗時は `Logger.Error` trail + `_initialized = false`
    /// - DetectActiveLauncherSessions の SMB エラー全般: 空 list 返却 + `Logger.Warn`
    /// - 個別 file の JSON parse 失敗: skip + `Logger.Warn`、他 file の検出は継続
    ///
    /// **thread safety**: 共有可変状態は `_initialized` flag (bool) のみで、`DetectActiveLauncherSessions` は毎回
    /// fresh scan (= polling 結果の cache なし) ＝**並行 read 安全**。当初は UI thread 専用だったが、ダッシュボードが
    /// 背景スレッド (`Task.Run`) から周期的に呼ぶ (= UI thread の編集前 check と同時実行されうる)。今後ここに
    /// 非スレッドセーフな cache 等を足す場合は同期が要る点に注意。
    ///
    /// **同 PC = Manager 起動 PC 上の Launcher も検出に含める** (SPEC §3.8.7.6): 自 PC 検出を冗長と
    /// 見なさず dialog 表示、file lock 競合は同 PC でも発生するため安全側設計。
    ///
    /// 詳細仕様: SPECIFICATION.md §3.8.7 参照。
    /// </summary>
    public class LauncherSessionService
    {
        // (#269) clock drift 耐性のため 30→60 秒に緩和。stale 判定は「読み手 now − 検出 timestamp」で
        // PC 間の時計ズレを被るため、heartbeat 10 秒 ×6 のマージンを取り、想定 skew を閾値で吸収する。
        // 広げる方向は安全側 (= 死んだ session が長めに active 表示 = 過剰警告) で、データ事故方向には倒れない。
        private const int StaleTimeoutSeconds = 60;

        private readonly string _sessionsFolder;
        private bool _initialized;

        public LauncherSessionService(string sessionsFolder)
        {
            _sessionsFolder = sessionsFolder;
        }

        /// <summary>
        /// caller (`MainForm.CheckSessionConflictBeforeWrite`) が「service が機能可能 state か」を
        /// 判定するための flag。`ManagerSessionService.IsInitialized` と対称、Initialize 失敗時は
        /// false のまま (= fail-soft で空 list 返却 path に倒れる)。
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// 起動時に呼ぶ。directory 不在時は自動作成、失敗時は `_initialized = false` で fail-soft。
        /// Manager 単独起動時 (= まだ Launcher 起動なし) でも directory を先に作って Listing 例外を
        /// 予防する。
        /// </summary>
        public void Initialize()
        {
            if (_initialized)
            {
                Logger.Warn("[LauncherSessionService] Initialize 多重 call、no-op");
                return;
            }
            try
            {
                if (!Directory.Exists(_sessionsFolder))
                {
                    Directory.CreateDirectory(_sessionsFolder);
                    Logger.Info("[LauncherSessionService] sessions directory 作成: " + _sessionsFolder);
                }
                _initialized = true;
                Logger.Info("[LauncherSessionService] Initialize 完了: folder=" + _sessionsFolder);
            }
            catch (Exception ex)
            {
                // directory 作成失敗 = SMB 到達不能 / 権限不足等の致命的 path、検出機能 disable で fail-soft
                // (Manager 自体は起動継続、`DetectActiveLauncherSessions` は空 list 返却に倒れる)
                Logger.Error("[LauncherSessionService] Initialize 失敗 (検出機能 disable、Manager は継続)", ex);
            }
        }

        /// <summary>
        /// polling-only service なので shutdown は no-op (= self row / self file なし)。
        /// `ManagerSessionService.Shutdown` と対称化のため API は維持。
        /// </summary>
        public void Shutdown()
        {
            _initialized = false;
        }

        /// <summary>
        /// on-demand polling で `&lt;sessionsFolder&gt;/*.json` を re-scan、stale を除外した active session
        /// list を返す。検出 trigger (= Manager 起動時 + 編集操作前、SPEC §3.8.7.4) 呼出ごとに fresh
        /// scan する設計 (= 周期 polling Timer なし)。
        ///
        /// **method 名の非対称性** (round 2 L-1): Manager 側は `DetectOtherActiveSessions` (= self
        /// 除外を name 上で示す) だが、本 method は `Detect` **Active** (= "Other" なし)。これは
        /// SPEC §3.8.7.6 の「同 PC = Manager 起動 PC 上の Launcher も検出に含める」設計判断による
        /// 意図的命名 (= file lock 競合は同 PC でも発生するため安全側設計、user は dialog で自 PC
        /// Launcher と分かれば閉じれば済む)。除外 option は別 PR 余地。
        ///
        /// 戻り値: 0 件以上の active Launcher session (= 自 PC Launcher も含む)、SMB エラー時は空 list
        /// で fail-soft。
        /// </summary>
        public IReadOnlyList<LauncherSessionInfo> DetectActiveLauncherSessions()
        {
            if (!_initialized) return new List<LauncherSessionInfo>();

            try
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long staleThresholdMs = nowMs - StaleTimeoutSeconds * 1000L;
                var result = new List<LauncherSessionInfo>();

                if (!Directory.Exists(_sessionsFolder))
                {
                    // Initialize 後に SMB 切断 / dir 削除等の race、空 list で fail-soft
                    return result;
                }

                foreach (var path in Directory.EnumerateFiles(_sessionsFolder, "*.json"))
                {
                    // (#179 PR3b round 2 M-2) Windows 8.3 short-name 有効時に `*.json` glob が `.json.tmp`
                    // にも match する quirk があり、Launcher atomic write の rename 中断 (= crash 等で
                    // `.tmp` 残置) で `pc-a.json.tmp` が誤検出され dialog list に「pc-a.json」が表示
                    // される drift path。明示 extension check で defensive filter する。
                    if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var info = TryParseSessionFile(path, staleThresholdMs);
                    if (info != null) result.Add(info);
                }
                return result;
            }
            catch (Exception ex)
            {
                // SMB 一時不到達 / Enumeration 失敗等、空 list 返却で fail-soft
                Logger.Warn("[LauncherSessionService] DetectActiveLauncherSessions 失敗 (空 list で fallback): " + ex.GetType().Name + ": " + ex.Message);
                return new List<LauncherSessionInfo>();
            }
        }

        /// <summary>
        /// 単一 JSON file を読込 + parse + stale 判定。
        ///
        /// **stale 判定の 3 経路** (round 2 M-3 で field 欠落 と parse 失敗 を区別):
        /// - **primary**: JSON 内 `last_heartbeat_at_unix_ms` を `long.TryParse` 成功で stale 判定
        /// - **fallback (= field 欠落 or `long.TryParse` 失敗)**: file mtime で stale 判定 (= JSON 内
        ///   科学記法 `1.7e+12` 等の数値 corruption で `long.TryParse` が false 返却するケースも mtime
        ///   fallback に倒れる、silent 0 で stale 扱いされる drift を予防)
        /// - **完全 parse 失敗** (JSON 構文エラー / SMB read エラー / file 削除 race): null 返却 (=
        ///   individual file skip、Warn log で trail、他 file の検出は継続)
        /// </summary>
        private LauncherSessionInfo TryParseSessionFile(string path, long staleThresholdMs)
        {
            try
            {
                string text = File.ReadAllText(path);
                var dict = JsonCompat.DeserializeToObjectTree(text) as Dictionary<string, object>;
                if (dict == null) return null;

                // (round 2 M-3) primary path: last_heartbeat_at_unix_ms を long.TryParse 成功で確定。
                // 失敗 (= field 欠落 / 値が 0 でない 0 / 科学記法 etc.) は fallback path に流す。
                // C# の definite assignment 制約 + && short-circuit を考慮し、初期値 0 で declare。
                long lastHeartbeat = 0;
                bool hasHeartbeatField = dict.ContainsKey("last_heartbeat_at_unix_ms");
                bool hasHeartbeat = hasHeartbeatField &&
                    long.TryParse(dict["last_heartbeat_at_unix_ms"]?.ToString() ?? "0", out lastHeartbeat);

                if (!hasHeartbeat)
                {
                    // (round 2 M-3) parse 失敗 (field あるが long として読めない) と field 欠落を区別、
                    // 前者は Warn log で trail を残してから mtime fallback (silent 0 で stale 扱いされる
                    // drift を予防)。後者は素直に mtime fallback。
                    if (hasHeartbeatField)
                    {
                        Logger.Warn("[LauncherSessionService] session file parse 警告 (last_heartbeat_at_unix_ms が long parse 不能、mtime fallback): path=" + path + " raw=" + (dict["last_heartbeat_at_unix_ms"]?.ToString() ?? "(null)"));
                    }
                    // fallback: file mtime で stale 判定
                    long mtimeMs = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
                    if (mtimeMs < staleThresholdMs) return null;
                    // (round 3 L-1) fallback path でも dict から pc_name / launcher_version / started_at /
                    // pid を best-effort 読込。dict 自体は parse 成功している (= 上の null check 通過) ので、
                    // 個別 field の欠落のみが fallback path に倒れた case で、有効 field は活用する設計に変更。
                    // 欠落時のみ primary path と同じ fallback 値 (filename / "(unknown)" / 0) を採用。
                    return new LauncherSessionInfo
                    {
                        PcName = dict.ContainsKey("pc_name") ? (dict["pc_name"]?.ToString() ?? Path.GetFileNameWithoutExtension(path)) : Path.GetFileNameWithoutExtension(path),
                        StartedAtUnixMs = ParseLongOrZero(dict, "started_at_unix_ms"),
                        LastHeartbeatAtUnixMs = mtimeMs,
                        Pid = ParseLongOrZero(dict, "pid"),
                        LauncherVersion = dict.ContainsKey("launcher_version") ? (dict["launcher_version"]?.ToString() ?? "(version 不明)") : "(version 不明)",
                    };
                }

                // primary path: last_heartbeat_at_unix_ms parse 成功。
                // (#269) clock drift 耐性: json timestamp (= 書き手 Launcher の自己申告時計) と file mtime
                // (= SMB サーバの単一時計) の **新しい方 (max)** で stale 判定する。これで両方向を吸収:
                //   - 書き手 PC の時計が遅れ → json が古く見えても mtime (サーバ時計) が救う (drift 対策)
                //   - mtime の SMB directory cache 遅延 (~10 秒、§6.5) → json が救う (元々 json を primary に
                //     した理由も維持)
                // = どちらか一方でも fresh なら active。残留リスクは「読み手 PC 自身の時計が大幅に進んでいる」
                // 単機ケースのみ (両者とも古く見え過小警告) で、閾値 60 秒 + 運用での NTP 同期で許容する。
                // 表示用 LastHeartbeatAtUnixMs にも effective (= max) を入れ、active 判定と「最終確認 N 秒前」
                // 表示を一致させる (mtime はサーバ時計なので self-report より信頼できる "最終確認" でもある)。
                //
                // (#269 review #1) **前提 (要実機検証)**: 本 mitigation は file mtime が SMB **サーバ**の時計で
                // 刻まれる構成に依存する。Launcher 書込側は timestamp を明示設定しない (確認済: session_heartbeat.gd
                // は store_string → rename_absolute のみ、set_modified_time 等なし) ため、default SMB ではサーバが
                // mtime を刻む = 前提成立。ただしサーバ構成によっては書き手クライアント時計を反映しうるため、実機
                // SMB での確認が必要 (SPEC §3.8.7.3 F-1)。前提が崩れても max は fresh 方向のみ = 安全側で回帰はしない
                // (= drift 防御が効かなくなるだけで、新たな過小警告は生まない)。
                // (#269 review #4) json が parse 成功の "0" でも max(0, mtime) = mtime に倒れ mtime 判定になる
                // (旧: 0 < threshold で常に stale 除外)。壊れた 0 値を mtime で救う安全方向の挙動変化。
                // (#270 review #3) 防御の分担: max が無効化するのは **書き手側** の時計ズレ。**読み手自身** の
                // ズレ (= 読み手 nowMs が真値より進む) は json/mtime 双方が読み手 threshold に対し古く見え max では
                // 救えず、60 秒閾値 + NTP 運用が吸収する。
                // (#270 review #2) max が mtime を採るため、死んだ Launcher の残骸 file の mtime を外部プロセス
                // (バックアップ / 同期 / 再 stamp 等) が touch すると json が古くても false-active が続きうる
                // (過剰警告 = 安全側。次回同 PC で Launcher 起動時に同名 file 上書きで解消。AV の read は mtime を
                // 変えないため通常無関係)。
                long mtimePrimaryMs = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
                long effectiveHeartbeatMs = Math.Max(lastHeartbeat, mtimePrimaryMs);
                if (effectiveHeartbeatMs < staleThresholdMs) return null;

                return new LauncherSessionInfo
                {
                    PcName = dict.ContainsKey("pc_name") ? (dict["pc_name"]?.ToString() ?? "(unknown)") : "(unknown)",
                    StartedAtUnixMs = ParseLongOrZero(dict, "started_at_unix_ms"),
                    LastHeartbeatAtUnixMs = effectiveHeartbeatMs,
                    Pid = ParseLongOrZero(dict, "pid"),
                    LauncherVersion = dict.ContainsKey("launcher_version") ? (dict["launcher_version"]?.ToString() ?? "(unknown)") : "(unknown)",
                };
            }
            catch (Exception ex)
            {
                // JSON parse 失敗 / SMB read 失敗 / file 削除 race 等、individual file は skip
                Logger.Warn("[LauncherSessionService] session file parse 失敗 (skip): path=" + path + " err=" + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static long ParseLongOrZero(Dictionary<string, object> dict, string key)
        {
            if (!dict.ContainsKey(key)) return 0;
            long v;
            return long.TryParse(dict[key]?.ToString() ?? "0", out v) ? v : 0;
        }
    }
}
