using System;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// アンケート結果を表すデータモデル
    /// データベースのsurveysテーブルに対応
    /// </summary>
    public class Survey
    {
        /// <summary>
        /// アンケートID（UUID）
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// ゲームID（games.game_idを参照）
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// 回答日時
        /// </summary>
        public DateTime SubmittedAt { get; set; }

        /// <summary>
        /// アンケート回答内容（JSON形式で保存）
        /// </summary>
        public string Responses { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Survey()
        {
            Id = Guid.NewGuid().ToString();
            SubmittedAt = DateTime.Now;
        }
    }
}

