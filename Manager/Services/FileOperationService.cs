using System;
using System.Collections.Generic;
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
        /// ゲームエンジン/開発環境の不要フォルダ一覧 (コピー時に除外する)。
        ///
        /// (Finding #2) 旧実装は "Build" / "Builds" / "Saved" / "Logs" / "Temp" も除外していたが、
        /// これらは「ゲーム本体の中身」でもありうる総称名 (Unreal の Saved、配布物の Build/Builds、
        /// 自作ゲームの Logs/Temp 等) で、exe / 画像がフォルダ直下にあると除外に気づかぬまま不完全
        /// コピーで起動不能ゲームが silent 登録される穴があった。除外対象は「再生成可能で出荷物に
        /// 含めない」ことがほぼ確実な engine cache (Library / Intermediate / DerivedDataCache / .import)
        /// と VCS / IDE / 言語キャッシュ (dotfiles 系) に限定する。残る曖昧な名前 (Library / node_modules
        /// 等、ごく稀に本体になりうる) は <see cref="FindExcludedFolderNames"/> + コピー前確認ダイアログで
        /// ユーザーに明示するため、silent には落とさない。
        /// </summary>
        public static readonly string[] ExcludedFolders = new[]
        {
            "Library",
            "Intermediate", "DerivedDataCache",
            ".import",
            ".vs", ".idea", ".vscode",
            ".git", ".svn", ".hg",
            "node_modules",
            "__pycache__", ".pytest_cache", ".mypy_cache"
        };

        /// <summary>
        /// (Finding #2) <paramref name="sourceDir"/> 配下に存在する除外対象フォルダ名 (distinct) を列挙する。
        /// コピー前に「これらは取り込まれません」とユーザーへ確認するための事前 scan。ディレクトリのみ走査
        /// (ファイルは見ない) のため軽量。アクセス不能フォルダは握り潰してスキップ。除外フォルダの内側は
        /// これ以上辿らない (= コピー側 <see cref="CopyDirectoryRecursive"/> の skip 挙動と一致させ、報告内容を
        /// 「実際にコピーされないフォルダ」と厳密に揃える)。
        /// </summary>
        public static List<string> FindExcludedFolderNames(string sourceDir)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectExcludedFolderNames(sourceDir, found);
            return found.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CollectExcludedFolderNames(string dir, HashSet<string> found)
        {
            string[] subDirs;
            try { subDirs = Directory.GetDirectories(dir); }
            catch { return; }
            foreach (string subDir in subDirs)
            {
                string name = Path.GetFileName(subDir);
                if (ExcludedFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(name);
                    continue; // 除外フォルダ内は走査不要 (コピーされないため)
                }
                CollectExcludedFolderNames(subDir, found);
            }
        }

        /// <summary>
        /// 指定ディレクトリ配下のファイル数を再帰的にカウント。
        /// (#250 M1) <paramref name="applyExclusions"/>=false で ExcludedFolders (Library/.import/node_modules 等) を
        /// 除外せず数える。アセット控え (AssetSnapshotService) は丸ごと走査するので、進捗分母も除外なしで揃える。
        /// </summary>
        public static int CountFiles(string dir, Func<string, bool> excludeFolderPredicate = null, bool applyExclusions = true)
        {
            int count = 0;
            try
            {
                count += Directory.GetFiles(dir).Length;

                foreach (string subDir in Directory.GetDirectories(dir))
                {
                    string folderName = Path.GetFileName(subDir);

                    if (applyExclusions && ExcludedFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (excludeFolderPredicate != null && excludeFolderPredicate(folderName))
                        continue;

                    count += CountFiles(subDir, excludeFolderPredicate, applyExclusions);
                }
            }
            catch { }
            return count;
        }

        /// <summary>
        /// ディレクトリを再帰的にコピー（進捗レポート付き）。
        ///
        /// (追加精査 ②) 個別ファイル / サブフォルダの copy 失敗は `failedFiles` に集約して呼び出し側に返す。
        /// 旧実装は catch で Logger.Warn / Error するだけで呼び出し側に伝えず、main.exe が他プロセスにロック
        /// されている等の状況で「DB は登録されたが実体が無い」起動不能ゲームを silent に生む経路があった。
        /// </summary>
        public static void CopyDirectoryRecursive(
            string sourceDir,
            string destDir,
            IProgress<ProgressInfo> progress,
            System.Threading.CancellationToken token,
            int totalFiles,
            ref int copiedFiles,
            List<string> failedFiles,
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
                    failedFiles?.Add(file);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[警告] ファイルのコピーに失敗: {file}", ex);
                    failedFiles?.Add(file);
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
                    // (累積監査 round 4 High-4) 区切り文字を含めて比較し、Foo と Foobar のような兄弟前方一致で
                    // 正当な subdir が silent skip されるのを防ぐ (line 81-82 の sourceDirWithSep と同じ pattern)。
                    // 旧実装は `fullDestDir.StartsWith(fullSubPath, ...)` のみで判定しており、`fullSubPath="C:\src\Foo"`
                    // と `fullDestDir="C:\src\Foobar"` で前方一致 true → continue 経路に流れ、`Foo/` 配下が
                    // 1 ファイルもコピーされない (取り込み後の存在 check で最終 rollback されるが、毎回必ず失敗する
                    // 取り込み不能ゲームの源だった)。
                    string subPathWithSep = fullSubPath.EndsWith("\\") ? fullSubPath : fullSubPath + "\\";
                    if (fullSubPath.Equals(fullDestDir, StringComparison.OrdinalIgnoreCase) ||
                        fullDestDir.StartsWith(subPathWithSep, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ExcludedFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    if (excludeFolderPredicate != null && excludeFolderPredicate(folderName))
                        continue;

                    string destSubDir = Path.Combine(safeDestDir, folderName);
                    CopyDirectoryRecursive(subDir, destSubDir, progress, token, totalFiles, ref copiedFiles, failedFiles, excludeFolderPredicate);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[警告] サブフォルダのコピーに失敗: {subDir}", ex);
                    failedFiles?.Add(subDir);
                }
            }
        }

        /// <summary>
        /// ファイル数カウント → ディレクトリコピーを一括実行。コピーに失敗したファイル / サブフォルダの
        /// path 一覧を返す (失敗ゼロなら空 list)。呼び出し側は List の中身を見て「致命的か続行可能か」を
        /// 判断する責務を負う (旧実装の silent failure を伝播経路に置き換えた)。
        /// </summary>
        public static List<string> CopyDirectoryWithProgress(
            string sourceDir,
            string destDir,
            IProgress<ProgressInfo> progress,
            System.Threading.CancellationToken token,
            Func<string, bool> excludeFolderPredicate = null)
        {
            progress.Report(new ProgressInfo(0, "ファイル数を計算中..."));
            int totalFiles = CountFiles(sourceDir, excludeFolderPredicate);
            int copiedFiles = 0;
            var failedFiles = new List<string>();
            CopyDirectoryRecursive(sourceDir, destDir, progress, token, totalFiles, ref copiedFiles, failedFiles, excludeFolderPredicate);
            return failedFiles;
        }

        /// <summary>
        /// CopyDirectoryWithProgress の戻り値 (失敗 list) を整形済例外メッセージにする helper。
        /// list 空なら null を返す (= 投げる必要なし)。先頭 10 件を表示し、それ以上は件数のみ。
        /// </summary>
        public static string FormatCopyFailureMessage(List<string> failedFiles, string baseDir = null)
        {
            if (failedFiles == null || failedFiles.Count == 0) return null;
            const int previewLimit = 10;
            var preview = failedFiles.Take(previewLimit).Select(p =>
            {
                if (!string.IsNullOrEmpty(baseDir))
                {
                    try
                    {
                        string fb = NormalizePath(baseDir);
                        string fp = NormalizePath(p);
                        if (fp.StartsWith(fb + "\\", StringComparison.OrdinalIgnoreCase))
                            return fp.Substring(fb.Length + 1);
                    }
                    catch { }
                }
                return p;
            });
            string body = string.Join("\n  - ", preview);
            string suffix = failedFiles.Count > previewLimit
                ? $"\n  (他 {failedFiles.Count - previewLimit} 件)"
                : "";
            return $"{failedFiles.Count} 件のファイル / サブフォルダのコピーに失敗しました:\n  - {body}{suffix}";
        }

        /// <summary>
        /// パスを正規化（\\?\ プレフィックスの除去と絶対パス化）。
        /// (L) null / 空文字は素通し (NRE 防止)、将来 caller drift で null path を渡されても落ちないように。
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            // (#250 C1) \\?\UNC\server\share → \\server\share。UNC 長パスを先に判定してから素の \\?\ を剥がす
            // (ForceLongPath/EnsureLongPath が付ける UNC プレフィックスと対称にし、剥がし漏れで相対パス化するのを防ぐ)。
            if (path.StartsWith(@"\\?\UNC\")) path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\")) path = path.Substring(4);
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// 長いパス名のサポート（260文字超のパスに \\?\ プレフィックスを付与）
        /// </summary>
        public static string EnsureLongPath(string path)
        {
            if (path.Length >= 240 && !path.StartsWith(@"\\?\"))
                return ApplyLongPathPrefix(path);
            return path;
        }

        /// <summary>
        /// (#250 C1) 長さに関わらず常に \\?\ プレフィックスを付ける版。深い木の列挙対象 dir に使う
        /// (EnsureLongPath は 240 字未満だと付けないため、短い親 + MAX_PATH 超の子で列挙自体が PathTooLong になる)。
        /// 既に \\?\ 付きなら素通し。UNC 対応は ApplyLongPathPrefix に集約。
        /// </summary>
        public static string ForceLongPath(string path)
            => path.StartsWith(@"\\?\") ? path : ApplyLongPathPrefix(path);

        /// <summary>
        /// (#250 C1) \\?\ 長パスプレフィックスを正しく付与する内部 helper。UNC パス (\\server\share\...) は正しい
        /// 長パス形 \\?\UNC\server\share\... へ変換する。単純な "\\?\" + path だと UNC で "\\?\\\server\..." という
        /// Win32 構文不正パスを生成し、SMB 上の Directory.GetFiles 等が "syntax is incorrect" 例外で全件失敗する
        /// (= アセット控えが silent に空のまま Success 扱いになる) ため UNC 分岐が必須。ローカルは \\?\C:\... 。
        /// caller (EnsureLongPath / ForceLongPath) が既存 \\?\ 付きを除外してから呼ぶ前提。
        /// </summary>
        private static string ApplyLongPathPrefix(string path)
        {
            // (round9 B-1) Path.GetFullPath は .NET Framework 既定の legacy path handling では ≥260 字入力で
            // PathTooLongException を投げる (= 長パスを安全化する関数が、まさに対象の長パスで落ちる自己矛盾)。
            // 通常は GetFullPath で正規化 (相対 / ".." / 区切り) するが、長すぎて throw した場合は caller が渡すのは
            // 常に絶対・正規化済みパス (DbPath 由来 / 列挙結果 / backup_dest) なので、生パスをそのまま \\?\ 化して
            // 深い backup_dest でも Failed にせず控えられるようにする。
            string full;
            try { full = Path.GetFullPath(path); }
            catch (PathTooLongException) { full = path; }
            if (full.StartsWith(@"\\")) return @"\\?\UNC\" + full.Substring(2); // UNC: \\server\share → \\?\UNC\server\share
            return @"\\?\" + full;
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
    }
}
