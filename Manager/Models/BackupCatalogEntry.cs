using System;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// バックアップファイル 1 件に対応するカタログエントリ。
    ///
    /// 旧 `backup_log` テーブル (DB v19 で DROP) を置き換える。履歴は DB ではなく `backups/` フォルダの
    /// 走査結果 (<see cref="TonePrism.Manager.Services.BackupCatalogService"/>) から、ファイル名・フォルダ位置・
    /// <see cref="System.IO.FileInfo"/> を解釈して構築する。
    ///
    /// identity は絶対ファイルパス (<see cref="FilePath"/>)。DB の id 相当・status・error_message・
    /// relative_path は持たない (= 存在するファイル = 成功・絶対パス identity)。
    /// </summary>
    public class BackupCatalogEntry
    {
        /// <summary>バックアップファイルの絶対パス。grid の row.Tag / 復元 / 削除の identity。</summary>
        public string FilePath { get; set; }

        /// <summary>
        /// "auto" | "manual" | "safety" | "unknown"。**フォルダ位置で確定**する
        /// (`auto/`→auto、`manual/`→manual、`safety/`→safety、保存先直下の旧フラット形式
        /// `toneprism_*.db` / `prism_*.db`→unknown = v0.20.0 以前で種類がファイル名に無く復元不能)。
        /// </summary>
        public string TriggerType { get; set; }

        /// <summary>ファイル名のタイムスタンプ由来 (UNIX秒、ローカル時刻→UTC換算)。</summary>
        public long StartedAt { get; set; }

        /// <summary>ファイル名の host 部から抽出した実行 PC 名。host を持たない旧形式ファイルは空文字。</summary>
        public string PcName { get; set; }

        /// <summary><see cref="System.IO.FileInfo.Length"/>。</summary>
        public long FileSizeBytes { get; set; }

        /// <summary>StartedAt をローカルタイムの DateTime に変換。</summary>
        public DateTime StartedAtLocal
        {
            get { return DateTimeOffset.FromUnixTimeSeconds(StartedAt).LocalDateTime; }
        }
    }
}
