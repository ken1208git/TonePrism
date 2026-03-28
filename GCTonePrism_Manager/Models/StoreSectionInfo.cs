using System.Collections.Generic;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// ストアセクション情報を表すデータモデル
    /// データベースのstore_sectionsテーブルに対応
    /// </summary>
    public class StoreSectionInfo
    {
        public int SectionId { get; set; }
        public string Title { get; set; }

        /// <summary>
        /// セクションタイプ: 0=通常カテゴリ行, 1=スライドショー, 2=タイルグリッド
        /// </summary>
        public int SectionType { get; set; }

        /// <summary>
        /// セクションソース: 'manual','popular','recent','recently_played',
        /// 'genre:ジャンル名','players_min:N','players_max:N','difficulty:N',
        /// 'play_time:N','online','random','controller'
        /// </summary>
        public string SectionSource { get; set; } = "manual";

        public int DisplayOrder { get; set; }
        public int MaxDisplayCount { get; set; } = 5;
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// このセクションに紐付けられたゲーム一覧（manual時はstore_section_gamesから取得）
        /// </summary>
        public List<GameInfo> Games { get; set; } = new List<GameInfo>();

        /// <summary>
        /// ゲームごとの表示テキスト（GameId → display_text）
        /// スライドショーやタイルグリッドでゲームタイトルの代わりに表示するカスタムテキスト
        /// 空の場合はゲームタイトルを表示
        /// </summary>
        public Dictionary<string, string> GameDisplayTexts { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// セクションタイプの表示名
        /// </summary>
        public string SectionTypeDisplay
        {
            get
            {
                switch (SectionType)
                {
                    case 1: return "スライドショー";
                    case 2: return "タイルグリッド";
                    default: return "通常カテゴリ行";
                }
            }
        }

        /// <summary>
        /// セクションソースの表示名
        /// </summary>
        public string SectionSourceDisplay
        {
            get
            {
                if (SectionSource == null) return "手動";
                if (SectionSource == "manual") return "手動";
                if (SectionSource == "popular") return "人気ランキング";
                if (SectionSource == "recent") return "新作";
                if (SectionSource == "recently_played") return "最近プレイ";
                if (SectionSource.StartsWith("genre:")) return "ジャンル: " + SectionSource.Substring(6);
                if (SectionSource.StartsWith("players_min:")) return "プレイ人数(以上): " + SectionSource.Substring(12);
                if (SectionSource.StartsWith("players_max:")) return "プレイ人数(以下): " + SectionSource.Substring(12);
                if (SectionSource.StartsWith("difficulty:")) return "難易度: " + SectionSource.Substring(11);
                if (SectionSource.StartsWith("play_time:")) return "プレイ時間: " + SectionSource.Substring(10);
                if (SectionSource == "online") return "通信プレイ";
                if (SectionSource == "random") return "ランダム";
                if (SectionSource == "controller") return "コントローラー";
                return SectionSource;
            }
        }
    }
}
