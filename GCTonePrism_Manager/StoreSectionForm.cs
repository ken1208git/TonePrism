using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// ストアセクション編集フォーム
    /// </summary>
    public partial class StoreSectionForm : Form
    {
        private readonly DatabaseManager _dbManager;
        private readonly StoreSectionInfo _section;
        private readonly bool _isNew;
        // ゲームID → 表示テキストの一時保持
        private readonly Dictionary<string, string> _displayTexts = new Dictionary<string, string>();
        private bool _suppressDisplayTextEvent = false;

        public StoreSectionForm(DatabaseManager dbManager, StoreSectionInfo section = null)
        {
            InitializeComponent();
            _dbManager = dbManager;

            if (section != null)
            {
                _section = section;
                _isNew = false;
            }
            else
            {
                _section = new StoreSectionInfo
                {
                    DisplayOrder = _dbManager.GetMaxSectionDisplayOrder() + 1
                };
                _isNew = true;
            }
        }

        private void StoreSectionForm_Load(object sender, EventArgs e)
        {
            // セクションタイプ
            cmbSectionType.Items.AddRange(new object[]
            {
                "通常カテゴリ行",
                "スライドショー",
                "タイルグリッド"
            });

            // セクションソース
            cmbSectionSource.Items.AddRange(new object[]
            {
                "手動",
                "人気ランキング",
                "新作",
                "最近プレイ",
                "ジャンル指定",
                "プレイ人数(以上)",
                "プレイ人数(以下)",
                "難易度",
                "プレイ時間",
                "通信プレイ",
                "ランダム",
                "コントローラー"
            });

            // ジャンルComboBox
            foreach (var genre in GenreList.AvailableGenres)
            {
                cmbGenre.Items.Add(genre);
            }

            // 値を設定
            txtTitle.Text = _section.Title ?? "";
            cmbSectionType.SelectedIndex = _section.SectionType;
            nudMaxDisplayCount.Value = _section.MaxDisplayCount;
            chkIsVisible.Checked = _section.IsVisible;

            // ソースの設定
            SetSourceControls(_section.SectionSource ?? "manual");

            // ゲーム一覧を読み込み
            LoadGameLists();

            // 既存のdisplay_textを読み込み
            if (_section.GameDisplayTexts != null)
            {
                foreach (var kvp in _section.GameDisplayTexts)
                {
                    _displayTexts[kvp.Key] = kvp.Value;
                }
            }

            // ソース変更時のイベント
            cmbSectionSource.SelectedIndexChanged += CmbSectionSource_SelectedIndexChanged;
            // タイプ変更時のイベント
            cmbSectionType.SelectedIndexChanged += CmbSectionType_SelectedIndexChanged;
            // 割当済みリスト選択変更時
            lstAssigned.SelectedIndexChanged += LstAssigned_SelectedIndexChanged;

            UpdateSourceParameterVisibility();
            UpdateGameListVisibility();
        }

        private void SetSourceControls(string source)
        {
            if (source == "manual") cmbSectionSource.SelectedIndex = 0;
            else if (source == "popular") cmbSectionSource.SelectedIndex = 1;
            else if (source == "recent") cmbSectionSource.SelectedIndex = 2;
            else if (source == "recently_played") cmbSectionSource.SelectedIndex = 3;
            else if (source.StartsWith("genre:"))
            {
                cmbSectionSource.SelectedIndex = 4;
                string genreName = source.Substring(6);
                int idx = cmbGenre.Items.IndexOf(genreName);
                if (idx >= 0) cmbGenre.SelectedIndex = idx;
            }
            else if (source.StartsWith("players_min:"))
            {
                cmbSectionSource.SelectedIndex = 5;
                if (int.TryParse(source.Substring(12), out int val))
                    nudSourceValue.Value = val;
            }
            else if (source.StartsWith("players_max:"))
            {
                cmbSectionSource.SelectedIndex = 6;
                if (int.TryParse(source.Substring(12), out int val))
                    nudSourceValue.Value = val;
            }
            else if (source.StartsWith("difficulty:"))
            {
                cmbSectionSource.SelectedIndex = 7;
                if (int.TryParse(source.Substring(11), out int val))
                    nudSourceValue.Value = val;
            }
            else if (source.StartsWith("play_time:"))
            {
                cmbSectionSource.SelectedIndex = 8;
                if (int.TryParse(source.Substring(10), out int val))
                    nudSourceValue.Value = val;
            }
            else if (source == "online") cmbSectionSource.SelectedIndex = 9;
            else if (source == "random") cmbSectionSource.SelectedIndex = 10;
            else if (source == "controller") cmbSectionSource.SelectedIndex = 11;
            else cmbSectionSource.SelectedIndex = 0;
        }

        private string GetSourceString()
        {
            int idx = cmbSectionSource.SelectedIndex;
            switch (idx)
            {
                case 0: return "manual";
                case 1: return "popular";
                case 2: return "recent";
                case 3: return "recently_played";
                case 4:
                    string genre = cmbGenre.SelectedItem?.ToString() ?? "";
                    return string.IsNullOrEmpty(genre) ? "manual" : "genre:" + genre;
                case 5: return "players_min:" + (int)nudSourceValue.Value;
                case 6: return "players_max:" + (int)nudSourceValue.Value;
                case 7: return "difficulty:" + (int)nudSourceValue.Value;
                case 8: return "play_time:" + (int)nudSourceValue.Value;
                case 9: return "online";
                case 10: return "random";
                case 11: return "controller";
                default: return "manual";
            }
        }

        private void CmbSectionSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateSourceParameterVisibility();
            UpdateGameListVisibility();
        }

        private void CmbSectionType_SelectedIndexChanged(object sender, EventArgs e)
        {
            // タイルグリッドに変更された場合、割当済みが3件超えていたら警告
            if (cmbSectionType.SelectedIndex == 2 && lstAssigned.Items.Count > 3)
            {
                MessageBox.Show(
                    "タイルグリッドは最大3件までしか表示できません。\n" +
                    "現在 " + lstAssigned.Items.Count + " 件割り当てられています。\n" +
                    "4件目以降は表示されません。複数行にしたい場合はセクションを分けてください。",
                    "注意", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void UpdateSourceParameterVisibility()
        {
            int idx = cmbSectionSource.SelectedIndex;
            // ジャンル選択: idx=4
            lblGenre.Visible = cmbGenre.Visible = (idx == 4);
            // 数値入力: idx=5,6,7,8
            lblSourceValue.Visible = nudSourceValue.Visible = (idx >= 5 && idx <= 8);

            // ラベル更新
            if (idx == 5 || idx == 6) lblSourceValue.Text = "人数:";
            else if (idx == 7) lblSourceValue.Text = "難易度:";
            else if (idx == 8) lblSourceValue.Text = "プレイ時間:";
        }

        private void UpdateGameListVisibility()
        {
            bool isManual = cmbSectionSource.SelectedIndex == 0;
            grpGameAssignment.Visible = isManual;
        }

        private void LoadGameLists()
        {
            lstAvailable.Items.Clear();
            lstAssigned.Items.Clear();

            var allGames = _dbManager.GetAllGames();
            var assignedGameIds = _section.Games?.Select(g => g.GameId).ToHashSet() ?? new HashSet<string>();

            // 割当済みゲーム（順序保持）
            if (_section.Games != null)
            {
                foreach (var game in _section.Games)
                {
                    lstAssigned.Items.Add(new GameListItem(game));
                }
            }

            // 未割当ゲーム
            foreach (var game in allGames)
            {
                if (!assignedGameIds.Contains(game.GameId))
                {
                    lstAvailable.Items.Add(new GameListItem(game));
                }
            }
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            // タイルグリッドの場合、3件制限チェック
            if (cmbSectionType.SelectedIndex == 2)
            {
                int selectedCount = lstAvailable.SelectedItems.Count;
                int currentCount = lstAssigned.Items.Count;
                if (currentCount + selectedCount > 3)
                {
                    MessageBox.Show(
                        "タイルグリッドは最大3件までです。\n" +
                        "4件以上表示したい場合はセクションを複数作成してください。",
                        "追加できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            MoveSelectedItems(lstAvailable, lstAssigned);
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            MoveSelectedItems(lstAssigned, lstAvailable);
        }

        private void btnUp_Click(object sender, EventArgs e)
        {
            MoveItem(lstAssigned, -1);
        }

        private void btnDown_Click(object sender, EventArgs e)
        {
            MoveItem(lstAssigned, 1);
        }

        private void MoveSelectedItems(ListBox from, ListBox to)
        {
            var selected = from.SelectedItems.Cast<GameListItem>().ToList();
            foreach (var item in selected)
            {
                from.Items.Remove(item);
                to.Items.Add(item);
            }
        }

        private void MoveItem(ListBox list, int direction)
        {
            int idx = list.SelectedIndex;
            if (idx < 0) return;

            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= list.Items.Count) return;

            var item = list.Items[idx];
            list.Items.RemoveAt(idx);
            list.Items.Insert(newIdx, item);
            list.SelectedIndex = newIdx;
        }

        private void LstAssigned_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = lstAssigned.SelectedItem as GameListItem;
            _suppressDisplayTextEvent = true;
            if (selected != null)
            {
                string gameId = selected.Game.GameId;
                txtDisplayText.Text = _displayTexts.ContainsKey(gameId) ? _displayTexts[gameId] : "";
                txtDisplayText.Enabled = true;
            }
            else
            {
                txtDisplayText.Text = "";
                txtDisplayText.Enabled = false;
            }
            _suppressDisplayTextEvent = false;
        }

        private void txtDisplayText_TextChanged(object sender, EventArgs e)
        {
            if (_suppressDisplayTextEvent) return;
            var selected = lstAssigned.SelectedItem as GameListItem;
            if (selected != null)
            {
                _displayTexts[selected.Game.GameId] = txtDisplayText.Text;
            }
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtTitle.Text))
            {
                MessageBox.Show("タイトルを入力してください。", "入力エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _section.Title = txtTitle.Text.Trim();
            _section.SectionType = cmbSectionType.SelectedIndex;
            _section.SectionSource = GetSourceString();
            _section.MaxDisplayCount = (int)nudMaxDisplayCount.Value;
            _section.IsVisible = chkIsVisible.Checked;

            // 割当済みゲーム
            _section.Games = lstAssigned.Items.Cast<GameListItem>()
                .Select(item => item.Game).ToList();
            _section.GameDisplayTexts = new Dictionary<string, string>(_displayTexts);

            try
            {
                if (_isNew)
                {
                    _dbManager.AddSection(_section);
                }
                else
                {
                    _dbManager.UpdateSection(_section);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存に失敗しました: " + ex.Message, "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// ListBoxの表示用ラッパー
        /// </summary>
        private class GameListItem
        {
            public GameInfo Game { get; }

            public GameListItem(GameInfo game)
            {
                Game = game;
            }

            public override string ToString()
            {
                return Game.Title ?? Game.GameId;
            }
        }
    }
}
