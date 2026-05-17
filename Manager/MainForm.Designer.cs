namespace GCTonePrism.Manager
{
    partial class MainForm
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

        #region Windows フォーム デザイナーで生成されたコード

        private void InitializeComponent()
        {
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabGame = new System.Windows.Forms.TabPage();
            this.tabStore = new System.Windows.Forms.TabPage();
            this.tabBackup = new System.Windows.Forms.TabPage();
            this.tabLog = new System.Windows.Forms.TabPage();
            this.tabUpdate = new System.Windows.Forms.TabPage();
            this.tabSettings = new System.Windows.Forms.TabPage();
            this.statusStrip1.SuspendLayout();
            this.tabControl1.SuspendLayout();
            this.SuspendLayout();
            //
            // statusStrip1
            //
            this.statusStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatus});
            this.statusStrip1.Location = new System.Drawing.Point(0, 628);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(1100, 22);
            this.statusStrip1.TabIndex = 0;
            //
            // lblStatus
            //
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(56, 17);
            this.lblStatus.Text = "準備完了";
            //
            // tabControl1
            //
            this.tabControl1.Controls.Add(this.tabGame);
            this.tabControl1.Controls.Add(this.tabStore);
            this.tabControl1.Controls.Add(this.tabBackup);
            this.tabControl1.Controls.Add(this.tabLog);
            this.tabControl1.Controls.Add(this.tabUpdate);
            this.tabControl1.Controls.Add(this.tabSettings);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Font = new System.Drawing.Font("Meiryo UI", 9.5F);
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(1100, 628);
            this.tabControl1.TabIndex = 1;
            this.tabControl1.SelectedIndexChanged += new System.EventHandler(this.tabControl1_SelectedIndexChanged);
            //
            // tabGame
            //
            this.tabGame.Location = new System.Drawing.Point(4, 28);
            this.tabGame.Name = "tabGame";
            this.tabGame.Padding = new System.Windows.Forms.Padding(3);
            this.tabGame.Size = new System.Drawing.Size(1092, 596);
            this.tabGame.TabIndex = 0;
            this.tabGame.Text = "ゲーム";
            this.tabGame.UseVisualStyleBackColor = true;
            //
            // tabStore
            //
            this.tabStore.Location = new System.Drawing.Point(4, 28);
            this.tabStore.Name = "tabStore";
            this.tabStore.Padding = new System.Windows.Forms.Padding(3);
            this.tabStore.Size = new System.Drawing.Size(1092, 596);
            this.tabStore.TabIndex = 1;
            this.tabStore.Text = "ストア";
            this.tabStore.UseVisualStyleBackColor = true;
            //
            // tabBackup
            //
            this.tabBackup.Location = new System.Drawing.Point(4, 28);
            this.tabBackup.Name = "tabBackup";
            this.tabBackup.Padding = new System.Windows.Forms.Padding(3);
            this.tabBackup.Size = new System.Drawing.Size(1092, 596);
            this.tabBackup.TabIndex = 2;
            this.tabBackup.Text = "バックアップ";
            this.tabBackup.UseVisualStyleBackColor = true;
            //
            // tabLog
            //
            this.tabLog.Location = new System.Drawing.Point(4, 28);
            this.tabLog.Name = "tabLog";
            this.tabLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabLog.Size = new System.Drawing.Size(1092, 596);
            this.tabLog.TabIndex = 3;
            this.tabLog.Text = "ログ";
            this.tabLog.UseVisualStyleBackColor = true;
            //
            // tabUpdate
            //
            this.tabUpdate.Location = new System.Drawing.Point(4, 28);
            this.tabUpdate.Name = "tabUpdate";
            this.tabUpdate.Padding = new System.Windows.Forms.Padding(3);
            this.tabUpdate.Size = new System.Drawing.Size(1092, 596);
            this.tabUpdate.TabIndex = 4;
            this.tabUpdate.Text = "アップデート";
            this.tabUpdate.UseVisualStyleBackColor = true;
            //
            // tabSettings
            //
            this.tabSettings.Location = new System.Drawing.Point(4, 28);
            this.tabSettings.Name = "tabSettings";
            this.tabSettings.Padding = new System.Windows.Forms.Padding(3);
            this.tabSettings.Size = new System.Drawing.Size(1092, 596);
            this.tabSettings.TabIndex = 5;
            this.tabSettings.Text = "設定";
            this.tabSettings.UseVisualStyleBackColor = true;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1100, 650);
            this.Controls.Add(this.tabControl1);
            this.Controls.Add(this.statusStrip1);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "ゲームセンターTONE Prism 管理ソフト";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.tabControl1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabGame;
        private System.Windows.Forms.TabPage tabStore;
        private System.Windows.Forms.TabPage tabBackup;
        private System.Windows.Forms.TabPage tabLog;
        private System.Windows.Forms.TabPage tabUpdate;
        private System.Windows.Forms.TabPage tabSettings;
    }
}
