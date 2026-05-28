using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TonePrism.Manager.Controls;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace TonePrism.Manager
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

        // (#158 round 5 L-4) cmbVersionList の現在表示中 GameVersion を保持。dropdown 切替時に
        // SaveGameDataToVersion でこの値に対して in-memory commit してから新しい version を Load する。
        // 旧実装は cmbVersionList_SelectedIndexChanged 直後に宣言されてフィールド集約規約を破っていた。
        private GameVersion currentDisplayingVersion = null;

        // (#234) LoadVersions が初期選択した version (= 起動対象 = games.version に一致する版) の DB id。
        // OK 押下時に「ドロップダウンを別の版に切り替えたまま保存しようとしている」= アクティブ版の
        // 暗黙切替を検出して確認ダイアログを出すために使う。row 単位 (DB id) で比較するため、
        // アクティブ版を rename しただけ (同 row・version 文字列のみ変更) では誤発火しない。
        private int? _initialSelectedVersionId = null;

        // (#158 CX-1) folder rename を 2-phase 化するための plan 単位 (Phase 1 で構築 / Phase 2 で実行)。
        // (#158 round 4 codex P1) Old{Executable,Thumbnail,Background}Path: in-memory state rollback 用に
        // path 書き換え前の値を capture。disk Move を rollback しても in-memory が NEW のままだと、同
        // dialog で再 OK 押下時に diff check が false (originalVer も snapshot 経由で NEW 化済) で
        // rename skip → DB に NEW 値 + 旧 disk folder 名で書き込む silent drift が再発する。
        // (#158 round 5 L-5) MoveDone: SourceExists=false 経路 (旧 folder 不在で Move skip) でも
        // path/snapshot mutation は行うので rollback 対象としては記録しつつ、disk Move 戻しは skip
        // させる flag。Move を実行した entry のみ true。
        private class RenamePlan
        {
            public GameVersion Version;
            public string OldDir;
            public string NewDir;
            public string OriginalVer;
            public bool SourceExists;
            public bool MoveDone;
            public string OldExecutablePath;
            public string OldThumbnailPath;
            public string OldBackgroundPath;
        }

        /// <summary>
        /// (#158 round 4 codex P1 + round 5 L-5) rename rollback の共通処理: completedRenames を逆順に
        /// disk Move を戻し (MoveDone=true のみ)、各エントリの in-memory state (_originalVersionByDbId
        /// snapshot + GameVersion の path 群) を capture 前の値に restore する。CX-1 (Phase 2 中の Move
        /// 失敗) と M-4 (UpdateGameVersion 失敗) 両方の catch 経路で呼び出される。disk Move の失敗は
        /// console log + count、in-memory 復元は失敗しない (= 単純代入)。
        ///
        /// **注意 (#158 round 5 codex P1)**: 本 method は「DB が一切 commit されていない」前提でのみ
        /// 安全に呼べる。`VersionRepository.Update` は call ごとに独立 transaction で commit するため、
        /// M-4 の UpdateGameVersion ループで N 件目で失敗しても 0..N-1 件目は既に DB commit 済。その
        /// 状態で本 method を呼ぶと commit 済 row が指す path/folder 名が disk rollback で消失して
        /// drift する。M-4 catch は dbSucceededCount を track して、>0 なら本 method を呼ばずに別経路
        /// (ユーザーに partial commit 状態を通知 + Manager 再起動を促す) で処理すること。
        /// </summary>
        private void RollbackCompletedRenames(List<RenamePlan> completedRenames, out int rolledBack, out int rollbackFailures)
        {
            rolledBack = 0;
            rollbackFailures = 0;
            for (int i = completedRenames.Count - 1; i >= 0; i--)
            {
                var done = completedRenames[i];
                if (done.MoveDone)
                {
                    try
                    {
                        System.IO.Directory.Move(done.NewDir, done.OldDir);
                        rolledBack++;
                    }
                    catch (Exception rbEx)
                    {
                        rollbackFailures++;
                        Logger.Error("[EditGameForm] (#158 rollback) disk rename 戻し失敗: " + done.NewDir + " → " + done.OldDir, rbEx);
                    }
                }
                // in-memory 復元 (disk Move 成否 / SourceExists=false に関わらず、UI/DB drift を最小化
                // するため必ず実行)。
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

            // (#224) 起動オプションの初期値設定はここでは行わない。LoadVersions →
            // SelectedIndexChanged → LoadGameDataForVersion が form open 時に走り、選択版の
            // arguments (空なら games フォールバック) を txtArguments に流すため、ここでの games
            // 由来ロードは重複かつ per-version 表示を一瞬上書きするので削除。

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
                    // (#158 round 5 H-1) 旧文言は "v0.0.0 にフォールバック" 固定だったが round 4 H-1 で
                    // TryParseAndSet が NumericUpDown 範囲外を parse 失敗扱いにしたため、range overflow
                    // (例: v500.0.0) では Clamp で UI が上限値 (例: v99.0.0) に張り付く。LoadVersions の
                    // 集約 MessageBox は per-version の clamp 結果を全件列挙すると長大になるため、文言を
                    // 「v0.0.0 または上限値に clamp」と幅広く書いて user に「該当 version を選択して
                    // UI 表示値を確認」する flow を促す形にする。
                    MessageBox.Show(this,
                        "DB に保存されている version 文字列のうち " + malformedVersions.Count + " 件が " +
                        "SemVer 形式ではありません。該当バージョンを選択すると v0.0.0 または上限値に " +
                        "clamp されて表示されるので、UI で実表示値を確認 → 意図した version 番号に修正して " +
                        "から OK を押してください (= 修正せず OK するとこの clamp 値が DB に書き戻されます)。\n\n" +
                        string.Join("\n", malformedVersions),
                        "バージョン読み込み警告 (" + malformedVersions.Count + " 件)",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                if (originalGame.Version != null)
                {
                    // (#158 round 7 M-2) `==` (= Ordinal) ではなく OrdinalIgnoreCase 比較。CX-3 で大文字
                    // V を regex 受理にした副作用で、games.version="V1.0.0" / game_versions.version="v1.0.0"
                    // (どこかの normalize 経由で書き戻された) が共存しうる。生 == 比較だと false →
                    // fallback で先頭 (= 最新版) が選択され「user が active と思っていた version と違う
                    // ものが表示される」silent UX drift になる。dup-check / rename 比較と同じ規則に揃える。
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (item is GameVersion v && string.Equals(v.Version, originalGame.Version, StringComparison.OrdinalIgnoreCase))
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

                // (#234) 初期選択 = 起動対象の版。OK 時のアクティブ版切替検出の基準として記録。
                _initialSelectedVersionId = (cmbVersionList.SelectedItem as GameVersion)?.Id;

                // 表示用にフォーマット
                cmbVersionList.DisplayMember = "Version"; // GameVersionクラスのToStringをオーバーライドするか、DisplayMemberを設定
            }
            catch (Exception ex)
            {
                Logger.Error("バージョン情報の読み込みに失敗", ex);
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
            // (#224 バグ①) 起動オプションは他フィールド同様 version に保存する必要があるが、旧実装は
            // ここで漏れており OK 保存時の mirror (game.Arguments = selectedVersion.Arguments) で
            // games.arguments が null 上書きされていた。版にも保存して per-version で round-trip させる。
            version.Arguments = string.IsNullOrWhiteSpace(txtArguments.Text) ? null : txtArguments.Text.Trim();
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

            // (#158 round 8.6 / #164) txtDescription / txtVersionDescription はそれぞれ
            // game_versions.description (ゲーム説明文) / game_versions.update_note (更新内容) を保持。
            // (#224 / #234) 各項目は per-version の値を読む。ただし「アクティブ版」(= games.version に
            // 一致する版。games は定義上この版の mirror) に限り、版の値が空のとき games にフォールバック
            // する。これは「desync と証明できる行」限定の修復 (Codex P2 / review #1 — 非アクティブ版の
            // 意図的な空は対象外なので per-version 独立性を壊さない)。旧 AddGameForm 由来の初期版行は
            // Description/Arguments だけでなく Title/Genre/難易度/プレイ時間/コントローラ/通信/サムネ/
            // 背景/製作者 も未設定だった (#234) ため、それら全項目をアクティブ版フォールバックで健全化
            // する。フォールバックした値は OK 保存でアクティブ版に書き戻され自己修復する。
            bool isActiveVersion = !string.IsNullOrEmpty(originalGame.Version)
                && string.Equals(ToVersionLeaf(version.Version ?? ""), ToVersionLeaf(originalGame.Version), StringComparison.OrdinalIgnoreCase);

            txtTitle.Text = !string.IsNullOrWhiteSpace(version.Title)
                ? version.Title
                : (isActiveVersion ? (originalGame.Title ?? "") : "");
            txtDescription.Text = !string.IsNullOrWhiteSpace(version.Description)
                ? version.Description
                : (isActiveVersion ? (originalGame.Description ?? "") : "");
            txtArguments.Text = !string.IsNullOrWhiteSpace(version.Arguments)
                ? version.Arguments
                : (isActiveVersion ? (originalGame.Arguments ?? "") : "");
            txtVersionDescription.Text = version.UpdateNote ?? "";

            // ジャンル (#234: 版が空ならアクティブ版に限り games へフォールバック)
            var genreToShow = (version.Genre != null && version.Genre.Count > 0)
                ? version.Genre
                : (isActiveVersion ? originalGame.Genre : null);
            GameFormHelper.SetSelectedGenres(clbGenre, genreToShow);

            // 数値系
            if (version.MinPlayers.HasValue) numMinPlayers.Value = version.MinPlayers.Value;
            else if (isActiveVersion && originalGame.MinPlayers.HasValue) numMinPlayers.Value = originalGame.MinPlayers.Value;
            if (version.MaxPlayers.HasValue) numMaxPlayers.Value = version.MaxPlayers.Value;
            else if (isActiveVersion && originalGame.MaxPlayers.HasValue) numMaxPlayers.Value = originalGame.MaxPlayers.Value;

            // Difficulty (1-3)
            if (version.Difficulty.HasValue && version.Difficulty >= 1 && version.Difficulty <= 3)
                cmbDifficulty.SelectedIndex = version.Difficulty.Value - 1;
            else if (isActiveVersion && originalGame.Difficulty.HasValue && originalGame.Difficulty >= 1 && originalGame.Difficulty <= 3)
                cmbDifficulty.SelectedIndex = originalGame.Difficulty.Value - 1;
            else cmbDifficulty.SelectedIndex = 1;

            // PlayTime (1-3)
            if (version.PlayTime.HasValue && version.PlayTime >= 1 && version.PlayTime <= 3)
                cmbPlayTime.SelectedIndex = version.PlayTime.Value - 1;
            else if (isActiveVersion && originalGame.PlayTime.HasValue && originalGame.PlayTime >= 1 && originalGame.PlayTime <= 3)
                cmbPlayTime.SelectedIndex = originalGame.PlayTime.Value - 1;
            else cmbPlayTime.SelectedIndex = 1;

            // Connection / ControllerSupport は非 nullable で「未設定」を判別する sentinel が無い。
            // アクティブ版は games が定義上の mirror (= 真値) なので、版値ではなく games 値を採用して
            // 旧初期版行 (= 既定値 0 / false で保存されていた) を健全化する。非アクティブ版は版値のまま。
            int connToShow = isActiveVersion ? originalGame.SupportedConnection : version.SupportedConnection;
            if (connToShow >= 0 && connToShow <= 2)
                cmbSupportedConnection.SelectedIndex = connToShow;
            else cmbSupportedConnection.SelectedIndex = 0;

            chkControllerSupport.Checked = isActiveVersion ? originalGame.ControllerSupport : version.ControllerSupport;

            // Paths（相対パスを絶対パスに変換して表示。#234: 版が空ならアクティブ版に限り games フォールバック）
            string thumbToShow = !string.IsNullOrEmpty(version.ThumbnailPath)
                ? version.ThumbnailPath
                : (isActiveVersion ? originalGame.ThumbnailPath : null);
            string bgToShow = !string.IsNullOrEmpty(version.BackgroundPath)
                ? version.BackgroundPath
                : (isActiveVersion ? originalGame.BackgroundPath : null);
            txtExecutablePath.Text = !string.IsNullOrEmpty(version.ExecutablePath)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, version.ExecutablePath) : "";
            txtThumbnailPath.Text = !string.IsNullOrEmpty(thumbToShow)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, thumbToShow) : "";
            txtBackgroundPath.Text = !string.IsNullOrEmpty(bgToShow)
                ? PathConversionHelper.ToAbsolutePath(gameFolder, bgToShow) : "";

            // Developers (#234: 版が空ならアクティブ版に限り games の製作者へフォールバック)
            developers.Clear();
            if (version.Developers != null && version.Developers.Count > 0)
            {
                foreach (var d in version.Developers) developers.Add(d);
            }
            else if (isActiveVersion && originalGame.Developers != null)
            {
                // games 由来の製作者をディープコピー (グリッド編集で originalGame.Developers を汚さない)。
                // Id はコピーしない (= 保存時に version_id 付きの新規行として INSERT される)。
                foreach (var d in originalGame.Developers)
                {
                    developers.Add(new DeveloperInfo
                    {
                        GameId = d.GameId,
                        LastName = d.LastName,
                        FirstName = d.FirstName,
                        Grade = d.Grade
                    });
                }
            }
            RefreshDevelopersGrid();

            // (#158 round 8.6 / #164) txtVersionDescription.Text 代入は冒頭で実施済 (二重代入削除)。
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

            // (#158 round 7 L-2 + L-3) 旧実装は (a) suffix scan / (b) 空文字 scan / (c) 数値 scan の
            // 3 段 return で、1 つの version が複数違反を持つと user は 2-3 巡 OK を押させられる UX。
            // 1 ループで classification → empty / malformed-suffix / malformed-numeric の 3 リストに
            // 分けて 1 つの MessageBox で全件まとめて表示する形に集約。
            // suffix 切り出しは TrySplit static helper 経由 (round 7 L-3、IndexOf('-') 直書きの
            // "v-1.0.0" 誤判定余地を排除)。
            var emptyIds = new List<string>();
            var malformedSuffixEntries = new List<string>();
            var malformedNumericEntries = new List<string>();
            foreach (var item in cmbVersionList.Items)
            {
                if (!(item is GameVersion vChk)) continue;
                string ver = vChk.Version;
                if (string.IsNullOrEmpty(ver))
                {
                    emptyIds.Add("(id=" + vChk.Id + ")");
                    continue;
                }
                string core, sfx;
                if (SemverInputControl.TrySplit(ver, out core, out sfx))
                {
                    if (!SemverInputControl.IsSuffixValid(sfx))
                    {
                        malformedSuffixEntries.Add("  - id=" + vChk.Id + ": '" + ver + "' (suffix 部分: '" + sfx + "')");
                    }
                }
                string normIgnored;
                if (!SemverInputControl.TryNormalize(ver, out normIgnored))
                {
                    malformedNumericEntries.Add("  - id=" + vChk.Id + ": '" + ver + "'");
                }
            }
            if (emptyIds.Count > 0 || malformedSuffixEntries.Count > 0 || malformedNumericEntries.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("以下のバージョンに修正が必要です。Manager の dropdown で該当 version を" +
                    "選択して入力欄を直してから再度 OK を押してください (= このまま OK すると DB に" +
                    "書き戻されます)。");
                sb.AppendLine();
                if (emptyIds.Count > 0)
                {
                    sb.AppendLine("● バージョン文字列が空 / 未設定 (" + emptyIds.Count + " 件):");
                    sb.AppendLine("  " + string.Join("\n  ", emptyIds));
                    sb.AppendLine();
                }
                if (malformedSuffixEntries.Count > 0)
                {
                    sb.AppendLine("● suffix 部分が SemVer 形式ではない (" + malformedSuffixEntries.Count +
                        " 件、英数字とハイフンの identifier をピリオドで区切る形式のみ可、例: rc1 / beta.2):");
                    sb.AppendLine(string.Join("\n", malformedSuffixEntries));
                    sb.AppendLine();
                }
                if (malformedNumericEntries.Count > 0)
                {
                    sb.AppendLine("● 数値部 (Major/Minor/Patch) または書式が parse 不能 (" +
                        malformedNumericEntries.Count + " 件):");
                    sb.AppendLine(string.Join("\n", malformedNumericEntries));
                    sb.AppendLine();
                }
                int total = emptyIds.Count + malformedSuffixEntries.Count + malformedNumericEntries.Count;
                MessageBox.Show(this, sb.ToString().TrimEnd(),
                    "バージョン入力エラー (" + total + " 件)",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 既知の残存 path (#158 round 6 M-1): currently-displayed の malformed は前段 dup-check 直前
            // SaveGameDataToVersion(currentSelected) で v.Version が clamp 値 (例: "v0.0.0" or "v99.0.0")
            // に上書き済 → 上の TryNormalize は succeed → 本 scan では catch できない。LoadVersions
            // 警告での user 認知 + UI clamp 表示 visibility に頼る (= 表示中 version は user の目に入る)。
            // (#158 round 6 codex P2) GroupBy のキーを TryNormalize 結果に変える。旧実装は raw v.Version
            // で比較していたため、過去 DB の "v1.0.0" / "1.0.0" / "V1.0.0" を別 key として素通しして
            // semantic 上は重複なのに通る silent danger があった (= Q2 fix の裏口再オープン状態)。
            // (#158 round 7 M-1) 上の事前 scan (空文字 / malformed-suffix / malformed-numeric の 3 段
            // 集約) で全 malformed を弾いて return しているため、ここに到達する時点で全件 TryNormalize
            // 成功確定。三項の `: v.Version` fallback path は事実上 dead code だが defensive に残す
            // (= 万一上の scan が緩められた場合の guard rail として機能、silent regression 防止)。
            // (#158 round 8 senior Low #4) `.Where(v => !string.IsNullOrEmpty(v.Version))` filter は
            // L-2 の事前 scan で空文字 version を return で弾いた後なので dead path、defensive guard
            // rail として残す (round 7 M-1 の fallback コメントと同方針、片方だけ defensive コメント
            // 付いて非対称だったため両方に注記)。
            var versionDups = cmbVersionList.Items
                .OfType<GameVersion>()
                .Where(v => !string.IsNullOrEmpty(v.Version))
                .GroupBy(v =>
                {
                    string normalized;
                    return SemverInputControl.TryNormalize(v.Version, out normalized) ? normalized : v.Version;
                })
                .Where(g => g.Count() > 1)
                .Select(g =>
                {
                    // 重複 key + 重複した raw 値群を表示 (normalize 後同じだが原型は違う場合の手がかり)
                    var raws = g.Select(v => v.Version).Distinct().ToList();
                    return raws.Count == 1 ? g.Key : g.Key + " (生値: " + string.Join(" / ", raws) + ")";
                })
                .ToList();
            if (versionDups.Count > 0)
            {
                MessageBox.Show(
                    "以下のバージョン名が複数のエントリで重複しています (SemVer 正規化後の比較、" +
                    "v 大文字/小文字・leading v 有無は同一視):\n\n  " +
                    string.Join("\n  ", versionDups) +
                    "\n\nバージョン管理ドロップダウンで該当の項目を選択し、別の名前に変更してください。",
                    "バージョン重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // (#234) アクティブ版 (= Launcher で起動する版) の暗黙切替を確認する。
            // OK 時は選択中の版が games 行にミラーされ games.version = 選択版になる (= 起動対象が変わる)
            // 設計のため、ロード時の起動対象と別の版を選んだまま保存しようとした場合に明示確認する。
            // 「いいえ」で編集画面に戻し、ドロップダウンで元の版を選び直せるようにする (= 誤操作で
            // 起動バージョンが入れ替わる footgun を塞ぐ。VersionUpForm のアクティブ化確認と同方針)。
            // row 単位 (DB id) 比較なので、アクティブ版を rename しただけでは発火しない。
            if (cmbVersionList.SelectedItem is GameVersion selForActiveCheck
                && _initialSelectedVersionId.HasValue
                && selForActiveCheck.Id != _initialSelectedVersionId.Value)
            {
                var activeDr = MessageBox.Show(this,
                    "現在表示しているバージョン「" + (selForActiveCheck.Version ?? "(未設定)") + "」を、" +
                    "ランチャーで表示・起動するバージョンにしますか?\n\n" +
                    "  これまでランチャーに表示: " + (originalGame.Version ?? "(未設定)") + "\n" +
                    "  OK 後にランチャーに表示: " + (selForActiveCheck.Version ?? "(未設定)") + "\n\n" +
                    "「はい」を押すと、いま表示しているこのバージョンがランチャーに表示・起動されるように" +
                    "なります。\n" +
                    "「いいえ」を押すと編集画面に戻ります（ランチャーの表示バージョンを変えたくない場合は、" +
                    "バージョン管理のドロップダウンで元のバージョンを選び直してから OK してください）。",
                    "ランチャーに表示するバージョンの確認",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (activeDr != DialogResult.Yes)
                {
                    return;
                }
            }

            // (#179 round 6 M-1 案 B) DB write 直前で他 PC session を再 check (race fence)。
            // SectionPanel 側 (`ShowDialog` 直前) で既に 1 回 check 済だが、user が編集画面を 5-10 分
            // 開きっぱなしにする間に他 PC が編集を始めると衝突しうるため二段 fence。Cancel 選択時は
            // **編集画面に戻る** (= `DialogResult.OK` を設定せず Form を閉じない、入力内容を保持)。
            // ここに置く理由: 全 validation / 重複 check を通った後、folder rename / DB write が始まる
            // 前のタイミングなので Cancel 時の rollback 不要 (disk / DB に未変更で stay)。
            if (SessionConflictHelper.CheckBeforeWrite(this, "ゲーム編集") == DialogResult.Cancel)
            {
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

                    // (#158 round 7 L-4 + round 8 codex P2) collision 判定を 3 経路に分ける:
                    //   (a) 両方存在: 真の collision、Move 不能 → throw
                    //   (b) oldFolder 不在 + newFolder 存在: recovery 可能性 (前回 rename interrupted +
                    //       disk 既に新名 + DB が旧 ID のまま) または別 user の手動作成 folder で silent
                    //       merge risk。区別不能なので user に明示確認 dialog (OK = 既存使用 / Cancel = abort)
                    //   (c) newFolder 不在: 通常 path (oldFolder Move or DB only update)
                    // round 7 L-4 は (b) を一律 throw にしていたため legitimate recovery が永久 block
                    // されていたが、確認 dialog で silent merge risk と recovery 両立。
                    bool oldFolderExists = System.IO.Directory.Exists(oldFolder);
                    bool newFolderExists = System.IO.Directory.Exists(newFolder);
                    if (oldFolderExists && newFolderExists)
                    {
                        // (a) 両方存在 = collision
                        throw new InvalidOperationException($"フォルダ「{newFolder}」が既に存在します。");
                    }
                    if (!oldFolderExists && newFolderExists)
                    {
                        // (b) recovery 可能性 / silent merge risk → user 確認
                        var dr = MessageBox.Show(this,
                            "指定された新しいゲーム ID のフォルダが既に存在しますが、旧 ID のフォルダは" +
                            "見つかりません:\n  " + newFolder + "\n\n" +
                            "これは前回の rename が DB 更新前に中断された残骸か、別目的で手動作成された" +
                            "フォルダの可能性があります。\n\n" +
                            "  ・ 前者なら「OK」で既存フォルダの中身を引き継いで DB のみ更新します\n" +
                            "  ・ 後者なら「キャンセル」して中身を別の場所に退避してから再試行してください\n\n" +
                            "既存フォルダの中身をそのまま使って DB を新 gameId に更新しますか?",
                            "フォルダ同期確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                        if (dr != DialogResult.OK)
                        {
                            throw new OperationCanceledException("ユーザーがフォルダ同期をキャンセルしました。");
                        }
                        // OK: ProcessingDialog 内の `if (Directory.Exists(oldFolder)) { Move; folderRenamed=true; }`
                        // で Move skip + folderRenamed=false のまま DB 更新が走る。
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

                // 起動オプション (#234 追加精査: 空白は null 正規化、Add/VersionUp と DB 表現を統一。
                // 通常経路では下の selectedVersion 反映で版の正規化値に上書きされるが、selectedVersion==null
                // の防御経路でも games.arguments に "" を残さないよう正規化を揃える)。
                string arguments = string.IsNullOrWhiteSpace(txtArguments.Text) ? null : txtArguments.Text.Trim();

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

                // (#234) games 行 (version_id IS NULL) の製作者は、編集中リスト (= 選択中の版 = OK 後の
                // アクティブ版の製作者) をミラーする。旧実装は originalGame.Developers をそのまま温存して
                // いたため、アクティブ版から製作者を全削除しても games 行の旧製作者が残り、Launcher の
                // 「版に紐づく製作者が空なら version_id IS NULL にフォールバック」(game_repository.gd) で
                // 削除したはずの製作者が表示され続けるバグがあった。games を選択版のミラーに揃えて解消する。
                game.Developers = new List<DeveloperInfo>(developers);

                // 選択中のバージョン
                var selectedVersion = cmbVersionList.SelectedItem as GameVersion;

                if (selectedVersion == null)
                {
                    // バージョンが無い場合は従来通りの更新（本来ありえないが）
                    dbManager.UpdateGame(game);
                }
                else
                {
                    // (#158 L-2 + round 6 M-3) ここでの SaveGameDataToVersion(selectedVersion) 呼び出しは
                    // dup-check 直前の `SaveGameDataToVersion(currentSelected)` (= 上の dup-check ブロックの
                    // すぐ前にある) で同 selectedVersion に対して既に呼ばれており、間の処理 (gameId
                    // rename / folder rename) は selectedVersion のフィールドを変えないため二重実行だった。
                    // 削除して dup-check 経路の 1 回呼び出しに統一。
                    // (round 6 M-3 で「line 497 付近」hardcoded 行番号参照をシンボル参照に書き換え、
                    // refactor / 行ずれで rot しない形に。)

                    // パス関連: 相対パス化ロジック (現在の選択中バージョンに対してのみ適用)
                    // 他のバージョンは既にロード済みまたは編集済みで、その時点でパスは保持されているはず
                    ApplyRelativePaths(selectedVersion);

                    // 2. 全てのバージョンをデータベースに保存
                    // (#158 Q3) version 文字列が変わった version について、per-version folder を rename。
                    // _originalVersionByDbId に LoadVersions 時の DB-fetched version を保存済なので、
                    // 現 v.Version との差分で rename 必要かを判定。relative path にも v<version>/
                    // prefix が含まれているため、rename 後同じく書き換える。
                    // (#234 ④ 以降) AddGameForm 経路もゲームルート基準で相対化するようになり exe/サムネ/
                    // 背景すべてに v<version>/ prefix が乗る (旧コメントの「AddGameForm 経路は prefix なし」
                    // は ④ 修正前の記述で現在は誤り)。EditGameForm 経路・AddGameForm 経路どちらの path も
                    // ReplaceVersionPrefix で正しく書き換わる (前方一致の v<old>/ prefix のみ置換する保守的処理)。
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
                    // (#158 round 6 M-2) Phase 1 衝突 check は disk 現在状態だけ見ると、同 OK 内で
                    // chained rename (例: A→B + B→C を同時) の場合に A→B 計画が「B が既存 disk に
                    // ある」で abort される。実際 B は B→C 計画で空く予定 → 順序付ければ成立。
                    // 全件の oldDir を「予約済み slot (rename で空く予定)」HashSet として除外してから
                    // 衝突判定する。さらに循環 (A→B + B→A) は両方の oldDir が両方の newDir でもあるので
                    // 互いに skip してしまう → 後の Phase 2 で先に Move した方の newDir が衝突して fail
                    // する経路に流れるが、その時の rollback path は既に整備済 (CX-1) のため許容。
                    var reservedOldDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (!(item is GameVersion vR)) continue;
                        if (!_originalVersionByDbId.TryGetValue(vR.Id, out string origR))
                        {
                            // (#158 round 8 senior Med #3) 現状 LoadVersions のみが snapshot を populate
                            // するため cmbVersionList.Items の全 item は snapshot 有り = ここに到達は
                            // 構造上ありえない。将来 form 内に「version 追加」ボタン等が入ると追加直後の
                            // item が snapshot なし → rename loop が黙って skip → silent drift する死角に
                            // なるため、defensive log を残しておく (Logger 移行は #166 で sweep)。
                            Logger.Warn("[EditGameForm] (#158 round 8 Med #3) reservedOldDirs build: snapshot 不在 version id=" + vR.Id + " ('" + (vR.Version ?? "(null)") + "')、rename plan skip");
                            continue;
                        }
                        // (#221) raw 文字列比較ではなく leaf 正規化後で「実際に rename されるか」を判定する。
                        // v prefix 有無や大文字 V のみの差 ("1.0.0"/"V1.0.0" vs "v1.0.0") は raw 不一致だが
                        // ToVersionLeaf で同一 leaf に畳まれ rename されない = この slot は空かない。raw guard の
                        // ままだと「空かない leaf」を予約済み (rename で空く予定) として登録してしまい、別 version
                        // が同一 leaf を target にした衝突を下の Phase 1 衝突 check で見逃す非対称が生じる
                        // (renamePlan ループは leaf 同一を skip するのに、こちらは登録してしまう)。
                        string oldLeafR = ToVersionLeaf(origR);
                        if (string.Equals(oldLeafR, ToVersionLeaf(vR.Version), StringComparison.OrdinalIgnoreCase)) continue;
                        reservedOldDirs.Add(System.IO.Path.Combine(gameFolder, oldLeafR));
                    }
                    foreach (var item in cmbVersionList.Items)
                    {
                        if (!(item is GameVersion v)) continue;
                        if (!_originalVersionByDbId.TryGetValue(v.Id, out string originalVer))
                        {
                            // (#158 round 8 senior Med #3) 同上、defensive log。
                            Logger.Warn("[EditGameForm] (#158 round 8 Med #3) renamePlan build: snapshot 不在 version id=" + v.Id + " ('" + (v.Version ?? "(null)") + "')、rename plan skip");
                            continue;
                        }
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

                        // (#221) ToVersionLeaf 正規化後に old/new が同一フォルダになるケースは disk rename 不要。
                        // 例: DB の version が "1.0.0" (v prefix 無し) のゲームは、save 時に
                        // SemverInputControl.VersionString が v.Version へ正規化形 "v1.0.0" を書き戻すため、
                        // 上の raw 文字列比較 guard (originalVer vs v.Version) は不一致で skip されないが、
                        // ToVersionLeaf はどちらも "v1.0.0" → oldDir == newDir の self-rename plan が作られ、
                        // Phase 2 の Directory.Move(oldDir, newDir) が "source == dest" 例外で
                        // 「フォルダリネーム失敗」になっていた (バージョン未変更の編集が全て詰む)。
                        // 下の UpdateGameVersion ループが全 version を normalized 値で DB 書き戻すので、
                        // ここで rename plan から除外すれば disk は触らず DB だけ正規化される正しい挙動になる。
                        if (string.Equals(oldDir, newDir, StringComparison.OrdinalIgnoreCase)) continue;

                        // (#158 round 6 M-2) reservedOldDirs に含まれる newDir は他 plan の oldDir、
                        // = rename 実行で空く予定なので衝突 skip。それ以外 (= 純粋に既存 disk フォルダ)
                        // のみ真の衝突として block する。
                        // (#158 round 8 codex P1) `Directory.Exists(oldDir) &&` を先頭に追加。oldDir 不在
                        // + newDir 存在は「partial-commit 後の recovery (= 前回 rename で disk は新版名に
                        // なったが DB row が旧版名のまま残っている)」シナリオで、user が DB row を新版名に
                        // 直して OK 押した時にこの check で永久 block されていた。oldDir 不在 = Move 自体
                        // 走らない (Phase 2 で SourceExists=false 経路) のため衝突判定の対象外、両方存在
                        // のみ block する形に絞る。version folder は gameFolder 配下に閉じているため cross-
                        // game 混入の risk なし、silent merge concern は version layer では無視可。
                        bool srcExistsV = System.IO.Directory.Exists(oldDir);
                        if (srcExistsV && System.IO.Directory.Exists(newDir) && !reservedOldDirs.Contains(newDir))
                        {
                            MessageBox.Show(
                                "バージョンフォルダのリネームに失敗しました:\n" +
                                "  " + oldDir + " → " + newDir + "\n\n" +
                                "  移動先フォルダが既に存在します (他の rename 計画でも空く予定なし)。" +
                                "別のバージョン番号を指定してください。",
                                "フォルダ衝突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        renamePlan.Add(new RenamePlan
                        {
                            Version = v,
                            OldDir = oldDir,
                            NewDir = newDir,
                            OriginalVer = originalVer,
                            SourceExists = srcExistsV,
                            // (#158 round 4 codex P1) in-memory rollback 用に path 書き換え前の値を capture。
                            OldExecutablePath = v.ExecutablePath,
                            OldThumbnailPath = v.ThumbnailPath,
                            OldBackgroundPath = v.BackgroundPath,
                        });
                    }

                    // (#158 round 7 H-1 = codex P2) Phase 2 を topological sort で並べ替え。chained
                    // rename (例: A→B + B→C) は Phase 1 の reservedOldDirs check で衝突 skip 通すが、
                    // Phase 2 の実行順序を UI 順 (= cmbVersionList.Items の DB 由来 row 順) のままにすると
                    // A→B が先に走った時点で B disk 残存で Move 失敗 → rollback、と「user 視点で同じ
                    // 操作が成功したり失敗したりする」非決定挙動になる。
                    // greedy sort: 「newDir が他 plan の oldDir でない」plan を優先実行 → destination
                    // が空く plan を先に潰す。cycle (A↔B 等) があれば pickIdx<0 で fall through、残り
                    // を UI 順で append (Phase 2 で先行 Move の newDir 衝突 → CX-1 rollback で安全)。
                    var orderedPlan = new List<RenamePlan>();
                    var pendingPlan = new List<RenamePlan>(renamePlan);
                    while (pendingPlan.Count > 0)
                    {
                        var pendingOldDirs = new HashSet<string>(
                            pendingPlan.Select(pp => pp.OldDir), StringComparer.OrdinalIgnoreCase);
                        int pickIdx = pendingPlan.FindIndex(pp => !pendingOldDirs.Contains(pp.NewDir));
                        if (pickIdx < 0)
                        {
                            // cycle 検出: 残りを UI 順で append、Phase 2 で先行 Move が newDir 衝突して
                            // 通常の CX-1 rollback path に流れる (rollback 経路は整備済 + in-memory revert
                            // も round 4 codex P1 + L-5 で対応済のため安全)。
                            orderedPlan.AddRange(pendingPlan);
                            break;
                        }
                        orderedPlan.Add(pendingPlan[pickIdx]);
                        pendingPlan.RemoveAt(pickIdx);
                    }

                    // Phase 2: 計画通り rename 実行。例外時は完了済を逆順 rollback。
                    // (#158 round 5 L-5) SourceExists=false の entry も path/snapshot mutation するため
                    // completedRenames に追加 (MoveDone=false で disk Move skip flag)。これで
                    // RollbackCompletedRenames が in-memory revert の対象として拾える。
                    // (#158 round 8.6 / #165) Phase 2 を ProcessingDialog (marquee) で包んで UI 応答性を
                    // 維持。共有フォルダ越しや cross-volume では Directory.Move が内部 copy+delete で
                    // 時間がかかり、orderedPlan.Count > 0 の状況で UI が応答停止に見える問題を解消。
                    // gameId rename block (line 743 付近) と同 pattern。worker 内の rollback メッセージは
                    // 文字列を local 変数に format して dialog 終了後に MessageBox 表示する (= MessageBox を
                    // background thread から直接呼ばない pattern、ProcessingDialog の generic error MessageBox
                    // との二重表示も回避するため worker 内では throw せず早期 return で抜ける)。
                    var completedRenames = new List<RenamePlan>();
                    string phase2ErrorMessage = null;
                    Action<IProgress<ProgressInfo>, CancellationToken> phase2Worker = (progress, token) =>
                    {
                        for (int i = 0; i < orderedPlan.Count; i++)
                        {
                            var p = orderedPlan[i];
                            progress?.Report(new ProgressInfo(-1,
                                $"バージョンフォルダをリネーム中 ({i + 1}/{orderedPlan.Count})...",
                                $"{p.OldDir}\n  → {p.NewDir}"));

                            if (!p.SourceExists)
                            {
                                // 旧 folder 不在: AddGameForm 経由で作成されなかった version (= DB のみ存在) 等。
                                // DB 更新だけ続けて警告ログのみ。disk Move 自体は skip するが path/snapshot
                                // mutation はやるため completedRenames に MoveDone=false で記録。
                                Logger.Warn("[EditGameForm] (#158 Q3) version '" + p.OriginalVer + "' のフォルダが見つかりません、rename skip: " + p.OldDir);
                                p.MoveDone = false;
                                completedRenames.Add(p);
                            }
                            else
                            {
                                try
                                {
                                    System.IO.Directory.Move(p.OldDir, p.NewDir);
                                    p.MoveDone = true;
                                    completedRenames.Add(p);
                                }
                                catch (Exception ex)
                                {
                                    // (#158 CX-1 + round 4 codex P1) rollback: 完了済 rename を逆順で disk Move
                                    // 戻し + in-memory state (_originalVersionByDbId snapshot + GameVersion の
                                    // path 群) も capture 前に復元。同 dialog で再 OK 押下時に diff check が
                                    // 正しく triggered されて rename retry できる状態に戻す。
                                    // ここは DB write が一切走っていない (UpdateGameVersion ループより前) ため
                                    // RollbackCompletedRenames は安全に呼べる (round 5 codex P1 の制約該当なし)。
                                    progress?.Report(new ProgressInfo(-1, "ロールバック中...", "完了済の rename を元の名前に戻しています"));
                                    int rolledBack, rollbackFailures;
                                    RollbackCompletedRenames(completedRenames, out rolledBack, out rollbackFailures);
                                    phase2ErrorMessage =
                                        "バージョンフォルダのリネームに失敗しました:\n" +
                                        "  " + p.OldDir + "\n  → " + p.NewDir + "\n\n" +
                                        "  " + ex.Message + "\n\n" +
                                        "  完了済の rename " + rolledBack + " 件を元の名前に rollback しました" +
                                        (rollbackFailures > 0 ? " (rollback 失敗 " + rollbackFailures + " 件、ログファイル参照)" : "") +
                                        "。\n  DB は更新していないので OK 押下前の状態に戻ります。\n\n" +
                                        "Launcher / 別プロセスが該当フォルダを使用していないか確認してください。";
                                    return; // worker 早期終了 (throw しないので ProcessingDialog の generic
                                            // error MessageBox は出ない、detailed MessageBox は dialog 終了後)
                                }
                            }

                            // version 文字列を含む相対パス (`v<old>/...` 形式) を新 prefix に置換。
                            // (#234 ④ 以降) AddGameForm 経路の path も v<version>/ prefix を持つため対象。
                            // prefix を持たない path (= 旧データ等) は ReplaceVersionPrefix が前方一致 skip して無変更。
                            p.Version.ExecutablePath = ReplaceVersionPrefix(p.Version.ExecutablePath, p.OriginalVer, p.Version.Version);
                            p.Version.ThumbnailPath = ReplaceVersionPrefix(p.Version.ThumbnailPath, p.OriginalVer, p.Version.Version);
                            p.Version.BackgroundPath = ReplaceVersionPrefix(p.Version.BackgroundPath, p.OriginalVer, p.Version.Version);

                            // _originalVersionByDbId snapshot を最新化 (将来 LoadVersions を OK 内で呼び直す
                            // path への保険、現状は cmbVersionList.Items 一意 id で同 OK 内の二重処理は
                            // 構造上ありえない、(#158 round 3 L-1) で review label 衝突を避けるため ID 撤去)。
                            _originalVersionByDbId[p.Version.Id] = p.Version.Version;
                        }
                    };

                    if (orderedPlan.Count > 0)
                    {
                        using (var dialog = new ProcessingDialog(phase2Worker)
                        {
                            Text = "バージョンフォルダをリネーム中",
                            MarqueeMode = true,
                            AllowCancel = false
                        })
                        {
                            dialog.ShowDialog(this);
                        }
                        if (phase2ErrorMessage != null)
                        {
                            MessageBox.Show(this, phase2ErrorMessage, "フォルダリネーム失敗",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }

                    // (#158 round 3 M-4 + round 5 codex P1) UpdateGameVersion ループ。
                    // VersionRepository.Update は call ごとに独立 transaction で commit するため、N 件目で
                    // 失敗しても 0..N-1 件目は既に DB commit 済 (per-call transaction)。
                    // - dbSucceededCount == 0: DB 未 commit、disk rollback で OK 押下前状態に戻せる安全
                    //   path → 従来の RollbackCompletedRenames を呼ぶ。
                    // - dbSucceededCount  > 0: 一部 DB commit 済、disk rollback すると commit 済 row が
                    //   指す新 folder 名を消失させて drift 拡大。disk は NEW のままにして user に partial
                    //   commit 状態 + Manager 再起動 + 手動修復を促す。
                    int dbSucceededCount = 0;
                    try
                    {
                        foreach (var item in cmbVersionList.Items)
                        {
                            if (item is GameVersion v)
                            {
                                dbManager.UpdateGameVersion(v);
                                dbSucceededCount++;
                            }
                        }
                    }
                    catch (Exception dbEx)
                    {
                        if (dbSucceededCount == 0)
                        {
                            // DB 一切 commit なし: disk + in-memory rollback で OK 押下前に戻す。
                            int rolledBack, rollbackFailures;
                            RollbackCompletedRenames(completedRenames, out rolledBack, out rollbackFailures);
                            MessageBox.Show(
                                "バージョン情報の DB 更新に失敗しました:\n" +
                                "  " + dbEx.Message + "\n\n" +
                                "  完了済の rename " + rolledBack + " 件を元の名前に rollback しました" +
                                (rollbackFailures > 0
                                    ? " (rollback 失敗 " + rollbackFailures + " 件、Console ログ参照、手動で元に戻してください)"
                                    : "") +
                                "。\n  DB は更新前なので OK 押下前の状態に戻ります。",
                                "DB 更新失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            // (#158 round 5 codex P1) 部分 commit 状態: disk rollback 不可 (commit 済 row
                            // が指す新 folder を消すと drift 拡大)。disk は NEW のまま、user に partial
                            // commit を通知 + Manager 再起動を促す。in-memory は結局 OK Form 終了で破棄
                            // されるので revert 不要。
                            MessageBox.Show(
                                "バージョン情報の DB 更新が途中で失敗しました:\n" +
                                "  " + dbEx.Message + "\n\n" +
                                "  ・ DB 更新成功: " + dbSucceededCount + " 件\n" +
                                "  ・ disk フォルダは新しい名前のまま (rename を rollback しません = 既に commit\n" +
                                "    済の DB row を壊さないため)\n" +
                                "  ・ 残りの version は DB が古い名前のまま、disk は新しい名前で drift 状態\n\n" +
                                "Manager を一度終了して再起動し、該当ゲームの version 一覧を確認してください。" +
                                "DB と disk の不整合が残っている version は手動で再編集してください。",
                                "DB 部分更新失敗 (要手動確認)", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        // (#158 round 6 H-1) 旧実装は `throw;` で外側 catch (System.Data.SQLite.SQLiteException
                        // / Exception) に再投していたため、本 catch で出した詳細 MessageBox の直後に
                        // 「ゲームの更新に失敗しました\n\n{ex.Message}」の汎用 MessageBox が必ず 2 枚目
                        // として出る UX bug があった (= partial commit / 安全 rollback 両 path で必発)。
                        // ここで return すれば form は閉じず DialogResult は default の None のまま、user は
                        // 状態を見て手動で Cancel するか修正リトライできる。
                        return;
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

                    // (#158 round 7 H-2) UpdateGame は UpdateGameVersion 群が全件成功した後に呼ばれる
                    // が、ここで一時的 SQLite 失敗等で例外が出ると「version 群 (= per-call commit 済) は
                    // 新値 / games 行は旧値 / disk folder は新名」という drift で残る。round 5 codex P1
                    // の partial commit pattern と同型なので同 wording で user に通知 + return (= round 6
                    // H-1 の二重 MessageBox 防止のため throw せず form 留める)。disk rollback は行わない
                    // (= UpdateGameVersion が既に commit 済の row が disk 新名を指しているため)。
                    try
                    {
                        dbManager.UpdateGame(game);
                    }
                    catch (Exception gameEx)
                    {
                        // (#158 round 8 senior Low #1) gameIdChanged=true 経路では既に UpdateGameId で
                        // games.gameId は新値、それ以外 (title/genre/people 等) は旧値の混合状態になる。
                        // 「games 行のみ古い値」と言い切ると user が「全 fields old」と誤読する余地が
                        // あるため、gameIdChanged 有無で文言分岐。
                        string driftDetail = gameIdChanged
                            ? "  ・ games 行は部分更新状態 (gameId は新値で更新済み、title / ジャンル / 人数等" +
                              " のフィールドは旧値の可能性あり)\n"
                            : "  ・ games 行のみ古い値で残っており drift 状態\n";
                        MessageBox.Show(
                            "ゲーム本体情報 (games table) の DB 更新に失敗しました:\n" +
                            "  " + gameEx.Message + "\n\n" +
                            "  ・ バージョン情報 (game_versions table) の更新は既に完了しています\n" +
                            "  ・ disk フォルダは新しい名前のまま\n" +
                            driftDetail +
                            "\nManager を一度終了して再起動し、該当ゲームを開いて UpdateGame の項目 (タイトル / " +
                            "ジャンル / 人数等) が想定通りか確認、必要なら手動で再編集してください。",
                            "DB 部分更新失敗 (要手動確認)", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }

                EditedGame = game;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                // (#158 round 8 codex P2) user が gameId rename の同期確認で Cancel を選んだ場合。
                // 既に MessageBox は閉じているのでここでは何も表示しない (= 静かに OK 処理を中断、
                // form 留めて user が編集を続けられる状態に戻す)。
                return;
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
            if (!PathConversionHelper.IsPathInside(gameFolder, txtExecutablePath.Text))
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
                if (!PathConversionHelper.IsPathInside(gameFolder, txtThumbnailPath.Text))
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
                if (!PathConversionHelper.IsPathInside(gameFolder, txtBackgroundPath.Text))
                {
                    MessageBox.Show("背景画像はゲームフォルダ内のファイルを選択してください。\n\n外部のファイルを使用する場合は、バージョンアップ機能をご利用ください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    btnSelectBackground.Focus();
                    return false;
                }
            }

            // (#234 追加精査) 最小プレイ人数 ≤ 最大プレイ人数 を検証 (3 フォーム共通 helper)。
            if (!GameFormHelper.ValidatePlayerCount((int)numMinPlayers.Value, (int)numMaxPlayers.Value, out string playerCountError))
            {
                MessageBox.Show(playerCountError, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                numMinPlayers.Focus();
                return false;
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

