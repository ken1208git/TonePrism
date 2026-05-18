using System;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// 製作者情報を表すデータモデル
    /// データベースのdevelopersテーブルに対応
    /// </summary>
    public class DeveloperInfo
    {
        /// <summary>
        /// 製作者ID（オートインクリメント）
        /// </summary>
        public int? Id { get; set; }

        /// <summary>
        /// ゲームID（games.game_idを参照）
        /// </summary>
        public string GameId { get; set; }

        /// <summary>
        /// 姓
        /// </summary>
        public string LastName { get; set; }

        /// <summary>
        /// 名
        /// </summary>
        public string FirstName { get; set; }

        /// <summary>
        /// 期生（0を指定すると「教員」と表記）
        /// </summary>
        public string Grade { get; set; }

        /// <summary>
        /// フルネームを取得
        /// </summary>
        public string FullName
        {
            get 
            { 
                if (string.IsNullOrEmpty(LastName))
                {
                    return FirstName;
                }
                return LastName + " " + FirstName; 
            }
        }

        /// <summary>
        /// 期生表示を取得（0の場合は「教員」と表示）
        /// </summary>
        public string GradeDisplay
        {
            get
            {
                if (Grade == "0")
                {
                    return "教員";
                }
                if (string.IsNullOrEmpty(Grade))
                {
                    return "";
                }
                return Grade + "期生";
            }
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public DeveloperInfo()
        {
            // デフォルト値の設定
        }
    }
}

