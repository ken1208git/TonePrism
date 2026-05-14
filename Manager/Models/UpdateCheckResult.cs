using System;
using System.Collections.Generic;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// `UpdateChecker.CheckAsync` の結果を表す POCO。Manager UI Phase 4 (#108) のアップデートタブが
    /// 受け取って表示状態を更新する。
    ///
    /// 失敗 case も含めて単一の戻り値型で表現するため、`Status` で分岐する設計:
    /// - `UpToDate`: 最新版 (= 現在の Bundle == latest)。「最新版を実行中」表示。
    /// - `UpdateAvailable`: 新バージョン検出。Latest != null、CumulativeReleases に間の release を含む。
    /// - `Skipped`: 検出したが SkipKey で skip 済。UI は「最新版を実行中」と同じ静かな表示にする。
    /// - `NetworkError`: API 失敗 (timeout / DNS / 4xx / 5xx)。前回 cache (LastError) を可能なら使う。
    /// - `ParseError`: API 応答の JSON が壊れている / 必須 field 不在。
    /// - `UnknownBundle`: 現在の Bundle version が CHANGELOG から取れず比較不能。
    ///
    /// `Latest` は `Status` が `UpdateAvailable` / `Skipped` の場合のみ非 null。
    /// </summary>
    internal sealed class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; set; }

        /// <summary>installed Bundle version (CHANGELOG.md 由来)。`UnknownBundle` 時は null。</summary>
        public Version Current { get; set; }

        /// <summary>GitHub Releases の最新 (`prerelease=false` の中での最新)。NetworkError / ParseError 時は null。</summary>
        public ReleaseInfo Latest { get; set; }

        /// <summary>
        /// 累積更新ノート用、Current &lt; tag ≤ Latest の release 群 (新しい順)。UI で間の version の
        /// release notes を表示するために使う。Latest が null の場合は空 list。
        /// </summary>
        public IReadOnlyList<ReleaseInfo> CumulativeReleases { get; set; }

        /// <summary>API check が走った時刻 (Unix epoch ms)。cache hydration / TTL 判定に使う。</summary>
        public long CheckedAtUnixMs { get; set; }

        /// <summary>cache から取得した結果か (= API は叩いていない)。</summary>
        public bool FromCache { get; set; }

        /// <summary>NetworkError / ParseError 時の人間可読エラーメッセージ。UI と log に出す。</summary>
        public string LastError { get; set; }
    }

    internal enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        Skipped,
        NetworkError,
        ParseError,
        UnknownBundle,
    }
}
