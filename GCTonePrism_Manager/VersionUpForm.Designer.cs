namespace GCTonePrism.Manager
{
    partial class VersionUpForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private System.Windows.Forms.Label lblVersionHint;

        private void InitializeComponent()
        {
            this.lblCurrentVersionLabel = new System.Windows.Forms.Label();
            this.lblCurrentVersion = new System.Windows.Forms.Label();
            this.lblNextVersion = new System.Windows.Forms.Label();
            this.txtNextVersion = new System.Windows.Forms.TextBox();
            this.lblVersionHint = new System.Windows.Forms.Label();
            this.lblGameFolder = new System.Windows.Forms.Label();
            this.txtGameFolder = new System.Windows.Forms.TextBox();
            this.btnSelectGameFolder = new System.Windows.Forms.Button();
            this.lblGameFolderHint = new System.Windows.Forms.Label();
            this.lblTitle = new System.Windows.Forms.Label();
            this.txtTitle = new System.Windows.Forms.TextBox();
            this.lblDescription = new System.Windows.Forms.Label();
            this.txtDescription = new System.Windows.Forms.TextBox();
            this.lblGenre = new System.Windows.Forms.Label();
            this.clbGenre = new System.Windows.Forms.CheckedListBox();
            this.lblMinPlayers = new System.Windows.Forms.Label();
            this.numMinPlayers = new System.Windows.Forms.NumericUpDown();
            this.lblMaxPlayers = new System.Windows.Forms.Label();
            this.numMaxPlayers = new System.Windows.Forms.NumericUpDown();
            this.lblDifficulty = new System.Windows.Forms.Label();
            this.cmbDifficulty = new System.Windows.Forms.ComboBox();
            this.lblPlayTime = new System.Windows.Forms.Label();
            this.cmbPlayTime = new System.Windows.Forms.ComboBox();
            this.chkControllerSupport = new System.Windows.Forms.CheckBox();
            this.lblSupportedConnection = new System.Windows.Forms.Label();
            this.cmbSupportedConnection = new System.Windows.Forms.ComboBox();
            this.lblThumbnailPath = new System.Windows.Forms.Label();
            this.txtThumbnailPath = new System.Windows.Forms.TextBox();
            this.btnSelectThumbnail = new System.Windows.Forms.Button();
            this.lblBackgroundPath = new System.Windows.Forms.Label();
            this.txtBackgroundPath = new System.Windows.Forms.TextBox();
            this.btnSelectBackground = new System.Windows.Forms.Button();
            this.lblExecutablePath = new System.Windows.Forms.Label();
            this.txtExecutablePath = new System.Windows.Forms.TextBox();
            this.btnSelectExecutable = new System.Windows.Forms.Button();
            this.lblArguments = new System.Windows.Forms.Label();
            this.txtArguments = new System.Windows.Forms.TextBox();
            this.lblDevelopers = new System.Windows.Forms.Label();
            this.dgvDevelopers = new System.Windows.Forms.DataGridView();
            this.btnAddDeveloper = new System.Windows.Forms.Button();
            this.btnEditDeveloper = new System.Windows.Forms.Button();
            this.btnDeleteDeveloper = new System.Windows.Forms.Button();
            this.lblUpdateNote = new System.Windows.Forms.Label();
            this.txtUpdateNote = new System.Windows.Forms.TextBox();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.picThumbnailPreview = new System.Windows.Forms.PictureBox();
            this.picBackgroundPreview = new System.Windows.Forms.PictureBox();
            this.btnTestRun = new System.Windows.Forms.Button();
            this.lblThumbnailHint = new System.Windows.Forms.Label();
            this.lblBackgroundHint = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numMinPlayers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxPlayers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDevelopers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picThumbnailPreview)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picBackgroundPreview)).BeginInit();
            this.SuspendLayout();
            // 
            // lblCurrentVersionLabel
            // 
            this.lblCurrentVersionLabel.AutoSize = true;
            this.lblCurrentVersionLabel.Location = new System.Drawing.Point(9, 12);
            this.lblCurrentVersionLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblCurrentVersionLabel.Name = "lblCurrentVersionLabel";
            this.lblCurrentVersionLabel.Size = new System.Drawing.Size(86, 12);
            this.lblCurrentVersionLabel.TabIndex = 0;
            this.lblCurrentVersionLabel.Text = "現在のバージョン:";
            // 
            // lblCurrentVersion
            // 
            this.lblCurrentVersion.AutoSize = true;
            this.lblCurrentVersion.Font = new System.Drawing.Font("MS UI Gothic", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblCurrentVersion.Location = new System.Drawing.Point(90, 12);
            this.lblCurrentVersion.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblCurrentVersion.Name = "lblCurrentVersion";
            this.lblCurrentVersion.Size = new System.Drawing.Size(32, 12);
            this.lblCurrentVersion.TabIndex = 1;
            this.lblCurrentVersion.Text = "1.0.0";
            // 
            // lblNextVersion
            // 
            this.lblNextVersion.AutoSize = true;
            this.lblNextVersion.Location = new System.Drawing.Point(9, 36);
            this.lblNextVersion.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblNextVersion.Name = "lblNextVersion";
            this.lblNextVersion.Size = new System.Drawing.Size(68, 12);
            this.lblNextVersion.TabIndex = 2;
            this.lblNextVersion.Text = "新バージョン*";
            // 
            // txtNextVersion
            // 
            this.txtNextVersion.Location = new System.Drawing.Point(90, 34);
            this.txtNextVersion.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtNextVersion.Name = "txtNextVersion";
            this.txtNextVersion.Size = new System.Drawing.Size(114, 19);
            this.txtNextVersion.TabIndex = 3;
            this.txtNextVersion.Text = "v";
            // 
            // lblVersionHint
            // 
            this.lblVersionHint.AutoSize = true;
            this.lblVersionHint.Font = new System.Drawing.Font("MS UI Gothic", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblVersionHint.ForeColor = System.Drawing.Color.Gray;
            this.lblVersionHint.Location = new System.Drawing.Point(210, 38);
            this.lblVersionHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblVersionHint.Name = "lblVersionHint";
            this.lblVersionHint.Size = new System.Drawing.Size(244, 11);
            this.lblVersionHint.TabIndex = 100;
            this.lblVersionHint.Text = "※セマンティックバージョニング（例: v1.0.0）がおすすめです";
            // 
            // lblGameFolder
            // 
            this.lblGameFolder.AutoSize = true;
            this.lblGameFolder.Location = new System.Drawing.Point(9, 60);
            this.lblGameFolder.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGameFolder.Name = "lblGameFolder";
            this.lblGameFolder.Size = new System.Drawing.Size(76, 12);
            this.lblGameFolder.TabIndex = 4;
            this.lblGameFolder.Text = "ゲームフォルダ*";
            // 
            // txtGameFolder
            // 
            this.txtGameFolder.Location = new System.Drawing.Point(90, 58);
            this.txtGameFolder.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtGameFolder.Name = "txtGameFolder";
            this.txtGameFolder.ReadOnly = true;
            this.txtGameFolder.Size = new System.Drawing.Size(301, 19);
            this.txtGameFolder.TabIndex = 5;
            // 
            // btnSelectGameFolder
            // 
            this.btnSelectGameFolder.Location = new System.Drawing.Point(394, 57);
            this.btnSelectGameFolder.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectGameFolder.Name = "btnSelectGameFolder";
            this.btnSelectGameFolder.Size = new System.Drawing.Size(70, 20);
            this.btnSelectGameFolder.TabIndex = 6;
            this.btnSelectGameFolder.Text = "選択...";
            this.btnSelectGameFolder.UseVisualStyleBackColor = true;
            this.btnSelectGameFolder.Click += new System.EventHandler(this.btnSelectGameFolder_Click);
            // 
            // lblGameFolderHint
            // 
            this.lblGameFolderHint.AutoSize = true;
            this.lblGameFolderHint.Font = new System.Drawing.Font("MS UI Gothic", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(128)));
            this.lblGameFolderHint.ForeColor = System.Drawing.Color.Gray;
            this.lblGameFolderHint.Location = new System.Drawing.Point(90, 78);
            this.lblGameFolderHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGameFolderHint.Name = "lblGameFolderHint";
            this.lblGameFolderHint.Size = new System.Drawing.Size(356, 11);
            this.lblGameFolderHint.TabIndex = 7;
            this.lblGameFolderHint.Text = "新しいバージョンのゲームフォルダを選択してください。フォルダ全体がコピーされます。";
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Location = new System.Drawing.Point(9, 94);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(46, 12);
            this.lblTitle.TabIndex = 8;
            this.lblTitle.Text = "タイトル*";
            // 
            // txtTitle
            // 
            this.txtTitle.Location = new System.Drawing.Point(90, 92);
            this.txtTitle.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtTitle.Name = "txtTitle";
            this.txtTitle.Size = new System.Drawing.Size(376, 19);
            this.txtTitle.TabIndex = 9;
            // 
            // lblDescription
            // 
            this.lblDescription.AutoSize = true;
            this.lblDescription.Location = new System.Drawing.Point(9, 118);
            this.lblDescription.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(41, 12);
            this.lblDescription.TabIndex = 10;
            this.lblDescription.Text = "説明文";
            // 
            // txtDescription
            // 
            this.txtDescription.Location = new System.Drawing.Point(90, 116);
            this.txtDescription.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtDescription.Multiline = true;
            this.txtDescription.Name = "txtDescription";
            this.txtDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDescription.Size = new System.Drawing.Size(376, 49);
            this.txtDescription.TabIndex = 11;
            // 
            // lblGenre
            // 
            this.lblGenre.AutoSize = true;
            this.lblGenre.Location = new System.Drawing.Point(9, 172);
            this.lblGenre.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGenre.Name = "lblGenre";
            this.lblGenre.Size = new System.Drawing.Size(42, 12);
            this.lblGenre.TabIndex = 12;
            this.lblGenre.Text = "ジャンル";
            // 
            // clbGenre
            // 
            this.clbGenre.CheckOnClick = true;
            this.clbGenre.FormattingEnabled = true;
            this.clbGenre.Location = new System.Drawing.Point(90, 170);
            this.clbGenre.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.clbGenre.Name = "clbGenre";
            this.clbGenre.Size = new System.Drawing.Size(226, 158);
            this.clbGenre.TabIndex = 13;
            // 
            // lblMinPlayers
            // 
            this.lblMinPlayers.AutoSize = true;
            this.lblMinPlayers.Location = new System.Drawing.Point(9, 340);
            this.lblMinPlayers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblMinPlayers.Name = "lblMinPlayers";
            this.lblMinPlayers.Size = new System.Drawing.Size(88, 12);
            this.lblMinPlayers.TabIndex = 14;
            this.lblMinPlayers.Text = "最小プレイヤー数";
            // 
            // numMinPlayers
            // 
            this.numMinPlayers.Location = new System.Drawing.Point(101, 338);
            this.numMinPlayers.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.numMinPlayers.Maximum = new decimal(new int[] {
            99,
            0,
            0,
            0});
            this.numMinPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMinPlayers.Name = "numMinPlayers";
            this.numMinPlayers.Size = new System.Drawing.Size(90, 19);
            this.numMinPlayers.TabIndex = 15;
            this.numMinPlayers.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // lblMaxPlayers
            // 
            this.lblMaxPlayers.AutoSize = true;
            this.lblMaxPlayers.Location = new System.Drawing.Point(195, 340);
            this.lblMaxPlayers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblMaxPlayers.Name = "lblMaxPlayers";
            this.lblMaxPlayers.Size = new System.Drawing.Size(88, 12);
            this.lblMaxPlayers.TabIndex = 16;
            this.lblMaxPlayers.Text = "最大プレイヤー数";
            // 
            // numMaxPlayers
            // 
            this.numMaxPlayers.Location = new System.Drawing.Point(287, 338);
            this.numMaxPlayers.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.numMaxPlayers.Maximum = new decimal(new int[] {
            99,
            0,
            0,
            0});
            this.numMaxPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMaxPlayers.Name = "numMaxPlayers";
            this.numMaxPlayers.Size = new System.Drawing.Size(90, 19);
            this.numMaxPlayers.TabIndex = 17;
            this.numMaxPlayers.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // lblDifficulty
            // 
            this.lblDifficulty.AutoSize = true;
            this.lblDifficulty.Location = new System.Drawing.Point(9, 364);
            this.lblDifficulty.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDifficulty.Name = "lblDifficulty";
            this.lblDifficulty.Size = new System.Drawing.Size(41, 12);
            this.lblDifficulty.TabIndex = 18;
            this.lblDifficulty.Text = "難易度";
            // 
            // cmbDifficulty
            // 
            this.cmbDifficulty.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDifficulty.FormattingEnabled = true;
            this.cmbDifficulty.Location = new System.Drawing.Point(90, 362);
            this.cmbDifficulty.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.cmbDifficulty.Name = "cmbDifficulty";
            this.cmbDifficulty.Size = new System.Drawing.Size(151, 20);
            this.cmbDifficulty.TabIndex = 19;
            // 
            // lblPlayTime
            // 
            this.lblPlayTime.AutoSize = true;
            this.lblPlayTime.Location = new System.Drawing.Point(255, 364);
            this.lblPlayTime.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblPlayTime.Name = "lblPlayTime";
            this.lblPlayTime.Size = new System.Drawing.Size(56, 12);
            this.lblPlayTime.TabIndex = 20;
            this.lblPlayTime.Text = "プレイ時間";
            // 
            // cmbPlayTime
            // 
            this.cmbPlayTime.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPlayTime.FormattingEnabled = true;
            this.cmbPlayTime.Location = new System.Drawing.Point(315, 362);
            this.cmbPlayTime.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.cmbPlayTime.Name = "cmbPlayTime";
            this.cmbPlayTime.Size = new System.Drawing.Size(151, 20);
            this.cmbPlayTime.TabIndex = 21;
            // 
            // chkControllerSupport
            // 
            this.chkControllerSupport.AutoSize = true;
            this.chkControllerSupport.Location = new System.Drawing.Point(9, 388);
            this.chkControllerSupport.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.chkControllerSupport.Name = "chkControllerSupport";
            this.chkControllerSupport.Size = new System.Drawing.Size(124, 16);
            this.chkControllerSupport.TabIndex = 22;
            this.chkControllerSupport.Text = "コントローラーサポート";
            this.chkControllerSupport.UseVisualStyleBackColor = true;
            // 
            // lblSupportedConnection
            // 
            this.lblSupportedConnection.AutoSize = true;
            this.lblSupportedConnection.Location = new System.Drawing.Point(9, 412);
            this.lblSupportedConnection.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblSupportedConnection.Name = "lblSupportedConnection";
            this.lblSupportedConnection.Size = new System.Drawing.Size(80, 12);
            this.lblSupportedConnection.TabIndex = 23;
            this.lblSupportedConnection.Text = "通信プレイ対応";
            // 
            // cmbSupportedConnection
            // 
            this.cmbSupportedConnection.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbSupportedConnection.FormattingEnabled = true;
            this.cmbSupportedConnection.Location = new System.Drawing.Point(90, 410);
            this.cmbSupportedConnection.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.cmbSupportedConnection.Name = "cmbSupportedConnection";
            this.cmbSupportedConnection.Size = new System.Drawing.Size(226, 20);
            this.cmbSupportedConnection.TabIndex = 24;
            // 
            // lblThumbnailPath
            // 
            this.lblThumbnailPath.AutoSize = true;
            this.lblThumbnailPath.Location = new System.Drawing.Point(9, 438);
            this.lblThumbnailPath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblThumbnailPath.Name = "lblThumbnailPath";
            this.lblThumbnailPath.Size = new System.Drawing.Size(78, 12);
            this.lblThumbnailPath.TabIndex = 25;
            this.lblThumbnailPath.Text = "サムネイル画像";
            // 
            // txtThumbnailPath
            // 
            this.txtThumbnailPath.Location = new System.Drawing.Point(90, 436);
            this.txtThumbnailPath.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtThumbnailPath.Name = "txtThumbnailPath";
            this.txtThumbnailPath.ReadOnly = true;
            this.txtThumbnailPath.Size = new System.Drawing.Size(301, 19);
            this.txtThumbnailPath.TabIndex = 26;
            // 
            // btnSelectThumbnail
            // 
            this.btnSelectThumbnail.Location = new System.Drawing.Point(394, 436);
            this.btnSelectThumbnail.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectThumbnail.Name = "btnSelectThumbnail";
            this.btnSelectThumbnail.Size = new System.Drawing.Size(70, 19);
            this.btnSelectThumbnail.TabIndex = 27;
            this.btnSelectThumbnail.Text = "選択...";
            this.btnSelectThumbnail.UseVisualStyleBackColor = true;
            this.btnSelectThumbnail.Click += new System.EventHandler(this.btnSelectThumbnail_Click);
            // 
            // lblBackgroundPath
            // 
            this.lblBackgroundPath.AutoSize = true;
            this.lblBackgroundPath.Location = new System.Drawing.Point(9, 549);
            this.lblBackgroundPath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblBackgroundPath.Name = "lblBackgroundPath";
            this.lblBackgroundPath.Size = new System.Drawing.Size(53, 12);
            this.lblBackgroundPath.TabIndex = 28;
            this.lblBackgroundPath.Text = "背景画像";
            // 
            // txtBackgroundPath
            // 
            this.txtBackgroundPath.Location = new System.Drawing.Point(90, 546);
            this.txtBackgroundPath.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtBackgroundPath.Name = "txtBackgroundPath";
            this.txtBackgroundPath.ReadOnly = true;
            this.txtBackgroundPath.Size = new System.Drawing.Size(301, 19);
            this.txtBackgroundPath.TabIndex = 29;
            // 
            // btnSelectBackground
            // 
            this.btnSelectBackground.Location = new System.Drawing.Point(394, 546);
            this.btnSelectBackground.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectBackground.Name = "btnSelectBackground";
            this.btnSelectBackground.Size = new System.Drawing.Size(70, 19);
            this.btnSelectBackground.TabIndex = 30;
            this.btnSelectBackground.Text = "選択...";
            this.btnSelectBackground.UseVisualStyleBackColor = true;
            this.btnSelectBackground.Click += new System.EventHandler(this.btnSelectBackground_Click);
            // 
            // lblExecutablePath
            // 
            this.lblExecutablePath.AutoSize = true;
            this.lblExecutablePath.Location = new System.Drawing.Point(9, 658);
            this.lblExecutablePath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblExecutablePath.Name = "lblExecutablePath";
            this.lblExecutablePath.Size = new System.Drawing.Size(69, 12);
            this.lblExecutablePath.TabIndex = 31;
            this.lblExecutablePath.Text = "実行ファイル*";
            // 
            // txtExecutablePath
            // 
            this.txtExecutablePath.Location = new System.Drawing.Point(90, 655);
            this.txtExecutablePath.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtExecutablePath.Name = "txtExecutablePath";
            this.txtExecutablePath.ReadOnly = true;
            this.txtExecutablePath.Size = new System.Drawing.Size(301, 19);
            this.txtExecutablePath.TabIndex = 32;
            // 
            // btnSelectExecutable
            // 
            this.btnSelectExecutable.Location = new System.Drawing.Point(394, 654);
            this.btnSelectExecutable.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectExecutable.Name = "btnSelectExecutable";
            this.btnSelectExecutable.Size = new System.Drawing.Size(70, 20);
            this.btnSelectExecutable.TabIndex = 33;
            this.btnSelectExecutable.Text = "選択...";
            this.btnSelectExecutable.UseVisualStyleBackColor = true;
            this.btnSelectExecutable.Click += new System.EventHandler(this.btnSelectExecutable_Click);
            // 
            // lblArguments
            // 
            this.lblArguments.AutoSize = true;
            this.lblArguments.Location = new System.Drawing.Point(9, 714);
            this.lblArguments.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblArguments.Name = "lblArguments";
            this.lblArguments.Size = new System.Drawing.Size(72, 12);
            this.lblArguments.TabIndex = 34;
            this.lblArguments.Text = "起動オプション";
            // 
            // txtArguments
            // 
            this.txtArguments.Location = new System.Drawing.Point(90, 711);
            this.txtArguments.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtArguments.Multiline = true;
            this.txtArguments.Name = "txtArguments";
            this.txtArguments.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtArguments.Size = new System.Drawing.Size(376, 41);
            this.txtArguments.TabIndex = 35;
            // 
            // lblDevelopers
            // 
            this.lblDevelopers.AutoSize = true;
            this.lblDevelopers.Location = new System.Drawing.Point(9, 763);
            this.lblDevelopers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDevelopers.Name = "lblDevelopers";
            this.lblDevelopers.Size = new System.Drawing.Size(41, 12);
            this.lblDevelopers.TabIndex = 36;
            this.lblDevelopers.Text = "製作者";
            // 
            // dgvDevelopers
            // 
            this.dgvDevelopers.AllowUserToAddRows = false;
            this.dgvDevelopers.AllowUserToDeleteRows = false;
            this.dgvDevelopers.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvDevelopers.Location = new System.Drawing.Point(90, 761);
            this.dgvDevelopers.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.dgvDevelopers.MultiSelect = false;
            this.dgvDevelopers.Name = "dgvDevelopers";
            this.dgvDevelopers.ReadOnly = true;
            this.dgvDevelopers.RowHeadersVisible = false;
            this.dgvDevelopers.RowTemplate.Height = 21;
            this.dgvDevelopers.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvDevelopers.Size = new System.Drawing.Size(300, 80);
            this.dgvDevelopers.TabIndex = 37;
            // 
            // btnAddDeveloper
            // 
            this.btnAddDeveloper.Location = new System.Drawing.Point(394, 761);
            this.btnAddDeveloper.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnAddDeveloper.Name = "btnAddDeveloper";
            this.btnAddDeveloper.Size = new System.Drawing.Size(70, 22);
            this.btnAddDeveloper.TabIndex = 38;
            this.btnAddDeveloper.Text = "追加";
            this.btnAddDeveloper.UseVisualStyleBackColor = true;
            this.btnAddDeveloper.Click += new System.EventHandler(this.btnAddDeveloper_Click);
            // 
            // btnEditDeveloper
            // 
            this.btnEditDeveloper.Location = new System.Drawing.Point(394, 788);
            this.btnEditDeveloper.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnEditDeveloper.Name = "btnEditDeveloper";
            this.btnEditDeveloper.Size = new System.Drawing.Size(70, 22);
            this.btnEditDeveloper.TabIndex = 39;
            this.btnEditDeveloper.Text = "編集";
            this.btnEditDeveloper.UseVisualStyleBackColor = true;
            this.btnEditDeveloper.Click += new System.EventHandler(this.btnEditDeveloper_Click);
            // 
            // btnDeleteDeveloper
            // 
            this.btnDeleteDeveloper.Location = new System.Drawing.Point(394, 815);
            this.btnDeleteDeveloper.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnDeleteDeveloper.Name = "btnDeleteDeveloper";
            this.btnDeleteDeveloper.Size = new System.Drawing.Size(70, 22);
            this.btnDeleteDeveloper.TabIndex = 40;
            this.btnDeleteDeveloper.Text = "削除";
            this.btnDeleteDeveloper.UseVisualStyleBackColor = true;
            this.btnDeleteDeveloper.Click += new System.EventHandler(this.btnDeleteDeveloper_Click);
            // 
            // lblUpdateNote
            // 
            this.lblUpdateNote.AutoSize = true;
            this.lblUpdateNote.Location = new System.Drawing.Point(9, 853);
            this.lblUpdateNote.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblUpdateNote.Name = "lblUpdateNote";
            this.lblUpdateNote.Size = new System.Drawing.Size(72, 12);
            this.lblUpdateNote.TabIndex = 41;
            this.lblUpdateNote.Text = "更新内容など";
            // 
            // txtUpdateNote
            // 
            this.txtUpdateNote.Location = new System.Drawing.Point(90, 850);
            this.txtUpdateNote.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtUpdateNote.Multiline = true;
            this.txtUpdateNote.Name = "txtUpdateNote";
            this.txtUpdateNote.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtUpdateNote.Size = new System.Drawing.Size(376, 49);
            this.txtUpdateNote.TabIndex = 42;
            // 
            // btnOK
            // 
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.Location = new System.Drawing.Point(315, 912);
            this.btnOK.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(71, 24);
            this.btnOK.TabIndex = 43;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(391, 912);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(71, 24);
            this.btnCancel.TabIndex = 44;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // picThumbnailPreview
            // 
            this.picThumbnailPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picThumbnailPreview.Location = new System.Drawing.Point(90, 460);
            this.picThumbnailPreview.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.picThumbnailPreview.Name = "picThumbnailPreview";
            this.picThumbnailPreview.Size = new System.Drawing.Size(76, 80);
            this.picThumbnailPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picThumbnailPreview.TabIndex = 47;
            this.picThumbnailPreview.TabStop = false;
            // 
            // picBackgroundPreview
            // 
            this.picBackgroundPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picBackgroundPreview.Location = new System.Drawing.Point(90, 569);
            this.picBackgroundPreview.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.picBackgroundPreview.Name = "picBackgroundPreview";
            this.picBackgroundPreview.Size = new System.Drawing.Size(134, 80);
            this.picBackgroundPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picBackgroundPreview.TabIndex = 48;
            this.picBackgroundPreview.TabStop = false;
            // 
            // btnTestRun
            // 
            this.btnTestRun.Location = new System.Drawing.Point(90, 684);
            this.btnTestRun.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnTestRun.Name = "btnTestRun";
            this.btnTestRun.Size = new System.Drawing.Size(90, 22);
            this.btnTestRun.TabIndex = 49;
            this.btnTestRun.Text = "テスト起動";
            this.btnTestRun.UseVisualStyleBackColor = true;
            this.btnTestRun.Click += new System.EventHandler(this.btnTestRun_Click);
            // 
            // lblThumbnailHint
            // 
            this.lblThumbnailHint.AutoSize = true;
            this.lblThumbnailHint.ForeColor = System.Drawing.Color.Gray;
            this.lblThumbnailHint.Location = new System.Drawing.Point(170, 492);
            this.lblThumbnailHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblThumbnailHint.Name = "lblThumbnailHint";
            this.lblThumbnailHint.Size = new System.Drawing.Size(150, 12);
            this.lblThumbnailHint.TabIndex = 50;
            this.lblThumbnailHint.Text = "※ 正方形（1:1）の画像を推奨";
            // 
            // lblBackgroundHint
            // 
            this.lblBackgroundHint.AutoSize = true;
            this.lblBackgroundHint.ForeColor = System.Drawing.Color.Gray;
            this.lblBackgroundHint.Location = new System.Drawing.Point(228, 601);
            this.lblBackgroundHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblBackgroundHint.Name = "lblBackgroundHint";
            this.lblBackgroundHint.Size = new System.Drawing.Size(108, 12);
            this.lblBackgroundHint.TabIndex = 51;
            this.lblBackgroundHint.Text = "※ 16:9の画像を推奨";
            // 
            // VersionUpForm
            // 
            this.AcceptButton = this.btnOK;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(480, 952);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
            this.Controls.Add(this.txtUpdateNote);
            this.Controls.Add(this.lblUpdateNote);
            this.Controls.Add(this.btnDeleteDeveloper);
            this.Controls.Add(this.btnEditDeveloper);
            this.Controls.Add(this.btnAddDeveloper);
            this.Controls.Add(this.dgvDevelopers);
            this.Controls.Add(this.lblDevelopers);
            this.Controls.Add(this.txtArguments);
            this.Controls.Add(this.lblArguments);
            this.Controls.Add(this.btnSelectExecutable);
            this.Controls.Add(this.txtExecutablePath);
            this.Controls.Add(this.lblExecutablePath);
            this.Controls.Add(this.picThumbnailPreview);
            this.Controls.Add(this.lblThumbnailHint);
            this.Controls.Add(this.picBackgroundPreview);
            this.Controls.Add(this.lblBackgroundHint);
            this.Controls.Add(this.btnTestRun);
            this.Controls.Add(this.btnSelectBackground);
            this.Controls.Add(this.txtBackgroundPath);
            this.Controls.Add(this.lblBackgroundPath);
            this.Controls.Add(this.btnSelectThumbnail);
            this.Controls.Add(this.txtThumbnailPath);
            this.Controls.Add(this.lblThumbnailPath);
            this.Controls.Add(this.cmbSupportedConnection);
            this.Controls.Add(this.lblSupportedConnection);
            this.Controls.Add(this.chkControllerSupport);
            this.Controls.Add(this.cmbPlayTime);
            this.Controls.Add(this.lblPlayTime);
            this.Controls.Add(this.cmbDifficulty);
            this.Controls.Add(this.lblDifficulty);
            this.Controls.Add(this.numMaxPlayers);
            this.Controls.Add(this.lblMaxPlayers);
            this.Controls.Add(this.numMinPlayers);
            this.Controls.Add(this.lblMinPlayers);
            this.Controls.Add(this.clbGenre);
            this.Controls.Add(this.lblGenre);
            this.Controls.Add(this.txtDescription);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.txtTitle);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.lblGameFolderHint);
            this.Controls.Add(this.btnSelectGameFolder);
            this.Controls.Add(this.txtGameFolder);
            this.Controls.Add(this.lblGameFolder);
            this.Controls.Add(this.txtNextVersion);
            this.Controls.Add(this.lblNextVersion);
            this.Controls.Add(this.lblVersionHint);
            this.Controls.Add(this.lblCurrentVersion);
            this.Controls.Add(this.lblCurrentVersionLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "VersionUpForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "バージョンアップ";
            this.Load += new System.EventHandler(this.VersionUpForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numMinPlayers)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxPlayers)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDevelopers)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picThumbnailPreview)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picBackgroundPreview)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblCurrentVersionLabel;
        private System.Windows.Forms.Label lblCurrentVersion;
        private System.Windows.Forms.Label lblNextVersion;
        private System.Windows.Forms.TextBox txtNextVersion;
        private System.Windows.Forms.Label lblGameFolder;
        private System.Windows.Forms.TextBox txtGameFolder;
        private System.Windows.Forms.Button btnSelectGameFolder;
        private System.Windows.Forms.Label lblGameFolderHint;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.TextBox txtTitle;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.TextBox txtDescription;
        private System.Windows.Forms.Label lblGenre;
        private System.Windows.Forms.CheckedListBox clbGenre;
        private System.Windows.Forms.Label lblMinPlayers;
        private System.Windows.Forms.NumericUpDown numMinPlayers;
        private System.Windows.Forms.Label lblMaxPlayers;
        private System.Windows.Forms.NumericUpDown numMaxPlayers;
        private System.Windows.Forms.Label lblDifficulty;
        private System.Windows.Forms.ComboBox cmbDifficulty;
        private System.Windows.Forms.Label lblPlayTime;
        private System.Windows.Forms.ComboBox cmbPlayTime;
        private System.Windows.Forms.CheckBox chkControllerSupport;
        private System.Windows.Forms.Label lblSupportedConnection;
        private System.Windows.Forms.ComboBox cmbSupportedConnection;
        private System.Windows.Forms.Label lblThumbnailPath;
        private System.Windows.Forms.TextBox txtThumbnailPath;
        private System.Windows.Forms.Button btnSelectThumbnail;
        private System.Windows.Forms.Label lblBackgroundPath;
        private System.Windows.Forms.TextBox txtBackgroundPath;
        private System.Windows.Forms.Button btnSelectBackground;
        private System.Windows.Forms.Label lblExecutablePath;
        private System.Windows.Forms.TextBox txtExecutablePath;
        private System.Windows.Forms.Button btnSelectExecutable;
        private System.Windows.Forms.Label lblArguments;
        private System.Windows.Forms.TextBox txtArguments;
        private System.Windows.Forms.Label lblDevelopers;
        private System.Windows.Forms.DataGridView dgvDevelopers;
        private System.Windows.Forms.Button btnAddDeveloper;
        private System.Windows.Forms.Button btnEditDeveloper;
        private System.Windows.Forms.Button btnDeleteDeveloper;
        private System.Windows.Forms.Label lblUpdateNote;
        private System.Windows.Forms.TextBox txtUpdateNote;
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.PictureBox picThumbnailPreview;
        private System.Windows.Forms.PictureBox picBackgroundPreview;
        private System.Windows.Forms.Button btnTestRun;
        private System.Windows.Forms.Label lblThumbnailHint;
        private System.Windows.Forms.Label lblBackgroundHint;
    }
}
