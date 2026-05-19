using System;
using System.Collections.Generic;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// ゲームのバージョン情報を表すデータモデル
    /// データベースのgame_versionsテーブルに対応
    /// </summary>
    public class GameVersion
    {
        /// <summary>
        /// バージョンID（自動採番）
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// ゲームID（外部キー）
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// バージョン名（例: 1.0.0）
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 実行ファイルのパス（バックアップまたは実体のパス）
        /// </summary>
        public string ExecutablePath { get; set; }

        /// <summary>
        /// 起動オプション（バージョン固有）
        /// </summary>
        public string Arguments { get; set; }

        /// <summary>
        /// ゲームの説明文
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 更新内容（バージョン更新時のメモ）
        /// </summary>
        public string UpdateNote { get; set; }

        /// <summary>
        /// ゲームタイトル（バージョン別）
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// ジャンルのリスト（バージョン別）
        /// </summary>
        public List<string> Genre { get; set; } = new List<string>();

        /// <summary>
        /// 最小プレイヤー数（バージョン別）
        /// </summary>
        public int? MinPlayers { get; set; }

        /// <summary>
        /// 最大プレイヤー数（バージョン別）
        /// </summary>
        public int? MaxPlayers { get; set; }

        /// <summary>
        /// 難易度（バージョン別）
        /// </summary>
        public int? Difficulty { get; set; }

        /// <summary>
        /// プレイ時間（バージョン別）
        /// </summary>
        public int? PlayTime { get; set; }

        /// <summary>
        /// コントローラーサポート（バージョン別）
        /// </summary>
        public bool ControllerSupport { get; set; }

        /// <summary>
        /// 通信対戦の対応状況（バージョン別）
        /// </summary>
        public int SupportedConnection { get; set; }

        /// <summary>
        /// サムネイル画像のパス（バージョン別）
        /// </summary>
        public string ThumbnailPath { get; set; }

        /// <summary>
        /// 背景画像のパス（バージョン別）
        /// </summary>
        public string BackgroundPath { get; set; }

        /// <summary>
        /// 製作者リスト（バージョン別）
        /// </summary>
        public List<DeveloperInfo> Developers { get; set; } = new List<DeveloperInfo>();

        /// <summary>
        /// 登録日時
        /// </summary>
        public DateTime RegisteredAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return Version;
        }
    }
}
