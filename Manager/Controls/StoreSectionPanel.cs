using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Windows.Forms;
using TonePrism.Manager.Models;

namespace TonePrism.Manager.Controls
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

            // (NRE 対策) `dgvSections.DataSource = null; = _sections;` 直後の transient な
            // auto-generated column 状態で、列のプロパティ setter (`Visible` / `HeaderText` / `Width`)
            // が WinForms 内部で NRE を起こす経路がある (=本 method 内で stack frame が止まる現象、
            // 復元 → DatabaseChanged → 全 panel reload の流れで観測)。`SuspendLayout` で内部レイアウト
            // 更新を抑止し、全列を確定してから一度に `ResumeLayout` する形に変える + 個別 column アクセスは
            // null チェック付き helper に集約することで、WinForms 内部の race / null deref に届く前に
            // 設定を完了させる。GameSectionPanel.ConfigureDataGridView の防御 pattern と趣旨は同じ
            // (= 明示列挙 + null チェック + 局所 helper)。
            dgvSections.SuspendLayout();
            try
            {
                // 全列を一旦非表示。foreach 中に DataGridView 側で列コレクションが変動しないよう
                // ToList() でスナップショット経由にする (= enumerator 維持 + col 参照保持の二重防御)。
                foreach (var col in dgvSections.Columns.Cast<DataGridViewColumn>().ToList())
                {
                    if (col == null) continue;
                    col.Visible = false;
                }

                // DisplayOrder / 内部用 property は非表示のまま、表示したいものだけ helper で復帰させる。
                ConfigureColumn("Title", "タイトル", 150);
                ConfigureColumn("SectionTypeDisplay", "タイプ", 110);
                ConfigureColumn("SectionSourceDisplay", "ソース", 120);
                ConfigureColumn("MaxDisplayCount", "表示数", 60);
                ConfigureColumn("IsVisible", "表示", 50);

                dgvSections.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            }
            finally
            {
                dgvSections.ResumeLayout(true);
            }
        }

        /// <summary>
        /// (NRE 対策) 名前指定 column アクセスの defensive helper。`Contains` + 二重 null チェックで、
        /// auto-generated 直後の transient state で `Columns["name"]` が unexpected に null を返す経路や、
        /// 列が存在しないバインドソースで設定処理が落ちる経路を構造的に封じる。設定対象は Visible / HeaderText /
        /// Width の 3 件に固定 (=旧コードの per-column block と等価)。
        /// </summary>
        private void ConfigureColumn(string name, string headerText, int width)
        {
            if (!dgvSections.Columns.Contains(name)) return;
            var col = dgvSections.Columns[name];
            if (col == null) return;
            col.Visible = true;
            col.HeaderText = headerText;
            col.Width = width;
        }

        private StoreSectionInfo GetSelectedSection()
        {
            if (dgvSections.CurrentRow == null || dgvSections.CurrentRow.Index < 0) return null;
            if (_sections == null || dgvSections.CurrentRow.Index >= _sections.Count) return null;
            return _sections[dgvSections.CurrentRow.Index];
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ストアセクション追加") == DialogResult.Cancel) return;
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
            // (round 2 High-2) selection 依存 validation を session conflict check より前に倒す
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
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ストアセクション編集") == DialogResult.Cancel) return;

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
            // (round 2 High-2) selection 依存 validation を session conflict check より前に倒す
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
                // (round 2 High-2) user 確認後、DB write 直前で session conflict check
                if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ストアセクション削除") == DialogResult.Cancel) return;
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
            // (round 2 High-2) selection 依存 validation は MoveSection 内、session conflict check は
            // MoveSection の write 直前で実行する形に集約
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

            // (round 2 High-2) selection / range validation 通過後、DB write 直前で session conflict check
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ストアセクション並び替え") == DialogResult.Cancel) return;

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
