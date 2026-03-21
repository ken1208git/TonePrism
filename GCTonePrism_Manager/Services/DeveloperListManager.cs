using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// 製作者リストのDataGridView管理を共通化するヘルパー。
    /// AddGameForm / EditGameForm で同一の製作者CRUD UIを提供する。
    /// </summary>
    public class DeveloperListManager
    {
        private readonly DataGridView _grid;
        private readonly List<DeveloperInfo> _developers;

        public DeveloperListManager(DataGridView grid, List<DeveloperInfo> developers)
        {
            _grid = grid;
            _developers = developers;
        }

        /// <summary>
        /// DataGridViewのカラム設定とデータバインド
        /// </summary>
        public void InitializeGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.ReadOnly = true;

            _grid.Columns.Clear();
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "LastName",
                HeaderText = "姓",
                DataPropertyName = "LastName",
                Width = 100
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "FirstName",
                HeaderText = "名",
                DataPropertyName = "FirstName",
                Width = 100
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "GradeDisplay",
                HeaderText = "期生",
                DataPropertyName = "GradeDisplay",
                Width = 60
            });

            _grid.DataSource = _developers;
        }

        /// <summary>
        /// DataGridViewを再バインドして表示を更新
        /// </summary>
        public void Refresh()
        {
            _grid.DataSource = null;
            _grid.DataSource = _developers;
        }

        /// <summary>
        /// 製作者追加ダイアログを表示し、リストに追加
        /// </summary>
        public void Add()
        {
            using (var form = new DeveloperForm())
            {
                if (form.ShowDialog() == DialogResult.OK && form.Developer != null)
                {
                    _developers.Add(form.Developer);
                    Refresh();
                }
            }
        }

        /// <summary>
        /// 選択中の製作者を編集ダイアログで編集
        /// </summary>
        public void Edit()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("編集する製作者を選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedDeveloper = _grid.SelectedRows[0].DataBoundItem as DeveloperInfo;
            if (selectedDeveloper == null) return;

            using (var form = new DeveloperForm(selectedDeveloper))
            {
                if (form.ShowDialog() == DialogResult.OK && form.Developer != null)
                {
                    int index = _developers.IndexOf(selectedDeveloper);
                    if (index >= 0)
                    {
                        _developers[index] = form.Developer;
                        Refresh();
                    }
                }
            }
        }

        /// <summary>
        /// 選択中の製作者を確認後に削除
        /// </summary>
        public void Delete()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("削除する製作者を選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selectedDeveloper = _grid.SelectedRows[0].DataBoundItem as DeveloperInfo;
            if (selectedDeveloper == null) return;

            var result = MessageBox.Show(
                $"製作者「{selectedDeveloper.FullName}」を削除しますか？",
                "削除確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _developers.Remove(selectedDeveloper);
                Refresh();
            }
        }
    }
}
