using System;
using System.Collections.Generic;
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
        /// 「このバージョンをスキップ」button からの呼出。`latest` の version 文字列を settings に書き、
        /// 次回 check 以降で `latest > skipped` になるまで通知抑止する。
        /// </summary>
        public void Skip(Version latest)
        {
            if (latest == null) return;
            _settings.SetString(SettingsKeys.UpdateSkippedVersion, latest.ToString(3));
        }

        /// <summary>skip を解除 (UI 上「スキップを解除」ボタンを将来出すなら使う、現状未配置)。</summary>
        public void ClearSkip()
        {
            _settings.SetString(SettingsKeys.UpdateSkippedVersion, string.Empty);
        }

        /// <summary>
        /// `latest > skipped` で通知すべきか判定する。skip が空 / null なら常に通知。
        /// </summary>
        public bool ShouldNotify(Version latest)
        {
            if (latest == null) return false;
            string skipStr = _settings.GetString(SettingsKeys.UpdateSkippedVersion, string.Empty);
            if (string.IsNullOrEmpty(skipStr)) return true;
            Version skipped = ChangelogParser.TryParseTagVersion(skipStr);
            if (skipped == null) return true;
            return latest > skipped;
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

            SaveCache(result);
            _settings.SetInt64(SettingsKeys.UpdateCheckLastAtUnixMs, nowMs);
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
                    Status = dto.Status,
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
                    CumulativeReleases = new List<ReleaseInfo>(), // 累積 list は cache 対象外 (size 抑制、再 fetch で取り直す)
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
                    Status = result.Status,
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
            public UpdateCheckStatus Status { get; set; }
            public string CurrentVer { get; set; }
            public CacheReleaseDto Latest { get; set; }
            public long CheckedAtUnixMs { get; set; }
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
