namespace GCTonePrism.Manager.Controls
{
    partial class StoreSectionPanel
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region コンポーネント デザイナーで生成されたコード

        private void InitializeComponent()
        {
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.btnAdd = new System.Windows.Forms.ToolStripButton();
            this.btnEdit = new System.Windows.Forms.ToolStripButton();
            this.btnDelete = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.btnMoveUp = new System.Windows.Forms.ToolStripButton();
            this.btnMoveDown = new System.Windows.Forms.ToolStripButton();
            this.dgvSections = new System.Windows.Forms.DataGridView();
            this.toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvSections)).BeginInit();
            this.SuspendLayout();
            //
            // toolStrip1
            //
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.btnAdd,
            this.btnEdit,
            this.btnDelete,
            this.toolStripSeparator1,
            this.btnMoveUp,
            this.btnMoveDown});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(800, 27);
            this.toolStrip1.TabIndex = 0;
            //
            // btnAdd
            //
            this.btnAdd.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(39, 24);
            this.btnAdd.Text = "追加";
            this.btnAdd.Click += new System.EventHandler(this.btnAdd_Click);
            //
            // btnEdit
            //
            this.btnEdit.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnEdit.Name = "btnEdit";
            this.btnEdit.Size = new System.Drawing.Size(39, 24);
            this.btnEdit.Text = "編集";
            this.btnEdit.Click += new System.EventHandler(this.btnEdit_Click);
            //
            // btnDelete
            //
            this.btnDelete.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(39, 24);
            this.btnDelete.Text = "削除";
            this.btnDelete.Click += new System.EventHandler(this.btnDelete_Click);
            //
            // toolStripSeparator1
            //
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 27);
            //
            // btnMoveUp
            //
            this.btnMoveUp.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnMoveUp.Name = "btnMoveUp";
            this.btnMoveUp.Size = new System.Drawing.Size(49, 24);
            this.btnMoveUp.Text = "▲上へ";
            this.btnMoveUp.Click += new System.EventHandler(this.btnMoveUp_Click);
            //
            // btnMoveDown
            //
            this.btnMoveDown.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnMoveDown.Name = "btnMoveDown";
            this.btnMoveDown.Size = new System.Drawing.Size(49, 24);
            this.btnMoveDown.Text = "▼下へ";
            this.btnMoveDown.Click += new System.EventHandler(this.btnMoveDown_Click);
            //
            // dgvSections
            //
            this.dgvSections.AllowUserToAddRows = false;
            this.dgvSections.AllowUserToDeleteRows = false;
            this.dgvSections.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvSections.EnableHeadersVisualStyles = false;
            this.dgvSections.ColumnHeadersDefaultCellStyle.SelectionBackColor = this.dgvSections.ColumnHeadersDefaultCellStyle.BackColor;
            this.dgvSections.ColumnHeadersDefaultCellStyle.SelectionForeColor = this.dgvSections.ColumnHeadersDefaultCellStyle.ForeColor;
            this.dgvSections.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvSections.Location = new System.Drawing.Point(0, 27);
            this.dgvSections.MultiSelect = false;
            this.dgvSections.Name = "dgvSections";
            this.dgvSections.ReadOnly = true;
            this.dgvSections.RowHeadersVisible = false;
            this.dgvSections.RowTemplate.Height = 25;
            this.dgvSections.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvSections.Size = new System.Drawing.Size(800, 473);
            this.dgvSections.TabIndex = 1;
            this.dgvSections.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvSections_CellDoubleClick);
            //
            // StoreSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.dgvSections);
            this.Controls.Add(this.toolStrip1);
            this.Name = "StoreSectionPanel";
            this.Size = new System.Drawing.Size(800, 500);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvSections)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton btnAdd;
        private System.Windows.Forms.ToolStripButton btnEdit;
        private System.Windows.Forms.ToolStripButton btnDelete;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton btnMoveUp;
        private System.Windows.Forms.ToolStripButton btnMoveDown;
        private System.Windows.Forms.DataGridView dgvSections;
    }
}
