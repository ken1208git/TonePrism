using System;
using System.Collections.Generic;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// ゲーム情報を表すデータモデル
    /// データベースのgamesテーブルに対応
    /// </summary>
    public class GameInfo
    {
        /// <summary>
        /// ゲームID（一意の識別子）
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// ゲームタイトル
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 説明文
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// リリース年
        /// </summary>
        public int? ReleaseYear { get; set; }

        /// <summary>
        /// ジャンルのリスト（データベースではJSON形式またはカンマ区切りで保存）
        /// </summary>
        public List<string> Genre { get; set; }

        /// <summary>
        /// 最小プレイヤー数
        /// </summary>
        public int? MinPlayers { get; set; }

        /// <summary>
        /// 最大プレイヤー数
        /// </summary>
        public int? MaxPlayers { get; set; }

        /// <summary>
        /// 難易度（1-3の3段階）
        /// 1: 易しい, 2: 普通, 3: 難しい
        /// </summary>
        public int? Difficulty { get; set; }

        /// <summary>
        /// プレイ時間の分類
        /// 1: ～5分, 2: 5分～15分, 3: 15分以上
        /// </summary>
        public int? PlayTime { get; set; }

        /// <summary>
        /// コントローラーサポート
        /// </summary>
        public bool ControllerSupport { get; set; }
        
        /// <summary>
        /// 通信対戦の対応状況
        /// 0: なし (オフラインのみ)
        /// 1: ローカル通信 (LAN)
        /// 2: オンライン通信 (WAN)
        /// </summary>
        public int SupportedConnection { get; set; }

        /// <summary>
        /// サムネイル画像のパス
        /// </summary>
        public string ThumbnailPath { get; set; }

        /// <summary>
        /// 背景画像のパス
        /// </summary>
        public string BackgroundPath { get; set; }

        /// <summary>
        /// 実行ファイルのパス
        /// </summary>
        public string ExecutablePath { get; set; }

        /// <summary>
        /// 起動オプション（引数）
        /// </summary>
        public string Arguments { get; set; }

        /// <summary>
        /// 表示順序（数値が小さいほど先に表示）
        /// </summary>
        public int? DisplayOrder { get; set; }

        /// <summary>
        /// 表示/非表示
        /// </summary>
        public bool IsVisible { get; set; }

        /// <summary>
        /// 操作説明（JSON形式で保存）
        /// </summary>
        public string Controls { get; set; }

        /// <summary>
        /// キーマッピング設定（JSON形式で保存）
        /// </summary>
        public string KeyMapping { get; set; }

        /// <summary>
        /// 最新バージョン（表示用）
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 製作者リスト（データベースではdevelopersテーブルとして分離）
        /// </summary>
        public List<DeveloperInfo> Developers { get; set; }

        /// <summary>
        /// 製作者情報の表示用文字列（DataGridView用）
        /// 「姓 名 (期生)」形式で複数の製作者をカンマ区切りで表示
        /// </summary>
        public string DevelopersDisplay
        {
            get
            {
                if (Developers == null || Developers.Count == 0)
                {
                    return "";
                }

                var displayList = new List<string>();
                foreach (var dev in Developers)
                {
                    string display = dev.FullName;
                    if (!string.IsNullOrEmpty(dev.GradeDisplay))
                    {
                        display += " (" + dev.GradeDisplay + ")";
                    }
                    displayList.Add(display);
                }

                return string.Join(", ", displayList);
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public GameInfo()
        {
            Genre = new List<string>();
            Developers = new List<DeveloperInfo>();
            ControllerSupport = false;
            IsVisible = true;
        }
    }
}

