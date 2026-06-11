using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// GitHub Releases API (`https://api.github.com/repos/<owner>/<repo>/releases/...`) のクライアント。
    ///
    /// Phase 4 (#108) で導入。Manager UI が:
    ///   - `GetLatestAsync()`: タブ初期表示で「最新版」を取得 (`/releases/latest`、prerelease 自動除外)
    ///   - `GetReleasesBetweenAsync(current, latest)`: 累積更新ノート用に `/releases?per_page=N` を 1 リクエスト
    ///                                                  で取得、`current &lt; tag &le; latest` で filter
    /// を叩く。`prerelease=true` / `draft=true` の release は常に除外。
    ///
    /// HttpClient は static 1 インスタンス (.NET HttpClient lifetime ベストプラクティス、socket exhaustion 防止)。
    /// TLS 1.2 を defensive に明示設定 (Win10/11 default は 1.2 だが、古い設定の PC で 1.0/1.1 にロックされている
    /// 場合の保険)。
    /// </summary>
    internal static class GitHubReleaseChecker
    {
        // 学校サーバー / 校内 PC からも見える public repo。固定で hardcode。
        // 将来 fork や transfer がある場合は本 const を変更するだけで対応可能。
        public const string Owner = "ken1208git";
        public const string Repo = "TonePrism";

        private const string ApiBase = "https://api.github.com";
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

        // 累積更新ノートで取得する最大 release 数。学校 LAN 規模で v0.2 → v0.5 のような小さな
        // ジャンプを想定すれば 30 で十分。30 を超える release を一度に飛び越える運用は実害ゼロでも
        // pagination を入れるほどの value がないため、現状 1 リクエスト固定。
        public const int ReleasesPageSize = 30;

        private static readonly Lazy<HttpClient> _client = new Lazy<HttpClient>(CreateClient);

        private static HttpClient CreateClient()
        {
            // (#108 Phase 4 round 3 L-1) SecurityProtocol 設定は AppDomain global で他 HttpClient consumer
            // (BackupService 等) の動作と隠れ結合するため Program.cs の起動時 1 回設定に移動。本 method
            // は HttpClient instance 作成のみに専念。

            var client = new HttpClient
            {
                Timeout = RequestTimeout,
            };
            string ver = "0.0.0";
            try
            {
                ver = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            }
            catch { }
            // GitHub API は UA header を必須要求する。`Manager` ascii で UA を構成。
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TonePrism-Manager", ver));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        /// <summary>
        /// `/releases/latest` を叩いて最新の **非 prerelease / 非 draft** release を返す。GitHub 仕様により
        /// `/latest` endpoint は prerelease / draft を自動除外する。null = 該当 release なし。
        /// 403 rate limit 時は <see cref="GitHubRateLimitException"/> を throw する (UI 側で「数時間後に
        /// リセット」専用文言を出すため)。
        /// </summary>
        public static async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct)
        {
            string url = string.Format("{0}/repos/{1}/{2}/releases/latest", ApiBase, Owner, Repo);
            using (var resp = await _client.Value.GetAsync(url, ct).ConfigureAwait(false))
            {
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    // 新規 repo で release が 1 つもない / private repo に切り替わった 等
                    return null;
                }
                ThrowIfRateLimited(resp);
                resp.EnsureSuccessStatusCode();
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return ParseRelease(body);
            }
        }

        /// <summary>
        /// `/releases?per_page=N` を叩いて release list を取得し、`current &lt; tag &le; latest` で filter する。
        /// 新しい順 (= API の natural order、published_at desc) で返す。prerelease / draft は除外。
        /// `current` が null なら下限なし、`latest` が null なら上限なし。
        /// </summary>
        public static async Task<IReadOnlyList<ReleaseInfo>> GetReleasesBetweenAsync(Version current, Version latest, CancellationToken ct)
        {
            string url = string.Format("{0}/repos/{1}/{2}/releases?per_page={3}", ApiBase, Owner, Repo, ReleasesPageSize);
            using (var resp = await _client.Value.GetAsync(url, ct).ConfigureAwait(false))
            {
                ThrowIfRateLimited(resp);
                resp.EnsureSuccessStatusCode();
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var all = ParseReleaseArray(body);
                var filtered = new List<ReleaseInfo>();
                foreach (var r in all)
                {
                    if (r.IsPrerelease || r.IsDraft) continue;
                    if (r.Version == null) continue;
                    if (current != null && r.Version <= current) continue;
                    if (latest != null && r.Version > latest) continue;
                    filtered.Add(r);
                }
                // (#108 Phase 4 round 8 L-5) 旧実装は API natural order (published_at desc) を信頼して
                // 返却していたが、GitHub API spec 変更 / pagination cursor 変更で順序が異なった場合に
                // 累積表示 (`MarkdownRenderer.BuildCumulativeHtml`) が予期せぬ順序になる risk があった。
                // Manager 側で Version desc に明示 sort して SoT 化、API 順序非依存に。
                // System.Version は IComparable で SemVer 3-part numeric 順比較、in-place List.Sort で
                // allocation 削減 (LINQ OrderByDescending より microopt)。
                filtered.Sort((a, b) => b.Version.CompareTo(a.Version));
                return filtered;
            }
        }

        /// <summary>
        /// `release` 単体の JSON 文字列を ReleaseInfo に変換。
        /// 失敗時は ChangelogCheckerException を throw する (network 経路から分離して扱う)。
        /// </summary>
        public static ReleaseInfo ParseRelease(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new GitHubReleaseException("空のレスポンスを受信しました");
            try
            {
                var dict = JsonCompat.DeserializeToObjectTree(json) as Dictionary<string, object>;
                if (dict == null) throw new GitHubReleaseException("レスポンスが JSON オブジェクトではありません");
                return BuildFromDict(dict);
            }
            catch (GitHubReleaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GitHubReleaseException("レスポンス JSON の解析に失敗しました: " + ex.Message, ex);
            }
        }

        /// <summary>release の配列形式 JSON を ReleaseInfo[] に変換。</summary>
        public static IReadOnlyList<ReleaseInfo> ParseReleaseArray(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new GitHubReleaseException("空のレスポンスを受信しました");
            try
            {
                var arr = JsonCompat.DeserializeToObjectTree(json) as object[];
                if (arr == null) throw new GitHubReleaseException("レスポンスが JSON 配列ではありません");
                var list = new List<ReleaseInfo>(arr.Length);
                foreach (var item in arr)
                {
                    var dict = item as Dictionary<string, object>;
                    if (dict == null) continue;
                    var info = BuildFromDict(dict);
                    if (info != null) list.Add(info);
                }
                return list;
            }
            catch (GitHubReleaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GitHubReleaseException("レスポンス JSON 配列の解析に失敗しました: " + ex.Message, ex);
            }
        }

        private static ReleaseInfo BuildFromDict(Dictionary<string, object> dict)
        {
            if (dict == null) return null;
            string tag = AsString(dict, "tag_name");
            if (string.IsNullOrEmpty(tag)) return null;

            var info = new ReleaseInfo
            {
                TagName = tag,
                Version = ChangelogParser.TryParseTagVersion(tag),
                Body = AsString(dict, "body"),
                HtmlUrl = AsString(dict, "html_url"),
                IsPrerelease = AsBool(dict, "prerelease"),
                IsDraft = AsBool(dict, "draft"),
            };

            string publishedAtStr = AsString(dict, "published_at");
            DateTimeOffset publishedAt;
            if (!string.IsNullOrEmpty(publishedAtStr)
                && DateTimeOffset.TryParse(publishedAtStr, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out publishedAt))
            {
                info.PublishedAt = publishedAt;
            }

            // assets[] から `TonePrism_v<X.Y.Z>.zip` を探す。
            //
            // JavaScriptSerializer の nested array 表現は .NET version / context で `object[]` /
            // `ArrayList` / `List<object>` のいずれかになる仕様揺らぎがある (実機検証で
            // `Deserialize<Dictionary<string, object>>` 経由の nested array が `ArrayList` で
            // 返るケースを確認、#108 Phase 4 debug)。同じく nested object も `Dictionary<string, object>`
            // ではなく非 generic `IDictionary` (Hashtable / OrderedDictionary 等) で返る変種に
            // 備える。両方 IEnumerable / IDictionary で defensive に拾う。
            // (#108 Phase 4 round 3 L-2) round 1 で「Phase 4 debug 用」として仕込んだ verbose Info 6 行を
            // 削除 (assets type / asset[N] type / name 等を release fetch 毎に出力していた production
            // noise)。Warning / matched ログのみ残置で、parse 失敗時の診断には十分。
            object assetsObj;
            if (dict.TryGetValue("assets", out assetsObj) && assetsObj != null)
            {
                var assetEnumerable = assetsObj as System.Collections.IEnumerable;
                if (assetEnumerable != null && !(assetsObj is string))
                {
                    int idx = 0;
                    foreach (var a in assetEnumerable)
                    {
                        idx++;
                        var assetDict = ToStringObjectDict(a);
                        if (assetDict == null)
                        {
                            Logger.Warn("[GitHubReleaseChecker]   asset[" + idx + "] ToStringObjectDict returned null, skip");
                            continue;
                        }
                        string name = AsString(assetDict, "name");
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!name.StartsWith("TonePrism_v", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        info.ZipAssetUrl = AsString(assetDict, "browser_download_url");
                        info.ZipSizeBytes = AsLong(assetDict, "size");
                        Logger.Info("[GitHubReleaseChecker]   matched: " + name + " size=" + info.ZipSizeBytes);
                        break;
                    }
                    if (idx == 0)
                    {
                        Logger.Warn("[GitHubReleaseChecker] assets enumerable was empty");
                    }
                }
                else
                {
                    Logger.Warn("[GitHubReleaseChecker] assets is not IEnumerable (or is string)");
                }
            }
            else
            {
                Logger.Warn("[GitHubReleaseChecker] 'assets' key missing from response");
            }
            return info;
        }

        /// <summary>
        /// JavaScriptSerializer が返す nested object を統一的に `Dictionary&lt;string, object&gt;` に
        /// 変換する helper。`Dictionary&lt;string, object&gt;` ならそのまま、非 generic `IDictionary`
        /// (Hashtable / OrderedDictionary 等) なら copy。それ以外 (null / 文字列等) は null。
        /// </summary>
        private static Dictionary<string, object> ToStringObjectDict(object obj)
        {
            var typed = obj as Dictionary<string, object>;
            if (typed != null) return typed;
            var nonGen = obj as System.Collections.IDictionary;
            if (nonGen == null) return null;
            var result = new Dictionary<string, object>();
            foreach (var key in nonGen.Keys)
            {
                if (key == null) continue;
                result[key.ToString()] = nonGen[key];
            }
            return result;
        }

        /// <summary>
        /// GitHub API の 403 (rate limit exceeded) を識別して専用 exception を throw する。
        /// API は 403 + `X-RateLimit-Remaining: 0` で rate limit を示す。Phase 4 UI は本 exception を
        /// catch して「GitHub API 利用上限、数時間後にリセット」専用文言 + grey 注記表示にする。
        /// 通常の 403 (= forbidden、auth 不備等、本 project は public repo + 認証なしなので発生しない想定)
        /// は EnsureSuccessStatusCode が HttpRequestException で投げる経路に流す。
        /// </summary>
        private static void ThrowIfRateLimited(HttpResponseMessage resp)
        {
            if (resp == null || resp.StatusCode != HttpStatusCode.Forbidden) return;
            string remaining = null;
            try
            {
                System.Collections.Generic.IEnumerable<string> vals;
                if (resp.Headers.TryGetValues("X-RateLimit-Remaining", out vals))
                {
                    foreach (var v in vals) { remaining = v; break; }
                }
            }
            catch { }
            // X-RateLimit-Remaining が 0 ならば rate limit 由来 403、それ以外の 403 は通常エラーとして
            // EnsureSuccessStatusCode に流す (caller が HttpRequestException で受ける)
            if (remaining == "0")
            {
                string resetEpoch = null;
                try
                {
                    System.Collections.Generic.IEnumerable<string> vals;
                    if (resp.Headers.TryGetValues("X-RateLimit-Reset", out vals))
                    {
                        foreach (var v in vals) { resetEpoch = v; break; }
                    }
                }
                catch { }
                DateTimeOffset? resetAt = null;
                long resetEpochLong;
                if (!string.IsNullOrEmpty(resetEpoch) && long.TryParse(resetEpoch, out resetEpochLong))
                {
                    resetAt = DateTimeOffset.FromUnixTimeSeconds(resetEpochLong);
                }
                throw new GitHubRateLimitException(resetAt);
            }
        }

        private static string AsString(Dictionary<string, object> dict, string key)
        {
            object v;
            if (!dict.TryGetValue(key, out v) || v == null) return null;
            return v.ToString();
        }
        private static bool AsBool(Dictionary<string, object> dict, string key)
        {
            object v;
            if (!dict.TryGetValue(key, out v) || v == null) return false;
            if (v is bool) return (bool)v;
            bool b;
            return bool.TryParse(v.ToString(), out b) && b;
        }
        private static long AsLong(Dictionary<string, object> dict, string key)
        {
            object v;
            if (!dict.TryGetValue(key, out v) || v == null) return 0;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            long l;
            return long.TryParse(v.ToString(), out l) ? l : 0;
        }
    }

    /// <summary>GitHub Releases API のレスポンス解析 / 必須 field 不足の例外。network 例外とは別系統。</summary>
    internal sealed class GitHubReleaseException : Exception
    {
        public GitHubReleaseException(string message) : base(message) { }
        public GitHubReleaseException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// GitHub API rate limit (60 req/hour 未認証) 超過専用の例外。Phase 4 UI は本例外を catch して
    /// 「数時間後にリセット」専用文言を出す + cache 表示中なら grey の sub-text 扱いに緩和する。
    /// </summary>
    internal sealed class GitHubRateLimitException : Exception
    {
        public DateTimeOffset? ResetAt { get; private set; }
        public GitHubRateLimitException(DateTimeOffset? resetAt)
            : base("GitHub API の利用上限に達しました (60 req/hour、未認証)")
        {
            ResetAt = resetAt;
        }
    }
}
