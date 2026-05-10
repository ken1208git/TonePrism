namespace GCTonePrism.Manager
{
    partial class ResetDatabaseConfirmForm
    {
        /// <summary>
        /// 必要なデザイナー変数です。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 使用中のリソースをすべてクリーンアップします。
        /// </summary>
        /// <param name="disposing">マネージド リソースを破棄する場合は true を指定し、その他の場合は false を指定します。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows フォーム デザイナーで生成されたコード

        /// <summary>
        /// デザイナー サポートに必要なメソッドです。このメソッドの内容を
        /// コード エディターで変更しないでください。
        /// </summary>
        private void InitializeComponent()
        {
            this.lblWarning = new System.Windows.Forms.Label();
            this.lblWarningDetail = new System.Windows.Forms.Label();
            this.lblGamesFolderWarning = new System.Windows.Forms.Label();
            this.lblConfirmationCode = new System.Windows.Forms.Label();
            this.txtConfirmationCode = new System.Windows.Forms.TextBox();
            this.lblInstruction = new System.Windows.Forms.Label();
            this.lblButtonWarning = new System.Windows.Forms.Label();
            this.btnConfirm = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblWarning
            // 
            this.lblWarning.AutoSize = true;
            this.lblWarning.Font = new System.Drawing.Font("MS UI Gothic", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblWarning.ForeColor = System.Drawing.Color.Red;
            this.lblWarning.Location = new System.Drawing.Point(20, 20);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(460, 20);
            this.lblWarning.TabIndex = 0;
            this.lblWarning.Text = "【警告】データベースリセット - すべてのデータが完全に削除されます";
            // 
            // lblWarningDetail
            // 
            this.lblWarningDetail.AutoSize = true;
            this.lblWarningDetail.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblWarningDetail.ForeColor = System.Drawing.Color.DarkRed;
            this.lblWarningDetail.Location = new System.Drawing.Point(20, 45);
            this.lblWarningDetail.Name = "lblWarningDetail";
            this.lblWarningDetail.Size = new System.Drawing.Size(450, 12);
            this.lblWarningDetail.TabIndex = 7;
            this.lblWarningDetail.Text = "・すべてのゲーム情報・プレイ記録・アンケート回答（製作者情報・バージョン情報も含む）";
            // 
            // lblGamesFolderWarning
            // 
            this.lblGamesFolderWarning.AutoSize = true;
            this.lblGamesFolderWarning.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblGamesFolderWarning.ForeColor = System.Drawing.Color.DarkRed;
            this.lblGamesFolderWarning.Location = new System.Drawing.Point(20, 62);
            this.lblGamesFolderWarning.Name = "lblGamesFolderWarning";
            this.lblGamesFolderWarning.Size = new System.Drawing.Size(260, 12);
            this.lblGamesFolderWarning.TabIndex = 8;
            this.lblGamesFolderWarning.Text = "・Manager に登録されている全ゲームのファイル（実行ファイル・サムネイル・背景画像など）\r\n　 ※ 部員の開発フォルダには影響しません。リセット前にバックアップ機能でスナップショット取得を推奨。";
            // 
            // lblConfirmationCode
            // 
            this.lblConfirmationCode.AutoSize = true;
            this.lblConfirmationCode.Font = new System.Drawing.Font("MS UI Gothic", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblConfirmationCode.Location = new System.Drawing.Point(20, 100);
            this.lblConfirmationCode.Name = "lblConfirmationCode";
            this.lblConfirmationCode.Size = new System.Drawing.Size(125, 17);
            this.lblConfirmationCode.TabIndex = 1;
            this.lblConfirmationCode.Text = "確認コード: XXXX";
            // 
            // txtConfirmationCode
            // 
            this.txtConfirmationCode.Font = new System.Drawing.Font("MS UI Gothic", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.txtConfirmationCode.Location = new System.Drawing.Point(20, 125);
            this.txtConfirmationCode.MaxLength = 10;
            this.txtConfirmationCode.Name = "txtConfirmationCode";
            this.txtConfirmationCode.Size = new System.Drawing.Size(150, 27);
            this.txtConfirmationCode.TabIndex = 2;
            this.txtConfirmationCode.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // lblInstruction
            // 
            this.lblInstruction.AutoSize = true;
            this.lblInstruction.Location = new System.Drawing.Point(20, 160);
            this.lblInstruction.Name = "lblInstruction";
            this.lblInstruction.Size = new System.Drawing.Size(550, 15);
            this.lblInstruction.TabIndex = 3;
            this.lblInstruction.Text = "上記の確認コードを入力してください。コードを間違えると新しいコードが生成されます。";
            // 
            // lblButtonWarning
            // 
            this.lblButtonWarning.AutoSize = true;
            this.lblButtonWarning.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblButtonWarning.ForeColor = System.Drawing.Color.OrangeRed;
            this.lblButtonWarning.Location = new System.Drawing.Point(20, 180);
            this.lblButtonWarning.Name = "lblButtonWarning";
            this.lblButtonWarning.Size = new System.Drawing.Size(350, 12);
            this.lblButtonWarning.TabIndex = 6;
            this.lblButtonWarning.Text = "※リセット実行ボタンは押そうとすると逃げますので、頑張って押してください";
            // 
            // btnConfirm
            // 
            this.btnConfirm.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnConfirm.BackColor = System.Drawing.Color.Red;
            this.btnConfirm.Font = new System.Drawing.Font("MS UI Gothic", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.btnConfirm.ForeColor = System.Drawing.Color.White;
            this.btnConfirm.Location = new System.Drawing.Point(340, 245);
            this.btnConfirm.Name = "btnConfirm";
            this.btnConfirm.Size = new System.Drawing.Size(150, 40);
            this.btnConfirm.TabIndex = 4;
            this.btnConfirm.Text = "リセット実行";
            this.btnConfirm.UseVisualStyleBackColor = false;
            this.btnConfirm.Click += new System.EventHandler(this.btnConfirm_Click);
            this.btnConfirm.MouseEnter += new System.EventHandler(this.btnConfirm_MouseEnter);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(500, 245);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(100, 40);
            this.btnCancel.TabIndex = 5;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // ResetDatabaseConfirmForm
            // 
            this.AcceptButton = this.btnConfirm;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(650, 325);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnConfirm);
            this.Controls.Add(this.lblButtonWarning);
            this.Controls.Add(this.lblInstruction);
            this.Controls.Add(this.txtConfirmationCode);
            this.Controls.Add(this.lblConfirmationCode);
            this.Controls.Add(this.lblGamesFolderWarning);
            this.Controls.Add(this.lblWarningDetail);
            this.Controls.Add(this.lblWarning);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ResetDatabaseConfirmForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "データベースリセット確認";
            this.Load += new System.EventHandler(this.ResetDatabaseConfirmForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblWarning;
        private System.Windows.Forms.Label lblWarningDetail;
        private System.Windows.Forms.Label lblGamesFolderWarning;
        private System.Windows.Forms.Label lblConfirmationCode;
        private System.Windows.Forms.TextBox txtConfirmationCode;
        private System.Windows.Forms.Label lblInstruction;
        private System.Windows.Forms.Label lblButtonWarning;
        private System.Windows.Forms.Button btnConfirm;
        private System.Windows.Forms.Button btnCancel;
    }
}

