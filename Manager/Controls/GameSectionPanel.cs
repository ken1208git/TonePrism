using System;
using System.Linq;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Controls
{
    public partial class GameSectionPanel : UserControl
    {
        private DatabaseManager _dbManager;
        // (#245 ゲーム画面 WPF 化 ①) version-up / delete の重ロジックは service に抽出済 (UI は薄く)。
        private GameVersionUpService _versionUpService;
        private GameDeletionService _deletionService;

        public event Action<string> StatusChanged;

        public int GameCount => dgvGames.Rows.Count;

        public GameSectionPanel()
        {
            InitializeComponent();
        }

        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            _versionUpService = new GameVersionUpService(dbManager);
            _deletionService = new GameDeletionService(dbManager);
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
                    // (#295 round3 #3) 成果の確認 (MessageBox) を先に見せてから、後追い best-effort でバックアップ
                    // (版up/削除と順序統一)。ゲーム追加は games/ を変えるので DB + ゲーム本体を控える (replace-in-session)。
                    _dbManager.SessionBackupCoordinator.RunAfterOperation(this.FindForm(), assetsChanged: true, "ゲーム追加");
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
                    // (#295 round3 #3) 成果の確認を先に (版up/削除と順序統一)。ゲーム本体を変えた編集 (id rename /
                    // 版削除 / 外部画像取込) のときだけアセットも控える。メタデータのみは DB だけ (form.AssetsChangedOnDisk)。
                    _dbManager.SessionBackupCoordinator.RunAfterOperation(this.FindForm(), form.AssetsChangedOnDisk, "ゲーム編集");
                }
                else if (form.DataChangedOutsideOk)
                {
                    // (#209 review finding 1) バージョン即時削除は OK を介さず DB 確定するため、Cancel/×で閉じても
                    // グリッドを再読込する。怠ると active 版付け替え後にメイン画面が削除済み版を出し続ける (stale)。
                    LoadGames();
                    // (#295) 版即時削除は games/{id}/v{} を消す = ゲーム本体が変わるのでアセットも控える。
                    _dbManager.SessionBackupCoordinator.RunAfterOperation(this.FindForm(), form.AssetsChangedOnDisk, "バージョン削除");
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

            // 重い処理 (コピー / atomic move / DB / アクティブ化 / バックアップ) は GameVersionUpService に委譲。
            _versionUpService.Run(this.FindForm(), game, LoadGames);
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

            // rename-rollback の多段削除フロー (退避 → DB 削除 → 物理削除) は GameDeletionService に委譲。
            _deletionService.Run(this.FindForm(), selectedGame, LoadGames);
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
