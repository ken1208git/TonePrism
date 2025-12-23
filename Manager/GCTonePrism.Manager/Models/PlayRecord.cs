using System;

namespace GCTonePrism.Manager.Models
{
    /// <summary>
    /// プレイ記録を表すデータモデル
    /// データベースのplay_recordsテーブルに対応
    /// </summary>
    public class PlayRecord
    {
        /// <summary>
        /// プレイ記録ID（オートインクリメント）
        /// </summary>
        public int? Id { get; set; }

        /// <summary>
        /// ゲームID（games.game_idを参照）
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// プレイ回数
        /// </summary>
        public int PlayCount { get; set; }

        /// <summary>
        /// 合計プレイ時間（秒）
        /// </summary>
        public int TotalPlayTime { get; set; }

        /// <summary>
        /// 最終プレイ日時
        /// </summary>
        public DateTime? LastPlayedAt { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public PlayRecord()
        {
            PlayCount = 0;
            TotalPlayTime = 0;
        }
    }
}

