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
        /// プレイ時間の統計に入れてよい値か。下限は 0 秒 (負 = 時計が巻き戻った)、上限は
        /// <see cref="PlayStatsTuning.MaxSaneDurationSeconds"/>。上限を超えるのは「遊び終わったのに
        /// ゲームを閉じずに放置した」ケースで、実際の遊びの長さを表していない。
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
        /// <summary>回答率 = アンケート件数 / プレイ回数。プレイ 0 なら null。</summary>
        public double? ResponseRate
        {
            get { return PlayCount > 0 ? (double?)SurveyCount / PlayCount : null; }
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

    /// <summary>1 日分の時間帯ヒストグラム (15 分刻み)。</summary>
    public sealed class PlayStatsTimeline
    {
        public DateTime Date { get; set; }
        /// <summary>15 分バケットごとのプレイ数 (1 日 96 本)。</summary>
        public int[] Buckets { get; set; }
        public int PeakCount { get; set; }
        /// <summary>ピークのバケット先頭時刻。</summary>
        public DateTime PeakAt { get; set; }
        public int Total { get; set; }
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
    /// 集計 1 回分の結果。`responses/` の全走査 (<see cref="Services.PlayStatsLoader"/>) は 1 度だけ行い、
    /// 期間の切り替えはメモリ上の再集計で済ませる — SMB 越しの走査を日付タブを押すたびに走らせない。
    /// </summary>
    public sealed class PlayStatsSnapshot
    {
        /// <summary>この集計の対象日。null = 全期間。</summary>
        public DateTime? Day { get; set; }

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

        public List<GameStats> Games { get; set; } = new List<GameStats>();
        public List<PlayStatsComment> Comments { get; set; } = new List<PlayStatsComment>();
        public List<PlayStatsTimeline> Timelines { get; set; } = new List<PlayStatsTimeline>();
        public OverallSurveyStats Overall { get; set; } = new OverallSurveyStats();

        // 切り口別
        public List<PlayStatsGroup> ByGenre { get; set; } = new List<PlayStatsGroup>();
        public List<PlayStatsGroup> ByDifficulty { get; set; } = new List<PlayStatsGroup>();
        public List<PlayStatsGroup> ByGrade { get; set; } = new List<PlayStatsGroup>();
        public List<PlayStatsGroup> ByDeveloper { get; set; } = new List<PlayStatsGroup>();
    }
}
