using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;

namespace GCTonePrism.Manager.Controls
{
    public partial class GameSectionPanel : UserControl
    {
        private DatabaseManager _dbManager;

        public event Action<string> StatusChanged;

        public int GameCount => dgvGames.Rows.Count;

        public GameSectionPanel()
        {
            InitializeComponent();
        }

        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        public void LoadGames()
        {
            if (_dbManager == null) return;

            try
            {
                var games = _dbManager.GetAllGames()
                    .OrderBy(g => g.Title, StringComparer.CurrentCulture)
                    .ToList();

                dgvGames.DataSource = null;
                dgvGames.DataSource = games;

                ConfigureDataGridView();
                dgvGames.ClearSelection();
                StatusChanged?.Invoke($"ゲーム数: {games.Count}件");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"ゲーム一覧の読み込みに失敗しました。\n\n{ex.Message}",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ConfigureDataGridView()
        {
            if (dgvGames.Columns.Count == 0) return;

            string[] hiddenColumns = {
                "Description", "Genre", "MinPlayers", "MaxPlayers",
                "Difficulty", "PlayTime", "ControllerSupport", "ThumbnailPath",
                "BackgroundPath", "ExecutablePath", "Controls",
                "KeyMapping", "Developers", "DisplayOrder", "SupportedConnection", "Arguments"
            };

            foreach (var columnName in hiddenColumns)
            {
                if (dgvGames.Columns[columnName] != null)
                {
                    dgvGames.Columns[columnName].Visible = false;
                }
            }

            if (dgvGames.Columns["DevelopersDisplay"] == null)
            {
                var developersColumn = new DataGridViewTextBoxColumn
                {
                    Name = "DevelopersDisplay",
                    HeaderText = "製作者",
                    DataPropertyName = "DevelopersDisplay",
                    ReadOnly = true
                };
                dgvGames.Columns.Add(developersColumn);
            }

            if (dgvGames.Columns["Version"] == null)
            {
                var versionColumn = new DataGridViewTextBoxColumn
                {
                    Name = "Version",
                    HeaderText = "バージョン",
                    DataPropertyName = "Version",
                    ReadOnly = true
                };
                dgvGames.Columns.Add(versionColumn);
            }

            if (dgvGames.Columns["GameId"] != null)
                dgvGames.Columns["GameId"].DisplayIndex = 0;
            if (dgvGames.Columns["Title"] != null)
                dgvGames.Columns["Title"].DisplayIndex = 1;
            if (dgvGames.Columns["ReleaseYear"] != null)
                dgvGames.Columns["ReleaseYear"].DisplayIndex = 2;
            if (dgvGames.Columns["DevelopersDisplay"] != null)
                dgvGames.Columns["DevelopersDisplay"].DisplayIndex = 3;
            if (dgvGames.Columns["Version"] != null)
                dgvGames.Columns["Version"].DisplayIndex = 4;
            if (dgvGames.Columns["IsVisible"] != null)
                dgvGames.Columns["IsVisible"].DisplayIndex = 5;

            if (dgvGames.Columns["GameId"] != null)
                dgvGames.Columns["GameId"].HeaderText = "ゲームID";
            if (dgvGames.Columns["Title"] != null)
                dgvGames.Columns["Title"].HeaderText = "タイトル";
            if (dgvGames.Columns["ReleaseYear"] != null)
                dgvGames.Columns["ReleaseYear"].HeaderText = "リリース年";
            if (dgvGames.Columns["IsVisible"] != null)
                dgvGames.Columns["IsVisible"].HeaderText = "ランチャー表示";
            if (dgvGames.Columns["DevelopersDisplay"] != null)
                dgvGames.Columns["DevelopersDisplay"].HeaderText = "製作者";
            if (dgvGames.Columns["Version"] != null)
                dgvGames.Columns["Version"].HeaderText = "バージョン";

            dgvGames.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvGames.Columns["GameId"].FillWeight = 80;
            dgvGames.Columns["Title"].FillWeight = 200;
            dgvGames.Columns["ReleaseYear"].FillWeight = 80;
            dgvGames.Columns["DevelopersDisplay"].FillWeight = 150;
            dgvGames.Columns["Version"].FillWeight = 80;
            dgvGames.Columns["IsVisible"].FillWeight = 100;
        }

        private void btnAddGame_Click(object sender, EventArgs e)
        {
            using (var form = new AddGameForm(_dbManager))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    LoadGames();
                    MessageBox.Show(
                        $"ゲーム「{form.AddedGame.Title}」を追加しました。",
                        "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void btnEditGame_Click(object sender, EventArgs e)
        {
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show("編集するゲームを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedGame = dgvGames.SelectedRows[0].DataBoundItem as GameInfo;
            if (selectedGame == null) return;

            var game = _dbManager.GetGameById(selectedGame.GameId);
            if (game == null)
            {
                MessageBox.Show("選択されたゲームが見つかりません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var form = new EditGameForm(_dbManager, game))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    LoadGames();
                    MessageBox.Show(
                        $"ゲーム「{form.EditedGame.Title}」を更新しました。",
                        "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void btnVersionUp_Click(object sender, EventArgs e)
        {
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show("バージョンアップするゲームを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedGame = dgvGames.SelectedRows[0].DataBoundItem as GameInfo;
            if (selectedGame == null) return;

            var game = _dbManager.GetGameById(selectedGame.GameId);
            if (game == null)
            {
                MessageBox.Show("選択されたゲームが見つかりません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var latestVersion = _dbManager.GetLatestVersion(game.GameId);
            string currentVersion = latestVersion?.Version ?? "1.0.0";

            using (var form = new VersionUpForm(game, currentVersion, latestVersion))
            {
                if (form.ShowDialog() == DialogResult.OK && form.NewVersion != null)
                {
                    var processingDialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
                    {
                        try
                        {
                            string versionDir = PathManager.GetVersionFolder(game.GameId, form.NewVersion.Version);
                            Directory.CreateDirectory(versionDir);
                            FileOperationService.CopyDirectoryWithProgress(
                                form.SourceFolderPath, versionDir, progress, token,
                                excludeFolderPredicate: FileOperationService.IsVersionFolder);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"ファイルコピー中にエラーが発生しました: {ex.Message}", ex);
                        }
                    });

                    if (processingDialog.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            string versionDir = PathManager.GetVersionFolder(game.GameId, form.NewVersion.Version);
                            string versionFolderName = form.NewVersion.Version.StartsWith("v") ? form.NewVersion.Version : "v" + form.NewVersion.Version;
                            string relativePath = Path.Combine(versionFolderName, form.RelativeExecutablePath);
                            form.NewVersion.ExecutablePath = relativePath;

                            _dbManager.AddGameVersion(form.NewVersion);

                            var activationResult = MessageBox.Show(
                                $"バージョン {form.NewVersion.Version} を現在のバージョン（アクティブ）として設定しますか？\n\n「いいえ」を選択した場合、バージョンは作成されますが、ランチャーで起動するバージョンは変更されません。",
                                "アクティブバージョンの確認",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                            if (activationResult == DialogResult.Yes)
                            {
                                form.UpdatedGameInfo.ExecutablePath = relativePath;
                                _dbManager.UpdateGame(form.UpdatedGameInfo);
                            }

                            MessageBox.Show(
                                $"ゲーム「{game.Title}」のバージョン {form.NewVersion.Version} を追加しました。",
                                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            LoadGames();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"データベースへの保存に失敗しました。\n\n{ex.Message}",
                                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    else if (processingDialog.DialogResult == DialogResult.Cancel)
                    {
                        MessageBox.Show("処理がキャンセルされました。", "キャンセル",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
        }

        private void btnDeleteGame_Click(object sender, EventArgs e)
        {
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show("削除するゲームを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedGame = dgvGames.SelectedRows[0].DataBoundItem as GameInfo;
            if (selectedGame == null) return;

            // GameId が空だと PathManager.GetGameFolder("") が GamesFolder 自体を返し、
            // Directory.Delete(folder, true) で全ゲームフォルダが消える致命バグになる。防御。
            if (string.IsNullOrWhiteSpace(selectedGame.GameId))
            {
                MessageBox.Show(this.FindForm(), "ゲームIDが不正です。削除できません。",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string gameFolder = PathManager.GetGameFolder(selectedGame.GameId);

            bool deleteFolder;
            using (var confirm = new DeleteGameConfirmForm(selectedGame.Title, selectedGame.GameId, gameFolder))
            {
                if (confirm.ShowDialog(this.FindForm()) != DialogResult.Yes) return;
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
                    catch (IOException ex) { renameError = ex; }
                    catch (UnauthorizedAccessException ex) { renameError = ex; }

                    using (var failDialog = new FolderDeletionFailureDialog(gameFolder, renameError))
                    {
                        var dr = failDialog.ShowDialog(this.FindForm());
                        if (dr != DialogResult.Retry)
                        {
                            MessageBox.Show(this.FindForm(),
                                "フォルダを退避できなかったため、ゲーム削除を中止しました。\n" +
                                "Launcher を閉じてから再度「削除」をお試しください。",
                                "中止", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                    }
                }
            }

            // フェーズ 2: DB 削除 (CASCADE で developers / game_versions / game_genres /
            //   play_records / surveys / store_section_games も削除される)
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
                dbDr = dialog.ShowDialog(this.FindForm());
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

                MessageBox.Show(this.FindForm(),
                    baseMsg + rollbackMsg,
                    "データベースエラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                LoadGames();
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
                        var dr = failDialog.ShowDialog(this.FindForm());
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
                MessageBox.Show(this.FindForm(),
                    "ゲームを削除しましたが、退避済みのフォルダの物理削除を諦めました。\n" +
                    "後で手動削除してください:\n  " + pendingDeleteFolder,
                    "ゲームフォルダ削除の警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this.FindForm(),
                    deleteFolder ? "ゲームと関連フォルダを削除しました。" : "ゲームを削除しました。",
                    "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            LoadGames();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            LoadGames();
        }

        private void dgvGames_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                btnEditGame_Click(sender, e);
            }
        }

    }
}
