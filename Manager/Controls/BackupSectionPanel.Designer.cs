namespace TonePrism.Manager.Controls
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
            this.lblLastSnapshot = new System.Windows.Forms.Label();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.grpHistory = new System.Windows.Forms.GroupBox();
            this.gridHistory = new System.Windows.Forms.DataGridView();
            this.btnRestore = new System.Windows.Forms.Button();
            this.btnDelete = new System.Windows.Forms.Button();
            this.btnReconcile = new System.Windows.Forms.Button();
            this.lblDestPath = new System.Windows.Forms.Label();
            this.grpActions.SuspendLayout();
            this.grpHistory.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridHistory)).BeginInit();
            this.SuspendLayout();
            //
            // grpActions
            //
            this.grpActions.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpActions.Controls.Add(this.btnBackupNow);
            this.grpActions.Controls.Add(this.lblLastBackup);
            this.grpActions.Controls.Add(this.lblLastSnapshot);
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
            // (round9 UI) DB(設定込み)+ゲーム本体を 1 バックアップとして 2 行で表示 (1 行だと右端の更新ボタンに被るため)。
            // 1 行目 = 取得日時、2 行目 (灰) = 中身 (ゲーム本体のファイル数 + プール実使用)。どちらも短く更新ボタンに被らない。
            this.lblLastBackup.AutoSize = true;
            this.lblLastBackup.Location = new System.Drawing.Point(244, 38);
            this.lblLastBackup.Name = "lblLastBackup";
            this.lblLastBackup.Size = new System.Drawing.Size(150, 15);
            this.lblLastBackup.TabIndex = 1;
            this.lblLastBackup.Text = "最終バックアップ: 未取得";
            //
            // lblLastSnapshot (2 行目、ゲーム本体の中身)
            //
            this.lblLastSnapshot.AutoSize = true;
            this.lblLastSnapshot.ForeColor = System.Drawing.Color.DimGray;
            this.lblLastSnapshot.Location = new System.Drawing.Point(244, 56);
            this.lblLastSnapshot.Name = "lblLastSnapshot";
            this.lblLastSnapshot.Size = new System.Drawing.Size(150, 15);
            this.lblLastSnapshot.TabIndex = 3;
            this.lblLastSnapshot.Text = "ゲームファイル: 未取得";
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
            this.grpHistory.Controls.Add(this.btnRestore);
            this.grpHistory.Controls.Add(this.btnDelete);
            this.grpHistory.Controls.Add(this.btnReconcile);
            this.grpHistory.Location = new System.Drawing.Point(20, 110);
            this.grpHistory.Name = "grpHistory";
            this.grpHistory.Size = new System.Drawing.Size(1040, 430);
            this.grpHistory.TabIndex = 1;
            this.grpHistory.TabStop = false;
            this.grpHistory.Text = "バックアップ履歴";
            //
            // gridHistory
            //
            this.gridHistory.AllowUserToAddRows = false;
            this.gridHistory.AllowUserToDeleteRows = false;
            this.gridHistory.AllowUserToResizeRows = false;
            this.gridHistory.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.gridHistory.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.gridHistory.Location = new System.Drawing.Point(10, 22);
            this.gridHistory.MultiSelect = false;
            this.gridHistory.Name = "gridHistory";
            this.gridHistory.ReadOnly = true;
            this.gridHistory.RowHeadersVisible = false;
            this.gridHistory.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridHistory.Size = new System.Drawing.Size(1020, 356);
            this.gridHistory.TabIndex = 0;
            //
            // btnRestore
            //
            this.btnRestore.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnRestore.BackColor = System.Drawing.Color.IndianRed;
            this.btnRestore.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold);
            this.btnRestore.ForeColor = System.Drawing.Color.White;
            this.btnRestore.Location = new System.Drawing.Point(10, 388);
            this.btnRestore.Name = "btnRestore";
            this.btnRestore.Size = new System.Drawing.Size(220, 32);
            this.btnRestore.TabIndex = 1;
            this.btnRestore.Text = "選択したバックアップから復元...";
            this.btnRestore.UseVisualStyleBackColor = false;
            this.btnRestore.Click += new System.EventHandler(this.btnRestore_Click);
            //
            // btnDelete
            //
            this.btnDelete.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnDelete.Location = new System.Drawing.Point(240, 388);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(140, 32);
            this.btnDelete.TabIndex = 2;
            this.btnDelete.Text = "選択した履歴を削除...";
            this.btnDelete.UseVisualStyleBackColor = true;
            this.btnDelete.Click += new System.EventHandler(this.btnDelete_Click);
            //
            // btnReconcile
            //
            // (#250 PR2) 整合性チェック (DB↔games/guide のズレ検出) を復元せずオンデマンドで再実行する。
            // 復元レポートの手順「修正後に再チェック」を Manager 再起動に頼らず正しく行えるようにするためのボタン。
            this.btnReconcile.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnReconcile.Location = new System.Drawing.Point(390, 388);
            this.btnReconcile.Name = "btnReconcile";
            this.btnReconcile.Size = new System.Drawing.Size(150, 32);
            this.btnReconcile.TabIndex = 3;
            this.btnReconcile.Text = "整合性チェック";
            this.btnReconcile.UseVisualStyleBackColor = true;
            this.btnReconcile.Click += new System.EventHandler(this.btnReconcile_Click);
            //
            // lblDestPath
            //
            this.lblDestPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.lblDestPath.AutoEllipsis = true;
            this.lblDestPath.Location = new System.Drawing.Point(20, 552);
            this.lblDestPath.Name = "lblDestPath";
            this.lblDestPath.Size = new System.Drawing.Size(1040, 18);
            this.lblDestPath.TabIndex = 2;
            this.lblDestPath.Text = "保存先: ";
            //
            // BackupSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.grpActions);
            this.Controls.Add(this.grpHistory);
            this.Controls.Add(this.lblDestPath);
            this.Name = "BackupSectionPanel";
            this.Size = new System.Drawing.Size(1080, 590);
            this.grpActions.ResumeLayout(false);
            this.grpActions.PerformLayout();
            this.grpHistory.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridHistory)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox grpActions;
        private System.Windows.Forms.Button btnBackupNow;
        private System.Windows.Forms.Label lblLastBackup;
        private System.Windows.Forms.Label lblLastSnapshot;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.GroupBox grpHistory;
        private System.Windows.Forms.DataGridView gridHistory;
        private System.Windows.Forms.Button btnRestore;
        private System.Windows.Forms.Button btnDelete;
        private System.Windows.Forms.Button btnReconcile;
        private System.Windows.Forms.Label lblDestPath;
    }
}
