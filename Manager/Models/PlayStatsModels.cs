using System;
using System.Collections.Generic;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// (#297 / #464) `responses/play_records/` の 1 件。JSON の生の姿をそのまま持つ薄い DTO で、
    /// 派生値 (プレイ時間・ローカル日時) だけ計算済で載せる。
    ///
    /// **`DurationSeconds` を信じてよいかは <see cref="DurationIsSane"/> で判断すること。** 記録は
    /// 会期中に数十台の PC が各自の時計で書いており、時計ズレ・スリープ復帰・ゲームの放置終了で
    /// 負の値や数時間の値が混ざりうる。件数 (= 遊ばれた回数) は 1 ファイル 1 プレイなので常に正しいが、
    /// 時間の統計だけは異常値を外さないと平均が壊れる。
    /// </summary>
    public sealed class PlayRecordEntry
    {
        public long CreatedAt { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
        public long GameNo { get; set; }
        public string SourcePc { get; set; }

        /// <summary>記録が書かれたローカル日時 (`created_at` 由来)。日付フォルダもこれから導かれている。</summary>
        public DateTime CreatedAtLocal { get; set; }

        /// <summary>プレイ時間 (秒)。異常値を含みうる (<see cref="DurationIsSane"/> 参照)。</summary>
        public long DurationSeconds { get { return EndTime - StartTime; } }

        /// <summary>
        /// プレイ時間の統計に入れてよい値か。
        ///
        /// **下限 (0 秒未満を弾く) は破損の除外**。負の値は PC 間の時計ズレやスリープ復帰で終了時刻が
        /// 開始より前になったもので、時間として意味を成さない。
        ///
        /// **上限は「異常の検出」ではなく保険**。ランチャーはプロセスの起動と終了しか見ておらず、
        /// 長い記録が熱中なのか閉じ忘れなのかは**区別できない**。上限は、閉じ忘れが 1 件混ざったときに
        /// それだけで合計プレイ時間 (賞とランキングの軸) がひっくり返るのを防ぐためのもの。
        /// 2026 年の実データでは 1 件も該当していない (最長 110 分)。
        /// ゲーム放置の自動終了が入って記録に終了理由が載れば、この推測は不要になる。
        /// </summary>
        public bool DurationIsSane
        {
            get { return DurationSeconds >= 0 && DurationSeconds <= PlayStatsTuning.MaxSaneDurationSeconds; }
        }
    }

    /// <summary>
    /// (#297 / #464) `responses/surveys/` の 1 件。
    ///
    /// **2 種類が同じフォルダに混ざっている。** `trigger` が
    /// <c>game_end</c> = ゲーム個別 (対象ゲームの `game_no` を持つ) /
    /// <c>launcher_end</c> = **全体アンケート** (`game_no` は 0 でどのゲームも指さない)。
    /// ゲーム別集計に全体アンケートを混ぜると「存在しないゲーム no.0」が最多評価件数のゲームとして
    /// 現れるので、必ず <see cref="IsOverall"/> で分けること。
    /// </summary>
    public sealed class SurveyEntry
    {
        public long CreatedAt { get; set; }
        public long GameNo { get; set; }
        public int Rating { get; set; }
        /// <summary>自由記述。**未入力は空文字ではなく null** (SPEC §6.5 の NULL 扱い)。</summary>
        public string Comment { get; set; }
        public string Trigger { get; set; }
        public string SourcePc { get; set; }
        public DateTime CreatedAtLocal { get; set; }

        public const string TriggerGameEnd = "game_end";
        public const string TriggerLauncherEnd = "launcher_end";

        /// <summary>全体アンケート (ランチャー終了時)。ゲーム別集計から除外する。</summary>
        public bool IsOverall { get { return Trigger == TriggerLauncherEnd; } }

        /// <summary>コメントが実際に書かれているか (null と空文字の両方を「なし」とする、SPEC §6.5)。</summary>
        public bool HasComment { get { return !string.IsNullOrWhiteSpace(Comment); } }
    }

    /// <summary>集計の閾値。画面と service で同じ値を使うため 1 箇所に置く。</summary>
    public static class PlayStatsTuning
    {
        /// <summary>これを超えるプレイ時間は「閉じ忘れ」とみなして時間統計から外す (件数には残す)。</summary>
        public const long MaxSaneDurationSeconds = 2 * 60 * 60;

        /// <summary>
        /// 平均★を「参考値」に格下げする回答件数の下限。1〜2 件の平均で並べ替えると、ほとんど遊ばれて
        /// いないゲームが★5 単発で 1 位に出てランキングが意味を失う。
        /// </summary>
        public const int MinSurveysForReliableAverage = 5;

        /// <summary>プレイ時間の分布バケット (秒)。境界値の意味は <see cref="DurationBucketLabels"/>。</summary>
        public static readonly long[] DurationBucketBounds = { 60, 180, 300, 600 };

        public static readonly string[] DurationBucketLabels =
        {
            "1分未満", "1〜3分", "3〜5分", "5〜10分", "10分以上"
        };

        /// <summary>時間帯グラフの刻み (分)。15 分 = 1 日 96 本。</summary>
        public const int TimelineBucketMinutes = 15;

        /// <summary>
        /// 「終わってすぐもう一度」とみなす間隔 (秒)。同じ PC で前のプレイが終わってからこの秒数以内に
        /// **同じゲーム**が始まったら再プレイとして数える。
        ///
        /// 2 分にしたのは、来場者がまだ台の前に居ると言える範囲に絞るため。長く取ると「次の人が
        /// たまたま同じゲームを選んだ」が混ざり、意味が「その場の熱気」から「単なる人気」へ薄まる。
        /// 実データ (2026 年) では 2 分と 5 分で件数が同じ 80 件 = 再プレイは即座に起きており、
        /// 広げても拾えるものが増えないことを確認済み。
        /// </summary>
        public const long RepeatGapSeconds = 120;

        /// <summary>賞の対象に入れるための最低プレイ回数 (率で競う賞が少数プレイに攫われないように)。</summary>
        public const int MinPlaysForAward = 5;

        /// <summary>「隠れた名作」の対象に入れるための最低アンケート件数。</summary>
        public const int MinSurveysForHiddenGem = 3;

        /// <summary>同点がこれを超えたら賞として意味を成さないので出さない。</summary>
        public const int MaxAwardWinners = 3;
    }

    /// <summary>
    /// 集計の対象期間。**両端を含む日付の閉区間**で、どちらも未指定なら全期間。
    ///
    /// `DateTime?` を 2 本引き回さず型にしてあるのは、(a) 開始と終了を取り違えても気づけないから、
    /// (b) 「片方だけ指定」「逆順に指定」の正規化を 1 箇所に閉じ込めたいから。日付だけを見る
    /// (時刻は落とす) ので、終了日の 23:59 の記録も必ず入る。
    /// </summary>
    public sealed class PlayStatsRange
    {
        private PlayStatsRange(DateTime? from, DateTime? to)
        {
            From = from;
            To = to;
        }

        /// <summary>開始日 (この日を含む)。null = 下限なし。</summary>
        public DateTime? From { get; private set; }
        /// <summary>終了日 (この日を含む)。null = 上限なし。</summary>
        public DateTime? To { get; private set; }

        /// <summary>全期間。</summary>
        public static readonly PlayStatsRange All = new PlayStatsRange(null, null);

        /// <summary>1 日だけ。</summary>
        public static PlayStatsRange Day(DateTime day)
        {
            return new PlayStatsRange(day.Date, day.Date);
        }

        /// <summary>
        /// 期間を指定する。**逆順で渡されたら入れ替える** — 画面で終了日を先に選ぶ操作は普通に
        /// 起きるので、そこで 0 件になるより素直に期間として扱う方がよい。
        /// </summary>
        public static PlayStatsRange Between(DateTime? from, DateTime? to)
        {
            DateTime? f = from.HasValue ? from.Value.Date : (DateTime?)null;
            DateTime? t = to.HasValue ? to.Value.Date : (DateTime?)null;
            if (f.HasValue && t.HasValue && f.Value > t.Value)
            {
                DateTime? swap = f;
                f = t;
                t = swap;
            }
            return new PlayStatsRange(f, t);
        }

        public bool IsAll { get { return !From.HasValue && !To.HasValue; } }

        public bool IsSingleDay
        {
            get { return From.HasValue && To.HasValue && From.Value == To.Value; }
        }

        /// <summary>その日時が期間に入るか (日付だけで判定)。</summary>
        public bool Contains(DateTime at)
        {
            DateTime d = at.Date;
            if (From.HasValue && d < From.Value) return false;
            if (To.HasValue && d > To.Value) return false;
            return true;
        }

        /// <summary>画面に出す期間の表示 (「全期間」「9月5日」「9月4日 〜 9月5日」)。</summary>
        public string Label
        {
            get
            {
                if (IsAll) return "全期間";
                if (IsSingleDay) return From.Value.ToString("M月d日");
                if (From.HasValue && To.HasValue)
                    return From.Value.ToString("M月d日") + " 〜 " + To.Value.ToString("M月d日");
                if (From.HasValue) return From.Value.ToString("M月d日") + " 以降";
                return To.Value.ToString("M月d日") + " まで";
            }
        }
    }

    /// <summary>1 件のコメント (ゲーム名を解決済み)。全ゲーム横断の一覧と、ゲーム詳細の両方で使う。</summary>
    public sealed class PlayStatsComment
    {
        public DateTime At { get; set; }
        public long GameNo { get; set; }
        /// <summary>解決済みのゲーム名。全体アンケートは「TONE 全体」。</summary>
        public string GameTitle { get; set; }
        public int Rating { get; set; }
        public string Text { get; set; }
        public bool IsOverall { get; set; }
        public string SourcePc { get; set; }

        public string RatingStars { get { return new string('★', Rating) + new string('☆', 5 - Rating); } }
        public string AtDisplay { get { return At.ToString("M/d HH:mm"); } }
    }

    /// <summary>
    /// ゲーム 1 本の集計。**登録済みだが 1 回も遊ばれなかったゲームも 0 件の行として作る** —
    /// 記録側にしか存在しない情報で集計すると「遊ばれなかった」が可視化されず、ランキングの下位が
    /// 嘘になるため (DB を持つ Manager でしか出せない情報)。
    /// </summary>
    public sealed class GameStats
    {
        public long GameNo { get; set; }
        public string GameId { get; set; }
        public string Title { get; set; }

        /// <summary>DB の games に対応する行があるか。false = 記録にしか居ない番号 (削除済みゲーム等)。</summary>
        public bool KnownGame { get; set; }

        // --- プレイ記録由来 ---
        public int PlayCount { get; set; }
        public long TotalDurationSeconds { get; set; }
        /// <summary>時間統計に採用した件数 (異常値を除いた後)。0 なら時間系は null。</summary>
        public int SaneDurationCount { get; set; }
        public int? MedianDurationSeconds { get; set; }
        public int? AverageDurationSeconds { get; set; }
        public int[] DurationBuckets { get; set; } = new int[PlayStatsTuning.DurationBucketLabels.Length];
        public DateTime? FirstPlayAt { get; set; }
        public DateTime? LastPlayAt { get; set; }
        /// <summary>このゲームを起動した PC の台数 (何台に置かれ・遊ばれたか)。</summary>
        public int PcCount { get; set; }
        /// <summary>時間帯別のプレイ数 (0〜23 時)。</summary>
        public int[] HourCounts { get; set; } = new int[24];

        // --- アンケート由来 (game_end のみ) ---
        public int SurveyCount { get; set; }
        public double? AverageRating { get; set; }
        /// <summary>★1〜5 の件数。index 0 = ★1。</summary>
        public int[] RatingCounts { get; set; } = new int[5];
        /// <summary>コメントを書いた回答だけの平均★ (★3 初期選択バイアスの比較材料、#463)。</summary>
        public double? AverageRatingWithComment { get; set; }
        public int CommentCount { get; set; }
        public List<PlayStatsComment> Comments { get; set; } = new List<PlayStatsComment>();

        // --- DB 由来 (メタデータ) ---
        public List<string> Genre { get; set; } = new List<string>();
        public int? Difficulty { get; set; }
        /// <summary>登録時に申告した想定プレイ時間の帯 (1=短い / 2=普通 / 3=長い)。実測との比較に使う。</summary>
        public int? PlayTimeBand { get; set; }
        public int? MinPlayers { get; set; }
        public int? MaxPlayers { get; set; }
        public bool IsVisible { get; set; }
        public string DevelopersDisplay { get; set; }
        public List<DeveloperInfo> Developers { get; set; } = new List<DeveloperInfo>();

        // --- 派生 ---
        /// <summary>
        /// 同じ PC で「終わってすぐもう一度」遊ばれた回数
        /// (<see cref="PlayStatsTuning.RepeatGapSeconds"/> 以内の同ゲーム再開)。
        /// 来場者がその場でもう 1 回やりたくなった度合いの代理指標。
        /// </summary>
        public int RepeatPlayCount { get; set; }

        /// <summary>回答率 = アンケート件数 / プレイ回数。プレイ 0 なら null。</summary>
        public double? ResponseRate
        {
            get { return PlayCount > 0 ? (double?)SurveyCount / PlayCount : null; }
        }

        /// <summary>★5 の割合。平均★と違い「刺さった人にどれだけ強く刺さったか」が出る。</summary>
        public double? FiveStarRate
        {
            get { return SurveyCount > 0 ? (double?)RatingCounts[4] / SurveyCount : null; }
        }

        /// <summary>平均★を額面通り読んでよいか (件数が閾値以上)。false なら UI で「参考値」と明示する。</summary>
        public bool RatingIsReliable
        {
            get { return SurveyCount >= PlayStatsTuning.MinSurveysForReliableAverage; }
        }
    }

    /// <summary>ジャンル / 難易度 / 期生 / 制作者などの「切り口別」1 行。</summary>
    public sealed class PlayStatsGroup
    {
        public string Key { get; set; }
        public int GameCount { get; set; }
        public int PlayCount { get; set; }
        public int SurveyCount { get; set; }
        public double? AverageRating { get; set; }
        /// <summary>この切り口に属するゲームのうち最も遊ばれたもの (UI の補足表示用)。</summary>
        public string TopGameTitle { get; set; }
    }

    /// <summary>時系列グラフの刻み。</summary>
    public enum PlayStatsTrendUnit
    {
        Minute15,
        Hour,
        Day,
        Week,
        Month,
        Year,
    }

    /// <summary>時系列グラフの 1 本。</summary>
    public sealed class PlayStatsTrendPoint
    {
        /// <summary>この本が受け持つ区間の開始時刻。</summary>
        public DateTime Start { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// 「いつ遊ばれたか」の時系列。**常に 1 本の連続した並び**で、日ごとに別のグラフを作らない。
    ///
    /// 日ごとに 1 枚ずつ描く作りだと、`responses/` が部活のたびに増えるぶんグラフの枚数が
    /// 際限なく増える (期間を広く取っても同じ)。刻みを期間の長さで決めれば、**本数は常に有界**になる。
    /// カレンダーが日表示と月表示を持つのと同じで、時間軸の粒度が期間に従うのは自然な振る舞い。
    ///
    /// 空の区間も点として持つ (連続した時間軸なので、間が空いていることも情報)。
    /// </summary>
    public sealed class PlayStatsTrend
    {
        public PlayStatsTrendUnit Unit { get; set; }
        public List<PlayStatsTrendPoint> Points { get; set; } = new List<PlayStatsTrendPoint>();
        /// <summary>いちばん多かった本の index。点が無ければ -1。</summary>
        public int PeakIndex { get; set; } = -1;
        public int PeakCount { get; set; }

        public bool HasData { get { return Points.Count > 0 && PeakCount > 0; } }

        /// <summary>「15 分ごと」など、いま何刻みで見ているかの表示。</summary>
        public string UnitLabel
        {
            get
            {
                switch (Unit)
                {
                    case PlayStatsTrendUnit.Minute15: return "15 分ごと";
                    case PlayStatsTrendUnit.Hour: return "1 時間ごと";
                    case PlayStatsTrendUnit.Day: return "1 日ごと";
                    case PlayStatsTrendUnit.Week: return "1 週ごと";
                    case PlayStatsTrendUnit.Month: return "1 か月ごと";
                    default: return "1 年ごと";
                }
            }
        }

        /// <summary>軸に出す目盛りの書式。</summary>
        public string TickFormat
        {
            get
            {
                switch (Unit)
                {
                    case PlayStatsTrendUnit.Minute15:
                    case PlayStatsTrendUnit.Hour: return "HH:mm";
                    case PlayStatsTrendUnit.Day:
                    case PlayStatsTrendUnit.Week: return "M/d";
                    case PlayStatsTrendUnit.Month: return "yyyy/M";
                    default: return "yyyy";
                }
            }
        }

        /// <summary>ピークの直接ラベル (時刻きざみなら時刻まで、日きざみなら日付まで)。</summary>
        public string PeakLabelFormat
        {
            get
            {
                switch (Unit)
                {
                    case PlayStatsTrendUnit.Minute15:
                    case PlayStatsTrendUnit.Hour: return "M/d HH:mm";
                    case PlayStatsTrendUnit.Day:
                    case PlayStatsTrendUnit.Week: return "M/d";
                    case PlayStatsTrendUnit.Month: return "yyyy/M";
                    default: return "yyyy";
                }
            }
        }
    }

    /// <summary>
    /// 全体アンケート (`trigger = launcher_end`、`game_no = 0`) の集計。ゲーム個別とは母集団も
    /// 問いも違うので合算せず別枠で出す。**★3 初期選択バイアス (#463) の検証材料**でもある
    /// (ゲーム個別と分布が揃えば操作導線由来、割れれば内容由来と読める)。
    /// </summary>
    public sealed class OverallSurveyStats
    {
        public int Count { get; set; }
        public double? AverageRating { get; set; }
        public int[] RatingCounts { get; set; } = new int[5];
        public int CommentCount { get; set; }
        public double? AverageRatingWithComment { get; set; }
        public List<PlayStatsComment> Comments { get; set; } = new List<PlayStatsComment>();
    }

    /// <summary>
    /// 表彰 1 つぶん。**受賞者は複数になりうる** (同点) ので名前は連結済みで持つ。
    ///
    /// 賞の軸は「互いに相関しにくいもの」を選んである — 回数・満足度・熱中・再プレイ・回答率・
    /// ★5 率・隠れた名作・制作者。全部が同じゲームに集まると表彰として機能しないため。
    /// 2026 年の実データでは 8 賞が 6 ゲームに散った。
    /// </summary>
    public sealed class PlayStatsAward
    {
        /// <summary>賞の名前 (「最多プレイ賞」など)。</summary>
        public string Title { get; set; }
        /// <summary>何で決めたか。読み手が納得できるよう必ず添える。</summary>
        public string Criterion { get; set; }
        /// <summary>受賞者の表示名 (同点は「・」で連結)。</summary>
        public string WinnerNames { get; set; }
        /// <summary>受賞の決め手になった数値の表示。</summary>
        public string ValueText { get; set; }
        /// <summary>補足 (「32 台で遊ばれました」など)。null なら出さない。</summary>
        public string Note { get; set; }
        public List<long> WinnerGameNos { get; set; } = new List<long>();
        public bool IsTie { get; set; }

        /// <summary>補足を出すか (同点だと個別の補足が他の受賞者に対して嘘になるので出さない)。</summary>
        public bool HasNote { get { return !string.IsNullOrEmpty(Note); } }
    }

    /// <summary>
    /// 集計 1 回分の結果。`responses/` の全走査 (<see cref="Services.PlayStatsLoader"/>) は 1 度だけ行い、
    /// 期間の切り替えはメモリ上の再集計で済ませる — SMB 越しの走査を日付タブを押すたびに走らせない。
    /// </summary>
    public sealed class PlayStatsSnapshot
    {
        /// <summary>この集計の対象期間。</summary>
        public PlayStatsRange Range { get; set; } = PlayStatsRange.All;

        // 概況
        public int TotalPlays { get; set; }
        public int TotalSurveys { get; set; }
        /// <summary>ゲーム個別アンケートの件数 (全体アンケートを除く)。</summary>
        public int GameSurveys { get; set; }
        public double? AverageRating { get; set; }
        public long TotalPlaySeconds { get; set; }
        public int PcCount { get; set; }
        public int PlayedGameCount { get; set; }
        public int RegisteredGameCount { get; set; }
        public DateTime? PeakAt { get; set; }
        public int PeakCount { get; set; }
        /// <summary>プレイ時間が異常で時間統計から外した件数 (0 でなければ画面に注記する)。</summary>
        public int OutlierDurationCount { get; set; }

        /// <summary>
        /// 期間全体の時間帯別プレイ数 (0〜23 時)。日をまたいでも同じ時刻に足し込む。
        /// 概況のスパークライン用。「何時に混むか」を答えるもので、期間のどこで盛り上がったかは
        /// <see cref="Trend"/> が答える (役割を分けることで、どちらも本数が有界になる)。
        /// </summary>
        public int[] HourCounts { get; set; } = new int[24];

        /// <summary>自動選出した表彰。受賞者がいない賞は入らない。</summary>
        public List<PlayStatsAward> Awards { get; set; } = new List<PlayStatsAward>();

        // --- 「この期間の記録」用 (表彰の合間に読み上げられるネタ) ---
        public DateTime? FirstPlayAt { get; set; }
        public long FirstPlayGameNo { get; set; }
        public DateTime? LastPlayAt { get; set; }
        public long LastPlayGameNo { get; set; }
        /// <summary>いちばんプレイ数を叩き出した PC 名。</summary>
        public string BusiestPc { get; set; }
        public int BusiestPcPlays { get; set; }

        public List<GameStats> Games { get; set; } = new List<GameStats>();
        public List<PlayStatsComment> Comments { get; set; } = new List<PlayStatsComment>();
        /// <summary>「いつ遊ばれたか」の時系列 (刻みは期間の長さで自動)。</summary>
        public PlayStatsTrend Trend { get; set; } = new PlayStatsTrend();
        public OverallSurveyStats Overall { get; set; } = new OverallSurveyStats();

        // 切り口別
        public List<PlayStatsGroup> ByGenre { get; set; } = new List<PlayStatsGroup>();
        public List<PlayStatsGroup> ByDifficulty { get; set; } = new List<PlayStatsGroup>();
        public List<PlayStatsGroup> ByGrade { get; set; } = new List<PlayStatsGroup>();
        public List<PlayStatsGroup> ByDeveloper { get; set; } = new List<PlayStatsGroup>();
    }
}
