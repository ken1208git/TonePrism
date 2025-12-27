namespace GCTonePrism.Manager
{
    partial class AddGameForm
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
            this.lblGameFolder = new System.Windows.Forms.Label();
            this.txtGameFolder = new System.Windows.Forms.TextBox();
            this.btnSelectGameFolder = new System.Windows.Forms.Button();
            this.lblGameFolderHint = new System.Windows.Forms.Label();
            this.lblGameId = new System.Windows.Forms.Label();
            this.txtGameId = new System.Windows.Forms.TextBox();
            this.lblGameIdWarning = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.txtTitle = new System.Windows.Forms.TextBox();
            this.lblDescription = new System.Windows.Forms.Label();
            this.txtDescription = new System.Windows.Forms.TextBox();
            this.lblGenre = new System.Windows.Forms.Label();
            this.clbGenre = new System.Windows.Forms.CheckedListBox();
            this.lblReleaseYear = new System.Windows.Forms.Label();
            this.numReleaseYear = new System.Windows.Forms.NumericUpDown();
            this.lblMinPlayers = new System.Windows.Forms.Label();
            this.numMinPlayers = new System.Windows.Forms.NumericUpDown();
            this.lblMaxPlayers = new System.Windows.Forms.Label();
            this.numMaxPlayers = new System.Windows.Forms.NumericUpDown();
            this.lblDifficulty = new System.Windows.Forms.Label();
            this.cmbDifficulty = new System.Windows.Forms.ComboBox();
            this.lblPlayTime = new System.Windows.Forms.Label();
            this.cmbPlayTime = new System.Windows.Forms.ComboBox();
            this.chkControllerSupport = new System.Windows.Forms.CheckBox();
            this.lblThumbnailPath = new System.Windows.Forms.Label();
            this.txtThumbnailPath = new System.Windows.Forms.TextBox();
            this.btnSelectThumbnail = new System.Windows.Forms.Button();
            this.lblBackgroundPath = new System.Windows.Forms.Label();
            this.txtBackgroundPath = new System.Windows.Forms.TextBox();
            this.btnSelectBackground = new System.Windows.Forms.Button();
            this.lblExecutablePath = new System.Windows.Forms.Label();
            this.txtExecutablePath = new System.Windows.Forms.TextBox();
            this.btnSelectExecutable = new System.Windows.Forms.Button();
            this.lblDevelopers = new System.Windows.Forms.Label();
            this.dgvDevelopers = new System.Windows.Forms.DataGridView();
            this.btnAddDeveloper = new System.Windows.Forms.Button();
            this.btnEditDeveloper = new System.Windows.Forms.Button();
            this.btnDeleteDeveloper = new System.Windows.Forms.Button();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.numReleaseYear)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinPlayers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxPlayers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDevelopers)).BeginInit();
            this.SuspendLayout();
            // 
            // lblGameFolder
            // 
            this.lblGameFolder.AutoSize = true;
            this.lblGameFolder.Location = new System.Drawing.Point(12, 15);
            this.lblGameFolder.Name = "lblGameFolder";
            this.lblGameFolder.Size = new System.Drawing.Size(104, 15);
            this.lblGameFolder.TabIndex = 0;
            this.lblGameFolder.Text = "ゲームフォルダ*";
            // 
            // txtGameFolder
            // 
            this.txtGameFolder.Location = new System.Drawing.Point(120, 12);
            this.txtGameFolder.Name = "txtGameFolder";
            this.txtGameFolder.ReadOnly = false;
            this.txtGameFolder.Size = new System.Drawing.Size(400, 22);
            this.txtGameFolder.TabIndex = 1;
            // 
            // btnSelectGameFolder
            // 
            this.btnSelectGameFolder.Location = new System.Drawing.Point(526, 11);
            this.btnSelectGameFolder.Name = "btnSelectGameFolder";
            this.btnSelectGameFolder.Size = new System.Drawing.Size(94, 23);
            this.btnSelectGameFolder.TabIndex = 2;
            this.btnSelectGameFolder.Text = "選択...";
            this.btnSelectGameFolder.UseVisualStyleBackColor = true;
            this.btnSelectGameFolder.Click += new System.EventHandler(this.btnSelectGameFolder_Click);
            // 
            // lblGameFolderHint
            // 
            this.lblGameFolderHint.AutoSize = true;
            this.lblGameFolderHint.Font = new System.Drawing.Font("MS UI Gothic", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblGameFolderHint.ForeColor = System.Drawing.Color.Gray;
            this.lblGameFolderHint.Location = new System.Drawing.Point(120, 37);
            this.lblGameFolderHint.Name = "lblGameFolderHint";
            this.lblGameFolderHint.Size = new System.Drawing.Size(420, 11);
            this.lblGameFolderHint.TabIndex = 3;
            this.lblGameFolderHint.Text = "注意: ゲームを動かすのに最低限のファイルのみを含むフォルダを選択してください。";
            // 
            // lblGameId
            // 
            this.lblGameId.AutoSize = true;
            this.lblGameId.Location = new System.Drawing.Point(12, 52);
            this.lblGameId.Name = "lblGameId";
            this.lblGameId.Size = new System.Drawing.Size(65, 15);
            this.lblGameId.TabIndex = 0;
            this.lblGameId.Text = "ゲームID*";
            // 
            // txtGameId
            // 
            this.txtGameId.Location = new System.Drawing.Point(120, 49);
            this.txtGameId.Name = "txtGameId";
            this.txtGameId.Size = new System.Drawing.Size(300, 22);
            this.txtGameId.TabIndex = 4;
            // 
            // lblGameIdWarning
            // 
            this.lblGameIdWarning.AutoSize = true;
            this.lblGameIdWarning.ForeColor = System.Drawing.Color.Red;
            this.lblGameIdWarning.Location = new System.Drawing.Point(120, 74);
            this.lblGameIdWarning.Name = "lblGameIdWarning";
            this.lblGameIdWarning.Size = new System.Drawing.Size(300, 15);
            this.lblGameIdWarning.TabIndex = 5;
            this.lblGameIdWarning.Text = "ゲームIDは英数字、アンダースコア（_）、ハイフン（-）のみ使用できます。";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Location = new System.Drawing.Point(12, 80);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(52, 15);
            this.lblTitle.TabIndex = 2;
            this.lblTitle.Text = "タイトル*";
            // 
            // txtTitle
            // 
            this.txtTitle.Location = new System.Drawing.Point(120, 77);
            this.txtTitle.Name = "txtTitle";
            this.txtTitle.Size = new System.Drawing.Size(500, 22);
            this.txtTitle.TabIndex = 6;
            // 
            // lblDescription
            // 
            this.lblDescription.AutoSize = true;
            this.lblDescription.Location = new System.Drawing.Point(12, 108);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(52, 15);
            this.lblDescription.TabIndex = 4;
            this.lblDescription.Text = "説明文";
            // 
            // txtDescription
            // 
            this.txtDescription.Location = new System.Drawing.Point(120, 105);
            this.txtDescription.Multiline = true;
            this.txtDescription.Name = "txtDescription";
            this.txtDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDescription.Size = new System.Drawing.Size(500, 60);
            this.txtDescription.TabIndex = 8;
            // 
            // lblGenre
            // 
            this.lblGenre.AutoSize = true;
            this.lblGenre.Location = new System.Drawing.Point(12, 173);
            this.lblGenre.Name = "lblGenre";
            this.lblGenre.Size = new System.Drawing.Size(52, 15);
            this.lblGenre.TabIndex = 6;
            this.lblGenre.Text = "ジャンル";
            // 
            // clbGenre
            // 
            this.clbGenre.CheckOnClick = true;
            this.clbGenre.FormattingEnabled = true;
            this.clbGenre.Location = new System.Drawing.Point(120, 170);
            this.clbGenre.Name = "clbGenre";
            this.clbGenre.Size = new System.Drawing.Size(300, 200);
            this.clbGenre.TabIndex = 10;
            // 
            // lblReleaseYear
            // 
            this.lblReleaseYear.AutoSize = true;
            this.lblReleaseYear.Location = new System.Drawing.Point(12, 381);
            this.lblReleaseYear.Name = "lblReleaseYear";
            this.lblReleaseYear.Size = new System.Drawing.Size(65, 15);
            this.lblReleaseYear.TabIndex = 8;
            this.lblReleaseYear.Text = "リリース年";
            // 
            // numReleaseYear
            // 
            this.numReleaseYear.Location = new System.Drawing.Point(120, 378);
            this.numReleaseYear.Maximum = new decimal(new int[] {
            2100,
            0,
            0,
            0});
            this.numReleaseYear.Minimum = new decimal(new int[] {
            1900,
            0,
            0,
            0});
            this.numReleaseYear.Name = "numReleaseYear";
            this.numReleaseYear.Size = new System.Drawing.Size(120, 22);
            this.numReleaseYear.TabIndex = 12;
            this.numReleaseYear.Value = new decimal(new int[] {
            2024,
            0,
            0,
            0});
            // 
            // lblMinPlayers
            // 
            this.lblMinPlayers.AutoSize = true;
            this.lblMinPlayers.Location = new System.Drawing.Point(12, 409);
            this.lblMinPlayers.Name = "lblMinPlayers";
            this.lblMinPlayers.Size = new System.Drawing.Size(104, 15);
            this.lblMinPlayers.TabIndex = 10;
            this.lblMinPlayers.Text = "最小プレイヤー数";
            // 
            // numMinPlayers
            // 
            this.numMinPlayers.Location = new System.Drawing.Point(120, 406);
            this.numMinPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMinPlayers.Name = "numMinPlayers";
            this.numMinPlayers.Size = new System.Drawing.Size(120, 22);
            this.numMinPlayers.TabIndex = 14;
            this.numMinPlayers.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // lblMaxPlayers
            // 
            this.lblMaxPlayers.AutoSize = true;
            this.lblMaxPlayers.Location = new System.Drawing.Point(260, 409);
            this.lblMaxPlayers.Name = "lblMaxPlayers";
            this.lblMaxPlayers.Size = new System.Drawing.Size(104, 15);
            this.lblMaxPlayers.TabIndex = 12;
            this.lblMaxPlayers.Text = "最大プレイヤー数";
            // 
            // numMaxPlayers
            // 
            this.numMaxPlayers.Location = new System.Drawing.Point(370, 406);
            this.numMaxPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMaxPlayers.Name = "numMaxPlayers";
            this.numMaxPlayers.Size = new System.Drawing.Size(120, 22);
            this.numMaxPlayers.TabIndex = 16;
            this.numMaxPlayers.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // lblDifficulty
            // 
            this.lblDifficulty.AutoSize = true;
            this.lblDifficulty.Location = new System.Drawing.Point(12, 437);
            this.lblDifficulty.Name = "lblDifficulty";
            this.lblDifficulty.Size = new System.Drawing.Size(52, 15);
            this.lblDifficulty.TabIndex = 14;
            this.lblDifficulty.Text = "難易度";
            // 
            // cmbDifficulty
            // 
            this.cmbDifficulty.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDifficulty.FormattingEnabled = true;
            this.cmbDifficulty.Location = new System.Drawing.Point(120, 434);
            this.cmbDifficulty.Name = "cmbDifficulty";
            this.cmbDifficulty.Size = new System.Drawing.Size(200, 23);
            this.cmbDifficulty.TabIndex = 18;
            // 
            // lblPlayTime
            // 
            this.lblPlayTime.AutoSize = true;
            this.lblPlayTime.Location = new System.Drawing.Point(340, 437);
            this.lblPlayTime.Name = "lblPlayTime";
            this.lblPlayTime.Size = new System.Drawing.Size(78, 15);
            this.lblPlayTime.TabIndex = 16;
            this.lblPlayTime.Text = "プレイ時間";
            // 
            // cmbPlayTime
            // 
            this.cmbPlayTime.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPlayTime.FormattingEnabled = true;
            this.cmbPlayTime.Location = new System.Drawing.Point(420, 434);
            this.cmbPlayTime.Name = "cmbPlayTime";
            this.cmbPlayTime.Size = new System.Drawing.Size(200, 23);
            this.cmbPlayTime.TabIndex = 20;
            // 
            // chkControllerSupport
            // 
            this.chkControllerSupport.AutoSize = true;
            this.chkControllerSupport.Location = new System.Drawing.Point(12, 463);
            this.chkControllerSupport.Name = "chkControllerSupport";
            this.chkControllerSupport.Size = new System.Drawing.Size(149, 19);
            this.chkControllerSupport.TabIndex = 22;
            this.chkControllerSupport.Text = "コントローラーサポート";
            this.chkControllerSupport.UseVisualStyleBackColor = true;
            // 
            // lblThumbnailPath
            // 
            this.lblThumbnailPath.AutoSize = true;
            this.lblThumbnailPath.Location = new System.Drawing.Point(12, 491);
            this.lblThumbnailPath.Name = "lblThumbnailPath";
            this.lblThumbnailPath.Size = new System.Drawing.Size(104, 15);
            this.lblThumbnailPath.TabIndex = 19;
            this.lblThumbnailPath.Text = "サムネイル画像";
            // 
            // txtThumbnailPath
            // 
            this.txtThumbnailPath.Location = new System.Drawing.Point(120, 488);
            this.txtThumbnailPath.Name = "txtThumbnailPath";
            this.txtThumbnailPath.ReadOnly = false;
            this.txtThumbnailPath.Size = new System.Drawing.Size(400, 22);
            this.txtThumbnailPath.TabIndex = 24;
            // 
            // btnSelectThumbnail
            // 
            this.btnSelectThumbnail.Location = new System.Drawing.Point(526, 487);
            this.btnSelectThumbnail.Name = "btnSelectThumbnail";
            this.btnSelectThumbnail.Size = new System.Drawing.Size(94, 23);
            this.btnSelectThumbnail.TabIndex = 25;
            this.btnSelectThumbnail.Text = "選択...";
            this.btnSelectThumbnail.UseVisualStyleBackColor = true;
            this.btnSelectThumbnail.Click += new System.EventHandler(this.btnSelectThumbnail_Click);
            // 
            // lblBackgroundPath
            // 
            this.lblBackgroundPath.AutoSize = true;
            this.lblBackgroundPath.Location = new System.Drawing.Point(12, 519);
            this.lblBackgroundPath.Name = "lblBackgroundPath";
            this.lblBackgroundPath.Size = new System.Drawing.Size(78, 15);
            this.lblBackgroundPath.TabIndex = 22;
            this.lblBackgroundPath.Text = "背景画像";
            // 
            // txtBackgroundPath
            // 
            this.txtBackgroundPath.Location = new System.Drawing.Point(120, 516);
            this.txtBackgroundPath.Name = "txtBackgroundPath";
            this.txtBackgroundPath.ReadOnly = false;
            this.txtBackgroundPath.Size = new System.Drawing.Size(400, 22);
            this.txtBackgroundPath.TabIndex = 27;
            // 
            // btnSelectBackground
            // 
            this.btnSelectBackground.Location = new System.Drawing.Point(526, 515);
            this.btnSelectBackground.Name = "btnSelectBackground";
            this.btnSelectBackground.Size = new System.Drawing.Size(94, 23);
            this.btnSelectBackground.TabIndex = 28;
            this.btnSelectBackground.Text = "選択...";
            this.btnSelectBackground.UseVisualStyleBackColor = true;
            this.btnSelectBackground.Click += new System.EventHandler(this.btnSelectBackground_Click);
            // 
            // lblExecutablePath
            // 
            this.lblExecutablePath.AutoSize = true;
            this.lblExecutablePath.Location = new System.Drawing.Point(12, 547);
            this.lblExecutablePath.Name = "lblExecutablePath";
            this.lblExecutablePath.Size = new System.Drawing.Size(104, 15);
            this.lblExecutablePath.TabIndex = 25;
            this.lblExecutablePath.Text = "実行ファイル*";
            // 
            // txtExecutablePath
            // 
            this.txtExecutablePath.Location = new System.Drawing.Point(120, 544);
            this.txtExecutablePath.Name = "txtExecutablePath";
            this.txtExecutablePath.ReadOnly = false;
            this.txtExecutablePath.Size = new System.Drawing.Size(400, 22);
            this.txtExecutablePath.TabIndex = 30;
            // 
            // btnSelectExecutable
            // 
            this.btnSelectExecutable.Location = new System.Drawing.Point(526, 543);
            this.btnSelectExecutable.Name = "btnSelectExecutable";
            this.btnSelectExecutable.Size = new System.Drawing.Size(94, 23);
            this.btnSelectExecutable.TabIndex = 31;
            this.btnSelectExecutable.Text = "選択...";
            this.btnSelectExecutable.UseVisualStyleBackColor = true;
            this.btnSelectExecutable.Click += new System.EventHandler(this.btnSelectExecutable_Click);
            // 
            // lblDevelopers
            // 
            this.lblDevelopers.AutoSize = true;
            this.lblDevelopers.Location = new System.Drawing.Point(12, 575);
            this.lblDevelopers.Name = "lblDevelopers";
            this.lblDevelopers.Size = new System.Drawing.Size(65, 15);
            this.lblDevelopers.TabIndex = 32;
            this.lblDevelopers.Text = "製作者情報";
            // 
            // dgvDevelopers
            // 
            this.dgvDevelopers.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvDevelopers.Location = new System.Drawing.Point(120, 572);
            this.dgvDevelopers.Name = "dgvDevelopers";
            this.dgvDevelopers.RowHeadersWidth = 51;
            this.dgvDevelopers.Size = new System.Drawing.Size(427, 120);
            this.dgvDevelopers.TabIndex = 33;
            // 
            // btnAddDeveloper
            // 
            this.btnAddDeveloper.Location = new System.Drawing.Point(553, 572);
            this.btnAddDeveloper.Name = "btnAddDeveloper";
            this.btnAddDeveloper.Size = new System.Drawing.Size(75, 23);
            this.btnAddDeveloper.TabIndex = 34;
            this.btnAddDeveloper.Text = "追加";
            this.btnAddDeveloper.UseVisualStyleBackColor = true;
            this.btnAddDeveloper.Click += new System.EventHandler(this.btnAddDeveloper_Click);
            // 
            // btnEditDeveloper
            // 
            this.btnEditDeveloper.Location = new System.Drawing.Point(553, 601);
            this.btnEditDeveloper.Name = "btnEditDeveloper";
            this.btnEditDeveloper.Size = new System.Drawing.Size(75, 23);
            this.btnEditDeveloper.TabIndex = 35;
            this.btnEditDeveloper.Text = "編集";
            this.btnEditDeveloper.UseVisualStyleBackColor = true;
            this.btnEditDeveloper.Click += new System.EventHandler(this.btnEditDeveloper_Click);
            // 
            // btnDeleteDeveloper
            // 
            this.btnDeleteDeveloper.Location = new System.Drawing.Point(553, 630);
            this.btnDeleteDeveloper.Name = "btnDeleteDeveloper";
            this.btnDeleteDeveloper.Size = new System.Drawing.Size(75, 23);
            this.btnDeleteDeveloper.TabIndex = 36;
            this.btnDeleteDeveloper.Text = "削除";
            this.btnDeleteDeveloper.UseVisualStyleBackColor = true;
            this.btnDeleteDeveloper.Click += new System.EventHandler(this.btnDeleteDeveloper_Click);
            // 
            // btnOK
            // 
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.Location = new System.Drawing.Point(420, 704);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(95, 30);
            this.btnOK.TabIndex = 35;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(521, 704);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(95, 30);
            this.btnCancel.TabIndex = 36;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // AddGameForm
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(632, 750);
            this.Controls.Add(this.btnDeleteDeveloper);
            this.Controls.Add(this.btnEditDeveloper);
            this.Controls.Add(this.btnAddDeveloper);
            this.Controls.Add(this.dgvDevelopers);
            this.Controls.Add(this.lblDevelopers);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.btnSelectExecutable);
            this.Controls.Add(this.txtExecutablePath);
            this.Controls.Add(this.lblExecutablePath);
            this.Controls.Add(this.btnSelectBackground);
            this.Controls.Add(this.txtBackgroundPath);
            this.Controls.Add(this.lblBackgroundPath);
            this.Controls.Add(this.btnSelectThumbnail);
            this.Controls.Add(this.txtThumbnailPath);
            this.Controls.Add(this.lblThumbnailPath);
            this.Controls.Add(this.chkControllerSupport);
            this.Controls.Add(this.cmbPlayTime);
            this.Controls.Add(this.lblPlayTime);
            this.Controls.Add(this.cmbDifficulty);
            this.Controls.Add(this.lblDifficulty);
            this.Controls.Add(this.numMaxPlayers);
            this.Controls.Add(this.lblMaxPlayers);
            this.Controls.Add(this.numMinPlayers);
            this.Controls.Add(this.lblMinPlayers);
            this.Controls.Add(this.numReleaseYear);
            this.Controls.Add(this.lblReleaseYear);
            this.Controls.Add(this.clbGenre);
            this.Controls.Add(this.lblGenre);
            this.Controls.Add(this.txtDescription);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.txtTitle);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.txtGameId);
            this.Controls.Add(this.lblGameIdWarning);
            this.Controls.Add(this.lblGameId);
            this.Controls.Add(this.lblGameFolderHint);
            this.Controls.Add(this.btnSelectGameFolder);
            this.Controls.Add(this.txtGameFolder);
            this.Controls.Add(this.lblGameFolder);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AddGameForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "ゲーム追加";
            this.Load += new System.EventHandler(this.AddGameForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numReleaseYear)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinPlayers)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxPlayers)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDevelopers)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblGameFolder;
        private System.Windows.Forms.TextBox txtGameFolder;
        private System.Windows.Forms.Button btnSelectGameFolder;
        private System.Windows.Forms.Label lblGameFolderHint;
        private System.Windows.Forms.Label lblGameId;
        private System.Windows.Forms.TextBox txtGameId;
        private System.Windows.Forms.Label lblGameIdWarning;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.TextBox txtTitle;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.TextBox txtDescription;
        private System.Windows.Forms.Label lblGenre;
        private System.Windows.Forms.CheckedListBox clbGenre;
        private System.Windows.Forms.Label lblReleaseYear;
        private System.Windows.Forms.NumericUpDown numReleaseYear;
        private System.Windows.Forms.Label lblMinPlayers;
        private System.Windows.Forms.NumericUpDown numMinPlayers;
        private System.Windows.Forms.Label lblMaxPlayers;
        private System.Windows.Forms.NumericUpDown numMaxPlayers;
        private System.Windows.Forms.Label lblDifficulty;
        private System.Windows.Forms.ComboBox cmbDifficulty;
        private System.Windows.Forms.Label lblPlayTime;
        private System.Windows.Forms.ComboBox cmbPlayTime;
        private System.Windows.Forms.CheckBox chkControllerSupport;
        private System.Windows.Forms.Label lblThumbnailPath;
        private System.Windows.Forms.TextBox txtThumbnailPath;
        private System.Windows.Forms.Button btnSelectThumbnail;
        private System.Windows.Forms.Label lblBackgroundPath;
        private System.Windows.Forms.TextBox txtBackgroundPath;
        private System.Windows.Forms.Button btnSelectBackground;
        private System.Windows.Forms.Label lblExecutablePath;
        private System.Windows.Forms.TextBox txtExecutablePath;
        private System.Windows.Forms.Button btnSelectExecutable;
        private System.Windows.Forms.Label lblDevelopers;
        private System.Windows.Forms.DataGridView dgvDevelopers;
        private System.Windows.Forms.Button btnAddDeveloper;
        private System.Windows.Forms.Button btnEditDeveloper;
        private System.Windows.Forms.Button btnDeleteDeveloper;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
    }
}

