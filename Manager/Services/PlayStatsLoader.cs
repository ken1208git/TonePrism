using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#464) `responses/` の走査結果 (生データ) + DB のゲーム一覧。**期間の切り替えはこのデータの
    /// 再集計で行い、走査はやり直さない** — 本番の `responses/` は SMB 共有上にあり、日付タブを
    /// 押すたびに数百ファイルを開き直すのは往復が無駄すぎる。
    /// </summary>
    public sealed class PlayStatsData
    {
        public List<PlayRecordEntry> Plays { get; set; } = new List<PlayRecordEntry>();
        public List<SurveyEntry> Surveys { get; set; } = new List<SurveyEntry>();
        public List<GameInfo> Games { get; set; } = new List<GameInfo>();

        /// <summary>記録が存在する日 (昇順)。画面の日付タブはこれで作る。</summary>
        public List<DateTime> AvailableDates { get; set; } = new List<DateTime>();

        /// <summary>`responses/` そのものが無い (= まだ 1 度も遊ばれていない / パス設定が違う)。</summary>
        public bool ResponsesFolderMissing { get; set; }

        /// <summary>中身を読めず**ファイル名から復元した**件数。0 でなければ画面に注記する。</summary>
        public int RecoveredFromFileNameCount { get; set; }

        /// <summary>中身もファイル名も解釈できず捨てた件数。</summary>
        public int UnreadableFileCount { get; set; }

        /// <summary>ゲーム一覧の取得に失敗した (集計は出せるがゲーム名が番号のままになる)。</summary>
        public bool GamesUnavailable { get; set; }

        public DateTime LoadedAt { get; set; }
    }

    /// <summary>
    /// (#464) `responses/play_records/` `responses/surveys/` を全走査して生の記録に起こす **I/O 層**。
    /// 集計は <see cref="PlayStatsService"/> (純粋計算) が行い、ここは読むだけに徹する。
    ///
    /// **書き込みは一切しない。** 記録は取り直しの効かない一度きりのデータで、しかも会期はもう
    /// 終わっている。集計画面が原因で 1 件でも壊れたら復旧手段が無いので、この層に書き込み API を
    /// 足さないこと (retention や集約が必要になったら別サービスにする)。
    ///
    /// **fail-soft**: 1 ファイル壊れていても残りは集計する。壊れたファイルも
    /// **ファイル名から時刻と game_no を復元して件数には残す** — ファイル名は
    /// `&lt;unix_ts&gt;-&lt;game_no&gt;-&lt;uuid&gt;.json` で、`ResponsesWriter` が「中身が正本・名前はそれを
    /// 速く読むための索引」として意図的に二重に持たせている (SPEC §7.5.3)。索引が生きているなら
    /// 「遊ばれた事実」だけは救えるので捨てない。
    ///
    /// SMB 越しの I/O を含むため**呼び出し側はバックグラウンドスレッドで実行**すること。
    /// </summary>
    public static class PlayStatsLoader
    {
        /// <summary>
        /// `responses/` と DB を読んで生データを揃える。例外は投げず、取れなかった区画は
        /// <see cref="PlayStatsData"/> のフラグで示す。
        /// </summary>
        /// <param name="db">ゲーム名・ジャンル・制作者の解決に使う。null なら番号のまま集計する。</param>
        /// <param name="responsesRoot">
        /// 走査する `responses/` の場所。既定 (null) は <see cref="PathManager.ResponsesFolder"/>。
        /// **テストと、別の場所から持ち帰った記録を読むために外から差せるようにしてある** —
        /// 本番 PC を触れないときに、コピーしてきた `responses/` をそのまま集計できる。
        /// </param>
        public static PlayStatsData Load(DatabaseManager db, string responsesRoot = null)
        {
            var data = new PlayStatsData { LoadedAt = DateTime.Now };

            // --- ゲーム一覧 (名前・ジャンル・制作者の解決に使う) ---
            try
            {
                if (db != null) data.Games = db.GetAllGames() ?? new List<GameInfo>();
            }
            catch (Exception ex)
            {
                // 名前が番号のままになるだけで集計自体は出せるので、ここで諦めない。
                data.GamesUnavailable = true;
                Logger.Warn("[PlayStatsLoader] ゲーム一覧の取得に失敗しました (番号のまま集計します): " + ex.Message);
            }

            // --- 記録本体 ---
            string root = string.IsNullOrWhiteSpace(responsesRoot) ? PathManager.ResponsesFolder : responsesRoot;
            string playRoot = Path.Combine(root, "play_records");
            string surveyRoot = Path.Combine(root, "surveys");
            bool anyFolder = SafeDirectoryExists(playRoot) || SafeDirectoryExists(surveyRoot);
            if (!anyFolder)
            {
                data.ResponsesFolderMissing = true;
                Logger.Info("[PlayStatsLoader] 記録フォルダがまだありません: " + playRoot);
                return data;
            }

            LoadPlays(playRoot, data);
            LoadSurveys(surveyRoot, data);

            data.AvailableDates = data.Plays.Select(p => p.CreatedAtLocal.Date)
                .Concat(data.Surveys.Select(s => s.CreatedAtLocal.Date))
                .Distinct().OrderBy(d => d).ToList();

            Logger.Info(string.Format(CultureInfo.InvariantCulture,
                "[PlayStatsLoader] 記録を読み込みました: プレイ {0} 件 / アンケート {1} 件 / 日数 {2} (名前復元 {3} / 読み取り不能 {4})",
                data.Plays.Count, data.Surveys.Count, data.AvailableDates.Count,
                data.RecoveredFromFileNameCount, data.UnreadableFileCount));

            return data;
        }

        private static void LoadPlays(string root, PlayStatsData data)
        {
            foreach (string path in EnumerateRecordFiles(root))
            {
                FileNameKey key = ParseFileName(path);
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
                    {
                        JsonElement r = doc.RootElement;
                        long created = ReadLong(r, "created_at", key.UnixTime);
                        var entry = new PlayRecordEntry
                        {
                            CreatedAt = created,
                            StartTime = ReadLong(r, "start_time", created),
                            EndTime = ReadLong(r, "end_time", created),
                            GameNo = ReadLong(r, "game_no", key.GameNo),
                            SourcePc = ReadString(r, "source_pc"),
                            CreatedAtLocal = ToLocal(created),
                        };
                        data.Plays.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    // 中身が壊れていてもファイル名 (索引) が生きていれば「遊ばれた事実」は救う。
                    if (key.IsValid)
                    {
                        data.RecoveredFromFileNameCount++;
                        data.Plays.Add(new PlayRecordEntry
                        {
                            CreatedAt = key.UnixTime,
                            StartTime = key.UnixTime,
                            EndTime = key.UnixTime,   // 時間統計には 0 秒として入るが DurationIsSane が拾う
                            GameNo = key.GameNo,
                            SourcePc = null,
                            CreatedAtLocal = ToLocal(key.UnixTime),
                        });
                    }
                    else
                    {
                        data.UnreadableFileCount++;
                        Logger.Warn("[PlayStatsLoader] プレイ記録を読めません (無視): "
                            + Path.GetFileName(path) + " / " + ex.Message);
                    }
                }
            }
        }

        private static void LoadSurveys(string root, PlayStatsData data)
        {
            foreach (string path in EnumerateRecordFiles(root))
            {
                FileNameKey key = ParseFileName(path);
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
                    {
                        JsonElement r = doc.RootElement;
                        long created = ReadLong(r, "created_at", key.UnixTime);
                        int rating = (int)ReadLong(r, "rating", 0);
                        if (rating < 1 || rating > 5)
                        {
                            // Launcher 側が範囲外を弾いている (survey_writer.gd) ので通常は起きない。
                            // 起きたなら壊れた値なので、平均を汚す前に落とす。
                            data.UnreadableFileCount++;
                            Logger.Warn("[PlayStatsLoader] 評価が範囲外のアンケートを無視: "
                                + Path.GetFileName(path) + " rating=" + rating);
                            continue;
                        }
                        data.Surveys.Add(new SurveyEntry
                        {
                            CreatedAt = created,
                            GameNo = ReadLong(r, "game_no", key.GameNo),
                            Rating = rating,
                            Comment = ReadString(r, "comment"),
                            Trigger = ReadString(r, "trigger") ?? SurveyEntry.TriggerGameEnd,
                            SourcePc = ReadString(r, "source_pc"),
                            CreatedAtLocal = ToLocal(created),
                        });
                    }
                }
                catch (Exception ex)
                {
                    // アンケートは**ファイル名から復元しない**。名前には評価値が入っていないので、
                    // 救えるのは「回答があった事実」だけ。件数だけ増えて★が無い行を混ぜると
                    // 回答率と平均★の母数がズレる (プレイ記録は件数そのものが意味を持つので復元する)。
                    data.UnreadableFileCount++;
                    Logger.Warn("[PlayStatsLoader] アンケートを読めません (無視): "
                        + Path.GetFileName(path) + " / " + ex.Message);
                }
            }
        }

        /// <summary>日付フォルダ配下の `*.json` を列挙する。フォルダ単位の失敗は握って続行。</summary>
        private static IEnumerable<string> EnumerateRecordFiles(string root)
        {
            if (!SafeDirectoryExists(root)) yield break;

            string[] files;
            try
            {
                files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Logger.Warn("[PlayStatsLoader] 記録フォルダを列挙できません: " + root + " / " + ex.Message);
                yield break;
            }

            // ファイル名先頭が unix 秒なので、名前順 = おおむね時系列。集計は順序に依存しないが、
            // ログや部分失敗時の追跡がしやすい。
            Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files) yield return f;
        }

        private static bool SafeDirectoryExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && Directory.Exists(path); }
            catch { return false; }
        }

        private static DateTime ToLocal(long unixSeconds)
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().DateTime; }
            catch { return DateTime.MinValue; }
        }

        private static long ReadLong(JsonElement obj, string name, long fallback)
        {
            JsonElement v;
            if (!obj.TryGetProperty(name, out v)) return fallback;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)) return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out long s)) return s;
            return fallback;
        }

        private static string ReadString(JsonElement obj, string name)
        {
            JsonElement v;
            if (!obj.TryGetProperty(name, out v)) return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        /// <summary>ファイル名 `&lt;unix_ts&gt;-&lt;game_no&gt;-&lt;uuid&gt;.json` から復元した索引。</summary>
        private struct FileNameKey
        {
            public long UnixTime;
            public long GameNo;
            public bool IsValid;
        }

        /// <summary>
        /// ファイル名を索引として読む。中身が読めたときは中身が正本なので、これは
        /// (a) 欠けたフィールドの既定値、(b) 中身が壊れたときの復元、にだけ使う。
        /// </summary>
        private static FileNameKey ParseFileName(string path)
        {
            var key = new FileNameKey();
            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(name)) return key;
                string[] parts = name.Split('-');
                if (parts.Length < 3) return key;
                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ts)) return key;
                if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long no)) return key;
                key.UnixTime = ts;
                key.GameNo = no;
                key.IsValid = true;
            }
            catch { /* 名前が想定外なら索引無しとして扱う */ }
            return key;
        }
    }
}
