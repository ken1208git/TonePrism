using System;
using System.IO;
using System.Linq;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// ファイル・ディレクトリ操作の共通サービス。
    /// AddGameForm / MainForm で使われるコピー処理を共通化する。
    /// </summary>
    public static class FileOperationService
    {
        /// <summary>
        /// ゲームエンジン/開発環境の不要フォルダ一覧
        /// </summary>
        public static readonly string[] ExcludedFolders = new[]
        {
            "Library", "Temp", "Logs", "Build", "Builds",
            "Intermediate", "Saved", "DerivedDataCache",
            ".import",
            ".vs", ".idea", ".vscode",
            ".git", ".svn", ".hg",
            "node_modules",
            "__pycache__", ".pytest_cache", ".mypy_cache"
        };

        /// <summary>
        /// 指定ディレクトリ配下のファイル数を再帰的にカウント
        /// </summary>
        public static int CountFiles(string dir, Func<string, bool> excludeFolderPredicate = null)
        {
            int count = 0;
            try
            {
                count += Directory.GetFiles(dir).Length;

                foreach (string subDir in Directory.GetDirectories(dir))
                {
                    string folderName = Path.GetFileName(subDir);

                    if (ExcludedFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (excludeFolderPredicate != null && excludeFolderPredicate(folderName))
                        continue;

                    count += CountFiles(subDir, excludeFolderPredicate);
                }
            }
            catch { }
            return count;
        }

        /// <summary>
        /// ディレクトリを再帰的にコピー（進捗レポート付き）
        /// </summary>
        public static void CopyDirectoryRecursive(
            string sourceDir,
            string destDir,
            IProgress<ProgressInfo> progress,
            System.Threading.CancellationToken token,
            int totalFiles,
            ref int copiedFiles,
            Func<string, bool> excludeFolderPredicate = null)
        {
            token.ThrowIfCancellationRequested();

            string safeDestDir = EnsureLongPath(destDir);
            Directory.CreateDirectory(safeDestDir);

            string fullSourceDir = NormalizePath(sourceDir);
            string fullDestDir = NormalizePath(destDir);

            // 親子関係ガード（パス区切り文字を含めて比較し、Foo と Foo2 の誤マッチを防止）
            string sourceDirWithSep = fullSourceDir.EndsWith("\\") ? fullSourceDir : fullSourceDir + "\\";
            if (fullDestDir.StartsWith(sourceDirWithSep, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullDestDir, fullSourceDir, StringComparison.OrdinalIgnoreCase))
                return;

            // ファイルコピー
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = EnsureLongPath(Path.Combine(safeDestDir, fileName));
                    string sourceFile = EnsureLongPath(file);

                    File.Copy(sourceFile, destFile, true);

                    copiedFiles++;
                    int percentage = totalFiles > 0 ? (int)((double)copiedFiles / totalFiles * 100) : 100;
                    progress.Report(new ProgressInfo(percentage, "ファイルをコピー中...", fileName));
                }
                catch (PathTooLongException)
                {
                    Logger.Warn($"[警告] パスが長すぎるためスキップ: {file}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[警告] ファイルのコピーに失敗: {file}", ex);
                }
            }

            // サブディレクトリコピー
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    string folderName = Path.GetFileName(subDir);
                    string fullSubPath = NormalizePath(subDir);

                    // コピー先自身やその親への再帰を防止
                    if (fullSubPath.Equals(fullDestDir, StringComparison.OrdinalIgnoreCase) ||
                        fullDestDir.StartsWith(fullSubPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ExcludedFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (excludeFolderPredicate != null && excludeFolderPredicate(folderName))
                        continue;

                    string destSubDir = Path.Combine(safeDestDir, folderName);
                    CopyDirectoryRecursive(subDir, destSubDir, progress, token, totalFiles, ref copiedFiles, excludeFolderPredicate);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[警告] サブフォルダのコピーに失敗: {subDir}", ex);
                }
            }
        }

        /// <summary>
        /// ファイル数カウント → ディレクトリコピーを一括実行
        /// </summary>
        public static void CopyDirectoryWithProgress(
            string sourceDir,
            string destDir,
            IProgress<ProgressInfo> progress,
            System.Threading.CancellationToken token,
            Func<string, bool> excludeFolderPredicate = null)
        {
            progress.Report(new ProgressInfo(0, "ファイル数を計算中..."));
            int totalFiles = CountFiles(sourceDir, excludeFolderPredicate);
            int copiedFiles = 0;
            CopyDirectoryRecursive(sourceDir, destDir, progress, token, totalFiles, ref copiedFiles, excludeFolderPredicate);
        }

        /// <summary>
        /// パスを正規化（\\?\ プレフィックスの除去と絶対パス化）
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (path.StartsWith(@"\\?\")) path = path.Substring(4);
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// 長いパス名のサポート（260文字超のパスに \\?\ プレフィックスを付与）
        /// </summary>
        public static string EnsureLongPath(string path)
        {
            if (path.Length >= 240 && !path.StartsWith(@"\\?\"))
                return @"\\?\" + Path.GetFullPath(path);
            return path;
        }

        /// <summary>
        /// ファイル名として使用可能な文字列に変換
        /// </summary>
        public static string CleanFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        /// <summary>
        /// バージョンフォルダかどうかを判定（v + 数字 で始まるか）
        /// </summary>
        public static bool IsVersionFolder(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return false;
            if (!folderName.StartsWith("v", StringComparison.OrdinalIgnoreCase)) return false;
            if (folderName.Length < 2) return false;
            return char.IsDigit(folderName[1]);
        }
    }
}
