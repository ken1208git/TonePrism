using System;
using System.IO;
using GCTonePrism.Manager.Services;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// アプリケーションのファイルパスを管理するクラス
    /// </summary>
    public static class PathManager
    {
        private static string _baseDirectory;
        
        /// <summary>
        /// アプリケーションのベースディレクトリ（プロジェクトルート）
        /// </summary>
        public static string BaseDirectory
        {
            get
            {
                if (_baseDirectory == null)
                {
                    _baseDirectory = FindBaseDirectory();
                }
                return _baseDirectory;
            }
        }
        
        /// <summary>
        /// gamesフォルダのパス
        /// </summary>
        public static string GamesFolder
        {
            get { return Path.Combine(BaseDirectory, "games"); }
        }
        
        /// <summary>
        /// データベースファイルのパス
        /// </summary>
        public static string DatabasePath
        {
            get { return Path.Combine(BaseDirectory, "prism.db"); }
        }
        
        /// <summary>
        /// 指定したゲームのフォルダパス
        /// </summary>
        public static string GetGameFolder(string gameId)
        {
            return Path.Combine(GamesFolder, gameId);
        }
        
        /// <summary>
        /// 指定したゲームのバージョンフォルダパス
        /// </summary>
        public static string GetVersionFolder(string gameId, string version)
        {
            string versionFolder = version.StartsWith("v") ? version : "v" + version;
            return Path.Combine(GetGameFolder(gameId), versionFolder);
        }
        
        /// <summary>
        /// プロジェクトルートを自動検出
        /// 開発時・本番時どちらも同じロジックで検出
        /// </summary>
        private static string FindBaseDirectory()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dir = new DirectoryInfo(exePath);
            string detectedBaseDirectory = null;
            
            // 最大10階層まで遡る（無限ループ防止）
            int maxLevels = 10;
            int currentLevel = 0;
            
            while (dir != null && currentLevel < maxLevels)
            {
                string currentPath = dir.FullName;
                
                // 優先順位1: prism.db（データベースファイル）
                if (File.Exists(Path.Combine(currentPath, "prism.db")))
                {
                    Logger.Info($"[PathManager] prism.db を検出: {currentPath}");
                    detectedBaseDirectory = currentPath;
                    break;
                }
                
                // 優先順位2: .git（Gitリポジトリのルート）
                if (Directory.Exists(Path.Combine(currentPath, ".git")))
                {
                    Logger.Info($"[PathManager] .git フォルダを検出: {currentPath}");
                    detectedBaseDirectory = currentPath;
                    break;
                }
                
                // 優先順位3: Managerフォルダが存在する場合（実行ファイルがその中にある場合）
                // 実行ファイルがManagerフォルダ内にある場合、親ディレクトリをプロジェクトルートとする
                //
                // NOTE: 比較は「等値 OR separator 付き StartsWith」の二段で行うこと。
                //   - exePath = AppDomain.CurrentDomain.BaseDirectory は現状 .NET の慣習で
                //     末尾 `\` 付きを返すので separator 付き StartsWith だけでも動くが、これは
                //     ランタイム実装依存の暗黙前提。将来 .NET 移行等で末尾 `\` が外れた場合、
                //     ちょうど Manager dir に居る正規ケースで誤って false になる regression
                //     リスクがある。Launcher 側の Godot get_base_dir() は末尾 "/" を返さない
                //     ため equality 比較が必須で、対称性のため Manager 側も同じパターンに揃える。
                //   - separator 付き StartsWith は "Manager" prefix が "ManagerStudio" 等の
                //     兄弟 dir 名にも誤マッチするのを防ぐため依然必要。
                //
                // 追加 guard (issue #151 で言及した sibling 同時存在検証):
                //   - 我々の install 構造は Manager と Launcher が必ず同一の親 dir 配下に
                //     セットで配置される (SPEC §3.7.1 / §7.5.1)。Launcher/ も同 currentPath
                //     直下に存在することを確認することで、`<install>/Manager/` 単独 dir
                //     (= 他アプリ等で偶然存在する Manager dir) との誤マッチを構造的に排除する。
                //     priority-1 (prism.db) / priority-2 (.git) が hit しない極限状況での
                //     false-match を低減 (round 7 L5)。
                string managerCandidate = Path.Combine(currentPath, "Manager");
                string managerCandidateWithSep = managerCandidate + Path.DirectorySeparatorChar;
                string siblingLauncher = Path.Combine(currentPath, "Launcher");
                if (Directory.Exists(managerCandidate) &&
                    Directory.Exists(siblingLauncher) &&
                    (exePath.Equals(managerCandidate, StringComparison.OrdinalIgnoreCase) ||
                     exePath.StartsWith(managerCandidateWithSep, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Info($"[PathManager] Manager + Launcher 兄弟フォルダを検出: {currentPath}");
                    detectedBaseDirectory = currentPath;
                    break;
                }
                
                dir = dir.Parent;
                currentLevel++;
            }
            
            // プロジェクトルートが見つからない場合
            if (detectedBaseDirectory == null)
            {
                string errorMessage = $"エラー: プロジェクトルートが見つかりません。\n\n" +
                                     $"実行ファイルのパス: {exePath}\n\n" +
                                     $"このアプリケーションは、Managerフォルダ内から実行してください。";
                Logger.Error($"[PathManager] {errorMessage}");
                throw new DirectoryNotFoundException(errorMessage);
            }

            // Managerフォルダが存在し、実行ファイルがその中にあるか確認
            string managerFolderPath = Path.Combine(detectedBaseDirectory, "Manager");
            if (!Directory.Exists(managerFolderPath))
            {
                string errorMessage = $"エラー: Managerフォルダが見つかりません。\n\n" +
                                     $"プロジェクトルート: {detectedBaseDirectory}\n" +
                                     $"実行ファイルのパス: {exePath}\n\n" +
                                     $"このアプリケーションは、Managerフォルダ内から実行してください。";
                Logger.Error($"[PathManager] {errorMessage}");
                throw new DirectoryNotFoundException(errorMessage);
            }

            // 「等値 OR separator 付き StartsWith」の二段比較。Launcher 側との対称化、および
            // 将来 .NET ランタイムの BaseDirectory が末尾 `\` を外した場合の future-proofing
            // (詳細は loop 内の同パターン NOTE を参照)。
            string managerFolderPathWithSep = managerFolderPath + Path.DirectorySeparatorChar;
            if (!exePath.Equals(managerFolderPath, StringComparison.OrdinalIgnoreCase) &&
                !exePath.StartsWith(managerFolderPathWithSep, StringComparison.OrdinalIgnoreCase))
            {
                string errorMessage = $"エラー: 実行ファイルがManagerフォルダ内にありません。\n\n" +
                                     $"プロジェクトルート: {detectedBaseDirectory}\n" +
                                     $"Managerフォルダ: {managerFolderPath}\n" +
                                     $"実行ファイルのパス: {exePath}\n\n" +
                                     $"このアプリケーションは、Managerフォルダ内から実行してください。";
                Logger.Error($"[PathManager] {errorMessage}");
                throw new DirectoryNotFoundException(errorMessage);
            }

            return detectedBaseDirectory;
        }
        
        /// <summary>
        /// パスの確認（デバッグ用）
        /// </summary>
        public static void VerifyPaths()
        {
            Logger.Info("=== PathManager - パス確認 ===");
            Logger.Info($"実行ファイル: {AppDomain.CurrentDomain.BaseDirectory}");
            Logger.Info($"プロジェクトルート: {BaseDirectory}");
            Logger.Info($"Gamesフォルダ: {GamesFolder}");
            Logger.Info($"データベース: {DatabasePath}");
            Logger.Info($"");
            Logger.Info($"Gamesフォルダ存在: {Directory.Exists(GamesFolder)}");
            Logger.Info($"データベース存在: {File.Exists(DatabasePath)}");
            Logger.Info("============================");
        }
        
        /// <summary>
        /// 必要なフォルダを作成
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(GamesFolder))
            {
                Directory.CreateDirectory(GamesFolder);
            }
        }
    }
}

