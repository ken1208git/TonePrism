using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GCTonePrism.Manager.Controls;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace GCTonePrism.Manager
{
    public partial class VersionUpForm : Form
    {
        private string gameId;
        private string currentVersion;
        private string selectedFolderPath;
        private List<DeveloperInfo> developers;
        private DeveloperListManager devListManager;
        private GameVersion baseVersion; // コピー元のバージョン情報
        private GameInfo originalGameInfo; // 元のゲーム情報

        /// <summary>
        /// 作成された新しいバージョン情報
        /// </summary>
        public GameVersion NewVersion { get; private set; }

        /// <summary>
        /// 選択されたゲームフォルダのパス（コピー元）
        /// </summary>
        public string SourceFolderPath { get; private set; }

        /// <summary>
        /// 選択された実行ファイルの相対パス（フォルダ内での相対パス）
        /// </summary>
        public string RelativeExecutablePath { get; private set; }

        /// <summary>
        /// 更新されたゲーム情報（タイトル、説明など）
        /// </summary>
        public GameInfo UpdatedGameInfo { get; private set; }

        /// <summary>
        /// 更新された製作者リスト
        /// </summary>
        public List<DeveloperInfo> UpdatedDevelopers => developers;

        public VersionUpForm(GameInfo gameInfo, string currentVersion, GameVersion baseVersion = null)
        {
            InitializeComponent();
            this.originalGameInfo = gameInfo;
            this.gameId = gameInfo.GameId;
            this.currentVersion = currentVersion;
            this.baseVersion = baseVersion;
            developers = new List<DeveloperInfo>();
            
            lblCurrentVersion.Text = currentVersion;
            NewVersion = null;

            // (#158 H-2) semverNext の初期化 + malformed 警告は VersionUpForm_Load に移動済。
            // ctor 中に MessageBox を出すと (a) Form 未 Show で owner=null → 別 window の裏に隠れる
            // (b) DPI / fonts が Load 前で再計算未完 → 表示崩れ (c) ctor 例外を caller が握り潰すと
            // silent skip、の 3 risk があるため Show 後の Load タイミングに統一。EditGameForm 側
            // (LoadGameDataForVersion) が SelectedIndexChanged 経由 = 既に Show 後で出している
            // のと一貫させる狙いも兼ねる。
        }

        private void VersionUpForm_Load(object sender, EventArgs e)
        {
            // (#158 H-2) ctor から移動: semverNext を currentVersion で初期化 + Patch を auto-bump
            // (= 「迷ったら Patch」default)。currentVersion が malformed (= DB に "1.0" / "alpha"
            // 等が残っていた場合) の silent v0.0.0 fallback で「気づかれずに 0.0.1 として書き戻される」
            // silent corruption を防ぐため TryParseAndSet で成否取得、失敗時は MessageBox 警告。
            // caller (= 呼び出し側 GameSectionPanel 等) がそもそも malformed をフィルタするのが理想
            // だが、本 form 単独でも防御線を張る。
            string semverParseErr;
            bool semverOk = semverNext.TryParseAndSet(currentVersion, out semverParseErr);
            // (#158 round 7 L-1) parse 失敗時は BumpPatch skip。旧実装は無条件 BumpPatch だったが、
            // 失敗時の clamp 値 (v0.0.0 や v99.0.0) を更に +1 すると user に表示される値が「clamp 結果
            // から派生した別物」になり、警告 MessageBox の「v0.0.0 / 上限値に clamp」表記と矛盾する
            // ("clamp って言ってるのに v0.0.1 が出てる" 混乱の元)。失敗時は clamp 値そのままで停止、
            // user に修正を促す。
            if (semverOk)
            {
                semverNext.BumpPatch();
            }
            else
            {
                // (#158 round 5 H-1 + round 7 L-1) 動的に表示値を挿入。BumpPatch を skip したため
                // 表示値は純粋な clamp 結果 (v0.0.0 / 上限値)、user に「これから OK で何が DB に書かれるか」
                // を率直に伝える。
                MessageBox.Show(this,
                    "現在の version 文字列が SemVer 形式ではありません。\n\n" +
                    "  値: '" + (currentVersion ?? "(null)") + "'\n" +
                    "  解析エラー: " + (semverParseErr ?? "(unknown)") + "\n\n" +
                    "現在の表示値: " + semverNext.VersionString + "\n" +
                    "  (parse 失敗のため数値部は v0.0.0 / 上限値に clamp、Patch+1 default は適用なし)\n\n" +
                    "意図した version に修正してから OK を押してください。",
                    "バージョン読み込み警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // コンボボックスを初期化（選択値はbaseVersion読み込み時に設定）
            GameFormHelper.InitializeDifficultyCombo(cmbDifficulty);
            GameFormHelper.InitializePlayTimeCombo(cmbPlayTime);
            GameFormHelper.InitializeConnectionCombo(cmbSupportedConnection);

            // ジャンルチェックボックスリストを初期化
            clbGenre.Items.Clear();
            foreach (var genre in GenreList.AvailableGenres)
            {
                clbGenre.Items.Add(genre, false);
            }

            if (baseVersion != null)
            {
                // 既存のバージョンがある場合はコピーする
                txtTitle.Text = baseVersion.Title;
                txtDescription.Text = baseVersion.Description; // ゲーム説明文をコピー
                txtArguments.Text = baseVersion.Arguments;
                txtUpdateNote.Text = ""; // 更新内容は空欄にする（ユーザー要望）
                
                // コンボボックス等の選択
                if (baseVersion.Difficulty.HasValue && baseVersion.Difficulty.Value >= 1 && baseVersion.Difficulty.Value <= 3)
                    cmbDifficulty.SelectedIndex = baseVersion.Difficulty.Value - 1;
                if (baseVersion.PlayTime.HasValue && baseVersion.PlayTime.Value >= 1 && baseVersion.PlayTime.Value <= 3)
                    cmbPlayTime.SelectedIndex = baseVersion.PlayTime.Value - 1;

                numMinPlayers.Value = baseVersion.MinPlayers ?? 1;
                numMaxPlayers.Value = baseVersion.MaxPlayers ?? 1;
                chkControllerSupport.Checked = baseVersion.ControllerSupport;

                if (baseVersion.SupportedConnection >= 0 && baseVersion.SupportedConnection < cmbSupportedConnection.Items.Count)
                    cmbSupportedConnection.SelectedIndex = baseVersion.SupportedConnection;

                // ジャンルのチェック
                GameFormHelper.SetSelectedGenres(clbGenre, baseVersion.Genre);

                // 製作者情報のコピー (Deep Copy)
                if (baseVersion.Developers != null)
                {
                    foreach (var dev in baseVersion.Developers)
                    {
                        developers.Add(new DeveloperInfo
                        {
                            // IDはコピーしない（新規作成のため）
                            GameId = dev.GameId,
                            LastName = dev.LastName,
                            FirstName = dev.FirstName,
                            Grade = dev.Grade
                        });
                    }
                }

                // パス情報は初期値としてセットしておく（変更なければそのまま使われる可能性があるが、通常は新しいフォルダになるので再設定推奨ではある）
                // ただし、VersionUpFormのロジック上、SourceFolderPath（コピー元）が設定されると、そこからの相対パスとして扱われる。
                // ここでは表示のみ行っておく。
                // 実際にはユーザーが「フォルダ選択」を行うことが前提のフローになっているため、
                // パスだけ入れてもコピー元フォルダが決まらないと意味がないかもしれない。
                // しかし「あらゆる要素」をコピペという要望なので入れておく。
                txtExecutablePath.Text = baseVersion.ExecutablePath;
                txtThumbnailPath.Text = baseVersion.ThumbnailPath;
                txtBackgroundPath.Text = baseVersion.BackgroundPath;
            }
            else
            {
                // デフォルト値
                cmbDifficulty.SelectedIndex = 1;
                cmbPlayTime.SelectedIndex = 1;
                cmbSupportedConnection.SelectedIndex = 0;
                chkControllerSupport.Checked = false;
                numMinPlayers.Value = 1;
                numMaxPlayers.Value = 1;
            }

            // 製作者情報のDataGridViewを初期化
            InitializeDevelopersGrid();
        }

        private void InitializeDevelopersGrid()
        {
            devListManager = new DeveloperListManager(dgvDevelopers, developers);
            devListManager.InitializeGrid();
        }

        private void btnSelectGameFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.Title = "新しいバージョンのゲームフォルダを選択してください";

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    selectedFolderPath = dialog.FileName;
                    txtGameFolder.Text = selectedFolderPath;
                    SourceFolderPath = selectedFolderPath;
                    
                    // フォルダ選択後、ファイルのパスをクリアして自動検出
                    txtExecutablePath.Text = "";
                    txtThumbnailPath.Text = "";
                    txtBackgroundPath.Text = "";
                    RelativeExecutablePath = "";
                    
                    AutoDetectFiles();
                }
            }
        }

        /// <summary>
        /// ファイルを自動検出
        /// </summary>
        private void AutoDetectFiles()
        {
            var (exePath, thumbPath, bgPath) = GameFormHelper.AutoDetectFiles(selectedFolderPath);
            if (exePath != null)
            {
                txtExecutablePath.Text = exePath;
                RelativeExecutablePath = PathConversionHelper.ToRelativePath(selectedFolderPath, exePath);
            }
            if (thumbPath != null) txtThumbnailPath.Text = thumbPath;
            if (bgPath != null) txtBackgroundPath.Text = bgPath;
        }

        private void btnSelectExecutable_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = selectedFolderPath;
                dialog.Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*";
                dialog.Title = "実行ファイルを選択してください";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    string selectedPath = dialog.FileName;
                    
                    if (!selectedPath.StartsWith(selectedFolderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "実行ファイルはゲームフォルダ内から選択してください。",
                            "入力エラー",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                    
                    txtExecutablePath.Text = selectedPath;
                    RelativeExecutablePath = selectedPath.Substring(selectedFolderPath.Length).TrimStart(Path.DirectorySeparatorChar);
                }
            }
        }

        private void btnSelectThumbnail_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = selectedFolderPath;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "サムネイル画像を選択";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtThumbnailPath.Text = dialog.FileName;
                    UpdateThumbnailPreview();
                }
            }
        }

        private void btnSelectBackground_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                MessageBox.Show("先にゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = selectedFolderPath;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "背景画像を選択";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtBackgroundPath.Text = dialog.FileName;
                    UpdateBackgroundPreview();
                }
            }
        }

        private void btnAddDeveloper_Click(object sender, EventArgs e) => devListManager.Add();
        private void btnEditDeveloper_Click(object sender, EventArgs e) => devListManager.Edit();
        private void btnDeleteDeveloper_Click(object sender, EventArgs e) => devListManager.Delete();

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (ValidateInput())
            {
                // バージョン情報を作成
                NewVersion = new GameVersion
                {
                    GameId = gameId,
                    Version = semverNext.VersionString,  // (#158)
                    ExecutablePath = "", // コピー後に設定
                    Arguments = txtArguments.Text.Trim(),
                    Description = txtDescription.Text.Trim(), // 説明文
                    UpdateNote = txtUpdateNote.Text.Trim(), // 更新内容
                    
                    Title = txtTitle.Text.Trim(),
                    Genre = GameFormHelper.GetSelectedGenres(clbGenre),
                    MinPlayers = (int)numMinPlayers.Value,
                    MaxPlayers = (int)numMaxPlayers.Value,
                    Difficulty = GetDifficultyValue(),
                    PlayTime = GetPlayTimeValue(),
                    ControllerSupport = chkControllerSupport.Checked,
                    SupportedConnection = cmbSupportedConnection.SelectedIndex,
                    
                    // サムネイルと背景はコピー後に相対パスになる可能性があるが、
                    // ここではとりあえずフォームの値をセットし、MainFormで処理するか、
                    // 相対パス計算ロジックをここに入れるか。
                    // 既存ロジック(UpdatedGameInfo)ではMainFormではなくForm内で計算していた(lines 394+)
                    // NewVersionにも同じロジックを適用する必要がある。
                    // ここでは空文字または絶対パスを入れておき、後続の処理で書き換える。
                    ThumbnailPath = txtThumbnailPath.Text,
                    BackgroundPath = txtBackgroundPath.Text,
                    
                    Developers = new List<DeveloperInfo>(developers), // リストをコピー
                    
                    RegisteredAt = DateTime.Now
                };

                // 更新されたゲーム情報を作成
                // 更新されたゲーム情報を作成（既存の情報をベースにする）
                UpdatedGameInfo = new GameInfo
                {
                    GameId = originalGameInfo.GameId,
                    Title = txtTitle.Text.Trim(),
                    Description = txtDescription.Text.Trim(),
                    Genre = GameFormHelper.GetSelectedGenres(clbGenre),
                    
                    // フォームにない項目は既存の値を引き継ぐ
                    ReleaseYear = originalGameInfo.ReleaseYear,
                    DisplayOrder = originalGameInfo.DisplayOrder,
                    IsVisible = originalGameInfo.IsVisible, // 表示設定は維持
                    
                    MinPlayers = (int)numMinPlayers.Value,
                    MaxPlayers = (int)numMaxPlayers.Value,
                    Difficulty = GetDifficultyValue(),
                    PlayTime = GetPlayTimeValue(),
                    ControllerSupport = chkControllerSupport.Checked,
                    SupportedConnection = cmbSupportedConnection.SelectedIndex,
                    Developers = new List<DeveloperInfo>(developers), // 追加
                    
                    // Controls, KeyMappingなども引き継ぐ
                    Controls = originalGameInfo.Controls,
                    KeyMapping = originalGameInfo.KeyMapping,
                    
                    Version = NewVersion.Version // 新しいバージョンをアクティブなバージョンとして設定
                };

                // サムネイルと背景のパスを計算（コピー後に相対パスになる）
                if (!string.IsNullOrEmpty(txtThumbnailPath.Text) && File.Exists(txtThumbnailPath.Text))
                {
                    string relativePath = PathConversionHelper.ToRelativePath(selectedFolderPath, txtThumbnailPath.Text);
                    UpdatedGameInfo.ThumbnailPath = relativePath;
                    NewVersion.ThumbnailPath = relativePath;
                }
                if (!string.IsNullOrEmpty(txtBackgroundPath.Text) && File.Exists(txtBackgroundPath.Text))
                {
                    string relativePath = PathConversionHelper.ToRelativePath(selectedFolderPath, txtBackgroundPath.Text);
                    UpdatedGameInfo.BackgroundPath = relativePath;
                    NewVersion.BackgroundPath = relativePath;
                }
                
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private int GetDifficultyValue()
        {
            return cmbDifficulty.SelectedIndex >= 0 ? cmbDifficulty.SelectedIndex + 1 : 2;
        }

        private int GetPlayTimeValue()
        {
            return cmbPlayTime.SelectedIndex >= 0 ? cmbPlayTime.SelectedIndex + 1 : 2;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }


        private bool ValidateInput()
        {
            // (#158) SemverInputControl は NumericUpDown 構造的に空にできないので「空文字」check は不要、
            // suffix の文字種だけ IsValid で確認する。
            string semverError;
            if (!semverNext.IsValid(out semverError))
            {
                MessageBox.Show(semverError, "バージョン入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                semverNext.Focus();
                return false;
            }

            // (#158 M-1) currentVersion は DB 由来で過去の "1.0.0" / "V1.0.0" 等のゆれを含みうる。
            // semverNext.VersionString は常に "v<X>.<Y>.<Z>[-suffix]" の正規化形なので、生比較すると
            // 同義値 ("v1.0.0" vs "1.0.0") をすり抜けて Launcher 側で 2 つの version が並ぶ silent
            // danger になる。両辺を SemverInputControl.TryNormalize で正規化してから比較する。
            // currentVersion 自体が malformed (= TryNormalize が false) のケースは ctor / Form_Load で
            // 既に MessageBox 警告済 + v0.0.0 fallback 入力済なので、ここでは「正規化できなければ dup
            // 判定対象外 = 続行」とする (= H-2 警告で user は修正済の前提)。
            // (#158 round 8 senior Low #6) 両辺は TryNormalize 経由で lowercase v 強制 + 数値部正規化済
            // なので Ordinal `==` でも機能等価だが、本 PR の他経路 (EditGameForm rename / dup-check 等)
            // は OrdinalIgnoreCase 統一なので規約整合のため string.Equals(... OrdinalIgnoreCase) に揃える。
            string currentNormalized;
            if (SemverInputControl.TryNormalize(currentVersion, out currentNormalized)
                && string.Equals(semverNext.VersionString, currentNormalized, StringComparison.OrdinalIgnoreCase))
            {
                // (#158 H3) bump button は round 3 で削除済 (#133 ガイドライン doc に移管予定)、
                // その案内を撤去。NumericUpDown を直接操作する旨だけ案内。
                MessageBox.Show("現在のバージョンと同じバージョンは指定できません。\n\n" +
                    "Major / Minor / Patch のいずれかの数値を直接編集してください (= ▲ で +1、▼ で -1)。",
                    "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                semverNext.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtGameFolder.Text))
            {
                MessageBox.Show("ゲームフォルダを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            if (!Directory.Exists(txtGameFolder.Text))
            {
                MessageBox.Show("選択されたゲームフォルダが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTitle.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(txtExecutablePath.Text))
            {
                MessageBox.Show("実行ファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            if (!File.Exists(txtExecutablePath.Text))
            {
                MessageBox.Show("選択された実行ファイルが見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectExecutable.Focus();
                return false;
            }

            return true;
        }

        private void UpdateThumbnailPreview() => ImagePreviewHelper.UpdatePreview(picThumbnailPreview, txtThumbnailPath.Text);
        private void UpdateBackgroundPreview() => ImagePreviewHelper.UpdatePreview(picBackgroundPreview, txtBackgroundPath.Text);

        private void btnTestRun_Click(object sender, EventArgs e) =>
            GameFormHelper.TestRunGame(txtExecutablePath.Text.Trim(), txtArguments.Text);
    }
}
