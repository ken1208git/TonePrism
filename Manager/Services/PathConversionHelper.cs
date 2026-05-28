using System;
using System.IO;

namespace TonePrism.Manager.Services
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

            // (累積監査 round 4 High-8) basePath が空文字 / null の防御経路は relative のまま返さない。
            // 旧実装は `Path.Combine("", "v1.0.0/thumb.png")` → "v1.0.0/thumb.png" を返し、後段の
            // File.Exists() が CWD (= Manager.exe 作業 dir) 基準で評価される silent corruption 経路だった。
            // ToRelativePath / IsPathInside と同様に「base が空なら相対変換は不可能」と扱い、Logger.Warn
            // で contract 違反を伝播 + null を返して caller に必須再 reject させる契約に統一。
            if (string.IsNullOrEmpty(basePath))
            {
                Logger.Warn("[PathConversionHelper] (H8) ToAbsolutePath: basePath が空。relativePath を CWD 基準解決させないよう null を返却: " + relativePath);
                return null;
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

            // (L) 戻り値が相対 path のままだと caller (File.Exists 等) で CWD 基準で評価されて
            // false になる経路があるため、最後に Path.GetFullPath で必ず絶対化する。
            string result;
            if (IsPathInside(destinationFolder, sourcePath))
            {
                // 既にコピー先フォルダ内のパス
                result = sourcePath;
            }
            else if (IsPathInside(sourceFolder, sourcePath))
            {
                // コピー元フォルダ内のパス → コピー先の絶対パスに変換
                string relativePath = ToRelativePath(sourceFolder, sourcePath);
                result = Path.Combine(destinationFolder, relativePath);
            }
            else
            {
                // その他のパスはそのまま
                result = sourcePath;
            }

            try { return Path.GetFullPath(result); }
            catch { return result; /* 不正 path 等 GetFullPath が throw する稀 case は元の値返却 */ }
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

            // パスを正規化（末尾区切りを除去して境界比較を区切り文字安全にする。生 StartsWith だと
            // dest="games\game1" が "games\game10\..." のような兄弟フォルダにも前方一致する死角があり、
            // IsPathInside / ToRelativePath と同じ「等値 OR 区切り付き StartsWith」に揃える）
            string normalizedAbsolutePath = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedDestinationFolder = Path.GetFullPath(destinationFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalizedAbsolutePath, normalizedDestinationFolder, StringComparison.OrdinalIgnoreCase))
            {
                // コピー先フォルダ自身を指す場合はファイル名のみ返す
                return Path.GetFileName(normalizedAbsolutePath);
            }

            // コピー先フォルダ内のパスか確認（区切り文字境界で判定）
            if (normalizedAbsolutePath.StartsWith(normalizedDestinationFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = normalizedAbsolutePath.Substring(normalizedDestinationFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.IsNullOrEmpty(relativePath) ? Path.GetFileName(normalizedAbsolutePath) : relativePath;
            }

            // コピー先フォルダ外のパスは警告を出して絶対パスのまま返す（フォールバック）
            Logger.Warn($"[警告] パスがコピー先フォルダ内にありません。絶対パスのまま保存します: {absolutePath}");
            Logger.Warn($"[警告] コピー先フォルダ: {destinationFolder}");
            return absolutePath;
        }

        /// <summary>
        /// (#234 追加精査 ③) targetPath が baseFolder 自身、またはその配下にあるかを区切り文字安全に判定する。
        /// 生の StartsWith は basePath が "C:\games\foo" のとき "C:\games\foobar\x.exe" のような兄弟
        /// フォルダにも前方一致してしまうため、正規化 + 区切り文字境界 (basePath + セパレータ) で比較する。
        /// 空 / 正規化不能の場合は false（= 呼び出し側は「内側ではない」扱い）。
        /// </summary>
        public static bool IsPathInside(string baseFolder, string targetPath)
        {
            if (string.IsNullOrEmpty(baseFolder) || string.IsNullOrEmpty(targetPath)) return false;

            string b, t;
            try
            {
                b = Path.GetFullPath(baseFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                t = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return false;
            }

            if (string.Equals(b, t, StringComparison.OrdinalIgnoreCase)) return true;
            return t.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
