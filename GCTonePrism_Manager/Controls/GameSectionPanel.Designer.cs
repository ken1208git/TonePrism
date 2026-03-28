namespace GCTonePrism.Manager.Controls
{
    partial class GameSectionPanel
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
            this.btnAddGame = new System.Windows.Forms.ToolStripButton();
            this.btnEditGame = new System.Windows.Forms.ToolStripButton();
            this.btnVersionUp = new System.Windows.Forms.ToolStripButton();
            this.btnDeleteGame = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.btnRefresh = new System.Windows.Forms.ToolStripButton();
            this.dgvGames = new System.Windows.Forms.DataGridView();
            this.toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvGames)).BeginInit();
            this.SuspendLayout();
            //
            // toolStrip1
            //
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.btnAddGame,
            this.btnEditGame,
            this.btnVersionUp,
            this.btnDeleteGame,
            this.toolStripSeparator1,
            this.btnRefresh});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(800, 27);
            this.toolStrip1.TabIndex = 0;
            //
            // btnAddGame
            //
            this.btnAddGame.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnAddGame.Name = "btnAddGame";
            this.btnAddGame.Size = new System.Drawing.Size(75, 24);
            this.btnAddGame.Text = "ゲーム追加";
            this.btnAddGame.Click += new System.EventHandler(this.btnAddGame_Click);
            //
            // btnEditGame
            //
            this.btnEditGame.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnEditGame.Name = "btnEditGame";
            this.btnEditGame.Size = new System.Drawing.Size(39, 24);
            this.btnEditGame.Text = "編集";
            this.btnEditGame.Click += new System.EventHandler(this.btnEditGame_Click);
            //
            // btnVersionUp
            //
            this.btnVersionUp.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnVersionUp.Name = "btnVersionUp";
            this.btnVersionUp.Size = new System.Drawing.Size(91, 24);
            this.btnVersionUp.Text = "バージョンアップ";
            this.btnVersionUp.Click += new System.EventHandler(this.btnVersionUp_Click);
            //
            // btnDeleteGame
            //
            this.btnDeleteGame.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnDeleteGame.Name = "btnDeleteGame";
            this.btnDeleteGame.Size = new System.Drawing.Size(39, 24);
            this.btnDeleteGame.Text = "削除";
            this.btnDeleteGame.Click += new System.EventHandler(this.btnDeleteGame_Click);
            //
            // toolStripSeparator1
            //
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 27);
            //
            // btnRefresh
            //
            this.btnRefresh.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(39, 24);
            this.btnRefresh.Text = "更新";
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            //
            // dgvGames
            //
            this.dgvGames.AllowUserToAddRows = false;
            this.dgvGames.AllowUserToDeleteRows = false;
            this.dgvGames.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvGames.EnableHeadersVisualStyles = false;
            this.dgvGames.ColumnHeadersDefaultCellStyle.SelectionBackColor = this.dgvGames.ColumnHeadersDefaultCellStyle.BackColor;
            this.dgvGames.ColumnHeadersDefaultCellStyle.SelectionForeColor = this.dgvGames.ColumnHeadersDefaultCellStyle.ForeColor;
            this.dgvGames.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvGames.Location = new System.Drawing.Point(0, 27);
            this.dgvGames.MultiSelect = false;
            this.dgvGames.Name = "dgvGames";
            this.dgvGames.ReadOnly = true;
            this.dgvGames.RowHeadersVisible = false;
            this.dgvGames.RowTemplate.Height = 24;
            this.dgvGames.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvGames.Size = new System.Drawing.Size(800, 473);
            this.dgvGames.TabIndex = 1;
            this.dgvGames.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvGames_CellDoubleClick);
            //
            // GameSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.dgvGames);
            this.Controls.Add(this.toolStrip1);
            this.Name = "GameSectionPanel";
            this.Size = new System.Drawing.Size(800, 500);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvGames)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton btnAddGame;
        private System.Windows.Forms.ToolStripButton btnEditGame;
        private System.Windows.Forms.ToolStripButton btnVersionUp;
        private System.Windows.Forms.ToolStripButton btnDeleteGame;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton btnRefresh;
        private System.Windows.Forms.DataGridView dgvGames;
    }
}
