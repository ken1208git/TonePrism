namespace GCTonePrism.Manager.Controls
{
    partial class BackupSectionPanel
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
            this.grpActions = new System.Windows.Forms.GroupBox();
            this.btnBackupNow = new System.Windows.Forms.Button();
            this.lblLastBackup = new System.Windows.Forms.Label();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.grpHistory = new System.Windows.Forms.GroupBox();
            this.gridHistory = new System.Windows.Forms.DataGridView();
            this.grpControls = new System.Windows.Forms.GroupBox();
            this.btnRestore = new System.Windows.Forms.Button();
            this.btnSettings = new System.Windows.Forms.Button();
            this.lblDestPath = new System.Windows.Forms.Label();
            this.grpActions.SuspendLayout();
            this.grpHistory.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridHistory)).BeginInit();
            this.grpControls.SuspendLayout();
            this.SuspendLayout();
            //
            // grpActions
            //
            this.grpActions.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpActions.Controls.Add(this.btnBackupNow);
            this.grpActions.Controls.Add(this.lblLastBackup);
            this.grpActions.Controls.Add(this.btnRefresh);
            this.grpActions.Location = new System.Drawing.Point(20, 20);
            this.grpActions.Name = "grpActions";
            this.grpActions.Size = new System.Drawing.Size(1040, 80);
            this.grpActions.TabIndex = 0;
            this.grpActions.TabStop = false;
            this.grpActions.Text = "バックアップ操作";
            //
            // btnBackupNow
            //
            this.btnBackupNow.BackColor = System.Drawing.Color.SeaGreen;
            this.btnBackupNow.Font = new System.Drawing.Font("MS UI Gothic", 10F, System.Drawing.FontStyle.Bold);
            this.btnBackupNow.ForeColor = System.Drawing.Color.White;
            this.btnBackupNow.Location = new System.Drawing.Point(20, 28);
            this.btnBackupNow.Name = "btnBackupNow";
            this.btnBackupNow.Size = new System.Drawing.Size(180, 36);
            this.btnBackupNow.TabIndex = 0;
            this.btnBackupNow.Text = "今すぐバックアップ";
            this.btnBackupNow.UseVisualStyleBackColor = false;
            this.btnBackupNow.Click += new System.EventHandler(this.btnBackupNow_Click);
            //
            // lblLastBackup
            //
            this.lblLastBackup.AutoSize = true;
            this.lblLastBackup.Location = new System.Drawing.Point(220, 38);
            this.lblLastBackup.Name = "lblLastBackup";
            this.lblLastBackup.Size = new System.Drawing.Size(150, 15);
            this.lblLastBackup.TabIndex = 1;
            this.lblLastBackup.Text = "最終バックアップ: 未取得";
            //
            // btnRefresh
            //
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRefresh.Location = new System.Drawing.Point(940, 30);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(80, 30);
            this.btnRefresh.TabIndex = 2;
            this.btnRefresh.Text = "更新";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            //
            // grpHistory
            //
            this.grpHistory.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpHistory.Controls.Add(this.gridHistory);
            this.grpHistory.Location = new System.Drawing.Point(20, 110);
            this.grpHistory.Name = "grpHistory";
            this.grpHistory.Size = new System.Drawing.Size(1040, 380);
            this.grpHistory.TabIndex = 1;
            this.grpHistory.TabStop = false;
            this.grpHistory.Text = "バックアップ履歴";
            //
            // gridHistory
            //
            this.gridHistory.AllowUserToAddRows = false;
            this.gridHistory.AllowUserToDeleteRows = false;
            this.gridHistory.AllowUserToResizeRows = false;
            this.gridHistory.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.gridHistory.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridHistory.Location = new System.Drawing.Point(3, 19);
            this.gridHistory.MultiSelect = false;
            this.gridHistory.Name = "gridHistory";
            this.gridHistory.ReadOnly = true;
            this.gridHistory.RowHeadersVisible = false;
            this.gridHistory.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridHistory.Size = new System.Drawing.Size(1034, 358);
            this.gridHistory.TabIndex = 0;
            //
            // grpControls
            //
            this.grpControls.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpControls.Controls.Add(this.lblDestPath);
            this.grpControls.Controls.Add(this.btnRestore);
            this.grpControls.Controls.Add(this.btnSettings);
            this.grpControls.Location = new System.Drawing.Point(20, 500);
            this.grpControls.Name = "grpControls";
            this.grpControls.Size = new System.Drawing.Size(1040, 70);
            this.grpControls.TabIndex = 2;
            this.grpControls.TabStop = false;
            this.grpControls.Text = "操作";
            //
            // btnRestore
            //
            this.btnRestore.BackColor = System.Drawing.Color.IndianRed;
            this.btnRestore.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold);
            this.btnRestore.ForeColor = System.Drawing.Color.White;
            this.btnRestore.Location = new System.Drawing.Point(20, 25);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Size = new System.Drawing.Size(220, 32);
            this.btnRestore.TabIndex = 0;
            this.btnRestore.Text = "選択したバックアップから復元...";
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnSettings
            //
            this.btnSettings.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSettings.Location = new System.Drawing.Point(940, 26);
            this.btnSettings.Name = "btnSettings";
            this.btnSettings.Size = new System.Drawing.Size(80, 30);
            this.btnSettings.TabIndex = 1;
            this.btnSettings.Text = "設定...";
            this.btnSettings.UseVisualStyleBackColor = true;
            this.btnSettings.Click += new System.EventHandler(this.btnSettings_Click);
            //
            // lblDestPath
            //
            this.lblDestPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.lblDestPath.AutoEllipsis = true;
            this.lblDestPath.Location = new System.Drawing.Point(260, 32);
            this.lblDestPath.Name = "lblDestPath";
            this.lblDestPath.Size = new System.Drawing.Size(670, 18);
            this.lblDestPath.TabIndex = 2;
            this.lblDestPath.Text = "保存先: ";
            //
            // BackupSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.grpActions);
            this.Controls.Add(this.grpHistory);
            this.Controls.Add(this.grpControls);
            this.Name = "BackupSectionPanel";
            this.Size = new System.Drawing.Size(1080, 590);
            this.grpActions.ResumeLayout(false);
            this.grpActions.PerformLayout();
            this.grpHistory.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridHistory)).EndInit();
            this.grpControls.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox grpActions;
        private System.Windows.Forms.Button btnBackupNow;
        private System.Windows.Forms.Label lblLastBackup;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.GroupBox grpHistory;
        private System.Windows.Forms.DataGridView gridHistory;
        private System.Windows.Forms.GroupBox grpControls;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnSettings;
        private System.Windows.Forms.Label lblDestPath;
    }
}
