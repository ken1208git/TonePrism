using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// `CHANGELOG.md` の `### [Bundle vX.Y.Z] - YYYY-MM-DD` セクションを抽出する parser。
    ///
    /// Phase 4 (#108) で導入: SPEC §3.7.7 で「`CHANGELOG.md` の最新 `### [Bundle vX.Y.Z]` エントリ
    /// で Bundle 版数を管理」と規定されており、Release.ps1 の `Get-BundleReleaseNotes` 関数が
    /// 同 regex で抽出して GitHub Releases 本文に流している。本 class はその論理同型を C# で
    /// 再実装し、Manager UI が installed CHANGELOG.md (`<install>/Manager/CHANGELOG.md`、zip 同梱)
    /// から:
    ///   (a) 「現在の Bundle version」を抽出 (= 最新 entry の version)
    ///   (b) staging CHANGELOG.md から「current &lt; X &le; latest」の累積 release notes を抽出
    /// するのに使う。
    ///
    /// 正規表現は Release.ps1:868-881 と論理同型:
    ///   `(?ms)^### \[Bundle v(?&lt;ver&gt;[\d.]+)\][^\r\n]*\r?\n(?&lt;body&gt;.*?)(?=^### |^---|^## |\Z)`
    /// 終端 anchor: 次の `### ` 見出し / `---` 区切り / 次の `## ` 見出し / EOF のいずれか。
    /// </summary>
    internal static class ChangelogParser
    {
        // (?m) で multiline ^ を行頭にマッチさせる。(?s) で . が改行にマッチ。
        // entry header の date 部分 (` - YYYY-MM-DD` 等) は optional に [^\r\n]* で吸収。
        // body は lazy で次の terminator anchor まで。`^[Bundle v` の link footer 行 (`[Bundle v0.2.0]: https://...`) は
        // `### ` ではなく `[` で始まるためマッチ対象外。
        private static readonly Regex BundleEntryRegex = new Regex(
            @"^###\s+\[Bundle\s+v(?<ver>\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?)\][^\r\n]*\r?\n(?<body>.*?)(?=^### |^---|^## |\Z)",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// CHANGELOG.md の内容文字列から最新 (= 最上段) の Bundle entry を抽出する。
        /// 見つからなければ null を返す (= CHANGELOG が壊れている / Bundle entry がまだない)。
        /// </summary>
        public static BundleEntry GetLatestBundle(string changelogContent)
        {
            if (string.IsNullOrEmpty(changelogContent)) return null;
            Match m = BundleEntryRegex.Match(changelogContent);
            if (!m.Success) return null;
            return BuildEntry(m);
        }

        /// <summary>
        /// CHANGELOG.md の内容文字列から `lower &lt; ver &le; upper` の Bundle entry 群を新しい順に抽出する。
        /// `lower` が null なら下限なし (全 entry を返す)、`upper` が null なら上限なし。
        /// Phase 4 累積更新 UI で「v0.2.0 → v0.5.0 のジャンプ時に v0.3.0/v0.4.0/v0.5.0 の release notes
        /// を全部表示」するのに使う (DL 後の staging CHANGELOG.md を信頼する経路)。
        /// </summary>
        public static IReadOnlyList<BundleEntry> GetBundleEntriesBetween(string changelogContent, Version lower, Version upper)
        {
            var list = new List<BundleEntry>();
            if (string.IsNullOrEmpty(changelogContent)) return list;
            foreach (Match m in BundleEntryRegex.Matches(changelogContent))
            {
                BundleEntry entry = BuildEntry(m);
                if (entry == null || entry.Version == null) continue;
                if (lower != null && entry.Version <= lower) continue;
                if (upper != null && entry.Version > upper) continue;
                list.Add(entry);
            }
            return list;
        }

        /// <summary>
        /// path 指定で CHANGELOG.md を読み込んで latest entry を返す helper。File 不在 / IOException 時は null。
        /// VersionInventory が `PathManager.BundleChangelogPath` 経由で呼ぶ。
        /// </summary>
        public static BundleEntry TryReadLatestFromFile(string changelogPath)
        {
            if (string.IsNullOrEmpty(changelogPath)) return null;
            try
            {
                if (!File.Exists(changelogPath)) return null;
                string content = File.ReadAllText(changelogPath, System.Text.Encoding.UTF8);
                return GetLatestBundle(content);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// GitHub Releases API の `tag_name` (例: "v0.3.0" / "0.3.0") を `System.Version` に変換する helper。
        /// 失敗時は null。pre-release suffix (`-rc1` 等) を含む tag は `Version.Parse` 不可なので null 扱い
        /// (= UI 上「不明」表示、Release.ps1 の Assert-VersionOrdering と同じ方針)。
        /// </summary>
        public static Version TryParseTagVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string trimmed = tag.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1);
            }
            // pre-release suffix (`-rc1` 等) は `Version.TryParse` で失敗する。3-part numeric のみ受理。
            Version v;
            return Version.TryParse(trimmed, out v) ? v : null;
        }

        private static BundleEntry BuildEntry(Match m)
        {
            string versionStr = m.Groups["ver"].Value;
            string body = m.Groups["body"].Value;
            Version v = TryParseTagVersion(versionStr);
            return new BundleEntry
            {
                RawVersionString = versionStr,
                Version = v,
                Body = body == null ? string.Empty : body.Trim(),
            };
        }
    }

    /// <summary>
    /// 抽出された 1 つの `### [Bundle vX.Y.Z]` セクションを表す。`Version` は SemVer parse 結果
    /// (失敗時 null、pre-release suffix 付きで起こる)。`Body` は trim 済の本文。
    /// </summary>
    internal sealed class BundleEntry
    {
        public string RawVersionString { get; set; }
        public Version Version { get; set; }
        public string Body { get; set; }
    }
}
