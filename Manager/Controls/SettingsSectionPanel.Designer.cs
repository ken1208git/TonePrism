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
            this.grpDatabase.SuspendLayout();
            this.grpInfo.SuspendLayout();
            this.grpLog.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numLogRetention)).BeginInit();
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
            // SettingsSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this.grpDatabase);
            this.Controls.Add(this.grpInfo);
            this.Controls.Add(this.grpLog);
            this.Name = "SettingsSectionPanel";
            this.Size = new System.Drawing.Size(800, 500);
            this.grpDatabase.ResumeLayout(false);
            this.grpInfo.ResumeLayout(false);
            this.grpInfo.PerformLayout();
            this.grpLog.ResumeLayout(false);
            this.grpLog.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numLogRetention)).EndInit();
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
    }
}
