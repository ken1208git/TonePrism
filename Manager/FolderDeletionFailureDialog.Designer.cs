namespace TonePrism.Manager
{
    partial class FolderDeletionFailureDialog
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
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblExplain = new System.Windows.Forms.Label();
            this.txtFolderPath = new System.Windows.Forms.TextBox();
            this.lblHint = new System.Windows.Forms.Label();
            this.lblErrorHeader = new System.Windows.Forms.Label();
            this.txtErrorDetail = new System.Windows.Forms.TextBox();
            this.btnRetry = new System.Windows.Forms.Button();
            this.btnGiveUp = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("MS UI Gothic", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblTitle.ForeColor = System.Drawing.Color.DarkRed;
            this.lblTitle.Location = new System.Drawing.Point(20, 20);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "⚠ フォルダの削除に失敗しました";
            //
            // lblExplain
            //
            this.lblExplain.AutoSize = true;
            this.lblExplain.Location = new System.Drawing.Point(20, 55);
            this.lblExplain.Name = "lblExplain";
            this.lblExplain.TabIndex = 1;
            this.lblExplain.Text = "以下のフォルダを削除しようとしましたが、Launcher などがフォルダ内のファイルを\r\n掴んでいる可能性があります。";
            //
            // txtFolderPath
            //
            this.txtFolderPath.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtFolderPath.Location = new System.Drawing.Point(20, 100);
            this.txtFolderPath.Name = "txtFolderPath";
            this.txtFolderPath.ReadOnly = true;
            this.txtFolderPath.Size = new System.Drawing.Size(560, 22);
            this.txtFolderPath.TabIndex = 2;
            this.txtFolderPath.BackColor = System.Drawing.SystemColors.Control;
            //
            // lblHint
            //
            this.lblHint.AutoSize = true;
            this.lblHint.ForeColor = System.Drawing.Color.DimGray;
            this.lblHint.Location = new System.Drawing.Point(20, 135);
            this.lblHint.Name = "lblHint";
            this.lblHint.TabIndex = 3;
            this.lblHint.Text = "⇒ Launcher を閉じてから「再試行」を押すと、再度削除を試みます。";
            //
            // lblErrorHeader
            //
            this.lblErrorHeader.AutoSize = true;
            this.lblErrorHeader.Location = new System.Drawing.Point(20, 170);
            this.lblErrorHeader.Name = "lblErrorHeader";
            this.lblErrorHeader.TabIndex = 4;
            this.lblErrorHeader.Text = "失敗の詳細:";
            //
            // txtErrorDetail
            //
            this.txtErrorDetail.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtErrorDetail.Location = new System.Drawing.Point(20, 190);
            this.txtErrorDetail.Multiline = true;
            this.txtErrorDetail.Name = "txtErrorDetail";
            this.txtErrorDetail.ReadOnly = true;
            this.txtErrorDetail.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtErrorDetail.Size = new System.Drawing.Size(560, 90);
            this.txtErrorDetail.TabIndex = 5;
            this.txtErrorDetail.BackColor = System.Drawing.SystemColors.Control;
            //
            // btnRetry
            //
            this.btnRetry.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRetry.BackColor = System.Drawing.Color.SteelBlue;
            this.btnRetry.Font = new System.Drawing.Font("MS UI Gothic", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnRetry.ForeColor = System.Drawing.Color.White;
            this.btnRetry.Location = new System.Drawing.Point(320, 305);
            this.btnRetry.Name = "btnRetry";
            this.btnRetry.Size = new System.Drawing.Size(110, 35);
            this.btnRetry.TabIndex = 6;
            this.btnRetry.Text = "再試行";
            this.btnRetry.UseVisualStyleBackColor = false;
            this.btnRetry.Click += new System.EventHandler(this.btnRetry_Click);
            //
            // btnGiveUp
            //
            this.btnGiveUp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnGiveUp.Location = new System.Drawing.Point(480, 305);
            this.btnGiveUp.Name = "btnGiveUp";
            this.btnGiveUp.Size = new System.Drawing.Size(100, 35);
            this.btnGiveUp.TabIndex = 7;
            this.btnGiveUp.Text = "諦める";
            this.btnGiveUp.UseVisualStyleBackColor = true;
            this.btnGiveUp.Click += new System.EventHandler(this.btnGiveUp_Click);
            //
            // FolderDeletionFailureDialog
            //
            this.AcceptButton = this.btnRetry;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnGiveUp;
            this.ClientSize = new System.Drawing.Size(610, 360);
            this.Controls.Add(this.btnGiveUp);
            this.Controls.Add(this.btnRetry);
            this.Controls.Add(this.txtErrorDetail);
            this.Controls.Add(this.lblErrorHeader);
            this.Controls.Add(this.lblHint);
            this.Controls.Add(this.txtFolderPath);
            this.Controls.Add(this.lblExplain);
            this.Controls.Add(this.lblTitle);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FolderDeletionFailureDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "フォルダ削除に失敗しました";
            this.Load += new System.EventHandler(this.FolderDeletionFailureDialog_Load);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblExplain;
        private System.Windows.Forms.TextBox txtFolderPath;
        private System.Windows.Forms.Label lblHint;
        private System.Windows.Forms.Label lblErrorHeader;
        private System.Windows.Forms.TextBox txtErrorDetail;
        private System.Windows.Forms.Button btnRetry;
        private System.Windows.Forms.Button btnGiveUp;
    }
}
