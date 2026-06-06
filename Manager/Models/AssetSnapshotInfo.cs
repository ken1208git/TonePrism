using System;
using System.Collections.Generic;

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

        /// <summary>(round8 C1) 取得は成功 (Success) したが、深部フォルダの列挙失敗 (SMB 一過性 I/O / 権限等) で
        /// 一部フォルダを skip しており **この世代が部分的な控えの可能性がある** こと。完全失敗 (Failed) でも
        /// 異常 skip (IsAnomaly) でもないが、緑チェック＝完全控えと誤認させないため UI/ログで警告する。</summary>
        public bool IsPartial { get; private set; }

        /// <summary>(round8 C1) 列挙できず skip した深部フォルダ数 (IsPartial の根拠)。</summary>
        public int SkippedDirCount { get; private set; }

        /// <summary>(#299 review #1) 並行編集での消失 / ロック等で控えられず skip した個別ファイル数 (IsPartial の根拠)。
        /// SkippedDirCount (フォルダ列挙失敗) と原因カテゴリが異なるため別フィールドで持ち、UI 文言を実態に合わせる
        /// (合算して 1 フィールドに詰めると「N 個のフォルダ」と誤報する)。</summary>
        public int SkippedFileCount { get; private set; }

        public bool IsSuccess => Kind == ResultKind.Success;
        public bool IsSkipped => Kind == ResultKind.Skipped;
        public bool IsFailed => Kind == ResultKind.Failed;

        public static SnapshotResult Success(string manifestPath, int fileCount, long logicalBytes, long newBytesCopied, int skippedDirCount = 0, int skippedFileCount = 0)
            => new SnapshotResult { Kind = ResultKind.Success, ManifestPath = manifestPath, FileCount = fileCount, LogicalBytes = logicalBytes, NewBytesCopied = newBytesCopied, SkippedDirCount = skippedDirCount, SkippedFileCount = skippedFileCount, IsPartial = (skippedDirCount + skippedFileCount) > 0 };

        public static SnapshotResult Skipped(string reason)
            => new SnapshotResult { Kind = ResultKind.Skipped, Message = reason };

        /// <summary>異常 (sources 欠損等) による Skipped。UI で「控えられなかった」と警告する。</summary>
        public static SnapshotResult SkippedAnomaly(string reason)
            => new SnapshotResult { Kind = ResultKind.Skipped, Message = reason, IsAnomaly = true };

        public static SnapshotResult Failed(string message)
            => new SnapshotResult { Kind = ResultKind.Failed, Message = message };
    }

    /// <summary>
    /// (#250 PR3) `AssetRestoreService.RestoreFromManifest` の結果。best-effort のため per-file 失敗は throw せず
    /// この型で返す。全体失敗 (manifest 不在/読めない・空ガード発動) は <see cref="ResultKind.Failed"/>、
    /// per-file 失敗を含むが完走したら <see cref="ResultKind.Success"/> + <see cref="IsPartial"/>。
    /// </summary>
    public class AssetRestoreResult
    {
        public enum ResultKind { Success, Skipped, Failed }

        public ResultKind Kind { get; private set; }
        /// <summary>pool から live へ新規/更新コピーしたファイル数。</summary>
        public int CopiedCount { get; private set; }
        /// <summary>live が既に manifest と一致 (size+mtime) で再コピーを省いたファイル数。</summary>
        public int SkippedCount { get; private set; }
        /// <summary>manifest に無い余剰 live ファイルを削除した数。</summary>
        public int DeletedCount { get; private set; }
        /// <summary>per-file で失敗した数 (pool blob 不在・I/O・パストラバーサル拒否等)。&gt;0 で IsPartial。</summary>
        public int FailedCount { get; private set; }
        /// <summary>Skipped(全体)/Failed の理由。</summary>
        public string Message { get; private set; }
        /// <summary>pool に blob が無く復元できなかった relpath 群 (live は保持＝削除しない)。UI/ログ用。</summary>
        public List<string> MissingBlobRelPaths { get; } = new List<string>();

        /// <summary>(#250 PR3a review #1/#2) manifest が不完全 (部分取得 or 破損行) のため余剰削除を抑止したか。
        /// true のとき live に manifest 外の余剰が残りうる (= 完全一致でない)。UI で「完全には戻していない」旨を出す材料。</summary>
        public bool DeletionSuppressed { get; private set; }

        public bool IsSuccess => Kind == ResultKind.Success;
        public bool IsSkipped => Kind == ResultKind.Skipped;
        public bool IsFailed => Kind == ResultKind.Failed;
        /// <summary>完走したが per-file 失敗 or 削除抑止を含む (= 緑チェックにせず警告すべき)。</summary>
        public bool IsPartial => Kind == ResultKind.Success && (FailedCount > 0 || DeletionSuppressed);

        public static AssetRestoreResult Success(int copied, int skipped, int deleted, int failed, List<string> missingBlobs = null, bool deletionSuppressed = false)
        {
            var r = new AssetRestoreResult { Kind = ResultKind.Success, CopiedCount = copied, SkippedCount = skipped, DeletedCount = deleted, FailedCount = failed, DeletionSuppressed = deletionSuppressed };
            if (missingBlobs != null) r.MissingBlobRelPaths.AddRange(missingBlobs);
            return r;
        }

        public static AssetRestoreResult Skipped(string reason)
            => new AssetRestoreResult { Kind = ResultKind.Skipped, Message = reason };

        public static AssetRestoreResult Failed(string message)
            => new AssetRestoreResult { Kind = ResultKind.Failed, Message = message };
    }
}
