using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TonePrism.Manager.Controls;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace TonePrism.Manager
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

        // (#234 ①) 既存全バージョンの version 文字列。ValidateInput で「最新版だけでなく全版」と
        // 重複比較するために caller (GameSectionPanel) から受け取る。null の場合は currentVersion
        // 単体との比較に fallback (旧挙動)。
        private readonly List<string> existingVersionStrings;

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

        public VersionUpForm(GameInfo gameInfo, string currentVersion, GameVersion baseVersion = null, IEnumerable<string> existingVersions = null)
        {
            InitializeComponent();
            this.originalGameInfo = gameInfo;
            this.gameId = gameInfo.GameId;
            this.currentVersion = currentVersion;
            this.baseVersion = baseVersion;
            this.existingVersionStrings = existingVersions?.ToList();
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
                    // (#234) 実行ファイル選択と同様、サムネはコピー元フォルダ内のものに限る。フォルダ外の
                    // ファイルだと相対化できず絶対パスのまま保存され、コピーされないファイルを指す壊れた
                    // パスになる (GameSectionPanel が v{version}/ を前置しても絶対パスは Path.Combine で
                    // そのまま残る)。AddGameForm と挙動を揃える。
                    if (!dialog.FileName.StartsWith(selectedFolderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "サムネイル画像はゲームフォルダ内から選択してください。",
                            "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
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
                    // (#234) 背景もコピー元フォルダ内に限る (サムネと同理由)。
                    if (!dialog.FileName.StartsWith(selectedFolderPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(
                            "背景画像はゲームフォルダ内から選択してください。",
                            "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
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
                // (#179 round 6 M-1 案 B) DB write 直前で他 PC session を再 check (race fence)。
                // SectionPanel 側 (`ShowDialog` 直前) で既に 1 回 check 済だが、user が編集画面を 5-10 分
                // 開きっぱなしにする間に他 PC が編集を始めると衝突しうるため二段 fence。Cancel 選択時は
                // **編集画面に戻る** (= `DialogResult.OK` を設定せず Form を閉じない、入力内容を保持)。
                // VersionUpForm 自体は DB write を持たず、Close 後に caller (GameSectionPanel) が
                // ファイルコピー (ProcessingDialog) + `AddGameVersion` を実行する。check はここで
                // pre-copy なので copy 中の race は依然残る (受容仕様、SPEC §3.8.5)。
                if (SessionConflictHelper.CheckBeforeWrite(this, "バージョン追加") == DialogResult.Cancel)
                {
                    return;
                }

                // バージョン情報を作成
                NewVersion = new GameVersion
                {
                    GameId = gameId,
                    Version = semverNext.VersionString,  // (#158)
                    ExecutablePath = "", // コピー後に設定
                    Arguments = string.IsNullOrWhiteSpace(txtArguments.Text) ? null : txtArguments.Text.Trim(),
                    // (#234) 空白は null 正規化。AddGameForm / EditGameForm と DB 表現を統一する
                    // (#224 で「3 フォームで統一」と決めたが VersionUpForm の Description/UpdateNote が
                    // 取り残されており、空欄入力時に "" が保存され他フォームの null と不一致だった)。
                    Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim(), // 説明文
                    UpdateNote = string.IsNullOrWhiteSpace(txtUpdateNote.Text) ? null : txtUpdateNote.Text.Trim(), // 更新内容
                    
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
                    // (#234) games 側も版と同じく空白を null 正規化 (上の NewVersion と揃える)。
                    Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim(),
                    // (#224) 旧実装は UpdatedGameInfo に Arguments を設定しておらず、バージョンUp 時に
                    // games.arguments が新版の値に更新されなかった (Launcher は games を読むため旧引数の
                    // まま起動)。NewVersion (329) と同じ値を games 側にも反映する。
                    Arguments = string.IsNullOrWhiteSpace(txtArguments.Text) ? null : txtArguments.Text.Trim(),
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

            // (#234 ①) 旧実装は currentVersion (=最新版) としか重複比較しておらず、数値欄を直接
            // 編集して非最新版 (例: 過去の 1.5.0) と同じ番号を入力すると validation を通過していた。
            // その後 GameSectionPanel が既存 version folder へ上書きマージコピー + game_versions へ
            // 重複行 INSERT (UNIQUE 制約なし) し、Launcher 側で「どちらの版か」決定不能になる silent
            // corruption が起きた。EditGameForm の #158 Q2 dup-check と同様、既存「全版」と比較する。
            //
            // 比較は SemverInputControl.TryNormalize で正規化後に行う (= 同義値 "v1.0.0"/"1.0.0"/"V1.0.0"
            // を同一視、#158 M-1 の規則を踏襲)。malformed な既存 version は正規化不能なので比較対象外
            // (= ctor/Form_Load で警告済の前提)。existingVersionStrings が null の旧 caller 経路では
            // currentVersion 単体との比較に fallback する。
            string newNormalized;
            if (!SemverInputControl.TryNormalize(semverNext.VersionString, out newNormalized))
            {
                // IsValid 通過後なので通常ここには来ない。defensive に生値で比較継続。
                newNormalized = semverNext.VersionString;
            }
            var versionsToCheck = existingVersionStrings ?? new List<string> { currentVersion };
            foreach (var existing in versionsToCheck)
            {
                string existingNormalized;
                if (SemverInputControl.TryNormalize(existing ?? "", out existingNormalized)
                    && string.Equals(newNormalized, existingNormalized, StringComparison.OrdinalIgnoreCase))
                {
                    // (#158 H3) bump button は削除済なので NumericUpDown 直接操作のみ案内。
                    MessageBox.Show("指定されたバージョンは既に存在します:\n\n" +
                        "  " + existing + "\n\n" +
                        "別のバージョン番号を指定してください。Major / Minor / Patch の数値を直接編集できます " +
                        "(▲ で +1、▼ で -1)。",
                        "バージョン重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    semverNext.Focus();
                    return false;
                }
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

            // (#234) サムネ/背景は AddGameForm と同様、OK 時点で「存在する」かつ「コピー元フォルダ内」で
            // あることを最終検証する。これを怠ると、btnOK_Click が File.Exists(false) 時に絶対パスを
            // そのまま NewVersion に残し、GameSectionPanel の Path.Combine(versionFolderName, 絶対パス) が
            // 絶対パスを素通しして「コピーされない元フォルダを指す壊れた画像パス」を DB 保存する穴になる
            // (④ と同種の silent corruption)。auto 検出値も含めてここで一律に弾く。
            if (!string.IsNullOrWhiteSpace(txtThumbnailPath.Text))
            {
                if (!File.Exists(txtThumbnailPath.Text))
                {
                    MessageBox.Show("選択されたサムネイル画像が見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectThumbnail.Focus();
                    return false;
                }
                if (!string.IsNullOrEmpty(selectedFolderPath)
                    && !txtThumbnailPath.Text.StartsWith(selectedFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("サムネイル画像はゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectThumbnail.Focus();
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(txtBackgroundPath.Text))
            {
                if (!File.Exists(txtBackgroundPath.Text))
                {
                    MessageBox.Show("選択された背景画像が見つかりません。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
                if (!string.IsNullOrEmpty(selectedFolderPath)
                    && !txtBackgroundPath.Text.StartsWith(selectedFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("背景画像はゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
            }

            return true;
        }

        private void UpdateThumbnailPreview() => ImagePreviewHelper.UpdatePreview(picThumbnailPreview, txtThumbnailPath.Text);
        private void UpdateBackgroundPreview() => ImagePreviewHelper.UpdatePreview(picBackgroundPreview, txtBackgroundPath.Text);

        private void btnTestRun_Click(object sender, EventArgs e) =>
            GameFormHelper.TestRunGame(txtExecutablePath.Text.Trim(), txtArguments.Text);
    }
}
