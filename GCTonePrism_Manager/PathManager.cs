using System;
using System.IO;

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
                    Console.WriteLine($"[PathManager] prism.db を検出: {currentPath}");
                    detectedBaseDirectory = currentPath;
                    break;
                }
                
                // 優先順位2: .git（Gitリポジトリのルート）
                if (Directory.Exists(Path.Combine(currentPath, ".git")))
                {
                    Console.WriteLine($"[PathManager] .git フォルダを検出: {currentPath}");
                    detectedBaseDirectory = currentPath;
                    break;
                }
                
                // 優先順位3: GCTonePrism_Managerフォルダが存在する場合（実行ファイルがその中にある場合）
                // 実行ファイルがGCTonePrism_Managerフォルダ内にある場合、親ディレクトリをプロジェクトルートとする
                if (Directory.Exists(Path.Combine(currentPath, "GCTonePrism_Manager")) &&
                    exePath.StartsWith(Path.Combine(currentPath, "GCTonePrism_Manager"), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[PathManager] GCTonePrism_Managerフォルダを検出: {currentPath}");
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
                                     $"このアプリケーションは、GCTonePrism_Managerフォルダ内から実行してください。";
                Console.WriteLine($"[PathManager] {errorMessage}");
                throw new DirectoryNotFoundException(errorMessage);
            }
            
            // GCTonePrism_Managerフォルダが存在し、実行ファイルがその中にあるか確認
            string managerFolderPath = Path.Combine(detectedBaseDirectory, "GCTonePrism_Manager");
            if (!Directory.Exists(managerFolderPath))
            {
                string errorMessage = $"エラー: GCTonePrism_Managerフォルダが見つかりません。\n\n" +
                                     $"プロジェクトルート: {detectedBaseDirectory}\n" +
                                     $"実行ファイルのパス: {exePath}\n\n" +
                                     $"このアプリケーションは、GCTonePrism_Managerフォルダ内から実行してください。";
                Console.WriteLine($"[PathManager] {errorMessage}");
                throw new DirectoryNotFoundException(errorMessage);
            }
            
            if (!exePath.StartsWith(managerFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                string errorMessage = $"エラー: 実行ファイルがGCTonePrism_Managerフォルダ内にありません。\n\n" +
                                     $"プロジェクトルート: {detectedBaseDirectory}\n" +
                                     $"GCTonePrism_Managerフォルダ: {managerFolderPath}\n" +
                                     $"実行ファイルのパス: {exePath}\n\n" +
                                     $"このアプリケーションは、GCTonePrism_Managerフォルダ内から実行してください。";
                Console.WriteLine($"[PathManager] {errorMessage}");
                throw new DirectoryNotFoundException(errorMessage);
            }
            
            return detectedBaseDirectory;
        }
        
        /// <summary>
        /// パスの確認（デバッグ用）
        /// </summary>
        public static void VerifyPaths()
        {
            Console.WriteLine("=== PathManager - パス確認 ===");
            Console.WriteLine($"実行ファイル: {AppDomain.CurrentDomain.BaseDirectory}");
            Console.WriteLine($"プロジェクトルート: {BaseDirectory}");
            Console.WriteLine($"Gamesフォルダ: {GamesFolder}");
            Console.WriteLine($"データベース: {DatabasePath}");
            Console.WriteLine($"");
            Console.WriteLine($"Gamesフォルダ存在: {Directory.Exists(GamesFolder)}");
            Console.WriteLine($"データベース存在: {File.Exists(DatabasePath)}");
            Console.WriteLine("============================");
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

