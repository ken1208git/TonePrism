namespace GCTonePrism.Manager
{
    partial class DeleteGameConfirmForm
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
            this.lblGameId = new System.Windows.Forms.Label();
            this.lblWarning = new System.Windows.Forms.Label();
            this.chkDeleteFolder = new System.Windows.Forms.CheckBox();
            this.txtFolderPath = new System.Windows.Forms.TextBox();
            this.lblFolderStatus = new System.Windows.Forms.Label();
            this.btnDelete = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("MS UI Gothic", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblTitle.Location = new System.Drawing.Point(20, 20);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(0, 18);
            this.lblTitle.TabIndex = 0;
            //
            // lblGameId
            //
            this.lblGameId.AutoSize = true;
            this.lblGameId.ForeColor = System.Drawing.Color.DimGray;
            this.lblGameId.Location = new System.Drawing.Point(20, 50);
            this.lblGameId.Name = "lblGameId";
            this.lblGameId.Size = new System.Drawing.Size(0, 15);
            this.lblGameId.TabIndex = 1;
            //
            // lblWarning
            //
            this.lblWarning.AutoSize = true;
            this.lblWarning.ForeColor = System.Drawing.Color.DarkRed;
            this.lblWarning.Location = new System.Drawing.Point(20, 80);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(289, 15);
            this.lblWarning.TabIndex = 2;
            this.lblWarning.Text = "DB からゲーム情報・関連レコードが削除されます。この操作は取り消せません。";
            //
            // chkDeleteFolder
            //
            this.chkDeleteFolder.AutoSize = true;
            this.chkDeleteFolder.Location = new System.Drawing.Point(20, 115);
            this.chkDeleteFolder.Name = "chkDeleteFolder";
            this.chkDeleteFolder.Size = new System.Drawing.Size(225, 19);
            this.chkDeleteFolder.TabIndex = 3;
            this.chkDeleteFolder.Text = "ゲームフォルダ（games/{game_id}/）も一緒に削除する";
            this.chkDeleteFolder.UseVisualStyleBackColor = true;
            //
            // txtFolderPath
            //
            this.txtFolderPath.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtFolderPath.Location = new System.Drawing.Point(40, 140);
            this.txtFolderPath.Name = "txtFolderPath";
            this.txtFolderPath.ReadOnly = true;
            this.txtFolderPath.Size = new System.Drawing.Size(540, 22);
            this.txtFolderPath.TabIndex = 4;
            this.txtFolderPath.BackColor = System.Drawing.SystemColors.Control;
            //
            // lblFolderStatus
            //
            this.lblFolderStatus.AutoSize = true;
            this.lblFolderStatus.Location = new System.Drawing.Point(40, 168);
            this.lblFolderStatus.Name = "lblFolderStatus";
            this.lblFolderStatus.Size = new System.Drawing.Size(0, 15);
            this.lblFolderStatus.TabIndex = 5;
            //
            // btnDelete
            //
            this.btnDelete.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDelete.BackColor = System.Drawing.Color.IndianRed;
            this.btnDelete.Font = new System.Drawing.Font("MS UI Gothic", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnDelete.ForeColor = System.Drawing.Color.White;
            this.btnDelete.Location = new System.Drawing.Point(360, 210);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(110, 35);
            this.btnDelete.TabIndex = 6;
            this.btnDelete.Text = "削除する";
            this.btnDelete.UseVisualStyleBackColor = false;
            this.btnDelete.Click += new System.EventHandler(this.btnDelete_Click);
            //
            // btnCancel
            //
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(480, 210);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 35);
            this.btnCancel.TabIndex = 7;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // DeleteGameConfirmForm
            //
            this.AcceptButton = this.btnCancel;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(610, 265);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnDelete);
            this.Controls.Add(this.lblFolderStatus);
            this.Controls.Add(this.txtFolderPath);
            this.Controls.Add(this.chkDeleteFolder);
            this.Controls.Add(this.lblWarning);
            this.Controls.Add(this.lblGameId);
            this.Controls.Add(this.lblTitle);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DeleteGameConfirmForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "ゲーム削除確認";
            this.Load += new System.EventHandler(this.DeleteGameConfirmForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblGameId;
        private System.Windows.Forms.Label lblWarning;
        private System.Windows.Forms.CheckBox chkDeleteFolder;
        private System.Windows.Forms.TextBox txtFolderPath;
        private System.Windows.Forms.Label lblFolderStatus;
        private System.Windows.Forms.Button btnDelete;
        private System.Windows.Forms.Button btnCancel;
    }
}
