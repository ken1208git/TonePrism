using System;
using System.Collections.Generic;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲームジャンル一覧
    /// マイニンテンドーストアのソフトジャンルに準拠
    /// </summary>
    public static class GenreList
    {
        /// <summary>
        /// 利用可能なジャンルのリスト
        /// </summary>
        public static readonly List<string> AvailableGenres = new List<string>
        {
            "アクション",
            "アドベンチャー",
            "アーケード",
            "パズル",
            "RPG",
            "カジュアル",
            "シミュレーション",
            "シューティング",
            "ストラテジー",
            "その他",
            "ドライビング/レース",
            "ホラー",
            "ファミリー",
            "スポーツ",
            "シミュレーター",
            "格闘",
            "脳トレ",
            "パーティー",
            "リズムアクション",
            "クイズ",
            "教育",
            "フィットネス"
        };

        /// <summary>
        /// ジャンルが有効かどうかを確認
        /// </summary>
        /// <param name="genre">確認するジャンル名</param>
        /// <returns>有効なジャンルの場合true</returns>
        public static bool IsValidGenre(string genre)
        {
            return AvailableGenres.Contains(genre);
        }
    }
}

