using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// DB 復元後に「復元した toneprism.db」と「現在の games/ フォルダ」を突き合わせ、両者のズレを検出する。
    ///
    /// 背景: バックアップ (BackupService) / 復元 (RestoreService) は toneprism.db 単体のみを対象とし、
    /// games/ フォルダには一切触れない。そのため別時点の DB を復元すると「DB は在るがディスクに無い版」
    /// 「ディスクに在るが DB に無い孤児フォルダ」といったドリフトが起こりうる。リセット (ResetDatabase) は
    /// DB と games/ を両方まっさらにするためズレないが、復元はこのサービスで明示的に検出してユーザーに
    /// 復旧手順を提示する。
    /// </summary>
    public class RestoreReconciliationService
    {
        private readonly DatabaseManager _dbManager;

        // version フォルダ leaf の見た目 ("v" + 数字始まり)。孤児判定で誤って素材フォルダ等を
        // 版フォルダ扱いしないための保守的フィルタ。
        private static readonly Regex VersionLeafLike = new Regex(@"^v\d", RegexOptions.IgnoreCase);

        public RestoreReconciliationService(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        public RestoreReconciliationResult Analyze()
        {
            var result = new RestoreReconciliationResult();

            List<GameInfo> games;
            try
            {
                games = _dbManager.GetAllGames();
            }
            catch (Exception ex)
            {
                Logger.Error("[RestoreReconciliation] ゲーム一覧の取得に失敗", ex);
                result.AnalysisFailed = true;
                result.AnalysisError = ex.Message;
                return result;
            }

            var dbGameIds = new HashSet<string>(
                games.Select(g => g.GameId ?? "").Where(id => id.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            string baseDir = PathManager.BaseDirectory;

            foreach (var game in games)
            {
                if (string.IsNullOrWhiteSpace(game.GameId)) continue;
                string gameFolder = PathManager.GetGameFolder(game.GameId);

                // (1) 起動できないゲーム = アクティブ版の実行ファイルが解決できない。
                //     Launcher.find_executable と同じ順で解決を試みる (絶対パス → ゲームルート基準 → install 基準)。
                if (!ResolvesExecutable(game.ExecutablePath, gameFolder, baseDir))
                {
                    result.BrokenGames.Add(new BrokenGame
                    {
                        GameId = game.GameId,
                        Title = string.IsNullOrWhiteSpace(game.Title) ? game.GameId : game.Title,
                        ActiveVersion = game.Version,
                        ExpectedExecutable = string.IsNullOrEmpty(game.ExecutablePath)
                            ? "(実行ファイル未設定)"
                            : Path.Combine(gameFolder, game.ExecutablePath),
                        GameFolderExists = Directory.Exists(gameFolder)
                    });
                }

                // (2) DB に在るがディスクに無い版フォルダ。
                List<GameVersion> versions;
                try
                {
                    versions = _dbManager.GetGameVersions(game.GameId);
                }
                catch (Exception ex)
                {
                    Logger.Warn("[RestoreReconciliation] 版一覧の取得に失敗 (game_id=" + game.GameId + "): " + ex.Message);
                    versions = new List<GameVersion>();
                }

                var knownLeaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in versions)
                {
                    string leaf = PathManager.GetVersionFolderLeaf(v.Version);
                    knownLeaves.Add(leaf);
                    string versionDir = Path.Combine(gameFolder, leaf);
                    if (!Directory.Exists(versionDir))
                    {
                        result.MissingVersionFolders.Add(new MissingVersionFolder
                        {
                            GameId = game.GameId,
                            Title = string.IsNullOrWhiteSpace(game.Title) ? game.GameId : game.Title,
                            Version = v.Version,
                            ExpectedFolder = versionDir
                        });
                    }
                }

                // (3b) 既知ゲーム配下の孤児版フォルダ (v* だが DB に対応版が無い)。
                if (Directory.Exists(gameFolder))
                {
                    foreach (var sub in SafeGetDirectories(gameFolder))
                    {
                        string leaf = Path.GetFileName(sub);
                        if (leaf == null || !VersionLeafLike.IsMatch(leaf)) continue;
                        if (!knownLeaves.Contains(leaf))
                        {
                            result.OrphanFolders.Add(new OrphanFolder { Path = sub, Kind = OrphanKind.Version });
                        }
                    }
                }
            }

            // (3a) games/ 直下で DB に無い孤児ゲームフォルダ。
            string gamesRoot = PathManager.GamesFolder;
            if (Directory.Exists(gamesRoot))
            {
                foreach (var dir in SafeGetDirectories(gamesRoot))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.IndexOf(".pending-delete-", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!dbGameIds.Contains(name))
                    {
                        result.OrphanFolders.Add(new OrphanFolder { Path = dir, Kind = OrphanKind.Game });
                    }
                }
            }

            return result;
        }

        private static bool ResolvesExecutable(string executablePath, string gameFolder, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return false;
            try
            {
                if (Path.IsPathRooted(executablePath))
                    return File.Exists(executablePath);
                if (File.Exists(Path.Combine(gameFolder, executablePath)))
                    return true;
                if (File.Exists(Path.Combine(baseDir, executablePath)))
                    return true;
            }
            catch
            {
                // 不正なパス文字等は「解決不可」として扱う。
                return false;
            }
            return false;
        }

        private static IEnumerable<string> SafeGetDirectories(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return Array.Empty<string>(); }
        }
    }

    public class RestoreReconciliationResult
    {
        public bool AnalysisFailed { get; set; }
        public string AnalysisError { get; set; }

        public List<BrokenGame> BrokenGames { get; } = new List<BrokenGame>();
        public List<MissingVersionFolder> MissingVersionFolders { get; } = new List<MissingVersionFolder>();
        public List<OrphanFolder> OrphanFolders { get; } = new List<OrphanFolder>();

        /// <summary>起動に直結する深刻な問題 (= 復元時点のフォルダ補完が必要)。</summary>
        public bool HasCriticalFindings => BrokenGames.Count > 0;

        /// <summary>何らかのズレが見つかったか (深刻 / 軽微いずれか)。</summary>
        public bool HasAnyFindings =>
            BrokenGames.Count > 0 || MissingVersionFolders.Count > 0 || OrphanFolders.Count > 0;
    }

    public class BrokenGame
    {
        public string GameId { get; set; }
        public string Title { get; set; }
        public string ActiveVersion { get; set; }
        public string ExpectedExecutable { get; set; }
        public bool GameFolderExists { get; set; }
    }

    public class MissingVersionFolder
    {
        public string GameId { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string ExpectedFolder { get; set; }
    }

    public enum OrphanKind { Game, Version }

    public class OrphanFolder
    {
        public string Path { get; set; }
        public OrphanKind Kind { get; set; }
    }
}
