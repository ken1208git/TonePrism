using System;
using System.Collections.Generic;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// `UpdateChecker.CheckAsync` の結果を表す POCO。Manager UI Phase 4 (#108) のアップデートタブが
    /// 受け取って表示状態を更新する。
    ///
    /// 失敗 case も含めて単一の戻り値型で表現するため、`Status` で分岐する設計:
    /// - `Initializing`: cache 不在 + API 未確認 (= 起動直後で background check 未完了) の遷移状態。
    ///   「最新版を確認中...」(灰色) + Update/Skip ボタン無効。`UpdateChecker.LoadCacheOnly` の cache 不在
    ///   path のみが返す。`Current` は非 null (UnknownBundle 経路に倒れない case)、`Latest` は **null**
    ///   (= まだ API 叩いていない)。CumulativeReleases / LastError も null / 空。background check 完了
    ///   (`OnCheckCompleted`) で `UpToDate` / `UpdateAvailable` / `NetworkError` 等に上書きされる
    ///   短命状態。`ComputeStatus` 経由ではなく `LoadCacheOnly` で直接代入する設計 (= `ComputeStatus`
    ///   が `latest == null` を `UpToDate` に倒す挙動を維持しつつ、cache 不在経路だけ別状態に分離)。
    /// - `UpToDate`: 最新版 (= 現在の Bundle == latest)。「最新版を実行中」(緑文字) + Update/Skip ボタン無効。
    ///   Latest は **非 null とは限らない** ((#108 Phase 4 round 7 H-1) round 6 M-1 訂正の再訂正):
    ///   `UpdateChecker.ComputeStatus` は `latest == null || latest.Version == null || latest.Version <= current`
    ///   の **3 条件 OR** で UpToDate に倒すため、`latest == null` (= API fetch 失敗 + cache 無 +
    ///   Current のみ取得成功 cases) でも UpToDate に分類される。「現在実行中」release notes は
    ///   Latest 非 null 時のみ表示、null 時は表示なしで縮退する想定。
    /// - `UpdateAvailable`: 新バージョン検出。Latest != null、CumulativeReleases に間の release を含む。
    ///   「アップデートあり」(Orange 文字) + Update/Skip ボタン有効。
    /// - `Skipped`: 検出したが SkipKey で skip 済。「このバージョンはスキップ済みです。」(Gray 文字) +
    ///   **Update ボタンは有効残置** ((#108 Phase 4 round 6 M-1) 旧 docstring の「UpToDate と同じ静かな表示」
    ///   は実装と乖離していたため訂正)。user が「タイミング決めて自分で apply」する path を保持。
    /// - `NetworkError`: API 失敗 (timeout / DNS / 4xx / 5xx)。前回 cache (LastError) を可能なら使う。
    /// - `ParseError`: API 応答の JSON が壊れている / 必須 field 不在。
    /// - `UnknownBundle`: 現在の Bundle version が CHANGELOG から取れず比較不能。
    ///
    /// `Latest` 非 null 条件 ((#108 Phase 4 round 7 H-1) 再訂正): **UpdateAvailable / Skipped のみ
    /// 非 null 保証** (= ComputeStatus が `latest != null && latest.Version != null && latest.Version > current`
    /// 条件でこの 2 status に分岐するため)。UpToDate / NetworkError / ParseError / UnknownBundle は
    /// Latest null あり得る。round 6 M-1 で書いた「UpdateAvailable / Skipped / UpToDate で非 null」は
    /// ComputeStatus の `latest == null` → UpToDate 経路を見落としていた誤訂正だったため、元の
    /// 「UpdateAvailable / Skipped のみ非 null」表現に戻す。
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
        /// <summary>(#173) cache 不在 + API 未確認の遷移状態。`LoadCacheOnly` cache 不在 path のみが返す。</summary>
        Initializing,
        UpToDate,
        UpdateAvailable,
        Skipped,
        NetworkError,
        ParseError,
        UnknownBundle,
    }
}
