namespace TonePrism.Manager.Controls
{
    partial class SettingsSectionPanel
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
            this.grpDatabase = new System.Windows.Forms.GroupBox();
            this.btnResetDatabase = new System.Windows.Forms.Button();
            this.grpInfo = new System.Windows.Forms.GroupBox();
            this.lblVersionInfo = new System.Windows.Forms.Label();
            this.grpLog = new System.Windows.Forms.GroupBox();
            this.lblLogRetentionPrompt = new System.Windows.Forms.Label();
            this.numLogRetention = new System.Windows.Forms.NumericUpDown();
            this.lblLogRetentionUnit = new System.Windows.Forms.Label();
            this.lblLogRetentionNote = new System.Windows.Forms.Label();
            this.grpBackup = new System.Windows.Forms.GroupBox();
            this.lblBackupDest = new System.Windows.Forms.Label();
            this.txtBackupDest = new System.Windows.Forms.TextBox();
            this.btnBackupBrowse = new System.Windows.Forms.Button();
            this.lblBackupDestHint = new System.Windows.Forms.Label();
            this.lblBackupInterval = new System.Windows.Forms.Label();
            this.numBackupInterval = new System.Windows.Forms.NumericUpDown();
            this.lblBackupIntervalUnit = new System.Windows.Forms.Label();
            this.lblBackupRetention = new System.Windows.Forms.Label();
            this.numBackupRetention = new System.Windows.Forms.NumericUpDown();
            this.lblBackupRetentionUnit = new System.Windows.Forms.Label();
            this.btnBackupSave = new System.Windows.Forms.Button();
            this.grpDatabase.SuspendLayout();
            this.grpInfo.SuspendLayout();
            this.grpLog.SuspendLayout();
            this.grpBackup.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numLogRetention)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBackupInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBackupRetention)).BeginInit();
            this.SuspendLayout();
            //
            // grpDatabase
            //
            this.grpDatabase.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpDatabase.Controls.Add(this.btnResetDatabase);
            this.grpDatabase.Location = new System.Drawing.Point(20, 20);
            this.grpDatabase.Name = "grpDatabase";
            this.grpDatabase.Size = new System.Drawing.Size(760, 80);
            this.grpDatabase.TabIndex = 0;
            this.grpDatabase.TabStop = false;
            this.grpDatabase.Text = "データベース";
            //
            // btnResetDatabase
            //
            this.btnResetDatabase.Location = new System.Drawing.Point(20, 30);
            this.btnResetDatabase.Name = "btnResetDatabase";
            this.btnResetDatabase.Size = new System.Drawing.Size(160, 30);
            this.btnResetDatabase.TabIndex = 0;
            this.btnResetDatabase.Text = "データベースリセット";
            this.btnResetDatabase.Click += new System.EventHandler(this.btnResetDatabase_Click);
            //
            // grpInfo
            //
            this.grpInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpInfo.Controls.Add(this.lblVersionInfo);
            this.grpInfo.Location = new System.Drawing.Point(20, 120);
            this.grpInfo.Name = "grpInfo";
            this.grpInfo.Size = new System.Drawing.Size(760, 200);
            this.grpInfo.TabIndex = 1;
            this.grpInfo.TabStop = false;
            this.grpInfo.Text = "バージョン情報";
            //
            // lblVersionInfo
            //
            this.lblVersionInfo.AutoSize = true;
            this.lblVersionInfo.Location = new System.Drawing.Point(20, 25);
            this.lblVersionInfo.Name = "lblVersionInfo";
            this.lblVersionInfo.Size = new System.Drawing.Size(0, 15);
            this.lblVersionInfo.TabIndex = 0;
            //
            // grpLog
            //
            this.grpLog.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLog.Controls.Add(this.lblLogRetentionPrompt);
            this.grpLog.Controls.Add(this.numLogRetention);
            this.grpLog.Controls.Add(this.lblLogRetentionUnit);
            this.grpLog.Controls.Add(this.lblLogRetentionNote);
            this.grpLog.Location = new System.Drawing.Point(20, 340);
            this.grpLog.Name = "grpLog";
            this.grpLog.Size = new System.Drawing.Size(760, 80);
            this.grpLog.TabIndex = 2;
            this.grpLog.TabStop = false;
            this.grpLog.Text = "ログ";
            //
            // lblLogRetentionPrompt
            //
            this.lblLogRetentionPrompt.AutoSize = true;
            this.lblLogRetentionPrompt.Location = new System.Drawing.Point(20, 30);
            this.lblLogRetentionPrompt.Name = "lblLogRetentionPrompt";
            this.lblLogRetentionPrompt.Size = new System.Drawing.Size(80, 15);
            this.lblLogRetentionPrompt.TabIndex = 0;
            this.lblLogRetentionPrompt.Text = "保存日数:";
            //
            // numLogRetention
            //
            this.numLogRetention.Location = new System.Drawing.Point(100, 28);
            this.numLogRetention.Name = "numLogRetention";
            this.numLogRetention.Size = new System.Drawing.Size(80, 23);
            this.numLogRetention.TabIndex = 1;
            this.numLogRetention.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numLogRetention.Maximum = new decimal(new int[] { 365, 0, 0, 0 });
            this.numLogRetention.Value = new decimal(new int[] { 30, 0, 0, 0 });
            //
            // lblLogRetentionUnit
            //
            this.lblLogRetentionUnit.AutoSize = true;
            this.lblLogRetentionUnit.Location = new System.Drawing.Point(185, 30);
            this.lblLogRetentionUnit.Name = "lblLogRetentionUnit";
            this.lblLogRetentionUnit.Size = new System.Drawing.Size(20, 15);
            this.lblLogRetentionUnit.TabIndex = 2;
            this.lblLogRetentionUnit.Text = "日";
            //
            // lblLogRetentionNote
            //
            this.lblLogRetentionNote.AutoSize = true;
            this.lblLogRetentionNote.ForeColor = System.Drawing.SystemColors.GrayText;
            this.lblLogRetentionNote.Location = new System.Drawing.Point(20, 55);
            this.lblLogRetentionNote.Name = "lblLogRetentionNote";
            this.lblLogRetentionNote.Size = new System.Drawing.Size(250, 15);
            this.lblLogRetentionNote.TabIndex = 3;
            this.lblLogRetentionNote.Text = "変更は次回 Manager 起動時に反映されます。";
            //
            // grpBackup
            //
            this.grpBackup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpBackup.Controls.Add(this.lblBackupDest);
            this.grpBackup.Controls.Add(this.txtBackupDest);
            this.grpBackup.Controls.Add(this.btnBackupBrowse);
            this.grpBackup.Controls.Add(this.lblBackupDestHint);
            this.grpBackup.Controls.Add(this.lblBackupInterval);
            this.grpBackup.Controls.Add(this.numBackupInterval);
            this.grpBackup.Controls.Add(this.lblBackupIntervalUnit);
            this.grpBackup.Controls.Add(this.lblBackupRetention);
            this.grpBackup.Controls.Add(this.numBackupRetention);
            this.grpBackup.Controls.Add(this.lblBackupRetentionUnit);
            this.grpBackup.Controls.Add(this.btnBackupSave);
            this.grpBackup.Location = new System.Drawing.Point(20, 440);
            this.grpBackup.Name = "grpBackup";
            this.grpBackup.Size = new System.Drawing.Size(760, 240);
            this.grpBackup.TabIndex = 3;
            this.grpBackup.TabStop = false;
            this.grpBackup.Text = "バックアップ";
            //
            // lblBackupDest
            //
            this.lblBackupDest.AutoSize = true;
            this.lblBackupDest.Location = new System.Drawing.Point(20, 25);
            this.lblBackupDest.Size = new System.Drawing.Size(115, 15);
            this.lblBackupDest.TabIndex = 0;
            this.lblBackupDest.Text = "バックアップ保存先:";
            //
            // txtBackupDest
            //
            this.txtBackupDest.Location = new System.Drawing.Point(20, 45);
            this.txtBackupDest.Size = new System.Drawing.Size(560, 23);
            this.txtBackupDest.TabIndex = 1;
            //
            // btnBackupBrowse
            //
            this.btnBackupBrowse.Location = new System.Drawing.Point(590, 44);
            this.btnBackupBrowse.Size = new System.Drawing.Size(80, 26);
            this.btnBackupBrowse.TabIndex = 2;
            this.btnBackupBrowse.Text = "参照...";
            this.btnBackupBrowse.UseVisualStyleBackColor = true;
            this.btnBackupBrowse.Click += new System.EventHandler(this.btnBackupBrowse_Click);
            //
            // lblBackupDestHint
            //
            this.lblBackupDestHint.AutoSize = true;
            this.lblBackupDestHint.ForeColor = System.Drawing.Color.DimGray;
            this.lblBackupDestHint.Location = new System.Drawing.Point(20, 75);
            this.lblBackupDestHint.Size = new System.Drawing.Size(420, 15);
            this.lblBackupDestHint.TabIndex = 3;
            this.lblBackupDestHint.Text = "空欄にするとデフォルト（データベースファイルの隣の backups/ フォルダ）が使われます";
            //
            // lblBackupInterval
            //
            this.lblBackupInterval.AutoSize = true;
            this.lblBackupInterval.Location = new System.Drawing.Point(20, 110);
            this.lblBackupInterval.Size = new System.Drawing.Size(280, 15);
            this.lblBackupInterval.TabIndex = 4;
            this.lblBackupInterval.Text = "自動バックアップ間隔 (Manager 起動時にチェック):";
            //
            // numBackupInterval
            //
            this.numBackupInterval.Location = new System.Drawing.Point(20, 130);
            this.numBackupInterval.Maximum = new decimal(new int[] { 720, 0, 0, 0 });
            this.numBackupInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numBackupInterval.Size = new System.Drawing.Size(80, 23);
            this.numBackupInterval.TabIndex = 5;
            this.numBackupInterval.Value = new decimal(new int[] { 24, 0, 0, 0 });
            //
            // lblBackupIntervalUnit
            //
            this.lblBackupIntervalUnit.AutoSize = true;
            this.lblBackupIntervalUnit.Location = new System.Drawing.Point(108, 133);
            this.lblBackupIntervalUnit.Size = new System.Drawing.Size(160, 15);
            this.lblBackupIntervalUnit.TabIndex = 6;
            this.lblBackupIntervalUnit.Text = "時間以上経過していたら実行";
            //
            // lblBackupRetention
            //
            this.lblBackupRetention.AutoSize = true;
            this.lblBackupRetention.Location = new System.Drawing.Point(20, 165);
            this.lblBackupRetention.Size = new System.Drawing.Size(95, 15);
            this.lblBackupRetention.TabIndex = 7;
            this.lblBackupRetention.Text = "保持する世代数:";
            //
            // numBackupRetention
            //
            this.numBackupRetention.Location = new System.Drawing.Point(20, 185);
            this.numBackupRetention.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            this.numBackupRetention.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numBackupRetention.Size = new System.Drawing.Size(80, 23);
            this.numBackupRetention.TabIndex = 8;
            this.numBackupRetention.Value = new decimal(new int[] { 30, 0, 0, 0 });
            //
            // lblBackupRetentionUnit
            //
            this.lblBackupRetentionUnit.AutoSize = true;
            this.lblBackupRetentionUnit.Location = new System.Drawing.Point(108, 188);
            this.lblBackupRetentionUnit.Size = new System.Drawing.Size(280, 15);
            this.lblBackupRetentionUnit.TabIndex = 9;
            this.lblBackupRetentionUnit.Text = "個 (これを超えた古いバックアップは自動削除されます)";
            //
            // btnBackupSave
            //
            this.btnBackupSave.Location = new System.Drawing.Point(580, 200);
            this.btnBackupSave.Size = new System.Drawing.Size(90, 30);
            this.btnBackupSave.TabIndex = 10;
            this.btnBackupSave.Text = "保存";
            this.btnBackupSave.UseVisualStyleBackColor = true;
            this.btnBackupSave.Click += new System.EventHandler(this.btnBackupSave_Click);
            //
            // SettingsSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this.grpDatabase);
            this.Controls.Add(this.grpInfo);
            this.Controls.Add(this.grpLog);
            this.Controls.Add(this.grpBackup);
            this.Name = "SettingsSectionPanel";
            this.Size = new System.Drawing.Size(800, 700);
            this.grpDatabase.ResumeLayout(false);
            this.grpInfo.ResumeLayout(false);
            this.grpInfo.PerformLayout();
            this.grpLog.ResumeLayout(false);
            this.grpLog.PerformLayout();
            this.grpBackup.ResumeLayout(false);
            this.grpBackup.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numLogRetention)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBackupInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBackupRetention)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox grpDatabase;
        private System.Windows.Forms.Button btnResetDatabase;
        private System.Windows.Forms.GroupBox grpInfo;
        private System.Windows.Forms.Label lblVersionInfo;
        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.Label lblLogRetentionPrompt;
        private System.Windows.Forms.NumericUpDown numLogRetention;
        private System.Windows.Forms.Label lblLogRetentionUnit;
        private System.Windows.Forms.Label lblLogRetentionNote;
        private System.Windows.Forms.GroupBox grpBackup;
        private System.Windows.Forms.Label lblBackupDest;
        private System.Windows.Forms.TextBox txtBackupDest;
        private System.Windows.Forms.Button btnBackupBrowse;
        private System.Windows.Forms.Label lblBackupDestHint;
        private System.Windows.Forms.Label lblBackupInterval;
        private System.Windows.Forms.NumericUpDown numBackupInterval;
        private System.Windows.Forms.Label lblBackupIntervalUnit;
        private System.Windows.Forms.Label lblBackupRetention;
        private System.Windows.Forms.NumericUpDown numBackupRetention;
        private System.Windows.Forms.Label lblBackupRetentionUnit;
        private System.Windows.Forms.Button btnBackupSave;
    }
}
