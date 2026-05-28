using System;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// backup_log テーブルの1行に対応するモデル
    /// </summary>
    public class BackupLogEntry
    {
        public long Id { get; set; }
        public long StartedAt { get; set; }       // UNIX秒
        public long? CompletedAt { get; set; }    // UNIX秒、進行中はnull
        public string PcName { get; set; }
        public string FilePath { get; set; }
        /// <summary>
        /// toneprism.db のあるディレクトリからの相対パス (#126 で追加)。
        /// プロジェクト場所の移動に追従できるよう、絶対パス (FilePath) と併せて記録する。
        /// マイグレーション前のレコードでは null。表示・復元時は BackupPathResolver で解決する。
        /// </summary>
        public string RelativePath { get; set; }
        public long? FileSizeBytes { get; set; }
        public string Status { get; set; }        // "in_progress" | "success" | "failed"
        public string ErrorMessage { get; set; }
        public string TriggerType { get; set; }   // "manual" | "auto" | "safety" | "restore"  (v10 で 'safety' / v16 で 'restore' 追加)

        /// <summary>
        /// StartedAt をローカルタイムの DateTime に変換
        /// </summary>
        public DateTime StartedAtLocal
        {
            get { return DateTimeOffset.FromUnixTimeSeconds(StartedAt).LocalDateTime; }
        }

        /// <summary>
        /// CompletedAt をローカルタイムの DateTime に変換（nullable）
        /// </summary>
        public DateTime? CompletedAtLocal
        {
            get
            {
                if (CompletedAt.HasValue)
                    return DateTimeOffset.FromUnixTimeSeconds(CompletedAt.Value).LocalDateTime;
                return null;
            }
        }
    }
}
