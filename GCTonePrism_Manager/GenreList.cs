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
            "シューティング",
            "ロールプレイング",
            "シミュレーション",
            "スポーツ",
            "格闘",
            "レース",
            "音楽ゲーム",
            "パズル",
            "テーブルゲーム",
            "パーティー",
            "コミュニケーション",
            "学習・教育",
            "トレーニング",
            "ツール"
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

