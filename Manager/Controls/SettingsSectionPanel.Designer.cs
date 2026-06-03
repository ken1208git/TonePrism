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
            this.grpBackup = new System.Windows.Forms.GroupBox();
            this.lblBackupDest = new System.Windows.Forms.Label();
            this.txtBackupDest = new System.Windows.Forms.TextBox();
            this.btnBackupBrowse = new System.Windows.Forms.Button();
            this.lblBackupDestHint = new System.Windows.Forms.Label();
            this.chkBackupAutoEnabled = new System.Windows.Forms.CheckBox();
            this.lblBackupRetention = new System.Windows.Forms.Label();
            this.numBackupRetention = new System.Windows.Forms.NumericUpDown();
            this.lblBackupRetentionUnit = new System.Windows.Forms.Label();
            this.lblBackupUnsaved = new System.Windows.Forms.Label();
            this.btnBackupRevert = new System.Windows.Forms.Button();
            this.btnBackupApply = new System.Windows.Forms.Button();
            this.grpLog = new System.Windows.Forms.GroupBox();
            this.lblLogDest = new System.Windows.Forms.Label();
            this.txtLogsRoot = new System.Windows.Forms.TextBox();
            this.btnLogBrowse = new System.Windows.Forms.Button();
            this.lblLogsRootHint = new System.Windows.Forms.Label();
            this.lblLogRetentionPrompt = new System.Windows.Forms.Label();
            this.numLogRetention = new System.Windows.Forms.NumericUpDown();
            this.lblLogRetentionUnit = new System.Windows.Forms.Label();
            this.lblLogRetentionNote = new System.Windows.Forms.Label();
            this.lblLogUnsaved = new System.Windows.Forms.Label();
            this.btnLogRevert = new System.Windows.Forms.Button();
            this.btnLogApply = new System.Windows.Forms.Button();
            this.grpDatabase = new System.Windows.Forms.GroupBox();
            this.btnResetDatabase = new System.Windows.Forms.Button();
            this.grpInfo = new System.Windows.Forms.GroupBox();
            this.lblVersionInfo = new System.Windows.Forms.Label();
            this.grpBackup.SuspendLayout();
            this.grpLog.SuspendLayout();
            this.grpDatabase.SuspendLayout();
            this.grpInfo.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numLogRetention)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBackupRetention)).BeginInit();
            this.SuspendLayout();
            //
            // grpLog (バックアップの下、ログ保存先 + 保存日数)
            //
            this.grpLog.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLog.Controls.Add(this.lblLogDest);
            this.grpLog.Controls.Add(this.txtLogsRoot);
            this.grpLog.Controls.Add(this.btnLogBrowse);
            this.grpLog.Controls.Add(this.lblLogsRootHint);
            this.grpLog.Controls.Add(this.lblLogRetentionPrompt);
            this.grpLog.Controls.Add(this.numLogRetention);
            this.grpLog.Controls.Add(this.lblLogRetentionUnit);
            this.grpLog.Controls.Add(this.lblLogRetentionNote);
            this.grpLog.Controls.Add(this.lblLogUnsaved);
            this.grpLog.Controls.Add(this.btnLogRevert);
            this.grpLog.Controls.Add(this.btnLogApply);
            this.grpLog.Location = new System.Drawing.Point(20, 325);
            this.grpLog.Name = "grpLog";
            this.grpLog.Size = new System.Drawing.Size(760, 205);
            this.grpLog.TabIndex = 0;
            this.grpLog.TabStop = false;
            this.grpLog.Text = "ログ";
            //
            // lblLogDest
            //
            this.lblLogDest.AutoSize = true;
            this.lblLogDest.Location = new System.Drawing.Point(20, 25);
            this.lblLogDest.Size = new System.Drawing.Size(75, 15);
            this.lblLogDest.TabIndex = 0;
            this.lblLogDest.Text = "ログ保存先:";
            //
            // txtLogsRoot
            //
            this.txtLogsRoot.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.txtLogsRoot.Location = new System.Drawing.Point(20, 45);
            this.txtLogsRoot.Size = new System.Drawing.Size(560, 23);
            this.txtLogsRoot.TabIndex = 1;
            //
            // btnLogBrowse
            //
            this.btnLogBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLogBrowse.Location = new System.Drawing.Point(590, 44);
            this.btnLogBrowse.Size = new System.Drawing.Size(80, 26);
            this.btnLogBrowse.TabIndex = 2;
            this.btnLogBrowse.Text = "参照...";
            this.btnLogBrowse.UseVisualStyleBackColor = true;
            this.btnLogBrowse.Click += new System.EventHandler(this.btnLogBrowse_Click);
            //
            // lblLogsRootHint
            //
            this.lblLogsRootHint.AutoSize = true;
            this.lblLogsRootHint.ForeColor = System.Drawing.Color.DimGray;
            this.lblLogsRootHint.Location = new System.Drawing.Point(20, 75);
            this.lblLogsRootHint.Size = new System.Drawing.Size(420, 15);
            this.lblLogsRootHint.TabIndex = 3;
            this.lblLogsRootHint.Text = "空欄にするとデフォルト（DB ファイルの隣の logs/）。指定先に manager/ launcher/ updater/ のフォルダが自動で作られます";
            //
            // lblLogRetentionPrompt
            //
            this.lblLogRetentionPrompt.AutoSize = true;
            this.lblLogRetentionPrompt.Location = new System.Drawing.Point(20, 110);
            this.lblLogRetentionPrompt.Size = new System.Drawing.Size(80, 15);
            this.lblLogRetentionPrompt.TabIndex = 4;
            this.lblLogRetentionPrompt.Text = "保存日数:";
            //
            // numLogRetention
            //
            this.numLogRetention.Location = new System.Drawing.Point(100, 108);
            this.numLogRetention.Name = "numLogRetention";
            this.numLogRetention.Size = new System.Drawing.Size(80, 23);
            this.numLogRetention.TabIndex = 5;
            this.numLogRetention.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numLogRetention.Maximum = new decimal(new int[] { 365, 0, 0, 0 });
            this.numLogRetention.Value = new decimal(new int[] { 30, 0, 0, 0 });
            //
            // lblLogRetentionUnit
            //
            this.lblLogRetentionUnit.AutoSize = true;
            this.lblLogRetentionUnit.Location = new System.Drawing.Point(185, 110);
            this.lblLogRetentionUnit.Size = new System.Drawing.Size(20, 15);
            this.lblLogRetentionUnit.TabIndex = 6;
            this.lblLogRetentionUnit.Text = "日";
            //
            // lblLogRetentionNote
            //
            this.lblLogRetentionNote.AutoSize = true;
            this.lblLogRetentionNote.ForeColor = System.Drawing.SystemColors.GrayText;
            this.lblLogRetentionNote.Location = new System.Drawing.Point(20, 145);
            this.lblLogRetentionNote.Size = new System.Drawing.Size(330, 15);
            this.lblLogRetentionNote.TabIndex = 7;
            this.lblLogRetentionNote.Text = "保存先の変更は次回 Manager / Launcher 起動時、保存日数の変更は次回 Manager 起動時に反映されます。";
            //
            // lblLogUnsaved (未保存マーカー、初期 hidden)
            //
            this.lblLogUnsaved.AutoSize = true;
            this.lblLogUnsaved.ForeColor = System.Drawing.Color.DarkOrange;
            this.lblLogUnsaved.Location = new System.Drawing.Point(20, 172);
            this.lblLogUnsaved.Name = "lblLogUnsaved";
            this.lblLogUnsaved.TabIndex = 8;
            this.lblLogUnsaved.Text = "● 未保存の変更があります";
            this.lblLogUnsaved.Visible = false;
            //
            // btnLogRevert (元に戻す、初期 disabled)
            //
            this.btnLogRevert.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLogRevert.Enabled = false;
            this.btnLogRevert.Location = new System.Drawing.Point(550, 166);
            this.btnLogRevert.Name = "btnLogRevert";
            this.btnLogRevert.Size = new System.Drawing.Size(90, 28);
            this.btnLogRevert.TabIndex = 9;
            this.btnLogRevert.Text = "元に戻す";
            this.btnLogRevert.UseVisualStyleBackColor = true;
            this.btnLogRevert.Click += new System.EventHandler(this.btnLogRevert_Click);
            //
            // btnLogApply (適用、初期 disabled)
            //
            this.btnLogApply.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnLogApply.Enabled = false;
            this.btnLogApply.Location = new System.Drawing.Point(650, 166);
            this.btnLogApply.Name = "btnLogApply";
            this.btnLogApply.Size = new System.Drawing.Size(90, 28);
            this.btnLogApply.TabIndex = 10;
            this.btnLogApply.Text = "適用";
            this.btnLogApply.UseVisualStyleBackColor = true;
            this.btnLogApply.Click += new System.EventHandler(this.btnLogApply_Click);
            //
            // grpBackup (top、バックアップ保存先 + 自動有効化 + 自動間隔 + 単位 + 世代数)
            //
            this.grpBackup.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpBackup.Controls.Add(this.lblBackupDest);
            this.grpBackup.Controls.Add(this.txtBackupDest);
            this.grpBackup.Controls.Add(this.btnBackupBrowse);
            this.grpBackup.Controls.Add(this.lblBackupDestHint);
            this.grpBackup.Controls.Add(this.chkBackupAutoEnabled);
            this.grpBackup.Controls.Add(this.lblBackupRetention);
            this.grpBackup.Controls.Add(this.numBackupRetention);
            this.grpBackup.Controls.Add(this.lblBackupRetentionUnit);
            this.grpBackup.Controls.Add(this.lblBackupUnsaved);
            this.grpBackup.Controls.Add(this.btnBackupRevert);
            this.grpBackup.Controls.Add(this.btnBackupApply);
            this.grpBackup.Location = new System.Drawing.Point(20, 20);
            this.grpBackup.Name = "grpBackup";
            this.grpBackup.Size = new System.Drawing.Size(760, 295);
            this.grpBackup.TabIndex = 0;
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
            this.txtBackupDest.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.txtBackupDest.Location = new System.Drawing.Point(20, 45);
            this.txtBackupDest.Size = new System.Drawing.Size(560, 23);
            this.txtBackupDest.TabIndex = 1;
            //
            // btnBackupBrowse
            //
            this.btnBackupBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
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
            this.lblBackupDestHint.Text = "空欄にするとデフォルト（DB ファイルの隣の backups/）。指定先に auto / manual のサブフォルダが作られ、その中に auto_日時.db / manual_日時.db が保存されます";
            //
            // chkBackupAutoEnabled (自動バックアップ有効/無効)
            //
            this.chkBackupAutoEnabled.AutoSize = true;
            this.chkBackupAutoEnabled.Checked = true;
            this.chkBackupAutoEnabled.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkBackupAutoEnabled.Location = new System.Drawing.Point(20, 110);
            this.chkBackupAutoEnabled.TabIndex = 4;
            this.chkBackupAutoEnabled.Text = "変更があったら自動でバックアップする";
            this.chkBackupAutoEnabled.UseVisualStyleBackColor = true;
            //
            // lblBackupRetention
            //
            this.lblBackupRetention.AutoSize = true;
            this.lblBackupRetention.Location = new System.Drawing.Point(20, 145);
            this.lblBackupRetention.Size = new System.Drawing.Size(95, 15);
            this.lblBackupRetention.TabIndex = 9;
            this.lblBackupRetention.Text = "保持する世代数:";
            //
            // numBackupRetention
            //
            this.numBackupRetention.Location = new System.Drawing.Point(20, 165);
            this.numBackupRetention.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            this.numBackupRetention.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numBackupRetention.Size = new System.Drawing.Size(80, 23);
            this.numBackupRetention.TabIndex = 10;
            this.numBackupRetention.Value = new decimal(new int[] { 30, 0, 0, 0 });
            //
            // lblBackupRetentionUnit
            //
            this.lblBackupRetentionUnit.AutoSize = true;
            this.lblBackupRetentionUnit.Location = new System.Drawing.Point(108, 168);
            this.lblBackupRetentionUnit.Size = new System.Drawing.Size(280, 15);
            this.lblBackupRetentionUnit.TabIndex = 11;
            this.lblBackupRetentionUnit.Text = "個 (これを超えた古いバックアップは自動削除されます)";
            //
            // lblBackupUnsaved (round9: ゲーム本体の enable/世代数 controls は撤去、DB と一括バックアップに統一) (未保存マーカー、初期 hidden)
            //
            this.lblBackupUnsaved.AutoSize = true;
            this.lblBackupUnsaved.ForeColor = System.Drawing.Color.DarkOrange;
            this.lblBackupUnsaved.Location = new System.Drawing.Point(20, 262);
            this.lblBackupUnsaved.Name = "lblBackupUnsaved";
            this.lblBackupUnsaved.TabIndex = 12;
            this.lblBackupUnsaved.Text = "● 未保存の変更があります";
            this.lblBackupUnsaved.Visible = false;
            //
            // btnBackupRevert (元に戻す、初期 disabled)
            //
            this.btnBackupRevert.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBackupRevert.Enabled = false;
            this.btnBackupRevert.Location = new System.Drawing.Point(550, 256);
            this.btnBackupRevert.Name = "btnBackupRevert";
            this.btnBackupRevert.Size = new System.Drawing.Size(90, 28);
            this.btnBackupRevert.TabIndex = 13;
            this.btnBackupRevert.Text = "元に戻す";
            this.btnBackupRevert.UseVisualStyleBackColor = true;
            this.btnBackupRevert.Click += new System.EventHandler(this.btnBackupRevert_Click);
            //
            // btnBackupApply (適用、初期 disabled)
            //
            this.btnBackupApply.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBackupApply.Enabled = false;
            this.btnBackupApply.Location = new System.Drawing.Point(650, 256);
            this.btnBackupApply.Name = "btnBackupApply";
            this.btnBackupApply.Size = new System.Drawing.Size(90, 28);
            this.btnBackupApply.TabIndex = 14;
            this.btnBackupApply.Text = "適用";
            this.btnBackupApply.UseVisualStyleBackColor = true;
            this.btnBackupApply.Click += new System.EventHandler(this.btnBackupApply_Click);
            //
            // grpDatabase (一番下手前、destructive)
            //
            this.grpDatabase.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpDatabase.Controls.Add(this.btnResetDatabase);
            this.grpDatabase.Location = new System.Drawing.Point(20, 540);
            this.grpDatabase.Name = "grpDatabase";
            this.grpDatabase.Size = new System.Drawing.Size(760, 80);
            this.grpDatabase.TabIndex = 2;
            this.grpDatabase.TabStop = false;
            this.grpDatabase.Text = "データベース";
            //
            // btnResetDatabase (赤色、destructive 強調)
            //
            this.btnResetDatabase.BackColor = System.Drawing.Color.IndianRed;
            this.btnResetDatabase.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold);
            this.btnResetDatabase.ForeColor = System.Drawing.Color.White;
            this.btnResetDatabase.Location = new System.Drawing.Point(20, 30);
            this.btnResetDatabase.Name = "btnResetDatabase";
            this.btnResetDatabase.Size = new System.Drawing.Size(180, 32);
            this.btnResetDatabase.TabIndex = 0;
            this.btnResetDatabase.Text = "データベースリセット";
            this.btnResetDatabase.UseVisualStyleBackColor = false;
            this.btnResetDatabase.Click += new System.EventHandler(this.btnResetDatabase_Click);
            //
            // grpInfo (一番下、informational)
            //
            this.grpInfo.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpInfo.Controls.Add(this.lblVersionInfo);
            this.grpInfo.Location = new System.Drawing.Point(20, 640);
            this.grpInfo.Name = "grpInfo";
            this.grpInfo.Size = new System.Drawing.Size(760, 200);
            this.grpInfo.TabIndex = 3;
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
            // SettingsSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this.grpBackup);
            this.Controls.Add(this.grpLog);
            this.Controls.Add(this.grpDatabase);
            this.Controls.Add(this.grpInfo);
            this.Name = "SettingsSectionPanel";
            this.Size = new System.Drawing.Size(800, 860);
            this.grpBackup.ResumeLayout(false);
            this.grpBackup.PerformLayout();
            this.grpLog.ResumeLayout(false);
            this.grpLog.PerformLayout();
            this.grpDatabase.ResumeLayout(false);
            this.grpInfo.ResumeLayout(false);
            this.grpInfo.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numLogRetention)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numBackupRetention)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox grpBackup;
        private System.Windows.Forms.Label lblBackupDest;
        private System.Windows.Forms.TextBox txtBackupDest;
        private System.Windows.Forms.Button btnBackupBrowse;
        private System.Windows.Forms.Label lblBackupDestHint;
        private System.Windows.Forms.CheckBox chkBackupAutoEnabled;
        private System.Windows.Forms.Label lblBackupRetention;
        private System.Windows.Forms.NumericUpDown numBackupRetention;
        private System.Windows.Forms.Label lblBackupRetentionUnit;
        private System.Windows.Forms.Label lblBackupUnsaved;
        private System.Windows.Forms.Button btnBackupRevert;
        private System.Windows.Forms.Button btnBackupApply;
        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.Label lblLogDest;
        private System.Windows.Forms.TextBox txtLogsRoot;
        private System.Windows.Forms.Button btnLogBrowse;
        private System.Windows.Forms.Label lblLogsRootHint;
        private System.Windows.Forms.Label lblLogRetentionPrompt;
        private System.Windows.Forms.NumericUpDown numLogRetention;
        private System.Windows.Forms.Label lblLogRetentionUnit;
        private System.Windows.Forms.Label lblLogRetentionNote;
        private System.Windows.Forms.Label lblLogUnsaved;
        private System.Windows.Forms.Button btnLogRevert;
        private System.Windows.Forms.Button btnLogApply;
        private System.Windows.Forms.GroupBox grpDatabase;
        private System.Windows.Forms.Button btnResetDatabase;
        private System.Windows.Forms.GroupBox grpInfo;
        private System.Windows.Forms.Label lblVersionInfo;
    }
}
