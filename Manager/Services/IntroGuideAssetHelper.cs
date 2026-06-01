using System;
using System.IO;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#253) イントロガイドのスライド画像を `guide/` フォルダへ取り込む helper。
    ///
    /// DB (`intro_slides.image_path`) には `guide/&lt;file&gt;` の**相対パス**(forward slash) のみ保存し、
    /// 画像実体は `guide/` にファイル別管理する (games のサムネと同流儀)。Launcher も同 relative path を
    /// base 起点で resolve する。
    ///
    /// UI を持たない純ロジック (= 単体テスト可能)。本番 wrapper (`ImportImage` / `ToAbsolute`) のみ
    /// `PathManager` に依存し、コア (`CopyImageInto` / `ResolveNonConflictingLeaf`) はフォルダパスを引数で受ける。
    /// 同名衝突は **自動 suffix** (`slide.png` → `slide_2.png`) で回避する (games の対話式 rename と異なり、
    /// staff 提供画像はまれな衝突を黙って避ければ十分なため UI を挟まない = テスト容易性も確保)。
    /// **例外**: 選択画像が既に `guide/` 直下にある場合は複製せず**その実体を再利用**する (孤児画像の再利用 +
    /// 無駄な複製防止)。別の場所にある同名の**別**画像は従来どおり suffix で取り込む (衝突回避は維持)。
    /// </summary>
    public static class IntroGuideAssetHelper
    {
        public const string GuideFolderName = "guide";

        /// <summary>
        /// 本番: 選択画像を `PathManager.GuideFolder` へ取り込み、DB 保存用の相対パス `guide/&lt;file&gt;` を返す。
        /// </summary>
        public static string ImportImage(string sourceAbsolutePath)
        {
            return ImportImage(sourceAbsolutePath, out _);
        }

        /// <summary>
        /// 本番: 上記に加え、新たにファイルをコピーしたか (`createdNewFile=true`)、既存 guide/ 画像を再利用したか
        /// (`false`) を返す。caller (編集 Form) は save 失敗時の orphan 掃除で、**新規コピー時のみ**削除するために使う
        /// (再利用した既存ファイルを誤って消さないため)。
        /// </summary>
        public static string ImportImage(string sourceAbsolutePath, out bool createdNewFile)
        {
            string leaf = CopyImageInto(PathManager.GuideFolder, sourceAbsolutePath, out createdNewFile);
            return GuideFolderName + "/" + leaf;
        }

        /// <summary>
        /// (テスト可能コア) `sourceAbsolutePath` を `guideFolderAbs` に取り込み、コピー先の leaf ファイル名を返す。
        /// 衝突時は自動 suffix。フォルダは無ければ作成。
        /// </summary>
        public static string CopyImageInto(string guideFolderAbs, string sourceAbsolutePath)
        {
            return CopyImageInto(guideFolderAbs, sourceAbsolutePath, out _);
        }

        /// <summary>
        /// (テスト可能コア) 上記に加え、新規コピー (`createdNewFile=true`) か既存 guide/ 画像の再利用 (`false`) かを返す。
        /// 選択画像が `guideFolderAbs` の**直下**にあるときは複製せずその leaf 名をそのまま返す。
        /// </summary>
        public static string CopyImageInto(string guideFolderAbs, string sourceAbsolutePath, out bool createdNewFile)
        {
            if (string.IsNullOrWhiteSpace(sourceAbsolutePath) || !File.Exists(sourceAbsolutePath))
            {
                throw new FileNotFoundException("コピー元の画像が見つかりません。", sourceAbsolutePath ?? "(null)");
            }
            Directory.CreateDirectory(guideFolderAbs);

            // 選択画像が既に guide/ 直下にあるなら複製せず再利用 (孤児の再利用 + 重複防止)。
            // ネスト (guide/sub/x.png) は guide/<leaf> のフラット不変条件に合わないため再利用対象外
            // → 従来どおりコピーして guide/ 直下に平坦化する。
            if (IsDirectlyInside(guideFolderAbs, sourceAbsolutePath))
            {
                createdNewFile = false;
                return Path.GetFileName(sourceAbsolutePath);
            }

            string leaf = ResolveNonConflictingLeaf(guideFolderAbs, Path.GetFileName(sourceAbsolutePath));
            File.Copy(sourceAbsolutePath, Path.Combine(guideFolderAbs, leaf), overwrite: false);
            createdNewFile = true;
            return leaf;
        }

        /// <summary>
        /// `sourceAbsolutePath` が `folderAbs` の**直下** (ネストではない) にあるか。
        /// path を正規化 (`GetFullPath`) し、Windows 前提で大小無視・末尾セパレータ無視で親フォルダを比較。
        /// </summary>
        private static bool IsDirectlyInside(string folderAbs, string sourceAbsolutePath)
        {
            string parent;
            try
            {
                parent = Path.GetDirectoryName(Path.GetFullPath(sourceAbsolutePath));
            }
            catch
            {
                return false;
            }
            if (string.IsNullOrEmpty(parent)) return false;

            string folderNorm = Path.GetFullPath(folderAbs)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            parent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(parent, folderNorm, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// (テスト可能コア) `folderAbs` 内で衝突しない leaf 名を返す。`slide.png` が空いていればそのまま、
        /// 使用中なら `slide_2.png` / `slide_3.png` … と増やす。
        /// </summary>
        public static string ResolveNonConflictingLeaf(string folderAbs, string desiredLeaf)
        {
            string name = Path.GetFileNameWithoutExtension(desiredLeaf);
            string ext = Path.GetExtension(desiredLeaf);
            string candidate = desiredLeaf;
            int n = 2;
            while (File.Exists(Path.Combine(folderAbs, candidate)))
            {
                candidate = name + "_" + n + ext;
                n++;
            }
            return candidate;
        }

        /// <summary>
        /// 本番: DB 保存の相対パス (`guide/&lt;file&gt;`) を絶対パスへ (preview / 存在確認用)。空/null は null。
        /// </summary>
        public static string ToAbsolute(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            return Path.Combine(PathManager.BaseDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// (テスト可能コア) `guide/&lt;leaf&gt;` 相対パスの画像実体を `guideFolderAbs` から削除 (best-effort、
        /// 失敗は false)。スライド削除時に「他スライドが同じ画像を参照していない」と確認した上で caller が呼ぶ。
        /// `guide/` 外を指す相対パスは安全のため無視 (true)。
        /// </summary>
        public static bool DeleteImage(string guideFolderAbs, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return true;
            string norm = relativePath.Replace('\\', '/');
            string prefix = GuideFolderName + "/";
            if (!norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true; // guide/ 配下のみ対象
            string leaf = norm.Substring(prefix.Length);
            if (string.IsNullOrWhiteSpace(leaf) || leaf.Contains("/")) return true; // ネストは扱わない
            try
            {
                string path = Path.Combine(guideFolderAbs, leaf);
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
