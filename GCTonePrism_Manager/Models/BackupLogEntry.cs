using System;

namespace GCTonePrism.Manager.Models
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
        public long? FileSizeBytes { get; set; }
        public string Status { get; set; }        // "in_progress" | "success" | "failed"
        public string ErrorMessage { get; set; }
        public string TriggerType { get; set; }   // "manual" | "auto"

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
