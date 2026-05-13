using System;
using System.IO;
using System.Threading;

namespace GCTonePrism.Manager.Services
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
                    if (i == maxRetries - 1) return result;
                    Thread.Sleep(delayMs);
                }
            }
            return result;
        }
    }
}
