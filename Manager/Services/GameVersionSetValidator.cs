using System;
using System.Collections.Generic;
using System.Linq;
using TonePrism.Manager.Controls;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#242 ② ゲーム登録フォーム WPF 化) EditGameForm.btnOK_Click から「版セット全体の入力検証」を抽出した
    /// 純関数 service。編集中の in-memory 版セット (cmbVersionList の全版) を走査し、構造化した違反を返す。
    /// scan logic は本 service、presentation (MessageBox の組み立て + 表示) は caller (form) 側に残す。
    /// 元は god-method 直書きの 3 段 scan (CLAUDE.md「UI は薄く、ロジックは外へ」)。挙動は完全保存。
    ///
    /// 検証する 3 観点 (各々が独立した違反カテゴリ):
    ///   (1) version 文字列の問題 — 空 / 不正 suffix / 不正数値 (1 つの MessageBox に集約する 3 分類)
    ///   (2) 正規化重複 — SemVer 正規化後 (v 大小・leading v 無視) の NOCASE 重複 (DB の UNIQUE と整合)
    ///   (3) 人数 — 非表示版も含めた全版の「最小プレイ人数 &gt; 最大プレイ人数」
    /// caller は version-string → 重複 → 人数 の順で early-return しながら MessageBox を出す。
    /// </summary>
    public class GameVersionSetValidator
    {
        /// <summary>検証結果。各リストは表示用の per-entry 文字列 (caller は header を付けて MessageBox 化)。</summary>
        public class Result
        {
            public List<string> EmptyIds { get; } = new List<string>();               // "(id=N)"
            public List<string> MalformedSuffixEntries { get; } = new List<string>(); // "  - id=N: 'ver' (suffix 部分: 'sfx')"
            public List<string> MalformedNumericEntries { get; } = new List<string>();// "  - id=N: 'ver'"
            public List<string> DuplicateVersions { get; } = new List<string>();      // "key" or "key (生値: a / b)"
            public List<string> PlayerCountViolations { get; } = new List<string>();  // "  - ver: 最小 N > 最大 M"

            /// <summary>version 文字列の問題 (空 + 不正 suffix + 不正数値) の合計件数。1 つの MessageBox に集約する単位。</summary>
            public int VersionStringIssueCount =>
                EmptyIds.Count + MalformedSuffixEntries.Count + MalformedNumericEntries.Count;
        }

        public Result Validate(IEnumerable<GameVersion> versions)
        {
            var list = (versions ?? Enumerable.Empty<GameVersion>()).ToList();
            var r = new Result();

            // ===== (1) version 文字列 scan (#158 round 7 L-2 + L-3) =====
            // 旧実装は suffix/空/数値の 3 段 return で 1 version が複数違反だと 2-3 巡 OK を押させた UX を、
            // 1 ループで empty / malformed-suffix / malformed-numeric の 3 リストに分類し 1 MessageBox に集約。
            // suffix 切り出しは TrySplit static helper 経由 (IndexOf('-') 直書きの "v-1.0.0" 誤判定を排除)。
            // 注意: malformed-suffix は **現状 dead path**。round 5 M-1 で VersionRegex の suffix group を SuffixRegex と
            // 同一パターンに揃えたため、TrySplit が成功 (= VersionRegex match) した時点で捕捉 suffix は必ず IsSuffixValid を
            // 満たす。不正 suffix は VersionRegex 不一致で TrySplit=false となり、結局 TryNormalize 失敗 → numeric 行に入る。
            // 両 regex が将来分岐したときの guard rail として残す (挙動は旧実装と同一)。
            foreach (var vChk in list)
            {
                if (vChk == null) continue;
                string ver = vChk.Version;
                if (string.IsNullOrEmpty(ver))
                {
                    r.EmptyIds.Add("(id=" + vChk.Id + ")");
                    continue;
                }
                string core, sfx;
                if (SemverInputControl.TrySplit(ver, out core, out sfx))
                {
                    if (!SemverInputControl.IsSuffixValid(sfx))
                        r.MalformedSuffixEntries.Add("  - id=" + vChk.Id + ": '" + ver + "' (suffix 部分: '" + sfx + "')");
                }
                string normIgnored;
                if (!SemverInputControl.TryNormalize(ver, out normIgnored))
                    r.MalformedNumericEntries.Add("  - id=" + vChk.Id + ": '" + ver + "'");
            }

            // ===== (2) 正規化重複 (NOCASE) =====
            // (#158 round 6 codex P2) GroupBy のキーを TryNormalize 結果に。raw v.Version 比較だと "v1.0.0"/"1.0.0"/
            // "V1.0.0" が別 key で素通りし semantic 重複が漏れる。(PR #236 #2) GroupBy を OrdinalIgnoreCase にして
            // DB の UNIQUE(game_id, version COLLATE NOCASE) と判定を揃え、case 違い重複が SQLiteException で表面化
            // するのを事前に弾く。`: v.Version` fallback (正規化不能版) と `Where(!IsNullOrEmpty)` は (1) で弾いた
            // 後の dead path だが defensive guard rail として残す (#158 round 7 M-1 / round 8 Low #4)。
            r.DuplicateVersions.AddRange(list
                .Where(v => v != null && !string.IsNullOrEmpty(v.Version))
                .GroupBy(v =>
                {
                    string normalized;
                    return SemverInputControl.TryNormalize(v.Version, out normalized) ? normalized : v.Version;
                }, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g =>
                {
                    var raws = g.Select(v => v.Version).Distinct().ToList();
                    return raws.Count == 1 ? g.Key : g.Key + " (生値: " + string.Join(" / ", raws) + ")";
                }));

            // ===== (3) 人数 min>max (#158 Finding #4) =====
            // 表示中の NumericUpDown 値しか見ない ValidateInput を補完し、非表示版で min>max のまま別版を表示して
            // OK する素通りを塞ぐ。両方とも非 null のときのみ比較。
            foreach (var vPc in list)
            {
                if (vPc == null) continue;
                if (vPc.MinPlayers.HasValue && vPc.MaxPlayers.HasValue && vPc.MinPlayers.Value > vPc.MaxPlayers.Value)
                    r.PlayerCountViolations.Add("  - " + (vPc.Version ?? "(id=" + vPc.Id + ")")
                        + ": 最小 " + vPc.MinPlayers.Value + " > 最大 " + vPc.MaxPlayers.Value);
            }

            return r;
        }
    }
}
