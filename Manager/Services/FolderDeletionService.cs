using System;
using System.IO;
using System.Threading;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// フォルダ削除のリトライ機構を提供する static service。
    /// SMB 共有上では Launcher 等が一瞬ファイルを掴むケースがあるため、
    /// 短時間のリトライで大半の競合を解消できる。それでも失敗した場合は
    /// 呼び出し側がユーザーに「再試行 / 諦める」を選ばせる UI を出す前提。
    /// (#122 Group C 実装。RestoreService.DeleteWithRetry と同じ 5 回 × 200ms パターン)
    /// </summary>
    public static class FolderDeletionService
    {
        public class Result
        {
            public bool Success { get; set; }
            public Exception LastError { get; set; }
            public string Path { get; set; }
        }

        /// <summary>
        /// フォルダを最大 5 回 × 200ms 間隔でリトライ削除する。
        /// IOException / UnauthorizedAccessException のみ捕捉、それ以外は throw。
        /// path が null/空、またはフォルダが存在しない場合は Success=true を返す。
        /// </summary>
        public static Result TryDelete(string path)
        {
            var result = new Result { Path = path };
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                result.Success = true;
                return result;
            }

            const int maxRetries = 5;
            const int delayMs = 200;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    result.Success = true;
                    return result;
                }
                catch (IOException ex)
                {
                    result.LastError = ex;
                    if (i == maxRetries - 1) return result;
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException ex)
                {
                    result.LastError = ex;
                    // (#209) UnauthorizedAccessException は read-only 属性のファイル/ディレクトリが原因のことが多い。
                    // Unity / Godot 等のゲームプロジェクトフォルダはサブディレクトリ (Assets / Library / Packages 等) に
                    // read-only 属性が付くことがあり、Directory.Delete はファイル削除後に read-only ディレクトリを消せず
                    // UnauthorizedAccessException を投げる (= retry だけでは永久に解消しない)。read-only を再帰的に外して
                    // から次の試行に進む。これでも残る純粋な ACL 拒否は retry 後に呼び出し側へ失敗を返す。
                    TryClearReadOnlyRecursive(path);
                    if (i == maxRetries - 1) return result;
                    Thread.Sleep(delayMs);
                }
            }
            return result;
        }

        /// <summary>
        /// (#209) path 配下のファイル/ディレクトリの read-only 属性を再帰的に外す (best-effort)。
        /// `Directory.Delete` は read-only なファイル/ディレクトリに当たると UnauthorizedAccessException を投げるため、
        /// 削除リトライ前に外す。個々の失敗 (ACL 拒否等) は握り潰し、外せた分だけでも削除が進むことを優先する。
        /// </summary>
        private static void TryClearReadOnlyRecursive(string path)
        {
            try
            {
                var root = new DirectoryInfo(path);
                if (!root.Exists) return;
                ClearReadOnly(root);
                // ファイル + ディレクトリの両方 (GetFileSystemInfos) を再帰的に処理。read-only ディレクトリも対象。
                foreach (var info in root.GetFileSystemInfos("*", SearchOption.AllDirectories))
                {
                    ClearReadOnly(info);
                }
            }
            catch { /* best-effort: 列挙自体が失敗しても削除リトライ側に委ねる */ }
        }

        private static void ClearReadOnly(FileSystemInfo info)
        {
            try
            {
                if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    info.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
            catch { /* 個別の属性変更失敗は無視 */ }
        }
    }
}
