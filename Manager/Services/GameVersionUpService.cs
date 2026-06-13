using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#245 ゲーム画面 WPF 化 ①) ゲームのバージョンアップ・オーケストレーションを UI から抽出した service。
    /// コピー (tempDir) → atomic Directory.Move → アセット存在 check → パス前置 → アクティブ化 → DB 保存 →
    /// バックアップ、までを並行 Manager 競合 / ロールバック込みで実行する。元は GameSectionPanel.btnVersionUp_Click に
    /// 直書きされていた重ロジック (CLAUDE.md「UI は薄く、ロジックは外へ」)。挙動は完全保存し、WinForms 依存 (VersionUpForm /
    /// ProcessingDialog / MessageBox) は据え置き＝<paramref name="owner"/> を所有者にして開く。WinForms パネルからも
    /// WPF ページからも同一 service を呼べる。
    ///
    /// **呼び出し側責務**: 行選択の検証・対象ゲームの解決 (GetGameById)・SessionConflictHelper.CheckBeforeWrite を
    /// 済ませてから <see cref="Run"/> を呼ぶこと (これらは UI 層に固有なため service には含めない)。
    /// </summary>
    public class GameVersionUpService
    {
        private readonly DatabaseManager _dbManager;

        public GameVersionUpService(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        /// <summary>
        /// バージョンアップを実行する。<paramref name="owner"/> = ダイアログ所有者、<paramref name="game"/> = 対象ゲーム
        /// (解決済)、<paramref name="reload"/> = 一覧再読込コールバック (成功時に呼ぶ)。
        /// </summary>
        public void Run(IWin32Window owner, GameInfo game, Action reload)
        {
            // (#234 ①) 全バージョンを取得し、最新版判定と VersionUpForm の重複チェックの両方に使う。
            // GetByGameId は id DESC 順なので先頭が最新 (GetLatestVersion と等価)。
            var allVersions = _dbManager.GetGameVersions(game.GameId);
            var latestVersion = allVersions.FirstOrDefault();
            string currentVersion = latestVersion?.Version ?? "1.0.0";
            var existingVersionStrings = allVersions.Select(v => v.Version).ToList();

            using (var form = new VersionUpForm(game, currentVersion, latestVersion, existingVersionStrings))
            {
                if (form.ShowDialog() == DialogResult.OK && form.NewVersion != null)
                {
                    string versionDir = PathManager.GetVersionFolder(game.GameId, form.NewVersion.Version);

                    // (累積監査 round 4 Critical-1) 並行 Manager race で勝者の versionDir を loser の rollback が
                    // 物理削除する経路を構造的に閉鎖する目的で、コピーは「自分専用の tempDir に書く → 全工程
                    // 成功後に Directory.Move で atomic に versionDir へ昇格」の 2 段に分離した。Directory.Move は
                    // 移動先が既存だと失敗するため、敗者は失敗を確定して自分の tempDir のみ delete (= 勝者の
                    // 物理ファイルには触れない)。`versionDirOwnedByThisCall` flag は move 成功後にだけ true にして、
                    // 後段の missing-asset / DB save 失敗時の cleanup でも勝者の versionDir を絶対に削除しない。
                    string tempDir = versionDir + ".pending-create-" + Guid.NewGuid().ToString("N");
                    bool versionDirOwnedByThisCall = false;

                    // (#234 ① 二重防御) DB 上は重複しない version でも、過去の中断 (#234 ③) で version
                    // folder だけが残っている場合がある。そのまま CopyDirectory すると既存フォルダへ
                    // 上書きマージされるため、ここで明示的に衝突を弾く (AddGameForm.CopyGameFolder と同方針)。
                    if (Directory.Exists(versionDir))
                    {
                        MessageBox.Show(
                            "バージョンフォルダが既に存在します:\n  " + versionDir + "\n\n" +
                            "前回のバージョンアップが中断された残骸の可能性があります。中身を確認し、" +
                            "必要なファイルを退避してからフォルダを削除して再試行してください。",
                            "フォルダ衝突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // (Finding #2) コピー時に除外されるフォルダ (Library / node_modules 等) があれば、
                    // 一覧を出して続行可否を確認する。除外を silent にしないための fence (Add 経路と共通)。
                    if (!GameFormHelper.ConfirmExcludedFoldersBeforeCopy(owner, form.SourceFolderPath))
                    {
                        return;
                    }

                    // (累積監査) ProcessingDialog は他箇所と同様 using で囲み、ハンドル / 内部 CTS の
                    // リークを防ぐ (ShowDialog は Dispose しないため明示破棄が必要)。
                    using (var processingDialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
                    {
                        try
                        {
                            // (Critical-1) tempDir にコピーする。後段で Directory.Move で atomic に versionDir へ昇格。
                            Directory.CreateDirectory(tempDir);
                            // (#234 追加精査) 旧実装は IsVersionFolder で v* フォルダを除外していたが、
                            // (a) コピー先が games/{id}/v.../ でソース内側になる「ルート選択」誤操作は
                            // CopyDirectoryRecursive 冒頭の再帰ガードが既に空コピーで防ぐため除外は無力、
                            // (b) 正当な v* 名フォルダを無言で取りこぼす下しか無い保険だった。除外を撤去し、
                            // ルート選択自体は VersionUpForm.ValidateInput の games/ 配下ソース拒否で明示的に弾く。
                            // (追加精査 ②) 個別 File.Copy 失敗を呼び出し側に伝播。1 件でも失敗があれば
                            // throw して ProcessingDialog の catch → 上位 cleanup 経路に流す。
                            var copyFailures = FileOperationService.CopyDirectoryWithProgress(
                                form.SourceFolderPath, tempDir, progress, token);
                            if (copyFailures.Count > 0)
                            {
                                string msg = FileOperationService.FormatCopyFailureMessage(copyFailures, form.SourceFolderPath);
                                throw new Exception(msg + "\n\nファイルが他のアプリケーションに開かれていないか、コピー元・コピー先の権限・ディスク容量を確認してください。");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"ファイルコピー中にエラーが発生しました: {ex.Message}", ex);
                        }
                    }))
                    {

                    if (processingDialog.ShowDialog() == DialogResult.OK)
                    {
                        // (Critical-1) tempDir の中身が全件揃ったので、Directory.Move で atomic に versionDir へ昇格。
                        // Move は移動先が既存だと失敗するため、並行 Manager の勝者が既に versionDir を作っていれば
                        // ここで弾かれる (= 我々は敗者)。敗者は自分の tempDir のみ delete して abort。勝者の
                        // 物理ファイルには絶対に触れないので Critical-1 / Medium-15 / Medium-16 を一括閉鎖。
                        try
                        {
                            Directory.Move(tempDir, versionDir);
                            versionDirOwnedByThisCall = true;
                        }
                        catch (IOException moveEx)
                        {
                            HandleVersionDirMoveFailure(tempDir, versionDir, moveEx);
                            return;
                        }
                        // (round 5 H1) MSDN 公式仕様: Directory.Move は権限拒否 (ACL / read-only attr /
                        // 親フォルダロック中等) で UnauthorizedAccessException を投げる。これは IOException を
                        // 継承していないため上記 catch をすり抜け、上位の WinForms 既定の ThreadException ダイアログ
                        // (英文 stack trace) が出て tempDir (`.pending-create-{guid}`) が永続残置 → disk 容量蓄積。
                        // round 4 R4-M10 で legacy safety MoveTo に既に UAE 別 catch を入れた規約と非対称解消。
                        catch (UnauthorizedAccessException moveEx)
                        {
                            HandleVersionDirMoveFailure(tempDir, versionDir, moveEx);
                            return;
                        }

                        // (追加精査 ②) DB commit 直前に exe / サムネ / 背景の実体存在を最終 check。
                        // CopyDirectoryWithProgress は failed list で個別 copy 失敗を伝播済だが、
                        // case 違いやコピー後の race による乖離を最後の砦として弾く。失敗時は versionDir
                        // を削除して入力やり直しを促す (DB 行は未 commit のため rollback 不要)。
                        // (Critical-1) Move 成功後の versionDir は我々の所有物なので無条件 delete でも勝者破壊
                        // にはならない (= versionDirOwnedByThisCall=true)。
                        var missingVersionAssets = new System.Collections.Generic.List<string>();
                        string exeCheckPath = string.IsNullOrEmpty(form.RelativeExecutablePath)
                            ? null
                            : Path.Combine(versionDir, form.RelativeExecutablePath);
                        string thumbCheckPath = string.IsNullOrEmpty(form.NewVersion.ThumbnailPath)
                            ? null
                            : Path.Combine(versionDir, form.NewVersion.ThumbnailPath);
                        string bgCheckPath = string.IsNullOrEmpty(form.NewVersion.BackgroundPath)
                            ? null
                            : Path.Combine(versionDir, form.NewVersion.BackgroundPath);
                        if (!string.IsNullOrEmpty(exeCheckPath) && !File.Exists(exeCheckPath))
                            missingVersionAssets.Add("実行ファイル: " + exeCheckPath);
                        if (!string.IsNullOrEmpty(thumbCheckPath) && !File.Exists(thumbCheckPath))
                            missingVersionAssets.Add("サムネイル: " + thumbCheckPath);
                        if (!string.IsNullOrEmpty(bgCheckPath) && !File.Exists(bgCheckPath))
                            missingVersionAssets.Add("背景画像: " + bgCheckPath);
                        if (missingVersionAssets.Count > 0)
                        {
                            if (versionDirOwnedByThisCall)
                            {
                                // (#288) read-only な Unity/Godot コピー (Assets/Library 等) でも消せるよう FolderDeletionService 経由。
                                var delResult = FolderDeletionService.TryDelete(versionDir);
                                if (!delResult.Success) Logger.Warn("[GameVersionUpService] (追加精査 ②) versionDir 削除失敗 (read-only/ロック等): " + versionDir + ": " + delResult.ErrorMessage);
                            }
                            MessageBox.Show(
                                "コピー後のファイルが見つかりません。バージョンアップを中止しました:\n\n  " +
                                string.Join("\n  ", missingVersionAssets) +
                                "\n\nコピー元のパス指定 / 権限 / ディスク容量を確認のうえ再試行してください。",
                                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        // (#234) disk のコピー先フォルダ (PathManager.GetVersionFolder = versionDir) と
                        // 同じ正規化規則で leaf 名を作る。両者を別実装で計算すると "V1.0.0" のような生値で
                        // 食い違い、DB 保存パスが実フォルダを指さなくなるため GetVersionFolderLeaf に揃える。
                        string versionFolderName = PathManager.GetVersionFolderLeaf(form.NewVersion.Version);

                        // (M4 二段目) Path.Combine の「第二引数が絶対 path なら第一引数を破棄」仕様で
                        // versionFolderName が無視され絶対 path がそのまま DB 保存される silent corruption を
                        // 物理閉鎖。VersionUpForm は M4 修正で relative 化済の path のみ返す契約だが、
                        // 将来の caller drift / fallback 経路への defense として ここでも assert。
                        if (!string.IsNullOrEmpty(form.RelativeExecutablePath) && Path.IsPathRooted(form.RelativeExecutablePath))
                        {
                            if (versionDirOwnedByThisCall)
                            {
                                // (#288) read-only コピー対応のため FolderDeletionService 経由。
                                var delResult = FolderDeletionService.TryDelete(versionDir);
                                if (!delResult.Success) Logger.Warn("[GameVersionUpService] (M4) versionDir 削除失敗 (read-only/ロック等): " + versionDir + ": " + delResult.ErrorMessage);
                            }
                            MessageBox.Show(
                                "実行ファイルの相対パス計算に失敗しました。バージョンアップを中止しました。\n\n" +
                                "コピー元フォルダを指定し直してください。",
                                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        string relativePath = Path.Combine(versionFolderName, form.RelativeExecutablePath);
                        form.NewVersion.ExecutablePath = relativePath;

                        // (#234) thumbnail / background も exe と同様に v{version}/ プレフィックスを付ける。
                        // VersionUpForm は source フォルダ基準の相対パス (プレフィックス無し、例: "thumb.png")
                        // で返すため、そのまま games / game_versions に保存すると Launcher がゲームルート基準
                        // (games/{id}/thumb.png) で解決して見つけられず、バージョンアップ後に画像が消える。
                        // exe (上の relativePath) と同じく version フォルダ名を前置してゲームルート基準に揃える。
                        // 両テーブル (NewVersion=game_versions / UpdatedGameInfo=activation 時の games) に反映。
                        // (M4 二段目) thumbnail / background も絶対 path assert。
                        if (!string.IsNullOrEmpty(form.NewVersion.ThumbnailPath) && !Path.IsPathRooted(form.NewVersion.ThumbnailPath))
                        {
                            string thumbRelative = Path.Combine(versionFolderName, form.NewVersion.ThumbnailPath);
                            form.NewVersion.ThumbnailPath = thumbRelative;
                            form.UpdatedGameInfo.ThumbnailPath = thumbRelative;
                        }
                        else if (!string.IsNullOrEmpty(form.NewVersion.ThumbnailPath))
                        {
                            Logger.Warn("[GameVersionUpService] (M4) ThumbnailPath が絶対 path、保存 skip: " + form.NewVersion.ThumbnailPath);
                            form.NewVersion.ThumbnailPath = null;
                            form.UpdatedGameInfo.ThumbnailPath = null;
                        }
                        if (!string.IsNullOrEmpty(form.NewVersion.BackgroundPath) && !Path.IsPathRooted(form.NewVersion.BackgroundPath))
                        {
                            string bgRelative = Path.Combine(versionFolderName, form.NewVersion.BackgroundPath);
                            form.NewVersion.BackgroundPath = bgRelative;
                            form.UpdatedGameInfo.BackgroundPath = bgRelative;
                        }
                        else if (!string.IsNullOrEmpty(form.NewVersion.BackgroundPath))
                        {
                            Logger.Warn("[GameVersionUpService] (M4) BackgroundPath が絶対 path、保存 skip: " + form.NewVersion.BackgroundPath);
                            form.NewVersion.BackgroundPath = null;
                            form.UpdatedGameInfo.BackgroundPath = null;
                        }

                        // (M5) activation 確認を DB write より前倒し。Yes 確定の場合 AddVersionAndActivate で
                        // version 行 INSERT と games 行 UPDATE を 1 transaction で atomic 実行 (partial commit
                        // 窓を物理閉鎖)。No なら従来通り AddGameVersion のみ。旧実装は AddGameVersion 後に
                        // 確認 dialog を出していたが、両 DB write を Yes 確定時に統合できる UI 順序へ整理。
                        var activationResult = MessageBox.Show(
                            $"バージョン {form.NewVersion.Version} を現在のバージョン（アクティブ）として設定しますか？\n\n「いいえ」を選択した場合、バージョンは作成されますが、ランチャーで起動するバージョンは変更されません。",
                            "アクティブバージョンの確認",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        try
                        {
                            if (activationResult == DialogResult.Yes)
                            {
                                // atomic: version INSERT + games UPDATE in single transaction
                                form.UpdatedGameInfo.ExecutablePath = relativePath;
                                _dbManager.AddVersionAndActivate(form.NewVersion, form.UpdatedGameInfo);
                            }
                            else
                            {
                                _dbManager.AddGameVersion(form.NewVersion);
                            }
                        }
                        catch (Exception ex)
                        {
                            // (Critical-1) Move 成功後 (versionDirOwnedByThisCall=true) のみ versionDir を削除。
                            // ここに来る時点で UNIQUE 違反等の DB エラーが起きているが、Move 自体は成功して
                            // 我々が versionDir 所有者なので、delete しても勝者破壊にはならない。
                            string cleanupNote;
                            if (versionDirOwnedByThisCall)
                            {
                                // (#288) read-only コピー対応のため FolderDeletionService 経由。
                                var delResult = FolderDeletionService.TryDelete(versionDir);
                                if (!delResult.Success) Logger.Warn("[GameVersionUpService] (M5) versionDir rollback 削除失敗 (read-only/ロック等): " + versionDir + ": " + delResult.ErrorMessage);
                                cleanupNote = "\n\nコピーしたファイルは削除しました。";
                            }
                            else
                            {
                                cleanupNote = "";
                            }
                            MessageBox.Show(
                                $"バージョン情報のデータベース保存に失敗しました。\n\n{ex.Message}{cleanupNote}",
                                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        MessageBox.Show(
                            $"ゲーム「{game.Title}」のバージョン {form.NewVersion.Version} を追加しました。",
                            "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        reload();
                        // (#295) バージョンアップは games/{id}/v{} を作るので DB + ゲーム本体を控える。
                        _dbManager.SessionBackupCoordinator.RunAfterOperation(owner, assetsChanged: true, "バージョンアップ");
                    }
                    else
                    {
                        // (H3) 非 OK 経路 (Cancel / Abort=例外) は版データ未 commit + コピー済 tempDir が
                        // 残留する状態。tempDir は guid 付きで永続的に block する経路は無いが、disk full
                        // 防止のため掃除する。
                        // (Critical-1) この経路は ProcessingDialog 内 = Move 前なので、片付け対象は tempDir のみ。
                        // versionDir は触らない (= 我々はまだ owner ではない、勝者が既に作っていれば破壊しない)。
                        // (#288) read-only コピー (.pending-create) でも消せるよう FolderDeletionService 経由。
                        var delResult = FolderDeletionService.TryDelete(tempDir);
                        if (!delResult.Success) Logger.Warn("[GameVersionUpService] (H3) 中断時の tempDir 削除失敗 (read-only/ロック等): " + tempDir + ": " + delResult.ErrorMessage);

                        if (processingDialog.DialogResult == DialogResult.Cancel)
                        {
                            MessageBox.Show("処理がキャンセルされました。", "キャンセル",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        // Abort 経路は ProcessingDialog 内で既に「エラーが発生しました: ...」MessageBox を
                        // 出しているため二重に出さない (cleanup のみ実施)。
                    }
                    } // using (processingDialog)
                }
            }
        }

        /// <summary>
        /// (round 5 H1) Directory.Move 失敗時の共通 cleanup + 通知。IOException / UnauthorizedAccessException
        /// の両 catch から呼ばれ、tempDir 削除 + user 通知 MessageBox を一元化する。
        /// </summary>
        private static void HandleVersionDirMoveFailure(string tempDir, string versionDir, Exception moveEx)
        {
            // (#288) read-only コピー (.pending-create) でも消せるよう FolderDeletionService 経由。
            var delResult = FolderDeletionService.TryDelete(tempDir);
            if (!delResult.Success) Logger.Warn("[GameVersionUpService] (round 5 H1) tempDir 削除失敗 (read-only/ロック等): " + tempDir + ": " + delResult.ErrorMessage);

            string detail = moveEx is UnauthorizedAccessException
                ? "別の Manager が同じバージョン番号で既にフォルダを作成した、または対象フォルダの権限が制限されている可能性があります。"
                : "別の Manager が同じバージョン番号で既にフォルダを作成した可能性があります。";

            MessageBox.Show(
                "バージョンフォルダの作成に失敗しました:\n  " + versionDir + "\n\n" +
                detail + "\n" +
                "別のバージョン番号を指定するか、少し待ってから再試行してください。\n\n" +
                "詳細: " + moveEx.Message,
                "フォルダ作成失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
