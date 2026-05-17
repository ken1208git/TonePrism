using System;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// GitHub Releases API (`/repos/<owner>/<repo>/releases/latest` または `/releases?per_page=N`)
    /// のレスポンス JSON のうち、Manager UI Phase 4 (#108) で必要な field のみを保持する POCO。
    ///
    /// SPEC §3.7.3 [2] / [5] / Phase 4 task list:
    /// - 新バージョン検知時の表示: バージョン番号 / リリース日 / リリースノート
    /// - zip ダウンロード URL は assets[].browser_download_url の `GCTonePrism_v<X.Y.Z>.zip` を採用
    ///
    /// `prerelease=true` の release は GitHubReleaseChecker 側で filter out するので、本 class は
    /// production release のみを保持する想定 (IsPrerelease flag は念のため保持して可視化用に残す)。
    /// </summary>
    internal sealed class ReleaseInfo
    {
        /// <summary>API レスポンスの `tag_name` (例: "v0.3.0")。</summary>
        public string TagName { get; set; }

        /// <summary>tag_name から `v` prefix を剥がして System.Version cast したもの。SemVer 比較用。null = parse 失敗。</summary>
        public Version Version { get; set; }

        /// <summary>API レスポンスの `published_at` (ISO 8601)。表示専用。</summary>
        public DateTimeOffset? PublishedAt { get; set; }

        /// <summary>API レスポンスの `body` (Markdown 形式の release notes 本文)。MarkdownRenderer 経由で HTML 化。</summary>
        public string Body { get; set; }

        /// <summary>API レスポンスの `html_url` (GitHub Releases ページ URL、「ブラウザで詳細を見る」用)。</summary>
        public string HtmlUrl { get; set; }

        /// <summary>`assets[]` 中の `GCTonePrism_v<X.Y.Z>.zip` の `browser_download_url`。null = 該当 asset 不在 (= 不正な release)。</summary>
        public string ZipAssetUrl { get; set; }

        /// <summary>zip asset のバイト数 (`assets[].size`)。ディスク容量 pre-check に使う。0 = サイズ情報なし。</summary>
        public long ZipSizeBytes { get; set; }

        /// <summary>API レスポンスの `prerelease`。GetLatestAsync は `/releases/latest` endpoint (= GitHub
        /// 側で prerelease を server-side 除外) を使うため filter 不要、GetReleasesBetweenAsync は client 側
        /// で filter する。本 flag は UI で意図的に prerelease を表示する場合の保険として保持。(L3 訂正)</summary>
        public bool IsPrerelease { get; set; }

        /// <summary>API レスポンスの `draft`。通常 GitHub API は draft を返さないが、保険として保持。</summary>
        public bool IsDraft { get; set; }
    }
}
