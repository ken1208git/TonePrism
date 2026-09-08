using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#464) 集計結果を**自己完結した HTML 1 枚**に書き出す。部員や顧問へ配って読んでもらうための出力で、
    /// Manager が無い PC でも開けることが目的。
    ///
    /// **外部リソースを一切参照しない** (CSS も含めて全部埋め込む): 配布先はネットに繋がらない学校の PC や
    /// USB 経由が想定で、CDN のフォントや JS を参照した瞬間に「開いたら真っ白」になる。同じ理由で
    /// JavaScript も使わず、静的な HTML + CSS だけで組む (印刷にもそのまま乗る)。
    /// </summary>
    public static class RecordsHtmlExporter
    {
        public static void Write(string path, PlayStatsSnapshot snap, PlayStatsData data)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("保存先が指定されていません。", "path");
            if (snap == null) throw new ArgumentNullException("snap");

            var sb = new StringBuilder(128 * 1024);
            WriteHead(sb, snap);
            WriteSummary(sb, snap, data);
            WriteCaveat(sb, snap);
            WriteRanking(sb, snap);
            WriteGroups(sb, snap);
            WriteOverall(sb, snap);
            WriteComments(sb, snap);
            WriteGameDetails(sb, snap);
            WriteNotes(sb, snap, data);
            sb.Append("</body></html>");

            // UTF-8 BOM 付きで書く。BOM 無しだと一部のブラウザー / Excel が Shift_JIS と誤判定して
            // 日本語が化ける (meta charset だけでは救われない環境がある)。
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        // ------------------------------------------------------------------

        private static void WriteHead(StringBuilder sb, PlayStatsSnapshot snap)
        {
            string span = snap.Day.HasValue ? snap.Day.Value.ToString("yyyy年M月d日") : "会期ぜんぶ";
            sb.Append("<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>ゲームセンターTONE の記録 — ").Append(Esc(span)).Append("</title>");
            sb.Append("<style>").Append(Css).Append("</style></head><body>");
            sb.Append("<header><h1>ゲームセンターTONE の記録</h1>");
            sb.Append("<p class=\"sub\">").Append(Esc(span))
              .Append(" ・ 書き出し ").Append(DateTime.Now.ToString("yyyy/M/d HH:mm")).Append("</p></header>");
        }

        private static void WriteSummary(StringBuilder sb, PlayStatsSnapshot s, PlayStatsData data)
        {
            sb.Append("<section><div class=\"tiles\">");
            Tile(sb, s.TotalPlays.ToString(CultureInfo.InvariantCulture), "プレイ回数", s.PlayedGameCount + " 種類が遊ばれた");
            Tile(sb, s.TotalSurveys.ToString(CultureInfo.InvariantCulture), "アンケート回答",
                "ゲーム別 " + s.GameSurveys + " / 全体 " + s.Overall.Count);
            Tile(sb, s.AverageRating.HasValue ? "★" + s.AverageRating.Value.ToString("0.00") : "—",
                "平均評価（ゲーム別）", s.GameSurveys + " 件の平均");
            Tile(sb, PlayStatsService.FormatHours(s.TotalPlaySeconds), "総プレイ時間", "みんなが遊んだ合計");
            Tile(sb, s.PcCount.ToString(CultureInfo.InvariantCulture), "稼働したPC", "記録を書いた台数");
            Tile(sb, s.PeakAt.HasValue ? s.PeakAt.Value.ToString("HH:mm") : "—", "ピーク時間帯",
                s.PeakCount > 0 ? "15分で " + s.PeakCount + " プレイ" : "—");
            sb.Append("</div></section>");
        }

        private static void Tile(StringBuilder sb, string value, string label, string sub)
        {
            sb.Append("<div class=\"tile\"><div class=\"v\">").Append(Esc(value))
              .Append("</div><div class=\"l\">").Append(Esc(label))
              .Append("</div><div class=\"s\">").Append(Esc(sub)).Append("</div></div>");
        }

        private static void WriteCaveat(StringBuilder sb, PlayStatsSnapshot snap)
        {
            int[] total = new int[5];
            foreach (GameStats g in snap.Games)
                for (int i = 0; i < 5; i++) total[i] += g.RatingCounts[i];
            if (total.Sum() == 0 || total[2] <= total[3]) return;

            sb.Append("<section class=\"caveat\"><h2>★3 が実際より多く出ている可能性があります</h2><ul>");
            sb.Append("<li>ゲーム別の回答は ★3 が ").Append(total[2]).Append(" 件で、★4 の ").Append(total[3])
              .Append(" 件を上回っています。満足度はふつう ★5→★4→★3 と減るので、この逆転は不自然です。</li>");
            sb.Append("<li>アンケートは★を選ばないと送信できませんが、開いた直後は★3 にカーソルが当たっています"
                + "（そのまま決定を押すと★3 で送られます）。数字はこの分だけ差し引いて読んでください。</li>");
            sb.Append("</ul></section>");
        }

        private static void WriteRanking(StringBuilder sb, PlayStatsSnapshot snap)
        {
            sb.Append("<section><h2>ゲーム別</h2>");
            sb.Append("<p class=\"cap\">遊ばれた回数の多い順。登録済みで 1 回も遊ばれなかったゲームも下に並びます。"
                + "平均★の「参考」は回答が ").Append(PlayStatsTuning.MinSurveysForReliableAverage)
              .Append(" 件未満で、額面どおりには読めないという印です。</p>");
            sb.Append("<table><thead><tr><th class=\"n\">#</th><th>ゲーム</th><th class=\"r\">回数</th>"
                + "<th class=\"r\">平均★</th><th class=\"r\">回答</th><th class=\"r\">中央時間</th>"
                + "<th class=\"r\">回答率</th></tr></thead><tbody>");

            int rank = 0;
            int max = snap.Games.Count > 0 ? snap.Games.Max(g => g.PlayCount) : 0;
            foreach (GameStats g in snap.Games)
            {
                rank++;
                sb.Append("<tr><td class=\"n\">").Append(rank).Append("</td><td><div class=\"gn\">")
                  .Append(Esc(g.Title));
                if (g.PlayCount == 0) sb.Append("<span class=\"badge\">未プレイ</span>");
                else if (!g.KnownGame) sb.Append("<span class=\"badge\">登録なし</span>");
                sb.Append("</div>");
                string sub = SubTitle(g);
                if (sub != null) sb.Append("<div class=\"gs\">").Append(Esc(sub)).Append("</div>");
                sb.Append("<div class=\"bar\"><i style=\"width:")
                  .Append(max > 0 ? (100.0 * g.PlayCount / max).ToString("0.#", CultureInfo.InvariantCulture) : "0")
                  .Append("%\"></i></div></td>");
                sb.Append("<td class=\"r\">").Append(g.PlayCount).Append("</td>");
                sb.Append("<td class=\"r\">").Append(g.AverageRating.HasValue
                    ? "★" + g.AverageRating.Value.ToString("0.00") + (g.RatingIsReliable ? "" : " <span class=\"ref\">参考</span>")
                    : "—").Append("</td>");
                sb.Append("<td class=\"r\">").Append(g.SurveyCount).Append("</td>");
                sb.Append("<td class=\"r\">").Append(Esc(PlayStatsService.FormatDuration(g.MedianDurationSeconds))).Append("</td>");
                sb.Append("<td class=\"r\">").Append(g.ResponseRate.HasValue
                    ? (g.ResponseRate.Value * 100).ToString("0") + "%" : "—").Append("</td></tr>");
            }
            sb.Append("</tbody></table></section>");
        }

        private static void WriteGroups(StringBuilder sb, PlayStatsSnapshot snap)
        {
            WriteGroupTable(sb, "ジャンル別", snap.ByGenre);
            WriteGroupTable(sb, "難易度別", snap.ByDifficulty);
            WriteGroupTable(sb, "期生別", snap.ByGrade);
            WriteGroupTable(sb, "制作者別", snap.ByDeveloper);
        }

        private static void WriteGroupTable(StringBuilder sb, string title, List<PlayStatsGroup> groups)
        {
            if (groups == null || groups.Count == 0) return;
            sb.Append("<section><h2>").Append(Esc(title)).Append("</h2>");
            sb.Append("<p class=\"cap\">1 本のゲームが複数に属することがあるため、合計は全体と一致しません。"
                + "平均★は回答 1 件ずつで重み付けしています。</p>");
            sb.Append("<table><thead><tr><th>区分</th><th class=\"r\">プレイ</th><th class=\"r\">本数</th>"
                + "<th class=\"r\">平均★</th><th>いちばん遊ばれた</th></tr></thead><tbody>");
            foreach (PlayStatsGroup g in groups)
            {
                sb.Append("<tr><td>").Append(Esc(g.Key)).Append("</td>")
                  .Append("<td class=\"r\">").Append(g.PlayCount).Append("</td>")
                  .Append("<td class=\"r\">").Append(g.GameCount).Append("</td>")
                  .Append("<td class=\"r\">").Append(g.AverageRating.HasValue
                      ? "★" + g.AverageRating.Value.ToString("0.00") + "（" + g.SurveyCount + "）" : "—").Append("</td>")
                  .Append("<td>").Append(Esc(g.TopGameTitle)).Append("</td></tr>");
            }
            sb.Append("</tbody></table></section>");
        }

        private static void WriteOverall(StringBuilder sb, PlayStatsSnapshot snap)
        {
            OverallSurveyStats o = snap.Overall;
            sb.Append("<section><h2>全体アンケート</h2>");
            if (o.Count == 0)
            {
                sb.Append("<p class=\"cap\">この期間の全体アンケートはありません。</p></section>");
                return;
            }
            sb.Append("<p class=\"cap\">ランチャーを終了するときに聞く「TONE 全体はどうだった？」の回答です。"
                + "ゲーム別とは別集計で、").Append(o.Count).Append(" 件・平均 ★")
              .Append(o.AverageRating.HasValue ? o.AverageRating.Value.ToString("0.00") : "—").Append("。</p>");
            WriteRatingBars(sb, o.RatingCounts, o.Count);
            sb.Append("</section>");
        }

        private static void WriteRatingBars(StringBuilder sb, int[] counts, int total)
        {
            int max = counts.Max();
            sb.Append("<div class=\"bars\">");
            for (int star = 5; star >= 1; star--)
            {
                int c = counts[star - 1];
                sb.Append("<div class=\"row\"><span class=\"lb\">").Append(new string('★', star)).Append("</span>")
                  .Append("<span class=\"bar\"><i style=\"width:")
                  .Append(max > 0 ? (100.0 * c / max).ToString("0.#", CultureInfo.InvariantCulture) : "0")
                  .Append("%\"></i></span><span class=\"vl\">").Append(c).Append(" 件</span>")
                  .Append("<span class=\"pc\">").Append(total > 0 ? (100.0 * c / total).ToString("0.0") + "%" : "—")
                  .Append("</span></div>");
            }
            sb.Append("</div>");
        }

        private static void WriteComments(StringBuilder sb, PlayStatsSnapshot snap)
        {
            if (snap.Comments.Count == 0) return;
            sb.Append("<section><h2>もらったコメント</h2>");
            sb.Append("<p class=\"cap\">").Append(snap.Comments.Count).Append(" 件（アンケート ")
              .Append(snap.TotalSurveys).Append(" 件のうち）</p>");
            foreach (PlayStatsComment c in snap.Comments)
            {
                sb.Append("<div class=\"cm\"><div class=\"cmh\"><span class=\"st\">").Append(c.RatingStars)
                  .Append("</span><span class=\"gt\">").Append(Esc(c.GameTitle))
                  .Append("</span><span class=\"tm\">").Append(Esc(c.AtDisplay)).Append("</span></div>")
                  .Append("<div class=\"cmb\">").Append(Esc(c.Text)).Append("</div></div>");
            }
            sb.Append("</section>");
        }

        private static void WriteGameDetails(StringBuilder sb, PlayStatsSnapshot snap)
        {
            var played = snap.Games.Where(g => g.PlayCount > 0).ToList();
            if (played.Count == 0) return;

            sb.Append("<section><h2>ゲームごとの内訳</h2>");
            foreach (GameStats g in played)
            {
                sb.Append("<div class=\"gd\"><h3>").Append(Esc(g.Title)).Append("</h3>");
                sb.Append("<div class=\"kv\">");
                Kv(sb, "プレイ回数", g.PlayCount + " 回");
                Kv(sb, "アンケート", g.SurveyCount + " 件"
                    + (g.ResponseRate.HasValue ? "（回答率 " + (g.ResponseRate.Value * 100).ToString("0") + "%）" : ""));
                Kv(sb, "平均★", g.AverageRating.HasValue ? "★" + g.AverageRating.Value.ToString("0.00") : "—");
                Kv(sb, "中央プレイ時間", PlayStatsService.FormatDuration(g.MedianDurationSeconds));
                Kv(sb, "平均プレイ時間", PlayStatsService.FormatDuration(g.AverageDurationSeconds));
                Kv(sb, "合計プレイ時間", PlayStatsService.FormatHours(g.TotalDurationSeconds));
                Kv(sb, "遊ばれたPC", g.PcCount + " 台");
                if (g.KnownGame)
                {
                    Kv(sb, "難易度", PlayStatsService.DifficultyLabel(g.Difficulty));
                    string gap = PlayStatsService.PlayTimeGapLabel(g.PlayTimeBand, g.MedianDurationSeconds);
                    Kv(sb, "想定プレイ時間", PlayStatsService.PlayTimeBandLabel(g.PlayTimeBand)
                        + (gap != null ? "（実測は " + gap + "）" : ""));
                    if (!string.IsNullOrWhiteSpace(g.DevelopersDisplay)) Kv(sb, "制作者", g.DevelopersDisplay);
                }
                if (g.FirstPlayAt.HasValue) Kv(sb, "はじめて遊ばれた", g.FirstPlayAt.Value.ToString("M/d HH:mm"));
                if (g.LastPlayAt.HasValue) Kv(sb, "最後に遊ばれた", g.LastPlayAt.Value.ToString("M/d HH:mm"));
                sb.Append("</div>");

                if (g.SurveyCount > 0) WriteRatingBars(sb, g.RatingCounts, g.SurveyCount);

                if (g.SaneDurationCount > 0)
                {
                    int maxb = g.DurationBuckets.Max();
                    sb.Append("<div class=\"bars\">");
                    for (int i = 0; i < g.DurationBuckets.Length; i++)
                    {
                        sb.Append("<div class=\"row\"><span class=\"lb\">")
                          .Append(Esc(PlayStatsTuning.DurationBucketLabels[i])).Append("</span>")
                          .Append("<span class=\"bar\"><i style=\"width:")
                          .Append(maxb > 0 ? (100.0 * g.DurationBuckets[i] / maxb).ToString("0.#", CultureInfo.InvariantCulture) : "0")
                          .Append("%\"></i></span><span class=\"vl\">").Append(g.DurationBuckets[i]).Append(" 回</span>")
                          .Append("<span class=\"pc\">")
                          .Append((100.0 * g.DurationBuckets[i] / g.SaneDurationCount).ToString("0")).Append("%</span></div>");
                    }
                    sb.Append("</div>");
                }

                foreach (PlayStatsComment c in g.Comments)
                {
                    sb.Append("<div class=\"cm\"><div class=\"cmh\"><span class=\"st\">").Append(c.RatingStars)
                      .Append("</span><span class=\"tm\">").Append(Esc(c.AtDisplay)).Append("</span></div>")
                      .Append("<div class=\"cmb\">").Append(Esc(c.Text)).Append("</div></div>");
                }
                sb.Append("</div>");
            }
            sb.Append("</section>");
        }

        private static void Kv(StringBuilder sb, string k, string v)
        {
            sb.Append("<div><span class=\"k\">").Append(Esc(k)).Append("</span><span class=\"v\">")
              .Append(Esc(v)).Append("</span></div>");
        }

        private static void WriteNotes(StringBuilder sb, PlayStatsSnapshot snap, PlayStatsData data)
        {
            sb.Append("<section><h2>この数字の読み方</h2><ul class=\"notes\">");
            sb.Append("<li>「プレイ回数」はゲームが 1 回起動して終わるごとに 1 件です。サービスモードの試遊テストは記録されません。</li>");
            sb.Append("<li>アンケートは「ゲームを終えたとき」と「ランチャーを終了するとき（全体）」の 2 種類があり、"
                + "ゲーム別の集計にはゲームを終えたときの回答だけを使っています。</li>");
            sb.Append("<li>ストアの「人気」並び順とは別の集計です。数字が違っていて問題ありません。</li>");
            if (snap.OutlierDurationCount > 0)
                sb.Append("<li>プレイ時間が異常だった ").Append(snap.OutlierDurationCount)
                  .Append(" 件は時間の統計から除きました（ゲームを閉じずに放置された記録など）。回数には含めています。</li>");
            if (data != null && data.RecoveredFromFileNameCount > 0)
                sb.Append("<li>").Append(data.RecoveredFromFileNameCount)
                  .Append(" 件は中身が壊れていたため、ファイル名から回数だけ復元しました。</li>");
            if (data != null && data.UnreadableFileCount > 0)
                sb.Append("<li>").Append(data.UnreadableFileCount).Append(" 件は読み取れず集計から除きました。</li>");
            sb.Append("</ul><p class=\"cap\">TonePrism Manager から書き出しました。</p></section>");
        }

        private static string SubTitle(GameStats g)
        {
            var parts = new List<string>();
            if (g.Genre != null && g.Genre.Count > 0) parts.Add(string.Join(" / ", g.Genre));
            if (!string.IsNullOrWhiteSpace(g.DevelopersDisplay)) parts.Add(g.DevelopersDisplay);
            return parts.Count > 0 ? string.Join("　", parts) : null;
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("\n", "<br>");
        }

        // 配布先がオフラインでも確実に開けるよう、フォントも配色も外部参照なしで完結させる。
        private const string Css = @"
:root{--bg:#faf8f4;--fg:#1e242c;--mut:#6b7480;--card:#fff;--line:#e3ded4;--accent:#2f5d9e;--amber:#b8860b}
*{box-sizing:border-box}
body{margin:0;padding:0 20px 60px;background:var(--bg);color:var(--fg);
  font-family:'Yu Gothic UI','Yu Gothic','Hiragino Kaku Gothic ProN','Meiryo',sans-serif;line-height:1.7}
header{max-width:1000px;margin:0 auto;padding:34px 0 10px}
h1{margin:0;font-size:26px;letter-spacing:.02em}
.sub{margin:4px 0 0;color:var(--mut);font-size:13px}
section{max-width:1000px;margin:0 auto 26px;background:var(--card);border:1px solid var(--line);
  border-radius:10px;padding:20px 22px}
h2{margin:0 0 10px;font-size:17px}
h3{margin:0 0 8px;font-size:15px}
.cap{margin:0 0 14px;color:var(--mut);font-size:12.5px}
.tiles{display:flex;flex-wrap:wrap;gap:12px}
.tile{flex:1 1 150px;border:1px solid var(--line);border-radius:8px;padding:14px 16px;background:#fcfbf8}
.tile .v{font-size:27px;font-weight:700;letter-spacing:.01em}
.tile .l{font-size:12.5px;margin-top:2px}
.tile .s{font-size:11px;color:var(--mut)}
table{width:100%;border-collapse:collapse;font-size:13px}
th,td{padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top;text-align:left}
th{color:var(--mut);font-weight:600;font-size:12px;white-space:nowrap}
td.r,th.r{text-align:right;white-space:nowrap}
td.n,th.n{width:34px;color:var(--mut);text-align:right}
.gn{font-weight:600}
.gs{color:var(--mut);font-size:11.5px}
.badge{margin-left:8px;font-size:10.5px;font-weight:400;color:var(--mut);
  border:1px solid var(--line);border-radius:4px;padding:1px 6px}
.ref{font-size:10.5px;color:var(--amber);border:1px solid #e6d9b0;border-radius:4px;padding:0 5px}
.bar{display:block;height:6px;background:#eee9e0;border-radius:3px;margin-top:5px;max-width:340px}
.bar i{display:block;height:100%;background:var(--accent);border-radius:3px}
.bars{margin:10px 0 4px}
.bars .row{display:flex;align-items:center;gap:10px;margin-bottom:5px;font-size:12.5px}
.bars .lb{width:76px;color:var(--amber);white-space:nowrap}
.bars .bar{flex:1;max-width:320px;margin:0;height:9px;border-radius:4.5px}
.bars .vl{width:52px;text-align:right}
.bars .pc{width:52px;text-align:right;color:var(--mut);font-size:11.5px}
.cm{border:1px solid var(--line);border-radius:8px;padding:10px 13px;margin-bottom:9px;background:#fcfbf8}
.cmh{display:flex;gap:12px;align-items:baseline;font-size:11.5px;color:var(--mut);margin-bottom:3px}
.cmh .st{color:var(--amber);font-size:12.5px}
.cmh .gt{font-weight:600;color:var(--fg)}
.cmb{font-size:13.5px;white-space:pre-wrap}
.gd{border-top:1px solid var(--line);padding:16px 0 6px}
.gd:first-of-type{border-top:0}
.kv{display:flex;flex-wrap:wrap;gap:4px 26px;font-size:12.5px;margin-bottom:10px}
.kv>div{min-width:210px}
.kv .k{color:var(--mut);display:inline-block;min-width:120px}
.kv .v{font-weight:600}
.caveat{background:#fdf6e3;border-color:#e8d9a8}
.caveat h2{color:#8a6d1f}
.caveat ul{margin:0;padding-left:20px;font-size:12.5px}
.caveat li{margin-bottom:6px}
.notes{margin:0;padding-left:20px;color:var(--mut);font-size:12.5px}
.notes li{margin-bottom:6px}
@media print{body{background:#fff}section{break-inside:avoid;border-color:#ccc}}
";
    }
}
