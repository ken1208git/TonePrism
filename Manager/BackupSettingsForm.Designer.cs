namespace GCTonePrism.Manager
{
    partial class BackupSettingsForm
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
            this.lblDestPath = new System.Windows.Forms.Label();
            this.txtDestPath = new System.Windows.Forms.TextBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.lblDestHint = new System.Windows.Forms.Label();
            this.lblInterval = new System.Windows.Forms.Label();
            this.numInterval = new System.Windows.Forms.NumericUpDown();
            this.lblIntervalUnit = new System.Windows.Forms.Label();
            this.lblRetention = new System.Windows.Forms.Label();
            this.numRetention = new System.Windows.Forms.NumericUpDown();
            this.lblRetentionUnit = new System.Windows.Forms.Label();
            this.btnOk = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRetention)).BeginInit();
            this.SuspendLayout();
            //
            // lblDestPath
            //
            this.lblDestPath.AutoSize = true;
            this.lblDestPath.Location = new System.Drawing.Point(20, 25);
            this.lblDestPath.Name = "lblDestPath";
            this.lblDestPath.Size = new System.Drawing.Size(115, 15);
            this.lblDestPath.TabIndex = 0;
            this.lblDestPath.Text = "バックアップ保存先:";
            //
            // txtDestPath
            //
            this.txtDestPath.Location = new System.Drawing.Point(20, 45);
            this.txtDestPath.Name = "txtDestPath";
            this.txtDestPath.Size = new System.Drawing.Size(440, 23);
            this.txtDestPath.TabIndex = 1;
            //
            // btnBrowse
            //
            this.btnBrowse.Location = new System.Drawing.Point(470, 44);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(80, 26);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "参照...";
            this.btnBrowse.UseVisualStyleBackColor = true;
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            //
            // lblDestHint
            //
            this.lblDestHint.AutoSize = true;
            this.lblDestHint.ForeColor = System.Drawing.Color.DimGray;
            this.lblDestHint.Location = new System.Drawing.Point(20, 75);
            this.lblDestHint.Name = "lblDestHint";
            this.lblDestHint.Size = new System.Drawing.Size(420, 15);
            this.lblDestHint.TabIndex = 3;
            this.lblDestHint.Text = "空欄にするとデフォルト（データベースファイルの隣の backups/ フォルダ）が使われます";
            //
            // lblInterval
            //
            this.lblInterval.AutoSize = true;
            this.lblInterval.Location = new System.Drawing.Point(20, 115);
            this.lblInterval.Name = "lblInterval";
            this.lblInterval.Size = new System.Drawing.Size(180, 15);
            this.lblInterval.TabIndex = 4;
            this.lblInterval.Text = "自動バックアップ間隔 (Manager起動時にチェック):";
            //
            // numInterval
            //
            this.numInterval.Location = new System.Drawing.Point(20, 135);
            this.numInterval.Maximum = new decimal(new int[] { 720, 0, 0, 0 });
            this.numInterval.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numInterval.Name = "numInterval";
            this.numInterval.Size = new System.Drawing.Size(80, 23);
            this.numInterval.TabIndex = 5;
            this.numInterval.Value = new decimal(new int[] { 24, 0, 0, 0 });
            //
            // lblIntervalUnit
            //
            this.lblIntervalUnit.AutoSize = true;
            this.lblIntervalUnit.Location = new System.Drawing.Point(108, 138);
            this.lblIntervalUnit.Name = "lblIntervalUnit";
            this.lblIntervalUnit.Size = new System.Drawing.Size(160, 15);
            this.lblIntervalUnit.TabIndex = 6;
            this.lblIntervalUnit.Text = "時間以上経過していたら実行";
            //
            // lblRetention
            //
            this.lblRetention.AutoSize = true;
            this.lblRetention.Location = new System.Drawing.Point(20, 175);
            this.lblRetention.Name = "lblRetention";
            this.lblRetention.Size = new System.Drawing.Size(95, 15);
            this.lblRetention.TabIndex = 7;
            this.lblRetention.Text = "保持する世代数:";
            //
            // numRetention
            //
            this.numRetention.Location = new System.Drawing.Point(20, 195);
            this.numRetention.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            this.numRetention.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numRetention.Name = "numRetention";
            this.numRetention.Size = new System.Drawing.Size(80, 23);
            this.numRetention.TabIndex = 8;
            this.numRetention.Value = new decimal(new int[] { 30, 0, 0, 0 });
            //
            // lblRetentionUnit
            //
            this.lblRetentionUnit.AutoSize = true;
            this.lblRetentionUnit.Location = new System.Drawing.Point(108, 198);
            this.lblRetentionUnit.Name = "lblRetentionUnit";
            this.lblRetentionUnit.Size = new System.Drawing.Size(280, 15);
            this.lblRetentionUnit.TabIndex = 9;
            this.lblRetentionUnit.Text = "個 (これを超えた古いバックアップは自動削除されます)";
            //
            // btnOk
            //
            this.btnOk.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOk.Location = new System.Drawing.Point(370, 240);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(90, 32);
            this.btnOk.TabIndex = 10;
            this.btnOk.Text = "保存";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new System.EventHandler(this.btnOk_Click);
            //
            // btnCancel
            //
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(470, 240);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 32);
            this.btnCancel.TabIndex = 11;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            //
            // BackupSettingsForm
            //
            this.AcceptButton = this.btnOk;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(580, 290);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.lblRetentionUnit);
            this.Controls.Add(this.numRetention);
            this.Controls.Add(this.lblRetention);
            this.Controls.Add(this.lblIntervalUnit);
            this.Controls.Add(this.numInterval);
            this.Controls.Add(this.lblInterval);
            this.Controls.Add(this.lblDestHint);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.txtDestPath);
            this.Controls.Add(this.lblDestPath);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "BackupSettingsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "バックアップ設定";
            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numRetention)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblDestPath;
        private System.Windows.Forms.TextBox txtDestPath;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Label lblDestHint;
        private System.Windows.Forms.Label lblInterval;
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.Label lblIntervalUnit;
        private System.Windows.Forms.Label lblRetention;
        private System.Windows.Forms.NumericUpDown numRetention;
        private System.Windows.Forms.Label lblRetentionUnit;
        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Button btnCancel;
    }
}
