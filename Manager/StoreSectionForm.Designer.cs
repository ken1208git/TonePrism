namespace GCTonePrism.Manager
{
    partial class StoreSectionForm
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
            this.txtTitle = new System.Windows.Forms.TextBox();
            this.lblSectionType = new System.Windows.Forms.Label();
            this.cmbSectionType = new System.Windows.Forms.ComboBox();
            this.lblSectionSource = new System.Windows.Forms.Label();
            this.cmbSectionSource = new System.Windows.Forms.ComboBox();
            this.lblGenre = new System.Windows.Forms.Label();
            this.cmbGenre = new System.Windows.Forms.ComboBox();
            this.lblSourceValue = new System.Windows.Forms.Label();
            this.nudSourceValue = new System.Windows.Forms.NumericUpDown();
            this.lblMaxDisplayCount = new System.Windows.Forms.Label();
            this.nudMaxDisplayCount = new System.Windows.Forms.NumericUpDown();
            this.chkIsVisible = new System.Windows.Forms.CheckBox();
            this.grpGameAssignment = new System.Windows.Forms.GroupBox();
            this.lblAvailable = new System.Windows.Forms.Label();
            this.lstAvailable = new System.Windows.Forms.ListBox();
            this.lblAssigned = new System.Windows.Forms.Label();
            this.lstAssigned = new System.Windows.Forms.ListBox();
            this.btnAdd = new System.Windows.Forms.Button();
            this.btnRemove = new System.Windows.Forms.Button();
            this.btnUp = new System.Windows.Forms.Button();
            this.btnDown = new System.Windows.Forms.Button();
            this.lblDisplayText = new System.Windows.Forms.Label();
            this.txtDisplayText = new System.Windows.Forms.TextBox();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.nudSourceValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudMaxDisplayCount)).BeginInit();
            this.grpGameAssignment.SuspendLayout();
            this.SuspendLayout();
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Location = new System.Drawing.Point(12, 15);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(44, 15);
            this.lblTitle.Text = "タイトル:";
            //
            // txtTitle
            //
            this.txtTitle.Location = new System.Drawing.Point(100, 12);
            this.txtTitle.Name = "txtTitle";
            this.txtTitle.Size = new System.Drawing.Size(350, 23);
            //
            // lblSectionType
            //
            this.lblSectionType.AutoSize = true;
            this.lblSectionType.Location = new System.Drawing.Point(12, 48);
            this.lblSectionType.Name = "lblSectionType";
            this.lblSectionType.Size = new System.Drawing.Size(37, 15);
            this.lblSectionType.Text = "タイプ:";
            //
            // cmbSectionType
            //
            this.cmbSectionType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbSectionType.Location = new System.Drawing.Point(100, 45);
            this.cmbSectionType.Name = "cmbSectionType";
            this.cmbSectionType.Size = new System.Drawing.Size(200, 23);
            //
            // lblSectionSource
            //
            this.lblSectionSource.AutoSize = true;
            this.lblSectionSource.Location = new System.Drawing.Point(12, 81);
            this.lblSectionSource.Name = "lblSectionSource";
            this.lblSectionSource.Size = new System.Drawing.Size(37, 15);
            this.lblSectionSource.Text = "ソース:";
            //
            // cmbSectionSource
            //
            this.cmbSectionSource.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbSectionSource.Location = new System.Drawing.Point(100, 78);
            this.cmbSectionSource.Name = "cmbSectionSource";
            this.cmbSectionSource.Size = new System.Drawing.Size(200, 23);
            //
            // lblGenre
            //
            this.lblGenre.AutoSize = true;
            this.lblGenre.Location = new System.Drawing.Point(12, 114);
            this.lblGenre.Name = "lblGenre";
            this.lblGenre.Size = new System.Drawing.Size(44, 15);
            this.lblGenre.Text = "ジャンル:";
            this.lblGenre.Visible = false;
            //
            // cmbGenre
            //
            this.cmbGenre.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbGenre.Location = new System.Drawing.Point(100, 111);
            this.cmbGenre.Name = "cmbGenre";
            this.cmbGenre.Size = new System.Drawing.Size(200, 23);
            this.cmbGenre.Visible = false;
            //
            // lblSourceValue
            //
            this.lblSourceValue.AutoSize = true;
            this.lblSourceValue.Location = new System.Drawing.Point(12, 114);
            this.lblSourceValue.Name = "lblSourceValue";
            this.lblSourceValue.Size = new System.Drawing.Size(22, 15);
            this.lblSourceValue.Text = "値:";
            this.lblSourceValue.Visible = false;
            //
            // nudSourceValue
            //
            this.nudSourceValue.Location = new System.Drawing.Point(100, 111);
            this.nudSourceValue.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.nudSourceValue.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudSourceValue.Name = "nudSourceValue";
            this.nudSourceValue.Size = new System.Drawing.Size(80, 23);
            this.nudSourceValue.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudSourceValue.Visible = false;
            //
            // lblMaxDisplayCount
            //
            this.lblMaxDisplayCount.AutoSize = true;
            this.lblMaxDisplayCount.Location = new System.Drawing.Point(12, 147);
            this.lblMaxDisplayCount.Name = "lblMaxDisplayCount";
            this.lblMaxDisplayCount.Size = new System.Drawing.Size(70, 15);
            this.lblMaxDisplayCount.Text = "最大表示数:";
            //
            // nudMaxDisplayCount
            //
            this.nudMaxDisplayCount.Location = new System.Drawing.Point(100, 144);
            this.nudMaxDisplayCount.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            this.nudMaxDisplayCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.nudMaxDisplayCount.Name = "nudMaxDisplayCount";
            this.nudMaxDisplayCount.Size = new System.Drawing.Size(80, 23);
            this.nudMaxDisplayCount.Value = new decimal(new int[] { 5, 0, 0, 0 });
            //
            // chkIsVisible
            //
            this.chkIsVisible.AutoSize = true;
            this.chkIsVisible.Checked = true;
            this.chkIsVisible.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkIsVisible.Location = new System.Drawing.Point(100, 177);
            this.chkIsVisible.Name = "chkIsVisible";
            this.chkIsVisible.Size = new System.Drawing.Size(74, 19);
            this.chkIsVisible.Text = "表示する";
            //
            // grpGameAssignment
            //
            this.grpGameAssignment.Controls.Add(this.lblAvailable);
            this.grpGameAssignment.Controls.Add(this.lstAvailable);
            this.grpGameAssignment.Controls.Add(this.lblAssigned);
            this.grpGameAssignment.Controls.Add(this.lstAssigned);
            this.grpGameAssignment.Controls.Add(this.btnAdd);
            this.grpGameAssignment.Controls.Add(this.btnRemove);
            this.grpGameAssignment.Controls.Add(this.btnUp);
            this.grpGameAssignment.Controls.Add(this.btnDown);
            this.grpGameAssignment.Controls.Add(this.lblDisplayText);
            this.grpGameAssignment.Controls.Add(this.txtDisplayText);
            this.grpGameAssignment.Location = new System.Drawing.Point(12, 206);
            this.grpGameAssignment.Name = "grpGameAssignment";
            this.grpGameAssignment.Size = new System.Drawing.Size(540, 280);
            this.grpGameAssignment.TabIndex = 0;
            this.grpGameAssignment.TabStop = false;
            this.grpGameAssignment.Text = "ゲーム割当";
            //
            // lblAvailable
            //
            this.lblAvailable.AutoSize = true;
            this.lblAvailable.Location = new System.Drawing.Point(10, 22);
            this.lblAvailable.Name = "lblAvailable";
            this.lblAvailable.Size = new System.Drawing.Size(80, 15);
            this.lblAvailable.Text = "未割当ゲーム:";
            //
            // lstAvailable
            //
            this.lstAvailable.FormattingEnabled = true;
            this.lstAvailable.ItemHeight = 15;
            this.lstAvailable.Location = new System.Drawing.Point(10, 40);
            this.lstAvailable.Name = "lstAvailable";
            this.lstAvailable.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.lstAvailable.Size = new System.Drawing.Size(200, 184);
            //
            // btnAdd
            //
            this.btnAdd.Location = new System.Drawing.Point(220, 80);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(40, 30);
            this.btnAdd.Text = "→";
            this.btnAdd.Click += new System.EventHandler(this.btnAdd_Click);
            //
            // btnRemove
            //
            this.btnRemove.Location = new System.Drawing.Point(220, 120);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(40, 30);
            this.btnRemove.Text = "←";
            this.btnRemove.Click += new System.EventHandler(this.btnRemove_Click);
            //
            // lblAssigned
            //
            this.lblAssigned.AutoSize = true;
            this.lblAssigned.Location = new System.Drawing.Point(270, 22);
            this.lblAssigned.Name = "lblAssigned";
            this.lblAssigned.Size = new System.Drawing.Size(80, 15);
            this.lblAssigned.Text = "割当済みゲーム:";
            //
            // lstAssigned
            //
            this.lstAssigned.FormattingEnabled = true;
            this.lstAssigned.ItemHeight = 15;
            this.lstAssigned.Location = new System.Drawing.Point(270, 40);
            this.lstAssigned.Name = "lstAssigned";
            this.lstAssigned.Size = new System.Drawing.Size(200, 184);
            //
            // btnUp
            //
            this.btnUp.Location = new System.Drawing.Point(480, 80);
            this.btnUp.Name = "btnUp";
            this.btnUp.Size = new System.Drawing.Size(40, 30);
            this.btnUp.Text = "▲";
            this.btnUp.Click += new System.EventHandler(this.btnUp_Click);
            //
            // btnDown
            //
            this.btnDown.Location = new System.Drawing.Point(480, 120);
            this.btnDown.Name = "btnDown";
            this.btnDown.Size = new System.Drawing.Size(40, 30);
            this.btnDown.Text = "▼";
            this.btnDown.Click += new System.EventHandler(this.btnDown_Click);
            //
            // lblDisplayText
            //
            this.lblDisplayText.AutoSize = true;
            this.lblDisplayText.Location = new System.Drawing.Point(270, 232);
            this.lblDisplayText.Name = "lblDisplayText";
            this.lblDisplayText.Size = new System.Drawing.Size(60, 15);
            this.lblDisplayText.Text = "表示テキスト:";
            //
            // txtDisplayText
            //
            this.txtDisplayText.Location = new System.Drawing.Point(360, 229);
            this.txtDisplayText.Name = "txtDisplayText";
            this.txtDisplayText.Size = new System.Drawing.Size(180, 23);
            this.txtDisplayText.TextChanged += new System.EventHandler(this.txtDisplayText_TextChanged);
            //
            // btnOK
            //
            this.btnOK.Location = new System.Drawing.Point(366, 500);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(90, 30);
            this.btnOK.Text = "OK";
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.Location = new System.Drawing.Point(462, 500);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(90, 30);
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // StoreSectionForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(564, 542);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.txtTitle);
            this.Controls.Add(this.lblSectionType);
            this.Controls.Add(this.cmbSectionType);
            this.Controls.Add(this.lblSectionSource);
            this.Controls.Add(this.cmbSectionSource);
            this.Controls.Add(this.lblGenre);
            this.Controls.Add(this.cmbGenre);
            this.Controls.Add(this.lblSourceValue);
            this.Controls.Add(this.nudSourceValue);
            this.Controls.Add(this.lblMaxDisplayCount);
            this.Controls.Add(this.nudMaxDisplayCount);
            this.Controls.Add(this.chkIsVisible);
            this.Controls.Add(this.grpGameAssignment);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "StoreSectionForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "セクション編集";
            this.Load += new System.EventHandler(this.StoreSectionForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.nudSourceValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nudMaxDisplayCount)).EndInit();
            this.grpGameAssignment.ResumeLayout(false);
            this.grpGameAssignment.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.TextBox txtTitle;
        private System.Windows.Forms.Label lblSectionType;
        private System.Windows.Forms.ComboBox cmbSectionType;
        private System.Windows.Forms.Label lblSectionSource;
        private System.Windows.Forms.ComboBox cmbSectionSource;
        private System.Windows.Forms.Label lblGenre;
        private System.Windows.Forms.ComboBox cmbGenre;
        private System.Windows.Forms.Label lblSourceValue;
        private System.Windows.Forms.NumericUpDown nudSourceValue;
        private System.Windows.Forms.Label lblMaxDisplayCount;
        private System.Windows.Forms.NumericUpDown nudMaxDisplayCount;
        private System.Windows.Forms.CheckBox chkIsVisible;
        private System.Windows.Forms.GroupBox grpGameAssignment;
        private System.Windows.Forms.Label lblAvailable;
        private System.Windows.Forms.ListBox lstAvailable;
        private System.Windows.Forms.Label lblAssigned;
        private System.Windows.Forms.ListBox lstAssigned;
        private System.Windows.Forms.Button btnAdd;
        private System.Windows.Forms.Button btnRemove;
        private System.Windows.Forms.Button btnUp;
        private System.Windows.Forms.Button btnDown;
        private System.Windows.Forms.Label lblDisplayText;
        private System.Windows.Forms.TextBox txtDisplayText;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}
