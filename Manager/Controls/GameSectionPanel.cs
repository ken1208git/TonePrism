using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Controls
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
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ゲーム追加") == DialogResult.Cancel) return;
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
            // (round 2 High-2) selection 依存 validation を session conflict check より前に倒し、
            // 「行選択なし」で警告 dialog が出る UX 退行を物理閉鎖。
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

            // 全 validation 通過後、DB write 直前で session conflict check
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ゲーム編集") == DialogResult.Cancel) return;

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
            // (round 2 High-2) selection 依存 validation を session conflict check より前に倒す
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show("バージョンアップするゲームを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedGame = dgvGames.SelectedRows[0].DataBoundItem as GameInfo;
            if (selectedGame == null) return;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ゲームのバージョンアップ") == DialogResult.Cancel) return;

            var game = _dbManager.GetGameById(selectedGame.GameId);
            if (game == null)
            {
                MessageBox.Show("選択されたゲームが見つかりません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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

                    var processingDialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
                    {
                        try
                        {
                            Directory.CreateDirectory(versionDir);
                            // (#234 追加精査) 旧実装は IsVersionFolder で v* フォルダを除外していたが、
                            // (a) コピー先が games/{id}/v.../ でソース内側になる「ルート選択」誤操作は
                            // CopyDirectoryRecursive 冒頭の再帰ガードが既に空コピーで防ぐため除外は無力、
                            // (b) 正当な v* 名フォルダを無言で取りこぼす下しか無い保険だった。除外を撤去し、
                            // ルート選択自体は VersionUpForm.ValidateInput の games/ 配下ソース拒否で明示的に弾く。
                            FileOperationService.CopyDirectoryWithProgress(
                                form.SourceFolderPath, versionDir, progress, token);
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
                        // (#234) disk のコピー先フォルダ (PathManager.GetVersionFolder = versionDir) と
                        // 同じ正規化規則で leaf 名を作る。両者を別実装で計算すると "V1.0.0" のような生値で
                        // 食い違い、DB 保存パスが実フォルダを指さなくなるため GetVersionFolderLeaf に揃える。
                        string versionFolderName = PathManager.GetVersionFolderLeaf(form.NewVersion.Version);
                        string relativePath = Path.Combine(versionFolderName, form.RelativeExecutablePath);
                        form.NewVersion.ExecutablePath = relativePath;

                        // (#234) thumbnail / background も exe と同様に v{version}/ プレフィックスを付ける。
                        // VersionUpForm は source フォルダ基準の相対パス (プレフィックス無し、例: "thumb.png")
                        // で返すため、そのまま games / game_versions に保存すると Launcher がゲームルート基準
                        // (games/{id}/thumb.png) で解決して見つけられず、バージョンアップ後に画像が消える。
                        // exe (上の relativePath) と同じく version フォルダ名を前置してゲームルート基準に揃える。
                        // 両テーブル (NewVersion=game_versions / UpdatedGameInfo=activation 時の games) に反映。
                        if (!string.IsNullOrEmpty(form.NewVersion.ThumbnailPath))
                        {
                            string thumbRelative = Path.Combine(versionFolderName, form.NewVersion.ThumbnailPath);
                            form.NewVersion.ThumbnailPath = thumbRelative;
                            form.UpdatedGameInfo.ThumbnailPath = thumbRelative;
                        }
                        if (!string.IsNullOrEmpty(form.NewVersion.BackgroundPath))
                        {
                            string bgRelative = Path.Combine(versionFolderName, form.NewVersion.BackgroundPath);
                            form.NewVersion.BackgroundPath = bgRelative;
                            form.UpdatedGameInfo.BackgroundPath = bgRelative;
                        }

                        // (#234 ③) version 行の INSERT と activation (games 更新) を分離した try で扱う。
                        // INSERT 失敗時はコピー済 versionDir を rollback 削除し「保存に失敗」と通知。
                        // activation 失敗時は「版は作成済・アクティブ化のみ失敗」と正確に伝える (旧実装は
                        // どちらも「データベースへの保存に失敗しました」で、版が保存済なのに再実行させて
                        // ① の重複地獄に誘導していた)。
                        try
                        {
                            _dbManager.AddGameVersion(form.NewVersion);
                        }
                        catch (Exception ex)
                        {
                            try { if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true); }
                            catch (Exception delEx) { Logger.Warn("[GameSectionPanel] (#234 ③) versionDir rollback 削除失敗: " + versionDir + ": " + delEx.Message); }
                            MessageBox.Show(
                                $"バージョン情報のデータベース保存に失敗しました。\n\n{ex.Message}\n\nコピーしたファイルは削除しました。",
                                "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }

                        var activationResult = MessageBox.Show(
                            $"バージョン {form.NewVersion.Version} を現在のバージョン（アクティブ）として設定しますか？\n\n「いいえ」を選択した場合、バージョンは作成されますが、ランチャーで起動するバージョンは変更されません。",
                            "アクティブバージョンの確認",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (activationResult == DialogResult.Yes)
                        {
                            try
                            {
                                form.UpdatedGameInfo.ExecutablePath = relativePath;
                                _dbManager.UpdateGame(form.UpdatedGameInfo);
                            }
                            catch (Exception ex)
                            {
                                // 版 (game_versions) は保存済。games のアクティブ化のみ失敗。disk / version 行は
                                // 健全なので rollback せず、状態を正確に通知して return (再実行で ① に入らない)。
                                MessageBox.Show(
                                    $"バージョン {form.NewVersion.Version} は作成されましたが、アクティブ版への切り替えに失敗しました。\n\n{ex.Message}\n\n" +
                                    "Launcher で起動する版は変更されていません。ゲーム編集画面でアクティブ版を切り替えられます。",
                                    "アクティブ化失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                LoadGames();
                                return;
                            }
                        }

                        MessageBox.Show(
                            $"ゲーム「{game.Title}」のバージョン {form.NewVersion.Version} を追加しました。",
                            "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                        LoadGames();
                    }
                    else if (processingDialog.DialogResult == DialogResult.Cancel)
                    {
                        // (#234 ③) コピーが途中まで進んでいた可能性があるため versionDir を掃除する。
                        try { if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true); }
                        catch (Exception delEx) { Logger.Warn("[GameSectionPanel] (#234 ③) cancel 時の versionDir 削除失敗: " + versionDir + ": " + delEx.Message); }
                        MessageBox.Show("処理がキャンセルされました。", "キャンセル",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
        }

        private void btnDeleteGame_Click(object sender, EventArgs e)
        {
            // (round 2 High-2) selection 依存 validation を session conflict check より前に倒す
            if (dgvGames.SelectedRows.Count == 0)
            {
                MessageBox.Show("削除するゲームを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ゲーム削除") == DialogResult.Cancel) return;

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
