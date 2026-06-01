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
                else if (form.DataChangedOutsideOk)
                {
                    // (#209 review finding 1) バージョン即時削除は OK を介さず DB 確定するため、Cancel/×で閉じても
                    // グリッドを再読込する。怠ると active 版付け替え後にメイン画面が削除済み版を出し続ける (stale)。
                    LoadGames();
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
                    if (!GameFormHelper.ConfirmExcludedFoldersBeforeCopy(this, form.SourceFolderPath))
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
                                try { if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true); }
                                catch (Exception delEx) { Logger.Warn("[GameSectionPanel] (追加精査 ②) versionDir 削除失敗: " + versionDir + ": " + delEx.Message); }
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
                                try { if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true); } catch { /* swallow */ }
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
                            Logger.Warn("[GameSectionPanel] (M4) ThumbnailPath が絶対 path、保存 skip: " + form.NewVersion.ThumbnailPath);
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
                            Logger.Warn("[GameSectionPanel] (M4) BackgroundPath が絶対 path、保存 skip: " + form.NewVersion.BackgroundPath);
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
                                try { if (Directory.Exists(versionDir)) Directory.Delete(versionDir, true); }
                                catch (Exception delEx) { Logger.Warn("[GameSectionPanel] (M5) versionDir rollback 削除失敗: " + versionDir + ": " + delEx.Message); }
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

                        LoadGames();
                    }
                    else
                    {
                        // (H3) 非 OK 経路 (Cancel / Abort=例外) は版データ未 commit + コピー済 tempDir が
                        // 残留する状態。tempDir は guid 付きで永続的に block する経路は無いが、disk full
                        // 防止のため掃除する。
                        // (Critical-1) この経路は ProcessingDialog 内 = Move 前なので、片付け対象は tempDir のみ。
                        // versionDir は触らない (= 我々はまだ owner ではない、勝者が既に作っていれば破壊しない)。
                        try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                        catch (Exception delEx) { Logger.Warn("[GameSectionPanel] (H3) 中断時の tempDir 削除失敗: " + tempDir + ": " + delEx.Message); }

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
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch (Exception delEx) { Logger.Warn("[GameSectionPanel] (round 5 H1) tempDir 削除失敗: " + tempDir + ": " + delEx.Message); }

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
