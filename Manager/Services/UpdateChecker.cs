using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Repositories;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// アップデート check のオーケストレーション層。Phase 4 (#108) で導入。
    ///
    /// 責務:
    ///   - cache (settings テーブル) の hydrate / 永続化
    ///   - GitHubReleaseChecker の呼出と結果統合 (latest + cumulative)
    ///   - skip 判定 (`update_skipped_version` &gt; latest なら通知抑止)
    ///   - TTL 判定 (default 6 時間、`update_check_interval_hours` で override 可能)
    ///   - background check と force refresh の経路分岐
    ///
    /// すべての public method は同一の `UpdateCheckResult` 戻り値型を持ち、失敗 case (NetworkError /
    /// ParseError / UnknownBundle) も含めて UI が一様にハンドリングできる。
    /// </summary>
    internal sealed class UpdateChecker
    {
        private readonly SettingsRepository _settings;

        // (#108 Phase 4 round 1 M4 → round 5 M-3 撤去) 旧実装は process-wide `_settingsWriteLock` で
        // Skip / SaveCache 等の settings 書込みを serialize していたが、SettingsRepository は
        //   - call ごとに `new SQLiteConnection` + ExecuteWithRetry で SQLITE_BUSY retry
        //   - prism.db は WAL モード (PR #103) で multi-writer 安全
        // のため SQLite 側で既に atomic、process-wide lock を被せるのは redundant な二重 lock だった。
        // round 4 L-4 で「read は lock 外で片手落ち」と debt 明示していたが、本 round で SettingsRepository
        // が atomic と確認できたため lock 自体撤去 + 各 method の try/catch + Logger.Warn の error
        // handling は保持 (= SQLite 書込み失敗時の防御は依然必要)。

        public UpdateChecker(SettingsRepository settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
        }

        /// <summary>
        /// 起動時 background check で呼ぶ entry point。cache TTL 内なら HTTP を叩かず cache を返す、
        /// TTL 超過なら API hit。failure 時は cache hit を fall through 表示する fault tolerance。
        /// cache hit 時は現在の Current で **Status を再評価** する (cache 保存時点と現状で Current が
        /// 乖離している可能性あり、`LoadCacheOnly` と同じ理由)。
        /// </summary>
        public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
        {
            UpdateCheckResult cached = TryLoadCache();
            if (cached != null && IsCacheFresh(cached))
            {
                Version current = VersionInventory.ReadBundleVersion();
                cached.FromCache = true;
                cached.Current = current;
                cached.Status = ComputeStatus(current, cached.Latest);
                return cached;
            }
            return await CheckFromApiAsync(ct, fallbackCache: cached).ConfigureAwait(false);
        }

        /// <summary>
        /// 「更新を確認」button から呼ぶ force refresh entry point。cache を無視して必ず API を叩く。
        /// 失敗時は前回 cache を `LastError` 付きで返す (UI 上で「ネットワーク失敗、キャッシュを表示中」)。
        /// </summary>
        public async Task<UpdateCheckResult> ForceRefreshAsync(CancellationToken ct)
        {
            UpdateCheckResult cached = TryLoadCache();
            return await CheckFromApiAsync(ct, fallbackCache: cached).ConfigureAwait(false);
        }

        /// <summary>
        /// 起動時 hydrate 専用 (API 呼ばない)。cache を読んで UI に「前回確認時の状態」を出すのに使う。
        ///
        /// **重要**: cache に保存された Status は「cache 保存時点の Current/Latest 関係」での判定結果なので、
        /// 現在の Current (例: Bundle SoT の配置変更で取得経路が変わった、CHANGELOG が編集された 等)
        /// と乖離している可能性がある。そのため hydrate 後に **現在の Current で Status を必ず再評価** する
        /// (= ComputeStatus に通す)。これをしないと「現在 Bundle が読めているのに status は古い UnknownBundle」
        /// のような矛盾表示が出る (#108 Phase 4 開発中に発見)。
        /// </summary>
        public UpdateCheckResult LoadCacheOnly()
        {
            Version current = VersionInventory.ReadBundleVersion();
            UpdateCheckResult cached = TryLoadCache();
            if (cached != null)
            {
                cached.FromCache = true;
                cached.Current = current;
                cached.Status = ComputeStatus(current, cached.Latest);
                return cached;
            }
            return new UpdateCheckResult
            {
                Status = ComputeStatus(current, null),
                Current = current,
                CheckedAtUnixMs = 0,
                FromCache = false,
            };
        }

        /// <summary>
        /// Current / Latest / skip 設定から Status を判定する SoT。`CheckFromApiAsync` と `LoadCacheOnly`
        /// から共通で使う (== 判定ロジックが 1 箇所、cache hydrate でも API check でも同じ結果に揃う)。
        /// </summary>
        private UpdateCheckStatus ComputeStatus(Version current, ReleaseInfo latest)
        {
            if (current == null)
            {
                return UpdateCheckStatus.UnknownBundle;
            }
            if (latest == null || latest.Version == null || latest.Version <= current)
            {
                return UpdateCheckStatus.UpToDate;
            }
            if (!ShouldNotify(latest.Version))
            {
                return UpdateCheckStatus.Skipped;
            }
            return UpdateCheckStatus.UpdateAvailable;
        }

        /// <summary>
        /// (#108 Phase 4 round 3 M-1 + round 5 M-3) 起動時通知 dialog を出した tag を記録
        /// (`UpdateNotifiedTag`)。MainForm.StartBackgroundUpdateCheckIfDue から呼ぶ。
        /// round 5 M-3 で process-wide lock 撤去 (SettingsRepository atomic 前提)、settings 書込み集約
        /// 自体は API 統一の責務として維持。
        /// </summary>
        public void MarkNotified(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return;
            try
            {
                _settings.SetString(SettingsKeys.UpdateNotifiedTag, tagName);
            }
            catch (Exception ex)
            {
                Logger.Warn("[UpdateChecker] MarkNotified 書込み失敗 (tag=" + tagName + "): " + ex.Message);
            }
        }

        /// <summary>(round 5 M-3) lock 撤去 (SettingsRepository atomic 前提)。</summary>
        public string GetNotifiedTag()
        {
            try
            {
                return _settings.GetString(SettingsKeys.UpdateNotifiedTag, string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Warn("[UpdateChecker] GetNotifiedTag 読込失敗: " + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// 「このバージョンをスキップ」button からの呼出。`latest` の version 文字列を settings に書く。
        /// (#108 Phase 4 round 3 L-5) 次回 check 以降は `latest != skipped` の **厳密一致比較** で通知判定
        /// (= `latest > skipped` だけでなく `latest < skipped` = downgrade release も「同 tag でなければ
        /// 新 release 扱いで通知」、maintainer downgrade 時の自然 UX に倒す、Skipped 判定は厳密一致のみ)。
        /// </summary>
        public void Skip(Version latest)
        {
            if (latest == null) return;
            // (#108 Phase 4 round 1 M4 → round 5 M-3) try/catch + Logger.Warn は保持 (SQLite 書込み失敗時の
            // 防御は依然必要)、process-wide lock は撤去 (SettingsRepository atomic 前提)。
            try
            {
                _settings.SetString(SettingsKeys.UpdateSkippedVersion, latest.ToString(3));
            }
            catch (Exception ex)
            {
                Logger.Warn("[UpdateChecker] Skip 書込み失敗: " + ex.Message);
            }
        }

        /// <summary>skip を解除 (UI 上「スキップを解除」ボタンを将来出すなら使う、現状未配置)。</summary>
        public void ClearSkip()
        {
            try
            {
                _settings.SetString(SettingsKeys.UpdateSkippedVersion, string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Warn("[UpdateChecker] ClearSkip 書込み失敗: " + ex.Message);
            }
        }

        /// <summary>
        /// (#108 Phase 4 round 3 L-5) `latest != skipped` の厳密一致比較で通知すべきか判定する。
        /// skip が空 / null なら常に通知。同一 tag = Skipped 扱い、それ以外 (`latest > skipped` も
        /// `latest < skipped` も) は通知可。downgrade release 時の不自然 UX を解消するため round 3 L-5 で
        /// `latest > skipped` から変更。
        /// </summary>
        public bool ShouldNotify(Version latest)
        {
            if (latest == null) return false;
            string skipStr = _settings.GetString(SettingsKeys.UpdateSkippedVersion, string.Empty);
            if (string.IsNullOrEmpty(skipStr)) return true;
            Version skipped = ChangelogParser.TryParseTagVersion(skipStr);
            if (skipped == null) return true;
            // (#108 Phase 4 round 3 L-5) **厳密一致のみ Skipped 判定** に変更。旧実装は `latest > skipped`
            // (= latest <= skipped で必ず Skipped) だったが、maintainer が release を手動 downgrade (= 後の
            // release tag が v0.4.0 < skipped v0.5.0) した case、user の current=v0.3.0 でも UI が
            // 「v0.4.0 はスキップ済み」と表示する不自然動作があった。`latest == skipped` のみ Skipped 扱い、
            // それ以外 (`latest > skipped` も `latest < skipped` も) は notify 可とする。`latest < skipped`
            // は ComputeStatus 上位で `latest <= current` なら UpToDate に倒れるため、実際に notify される
            // のは「downgrade 後の latest > current」case のみ (= 自然な「新しい release が来た」UX)。
            return !latest.Equals(skipped);
        }

        // ---------- internal ----------

        private async Task<UpdateCheckResult> CheckFromApiAsync(CancellationToken ct, UpdateCheckResult fallbackCache)
        {
            Version current = VersionInventory.ReadBundleVersion();

            ReleaseInfo latest = null;
            IReadOnlyList<ReleaseInfo> cumulative = new List<ReleaseInfo>();
            string lastError = null;
            UpdateCheckStatus errorStatus = UpdateCheckStatus.NetworkError;

            try
            {
                latest = await GitHubReleaseChecker.GetLatestAsync(ct).ConfigureAwait(false);
                if (latest != null && current != null && latest.Version != null && latest.Version > current)
                {
                    cumulative = await GitHubReleaseChecker.GetReleasesBetweenAsync(current, latest.Version, ct)
                        .ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // HttpClient timeout は TaskCanceledException で来るが、user cancel と区別したい。
                lastError = "タイムアウト: " + ex.Message;
                errorStatus = UpdateCheckStatus.NetworkError;
            }
            catch (OperationCanceledException)
            {
                // user cancel (ct から発火、TaskCanceledException の親含む) はそのまま伝播
                throw;
            }
            catch (GitHubRateLimitException ex)
            {
                // rate limit 専用文言: 「GitHub API 利用上限、数時間後にリセット」
                string resetHint = string.Empty;
                if (ex.ResetAt.HasValue)
                {
                    var local = ex.ResetAt.Value.ToLocalTime();
                    resetHint = " (" + local.ToString("HH:mm") + " 頃にリセット)";
                }
                lastError = "GitHub API 利用上限に達しました" + resetHint;
                errorStatus = UpdateCheckStatus.NetworkError;
            }
            catch (GitHubReleaseException ex)
            {
                lastError = ex.Message;
                errorStatus = UpdateCheckStatus.ParseError;
            }
            catch (HttpRequestException ex)
            {
                lastError = "ネットワーク失敗: " + ex.Message;
                errorStatus = UpdateCheckStatus.NetworkError;
            }
            catch (Exception ex)
            {
                lastError = "予期しないエラー: " + ex.Message;
                errorStatus = UpdateCheckStatus.NetworkError;
            }

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 失敗 case (lastError != null):
            //   - fallback cache あり → cache の Latest を保持しつつ、**現在の Current で Status を再評価**
            //     して FromCache=true + LastError を添付。UI は「cache 表示中 + 再確認失敗」を grey の
            //     sub-text 扱いで出す (= cache のデータが見えてるのに「失敗」赤字で alarming にしない)。
            //     cache 保存時点の Status を盲信せず ComputeStatus に通すのは LoadCacheOnly / CheckAsync と
            //     同じ SoT 集約方針 (Current の経路変更や CHANGELOG 編集で乖離する path を防ぐ)。
            //   - fallback cache なし → Status=NetworkError/ParseError、データ無し赤字エラー表示
            if (lastError != null)
            {
                if (fallbackCache != null)
                {
                    fallbackCache.FromCache = true;
                    fallbackCache.LastError = lastError;
                    fallbackCache.Current = current;
                    fallbackCache.Status = ComputeStatus(current, fallbackCache.Latest);
                    return fallbackCache;
                }
                return new UpdateCheckResult
                {
                    Status = errorStatus,
                    Current = current,
                    Latest = null,
                    CumulativeReleases = new List<ReleaseInfo>(),
                    CheckedAtUnixMs = nowMs,
                    FromCache = false,
                    LastError = lastError,
                };
            }

            var result = new UpdateCheckResult
            {
                Status = ComputeStatus(current, latest),
                Current = current,
                Latest = latest,
                CumulativeReleases = cumulative,
                CheckedAtUnixMs = nowMs,
                FromCache = false,
            };

            // (#108 Phase 4 round 1 M4 → round 5 M-3) lock 撤去 (SettingsRepository atomic 前提)。
            SaveCache(result);
            try
            {
                _settings.SetInt64(SettingsKeys.UpdateCheckLastAtUnixMs, nowMs);
            }
            catch (Exception ex)
            {
                Logger.Warn("[UpdateChecker] UpdateCheckLastAtUnixMs 書込み失敗: " + ex.Message);
            }
            return result;
        }

        private UpdateCheckResult TryLoadCache()
        {
            try
            {
                string json = _settings.GetString(SettingsKeys.UpdateCheckCachedJson, null);
                if (string.IsNullOrEmpty(json)) return null;
                var ser = new JavaScriptSerializer();
                var dto = ser.Deserialize<CacheDto>(json);
                if (dto == null) return null;
                return new UpdateCheckResult
                {
                    // (#108 Phase 4 round 6 L-5) Status は ComputeStatus が caller 側で必ず上書きする
                    // (LoadCacheOnly / CheckAsync) ため、ここでは default (UpToDate) で OK。
                    Current = TryParse(dto.CurrentVer),
                    Latest = dto.Latest == null ? null : new ReleaseInfo
                    {
                        TagName = dto.Latest.TagName,
                        Version = TryParse(dto.Latest.TagName),
                        Body = dto.Latest.Body,
                        HtmlUrl = dto.Latest.HtmlUrl,
                        ZipAssetUrl = dto.Latest.ZipAssetUrl,
                        ZipSizeBytes = dto.Latest.ZipSizeBytes,
                        PublishedAt = string.IsNullOrEmpty(dto.Latest.PublishedAt) ? (DateTimeOffset?)null
                            : DateTimeOffset.Parse(dto.Latest.PublishedAt, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind),
                    },
                    // (#108 Phase 4 round 5 M-2) cache から CumulativeReleases も hydrate。
                    // null fallback で旧 cache schema (Cumulative 不在) との互換も維持。
                    CumulativeReleases = dto.Cumulative == null ? new List<ReleaseInfo>() : dto.Cumulative
                        .Select(c => new ReleaseInfo
                        {
                            TagName = c.TagName,
                            Version = TryParse(c.TagName),
                            Body = c.Body,
                            HtmlUrl = c.HtmlUrl,
                            ZipAssetUrl = c.ZipAssetUrl,
                            ZipSizeBytes = c.ZipSizeBytes,
                            PublishedAt = string.IsNullOrEmpty(c.PublishedAt) ? (DateTimeOffset?)null
                                : DateTimeOffset.Parse(c.PublishedAt, System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.RoundtripKind),
                        }).ToList<ReleaseInfo>(),
                    CheckedAtUnixMs = dto.CheckedAtUnixMs,
                };
            }
            catch
            {
                return null;
            }
        }

        private void SaveCache(UpdateCheckResult result)
        {
            try
            {
                var dto = new CacheDto
                {
                    // (#108 Phase 4 round 6 L-5) Status は dead field のため Save しない (上記
                    // CacheDto の docstring 参照)。
                    CurrentVer = result.Current == null ? null : result.Current.ToString(3),
                    CheckedAtUnixMs = result.CheckedAtUnixMs,
                    Latest = result.Latest == null ? null : new CacheReleaseDto
                    {
                        TagName = result.Latest.TagName,
                        Body = result.Latest.Body,
                        HtmlUrl = result.Latest.HtmlUrl,
                        ZipAssetUrl = result.Latest.ZipAssetUrl,
                        ZipSizeBytes = result.Latest.ZipSizeBytes,
                        PublishedAt = result.Latest.PublishedAt.HasValue
                            ? result.Latest.PublishedAt.Value.ToString("o")
                            : null,
                    },
                    // (#108 Phase 4 round 5 M-2) CumulativeReleases も serialize。
                    Cumulative = result.CumulativeReleases == null ? null : result.CumulativeReleases
                        .Select(r => new CacheReleaseDto
                        {
                            TagName = r.TagName,
                            Body = r.Body,
                            HtmlUrl = r.HtmlUrl,
                            ZipAssetUrl = r.ZipAssetUrl,
                            ZipSizeBytes = r.ZipSizeBytes,
                            PublishedAt = r.PublishedAt.HasValue
                                ? r.PublishedAt.Value.ToString("o")
                                : null,
                        }).ToList(),
                };
                var ser = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                string json = ser.Serialize(dto);
                _settings.SetString(SettingsKeys.UpdateCheckCachedJson, json);
            }
            catch
            {
                // cache 失敗は致命ではない、無視
            }
        }

        private bool IsCacheFresh(UpdateCheckResult cached)
        {
            int hours = _settings.GetInt32(SettingsKeys.UpdateCheckIntervalHours,
                SettingsKeys.DefaultUpdateCheckIntervalHours);
            if (hours <= 0) hours = SettingsKeys.DefaultUpdateCheckIntervalHours;
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long elapsedMs = nowMs - cached.CheckedAtUnixMs;
            long ttlMs = (long)hours * 3600L * 1000L;
            return elapsedMs >= 0 && elapsedMs < ttlMs;
        }

        private static Version TryParse(string s)
        {
            return ChangelogParser.TryParseTagVersion(s);
        }

        // cache serialization 用 DTO (System.Version は JavaScriptSerializer がうまく扱えないので string 経由)
        private sealed class CacheDto
        {
            // (#108 Phase 4 round 6 L-5) Status field 削除。SaveCache で書いて TryLoadCache で読んでも
            // 全 hydrate 経路 (LoadCacheOnly / CheckAsync / CheckFromApiAsync) で
            // `ComputeStatus(current, cached.Latest)` で必ず上書きされる dead data だった。
            // SoT を ComputeStatus に集約 + cache size 微減 (round 5 M-2 Cumulative 追加と一緒に sweep)。
            public string CurrentVer { get; set; }
            public CacheReleaseDto Latest { get; set; }
            public long CheckedAtUnixMs { get; set; }
            // (#108 Phase 4 round 5 M-2) CumulativeReleases も cache。旧設計は「size 抑制」で空 list
            // hydrate していたが、fresh cache 経路 (= cache TTL 6h 以内の再起動) で UpdateAvailable / Skipped
            // 表示時に release notes UI が「現在実行中: vLATEST」誤誘導する path があった。1 release
            // あたり数 KB、典型 30 release で 100KB 程度、SQLite K/V に十分入る size。
            public List<CacheReleaseDto> Cumulative { get; set; }
        }
        private sealed class CacheReleaseDto
        {
            public string TagName { get; set; }
            public string Body { get; set; }
            public string HtmlUrl { get; set; }
            public string ZipAssetUrl { get; set; }
            public long ZipSizeBytes { get; set; }
            public string PublishedAt { get; set; }
        }
    }
}
