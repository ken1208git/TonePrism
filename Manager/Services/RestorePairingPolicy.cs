using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#250 PR3b) 復元時に DB バックアップ世代をアセット控え(manifest)とペアリングしてよいかの純ロジック。
    /// UserControl から切り出して WinForms 非依存で単体テスト可能にする (CLAUDE.md「UI は薄く、ロジックは外へ」)。
    /// </summary>
    public static class RestorePairingPolicy
    {
        /// <summary>
        /// (review #1) この trigger type のバックアップが**アセット控え(manifest)とペアリング可能か**。
        /// auto/manual のみ true。これらは DB バックアップ成功直後に同一 timestamp で manifest を co-create するため
        /// .db↔.manifest が真に対応する。safety（復元 undo 用に退避した live DB）/ unknown（v0.20.0 以前の旧フラット形式）は
        /// 対の manifest を持たないので false＝DBのみ復元に倒す（時刻 fallback で無関係な世代を拾い、reconcile の余剰削除で
        /// undo 後に追加したゲームファイルを消す事故を防ぐ）。allowlist 方式で将来の新 trigger type も既定 DBのみに倒す。
        /// </summary>
        public static bool IsAssetPairingEligible(string triggerType)
        {
            return string.Equals(triggerType, "auto", StringComparison.OrdinalIgnoreCase)
                || string.Equals(triggerType, "manual", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// (#250 PR3b round3) `RestoreService` が作る退避ファイル <c>safety_&lt;yyyyMMdd&gt;_&lt;HHmmss&gt;[_host][_suffix].db</c> の
        /// ファイル名から <c>yyyyMMdd_HHmmss</c> 部を取り出す。退避時のアセット safety 控えの timestamp に流用し、
        /// safety_db ↔ アセット safety 控えを同 timestamp でペアにする (undo の完全一致ペアリング用)。形式不一致は null。
        ///
        /// (review round5 #2) ここは `RestoreService` の safety 命名規約 (`safety_{yyyyMMdd_HHmmss}[_host]`) と暗黙結合する。
        /// 規約が変わると null → 退避が silent に DBのみ degrade するため、純ロジックに切り出して回帰テストで invariant を固定する。
        /// </summary>
        public static string ParseSafetyTimestamp(string safetyPath)
        {
            if (string.IsNullOrEmpty(safetyPath)) return null;
            string fn = Path.GetFileNameWithoutExtension(safetyPath);
            const string prefix = "safety_";
            if (!fn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = fn.Substring(prefix.Length); // "yyyyMMdd_HHmmss[_host]..."
            if (rest.Length < 15) return null;
            string ts = rest.Substring(0, 15);          // "yyyyMMdd_HHmmss"
            return Regex.IsMatch(ts, @"^\d{8}_\d{6}$") ? ts : null;
        }
    }
}
