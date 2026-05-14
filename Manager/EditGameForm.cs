using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using GCTonePrism.Manager.Controls;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ゲーム編集フォーム
    /// </summary>
    public partial class EditGameForm : Form
    {
        private DatabaseManager dbManager;
        private GameInfo originalGame;
        private string gameFolder;
        private List<DeveloperInfo> developers;
        private DeveloperListManager devListManager;
        private Label lblArgumentsPlaceholder;

        // (#158) LoadVersions 時に DB から取得したまま (= ユーザー編集前) の version 文字列を id で記録。
        // OK 押下時に「version 文字列が変わった」を検出して per-version folder rename する判定に使う。
        private Dictionary<int, string> _originalVersionByDbId = new Dictionary<int, string>();

        // (#158 CX-1) folder rename を 2-phase 化するための plan 単位 (Phase 1 で構築 / Phase 2 で実行)。
        // (#158 round 4 codex P1) Old{Executable,Thumbnail,Background}Path: in-memory state rollback 用に
        // path 書き換え前の値を capture。disk Move を rollback しても in-memory が NEW のままだと、同
        // dialog で再 OK 押下時に diff check が false (originalVer も snapshot 経由で NEW 化済) で
        // rename skip → DB に NEW 値 + 旧 disk folder 名で書き込む silent drift が再発する。
        private class RenamePlan
        {
            public GameVersion Version;
            public string OldDir;
            public string NewDir;
            public string OriginalVer;
            public bool SourceExists;
            public string OldExecutablePath;
            public string OldThumbnailPath;
            public string OldBackgroundPath;
        }

        /// <summary>
        /// (#158 round 4 codex P1) rename rollback の共通処理: completedRenames を逆順に disk Move を
        /// 戻し、各エントリの in-memory state (_originalVersionByDbId snapshot + GameVersion の path 群)
        /// も capture 前の値に restore する。CX-1 (Phase 2 中の Move 失敗) と M-4 (UpdateGameVersion
        /// 失敗) 両方の catch 経路で呼び出される。disk Move の失敗は console log + count、in-memory
        /// 復元は失敗しない (= 単純代入)。
        /// </summary>
        private void RollbackCompletedRenames(List<RenamePlan> completedRenames, out int rolledBack, out int rollbackFailures)
        {
            rolledBack = 0;
            rollbackFailures = 0;
            for (int i = completedRenames.Count - 1; i >= 0; i--)
            {
                var done = completedRenames[i];
                try
                {
                    System.IO.Directory.Move(done.NewDir, done.OldDir);
                    rolledBack++;
                }
                catch (Exception rbEx)
                {
                    rollbackFailures++;
                    Console.WriteLine("[EditGameForm] (#158 rollback) disk rename 戻し失敗: " + done.NewDir + " → " + done.OldDir + ": " + rbEx.Message);
                }
                // in-memory 復元 (disk Move 成否に関わらず、UI/DB drift を最小化するため必ず実行)。
                _originalVersionByDbId[done.Version.Id] = done.OriginalVer;
                done.Version.ExecutablePath = done.OldExecutablePath;
                done.Version.ThumbnailPath = done.OldThumbnailPath;
                done.Version.BackgroundPath = done.OldBackgroundPath;
            }
        }

        /// <summary>
        /// 編集されたゲーム情報（OKボタンがクリックされた場合のみ設定される）
        /// </summary>
        public GameInfo EditedGame { get; private set; }

        public EditGameForm(DatabaseManager dbManager, GameInfo game)
        {
            InitializeComponent();
            this.dbManager = dbManager;
            this.originalGame = game;
            this.gameFolder = PathManager.GetGameFolder(game.GameId);
            EditedGame = null;
            developers = new List<DeveloperInfo>();
        }

        /// <summary>
        /// フォームロード時の処理
        /// </summary>
        private void EditGameForm_Load(object sender, EventArgs e)
        {
            // ゲームIDは編集不可（Enabled = falseで選択も不可）
            txtGameId.Text = originalGame.GameId;

            // 既存の値を設定
            txtTitle.Text = originalGame.Title ?? "";
            txtDescription.Text = originalGame.Description ?? "";
            
            if (originalGame.ReleaseYear.HasValue)
            {
                numReleaseYear.Value = originalGame.ReleaseYear.Value;
            }
            else
            {
                numReleaseYear.Value = DateTime.Now.Year;
            }

            // ジャンルチェックボックスリストを初期化
            clbGenre.Items.Clear();
            foreach (var genre in GenreList.AvailableGenres)
            {
                clbGenre.Items.Add(genre, false);
            }
            GameFormHelper.SetSelectedGenres(clbGenre, originalGame.Genre);

            if (originalGame.MinPlayers.HasValue)
            {
                numMinPlayers.Value = originalGame.MinPlayers.Value;
            }
            else
            {
                numMinPlayers.Value = 1;
            }

            if (originalGame.MaxPlayers.HasValue)
            {
                numMaxPlayers.Value = originalGame.MaxPlayers.Value;
            }
            else
            {
                numMaxPlayers.Value = 1;
            }

            // コンボボックスを初期化
            GameFormHelper.InitializeDifficultyCombo(cmbDifficulty, originalGame.Difficulty);
            GameFormHelper.InitializePlayTimeCombo(cmbPlayTime, originalGame.PlayTime);
            GameFormHelper.InitializeConnectionCombo(cmbSupportedConnection, originalGame.SupportedConnection);

            chkControllerSupport.Checked = originalGame.ControllerSupport;
            chkIsVisible.Checked = originalGame.IsVisible;

            // ファイルパスの設定（相対パスを絶対パスに変換して表示）
            if (!string.IsNullOrEmpty(originalGame.ExecutablePath))
                txtExecutablePath.Text = PathConversionHelper.ToAbsolutePath(gameFolder, originalGame.ExecutablePath);
            if (!string.IsNullOrEmpty(originalGame.ThumbnailPath))
                txtThumbnailPath.Text = PathConversionHelper.ToAbsolutePath(gameFolder, originalGame.ThumbnailPath);
            if (!string.IsNullOrEmpty(originalGame.BackgroundPath))
                txtBackgroundPath.Text = PathConversionHelper.ToAbsolutePath(gameFolder, originalGame.BackgroundPath);

            // ゲームフォルダの表示（既存のgames/{game_id}/フォルダを表示、編集不可）
            txtGameFolder.Text = gameFolder;



            // 既存の製作者情報をコピー
            if (originalGame.Developers != null)
            {
                foreach (var dev in originalGame.Developers)
                {
                    developers.Add(new DeveloperInfo
                    {
                        Id = dev.Id,
                        GameId = dev.GameId,
                        LastName = dev.LastName,
                        FirstName = dev.FirstName,
                        Grade = dev.Grade
                    });
                }
            }

            // 起動オプションの設定
            if (!string.IsNullOrWhiteSpace(originalGame.Arguments))
            {
                txtArguments.Text = originalGame.Arguments;
            }
            
            // 起動オプションのプレースホルダー設定
            lblArgumentsPlaceholder = GameFormHelper.SetupArgumentsPlaceholder(txtArguments, this);

            // 製作者情報のDataGridViewを初期化
            InitializeDevelopersGrid();

            // バージョン情報を読み込み
            LoadVersions();
        }

        /// <summary>
        /// バージョン情報を読み込み
        /// </summary>
        private void LoadVersions()
        {
            // (#158) original version の snapshot を取り直す (再 load 対応)
            _originalVersionByDbId.Clear();
            try
            {
                var versions = dbManager.GetGameVersions(originalGame.GameId);
                cmbVersionList.Items.Clear();
                // (#158 M-3) malformed version を per-version SelectedIndexChanged で警告すると、
                // DB に複数 malformed あった場合に切替ごと OK を連打させられる UX になるため、
                // LoadVersions 段階で全件 scan し 1 個の MessageBox にまとめる。LoadGameDataForVersion
                // 側の TryParseAndSet は値の流し込み (= UI 整合のための v0.0.0 fallback) は引き続き
                // 行うが per-version MessageBox は出さない (M-3 対応)。
                var malformedVersions = new List<string>();
                foreach (var v in versions)
                {
                    cmbVersionList.Items.Add(v);
                    // (#158) DB-fetched 時点の version 文字列を id key で記録、OK 押下時の rename 検出に使う
                    _originalVersionByDbId[v.Id] = v.Version;
                    string normIgnored;
                    if (!SemverInputControl.TryNormalize(v.Version ?? "", out normIgnored))
                    {
                        malformedVersions.Add("  - id=" + v.Id + ": '" + (v.Version ?? "(null)") + "'");
                    }
                }
                if (malformedVersions.Count > 0)
                {
                    MessageBox.Show(this,
                        "DB に保存されている version 文字列のうち " + malformedVersions.Count + " 件が " +
                        "SemVer 形式ではありません。該当バージョンを選択すると v0.0.0 にフォールバック表示" +
                        "されるので、意図した version 番号に修正してから OK を押してください (= 修正せず OK " +
                        "するとこの値が DB に書き戻されます)。\n\n" +
                        string.Join("\n", malformedVersions),
                        "バージョン読み込み警告 (" + malformedVersions.Count + " 件)",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                if (originalGame.Version != null)
                {
                    // アクティブなバージョンを選択
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (item is GameVersion v && v.Version == originalGame.Version)
                        {
                            cmbVersionList.SelectedItem = item;
                            break;
                        }
                    }
                    
                    // 見つからなかった場合（またはVersionが設定されていない場合）は先頭（最新）を選択
                    if (cmbVersionList.SelectedIndex == -1 && cmbVersionList.Items.Count > 0)
                    {
                        cmbVersionList.SelectedIndex = 0;
                    }
                }
                else if (cmbVersionList.Items.Count > 0)
                {
                    cmbVersionList.SelectedIndex = 0;
                }
                
                // 表示用にフォーマット
                cmbVersionList.DisplayMember = "Version"; // GameVersionクラスのToStringをオーバーライドするか、DisplayMemberを設定
            }
            catch (Exception ex)
            {
                Console.WriteLine($"バージョン情報の読み込みに失敗: {ex.Message}");
            }
        }


        /// <summary>
        /// バージョン選択変更時の処理 - 更新内容を表示
        /// </summary>
        private void cmbVersionList_SelectedIndexChanged(object sender, EventArgs e)
        {
            // まず現在の入力内容を保存（選択変更前のアイテムに対して）
            // しかしSelectedIndexChanged発火時点ではSelectedItemは既に新しいものになっている
            // したがって、前回の選択アイテムを保持しておく必要があるか、
            // または「変更前」イベントがないため、工夫が必要。
            // 簡易的に、LoadGameDataForVersionの冒頭で「現在表示中のデータ」を「直前に選択されていたバージョン」に保存する…のは難しい（直前のバージョンがどれかわからない）
            
            // アプローチ:
            // メンバ変数 `currentDisplayingVersion` を用意し、LoadGameDataForVersionで更新する。
            // その前に `SaveGameDataToVersion(currentDisplayingVersion)` を呼ぶ。
            
            if (currentDisplayingVersion != null)
            {
                SaveGameDataToVersion(currentDisplayingVersion);
            }

            if (cmbVersionList.SelectedItem is GameVersion selectedVersion)
            {
                LoadGameDataForVersion(selectedVersion);
            }
        }

        private GameVersion currentDisplayingVersion = null;

        /// <summary>
        /// バージョンオブジェクトのパスを相対パスに変換・適用する
        /// </summary>
        private void ApplyRelativePaths(GameVersion version)
        {
            if (version == null) return;

            version.ExecutablePath = !string.IsNullOrEmpty(txtExecutablePath.Text)
                ? PathConversionHelper.ToRelativePath(gameFolder, txtExecutablePath.Text) : "";
            version.ThumbnailPath = !string.IsNullOrEmpty(txtThumbnailPath.Text)
                ? PathConversionHelper.ToRelativePath(gameFolder, txtThumbnailPath.Text) : "";
            version.BackgroundPath = !string.IsNullOrEmpty(txtBackgroundPath.Text)
                ? PathConversionHelper.ToRelativePath(gameFolder, txtBackgroundPath.Text) : "";
        }

        /// <summary>
        /// (#158 round 4 M-1) version 文字列から folder leaf 形式 (`v<X>.<Y>.<Z>[-suffix]`、必ず
        /// 小文字 v prefix) を作る。CX-3 で大文字 V を regex 受理にした副作用で、`oldVer="V1.2.3"`
        /// の場合に旧実装の `StartsWith("v")` が case-sensitive で false → `"v" + "V1.2.3" = "vV1.2.3"`
        /// と二重 prefix の歪な leaf になる経路があった。`TrimStart('v', 'V')` で先頭 v/V を一度
        /// 剥がしてから小文字 v を被せ直す形に統一。
        /// </summary>
        private static string ToVersionLeaf(string ver)
        {
            if (ver == null) return "v";
            return "v" + ver.TrimStart('v', 'V');
        }

        /// <summary>
        /// (#158 Q3) 相対パス先頭の `v<oldVer>/` (or `v<oldVer>\`) prefix を `v<newVer>/` に置換する。
        /// AddGameForm が「<gameFolder> 起点で相対化」する関係上、executable_path 等は
        /// 「v<version>/main.exe」のような形で DB 保存されている。version rename 時にこれらも
        /// 連動して書き換える。前方一致のみ (中間に v<old>/ が登場するケースは触らない、保守的)。
        /// </summary>
        private static string ReplaceVersionPrefix(string relPath, string oldVer, string newVer)
        {
            if (string.IsNullOrEmpty(relPath)) return relPath;
            // (#158 round 4 M-1) ToVersionLeaf 経由で大文字 V も leaf 構築可能に統一。
            string oldLeaf = ToVersionLeaf(oldVer);
            string newLeaf = ToVersionLeaf(newVer);
            string oldPrefixFwd = oldLeaf + "/";
            string newPrefixFwd = newLeaf + "/";
            string oldPrefixBack = oldLeaf + "\\";
            string newPrefixBack = newLeaf + "\\";
            if (relPath.StartsWith(oldPrefixFwd, StringComparison.OrdinalIgnoreCase))
                return newPrefixFwd + relPath.Substring(oldPrefixFwd.Length);
            if (relPath.StartsWith(oldPrefixBack, StringComparison.OrdinalIgnoreCase))
                return newPrefixBack + relPath.Substring(oldPrefixBack.Length);
            return relPath;
        }

        private void SaveGameDataToVersion(GameVersion version)
        {
            if (version == null) return;

            // Title
            version.Title = txtTitle.Text.Trim();
            // Descriptionは "Game Description"
            version.Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim();
            // UpdateNote
            version.UpdateNote = string.IsNullOrWhiteSpace(txtVersionDescription.Text) ? null : txtVersionDescription.Text.Trim();
            
            // Version Name (Rename) — (#158) SemverInputControl は NumericUpDown 構造的に常に値を持つので
            // 「空白なら更新しない」旧 guard は不要。VersionString は常に v<X>.<Y>.<Z>[-<suffix>] 形式。
            version.Version = semverVersionName.VersionString;
            
            version.Genre = GameFormHelper.GetSelectedGenres(clbGenre);
            
            version.MinPlayers = (int)numMinPlayers.Value;
            version.MaxPlayers = (int)numMaxPlayers.Value;
            version.Difficulty = cmbDifficulty.SelectedIndex >= 0 ? cmbDifficulty.SelectedIndex + 1 : (int?)null;
            version.PlayTime = cmbPlayTime.SelectedIndex >= 0 ? cmbPlayTime.SelectedIndex + 1 : (int?)null;
            version.ControllerSupport = chkControllerSupport.Checked;
            version.SupportedConnection = cmbSupportedConnection.SelectedIndex >= 0 ? cmbSupportedConnection.SelectedIndex : 0;
            
            // Developers
            version.Developers = new List<DeveloperInfo>(developers);

            // Paths: ApplyRelativePaths handles reading from text boxes and converting to relative if possible
            ApplyRelativePaths(version);
        }

        private void LoadGameDataForVersion(GameVersion version)
        {
            if (version == null) return;

            // 基本情報の読み込み
            // (#158 H2 → M-3) DB から読んだ malformed version (例: "1.0" / "alpha" / null) は v0.0.0
            // fallback 入力する (= UI 整合)。malformed の警告 MessageBox は LoadVersions で全件
            // 事前 scan + 1 回まとめて表示するため per-version では出さない (= dropdown 切替ごとに
            // 同警告を連発させない、M-3)。fallback 入力自体は依然必要 (semverVersionName が前 version
            // の入力値を保持したまま OK 押下されると別 version に化けるため)。
            // (#158 round 4 L-1) 戻り値 / out error は意図的に discard。`out _` で意図を明示。
            semverVersionName.TryParseAndSet(version.Version ?? "", out _);
            txtTitle.Text = version.Title ?? "";
            txtDescription.Text = version.Description ?? "";
            txtVersionDescription.Text = version.UpdateNote ?? "";

            // game_versions.description = ゲーム説明文（バージョンごと）
            // game_versions.update_note = 更新内容（バージョンごと）
            txtDescription.Text = version.Description ?? "";
            
            // ジャンル
            GameFormHelper.SetSelectedGenres(clbGenre, version.Genre);

            // 数値系
            if (version.MinPlayers.HasValue) numMinPlayers.Value = version.MinPlayers.Value;
            if (version.MaxPlayers.HasValue) numMaxPlayers.Value = version.MaxPlayers.Value;
            
            // Difficulty (1-3)
            if (version.Difficulty.HasValue && version.Difficulty >= 1 && version.Difficulty <= 3)
                cmbDifficulty.SelectedIndex = version.Difficulty.Value - 1;
            else cmbDifficulty.SelectedIndex = 1;

            // PlayTime (1-3)
            if (version.PlayTime.HasValue && version.PlayTime >= 1 && version.PlayTime <= 3)
                cmbPlayTime.SelectedIndex = version.PlayTime.Value - 1;
            else cmbPlayTime.SelectedIndex = 1;

            // Connection
            if (version.SupportedConnection >= 0 && version.SupportedConnection <= 2)
                cmbSupportedConnection.SelectedIndex = version.SupportedConnection;
            else cmbSupportedConnection.SelectedIndex = 0;

            chkControllerSupport.Checked = version.ControllerSupport;
            
            // Paths（相対パスを絶対パスに変換して表示）
            txtExecutablePath.Text = !string.IsNullOrEmpty(version.ExecutablePath)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, version.ExecutablePath) : "";
            txtThumbnailPath.Text = !string.IsNullOrEmpty(version.ThumbnailPath)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, version.ThumbnailPath) : "";
            txtBackgroundPath.Text = !string.IsNullOrEmpty(version.BackgroundPath)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, version.BackgroundPath) : "";

            // Developers
            developers.Clear();
            if (version.Developers != null)
            {
                foreach(var d in version.Developers) developers.Add(d);
            }
            RefreshDevelopersGrid();
            
            txtVersionDescription.Text = version.UpdateNote ?? "";
            txtVersionDescription.ReadOnly = false; // 編集可能にする
            
            // Update image previews
            UpdateThumbnailPreview();
            UpdateBackgroundPreview();

            // 現在表示中のバージョンを記録（次回切り替え時の保存用）
            currentDisplayingVersion = version;
        }


        /// <summary>
        /// 製作者情報のDataGridViewを初期化
        /// </summary>
        private void InitializeDevelopersGrid()
        {
            devListManager = new DeveloperListManager(dgvDevelopers, developers);
            devListManager.InitializeGrid();
        }

        private void RefreshDevelopersGrid() => devListManager.Refresh();

        /// <summary>
        /// 実行ファイル選択ボタンクリック（既存のgames/{game_id}/フォルダ内から選択）
        /// </summary>
        private void btnSelectExecutable_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = gameFolder;
                dialog.Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*";
                dialog.Title = "実行ファイルを選択（ゲームフォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtExecutablePath.Text = dialog.FileName;
                }
            }
        }

        /// <summary>
        /// サムネイル画像選択ボタンクリック（既存のgames/{game_id}/フォルダ内から選択）
        /// </summary>
        private void btnSelectThumbnail_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = gameFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "サムネイル画像を選択（ゲームフォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtThumbnailPath.Text = dialog.FileName;
                    UpdateThumbnailPreview();
                }
            }
        }

        /// <summary>
        /// 背景画像選択ボタンクリック（既存のgames/{game_id}/フォルダ内から選択）
        /// </summary>
        private void btnSelectBackground_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = gameFolder;
                dialog.Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*";
                dialog.Title = "背景画像を選択（ゲームフォルダ内から選択）";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtBackgroundPath.Text = dialog.FileName;
                    UpdateBackgroundPreview();
                }
            }
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

            // (#158) semverVersionName の suffix 文字種を validate (= 数値部は NumericUpDown で構造的に safe)。
            // 不正 suffix (例: 日本語) を含む VersionString が DB に流れ込むのを block。
            // (#158 round 4 L-3) 直後の H-1 全件 scan (cmbVersionList.Items 全体) と意図的に重複させて
            // いる: 本 check は「現在表示中の 1 個」が対象で Focus() で UI を該当 control に戻し、
            // 後の全件 scan (line ~520) は dropdown 切替で in-memory commit された別 version の suffix
            // を集約報告する役割。両者は UX 用途が違うので片方削除しないこと。
            string semverErr;
            if (!semverVersionName.IsValid(out semverErr))
            {
                MessageBox.Show(semverErr, "バージョン入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                semverVersionName.Focus();
                return;
            }

            // (#158 Q2) cmbVersionList 内で version 文字列の重複が無いか check。
            // game_versions table は (game_id, version) UNIQUE 制約を持たないので、ユーザーが
            // EditGameForm でうっかり 2 つの version を同じ名前にすると DB に同 (gameId, version)
            // の row が並ぶ silent danger があった (= Launcher 側で「どちらの version か」決定不能)。
            // app-level check で OK 押下時に block する形で fix。
            // 現在編集中の表示内容も反映させるため、選択中 version に対して SaveGameDataToVersion
            // を 1 回呼んでから判定する (= まだ commit してない最新 version 文字列も拾う)。
            if (cmbVersionList.SelectedItem is GameVersion currentSelected)
            {
                SaveGameDataToVersion(currentSelected);
            }

            // (#158 round 3 H-1) 上の semverVersionName.IsValid は表示中 1 個の suffix しか check
            // していないため、ユーザーが version A の txtSuffix に「鈴木」等の不正値を入力 → dropdown
            // を version B に切替 (= cmbVersionList_SelectedIndexChanged 経由で SaveGameDataToVersion(A)
            // により A.Version = "v...-鈴木" を in-memory commit) → そのまま OK 押下、で末尾の
            // dbManager.UpdateGameVersion(v) で bad value が DB に流れ込む silent path があった。
            // cmbVersionList.Items 全件の suffix を SemverInputControl.IsSuffixValid で事前 scan、
            // 不正があれば id 一覧で 1 つの MessageBox に集約して block (M-3 と同 pattern)。
            var malformedSuffixVersions = new List<string>();
            foreach (var item in cmbVersionList.Items)
            {
                if (!(item is GameVersion vChk)) continue;
                // v.Version の suffix 部分を切り出して check。"v1.0.0-鈴木" → "鈴木"。
                string ver = vChk.Version ?? "";
                int dashIdx = ver.IndexOf('-');
                string sfx = dashIdx >= 0 ? ver.Substring(dashIdx + 1) : "";
                if (!SemverInputControl.IsSuffixValid(sfx))
                {
                    malformedSuffixVersions.Add("  - id=" + vChk.Id + ": '" + ver + "' (suffix 部分: '" + sfx + "')");
                }
            }
            if (malformedSuffixVersions.Count > 0)
            {
                MessageBox.Show(this,
                    "以下のバージョンの suffix 部分が SemVer 形式ではありません (英数字とハイフンの " +
                    "identifier をピリオドで区切る形式のみ可、例: rc1 / beta.2)。dropdown でそれぞれ" +
                    "選択して suffix 欄を修正してから再度 OK を押してください。\n\n" +
                    string.Join("\n", malformedSuffixVersions),
                    "バージョン入力エラー (" + malformedSuffixVersions.Count + " 件)",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // (#158 M4) 空文字 / null version は「重複」ではなく「未入力」として別ハンドリング。
            // GroupBy で ?? "" 正規化したまま重複扱いすると、空文字 version が複数あった場合に
            // MessageBox に空行が並んで意味不明になる + H2 silent fallback 経路で複数 version が同時に
            // v0.0.0 化した場合の誤検出回避にもなる。
            var versionsWithEmpty = cmbVersionList.Items.OfType<GameVersion>()
                .Where(v => string.IsNullOrEmpty(v.Version))
                .ToList();
            if (versionsWithEmpty.Count > 0)
            {
                MessageBox.Show(
                    "以下のエントリで version 文字列が空または未設定です:\n\n  " +
                    string.Join("\n  ", versionsWithEmpty.Select(v => "(id=" + v.Id + ")")) +
                    "\n\nバージョン管理ドロップダウンで該当の項目を選択し、有効な version 番号を入力してください。",
                    "バージョン未入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var versionDups = cmbVersionList.Items
                .OfType<GameVersion>()
                .Where(v => !string.IsNullOrEmpty(v.Version))
                .GroupBy(v => v.Version)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (versionDups.Count > 0)
            {
                MessageBox.Show(
                    "以下のバージョン名が複数のエントリで重複しています:\n\n  " +
                    string.Join("\n  ", versionDups) +
                    "\n\nバージョン管理ドロップダウンで該当の項目を選択し、別の名前に変更してください。",
                    "バージョン重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                // ゲームID変更処理
                string newGameId = txtGameId.Text.Trim();
                string oldGameId = originalGame.GameId;
                bool gameIdChanged = !string.Equals(newGameId, oldGameId, StringComparison.Ordinal);

                if (gameIdChanged)
                {
                    // フォルダリネームを先に実行（失敗時にDB不整合を防ぐ）
                    string oldFolder = PathManager.GetGameFolder(oldGameId);
                    string newFolder = PathManager.GetGameFolder(newGameId);

                    if (System.IO.Directory.Exists(oldFolder) && System.IO.Directory.Exists(newFolder))
                    {
                        throw new InvalidOperationException($"フォルダ「{newFolder}」が既に存在します。");
                    }

                    // ゲームフォルダのリネームは同一ボリュームでは一瞬だが、共有フォルダ越しや
                    // クロスボリュームでは内部的にコピー＋削除になり時間がかかる。
                    // ProcessingDialog で進捗を表示する。
                    Exception caught = null;
                    using (var dialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
                    {
                        try
                        {
                            progress?.Report(new ProgressInfo(-1, "フォルダをリネーム中...", $"{oldFolder} → {newFolder}"));

                            bool folderRenamed = false;
                            if (System.IO.Directory.Exists(oldFolder))
                            {
                                System.IO.Directory.Move(oldFolder, newFolder);
                                folderRenamed = true;
                            }

                            progress?.Report(new ProgressInfo(-1, "データベースを更新中...", $"ゲームID: {oldGameId} → {newGameId}"));

                            try
                            {
                                dbManager.UpdateGameId(oldGameId, newGameId);
                            }
                            catch
                            {
                                // DB更新失敗時はフォルダを元に戻す
                                if (folderRenamed)
                                {
                                    progress?.Report(new ProgressInfo(-1, "ロールバック中...", "フォルダを元の名前に戻しています"));
                                    System.IO.Directory.Move(newFolder, oldFolder);
                                }
                                throw;
                            }
                        }
                        catch (Exception ex)
                        {
                            caught = ex;
                            throw;
                        }
                    })
                    {
                        Text = "ゲームIDを変更中",
                        MarqueeMode = true,
                        AllowCancel = false
                    })
                    {
                        var dr = dialog.ShowDialog(this);
                        if (dr != DialogResult.OK)
                        {
                            // ProcessingDialog 内で MessageBox 表示済みなのでここでは何もしない
                            if (caught != null) throw caught;
                            return;
                        }
                    }

                    // gameFolderを更新（以降のパス処理で使用）
                    gameFolder = newFolder;

                    // パステキストボックスを新フォルダベースに更新
                    UpdatePathTextBox(txtExecutablePath, oldFolder, newFolder);
                    UpdatePathTextBox(txtThumbnailPath, oldFolder, newFolder);
                    UpdatePathTextBox(txtBackgroundPath, oldFolder, newFolder);

                    // バージョンオブジェクトのGameIdを新IDに更新
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (item is GameVersion v)
                        {
                            v.GameId = newGameId;
                        }
                    }
                }

                // パスを相対パスに変換（可能な場合）
                string executablePath = PathConversionHelper.ToRelativePath(gameFolder, txtExecutablePath.Text.Trim());
                string thumbnailPath = string.IsNullOrWhiteSpace(txtThumbnailPath.Text) ? null : PathConversionHelper.ToRelativePath(gameFolder, txtThumbnailPath.Text.Trim());
                string backgroundPath = string.IsNullOrWhiteSpace(txtBackgroundPath.Text) ? null : PathConversionHelper.ToRelativePath(gameFolder, txtBackgroundPath.Text.Trim());

                // 起動オプション
                string arguments = txtArguments.Text;

                // GameInfoオブジェクトを作成（既存の値をベースに）
                var game = new GameInfo
                {
                    GameId = txtGameId.Text.Trim(),
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
                    DisplayOrder = originalGame.DisplayOrder, // 表示順序は変更しない（メイン画面のドラッグ&ドロップで変更）
                    IsVisible = chkIsVisible.Checked,
                    Controls = originalGame.Controls, // 後で実装
                    KeyMapping = originalGame.KeyMapping // 後で実装
                };

                // ジャンルを処理
                game.Genre = GameFormHelper.GetSelectedGenres(clbGenre);

                // 製作者情報は既存のものを保持
                game.Developers = originalGame.Developers ?? new List<DeveloperInfo>();

                // 選択中のバージョン
                var selectedVersion = cmbVersionList.SelectedItem as GameVersion;

                if (selectedVersion == null)
                {
                    // バージョンが無い場合は従来通りの更新（本来ありえないが）
                    dbManager.UpdateGame(game);
                }
                else
                {
                    // (#158 L-2) ここでの SaveGameDataToVersion(selectedVersion) は dup-check 直前
                    // (line 497 付近) で同 selectedVersion に対して既に呼ばれており、間の処理 (gameId
                    // rename / folder rename) は selectedVersion のフィールドを変えないため二重実行
                    // だった。削除して dup-check 経路の 1 回呼び出しに統一。

                    // パス関連: 相対パス化ロジック (現在の選択中バージョンに対してのみ適用)
                    // 他のバージョンは既にロード済みまたは編集済みで、その時点でパスは保持されているはず
                    ApplyRelativePaths(selectedVersion);

                    // 2. 全てのバージョンをデータベースに保存
                    // (#158 Q3) version 文字列が変わった version について、per-version folder を rename。
                    // _originalVersionByDbId に LoadVersions 時の DB-fetched version を保存済なので、
                    // 現 v.Version との差分で rename 必要かを判定。relative path にも v<version>/
                    // prefix が含まれているため、rename 後同じく書き換える (= EditGameForm 経路で保存
                    // された path のみ `<gameFolder>` 基準で v<version>/ prefix が乗る、AddGameForm
                    // 経路は version folder 基準なので prefix なし。M2 で誤記訂正、ReplaceVersionPrefix
                    // は前者の prefix のみ書き換える保守的処理)。
                    //
                    // (#158 H1 fix): folder path は `gameFolder` (直前の gameIdChanged block で
                    // `gameFolder = newFolder` に上書き済) を base にする。`PathManager.GetVersionFolder(
                    // originalGame.GameId, ...)` を使うと gameId と version を同 OK で同時変更した
                    // ケースで old gameId の path を返してしまい、disk は手前で `<newGameFolder>` 配下
                    // に既に移動済 → oldDir.Exists = false で silent skip + DB は v<new> に更新、という
                    // silent drift が発生していた。gameFolder 変数を直接使うことで gameIdChanged の
                    // 効果を取り込む。
                    //
                    // (#158 CX-1): rename を 2-phase に分離。Phase 1 で全件衝突 check + 計画作成、
                    // Phase 2 で順次 rename + 例外時は完了済を逆順 rollback。旧実装は「N 件目で失敗 →
                    // return」で disk 上に部分 rename 残存 + DB 未更新 (= 後続の UpdateGameVersion 群が
                    // 走らない) の drift で launcher が「rename 後 disk」「rename 前 DB」を見て該当
                    // version の起動失敗 silent corruption が発生していた。
                    var renamePlan = new List<RenamePlan>();
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (!(item is GameVersion v)) continue;
                        if (!_originalVersionByDbId.TryGetValue(v.Id, out string originalVer)) continue;
                        // (#158 round 3 H-2) CX-3 で大文字 V を regex IgnoreCase 受理にした副作用で、DB に
                        // "V1.2.3" があった version は SaveGameDataToVersion で v.Version = "v1.2.3" に
                        // 正規化される (= getter が常に小文字 v 出力)。case-only な差は disk 上 (Windows
                        // FS は case-insensitive) で同じフォルダなので rename 不要、`OrdinalIgnoreCase`
                        // で skip して DB 側だけ normalized 値で書き戻す。生 `Ordinal` 比較だと case-only
                        // 差で rename path に入り、`Directory.Exists(newDir)` が同フォルダを hit して
                        // "移動先フォルダが既に存在します" abort で詰む regression が発生していた。
                        if (string.Equals(originalVer, v.Version, StringComparison.OrdinalIgnoreCase)) continue;

                        // (#158 round 4 M-1) ToVersionLeaf で大文字 V も小文字 v leaf に正規化。
                        string oldLeaf = ToVersionLeaf(originalVer);
                        string newLeaf = ToVersionLeaf(v.Version);
                        string oldDir = System.IO.Path.Combine(gameFolder, oldLeaf);
                        string newDir = System.IO.Path.Combine(gameFolder, newLeaf);

                        if (System.IO.Directory.Exists(newDir))
                        {
                            MessageBox.Show(
                                "バージョンフォルダのリネームに失敗しました:\n" +
                                "  " + oldDir + " → " + newDir + "\n\n" +
                                "  移動先フォルダが既に存在します。別のバージョン番号を指定してください。",
                                "フォルダ衝突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        renamePlan.Add(new RenamePlan
                        {
                            Version = v,
                            OldDir = oldDir,
                            NewDir = newDir,
                            OriginalVer = originalVer,
                            SourceExists = System.IO.Directory.Exists(oldDir),
                            // (#158 round 4 codex P1) in-memory rollback 用に path 書き換え前の値を capture。
                            OldExecutablePath = v.ExecutablePath,
                            OldThumbnailPath = v.ThumbnailPath,
                            OldBackgroundPath = v.BackgroundPath,
                        });
                    }

                    // Phase 2: 計画通り rename 実行。例外時は完了済を逆順 rollback。
                    var completedRenames = new List<RenamePlan>();
                    foreach (var p in renamePlan)
                    {
                        if (!p.SourceExists)
                        {
                            // 旧 folder 不在: AddGameForm 経由で作成されなかった version (= DB のみ存在) 等。
                            // DB 更新だけ続けて警告ログのみ。rollback 対象外 (Move していないので)。
                            Console.WriteLine("[EditGameForm] (#158 Q3) version '" + p.OriginalVer + "' のフォルダが見つかりません、rename skip: " + p.OldDir);
                        }
                        else
                        {
                            try
                            {
                                System.IO.Directory.Move(p.OldDir, p.NewDir);
                                completedRenames.Add(p);
                            }
                            catch (Exception ex)
                            {
                                // (#158 CX-1 + round 4 codex P1) rollback: 完了済 rename を逆順で disk Move
                                // 戻し + in-memory state (_originalVersionByDbId snapshot + GameVersion の
                                // path 群) も capture 前に復元。同 dialog で再 OK 押下時に diff check が
                                // 正しく triggered されて rename retry できる状態に戻す。
                                int rolledBack, rollbackFailures;
                                RollbackCompletedRenames(completedRenames, out rolledBack, out rollbackFailures);
                                MessageBox.Show(
                                    "バージョンフォルダのリネームに失敗しました:\n" +
                                    "  " + p.OldDir + "\n  → " + p.NewDir + "\n\n" +
                                    "  " + ex.Message + "\n\n" +
                                    "  完了済の rename " + completedRenames.Count + " 件のうち " +
                                    rolledBack + " 件を元の名前に rollback しました" +
                                    (rollbackFailures > 0 ? " (rollback 失敗 " + rollbackFailures + " 件、Console ログ参照)" : "") +
                                    "。\n  DB は更新していないので OK 押下前の状態に戻ります。\n\n" +
                                    "Launcher / 別プロセスが該当フォルダを使用していないか確認してください。",
                                    "フォルダリネーム失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }
                        }

                        // version 文字列を含む相対パス (`v<old>/...` 形式) を新 prefix に置換
                        // (EditGameForm 経路で保存された path のみ対象、AddGameForm 経路は prefix なしで
                        // 影響を受けない、M2)
                        p.Version.ExecutablePath = ReplaceVersionPrefix(p.Version.ExecutablePath, p.OriginalVer, p.Version.Version);
                        p.Version.ThumbnailPath = ReplaceVersionPrefix(p.Version.ThumbnailPath, p.OriginalVer, p.Version.Version);
                        p.Version.BackgroundPath = ReplaceVersionPrefix(p.Version.BackgroundPath, p.OriginalVer, p.Version.Version);

                        // _originalVersionByDbId snapshot を最新化 (将来 LoadVersions を OK 内で呼び直す
                        // path への保険、現状は cmbVersionList.Items 一意 id で同 OK 内の二重処理は
                        // 構造上ありえない、(#158 round 3 L-1) で review label 衝突を避けるため ID 撤去)。
                        _originalVersionByDbId[p.Version.Id] = p.Version.Version;
                    }

                    // (#158 round 3 M-4) UpdateGameVersion ループも try/catch で囲み、DB 例外時は
                    // Phase 2 で完了済の rename を逆順 rollback。CX-1 で fix した「rename 中失敗 →
                    // disk/DB drift」と同型の「rename 完了 → DB update 失敗 → disk は新名 / DB は
                    // 旧名」silent corruption が、SQLite 一時的失敗等で再発しうるため塞ぐ。
                    try
                    {
                        foreach (var item in cmbVersionList.Items)
                        {
                            if (item is GameVersion v)
                            {
                                dbManager.UpdateGameVersion(v);
                            }
                        }
                    }
                    catch (Exception dbEx)
                    {
                        // (#158 round 3 M-4 + round 4 codex P1) disk rollback + in-memory state restore。
                        int rolledBack, rollbackFailures;
                        RollbackCompletedRenames(completedRenames, out rolledBack, out rollbackFailures);
                        MessageBox.Show(
                            "バージョン情報の DB 更新に失敗しました:\n" +
                            "  " + dbEx.Message + "\n\n" +
                            "  完了済の rename " + completedRenames.Count + " 件のうち " +
                            rolledBack + " 件を元の名前に rollback しました" +
                            (rollbackFailures > 0
                                ? " (rollback 失敗 " + rollbackFailures + " 件、Console ログ参照、手動で元に戻してください)"
                                : "") +
                            "。\n  DB は途中まで更新済の可能性があります。Manager を一度再起動して状態を確認してください。",
                            "DB 更新失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        throw;
                    }
                    
                    // 3. メインのゲーム情報を更新（選択中バージョンの全フィールドを反映）
                    game.Title = selectedVersion.Title ?? game.Title;
                    game.Description = selectedVersion.Description;
                    game.Genre = selectedVersion.Genre ?? game.Genre;
                    game.MinPlayers = selectedVersion.MinPlayers;
                    game.MaxPlayers = selectedVersion.MaxPlayers;
                    game.Difficulty = selectedVersion.Difficulty;
                    game.PlayTime = selectedVersion.PlayTime;
                    game.ControllerSupport = selectedVersion.ControllerSupport;
                    game.SupportedConnection = selectedVersion.SupportedConnection;
                    game.ExecutablePath = selectedVersion.ExecutablePath;
                    game.ThumbnailPath = selectedVersion.ThumbnailPath;
                    game.BackgroundPath = selectedVersion.BackgroundPath;
                    game.Arguments = selectedVersion.Arguments;
                    game.Version = selectedVersion.Version;
                    
                    dbManager.UpdateGame(game);
                }

                EditedGame = game;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                string errorMessage = DatabaseManager.GetUserFriendlyErrorMessage(ex);
                MessageBox.Show(
                    $"ゲームの更新に失敗しました。\n\n{errorMessage}",
                    "データベースエラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"ゲームの更新に失敗しました。\n\n{ex.Message}",
                    "エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
        /// フォルダリネーム後にパステキストボックスの値を新フォルダベースに更新
        /// </summary>
        private void UpdatePathTextBox(TextBox textBox, string oldFolder, string newFolder)
        {
            string path = textBox.Text.Trim();
            if (string.IsNullOrEmpty(path)) return;

            // 絶対パスで旧フォルダ配下の場合、新フォルダに置換
            if (Path.IsPathRooted(path) && path.StartsWith(oldFolder, StringComparison.OrdinalIgnoreCase))
            {
                textBox.Text = newFolder + path.Substring(oldFolder.Length);
            }
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
            if (!GameFormHelper.IsValidGameId(txtGameId.Text))
            {
                MessageBox.Show("ゲームIDには半角英数字、ハイフン(-)、アンダースコア(_)のみ使用できます。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            // 実行ファイルパス
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

            // 実行ファイルがゲームフォルダ内にあるか確認
            if (!txtExecutablePath.Text.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("実行ファイルはゲームフォルダ内のファイルを選択してください。\n\n外部のファイルを使用する場合は、バージョンアップ機能をご利用ください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                if (!txtThumbnailPath.Text.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("サムネイル画像はゲームフォルダ内のファイルを選択してください。\n\n外部のファイルを使用する場合は、バージョンアップ機能をご利用ください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                if (!txtBackgroundPath.Text.StartsWith(gameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("背景画像はゲームフォルダ内のファイルを選択してください。\n\n外部のファイルを使用する場合は、バージョンアップ機能をご利用ください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
            }

            return true;
        }

        private void btnAddDeveloper_Click(object sender, EventArgs e) => devListManager.Add();
        private void btnEditDeveloper_Click(object sender, EventArgs e) => devListManager.Edit();
        private void btnDeleteDeveloper_Click(object sender, EventArgs e) => devListManager.Delete();

        private void UpdateThumbnailPreview() => ImagePreviewHelper.UpdatePreview(picThumbnailPreview, txtThumbnailPath.Text, gameFolder);
        private void UpdateBackgroundPreview() => ImagePreviewHelper.UpdatePreview(picBackgroundPreview, txtBackgroundPath.Text, gameFolder);

        private void btnTestRun_Click(object sender, EventArgs e) =>
            GameFormHelper.TestRunGame(txtExecutablePath.Text.Trim(), txtArguments.Text, gameFolder);

    }
}

