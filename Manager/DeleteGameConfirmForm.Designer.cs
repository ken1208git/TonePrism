namespace TonePrism.Manager
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
            this.lblHeader = new System.Windows.Forms.Label();
            this.lblItem1Title = new System.Windows.Forms.Label();
            this.lblItem1Detail = new System.Windows.Forms.Label();
            this.lblItem2Title = new System.Windows.Forms.Label();
            this.lblItem2Note1 = new System.Windows.Forms.Label();
            this.lblItem2Note2 = new System.Windows.Forms.Label();
            this.txtFolderPath = new System.Windows.Forms.TextBox();
            this.lblLauncherHint = new System.Windows.Forms.Label();
            this.lblFinalWarning = new System.Windows.Forms.Label();
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
            this.lblTitle.TabIndex = 0;
            //
            // lblGameId
            //
            this.lblGameId.AutoSize = true;
            this.lblGameId.ForeColor = System.Drawing.Color.DimGray;
            this.lblGameId.Location = new System.Drawing.Point(20, 50);
            this.lblGameId.Name = "lblGameId";
            this.lblGameId.TabIndex = 1;
            //
            // lblHeader
            //
            this.lblHeader.AutoSize = true;
            this.lblHeader.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblHeader.Location = new System.Drawing.Point(20, 80);
            this.lblHeader.Name = "lblHeader";
            this.lblHeader.TabIndex = 2;
            //
            // lblItem1Title
            //
            this.lblItem1Title.AutoSize = true;
            this.lblItem1Title.ForeColor = System.Drawing.Color.DarkRed;
            this.lblItem1Title.Location = new System.Drawing.Point(20, 105);
            this.lblItem1Title.Name = "lblItem1Title";
            this.lblItem1Title.TabIndex = 3;
            //
            // lblItem1Detail
            //
            this.lblItem1Detail.AutoSize = true;
            this.lblItem1Detail.ForeColor = System.Drawing.Color.DarkRed;
            this.lblItem1Detail.Location = new System.Drawing.Point(20, 122);
            this.lblItem1Detail.Name = "lblItem1Detail";
            this.lblItem1Detail.TabIndex = 4;
            //
            // lblItem2Title
            //
            this.lblItem2Title.AutoSize = true;
            this.lblItem2Title.Location = new System.Drawing.Point(20, 150);
            this.lblItem2Title.Name = "lblItem2Title";
            this.lblItem2Title.TabIndex = 5;
            //
            // lblItem2Note1
            //
            this.lblItem2Note1.AutoSize = true;
            this.lblItem2Note1.ForeColor = System.Drawing.Color.DarkRed;
            this.lblItem2Note1.Location = new System.Drawing.Point(20, 167);
            this.lblItem2Note1.Name = "lblItem2Note1";
            this.lblItem2Note1.TabIndex = 6;
            //
            // lblItem2Note2
            //
            this.lblItem2Note2.AutoSize = true;
            this.lblItem2Note2.ForeColor = System.Drawing.Color.DarkRed;
            this.lblItem2Note2.Location = new System.Drawing.Point(20, 184);
            this.lblItem2Note2.Name = "lblItem2Note2";
            this.lblItem2Note2.TabIndex = 7;
            //
            // txtFolderPath
            //
            this.txtFolderPath.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtFolderPath.Location = new System.Drawing.Point(40, 210);
            this.txtFolderPath.Name = "txtFolderPath";
            this.txtFolderPath.ReadOnly = true;
            this.txtFolderPath.Size = new System.Drawing.Size(540, 22);
            this.txtFolderPath.TabIndex = 8;
            this.txtFolderPath.BackColor = System.Drawing.SystemColors.Control;
            //
            // lblLauncherHint
            //
            this.lblLauncherHint.AutoSize = true;
            this.lblLauncherHint.ForeColor = System.Drawing.Color.DarkRed;
            this.lblLauncherHint.Location = new System.Drawing.Point(20, 240);
            this.lblLauncherHint.Name = "lblLauncherHint";
            this.lblLauncherHint.TabIndex = 9;
            this.lblLauncherHint.Text = "※ このゲームを起動中の Launcher があれば閉じてください（フォルダ削除に失敗する原因になります）";
            //
            // lblFinalWarning
            //
            this.lblFinalWarning.AutoSize = true;
            this.lblFinalWarning.Font = new System.Drawing.Font("MS UI Gothic", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblFinalWarning.ForeColor = System.Drawing.Color.DarkRed;
            this.lblFinalWarning.Location = new System.Drawing.Point(20, 270);
            this.lblFinalWarning.Name = "lblFinalWarning";
            this.lblFinalWarning.TabIndex = 9;
            this.lblFinalWarning.Text = "この操作は取り消せません。";
            //
            // btnDelete
            //
            this.btnDelete.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDelete.BackColor = System.Drawing.Color.IndianRed;
            this.btnDelete.Font = new System.Drawing.Font("MS UI Gothic", 9.5F, System.Drawing.FontStyle.Bold);
            this.btnDelete.ForeColor = System.Drawing.Color.White;
            this.btnDelete.Location = new System.Drawing.Point(360, 315);
            this.btnDelete.Name = "btnDelete";
            this.btnDelete.Size = new System.Drawing.Size(110, 35);
            this.btnDelete.TabIndex = 10;
            this.btnDelete.Text = "削除する";
            this.btnDelete.UseVisualStyleBackColor = false;
            this.btnDelete.Click += new System.EventHandler(this.btnDelete_Click);
            //
            // btnCancel
            //
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(480, 315);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 35);
            this.btnCancel.TabIndex = 11;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // DeleteGameConfirmForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(610, 370);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnDelete);
            this.Controls.Add(this.lblFinalWarning);
            this.Controls.Add(this.lblLauncherHint);
            this.Controls.Add(this.txtFolderPath);
            this.Controls.Add(this.lblItem2Note2);
            this.Controls.Add(this.lblItem2Note1);
            this.Controls.Add(this.lblItem2Title);
            this.Controls.Add(this.lblItem1Detail);
            this.Controls.Add(this.lblItem1Title);
            this.Controls.Add(this.lblHeader);
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
        private System.Windows.Forms.Label lblHeader;
        private System.Windows.Forms.Label lblItem1Title;
        private System.Windows.Forms.Label lblItem1Detail;
        private System.Windows.Forms.Label lblItem2Title;
        private System.Windows.Forms.Label lblItem2Note1;
        private System.Windows.Forms.Label lblItem2Note2;
        private System.Windows.Forms.TextBox txtFolderPath;
        private System.Windows.Forms.Label lblLauncherHint;
        private System.Windows.Forms.Label lblFinalWarning;
        private System.Windows.Forms.Button btnDelete;
        private System.Windows.Forms.Button btnCancel;
    }
}
