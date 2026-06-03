using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Controls
{
    /// <summary>
    /// (#253) イントロガイドのスライド一覧パネル (`StoreSectionPanel` をミラー、UserControl・コード組み)。
    /// スライドの 追加 / 編集 / 削除 / 並び替え を行う。実際の 1 枚編集は `IntroSlideEditForm`。
    /// DB write 前に必ず `SessionConflictHelper.CheckBeforeWrite` を挟む (他 PC/Launcher 競合検出)。
    /// </summary>
    public class IntroGuidePanel : UserControl
    {
        private DatabaseManager _dbManager;
        private List<IntroSlide> _slides;
        private DataGridView _grid;

        public IntroGuidePanel()
        {
            BuildUi();
        }

        /// <summary>MainForm から DatabaseManager を注入 (StoreSectionPanel.Initialize と対称)。</summary>
        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
        }

        private void BuildUi()
        {
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 6) };
            AddButton(toolbar, "追加", (s, e) => OnAdd());
            AddButton(toolbar, "編集", (s, e) => OnEdit());
            AddButton(toolbar, "削除", (s, e) => OnDelete());
            AddButton(toolbar, "↑ 上へ", (s, e) => MoveSlide(-1));
            AddButton(toolbar, "↓ 下へ", (s, e) => MoveSlide(+1));
            AddButton(toolbar, "再読み込み", (s, e) => LoadSlides());

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoGenerateColumns = true,
                RowHeadersVisible = false,
            };
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OnEdit(); };

            Controls.Add(_grid);
            Controls.Add(toolbar);
        }

        private void AddButton(Control parent, string text, EventHandler handler)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };
            b.Click += handler;
            parent.Controls.Add(b);
        }

        /// <summary>スライド一覧を DB から再読込して grid に表示。</summary>
        public void LoadSlides()
        {
            if (_dbManager == null) return;
            try
            {
                _slides = _dbManager.GetAllIntroSlides();
                _grid.DataSource = null;
                _grid.DataSource = _slides;
                ConfigureColumns();
                _grid.ClearSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show("初回説明一覧の読み込みに失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ConfigureColumns()
        {
            _grid.SuspendLayout();
            foreach (DataGridViewColumn col in _grid.Columns)
            {
                col.Visible = false;
            }
            ConfigureColumn("DisplayOrder", "順", 50);
            ConfigureColumn("BodyText", "本文", 300);
            ConfigureColumn("ImagePath", "画像", 200);
            ConfigureColumn("IsVisible", "表示", 50);
            _grid.ResumeLayout();
        }

        private void ConfigureColumn(string name, string header, int width)
        {
            if (!_grid.Columns.Contains(name)) return;
            var col = _grid.Columns[name];
            if (col == null) return;
            col.Visible = true;
            col.HeaderText = header;
            col.Width = width;
        }

        private IntroSlide Selected()
        {
            return _grid.CurrentRow?.DataBoundItem as IntroSlide;
        }

        private void OnAdd()
        {
            if (_dbManager == null) return;
            if (SessionConflictHelper.CheckBeforeWrite(this, "初回説明 スライド追加") == DialogResult.Cancel) return;
            using (var form = new IntroSlideEditForm(_dbManager))
            {
                if (form.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadSlides();
                    // (#295) guide/ に新規画像を取り込んだときだけアセットも控える (本文のみは DB だけ)。
                    _dbManager.SessionBackupCoordinator.RunAfterOperation(FindForm(), form.AssetsChangedOnDisk, "スライド追加");
                }
            }
        }

        private void OnEdit()
        {
            if (_dbManager == null) return;
            var slide = Selected();
            if (slide == null)
            {
                MessageBox.Show("編集するスライドを選択してください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (SessionConflictHelper.CheckBeforeWrite(this, "初回説明 スライド編集") == DialogResult.Cancel) return;
            using (var form = new IntroSlideEditForm(_dbManager, slide))
            {
                if (form.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    LoadSlides();
                    // (#295) guide/ に新規画像を取り込んだときだけアセットも控える (本文のみは DB だけ)。
                    _dbManager.SessionBackupCoordinator.RunAfterOperation(FindForm(), form.AssetsChangedOnDisk, "スライド編集");
                }
            }
        }

        private void OnDelete()
        {
            if (_dbManager == null) return;
            var slide = Selected();
            if (slide == null)
            {
                MessageBox.Show("削除するスライドを選択してください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("このスライドを削除しますか？", "確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (SessionConflictHelper.CheckBeforeWrite(this, "初回説明 スライド削除") == DialogResult.Cancel) return;

            try
            {
                _dbManager.DeleteIntroSlide(slide.SlideId);

                // 画像実体は「他スライドが同じ画像を参照していない」場合のみ guide/ から削除 (orphan 防止)。
                // 参照判定は削除直後の **最新 DB** を再取得して行う (in-memory snapshot 依存だと、前回ロード以降に
                // 他セッションが同一画像を参照するスライドを足していた場合に使用中画像を消しうる、#274 review #6)。
                bool imageRemoved = false;
                if (!string.IsNullOrWhiteSpace(slide.ImagePath))
                {
                    bool referencedByOthers = _dbManager.GetAllIntroSlides()
                        .Any(s => string.Equals(s.ImagePath, slide.ImagePath, StringComparison.OrdinalIgnoreCase));
                    if (!referencedByOthers)
                    {
                        IntroGuideAssetHelper.DeleteImage(PathManager.GuideFolder, slide.ImagePath);
                        imageRemoved = true;
                    }
                }
                LoadSlides();
                // (#295) guide/ から画像実体を消したときだけアセットも控える (DB だけのスライド削除なら DB だけ)。
                _dbManager.SessionBackupCoordinator.RunAfterOperation(FindForm(), imageRemoved, "スライド削除");
            }
            catch (Exception ex)
            {
                MessageBox.Show("削除に失敗しました: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MoveSlide(int direction)
        {
            if (_dbManager == null || _slides == null) return;
            var slide = Selected();
            if (slide == null) return;
            int idx = _slides.FindIndex(s => s.SlideId == slide.SlideId);
            int newIdx = idx + direction;
            if (idx < 0 || newIdx < 0 || newIdx >= _slides.Count) return;

            // 選択・範囲 validation 通過後、DB write 直前で session check (StoreSectionPanel.MoveSection と同位置)。
            if (SessionConflictHelper.CheckBeforeWrite(this, "初回説明 スライド並び替え") == DialogResult.Cancel) return;

            var a = _slides[idx];
            var b = _slides[newIdx];

            try
            {
                // a と b の display_order を **1 transaction で入れ替え** (#274 review #2)。
                // UpdateIntroSlide を 2 回別々に投げると、片方成功・片方失敗で両者が同じ display_order になる
                // half-write が起きうるため、atomic な swap に委ねる (a は b の order、b は a の order を持つ)。
                _dbManager.SwapIntroSlideOrder(a.SlideId, b.DisplayOrder, b.SlideId, a.DisplayOrder);
                LoadSlides();
                // 移動後の行を選び直す。
                var moved = _slides?.FirstOrDefault(s => s.SlideId == slide.SlideId);
                if (moved != null)
                {
                    int row = _slides.IndexOf(moved);
                    if (row >= 0 && row < _grid.Rows.Count)
                    {
                        _grid.ClearSelection();
                        _grid.Rows[row].Selected = true;
                        _grid.CurrentCell = _grid.Rows[row].Cells[_grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible).Index];
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("並び替えに失敗しました: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
