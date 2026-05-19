using System;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// システム設定を表すデータモデル
    /// データベースのsettingsテーブルに対応
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// 設定ID（常に1）
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// カラーテーマ設定（JSON形式で保存）
        /// </summary>
        public string ColorTheme { get; set; }

        /// <summary>
        /// ランチャー設定（JSON形式で保存）
        /// </summary>
        public string LauncherSettings { get; set; }

        /// <summary>
        /// フィルター設定（JSON形式で保存）
        /// </summary>
        public string FilterSettings { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public Settings()
        {
            Id = 1; // 常に1
        }
    }
}

