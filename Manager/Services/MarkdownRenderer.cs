using System;
using System.Text;
using Markdig;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// Markdig を使ってリリースノート Markdown を HTML に変換するヘルパー。Phase 4 (#108) で導入。
    ///
    /// SPEC §3.7.3 [5] で「リリースノート Markdown レンダリング」を規定。Markdig は .NET Standard 2.0
    /// ターゲットで .NET Framework 4.8 互換、軽量で外部依存なし。
    ///
    /// セキュリティ: WebBrowser は IE11 engine 経由で表示するため、生 HTML を表示する設計だが
    /// 信頼境界として「release notes 本文は repo maintainer が書いたものに限定」する運用 (任意 user
    /// 入力を render しない)。Markdig は raw HTML を default で許容するが、本 helper は
    /// `DisableHtml` を有効化して `&lt;script&gt;` 等の侵入経路を構造的に塞ぐ。
    /// </summary>
    internal static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
            // ## や ### の自動 ID 化、テーブル等の advanced extension は不要 (raw markdown を読みやすく
            // すれば十分)。シンプルさを優先して default に近い構成。
            .DisableHtml()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();

        /// <summary>Markdown 文字列を HTML body 部分に変換する。null / 空文字 → 空 string。</summary>
        public static string MarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return string.Empty;
            try
            {
                return Markdown.ToHtml(markdown, _pipeline);
            }
            catch (Exception ex)
            {
                // 想定外の例外で UI を巻き添えにしない、preformatted で生 markdown 表示にフォールバック
                return "<pre>" + System.Net.WebUtility.HtmlEncode(markdown) +
                       "</pre><p style='color:#c00'>Markdown 変換でエラー: " +
                       System.Net.WebUtility.HtmlEncode(ex.Message) + "</p>";
            }
        }

        /// <summary>
        /// WebBrowser.DocumentText に投入できる完全 HTML 文書 (head + body) を組み立てる。
        /// charset = UTF-8 (default の Shift_JIS 解釈で日本語化け回避)、簡素な CSS で読みやすさ調整。
        /// </summary>
        public static string WrapAsDocument(string bodyHtml)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head>");
            sb.Append("<meta charset=\"utf-8\"/>");
            sb.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"/>");
            sb.Append("<style>");
            sb.Append("html,body{margin:0;padding:12px;font-family:'Meiryo UI','Yu Gothic UI',sans-serif;font-size:13px;color:#222;line-height:1.6;}");
            sb.Append("h1,h2,h3,h4{margin-top:1em;margin-bottom:.4em;line-height:1.3;}");
            sb.Append("h1{font-size:1.4em;border-bottom:1px solid #ccc;padding-bottom:.2em;}");
            sb.Append("h2{font-size:1.2em;border-bottom:1px solid #eee;padding-bottom:.2em;}");
            sb.Append("h3{font-size:1.08em;}");
            sb.Append("p{margin:.4em 0;}");
            sb.Append("ul,ol{margin:.4em 0 .4em 1.6em;}");
            sb.Append("li{margin:.1em 0;}");
            sb.Append("code{background:#f5f5f5;padding:1px 4px;border-radius:3px;font-family:Consolas,monospace;font-size:90%;}");
            sb.Append("pre{background:#f5f5f5;padding:8px;border-radius:4px;overflow-x:auto;}");
            sb.Append("pre code{background:none;padding:0;}");
            sb.Append("hr{border:none;border-top:1px solid #ccc;margin:1em 0;}");
            sb.Append("a{color:#06c;}");
            sb.Append("blockquote{border-left:3px solid #ccc;padding-left:.8em;color:#555;margin:.4em 0;}");
            sb.Append("</style></head><body>");
            sb.Append(bodyHtml ?? string.Empty);
            sb.Append("</body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// 累積更新ノート (current &lt; ver &le; latest) を単一の HTML 文書に結合する。
        /// `## Bundle vX.Y.Z (YYYY-MM-DD)` 形式の見出し + `body` + `<hr/>` で区切る。
        /// `topHeading` を指定すると先頭に `<h1>` で表示 (例: 「これから適用される変更」、Phase 4 UI 用)。
        /// `releases` が空なら「リリースノートはありません」を返す。
        /// </summary>
        public static string BuildCumulativeHtml(System.Collections.Generic.IReadOnlyList<Models.ReleaseInfo> releases, string topHeading = null)
        {
            if (releases == null || releases.Count == 0) return WrapAsDocument("<p>リリースノートはありません。</p>");

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(topHeading))
            {
                sb.Append("<h1>").Append(System.Net.WebUtility.HtmlEncode(topHeading)).Append("</h1>");
            }
            for (int i = 0; i < releases.Count; i++)
            {
                var r = releases[i];
                string title = r.TagName ?? "(unknown)";
                string date = r.PublishedAt.HasValue ? r.PublishedAt.Value.ToString("yyyy-MM-dd") : "";
                sb.Append("<h2>Bundle ").Append(System.Net.WebUtility.HtmlEncode(title));
                if (!string.IsNullOrEmpty(date))
                {
                    sb.Append(" <span style='color:#888;font-weight:normal;font-size:.85em;'>(")
                      .Append(date).Append(")</span>");
                }
                sb.Append("</h2>");
                sb.Append(MarkdownToHtml(r.Body));
                if (i < releases.Count - 1) sb.Append("<hr/>");
            }
            return WrapAsDocument(sb.ToString());
        }
    }
}
