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
        /// フォルダを最大 5 回 × 200ms 間隔でリトライ削除する (read-only 属性は ForceDeleteDirectory が再帰解除、#209)。
        /// `IOException` / `UnauthorizedAccessException` は transient lock とみなし retry、それ以外の想定外例外は retry せず
        /// 即 `Result` (`Success=false`, `LastError` セット) を返す。**本メソッドは throw しない (best-effort never-throw API)**
        /// (#288): cleanup / rollback の呼び出し側が全例外 swallow を各所で書かずに `Result.Success` だけ見れば済む。
        /// path が null/空、またはフォルダが存在しない場合は `Success=true` を返す。
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
                    // i==0 は通常フォルダの高速パス。失敗 (read-only / 一時ロック) したら以降は read-only を
                    // 外しながら削除する robust 再帰 (ForceDeleteDirectory) に切り替える。
                    if (i == 0)
                    {
                        Directory.Delete(path, true);
                    }
                    else
                    {
                        ForceDeleteDirectory(path);
                    }
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
                catch (Exception ex)
                {
                    // (#288 review) 想定外例外 (ArgumentException 等) は transient lock ではないので retry せず失敗を返す。
                    // TryDelete は best-effort 削除サービスとして「throw せず常に Result を返す」契約にし、cleanup /
                    // rollback の呼び出し側が「全例外 swallow」を各所で書かずに Result.Success だけ見れば済むようにする。
                    result.LastError = ex;
                    return result;
                }
            }
            return result;
        }

        /// <summary>
        /// (#209) read-only 属性を外しながらフォルダを再帰削除する。Unity / Godot のゲームプロジェクトフォルダは
        /// サブディレクトリ (Assets / Library / Packages 等) に read-only 属性が付き、`Directory.Delete` は read-only
        /// ディレクトリを消せず `UnauthorizedAccessException` で失敗する。各ディレクトリ/ファイルの read-only を
        /// **削除直前に** 外すことで確実に消す。
        ///
        /// **旧実装 (`GetFileSystemInfos("*", AllDirectories)` の単一呼び出しで read-only を一括解除) のバグ修正** (#209
        /// 実機): その API は深い/問題のあるパス (Unity Library 等の MAX_PATH 超) で **atomic に例外を投げ**、
        /// read-only 解除が丸ごと中断していた (浅いパスの単体テストでは素通り)。階層ごとに処理する本実装は、その
        /// 一括 throw を構造的に排除する。read-only でないファイル/ロック中ファイルは従来通り例外を投げ、TryDelete の
        /// retry に委ねる (= 一時ロックは retry で解消、純粋な ACL 拒否は最終的に失敗を返す)。
        /// </summary>
        private static void ForceDeleteDirectory(string dir)
        {
            // このディレクトリ自身の read-only を外す (空になった後の Directory.Delete で消せるように)。
            var di = new DirectoryInfo(dir);
            if ((di.Attributes & FileAttributes.ReadOnly) != 0)
            {
                di.Attributes &= ~FileAttributes.ReadOnly;
            }

            // 直下ファイル: read-only を外して削除。
            foreach (string file in Directory.GetFiles(dir))
            {
                FileAttributes attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                }
                File.Delete(file);
            }

            // 直下サブディレクトリ: 再帰。
            foreach (string sub in Directory.GetDirectories(dir))
            {
                ForceDeleteDirectory(sub);
            }

            // 空になった自身を削除 (read-only は上で解除済み)。
            Directory.Delete(dir, false);
        }
    }
}
