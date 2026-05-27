using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace TonePrism.Manager
{
    /// <summary>
    /// ゲーム追加フォーム
    /// </summary>
    public partial class AddGameForm : Form
    {
        private DatabaseManager dbManager;
        private string sourceGameFolder;
        private string destinationGameFolder;
        private List<DeveloperInfo> developers;
        private DeveloperListManager devListManager;
        private Label lblArgumentsPlaceholder;

        /// <summary>
        /// 追加されたゲーム情報（OKボタンがクリックされた場合のみ設定される）
        /// </summary>
        public GameInfo AddedGame { get; private set; }

        public AddGameForm(DatabaseManager dbManager)
        {
            InitializeComponent();
            this.dbManager = dbManager;
            AddedGame = null;
            sourceGameFolder = null;
            destinationGameFolder = null;
            developers = new List<DeveloperInfo>();
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void AddGameForm_Load(object sender, EventArgs e)
        {
            // コンボボックスを初期化
            GameFormHelper.InitializeDifficultyCombo(cmbDifficulty);
            GameFormHelper.InitializePlayTimeCombo(cmbPlayTime);
            GameFormHelper.InitializeConnectionCombo(cmbSupportedConnection);

            // 起動オプションのプレースホルダー設定
            lblArgumentsPlaceholder = GameFormHelper.SetupArgumentsPlaceholder(txtArguments, this);

            // デフォルト値の設定
            chkControllerSupport.Checked = false;
            numMinPlayers.Value = 1;
            numMaxPlayers.Value = 1;

            
            // ジャンルチェックボックスリストを初期化
            clbGenre.Items.Clear();
            foreach (var genre in GenreList.AvailableGenres)
            {
                clbGenre.Items.Add(genre, false);
            }

            btnOK.Enabled = true;

            // リリース年の初期値を今年に設定
            numReleaseYear.Value = DateTime.Now.Year;

            // (#158 L2) バージョンの初期値は AddGameForm.Designer.cs の `semverInput.VersionString = "v1.0.0";`
            // 設定が SoT。Load では再代入しない (= 二重初期化のノイズ排除)。
            // (#158 round 4 L-5 + round 5 L-2) 構造的には:
            //   - SemverInputControl.Designer の `numMajor.Value = 1` (Minor/Patch は明示なしで
            //     NumericUpDown.Value class default = 0)
            //   - AddGameForm.Designer の `semverInput.VersionString = "v1.0.0"` (上書き)
            // の二段で v1.0.0 が確定する (Major は二段保険、Minor/Patch は class default に一段依存)。
            // SoT は AddGameForm.Designer 側であり、SemverInputControl.Designer の Major=1 / Minor/Patch
            // class default のいずれにも依存しない設計を維持すること。

            // 製作者情報のDataGridViewを初期化
            InitializeDevelopersGrid();
        }

        /// <summary>
        /// 製作者情報のDataGridViewを初期化
        /// </summary>
        private void InitializeDevelopersGrid()
        {
            devListManager = new DeveloperListManager(dgvDevelopers, developers);
            devListManager.InitializeGrid();
        }

        /// <summary>
        /// ファイルを自動検出
        /// </summary>
        private void AutoDetectFiles()
        {
            var (exePath, thumbPath, bgPath) = GameFormHelper.AutoDetectFiles(sourceGameFolder);
            if (exePath != null) txtExecutablePath.Text = exePath;
            if (thumbPath != null)
            {
                txtThumbnailPath.Text = thumbPath;
                UpdateThumbnailPreview();
            }
            if (bgPath != null)
            {
                txtBackgroundPath.Text = bgPath;
                UpdateBackgroundPreview();
            }
        }


        /// <summary>
        /// ゲームフォルダ選択ボタンクリック
        /// </summary>
        private void btnSelectGameFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.Title = "ゲームフォルダを選択してください";
                dialog.Multiselect = false;

                // 現在のパスが設定されている場合は、そこから開始
                if (!string.IsNullOrEmpty(txtGameFolder.Text) && Directory.Exists(txtGameFolder.Text))
                {
                    dialog.InitialDirectory = txtGameFolder.Text;
                }

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    sourceGameFolder = dialog.FileName;
                    txtGameFolder.Text = sourceGameFolder;
                    
                    // 自動検出を実行
                    AutoDetectFiles();
                }
            }
        }

        /// <summary>
        /// 実行ファイル選択ボタンクリック（コピー先フォルダから選択）
        /// </summary>
        private void btnSelectExecutable_Click(object sender, EventArgs e)
        {
            string searchFolder = sourceGameFolder;
            if (string.IsNullOrEmpty(searchFolder))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = searchFolder;
                dialog.Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*";
                dialog.Title = "実行ファイルを選択（コピー元フォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtExecutablePath.Text = dialog.FileName;
                }
            }
        }

        /// <summary>
        /// サムネイル画像選択ボタンクリック（コピー先フォルダから選択）
        /// </summary>
        private void btnSelectThumbnail_Click(object sender, EventArgs e)
        {
            string searchFolder = sourceGameFolder;
            if (string.IsNullOrEmpty(searchFolder))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = searchFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "サムネイル画像を選択（コピー元フォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtThumbnailPath.Text = dialog.FileName;
                    UpdateThumbnailPreview();
                }
            }
        }

        /// <summary>
        /// 背景画像選択ボタンクリック（コピー先フォルダから選択）
        /// </summary>
        private void btnSelectBackground_Click(object sender, EventArgs e)
        {
            string searchFolder = sourceGameFolder;
            if (string.IsNullOrEmpty(searchFolder))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = searchFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "背景画像を選択（コピー元フォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtBackgroundPath.Text = dialog.FileName;
                    UpdateBackgroundPreview();
                }
            }
        }

        /// <summary>
        /// ゲームフォルダをコピー（バージョンフォルダ構造）
        /// </summary>
        private void CopyGameFolder(string gameId, string version, IProgress<ProgressInfo> progress, System.Threading.CancellationToken token)
        {
            // ゲームベースフォルダを作成
            string gameBaseFolder = PathManager.GetGameFolder(gameId);
            if (!Directory.Exists(gameBaseFolder))
            {
                Directory.CreateDirectory(gameBaseFolder);
            }
            
            // バージョンフォルダにコピー
            destinationGameFolder = PathManager.GetVersionFolder(gameId, version);

            // 既に存在する場合はエラー
            if (Directory.Exists(destinationGameFolder))
            {
                throw new Exception($"バージョンフォルダ '{destinationGameFolder}' は既に存在します。");
            }

            // バージョンフォルダを作成
            Directory.CreateDirectory(destinationGameFolder);
            
            // FileOperationServiceに委譲してコピー
            FileOperationService.CopyDirectoryWithProgress(sourceGameFolder, destinationGameFolder, progress, token);
        }


        // パス変換はPathConversionHelperに委譲

        /// <summary>
        /// OKボタンクリック
        /// </summary>
        private void btnOK_Click(object sender, EventArgs e)
        {
            // バリデーション
            if (!ValidateInput())
            {
                return;
            }

            string gameId = txtGameId.Text.Trim();

            // gameId が既存フォルダと衝突している場合の警告 (#120)
            // 何らかの原因で games/{gameId}/ が残っていると、同 gameId 追加で:
            //  - 同バージョン追加時にエラー
            //  - 別バージョン追加時に古いファイルがゴミとして残る
            //  - 最悪、Launcher が古い実行ファイルを起動する silent failure
            // 自動削除はせず手動退避を促す方針 (失いたくないデータ保護のため)
            string existingFolder = PathManager.GetGameFolder(gameId);
            if (Directory.Exists(existingFolder))
            {
                var dr = MessageBox.Show(this,
                    "古いゲームデータが残っています。\n\n" +
                    $"ゲームID '{gameId}' のフォルダが既に存在します:\n  {existingFolder}\n\n" +
                    "このまま続行すると、以下の挙動になります:\n" +
                    "  ・同じバージョンを追加しようとした場合 → エラーになります\n" +
                    "  ・別のバージョンを追加した場合 → 古いファイルがフォルダに残ります\n" +
                    "  ・最悪、Launcher が古い実行ファイルを起動する可能性があります\n\n" +
                    "失いたくないデータがある場合は、いったん上記のフォルダの中身を別の場所に\n" +
                    "退避してから「キャンセル」を押してフォルダを手動で削除し、\n" +
                    "再度ゲーム追加を試みてください。\n\n" +
                    "退避不要であれば「OK」を押してください。\n" +
                    "※ 古いフォルダの中身は自動削除されません。",
                    "古いゲームデータの確認",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (dr != DialogResult.OK) return;
            }

            // (#234 ②) AddGame と AddGameVersion は別トランザクション。AddGameVersion が失敗すると
            // commit 済の games 行だけが残り、版なし・フォルダなしの孤児ゲームが Launcher に出て起動
            // 不能になる。失敗時 catch で games 行も rollback するため、AddGame 成否を追跡する。
            bool gameAdded = false;

            try
            {
                // 初期バージョン番号 (#158: SemverInputControl で typo / フォーマットゆれを構造的に排除)
                // suffix 文字種チェックは ValidateInput に移動済 (L-3)。
                string version = semverInput.VersionString;

                // (#187) 2 段目 fence は CopyGameFolder の前 (= pre-copy)。Cancel 時に file copy が走らず
                // **この path に限り** rollback 不要 (= ProcessingDialog Cancel / SQLite catch / general
                // Exception catch の 3 rollback path は依然維持、SPEC §3.8.5 「parent gameFolder retention
                // の非対称性」参照)。trade-off の rationale は SPEC §3.8.2 / §3.8.5 参照。
                if (SessionConflictHelper.CheckBeforeWrite(this, "ゲーム追加") == DialogResult.Cancel)
                {
                    return;
                }

                // ProcessingDialog を使用して非同期コピー
                var processingDialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
                {
                    try
                    {
                        CopyGameFolder(gameId, version, progress, token);
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

                if (processingDialog.ShowDialog() != DialogResult.OK)
                {
                    // キャンセルまたはエラー時
                    if (processingDialog.DialogResult == DialogResult.Cancel)
                    {
                        MessageBox.Show("処理がキャンセルされました。", "キャンセル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    
                    // コピー失敗時のロールバック（フォルダ削除）
                    if (!string.IsNullOrEmpty(destinationGameFolder) && Directory.Exists(destinationGameFolder))
                    {
                        try { Directory.Delete(destinationGameFolder, true); } catch { }
                    }
                    return;
                }

                destinationGameFolder = PathManager.GetVersionFolder(gameId, version);

                // コピー元のパスをコピー先の絶対パスに変換
                string executableAbsolutePath = PathConversionHelper.ConvertSourceToDestination(txtExecutablePath.Text.Trim(), sourceGameFolder, destinationGameFolder);
                string thumbnailAbsolutePath = string.IsNullOrWhiteSpace(txtThumbnailPath.Text) ? null : PathConversionHelper.ConvertSourceToDestination(txtThumbnailPath.Text.Trim(), sourceGameFolder, destinationGameFolder);
                string backgroundAbsolutePath = string.IsNullOrWhiteSpace(txtBackgroundPath.Text) ? null : PathConversionHelper.ConvertSourceToDestination(txtBackgroundPath.Text.Trim(), sourceGameFolder, destinationGameFolder);

                // デバッグログ（開発時のみ）
                Logger.Info($"[AddGameForm] コピー先フォルダ: {destinationGameFolder}");
                Logger.Info($"[AddGameForm] 実行ファイル絶対パス: {executableAbsolutePath}");
                Logger.Info($"[AddGameForm] サムネイル絶対パス: {thumbnailAbsolutePath}");
                Logger.Info($"[AddGameForm] 背景絶対パス: {backgroundAbsolutePath}");

                // (#234) 相対化の基準は version フォルダではなく **ゲームルート (games/{game_id}/)**。
                // Launcher は games / game_versions のパスをゲームルート基準でしか解決しない
                // (GamePathResolver.find_executable / resolve_path、Manager の EditGameForm も
                // ToAbsolutePath(gameFolder, ...) で解決) ため、ファイル実体が games/{id}/v{version}/
                // 配下にある以上、保存値は v{version}/ プレフィックスを含む必要がある。version フォルダ
                // 基準で相対化すると "main.exe" のようにプレフィックスが落ち、新規追加ゲームが起動不能
                // + サムネ/背景が表示されない silent corruption になっていた (version-up の exe は #234 で
                // GameSectionPanel が同方針で修正済、本修正で追加フローと thumb/bg を揃える)。
                string gameRelativeBase = PathManager.GetGameFolder(gameId);
                string executablePath = PathConversionHelper.ToRelativePathAfterCopy(executableAbsolutePath, gameRelativeBase);
                string thumbnailPath = PathConversionHelper.ToRelativePathAfterCopy(thumbnailAbsolutePath, gameRelativeBase);
                string backgroundPath = PathConversionHelper.ToRelativePathAfterCopy(backgroundAbsolutePath, gameRelativeBase);

                // デバッグログ（開発時のみ）
                Logger.Info($"[AddGameForm] 実行ファイル相対パス: {executablePath}");
                Logger.Info($"[AddGameForm] サムネイル相対パス: {thumbnailPath}");
                Logger.Info($"[AddGameForm] 背景相対パス: {backgroundPath}");

                // 起動オプション（空白は null 正規化 — 3 フォームで DB 表現を統一、#224 review #2）
                string arguments = string.IsNullOrWhiteSpace(txtArguments.Text) ? null : txtArguments.Text.Trim();

                // GameInfoオブジェクトを作成
                var game = new GameInfo
                {
                    GameId = gameId,
                    Title = txtTitle.Text.Trim(),
                    Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim(),
                    ReleaseYear = numReleaseYear.Value > 0 ? (int?)numReleaseYear.Value : null,
                    MinPlayers = numMinPlayers.Value > 0 ? (int?)numMinPlayers.Value : null,
                    MaxPlayers = numMaxPlayers.Value > 0 ? (int?)numMaxPlayers.Value : null,
                    Difficulty = cmbDifficulty.SelectedIndex >= 0 ? cmbDifficulty.SelectedIndex + 1 : (int?)null,
                    PlayTime = cmbPlayTime.SelectedIndex >= 0 ? cmbPlayTime.SelectedIndex + 1 : (int?)null,
                    SupportedConnection = cmbSupportedConnection.SelectedIndex >= 0 ? cmbSupportedConnection.SelectedIndex : 0,
                    ControllerSupport = chkControllerSupport.Checked,
                    ThumbnailPath = thumbnailPath,
                    BackgroundPath = backgroundPath,
                    ExecutablePath = executablePath,
                    Arguments = arguments,
                    DisplayOrder = dbManager.GetMinDisplayOrder() - 1, // 既存の最小値より1小さい値（一番上に配置）
                    IsVisible = true, // 新規追加のゲームは常にランチャーに表示
                    Controls = null, // 後で実装
                    KeyMapping = null, // 後で実装
                    Version = version // (#158, round 3 L-2: line 282 でキャプチャ済の local を使い回し)
                };

                // ジャンルを処理
                game.Genre = GameFormHelper.GetSelectedGenres(clbGenre);

                // 製作者情報を設定
                game.Developers = developers;

                // データベースに追加
                dbManager.AddGame(game);
                gameAdded = true;

                // 初期バージョン情報を追加
                // (#224 バグ②) 旧実装は Description に "初期バージョン" を入れていたが、編集画面が
                // version.Description を読むため、編集→保存で games の本物の説明が "初期バージョン" で
                // 上書き消去されていた。初期版は games と同じ本物の説明 / 起動オプションを持たせ、
                // "初期バージョン" は更新内容 (UpdateNote) に移す。
                // (#234 補完) #224 は Description / Arguments のみ初期版にミラーしていたが、Title / Genre /
                // 難易度 / プレイ時間 / コントローラ対応 / 通信対応 / サムネ / 背景 / 製作者 が未設定のままで、
                // 編集画面でアクティブ版 (=初期版) を読むと UI が空 / 既定値で上書きされ、保存で games 側も
                // 巻き添えに消える #224 と同種のデータ損失が残っていた。初期版は games の完全ミラーにする。
                var initialVersion = new GameVersion
                {
                    GameId = game.GameId,
                    Version = version, // (#158, round 3 L-2: 同上)
                    ExecutablePath = game.ExecutablePath,
                    Description = game.Description,
                    Arguments = game.Arguments,
                    UpdateNote = "初期バージョン",
                    Title = game.Title,
                    Genre = game.Genre,
                    MinPlayers = game.MinPlayers,
                    MaxPlayers = game.MaxPlayers,
                    Difficulty = game.Difficulty,
                    PlayTime = game.PlayTime,
                    ControllerSupport = game.ControllerSupport,
                    SupportedConnection = game.SupportedConnection,
                    ThumbnailPath = game.ThumbnailPath,
                    BackgroundPath = game.BackgroundPath,
                    // 製作者は version_id 付き行として別 INSERT されるためディープコピー (Id はコピーしない)。
                    Developers = developers.Select(d => new DeveloperInfo
                    {
                        GameId = d.GameId,
                        LastName = d.LastName,
                        FirstName = d.FirstName,
                        Grade = d.Grade
                    }).ToList(),
                    RegisteredAt = DateTime.Now
                };
                dbManager.AddGameVersion(initialVersion);

                AddedGame = game;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                // エラーが発生した場合、コピーしたフォルダを削除（ロールバック）
                if (!string.IsNullOrEmpty(destinationGameFolder) && Directory.Exists(destinationGameFolder))
                {
                    try
                    {
                        Directory.Delete(destinationGameFolder, true);
                    }
                    catch
                    {
                        // 削除に失敗しても続行
                    }
                }

                // (#234 ②) games 行が commit 済なら版なし孤児を残さないよう削除 (CASCADE で developers 等も除去)。
                RollbackGameRow(gameId, gameAdded);

                string errorMessage = DatabaseManager.GetUserFriendlyErrorMessage(ex);
                MessageBox.Show(
                    $"ゲームの追加に失敗しました。\n\n{errorMessage}",
                    "データベースエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                // エラーが発生した場合、コピーしたフォルダを削除（ロールバック）
                if (!string.IsNullOrEmpty(destinationGameFolder) && Directory.Exists(destinationGameFolder))
                {
                    try
                    {
                        Directory.Delete(destinationGameFolder, true);
                    }
                    catch
                    {
                        // 削除に失敗しても続行
                    }
                }

                // (#234 ②) games 行が commit 済なら版なし孤児を残さないよう削除 (CASCADE で developers 等も除去)。
                RollbackGameRow(gameId, gameAdded);

                MessageBox.Show(
                    $"ゲームの追加に失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// (#234 ②) AddGame は成功したが後続 (AddGameVersion 等) が失敗した場合に、commit 済の
        /// games 行を削除して「版なし孤児ゲーム」を残さないための rollback。DeleteGame は FK CASCADE で
        /// developers / game_versions 等も巻き取る。削除自体の失敗は握り潰す (元の例外通知を優先)。
        /// </summary>
        private void RollbackGameRow(string gameId, bool gameAdded)
        {
            if (!gameAdded) return;
            try
            {
                dbManager.DeleteGame(gameId);
            }
            catch (Exception delEx)
            {
                Logger.Warn("[AddGameForm] (#234 ②) games 行 rollback 削除失敗: " + gameId + ": " + delEx.Message);
            }
        }

        /// <summary>
        /// キャンセルボタンクリック
        /// </summary>
        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// 入力値のバリデーション
        /// </summary>
        private bool ValidateInput()
        {
            // ゲームID
            if (string.IsNullOrWhiteSpace(txtGameId.Text))
            {
                MessageBox.Show("ゲームIDを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtGameId.Focus();
                return false;
            }

            // ゲームIDの文字種チェック（英数字と一部の記号のみ許可）
            if (!GameFormHelper.IsValidGameId(txtGameId.Text))
            {
                MessageBox.Show("ゲームIDは英数字、アンダースコア（_）、ハイフン（-）のみ使用できます。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtGameId.Focus();
                return false;
            }

            // タイトル
            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTitle.Focus();
                return false;
            }

            // ゲームフォルダ（テキストボックスから取得）
            string gameFolderPath = txtGameFolder.Text.Trim();
            if (string.IsNullOrEmpty(gameFolderPath))
            {
                MessageBox.Show("ゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            if (!Directory.Exists(gameFolderPath))
            {
                MessageBox.Show("選択されたゲームフォルダが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            // sourceGameFolderを更新
            sourceGameFolder = gameFolderPath;

            // 実行ファイルパス
            if (string.IsNullOrWhiteSpace(txtExecutablePath.Text))
            {
                MessageBox.Show("実行ファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            // 実行ファイルが選択されたフォルダ内にあるか確認（ゲームフォルダが選択されている場合のみ）
            if (!string.IsNullOrEmpty(sourceGameFolder) && 
                !txtExecutablePath.Text.StartsWith(sourceGameFolder, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("実行ファイルは選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            if (!File.Exists(txtExecutablePath.Text))
            {
                MessageBox.Show("選択された実行ファイルが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            // サムネイル画像パスのチェック（指定されている場合）
            if (!string.IsNullOrWhiteSpace(txtThumbnailPath.Text))
            {
                if (!File.Exists(txtThumbnailPath.Text))
                {
                    MessageBox.Show("選択されたサムネイル画像が見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectThumbnail.Focus();
                    return false;
                }
                if (!string.IsNullOrEmpty(sourceGameFolder) && 
                    !txtThumbnailPath.Text.StartsWith(sourceGameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("サムネイル画像は選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectThumbnail.Focus();
                    return false;
                }
            }

            // 背景画像パスのチェック（指定されている場合）
            if (!string.IsNullOrWhiteSpace(txtBackgroundPath.Text))
            {
                if (!File.Exists(txtBackgroundPath.Text))
                {
                    MessageBox.Show("選択された背景画像が見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
                if (!string.IsNullOrEmpty(sourceGameFolder) && 
                    !txtBackgroundPath.Text.StartsWith(sourceGameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("背景画像は選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
            }

            // ゲームIDの重複チェック
            var existingGame = dbManager.GetGameById(txtGameId.Text.Trim());
            if (existingGame != null)
            {
                MessageBox.Show("このゲームIDは既に使用されています。別のIDを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtGameId.Focus();
                return false;
            }

            // (#158 L-3) suffix の文字種チェックは旧実装で「古いデータ確認」MessageBox の後に
            // 置かれていたため、不正 suffix 入力時にユーザーが先に長文の確認 MessageBox を読まされ
            // てから「やっぱバージョン入力エラー」に戻される UX だった。本 ValidateInput の末尾に
            // 統合して既存 validation と同じタイミングで弾く。
            string semverError;
            if (!semverInput.IsValid(out semverError))
            {
                MessageBox.Show(semverError, "バージョン入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                semverInput.Focus();
                return false;
            }

            return true;
        }

        private void btnAddDeveloper_Click(object sender, EventArgs e) => devListManager.Add();
        private void btnEditDeveloper_Click(object sender, EventArgs e) => devListManager.Edit();
        private void btnDeleteDeveloper_Click(object sender, EventArgs e) => devListManager.Delete();

        private void UpdateThumbnailPreview() => ImagePreviewHelper.UpdatePreview(picThumbnailPreview, txtThumbnailPath.Text);
        private void UpdateBackgroundPreview() => ImagePreviewHelper.UpdatePreview(picBackgroundPreview, txtBackgroundPath.Text);

        private void btnTestRun_Click(object sender, EventArgs e) =>
            GameFormHelper.TestRunGame(txtExecutablePath.Text.Trim(), txtArguments.Text);
    }
}

