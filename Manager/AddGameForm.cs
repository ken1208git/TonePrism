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
        // (#234 追加精査 ②対称化) rollback 時に「この追加操作で新規作成した」親フォルダ games/{id}/ だけを
        // 空なら片付けるための記録。SPEC §3.8.5 が提案する parentCreatedThisCall flag 方式。追加前から親が
        // 存在していた #120「既存フォルダで続行」経路では baseGameFolderCreated=false となり親に一切触らない。
        private string baseGameFolder;
        private bool baseGameFolderCreated;
        // (M7) 「この追加操作で destinationGameFolder を新規作成した」flag。並行 Manager race で
        // 「既に存在する」throw を踏んだ敗者の rollback が、勝者の直前 CreateDirectory した folder を
        // 巻き込み削除する footgun を物理閉鎖。baseGameFolderCreated と同じパターン (自分が作った
        // disk 状態のみ自分で消す)。
        private bool versionFolderCreatedThisCall;
        // (#234 追加精査) #120「既存フォルダあり」警告でユーザーが OK (= 中身ごと削除して作り直す) を選んだか。
        // CopyGameFolder が親 games/{id}/ を丸ごと削除→再作成する gate。警告を出した時のみ true になる
        // ため、警告未提示の通常追加 / race で後から出現したフォルダを無確認で wipe することはない。
        private bool wipeExistingOnCopy;
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

            // (round 5 Phase D) path textbox を編集可能 (ReadOnly=false) に解放した分、入力時の正規化 +
            // プレビュー連動を hook する。`/` → `\` への正規化は GameFormHelper の共通実装。
            txtThumbnailPath.TextChanged += (s, ev) =>
            {
                GameFormHelper.NormalizeSlashInPathTextBox(txtThumbnailPath);
                UpdateThumbnailPreview();
            };
            txtBackgroundPath.TextChanged += (s, ev) =>
            {
                GameFormHelper.NormalizeSlashInPathTextBox(txtBackgroundPath);
                UpdateBackgroundPreview();
            };
            txtExecutablePath.TextChanged += (s, ev) =>
            {
                GameFormHelper.NormalizeSlashInPathTextBox(txtExecutablePath);
            };

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
            baseGameFolder = gameBaseFolder;

            // (#234 追加精査) #120 で「中身ごと削除して作り直す」を承諾済みなら、バージョンに関わらず
            // 親 games/{id}/ を丸ごと削除して fresh state にする。これにより既存バージョンフォルダとの
            // 衝突 throw + rollback 巻き込み削除 footgun を構造的に排除し、警告文言（OK=全削除）とも一致。
            // worker thread (ProcessingDialog) 内で実行されるため UI freeze しない。削除失敗 (Launcher 等が
            // 使用中) は明示メッセージにして中断する (= 半削除フォルダにコピーを重ねない)。
            //
            // (累積監査 round 4 Medium-11) UI 側 warning の OK 押下から worker thread でこの wipe が走るまでの間に、
            // 他 Manager が同 gameId でゲーム追加 → 完了したケースの保護。DB を再 check して existing game が
            // できていれば wipe を skip + throw して abort。並行 race の確率は低いが本番 LAN 運用想定では non-zero
            // のため fence を入れる。
            if (wipeExistingOnCopy)
            {
                try
                {
                    var existingDbGame = dbManager.GetGameById(gameId);
                    if (existingDbGame != null)
                    {
                        throw new Exception(
                            $"他の Manager が同じゲーム ID '{gameId}' で先にゲームを追加した可能性があります。\n" +
                            "ゲーム追加を中止します。ゲーム ID を変えるか、最新の状態を再読み込みしてから再試行してください。");
                    }
                }
                catch (Exception ex) when (!(ex.Message.StartsWith("他の Manager")))
                {
                    // DB read 自体の失敗は wipe 安全側 (skip) に倒さず、明示 throw でユーザーに判断委ねる。
                    throw new Exception(
                        $"既存ゲームの存在確認に失敗したため wipe を中止しました:\n  {ex.Message}", ex);
                }
            }
            if (wipeExistingOnCopy && Directory.Exists(gameBaseFolder))
            {
                try
                {
                    Directory.Delete(gameBaseFolder, true);
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        $"既存のゲームフォルダの削除に失敗しました（Launcher 等が使用中の可能性があります）:\n  {gameBaseFolder}\n\n{ex.Message}", ex);
                }
            }

            if (!Directory.Exists(gameBaseFolder))
            {
                Directory.CreateDirectory(gameBaseFolder);
                // (#234 追加精査 ②対称化) この追加操作が親フォルダを新規作成したことを記録。
                // 失敗時 rollback で空なら削除するのに使う (既存フォルダで続行した場合は false のまま)。
                baseGameFolderCreated = true;
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
            // (M7) 自分で作った直後に flag を立てる。CreateDirectory より後 (= throw が出る前に立てない) で、
            // 「Directory.Exists 判定で throw した敗者」と「Created 直後の勝者」の状態を rollback で区別可能に。
            versionFolderCreatedThisCall = true;

            // FileOperationServiceに委譲してコピー
            // (追加精査 ②) 個別 File.Copy 失敗を呼び出し側に伝播。1 件でも失敗があれば throw して
            // ProcessingDialog の catch → CleanupCopiedFoldersOnRollback に流す (= DB 未 commit のうちに
            // 物理 rollback)。これで「DB は登録されたが実体が無い」起動不能ゲームの silent 生成を防ぐ。
            // exe / サムネ / 背景の実体存在 check は ProcessingDialog 完了後 (= 上位 btnOK_Click 内) で
            // executableAbsolutePath を計算したあとに行う (= 相対 path 解決ロジックは上位に集約済)。
            var copyFailures = FileOperationService.CopyDirectoryWithProgress(sourceGameFolder, destinationGameFolder, progress, token);
            if (copyFailures.Count > 0)
            {
                string msg = FileOperationService.FormatCopyFailureMessage(copyFailures, sourceGameFolder);
                throw new Exception(msg + "\n\nファイルが他のアプリケーションに開かれていないか、コピー元・コピー先の権限・ディスク容量を確認してください。");
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

            string gameId = txtGameId.Text.Trim();

            // gameId が既存フォルダと衝突している場合の警告 (#120 / #234 追加精査で「wipe & recreate」化)
            // 何らかの原因で games/{gameId}/ が残っている状態での同 gameId 追加。
            // 旧方針は「親フォルダを retain したまま続行」だったが、(a) バージョン衝突時に rollback が
            // 既存版フォルダを巻き込み削除する footgun があり、(b)「自動削除されません」という文言と実装が
            // 矛盾していた。新方針は **OK = フォルダを中身ごと削除して新規作成** に一本化し、文言と実装を
            // 一致させる。大事なデータがあれば警告時点でユーザーが退避する前提 (= ゲーム削除と同じ思想)。
            // ⑪ で「追加失敗時に空フォルダが残らない」ようにしたため、本ケース自体めったに発生しない。
            // 毎クリックで再評価 (= 前回 OK で失敗→フォーム継続→再 OK 時に stale true を持ち越さない)。
            // wipe は下の警告で OK したときのみ true になり、かつ CopyGameFolder で Directory.Exists と AND される。
            wipeExistingOnCopy = false;

            // (Finding #3) M7 の「自分が作った disk 状態だけ自分で消す」flag 群も毎クリックでリセットする。
            // 旧実装は wipeExistingOnCopy しかリセットしておらず、1 回目 OK が CopyGameFolder で
            // baseGameFolderCreated / versionFolderCreatedThisCall / destinationGameFolder を立てた後に失敗
            // → cleanup で folder は消すが flag は stale true のまま残存。2 回目 OK で wipe-check の DB 再 check が
            // 早期 throw (= destinationGameFolder の再代入より前) すると、CleanupCopiedFoldersOnRollback が stale な
            // destinationGameFolder を「自分が作った」と誤認して削除し、並行 Manager の勝者が作成済の version
            // フォルダを巻き込む footgun があった (M7 ガードが stale flag で無効化)。上記コメントの意図
            // (= 持ち越さない) を flag 群全体に適用して構造的に閉じる。CopyGameFolder で必ず再代入されるため
            // 通常経路には影響しない。
            baseGameFolderCreated = false;
            versionFolderCreatedThisCall = false;
            destinationGameFolder = null;
            baseGameFolder = null;

            string existingFolder = PathManager.GetGameFolder(gameId);
            if (Directory.Exists(existingFolder))
            {
                var dr = MessageBox.Show(this,
                    "古いゲームデータが残っています。\n\n" +
                    $"ゲームID '{gameId}' のフォルダが既に存在します:\n  {existingFolder}\n\n" +
                    "「OK」を押すと、このフォルダを中身ごと削除してから新しく作り直します。\n" +
                    "（フォルダ内の古いバージョン・ファイルはすべて削除されます）\n\n" +
                    "失いたくないデータがある場合は「キャンセル」を押し、上記フォルダの中身を\n" +
                    "別の場所に退避してから、もう一度ゲーム追加をやり直してください。",
                    "古いゲームデータの確認",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning);

                if (dr != DialogResult.OK) return;

                // OK = 中身ごと削除して作り直す承諾。CopyGameFolder が wipe する gate を立てる。
                wipeExistingOnCopy = true;
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

                // (Finding #2) コピー時に除外されるフォルダ (Library / node_modules 等) があれば、
                // 一覧を出して続行可否を確認する。除外を silent にしないための fence。Cancel なら中止。
                if (!GameFormHelper.ConfirmExcludedFoldersBeforeCopy(this, sourceGameFolder))
                {
                    return;
                }

                // ProcessingDialog を使用して非同期コピー
                // (累積監査) 他箇所と同様 using で囲み、ハンドル / 内部 CTS のリークを防ぐ
                // (ShowDialog は Dispose しないため明示破棄が必要)。
                using (var processingDialog = new ProcessingDialog((IProgress<ProgressInfo> progress, CancellationToken token) =>
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
                }))
                {

                if (processingDialog.ShowDialog() != DialogResult.OK)
                {
                    // キャンセルまたはエラー時
                    if (processingDialog.DialogResult == DialogResult.Cancel)
                    {
                        MessageBox.Show("処理がキャンセルされました。", "キャンセル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    
                    // コピー失敗時のロールバック（バージョンフォルダ + 新規作成した空の親フォルダを削除）
                    CleanupCopiedFoldersOnRollback();
                    return;
                }
                } // using (processingDialog)

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

                // (追加精査 ②) DB commit 直前に exe / サムネ / 背景の実体存在を最終 check。
                // CopyDirectoryWithProgress は failed list で個別 copy 失敗を伝播済だが、
                // (a) ファイル名の case 違いや (b) コピー後に別プロセスが消した、等の path で
                // DB と実体が乖離するケースを最後の砦として弾く。失敗時は CleanupCopiedFoldersOnRollback
                // と同じ rollback path に流す。
                var missingAssets = new List<string>();
                if (!string.IsNullOrEmpty(executableAbsolutePath) && !File.Exists(executableAbsolutePath))
                    missingAssets.Add("実行ファイル: " + executableAbsolutePath);
                if (!string.IsNullOrEmpty(thumbnailAbsolutePath) && !File.Exists(thumbnailAbsolutePath))
                    missingAssets.Add("サムネイル: " + thumbnailAbsolutePath);
                if (!string.IsNullOrEmpty(backgroundAbsolutePath) && !File.Exists(backgroundAbsolutePath))
                    missingAssets.Add("背景画像: " + backgroundAbsolutePath);
                if (missingAssets.Count > 0)
                {
                    CleanupCopiedFoldersOnRollback();
                    MessageBox.Show(
                        "コピー後のファイルが見つかりません。ゲーム追加を中止しました:\n\n  " +
                        string.Join("\n  ", missingAssets) +
                        "\n\nコピー元のパス指定 / 権限 / ディスク容量を確認のうえ再試行してください。",
                        "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

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
                    DisplayOrder = null, // (累積監査 round 4 Medium-10) AddGameAtTop が atomic に MIN-1 を採番
                    IsVisible = true, // 新規追加のゲームは常にランチャーに表示
                    Controls = null, // 後で実装
                    KeyMapping = null, // 後で実装
                    Version = version // (#158, round 3 L-2: line 282 でキャプチャ済の local を使い回し)
                };

                // ジャンルを処理
                game.Genre = GameFormHelper.GetSelectedGenres(clbGenre);

                // 製作者情報を設定
                game.Developers = developers;

                // 初期バージョン情報を作成
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

                // データベースに追加 (累積監査 round 6 High-1) games 行 (display_order の MIN-1 採番込み) と
                // 初期版 INSERT を 1 transaction で atomic に実行する。旧実装は AddGameAtTop と AddGameVersion を
                // 別 transaction で順次実行しており、前者 commit 直後の電源断 / SMB disconnect で「games 行はあるが
                // 版なし」の起動不能孤児ゲームが残る partial-commit 窓があった。両 INSERT を統合して窓を物理閉鎖。
                dbManager.AddGameAtTopWithInitialVersion(game, initialVersion);
                gameAdded = true;

                AddedGame = game;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (System.Data.SQLite.SQLiteException ex)
            {
                // エラー時のロールバック（バージョンフォルダ + 新規作成した空の親フォルダ）
                CleanupCopiedFoldersOnRollback();

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
                // エラー時のロールバック（バージョンフォルダ + 新規作成した空の親フォルダ）
                CleanupCopiedFoldersOnRollback();

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
                // (累積監査 round 4 Medium-12) zombie 状態 (= 物理ファイルは消えたが DB に games 行のみ残留 →
                // 次回起動時に「版なし孤児ゲーム」として表示 + 同 gameId 再追加が重複 check で永久 block) を user に
                // 伝える。手動 SQL での修復が必要なケースを明示通知し、復旧 SQL 文も Logger に残す。
                string repairSql =
                    "DELETE FROM developers WHERE game_id='" + gameId + "';\n" +
                    "DELETE FROM game_versions WHERE game_id='" + gameId + "';\n" +
                    "DELETE FROM games WHERE game_id='" + gameId + "';";
                Logger.Warn("[AddGameForm] (#234 ②/Medium-12) games 行 rollback 削除失敗: " + gameId + ": " + delEx.Message);
                Logger.Warn("[AddGameForm] (Medium-12) zombie 復旧 SQL (sqlite3 toneprism.db で実行): \n" + repairSql);
                try
                {
                    MessageBox.Show(this,
                        $"ゲーム '{gameId}' の rollback 削除に失敗しました。\n\n" +
                        $"{delEx.Message}\n\n" +
                        "ファイルは削除されましたが、データベース上のゲーム情報は残っています。\n" +
                        "次回 Manager 起動時に「版なしのゲーム」として表示される可能性があり、\n" +
                        "同じゲーム ID では再追加できなくなります。\n\n" +
                        "対処方法:\n" +
                        "  ① 別のゲーム ID を使って再追加する (推奨、データ損失なし)\n" +
                        "  ② Manager の「ゲーム削除」で zombie ゲームを削除してから再追加する\n" +
                        "  ③ 上記が両方失敗する場合のみ手動で DB を修復 (修復 SQL は Manager.log 参照)",
                        "rollback 失敗 (zombie ゲーム残留)",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch { /* MessageBox 失敗は無視 */ }
            }
        }

        /// <summary>
        /// (#234 追加精査 ②対称化) rollback 時の disk 後始末。バージョンフォルダ (destinationGameFolder) を
        /// 削除し、さらに「この追加操作で新規作成した」親フォルダ games/{id}/ が空になっていれば削除する。
        /// SPEC §3.8.5 が「別 issue 候補」としていた parentCreatedThisCall flag 方式の実装。
        ///   - baseGameFolderCreated=false (= 追加前から親が存在 = #120「既存フォルダで続行」経路) では親に
        ///     一切触らない (他 version 共存ケースの保護)。
        ///   - 削除前に必ず空チェックを挟むため、万一中身が残っていても消さない (誤削除防止の二重防御)。
        /// 削除失敗は握り潰す (元の例外通知を優先)。
        /// </summary>
        private void CleanupCopiedFoldersOnRollback()
        {
            // (M7) versionFolderCreatedThisCall=true (= 自分が CreateDirectory した version folder) のときだけ削除。
            // 並行 Manager race で「既に存在する」throw を踏んだ敗者は flag=false のままで、勝者の folder には
            // 触らない。baseGameFolderCreated と同じパターン。
            if (versionFolderCreatedThisCall && !string.IsNullOrEmpty(destinationGameFolder) && Directory.Exists(destinationGameFolder))
            {
                try { Directory.Delete(destinationGameFolder, true); } catch { }
            }

            if (baseGameFolderCreated && !string.IsNullOrEmpty(baseGameFolder) && Directory.Exists(baseGameFolder))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(baseGameFolder).Any())
                    {
                        Directory.Delete(baseGameFolder, false);
                    }
                }
                catch { }
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

            // (#206) ゲームID 検証: 理由別文言 (空 / 64文字超 / 文字種 / Windows 予約名 CON/PRN/NUL/COM1 等) を
            // 区別して表示する。bool-only overload + ハードコード文言だと「文字種が悪い」一択で誤誘導していた。
            if (!GameFormHelper.IsValidGameId(txtGameId.Text, out string gameIdError))
            {
                MessageBox.Show(gameIdError, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

            // (#234 追加精査) コピー元に games/ 配下 (= Manager 管理下のゲーム本体・版フォルダ) を選ぶ
            // 誤操作を境界で弾く (VersionUpForm と共通)。新ビルドは必ず games/ の外から取り込む。
            if (!GameFormHelper.ValidateSourceNotInGamesFolder(gameFolderPath, out string srcInGamesError))
            {
                MessageBox.Show(srcInGamesError, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnSelectGameFolder.Focus();
                return false;
            }

            // sourceGameFolderを更新
            sourceGameFolder = gameFolderPath;

            // (round 5 Phase D) ReadOnly 解除に伴う validation 強化: 拡張子 + 存在 check を共通 helper 経由に統一。
            // 実行ファイル (必須、.exe)
            if (!GameFormHelper.ValidateFilePath(txtExecutablePath.Text, sourceGameFolder,
                GameFormHelper.ExecutableFileExtensions, true, "実行ファイル", out string exeErr))
            {
                MessageBox.Show(exeErr, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtExecutablePath.Focus();
                return false;
            }

            // 実行ファイルが選択されたフォルダ内にあるか確認（ゲームフォルダが選択されている場合のみ）
            if (!string.IsNullOrEmpty(sourceGameFolder) &&
                !PathConversionHelper.IsPathInside(sourceGameFolder, txtExecutablePath.Text))
            {
                MessageBox.Show("実行ファイルは選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtExecutablePath.Focus();
                return false;
            }

            // サムネイル画像 (任意、画像拡張子)
            if (!GameFormHelper.ValidateFilePath(txtThumbnailPath.Text, sourceGameFolder,
                GameFormHelper.ImageFileExtensions, false, "サムネイル画像", out string thumbErr))
            {
                MessageBox.Show(thumbErr, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtThumbnailPath.Focus();
                return false;
            }
            if (!string.IsNullOrWhiteSpace(txtThumbnailPath.Text) &&
                !string.IsNullOrEmpty(sourceGameFolder) &&
                !PathConversionHelper.IsPathInside(sourceGameFolder, txtThumbnailPath.Text))
            {
                MessageBox.Show("サムネイル画像は選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtThumbnailPath.Focus();
                return false;
            }

            // 背景画像 (任意、画像拡張子)
            if (!GameFormHelper.ValidateFilePath(txtBackgroundPath.Text, sourceGameFolder,
                GameFormHelper.ImageFileExtensions, false, "背景画像", out string bgErr))
            {
                MessageBox.Show(bgErr, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtBackgroundPath.Focus();
                return false;
            }
            if (!string.IsNullOrWhiteSpace(txtBackgroundPath.Text) &&
                !string.IsNullOrEmpty(sourceGameFolder) &&
                !PathConversionHelper.IsPathInside(sourceGameFolder, txtBackgroundPath.Text))
            {
                MessageBox.Show("背景画像は選択されたゲームフォルダ内のファイルを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtBackgroundPath.Focus();
                return false;
            }

            // (#234 追加精査) 最小プレイ人数 ≤ 最大プレイ人数 を検証 (3 フォーム共通 helper)。
            if (!GameFormHelper.ValidatePlayerCount((int)numMinPlayers.Value, (int)numMaxPlayers.Value, out string playerCountError))
            {
                MessageBox.Show(playerCountError, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                numMinPlayers.Focus();
                return false;
            }

            // ゲームIDの重複チェック
            // (#206 follow-up) Edit 経路 (GameRepository.UpdateGameId) と文言統一: 具体 ID + 案内。
            var existingGame = dbManager.GetGameById(txtGameId.Text.Trim());
            if (existingGame != null)
            {
                MessageBox.Show($"ゲームID「{txtGameId.Text.Trim()}」は既に使用されています。別のIDを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

