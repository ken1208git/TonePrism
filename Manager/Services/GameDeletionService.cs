using System;
using System.IO;
using System.Windows.Forms;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#245 ゲーム画面 WPF 化 ①) ゲーム削除のオーケストレーションを UI から抽出した service。
    /// rename-rollback パターン (games/{id}/ を .pending-delete-{guid}/ へ退避 → DB 削除 → 退避フォルダ物理削除、
    /// 各段の失敗で再試行 UI / ロールバック) を実行する。元は GameSectionPanel.btnDeleteGame_Click に直書きされて
    /// いた重ロジック (CLAUDE.md「UI は薄く、ロジックは外へ」)。挙動は完全保存し、WinForms 依存 (DeleteGameConfirmForm /
    /// FolderDeletionFailureDialog / ProcessingDialog / MessageBox) は据え置き＝<paramref name="owner"/> を所有者に開く。
    ///
    /// **呼び出し側責務**: 行選択の検証・SessionConflictHelper.CheckBeforeWrite・対象 GameInfo の解決を済ませてから
    /// <see cref="Run"/> を呼ぶこと (UI 層に固有なため service には含めない)。
    /// </summary>
    public class GameDeletionService
    {
        private readonly DatabaseManager _dbManager;

        public GameDeletionService(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        /// <summary>
        /// ゲーム削除を実行する。<paramref name="owner"/> = ダイアログ所有者、<paramref name="selectedGame"/> = 対象、
        /// <paramref name="reload"/> = 一覧再読込コールバック。
        /// </summary>
        public void Run(IWin32Window owner, GameInfo selectedGame, Action reload)
        {
            // GameId が空だと PathManager.GetGameFolder("") が GamesFolder 自体を返し、
            // Directory.Delete(folder, true) で全ゲームフォルダが消える致命バグになる。防御。
            if (string.IsNullOrWhiteSpace(selectedGame.GameId))
            {
                MessageBox.Show(owner, "ゲームIDが不正です。削除できません。",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string gameFolder = PathManager.GetGameFolder(selectedGame.GameId);

            bool deleteFolder;
            using (var confirm = new DeleteGameConfirmForm(selectedGame.Title, selectedGame.GameId, gameFolder))
            {
                if (confirm.ShowDialog(owner) != DialogResult.Yes) return;
                // フォルダが存在すれば常に削除、不在ならスキップ (#111)
                deleteFolder = confirm.DeleteFolder;
            }

            // 削除フローは rename rollback パターン (リセットと同じ思想、Codex P1 #122):
            //   (1) games/{gameId}/ を games/{gameId}.pending-delete-{guid}/ に rename で退避
            //       失敗 = Launcher 等がファイルロック中 → 再試行 UI、諦めたら全体中止
            //   (2) DB 削除 (CASCADE 含む)
            //       失敗 = (1) の rename を戻して「何も変わらない」状態にロールバック → throw
            //   (3) 退避フォルダを物理削除
            //       失敗 = DB は消えたので戻れない、再試行 UI で対処 (諦めたらゴミ残るが Manager は普通に動く)
            // これでフォルダ物理削除前に DB 削除が走るので、永続的なデータロストの可能性を排除。

            string pendingDeleteFolder = gameFolder + ".pending-delete-" + Guid.NewGuid().ToString("N");
            bool gamesRenamed = false;

            // フェーズ 1: フォルダ rename で退避 (失敗時は再試行ループ、諦めたら全体中止)
            if (deleteFolder && Directory.Exists(gameFolder))
            {
                while (true)
                {
                    Exception renameError = null;
                    try
                    {
                        Directory.Move(gameFolder, pendingDeleteFolder);
                        gamesRenamed = true;
                        break;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // 初期 Directory.Exists チェック後に他プロセスがフォルダを削除した
                        // race condition。既にフォルダは無く削除目的は達成しているので
                        // 「rename してない」扱いで次のフェーズ (DB 削除) に進む (Codex P2 #122)
                        gamesRenamed = false;
                        break;
                    }
                    catch (IOException ex) { renameError = ex; }
                    catch (UnauthorizedAccessException ex) { renameError = ex; }

                    using (var failDialog = new FolderDeletionFailureDialog(gameFolder, renameError))
                    {
                        var dr = failDialog.ShowDialog(owner);
                        if (dr != DialogResult.Retry)
                        {
                            MessageBox.Show(owner,
                                "フォルダを退避できなかったため、ゲーム削除を中止しました。\n" +
                                "Launcher を閉じてから再度「削除」をお試しください。",
                                "中止", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }
                }
            }

            // フェーズ 2: DB 削除 (CASCADE で developers / game_versions / store_section_games も削除される。
            //   ※ play_records / surveys は DB v23/#297 で撤去済、game_genres は v18 で撤去済)
            //   失敗時は (1) で退避したフォルダを games/{gameId}/ に戻して全体ロールバック
            //   ロールバック失敗 (権限変更・他プロセスが games/{gameId}/ を再作成した等) は
            //   rollbackError に記録し、後段で正確に通知する (Codex P2 #122)
            Exception caught = null;
            Exception rollbackError = null;
            DialogResult dbDr;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                try
                {
                    progress?.Report(new ProgressInfo(-1, "データベースから削除中...",
                        $"「{selectedGame.Title}」と関連レコードをデータベースから削除しています"));
                    _dbManager.DeleteGame(selectedGame.GameId);
                }
                catch (Exception ex)
                {
                    caught = ex;
                    // ロールバック: 退避フォルダを games/{gameId}/ に戻す
                    if (gamesRenamed && Directory.Exists(pendingDeleteFolder) && !Directory.Exists(gameFolder))
                    {
                        try { Directory.Move(pendingDeleteFolder, gameFolder); }
                        catch (Exception rbEx) { rollbackError = rbEx; }
                    }
                    else if (gamesRenamed && Directory.Exists(gameFolder))
                    {
                        // 何らかの理由で games/{gameId}/ が既に存在する (例: 別プロセスが再作成) →
                        // 安全にロールバックできない
                        rollbackError = new IOException(
                            $"ロールバック先 '{gameFolder}' が既に存在するためロールバックできません。");
                    }
                    throw;
                }
            })
            {
                Text = "ゲーム削除中",
                MarqueeMode = true,
                AllowCancel = false
            })
            {
                dbDr = dialog.ShowDialog(owner);
            }

            if (dbDr != DialogResult.OK)
            {
                string baseMsg;
                if (caught is System.Data.SQLite.SQLiteException sqEx)
                {
                    baseMsg = $"データベースからの削除に失敗しました。\n\n{DatabaseManager.GetUserFriendlyErrorMessage(sqEx)}";
                }
                else
                {
                    baseMsg = "データベースからの削除に失敗しました。" +
                        (caught != null ? $"\n\n{caught.Message}" : "");
                }

                string rollbackMsg;
                if (!gamesRenamed)
                {
                    rollbackMsg = ""; // 元々 rename していないので戻す対象なし
                }
                else if (rollbackError != null)
                {
                    // ロールバック失敗 → 嘘をつかず正確に通知
                    rollbackMsg = "\n\n【さらに重要】退避フォルダの復元にも失敗しました。\n" +
                        $"  退避先: {pendingDeleteFolder}\n" +
                        $"  本来の場所: {gameFolder}\n" +
                        $"手動で退避先を本来の場所に戻してください。\n\n復元失敗の詳細: {rollbackError.Message}";
                }
                else
                {
                    rollbackMsg = "\n\nフォルダは元に戻されています。";
                }

                MessageBox.Show(owner,
                    baseMsg + rollbackMsg,
                    "データベースエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                reload();
                return;
            }

            // フェーズ 3: 退避フォルダを物理削除 (失敗時は再試行 UI)
            //   ここまで来た = DB 削除成功。退避フォルダ物理削除に失敗してもゴミとして残るだけで
            //   Manager は普通に動く (リセットの Step 4 と同じ位置付け)。
            bool pendingGivenUp = false;
            if (gamesRenamed)
            {
                while (true)
                {
                    var result = FolderDeletionService.TryDelete(pendingDeleteFolder);
                    if (result.Success) break;

                    using (var failDialog = new FolderDeletionFailureDialog(pendingDeleteFolder, result.LastError))
                    {
                        var dr = failDialog.ShowDialog(owner);
                        if (dr != DialogResult.Retry)
                        {
                            pendingGivenUp = true;
                            break;
                        }
                    }
                }
            }

            if (pendingGivenUp)
            {
                MessageBox.Show(owner,
                    "ゲームを削除しましたが、退避済みのフォルダの物理削除を諦めました。\n" +
                    "後で手動削除してください:\n  " + pendingDeleteFolder,
                    "ゲームフォルダ削除の警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(owner,
                    deleteFolder ? "ゲームと関連フォルダを削除しました。" : "ゲームを削除しました。",
                    "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            reload();
            // (#295) ゲーム削除のバックアップ。フォルダを退避/削除した (gamesRenamed) ときだけ games/ が変わるので
            // アセットも控える。DB だけ削除 (フォルダ温存) なら DB だけ控えて重い走査を skip。
            _dbManager.SessionBackupCoordinator.RunAfterOperation(owner, assetsChanged: gamesRenamed, "ゲーム削除");
        }
    }
}
