using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲームファイルのバージョン管理を行うヘルパークラス
    /// </summary>
    public static class FileVersioningHelper
    {
        /// <summary>
        /// バージョンフォルダのプレフィックス
        /// </summary>
        private const string VersionPrefix = "v";

        /// <summary>
        /// パスがバージョン管理構造になっているかチェック
        /// 新形式: D:\Games\MyGame\v1.0.0\game.exe → true
        /// 旧形式: D:\Games\MyGame\versions\1.0.0\game.exe → true（旧形式も認識）
        /// 非構造: D:\Games\MyGame\game.exe → false
        /// </summary>
        public static bool IsVersionedStructure(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return false;

            try
            {
                string directory = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(directory))
                    return false;

                string folderName = Path.GetFileName(directory);
                
                // 新形式: vX.Y.Z
                if (folderName.StartsWith(VersionPrefix) && 
                    folderName.Length > 1 && 
                    char.IsDigit(folderName[1]))
                {
                    return true;
                }
                
                // 旧形式: versions/X.Y.Z
                string parentDir = Path.GetDirectoryName(directory);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    string parentFolderName = Path.GetFileName(parentDir);
                    if (parentFolderName == "versions" && 
                        folderName.Length > 0 && 
                        char.IsDigit(folderName[0]))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// パスが旧形式のバージョン構造（versions/X.Y.Z）かチェック
        /// </summary>
        public static bool IsOldVersionedStructure(string exePath)
        {
            if (string.IsNullOrEmpty(exePath))
                return false;

            try
            {
                string directory = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(directory))
                    return false;

                string folderName = Path.GetFileName(directory);
                string parentDir = Path.GetDirectoryName(directory);
                
                if (!string.IsNullOrEmpty(parentDir))
                {
                    string parentFolderName = Path.GetFileName(parentDir);
                    return parentFolderName == "versions" && 
                           folderName.Length > 0 && 
                           char.IsDigit(folderName[0]);
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// バージョンフォルダのパスを取得
        /// </summary>
        public static string GetVersionFolderPath(string gameBaseFolder, string version)
        {
            string versionFolder = version.StartsWith(VersionPrefix) ? version : VersionPrefix + version;
            return Path.Combine(gameBaseFolder, versionFolder);
        }

        /// <summary>
        /// ゲームのベースフォルダを取得（バージョンフォルダの親）
        /// </summary>
        public static string GetGameBaseFolder(string versionedExePath)
        {
            if (!IsVersionedStructure(versionedExePath))
                return Path.GetDirectoryName(versionedExePath);

            // exeのフォルダ → バージョンフォルダ → ゲームベースフォルダ
            string exeFolder = Path.GetDirectoryName(versionedExePath);
            return Path.GetDirectoryName(exeFolder);
        }

        /// <summary>
        /// 既存のゲームフォルダをバージョン管理構造にマイグレーション
        /// </summary>
        /// <param name="currentExePath">現在のexeパス</param>
        /// <param name="version">バージョン番号（例: 1.0.0）</param>
        /// <param name="progress">進捗報告用コールバック（オプション）</param>
        /// <returns>マイグレーション後のexeパス</returns>
        public static string MigrateToVersionedStructure(
            string currentExePath, 
            string version,
            Action<int, string> progress = null)
        {
            if (IsVersionedStructure(currentExePath))
            {
                // 既にバージョン構造になっている
                return currentExePath;
            }

            string currentFolder = Path.GetDirectoryName(currentExePath);
            string exeFileName = Path.GetFileName(currentExePath);
            string parentFolder = Path.GetDirectoryName(currentFolder);
            string gameFolderName = Path.GetFileName(currentFolder);

            // 一時フォルダにまず移動
            string tempFolder = Path.Combine(parentFolder, "_temp_migration_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string versionFolder = GetVersionFolderPath(currentFolder, version);

            try
            {
                progress?.Invoke(10, "一時フォルダに移動中...");
                
                // 現在のフォルダを一時フォルダに移動
                Directory.Move(currentFolder, tempFolder);

                progress?.Invoke(30, "新しいフォルダ構造を作成中...");
                
                // ゲームベースフォルダを作成
                Directory.CreateDirectory(currentFolder);
                
                // バージョンフォルダとしてリネーム
                string newVersionFolder = Path.Combine(currentFolder, VersionPrefix + version);
                Directory.Move(tempFolder, newVersionFolder);

                progress?.Invoke(100, "完了");

                return Path.Combine(newVersionFolder, exeFileName);
            }
            catch (Exception)
            {
                // エラー時はロールバック
                if (Directory.Exists(tempFolder) && !Directory.Exists(currentFolder))
                {
                    Directory.Move(tempFolder, currentFolder);
                }
                throw;
            }
        }

        /// <summary>
        /// フォルダをバージョンフォルダにコピー
        /// </summary>
        /// <param name="sourceFolder">コピー元フォルダ</param>
        /// <param name="gameBaseFolder">ゲームベースフォルダ</param>
        /// <param name="version">バージョン番号</param>
        /// <param name="progress">進捗報告用コールバック（オプション）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>コピー先のバージョンフォルダパス</returns>
        public static async Task<string> CopyFolderToVersionedPathAsync(
            string sourceFolder,
            string gameBaseFolder,
            string version,
            IProgress<(int Percentage, string Message)> progress = null,
            CancellationToken cancellationToken = default)
        {
            string versionFolder = GetVersionFolderPath(gameBaseFolder, version);

            if (Directory.Exists(versionFolder))
            {
                throw new InvalidOperationException($"バージョンフォルダが既に存在します: {versionFolder}");
            }

            // ファイル数をカウント
            var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
            int totalFiles = files.Length;
            int copiedFiles = 0;

            // コピー先フォルダを作成
            Directory.CreateDirectory(versionFolder);

            try
            {
                await Task.Run(() =>
                {
                    foreach (string sourceFile in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string relativePath = sourceFile.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar);
                        string destFile = Path.Combine(versionFolder, relativePath);
                        string destDir = Path.GetDirectoryName(destFile);

                        if (!Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        File.Copy(sourceFile, destFile, overwrite: true);
                        copiedFiles++;

                        int percentage = (int)((double)copiedFiles / totalFiles * 100);
                        progress?.Report((percentage, $"コピー中: {relativePath}"));
                    }
                }, cancellationToken);

                return versionFolder;
            }
            catch
            {
                // エラー時はコピー先を削除
                if (Directory.Exists(versionFolder))
                {
                    try { Directory.Delete(versionFolder, recursive: true); } catch { }
                }
                throw;
            }
        }

        /// <summary>
        /// 同期版のフォルダコピー
        /// </summary>
        public static string CopyFolderToVersionedPath(
            string sourceFolder,
            string gameBaseFolder,
            string version,
            Action<int, string> progress = null)
        {
            string versionFolder = GetVersionFolderPath(gameBaseFolder, version);

            if (Directory.Exists(versionFolder))
            {
                throw new InvalidOperationException($"バージョンフォルダが既に存在します: {versionFolder}");
            }

            var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
            int totalFiles = files.Length;
            int copiedFiles = 0;

            Directory.CreateDirectory(versionFolder);

            try
            {
                foreach (string sourceFile in files)
                {
                    string relativePath = sourceFile.Substring(sourceFolder.Length).TrimStart(Path.DirectorySeparatorChar);
                    string destFile = Path.Combine(versionFolder, relativePath);
                    string destDir = Path.GetDirectoryName(destFile);

                    if (!Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Copy(sourceFile, destFile, overwrite: true);
                    copiedFiles++;

                    int percentage = (int)((double)copiedFiles / totalFiles * 100);
                    progress?.Invoke(percentage, $"コピー中: {relativePath}");
                }

                return versionFolder;
            }
            catch
            {
                if (Directory.Exists(versionFolder))
                {
                    try { Directory.Delete(versionFolder, recursive: true); } catch { }
                }
                throw;
            }
        }

        /// <summary>
        /// ファイルパスをバージョンフォルダからの相対パスに変換
        /// </summary>
        public static string ConvertToRelativePath(string absolutePath, string versionFolder)
        {
            if (string.IsNullOrEmpty(absolutePath) || string.IsNullOrEmpty(versionFolder))
                return absolutePath;

            if (absolutePath.StartsWith(versionFolder, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Substring(versionFolder.Length).TrimStart(Path.DirectorySeparatorChar);
            }

            return absolutePath;
        }

        /// <summary>
        /// 相対パスをバージョンフォルダの絶対パスに変換
        /// </summary>
        public static string ConvertToAbsolutePath(string relativePath, string versionFolder)
        {
            if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
                return relativePath;

            return Path.Combine(versionFolder, relativePath);
        }
    }
}
