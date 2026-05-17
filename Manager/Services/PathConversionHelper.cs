using System;
using System.IO;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// パス変換ヘルパー（相対パス⇔絶対パス変換）
    /// </summary>
    public static class PathConversionHelper
    {
        /// <summary>
        /// 絶対パスから基準フォルダに対する相対パスを取得（.NET Framework 4.8対応）
        /// </summary>
        /// <param name="basePath">基準フォルダ</param>
        /// <param name="targetPath">変換対象のパス</param>
        /// <returns>相対パス（基準フォルダ外の場合は元のパスをそのまま返す）</returns>
        public static string ToRelativePath(string basePath, string targetPath)
        {
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(targetPath))
            {
                return targetPath;
            }

            // 既に相対パスの場合はそのまま返す
            if (!Path.IsPathRooted(targetPath))
            {
                return targetPath;
            }

            basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            targetPath = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(basePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                // 同じパスの場合は、ファイル名のみを返す
                return Path.GetFileName(targetPath);
            }

            if (!targetPath.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                // 基準フォルダ外のパスはそのまま返す
                return targetPath;
            }

            string relativePath = targetPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.IsNullOrEmpty(relativePath) ? Path.GetFileName(targetPath) : relativePath;
        }

        /// <summary>
        /// 相対パスを絶対パスに変換（既に絶対パスの場合はそのまま返す）
        /// </summary>
        /// <param name="basePath">基準フォルダ</param>
        /// <param name="relativePath">相対パスまたは絶対パス</param>
        /// <returns>絶対パス</returns>
        public static string ToAbsolutePath(string basePath, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return relativePath;
            }

            if (Path.IsPathRooted(relativePath))
            {
                return relativePath; // 既に絶対パスの場合
            }

            return Path.Combine(basePath, relativePath);
        }

        /// <summary>
        /// コピー元のパスをコピー先の絶対パスに変換
        /// </summary>
        /// <param name="sourcePath">変換対象のパス</param>
        /// <param name="sourceFolder">コピー元フォルダ</param>
        /// <param name="destinationFolder">コピー先フォルダ</param>
        /// <returns>コピー先の絶対パス</returns>
        public static string ConvertSourceToDestination(string sourcePath, string sourceFolder, string destinationFolder)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return null;
            }

            if (sourcePath.StartsWith(destinationFolder, StringComparison.OrdinalIgnoreCase))
            {
                // 既にコピー先フォルダ内のパス
                return sourcePath;
            }
            else if (sourcePath.StartsWith(sourceFolder, StringComparison.OrdinalIgnoreCase))
            {
                // コピー元フォルダ内のパス → コピー先の絶対パスに変換
                string relativePath = ToRelativePath(sourceFolder, sourcePath);
                return Path.Combine(destinationFolder, relativePath);
            }
            else
            {
                // その他のパスはそのまま
                return sourcePath;
            }
        }

        /// <summary>
        /// コピー後にコピー先フォルダからの相対パスに変換
        /// コピー後は必ずコピー先フォルダ内にあるはずなので、相対パスに変換する
        /// </summary>
        /// <param name="absolutePath">絶対パス</param>
        /// <param name="destinationFolder">コピー先フォルダ</param>
        /// <returns>相対パス</returns>
        public static string ToRelativePathAfterCopy(string absolutePath, string destinationFolder)
        {
            if (string.IsNullOrEmpty(absolutePath))
            {
                return null;
            }

            // 既に相対パスの場合はそのまま返す
            if (!Path.IsPathRooted(absolutePath))
            {
                return absolutePath;
            }

            // パスを正規化
            string normalizedAbsolutePath = Path.GetFullPath(absolutePath);
            string normalizedDestinationFolder = Path.GetFullPath(destinationFolder);

            // コピー先フォルダ内のパスか確認
            if (normalizedAbsolutePath.StartsWith(normalizedDestinationFolder, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = normalizedAbsolutePath.Substring(normalizedDestinationFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(relativePath) ? Path.GetFileName(normalizedAbsolutePath) : relativePath;
            }

            // コピー先フォルダ外のパスは警告を出して絶対パスのまま返す（フォールバック）
            Logger.Warn($"[警告] パスがコピー先フォルダ内にありません。絶対パスのまま保存します: {absolutePath}");
            Logger.Warn($"[警告] コピー先フォルダ: {destinationFolder}");
            return absolutePath;
        }
    }
}
