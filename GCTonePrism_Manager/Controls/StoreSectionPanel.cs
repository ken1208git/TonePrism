using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Controls
{
    public partial class StoreSectionPanel : UserControl
    {
        private DatabaseManager _dbManager;
        private List<StoreSectionInfo> _sections;

        public StoreSectionPanel()
        {
            InitializeComponent();
        }

        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        public void LoadSections()
        {
            if (_dbManager == null) return;

            try
            {
                _sections = _dbManager.GetAllSections();
                dgvSections.DataSource = null;
                dgvSections.DataSource = _sections;
                ConfigureDataGridView();
                dgvSections.ClearSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show("セクション一覧の読み込みに失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ConfigureDataGridView()
        {
            if (dgvSections.Columns.Count == 0) return;

            foreach (DataGridViewColumn col in dgvSections.Columns)
            {
                col.Visible = false;
            }

            // DisplayOrderは非表示（表の並び順がそのまま順序）
            if (dgvSections.Columns.Contains("Title"))
            {
                dgvSections.Columns["Title"].Visible = true;
                dgvSections.Columns["Title"].HeaderText = "タイトル";
                dgvSections.Columns["Title"].Width = 150;
            }
            if (dgvSections.Columns.Contains("SectionTypeDisplay"))
            {
                dgvSections.Columns["SectionTypeDisplay"].Visible = true;
                dgvSections.Columns["SectionTypeDisplay"].HeaderText = "タイプ";
                dgvSections.Columns["SectionTypeDisplay"].Width = 110;
            }
            if (dgvSections.Columns.Contains("SectionSourceDisplay"))
            {
                dgvSections.Columns["SectionSourceDisplay"].Visible = true;
                dgvSections.Columns["SectionSourceDisplay"].HeaderText = "ソース";
                dgvSections.Columns["SectionSourceDisplay"].Width = 120;
            }
            if (dgvSections.Columns.Contains("MaxDisplayCount"))
            {
                dgvSections.Columns["MaxDisplayCount"].Visible = true;
                dgvSections.Columns["MaxDisplayCount"].HeaderText = "表示数";
                dgvSections.Columns["MaxDisplayCount"].Width = 60;
            }
            if (dgvSections.Columns.Contains("IsVisible"))
            {
                dgvSections.Columns["IsVisible"].Visible = true;
                dgvSections.Columns["IsVisible"].HeaderText = "表示";
                dgvSections.Columns["IsVisible"].Width = 50;
            }

            dgvSections.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        private StoreSectionInfo GetSelectedSection()
        {
            if (dgvSections.CurrentRow == null || dgvSections.CurrentRow.Index < 0) return null;
            if (_sections == null || dgvSections.CurrentRow.Index >= _sections.Count) return null;
            return _sections[dgvSections.CurrentRow.Index];
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            using (var form = new StoreSectionForm(_dbManager))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    LoadSections();
                }
            }
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            var section = GetSelectedSection();
            if (section == null)
            {
                MessageBox.Show("編集するセクションを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var latest = _dbManager.GetSectionById(section.SectionId);
            if (latest == null)
            {
                MessageBox.Show("セクションが見つかりません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (var form = new StoreSectionForm(_dbManager, latest))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    LoadSections();
                }
            }
        }

        private void btnDelete_Click(object sender, EventArgs e)
        {
            var section = GetSelectedSection();
            if (section == null)
            {
                MessageBox.Show("削除するセクションを選択してください。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"セクション「{section.Title}」を削除しますか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                try
                {
                    _dbManager.DeleteSection(section.SectionId);
                    LoadSections();
                }
                catch (SQLiteException ex)
                {
                    MessageBox.Show(DatabaseManager.GetUserFriendlyErrorMessage(ex),
                        "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnMoveUp_Click(object sender, EventArgs e)
        {
            MoveSection(-1);
        }

        private void btnMoveDown_Click(object sender, EventArgs e)
        {
            MoveSection(1);
        }

        private void MoveSection(int direction)
        {
            if (_sections == null) return;
            int idx = dgvSections.CurrentRow?.Index ?? -1;
            if (idx < 0) return;

            int newIdx = idx + direction;
            if (newIdx < 0 || newIdx >= _sections.Count) return;

            var sectionA = _sections[idx];
            var sectionB = _sections[newIdx];
            int tempOrder = sectionA.DisplayOrder;
            sectionA.DisplayOrder = sectionB.DisplayOrder;
            sectionB.DisplayOrder = tempOrder;

            try
            {
                _dbManager.UpdateSection(sectionA);
                _dbManager.UpdateSection(sectionB);
                LoadSections();

                if (newIdx < dgvSections.Rows.Count)
                {
                    foreach (DataGridViewCell cell in dgvSections.Rows[newIdx].Cells)
                    {
                        if (cell.Visible)
                        {
                            dgvSections.CurrentCell = cell;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("並び替えに失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dgvSections_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                btnEdit_Click(sender, e);
            }
        }
    }
}
