using System;

namespace TonePrism.Manager.Models
{
    /// <summary>
    /// (#250 PR1) 取得済みアセットスナップショット 1 世代分のメタ情報 (UI 表示用)。共有プール方式では
    /// 1 世代 = 1 manifest ファイル。
    /// </summary>
    public class AssetSnapshotInfo
    {
        /// <summary>世代名のタイムスタンプ部 (yyyyMMdd_HHmmss、DB バックアップと同一規約)。</summary>
        public string Timestamp { get; set; }

        /// <summary>Timestamp をローカル日時に解釈したもの (UI 表示用)。解釈不能時は DateTime.MinValue。</summary>
        public DateTime StartedAtLocal { get; set; }

        /// <summary>"auto" / "manual"。</summary>
        public string TriggerType { get; set; }

        /// <summary>取得した PC 名 (manifest 名の _host 部、無ければ空)。</summary>
        public string Host { get; set; }

        /// <summary>この世代に含まれるファイル総数。</summary>
        public int FileCount { get; set; }

        /// <summary>この世代の論理合計バイト数 (重複排除前＝games/guide をそのまま足した値)。</summary>
        public long LogicalBytes { get; set; }

        /// <summary>manifest ファイルの絶対パス。</summary>
        public string ManifestPath { get; set; }
    }

    /// <summary>
    /// (#250 PR1) `AssetSnapshotService.CreateSnapshot` の結果。best-effort のため throw せずこの型で成否を返す。
    /// </summary>
    public class SnapshotResult
    {
        public enum ResultKind { Success, Skipped, Failed }

        public ResultKind Kind { get; private set; }
        public string ManifestPath { get; private set; }
        public int FileCount { get; private set; }
        public long LogicalBytes { get; private set; }
        /// <summary>今回このバックアップで実際にプールへ新規コピーしたバイト数 (= ディスク増分)。</summary>
        public long NewBytesCopied { get; private set; }
        /// <summary>Skipped の理由 / Failed のメッセージ。</summary>
        public string Message { get; private set; }
        /// <summary>「想定外で控えられなかった」異常 (SMB 不達等での sources 欠損)。Failed と合わせて UI で警告を出す。
        /// 通常の Skipped (設定で無効 / キャンセル) は false。</summary>
        public bool IsAnomaly { get; private set; }

        public bool IsSuccess => Kind == ResultKind.Success;
        public bool IsSkipped => Kind == ResultKind.Skipped;
        public bool IsFailed => Kind == ResultKind.Failed;

        public static SnapshotResult Success(string manifestPath, int fileCount, long logicalBytes, long newBytesCopied)
            => new SnapshotResult { Kind = ResultKind.Success, ManifestPath = manifestPath, FileCount = fileCount, LogicalBytes = logicalBytes, NewBytesCopied = newBytesCopied };

        public static SnapshotResult Skipped(string reason)
            => new SnapshotResult { Kind = ResultKind.Skipped, Message = reason };

        /// <summary>異常 (sources 欠損等) による Skipped。UI で「控えられなかった」と警告する。</summary>
        public static SnapshotResult SkippedAnomaly(string reason)
            => new SnapshotResult { Kind = ResultKind.Skipped, Message = reason, IsAnomaly = true };

        public static SnapshotResult Failed(string message)
            => new SnapshotResult { Kind = ResultKind.Failed, Message = message };
    }
}
