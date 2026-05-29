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

            // (追加精査 ③) schema version の不一致を critical findings として検出する。
            // v14→v15 のような migration が「重複行残存」で partial skip した場合、SchemaManager は
            // Logger.Warn のみで user_version を据え置いて起動継続する (起動可能性優先の設計、
            // SchemaManager L849-868 参照)。その状態だと UNIQUE INDEX 等の最終スキーマが未適用なのに
            // RestoreReportForm が「✓ 復元完了：問題なし」と誤った安心メッセージを出す経路が残る。
            // ここで明示的に actual vs target を比較してレポートに乗せる。実害は復元時に限らないが
            // 復元直後はユーザーが必ずレポート画面を見るため、この経路で表面化させる。
            try
            {
                int actual = _dbManager.GetActualDatabaseVersion();
                int target = _dbManager.GetTargetDatabaseVersion();
                result.ActualSchemaVersion = actual;
                result.ExpectedSchemaVersion = target;
                if (actual < target)
                {
                    result.SchemaIncomplete = true;
                    Logger.Warn("[RestoreReconciliation] DB スキーマが未完です: actual=v" + actual + ", target=v" + target);
                }
            }
            catch (Exception ex)
            {
                // schema version 取得失敗は致命でない (= 既存の整合性チェックは続行)。Warn のみ。
                Logger.Warn("[RestoreReconciliation] DB スキーマ version の取得に失敗: " + ex.Message);
            }

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

                // (累積監査 round 4 High-7) アクティブ版の thumbnail / background が物理欠落していれば warning として
                // 報告する (Launcher の MainForm サムネ表示 / 背景描画が silent に欠落するのを表面化)。
                if (!string.IsNullOrWhiteSpace(game.ThumbnailPath) && !ResolvesAsset(game.ThumbnailPath, gameFolder, baseDir))
                {
                    result.BrokenAssets.Add(new BrokenAsset
                    {
                        GameId = game.GameId,
                        Title = string.IsNullOrWhiteSpace(game.Title) ? game.GameId : game.Title,
                        Version = game.Version,
                        AssetKind = "サムネイル",
                        ExpectedPath = Path.IsPathRooted(game.ThumbnailPath) ? game.ThumbnailPath : Path.Combine(gameFolder, game.ThumbnailPath)
                    });
                }
                if (!string.IsNullOrWhiteSpace(game.BackgroundPath) && !ResolvesAsset(game.BackgroundPath, gameFolder, baseDir))
                {
                    result.BrokenAssets.Add(new BrokenAsset
                    {
                        GameId = game.GameId,
                        Title = string.IsNullOrWhiteSpace(game.Title) ? game.GameId : game.Title,
                        Version = game.Version,
                        AssetKind = "背景画像",
                        ExpectedPath = Path.IsPathRooted(game.BackgroundPath) ? game.BackgroundPath : Path.Combine(gameFolder, game.BackgroundPath)
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
                    // (累積監査) version が空文字 / 空白のみの game_versions 行が 1 つでもあると
                    // GetVersionFolderLeaf が ArgumentException を投げ、ループごと中断 → Analyze() 全体が落ち、
                    // 呼び出し側 (BackupSectionPanel) が握り潰して「整合性に問題なし」と誤通知してしまう。
                    // 不正な版はその版だけ skip + 警告して、他ゲーム / 他版の検査は継続する。
                    if (string.IsNullOrWhiteSpace(v.Version))
                    {
                        Logger.Warn("[RestoreReconciliation] version が空の game_versions 行を検出 (game_id=" + game.GameId + ")、当該版を skip");
                        continue;
                    }
                    string leaf;
                    try
                    {
                        leaf = PathManager.GetVersionFolderLeaf(v.Version);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("[RestoreReconciliation] version '" + v.Version + "' の leaf 名生成に失敗 (game_id=" + game.GameId + "): " + ex.Message + "、当該版を skip");
                        continue;
                    }
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
                        continue; // フォルダごと欠落なら exe / 画像 check は冗長。
                    }

                    // (累積監査 round 4 High-7) 非アクティブ版の exe / thumbnail / background も検証する。
                    // 旧実装はアクティブ版のみ check で、非アクティブ版に切替えた瞬間に起動不能になる状態でも
                    // 「✓ 復元完了」と誤通知していた。フォルダは存在するが個別ファイルが欠落するケースを拾う。
                    // アクティブ版 (= 上の BrokenGames 経路) と重複しないよう、`v.Version == game.Version` の場合は skip。
                    bool isActiveVersion = !string.IsNullOrEmpty(game.Version) &&
                        string.Equals(v.Version, game.Version, StringComparison.OrdinalIgnoreCase);
                    if (!isActiveVersion)
                    {
                        if (!string.IsNullOrWhiteSpace(v.ExecutablePath) && !ResolvesExecutable(v.ExecutablePath, gameFolder, baseDir))
                        {
                            result.BrokenVersions.Add(new BrokenVersion
                            {
                                GameId = game.GameId,
                                Title = string.IsNullOrWhiteSpace(game.Title) ? game.GameId : game.Title,
                                Version = v.Version,
                                ExpectedExecutable = Path.IsPathRooted(v.ExecutablePath) ? v.ExecutablePath : Path.Combine(gameFolder, v.ExecutablePath)
                            });
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(v.ThumbnailPath) && !ResolvesAsset(v.ThumbnailPath, gameFolder, baseDir))
                    {
                        result.BrokenAssets.Add(new BrokenAsset
                        {
                            GameId = game.GameId,
                            Title = string.IsNullOrWhiteSpace(game.Title) ? game.GameId : game.Title,
                            Version = v.Version,
                            AssetKind = "サムネイル",
                            ExpectedPath = Path.IsPathRooted(v.ThumbnailPath) ? v.ThumbnailPath : Path.Combine(gameFolder, v.ThumbnailPath)
                        });
                    }
                    if (!string.IsNullOrWhiteSpace(v.BackgroundPath) && !ResolvesAsset(v.BackgroundPath, gameFolder, baseDir))
                    {
                        result.BrokenAssets.Add(new BrokenAsset
                        {
                            GameId = game.GameId,
                            Title = string.IsNullOrWhiteSpace(game.Title) ? game.GameId : game.Title,
                            Version = v.Version,
                            AssetKind = "背景画像",
                            ExpectedPath = Path.IsPathRooted(v.BackgroundPath) ? v.BackgroundPath : Path.Combine(gameFolder, v.BackgroundPath)
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

        /// <summary>
        /// (累積監査 round 4 High-7) thumbnail / background など非実行ファイル系の解決 helper。
        /// 実装は ResolvesExecutable と同じ三段 (絶対 / gameFolder 基準 / install 基準) だが、意味的区別のため別名にする。
        /// </summary>
        private static bool ResolvesAsset(string assetPath, string gameFolder, string baseDir)
        {
            return ResolvesExecutable(assetPath, gameFolder, baseDir);
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
        // (累積監査 round 4 High-7) 非アクティブ版で exe が解決不能なケース。フォルダはあるが個別 file が欠落。
        // この版に切替えた瞬間に起動不能になるため、復元直後にユーザーに表面化する。
        public List<BrokenVersion> BrokenVersions { get; } = new List<BrokenVersion>();
        // (累積監査 round 4 High-7) thumbnail / background の物理欠落。画像なので起動はできるが UI が劣化する warning。
        public List<BrokenAsset> BrokenAssets { get; } = new List<BrokenAsset>();

        /// <summary>
        /// (追加精査 ③) DB スキーマが未完 (= migration が partial skip された) ことを示す。
        /// 例: v14→v15 で `(game_id, version)` 重複残存により UNIQUE INDEX 作成を skip した場合、
        /// user_version は 14 のまま据え置かれ、UNIQUE 制約は未適用のまま起動継続する。SchemaManager 側は
        /// Logger.Warn しか出さないため、UI 経路として復元レポートで表面化させる。
        /// </summary>
        public bool SchemaIncomplete { get; set; }

        /// <summary>実際の DB の user_version (PRAGMA user_version)。SchemaIncomplete 時の表示用。</summary>
        public int ActualSchemaVersion { get; set; }

        /// <summary>このバージョンの Manager が想定する target schema version (= SchemaManager.CurrentDbVersion)。</summary>
        public int ExpectedSchemaVersion { get; set; }

        /// <summary>
        /// 起動に直結する深刻な問題 (= 復元時点のフォルダ補完が必要)、または
        /// schema 未完 (= 後続の DB 操作で重複行増殖等のリスク残存)。
        /// (High-7) BrokenVersions も「版切替時点で起動不能」になるため critical に含める。
        /// </summary>
        public bool HasCriticalFindings => BrokenGames.Count > 0 || BrokenVersions.Count > 0 || SchemaIncomplete;

        /// <summary>何らかのズレが見つかったか (深刻 / 軽微いずれか)。</summary>
        public bool HasAnyFindings =>
            BrokenGames.Count > 0 || MissingVersionFolders.Count > 0 || OrphanFolders.Count > 0
            || BrokenVersions.Count > 0 || BrokenAssets.Count > 0
            || SchemaIncomplete;
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

    /// <summary>
    /// (累積監査 round 4 High-7) 非アクティブ版の exe 欠落。
    /// </summary>
    public class BrokenVersion
    {
        public string GameId { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string ExpectedExecutable { get; set; }
    }

    /// <summary>
    /// (累積監査 round 4 High-7) 画像 asset (thumbnail / background) の欠落。
    /// </summary>
    public class BrokenAsset
    {
        public string GameId { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string AssetKind { get; set; } // "サムネイル" or "背景画像"
        public string ExpectedPath { get; set; }
    }
}
