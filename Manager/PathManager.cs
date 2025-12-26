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
        /// プロジェクトルートを自動検出
        /// 開発時・本番時どちらも同じロジックで検出
        /// </summary>
        private static string FindBaseDirectory()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dir = new DirectoryInfo(exePath);
            
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
                    return currentPath;
                }
                
                // 優先順位2: .git（Gitリポジトリのルート）
                if (Directory.Exists(Path.Combine(currentPath, ".git")))
                {
                    Console.WriteLine($"[PathManager] .git フォルダを検出: {currentPath}");
                    return currentPath;
                }
                
                // 優先順位3: Launcher + Manager（プロジェクト構造）
                if (Directory.Exists(Path.Combine(currentPath, "Launcher")) &&
                    Directory.Exists(Path.Combine(currentPath, "Manager")))
                {
                    Console.WriteLine($"[PathManager] Launcher + Manager を検出: {currentPath}");
                    return currentPath;
                }
                
                dir = dir.Parent;
                currentLevel++;
            }
            
            // どの目印も見つからない場合は実行ファイルと同じ場所を使う
            Console.WriteLine($"[PathManager] 警告: プロジェクトルートが見つかりません。実行パスを使用: {exePath}");
            return exePath;
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

