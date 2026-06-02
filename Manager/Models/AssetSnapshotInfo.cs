using System;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// (#250 PR1) 取得済みアセットスナップショット 1 世代分のメタ情報 (UI 表示用)。
    /// </summary>
    public class AssetSnapshotInfo
    {
        /// <summary>世代名のタイムスタンプ部 (yyyyMMdd_HHmmss、DB バックアップと同一規約)。</summary>
        public string Timestamp { get; set; }

        /// <summary>Timestamp をローカル日時に解釈したもの (UI 表示用)。解釈不能時は DateTime.MinValue。</summary>
        public DateTime StartedAtLocal { get; set; }

        /// <summary>"auto" / "manual"。</summary>
        public string TriggerType { get; set; }

        /// <summary>取得した PC 名 (世代名の _host 部、無ければ空)。</summary>
        public string Host { get; set; }

        /// <summary>世代内のファイル総数。</summary>
        public int FileCount { get; set; }

        /// <summary>世代内ファイルの論理合計バイト数 (ハードリンク共有を考慮しない素の合計)。</summary>
        public long LogicalBytes { get; set; }

        /// <summary>この世代でハードリンク共有を使ったか (false = 全実コピー)。</summary>
        public bool UsedHardLinks { get; set; }

        /// <summary>世代ディレクトリの絶対パス。</summary>
        public string DirectoryPath { get; set; }
    }

    /// <summary>
    /// (#250 PR1) `AssetSnapshotService.CreateSnapshot` の結果。BackupResult に倣う軽量型。
    /// best-effort のため throw せずこの型で成否を返す。
    /// </summary>
    public class SnapshotResult
    {
        public enum ResultKind { Success, Skipped, Failed }

        public ResultKind Kind { get; private set; }
        public string DirectoryPath { get; private set; }
        public int FileCount { get; private set; }
        public long LogicalBytes { get; private set; }
        public bool UsedHardLinks { get; private set; }
        /// <summary>Skipped の理由 / Failed のメッセージ。</summary>
        public string Message { get; private set; }

        public bool IsSuccess => Kind == ResultKind.Success;
        public bool IsSkipped => Kind == ResultKind.Skipped;
        public bool IsFailed => Kind == ResultKind.Failed;

        public static SnapshotResult Success(string dir, int fileCount, long logicalBytes, bool usedHardLinks)
            => new SnapshotResult { Kind = ResultKind.Success, DirectoryPath = dir, FileCount = fileCount, LogicalBytes = logicalBytes, UsedHardLinks = usedHardLinks };

        public static SnapshotResult Skipped(string reason)
            => new SnapshotResult { Kind = ResultKind.Skipped, Message = reason };

        public static SnapshotResult Failed(string message)
            => new SnapshotResult { Kind = ResultKind.Failed, Message = message };
    }
}
