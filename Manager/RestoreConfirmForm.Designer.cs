namespace TonePrism.Manager
{
    partial class RestoreConfirmForm
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
            this.lblWarning = new System.Windows.Forms.Label();
            this.lblTargetFile = new System.Windows.Forms.Label();
            this.lblWarningDetail1 = new System.Windows.Forms.Label();
            this.lblWarningDetail2 = new System.Windows.Forms.Label();
            this.lblWarningDetail3 = new System.Windows.Forms.Label();
            this.lblAssetInfo = new System.Windows.Forms.Label();
            this.lblConfirmationCode = new System.Windows.Forms.Label();
            this.txtConfirmationCode = new System.Windows.Forms.TextBox();
            this.lblInstruction = new System.Windows.Forms.Label();
            this.btnConfirm = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // lblWarning
            //
            this.lblWarning.AutoSize = true;
            this.lblWarning.Font = new System.Drawing.Font("MS UI Gothic", 12F, System.Drawing.FontStyle.Bold);
            this.lblWarning.ForeColor = System.Drawing.Color.Red;
            this.lblWarning.Location = new System.Drawing.Point(20, 20);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(420, 20);
            this.lblWarning.TabIndex = 0;
            this.lblWarning.Text = "【警告】データベースを復元します";
            //
            // lblTargetFile
            //
            this.lblTargetFile.AutoEllipsis = true;
            this.lblTargetFile.Location = new System.Drawing.Point(20, 50);
            this.lblTargetFile.Name = "lblTargetFile";
            // 4 行 (対象 / 作成日時 / サイズ / フルパス) を縦に収めるため 88px に拡張。
            // 旧 40px だと 2 行強しか入らずサイズ・フルパス行が見切れていた。
            this.lblTargetFile.Size = new System.Drawing.Size(620, 88);
            this.lblTargetFile.TabIndex = 1;
            this.lblTargetFile.Text = "対象: ";
            //
            // lblWarningDetail1
            //
            this.lblWarningDetail1.AutoSize = true;
            this.lblWarningDetail1.ForeColor = System.Drawing.Color.DarkRed;
            this.lblWarningDetail1.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold);
            this.lblWarningDetail1.Location = new System.Drawing.Point(20, 150);
            this.lblWarningDetail1.Name = "lblWarningDetail1";
            this.lblWarningDetail1.Size = new System.Drawing.Size(550, 14);
            this.lblWarningDetail1.TabIndex = 2;
            this.lblWarningDetail1.Text = "・実行前に、すべての展示PCのLauncherを終了してください";
            //
            // lblWarningDetail2
            //
            this.lblWarningDetail2.AutoSize = true;
            this.lblWarningDetail2.ForeColor = System.Drawing.Color.DarkRed;
            this.lblWarningDetail2.Location = new System.Drawing.Point(20, 170);
            this.lblWarningDetail2.Name = "lblWarningDetail2";
            this.lblWarningDetail2.Size = new System.Drawing.Size(560, 14);
            this.lblWarningDetail2.TabIndex = 3;
            this.lblWarningDetail2.Text = "・現在のデータベースは安全のため自動的に退避されます (safety_*.db)";
            //
            // lblWarningDetail3
            //
            this.lblWarningDetail3.AutoSize = true;
            this.lblWarningDetail3.ForeColor = System.Drawing.Color.DarkRed;
            this.lblWarningDetail3.Location = new System.Drawing.Point(20, 190);
            this.lblWarningDetail3.Name = "lblWarningDetail3";
            this.lblWarningDetail3.Size = new System.Drawing.Size(560, 14);
            this.lblWarningDetail3.TabIndex = 4;
            this.lblWarningDetail3.Text = "・復元後の状態に問題があれば、退避ファイルから手動で戻すことが可能です";
            //
            // lblAssetInfo
            //
            this.lblAssetInfo.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold);
            this.lblAssetInfo.ForeColor = System.Drawing.Color.DarkRed;
            this.lblAssetInfo.Location = new System.Drawing.Point(20, 216);
            this.lblAssetInfo.Name = "lblAssetInfo";
            this.lblAssetInfo.Size = new System.Drawing.Size(620, 80);
            this.lblAssetInfo.TabIndex = 5;
            this.lblAssetInfo.Text = "";
            //
            // lblConfirmationCode
            //
            this.lblConfirmationCode.AutoSize = true;
            this.lblConfirmationCode.Font = new System.Drawing.Font("MS UI Gothic", 10F, System.Drawing.FontStyle.Bold);
            this.lblConfirmationCode.Location = new System.Drawing.Point(20, 304);
            this.lblConfirmationCode.Name = "lblConfirmationCode";
            this.lblConfirmationCode.Size = new System.Drawing.Size(125, 17);
            this.lblConfirmationCode.TabIndex = 6;
            this.lblConfirmationCode.Text = "確認コード: XXXX";
            //
            // txtConfirmationCode
            //
            this.txtConfirmationCode.Font = new System.Drawing.Font("MS UI Gothic", 12F, System.Drawing.FontStyle.Bold);
            this.txtConfirmationCode.Location = new System.Drawing.Point(20, 329);
            this.txtConfirmationCode.MaxLength = 10;
            this.txtConfirmationCode.Name = "txtConfirmationCode";
            this.txtConfirmationCode.Size = new System.Drawing.Size(150, 27);
            this.txtConfirmationCode.TabIndex = 7;
            this.txtConfirmationCode.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            //
            // lblInstruction
            //
            this.lblInstruction.AutoSize = true;
            this.lblInstruction.Location = new System.Drawing.Point(20, 364);
            this.lblInstruction.Name = "lblInstruction";
            this.lblInstruction.Size = new System.Drawing.Size(550, 15);
            this.lblInstruction.TabIndex = 8;
            this.lblInstruction.Text = "上記の確認コードを入力してください。コードを間違えると新しいコードが生成されます。";
            //
            // btnConfirm
            //
            this.btnConfirm.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnConfirm.BackColor = System.Drawing.Color.Red;
            this.btnConfirm.Font = new System.Drawing.Font("MS UI Gothic", 10F, System.Drawing.FontStyle.Bold);
            this.btnConfirm.ForeColor = System.Drawing.Color.White;
            this.btnConfirm.Location = new System.Drawing.Point(380, 410);
            this.btnConfirm.Name = "btnConfirm";
            this.btnConfirm.Size = new System.Drawing.Size(150, 40);
            this.btnConfirm.TabIndex = 9;
            this.btnConfirm.Text = "復元実行";
            this.btnConfirm.UseVisualStyleBackColor = false;
            this.btnConfirm.Click += new System.EventHandler(this.btnConfirm_Click);
            //
            // btnCancel
            //
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(540, 410);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 40);
            this.btnCancel.TabIndex = 10;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // RestoreConfirmForm
            //
            this.AcceptButton = this.btnConfirm;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(660, 470);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnConfirm);
            this.Controls.Add(this.lblInstruction);
            this.Controls.Add(this.txtConfirmationCode);
            this.Controls.Add(this.lblConfirmationCode);
            this.Controls.Add(this.lblAssetInfo);
            this.Controls.Add(this.lblWarningDetail3);
            this.Controls.Add(this.lblWarningDetail2);
            this.Controls.Add(this.lblWarningDetail1);
            this.Controls.Add(this.lblTargetFile);
            this.Controls.Add(this.lblWarning);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "RestoreConfirmForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "データベース復元の確認";
            this.Load += new System.EventHandler(this.RestoreConfirmForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblWarning;
        private System.Windows.Forms.Label lblTargetFile;
        private System.Windows.Forms.Label lblWarningDetail1;
        private System.Windows.Forms.Label lblWarningDetail2;
        private System.Windows.Forms.Label lblWarningDetail3;
        private System.Windows.Forms.Label lblAssetInfo;
        private System.Windows.Forms.Label lblConfirmationCode;
        private System.Windows.Forms.TextBox txtConfirmationCode;
        private System.Windows.Forms.Label lblInstruction;
        private System.Windows.Forms.Button btnConfirm;
        private System.Windows.Forms.Button btnCancel;
    }
}
