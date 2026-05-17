namespace GCTonePrism.Manager.Controls
{
    partial class LogSectionPanel
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
            this.toolStrip = new System.Windows.Forms.Panel();
            this.row2Panel = new System.Windows.Forms.Panel();
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.lblSearch = new System.Windows.Forms.Label();
            this.row1Panel = new System.Windows.Forms.Panel();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.btnOpenLogFolder = new System.Windows.Forms.Button();
            this.chkInfo = new System.Windows.Forms.CheckBox();
            this.chkWarn = new System.Windows.Forms.CheckBox();
            this.chkError = new System.Windows.Forms.CheckBox();
            this.chkManager = new System.Windows.Forms.CheckBox();
            this.chkLauncher = new System.Windows.Forms.CheckBox();
            this.lblFileCount = new System.Windows.Forms.Label();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.gridFiles = new System.Windows.Forms.DataGridView();
            this.txtContent = new System.Windows.Forms.RichTextBox();
            this.toolStrip.SuspendLayout();
            this.row2Panel.SuspendLayout();
            this.row1Panel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridFiles)).BeginInit();
            this.SuspendLayout();
            //
            // toolStrip (上部 2 行ツールバー: row1 = フィルタ群, row2 = 検索)
            //
            this.toolStrip.Controls.Add(this.row2Panel);
            this.toolStrip.Controls.Add(this.row1Panel);
            this.toolStrip.Dock = System.Windows.Forms.DockStyle.Top;
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(1092, 76);
            this.toolStrip.TabIndex = 0;
            //
            // row1Panel (Dock=Top でフィルタ群を 1 行目に配置)
            //
            this.row1Panel.Controls.Add(this.btnRefresh);
            this.row1Panel.Controls.Add(this.btnOpenLogFolder);
            this.row1Panel.Controls.Add(this.chkInfo);
            this.row1Panel.Controls.Add(this.chkWarn);
            this.row1Panel.Controls.Add(this.chkError);
            this.row1Panel.Controls.Add(this.chkManager);
            this.row1Panel.Controls.Add(this.chkLauncher);
            this.row1Panel.Controls.Add(this.lblFileCount);
            this.row1Panel.Dock = System.Windows.Forms.DockStyle.Top;
            this.row1Panel.Location = new System.Drawing.Point(0, 0);
            this.row1Panel.Name = "row1Panel";
            this.row1Panel.Size = new System.Drawing.Size(1092, 42);
            this.row1Panel.TabIndex = 0;
            //
            // btnRefresh
            //
            this.btnRefresh.Location = new System.Drawing.Point(8, 8);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(70, 26);
            this.btnRefresh.TabIndex = 0;
            this.btnRefresh.Text = "更新";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            //
            // btnOpenLogFolder (btnRefresh の隣、checkboxes の左、左寄せ)
            //
            this.btnOpenLogFolder.Location = new System.Drawing.Point(84, 8);
            this.btnOpenLogFolder.Name = "btnOpenLogFolder";
            this.btnOpenLogFolder.Size = new System.Drawing.Size(150, 26);
            this.btnOpenLogFolder.TabIndex = 1;
            this.btnOpenLogFolder.Text = "ログフォルダを開く";
            this.btnOpenLogFolder.UseVisualStyleBackColor = true;
            this.btnOpenLogFolder.Click += new System.EventHandler(this.btnOpenLogFolder_Click);
            //
            // chkInfo
            //
            this.chkInfo.AutoSize = true;
            this.chkInfo.Checked = true;
            this.chkInfo.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkInfo.Location = new System.Drawing.Point(240, 12);
            this.chkInfo.Name = "chkInfo";
            this.chkInfo.Size = new System.Drawing.Size(56, 19);
            this.chkInfo.TabIndex = 1;
            this.chkInfo.Text = "INFO";
            this.chkInfo.UseVisualStyleBackColor = true;
            this.chkInfo.CheckedChanged += new System.EventHandler(this.chkLevelFilter_Changed);
            //
            // chkWarn
            //
            this.chkWarn.AutoSize = true;
            this.chkWarn.Checked = true;
            this.chkWarn.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkWarn.Location = new System.Drawing.Point(302, 12);
            this.chkWarn.Name = "chkWarn";
            this.chkWarn.Size = new System.Drawing.Size(64, 19);
            this.chkWarn.TabIndex = 2;
            this.chkWarn.Text = "WARN";
            this.chkWarn.UseVisualStyleBackColor = true;
            this.chkWarn.CheckedChanged += new System.EventHandler(this.chkLevelFilter_Changed);
            //
            // chkError
            //
            this.chkError.AutoSize = true;
            this.chkError.Checked = true;
            this.chkError.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkError.Location = new System.Drawing.Point(372, 12);
            this.chkError.Name = "chkError";
            this.chkError.Size = new System.Drawing.Size(64, 19);
            this.chkError.TabIndex = 3;
            this.chkError.Text = "ERROR";
            this.chkError.UseVisualStyleBackColor = true;
            this.chkError.CheckedChanged += new System.EventHandler(this.chkLevelFilter_Changed);
            //
            // chkManager
            //
            this.chkManager.AutoSize = true;
            this.chkManager.Checked = true;
            this.chkManager.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkManager.Location = new System.Drawing.Point(458, 12);
            this.chkManager.Name = "chkManager";
            this.chkManager.Size = new System.Drawing.Size(80, 19);
            this.chkManager.TabIndex = 4;
            this.chkManager.Text = "Manager";
            this.chkManager.UseVisualStyleBackColor = true;
            this.chkManager.CheckedChanged += new System.EventHandler(this.chkLevelFilter_Changed);
            //
            // chkLauncher
            //
            this.chkLauncher.AutoSize = true;
            this.chkLauncher.Checked = true;
            this.chkLauncher.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkLauncher.Location = new System.Drawing.Point(544, 12);
            this.chkLauncher.Name = "chkLauncher";
            this.chkLauncher.Size = new System.Drawing.Size(80, 19);
            this.chkLauncher.TabIndex = 5;
            this.chkLauncher.Text = "Launcher";
            this.chkLauncher.UseVisualStyleBackColor = true;
            this.chkLauncher.CheckedChanged += new System.EventHandler(this.chkLevelFilter_Changed);
            //
            // lblFileCount (右アンカー)
            //
            this.lblFileCount.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblFileCount.AutoSize = false;
            this.lblFileCount.ForeColor = System.Drawing.Color.DimGray;
            this.lblFileCount.Location = new System.Drawing.Point(910, 14);
            this.lblFileCount.Name = "lblFileCount";
            this.lblFileCount.Size = new System.Drawing.Size(170, 18);
            this.lblFileCount.TabIndex = 6;
            this.lblFileCount.Text = "ログファイル: -";
            this.lblFileCount.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // row2Panel (Dock=Fill で 1 行目の下を埋める = 検索行)
            //
            this.row2Panel.Controls.Add(this.txtSearch);
            this.row2Panel.Controls.Add(this.lblSearch);
            this.row2Panel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.row2Panel.Location = new System.Drawing.Point(0, 42);
            this.row2Panel.Name = "row2Panel";
            this.row2Panel.Size = new System.Drawing.Size(1092, 34);
            this.row2Panel.TabIndex = 1;
            //
            // lblSearch
            //
            this.lblSearch.AutoSize = true;
            this.lblSearch.Location = new System.Drawing.Point(8, 8);
            this.lblSearch.Name = "lblSearch";
            this.lblSearch.Size = new System.Drawing.Size(40, 15);
            this.lblSearch.TabIndex = 0;
            this.lblSearch.Text = "検索:";
            //
            // txtSearch (row2Panel 内で Top|Left|Right で残り全幅)
            // row2Panel は他に右アンカーコントロールが無いので衝突せず確実に伸びる
            //
            this.txtSearch.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                | System.Windows.Forms.AnchorStyles.Right)));
            this.txtSearch.Location = new System.Drawing.Point(60, 5);
            this.txtSearch.Name = "txtSearch";
            this.txtSearch.Size = new System.Drawing.Size(1024, 23);
            this.txtSearch.TabIndex = 1;
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            //
            // splitContainer (横割り: 上=ファイル一覧 / 下=本文)
            //
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Location = new System.Drawing.Point(0, 76);
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitContainer.Panel1.Controls.Add(this.gridFiles);
            this.splitContainer.Panel2.Controls.Add(this.txtContent);
            this.splitContainer.Size = new System.Drawing.Size(1092, 520);
            this.splitContainer.SplitterDistance = 220;
            this.splitContainer.TabIndex = 1;
            //
            // gridFiles
            //
            this.gridFiles.AllowUserToAddRows = false;
            this.gridFiles.AllowUserToDeleteRows = false;
            this.gridFiles.AllowUserToResizeRows = false;
            this.gridFiles.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.gridFiles.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridFiles.Location = new System.Drawing.Point(0, 0);
            this.gridFiles.MultiSelect = false;
            this.gridFiles.Name = "gridFiles";
            this.gridFiles.ReadOnly = true;
            this.gridFiles.RowHeadersVisible = false;
            this.gridFiles.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridFiles.Size = new System.Drawing.Size(1092, 220);
            this.gridFiles.TabIndex = 0;
            this.gridFiles.SelectionChanged += new System.EventHandler(this.gridFiles_SelectionChanged);
            //
            // txtContent (改行折り返し有効、横スクロールなし)
            //
            this.txtContent.BackColor = System.Drawing.Color.White;
            this.txtContent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtContent.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtContent.HideSelection = false;
            this.txtContent.Location = new System.Drawing.Point(0, 0);
            this.txtContent.Name = "txtContent";
            this.txtContent.ReadOnly = true;
            this.txtContent.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.txtContent.Size = new System.Drawing.Size(1092, 296);
            this.txtContent.TabIndex = 0;
            this.txtContent.Text = "";
            this.txtContent.WordWrap = true;
            //
            // LogSectionPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this.toolStrip);
            this.Name = "LogSectionPanel";
            this.Size = new System.Drawing.Size(1092, 596);
            this.toolStrip.ResumeLayout(false);
            this.row2Panel.ResumeLayout(false);
            this.row2Panel.PerformLayout();
            this.row1Panel.ResumeLayout(false);
            this.row1Panel.PerformLayout();
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridFiles)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel toolStrip;
        private System.Windows.Forms.Panel row1Panel;
        private System.Windows.Forms.Panel row2Panel;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Button btnOpenLogFolder;
        private System.Windows.Forms.CheckBox chkInfo;
        private System.Windows.Forms.CheckBox chkWarn;
        private System.Windows.Forms.CheckBox chkError;
        private System.Windows.Forms.CheckBox chkManager;
        private System.Windows.Forms.CheckBox chkLauncher;
        private System.Windows.Forms.Label lblSearch;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Label lblFileCount;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.DataGridView gridFiles;
        private System.Windows.Forms.RichTextBox txtContent;
    }
}
