namespace TonePrism.Manager
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
            this.lblVersion = new System.Windows.Forms.Label();
            this.semverInput = new TonePrism.Manager.Controls.SemverInputControl();
            this.txtGameFolder = new System.Windows.Forms.TextBox();
            this.btnSelectGameFolder = new System.Windows.Forms.Button();
            this.lblGameFolderHint = new System.Windows.Forms.Label();
            this.lblGameId = new System.Windows.Forms.Label();
            this.txtGameId = new System.Windows.Forms.TextBox();
            this.lblTitle = new System.Windows.Forms.Label();
            this.txtTitle = new System.Windows.Forms.TextBox();
            this.lblDescription = new System.Windows.Forms.Label();
            this.txtDescription = new System.Windows.Forms.TextBox();
            this.lblGenre = new System.Windows.Forms.Label();
            this.clbGenre = new System.Windows.Forms.CheckedListBox();
            this.lblReleaseYear = new System.Windows.Forms.Label();
            this.numReleaseYear = new System.Windows.Forms.NumericUpDown();
            this.chkReleaseYearUnknown = new System.Windows.Forms.CheckBox();
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
            this.lblSupportedConnection = new System.Windows.Forms.Label();
            this.cmbSupportedConnection = new System.Windows.Forms.ComboBox();
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
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.picThumbnailPreview = new System.Windows.Forms.PictureBox();
            this.picBackgroundPreview = new System.Windows.Forms.PictureBox();
            this.btnTestRun = new System.Windows.Forms.Button();
            this.lblThumbnailHint = new System.Windows.Forms.Label();
            this.lblBackgroundHint = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numReleaseYear)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinPlayers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxPlayers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvDevelopers)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picThumbnailPreview)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picBackgroundPreview)).BeginInit();
            this.SuspendLayout();
            //
            // ===== 左列: 基本情報 (x = 10 〜 470) =====
            //
            // lblGameFolder
            //
            this.lblGameFolder.AutoSize = true;
            this.lblGameFolder.Location = new System.Drawing.Point(9, 12);
            this.lblGameFolder.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGameFolder.Name = "lblGameFolder";
            this.lblGameFolder.Size = new System.Drawing.Size(76, 12);
            this.lblGameFolder.TabIndex = 0;
            this.lblGameFolder.Text = "ゲームフォルダ*";
            //
            // txtGameFolder
            //
            this.txtGameFolder.Location = new System.Drawing.Point(110, 10);
            this.txtGameFolder.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtGameFolder.Name = "txtGameFolder";
            this.txtGameFolder.ReadOnly = true;
            this.txtGameFolder.Size = new System.Drawing.Size(285, 19);
            this.txtGameFolder.TabIndex = 1;
            //
            // btnSelectGameFolder
            //
            this.btnSelectGameFolder.Location = new System.Drawing.Point(400, 9);
            this.btnSelectGameFolder.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectGameFolder.Name = "btnSelectGameFolder";
            this.btnSelectGameFolder.Size = new System.Drawing.Size(65, 20);
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
            this.lblGameFolderHint.Location = new System.Drawing.Point(110, 32);
            this.lblGameFolderHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGameFolderHint.Name = "lblGameFolderHint";
            this.lblGameFolderHint.Size = new System.Drawing.Size(350, 11);
            this.lblGameFolderHint.TabIndex = 3;
            this.lblGameFolderHint.Text = "注意: ゲームを動かすのに最低限のファイルのみを含むフォルダを選択してください。";
            //
            // lblGameId
            //
            this.lblGameId.AutoSize = true;
            this.lblGameId.Location = new System.Drawing.Point(9, 54);
            this.lblGameId.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGameId.Name = "lblGameId";
            this.lblGameId.Size = new System.Drawing.Size(52, 12);
            this.lblGameId.TabIndex = 4;
            this.lblGameId.Text = "ゲームID*";
            //
            // txtGameId
            //
            this.txtGameId.Location = new System.Drawing.Point(110, 51);
            this.txtGameId.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtGameId.Name = "txtGameId";
            this.txtGameId.Size = new System.Drawing.Size(180, 19);
            this.txtGameId.TabIndex = 5;
            //
            // lblVersion
            //
            this.lblVersion.AutoSize = true;
            this.lblVersion.Location = new System.Drawing.Point(9, 80);
            this.lblVersion.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(50, 12);
            this.lblVersion.TabIndex = 6;
            this.lblVersion.Text = "バージョン";
            //
            // semverInput (#158: NumericUpDown × 3 で SemVer 入力、txtVersion 置換)
            //
            this.semverInput.Location = new System.Drawing.Point(110, 78);
            this.semverInput.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.semverInput.Name = "semverInput";
            this.semverInput.Size = new System.Drawing.Size(300, 28);
            this.semverInput.TabIndex = 7;
            this.semverInput.VersionString = "v1.0.0";
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Location = new System.Drawing.Point(9, 110);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(46, 12);
            this.lblTitle.TabIndex = 8;
            this.lblTitle.Text = "タイトル*";
            //
            // txtTitle
            //
            this.txtTitle.Location = new System.Drawing.Point(110, 107);
            this.txtTitle.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtTitle.Name = "txtTitle";
            this.txtTitle.Size = new System.Drawing.Size(355, 19);
            this.txtTitle.TabIndex = 9;
            //
            // lblDescription
            //
            this.lblDescription.AutoSize = true;
            this.lblDescription.Location = new System.Drawing.Point(9, 132);
            this.lblDescription.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(41, 12);
            this.lblDescription.TabIndex = 10;
            this.lblDescription.Text = "説明文";
            //
            // txtDescription
            //
            this.txtDescription.Location = new System.Drawing.Point(110, 130);
            this.txtDescription.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtDescription.Multiline = true;
            // (#312) Enter を「改行」にする。未設定 (false) だと Multiline でも Enter が AcceptButton(保存) を発火する。
            this.txtDescription.AcceptsReturn = true;
            this.txtDescription.Name = "txtDescription";
            this.txtDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDescription.Size = new System.Drawing.Size(355, 60);
            this.txtDescription.TabIndex = 11;
            //
            // lblGenre
            //
            this.lblGenre.AutoSize = true;
            this.lblGenre.Location = new System.Drawing.Point(9, 200);
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
            this.clbGenre.Location = new System.Drawing.Point(110, 197);
            this.clbGenre.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.clbGenre.Name = "clbGenre";
            this.clbGenre.Size = new System.Drawing.Size(355, 158);
            this.clbGenre.TabIndex = 13;
            //
            // lblReleaseYear
            //
            this.lblReleaseYear.AutoSize = true;
            this.lblReleaseYear.Location = new System.Drawing.Point(9, 364);
            this.lblReleaseYear.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblReleaseYear.Name = "lblReleaseYear";
            this.lblReleaseYear.Size = new System.Drawing.Size(50, 12);
            this.lblReleaseYear.TabIndex = 14;
            this.lblReleaseYear.Text = "リリース年";
            //
            // numReleaseYear
            //
            this.numReleaseYear.Location = new System.Drawing.Point(110, 362);
            this.numReleaseYear.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
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
            this.numReleaseYear.Size = new System.Drawing.Size(90, 19);
            this.numReleaseYear.TabIndex = 15;
            this.numReleaseYear.Value = new decimal(new int[] {
            2024,
            0,
            0,
            0});
            //
            // chkReleaseYearUnknown
            //
            this.chkReleaseYearUnknown.AutoSize = true;
            this.chkReleaseYearUnknown.Location = new System.Drawing.Point(210, 363);
            this.chkReleaseYearUnknown.Name = "chkReleaseYearUnknown";
            this.chkReleaseYearUnknown.Size = new System.Drawing.Size(53, 16);
            this.chkReleaseYearUnknown.TabIndex = 15;
            this.chkReleaseYearUnknown.Text = "不明";
            this.chkReleaseYearUnknown.UseVisualStyleBackColor = true;
            this.chkReleaseYearUnknown.CheckedChanged += new System.EventHandler(this.chkReleaseYearUnknown_CheckedChanged);
            //
            // lblMinPlayers
            //
            this.lblMinPlayers.AutoSize = true;
            this.lblMinPlayers.Location = new System.Drawing.Point(9, 390);
            this.lblMinPlayers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblMinPlayers.Name = "lblMinPlayers";
            this.lblMinPlayers.Size = new System.Drawing.Size(88, 12);
            this.lblMinPlayers.TabIndex = 16;
            this.lblMinPlayers.Text = "最小プレイヤー数";
            //
            // numMinPlayers
            //
            this.numMinPlayers.Location = new System.Drawing.Point(110, 388);
            this.numMinPlayers.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.numMinPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMinPlayers.Name = "numMinPlayers";
            this.numMinPlayers.Size = new System.Drawing.Size(70, 19);
            this.numMinPlayers.TabIndex = 17;
            this.numMinPlayers.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            //
            // lblMaxPlayers
            //
            this.lblMaxPlayers.AutoSize = true;
            this.lblMaxPlayers.Location = new System.Drawing.Point(225, 390);
            this.lblMaxPlayers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblMaxPlayers.Name = "lblMaxPlayers";
            this.lblMaxPlayers.Size = new System.Drawing.Size(88, 12);
            this.lblMaxPlayers.TabIndex = 18;
            this.lblMaxPlayers.Text = "最大プレイヤー数";
            //
            // numMaxPlayers
            //
            this.numMaxPlayers.Location = new System.Drawing.Point(325, 388);
            this.numMaxPlayers.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.numMaxPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMaxPlayers.Name = "numMaxPlayers";
            this.numMaxPlayers.Size = new System.Drawing.Size(70, 19);
            this.numMaxPlayers.TabIndex = 19;
            this.numMaxPlayers.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            //
            // lblDifficulty
            //
            this.lblDifficulty.AutoSize = true;
            this.lblDifficulty.Location = new System.Drawing.Point(9, 416);
            this.lblDifficulty.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDifficulty.Name = "lblDifficulty";
            this.lblDifficulty.Size = new System.Drawing.Size(41, 12);
            this.lblDifficulty.TabIndex = 20;
            this.lblDifficulty.Text = "難易度";
            //
            // cmbDifficulty
            //
            this.cmbDifficulty.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDifficulty.FormattingEnabled = true;
            this.cmbDifficulty.Location = new System.Drawing.Point(110, 413);
            this.cmbDifficulty.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.cmbDifficulty.Name = "cmbDifficulty";
            this.cmbDifficulty.Size = new System.Drawing.Size(135, 20);
            this.cmbDifficulty.TabIndex = 21;
            //
            // lblPlayTime
            //
            this.lblPlayTime.AutoSize = true;
            this.lblPlayTime.Location = new System.Drawing.Point(255, 416);
            this.lblPlayTime.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblPlayTime.Name = "lblPlayTime";
            this.lblPlayTime.Size = new System.Drawing.Size(56, 12);
            this.lblPlayTime.TabIndex = 22;
            this.lblPlayTime.Text = "プレイ時間";
            //
            // cmbPlayTime
            //
            this.cmbPlayTime.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPlayTime.FormattingEnabled = true;
            this.cmbPlayTime.Location = new System.Drawing.Point(325, 413);
            this.cmbPlayTime.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.cmbPlayTime.Name = "cmbPlayTime";
            this.cmbPlayTime.Size = new System.Drawing.Size(140, 20);
            this.cmbPlayTime.TabIndex = 23;
            //
            // chkControllerSupport
            //
            this.chkControllerSupport.AutoSize = true;
            this.chkControllerSupport.Location = new System.Drawing.Point(11, 442);
            this.chkControllerSupport.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.chkControllerSupport.Name = "chkControllerSupport";
            this.chkControllerSupport.Size = new System.Drawing.Size(124, 16);
            this.chkControllerSupport.TabIndex = 24;
            this.chkControllerSupport.Text = "コントローラーサポート";
            this.chkControllerSupport.UseVisualStyleBackColor = true;
            //
            // lblSupportedConnection
            //
            this.lblSupportedConnection.AutoSize = true;
            this.lblSupportedConnection.Location = new System.Drawing.Point(9, 468);
            this.lblSupportedConnection.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblSupportedConnection.Name = "lblSupportedConnection";
            this.lblSupportedConnection.Size = new System.Drawing.Size(80, 12);
            this.lblSupportedConnection.TabIndex = 25;
            this.lblSupportedConnection.Text = "通信プレイ対応";
            //
            // cmbSupportedConnection
            //
            this.cmbSupportedConnection.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbSupportedConnection.FormattingEnabled = true;
            this.cmbSupportedConnection.Location = new System.Drawing.Point(110, 465);
            this.cmbSupportedConnection.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.cmbSupportedConnection.Name = "cmbSupportedConnection";
            this.cmbSupportedConnection.Size = new System.Drawing.Size(225, 20);
            this.cmbSupportedConnection.TabIndex = 26;
            //
            // ===== 右列: アセット & 設定 (x = 490 〜 940) =====
            //
            // lblThumbnailPath
            //
            this.lblThumbnailPath.AutoSize = true;
            this.lblThumbnailPath.Location = new System.Drawing.Point(490, 12);
            this.lblThumbnailPath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblThumbnailPath.Name = "lblThumbnailPath";
            this.lblThumbnailPath.Size = new System.Drawing.Size(78, 12);
            this.lblThumbnailPath.TabIndex = 27;
            this.lblThumbnailPath.Text = "サムネイル画像";
            //
            // txtThumbnailPath
            //
            this.txtThumbnailPath.Location = new System.Drawing.Point(595, 10);
            this.txtThumbnailPath.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtThumbnailPath.Name = "txtThumbnailPath";
            // (round 5 Phase D) ReadOnly 解除: user が直接 path を編集できるように。
            // 入力時の正規化 + validation は AddGameForm.cs の TextChanged ハンドラ + ValidateInput で対応。
            this.txtThumbnailPath.Size = new System.Drawing.Size(265, 19);
            this.txtThumbnailPath.TabIndex = 28;
            //
            // btnSelectThumbnail
            //
            this.btnSelectThumbnail.Location = new System.Drawing.Point(865, 9);
            this.btnSelectThumbnail.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectThumbnail.Name = "btnSelectThumbnail";
            this.btnSelectThumbnail.Size = new System.Drawing.Size(70, 20);
            this.btnSelectThumbnail.TabIndex = 29;
            this.btnSelectThumbnail.Text = "選択...";
            this.btnSelectThumbnail.UseVisualStyleBackColor = true;
            this.btnSelectThumbnail.Click += new System.EventHandler(this.btnSelectThumbnail_Click);
            //
            // picThumbnailPreview
            //
            this.picThumbnailPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picThumbnailPreview.Location = new System.Drawing.Point(595, 35);
            this.picThumbnailPreview.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.picThumbnailPreview.Name = "picThumbnailPreview";
            this.picThumbnailPreview.Size = new System.Drawing.Size(76, 80);
            this.picThumbnailPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picThumbnailPreview.TabIndex = 30;
            this.picThumbnailPreview.TabStop = false;
            //
            // lblThumbnailHint
            //
            this.lblThumbnailHint.AutoSize = true;
            this.lblThumbnailHint.ForeColor = System.Drawing.Color.Gray;
            this.lblThumbnailHint.Location = new System.Drawing.Point(680, 50);
            this.lblThumbnailHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblThumbnailHint.Name = "lblThumbnailHint";
            this.lblThumbnailHint.Size = new System.Drawing.Size(150, 12);
            this.lblThumbnailHint.TabIndex = 31;
            this.lblThumbnailHint.Text = "※ 正方形(1:1)の画像を推奨";
            //
            // lblBackgroundPath
            //
            this.lblBackgroundPath.AutoSize = true;
            this.lblBackgroundPath.Location = new System.Drawing.Point(490, 130);
            this.lblBackgroundPath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblBackgroundPath.Name = "lblBackgroundPath";
            this.lblBackgroundPath.Size = new System.Drawing.Size(53, 12);
            this.lblBackgroundPath.TabIndex = 32;
            this.lblBackgroundPath.Text = "背景画像";
            //
            // txtBackgroundPath
            //
            this.txtBackgroundPath.Location = new System.Drawing.Point(595, 128);
            this.txtBackgroundPath.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtBackgroundPath.Name = "txtBackgroundPath";
            // (round 5 Phase D) ReadOnly 解除
            this.txtBackgroundPath.Size = new System.Drawing.Size(265, 19);
            this.txtBackgroundPath.TabIndex = 33;
            //
            // btnSelectBackground
            //
            this.btnSelectBackground.Location = new System.Drawing.Point(865, 127);
            this.btnSelectBackground.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectBackground.Name = "btnSelectBackground";
            this.btnSelectBackground.Size = new System.Drawing.Size(70, 20);
            this.btnSelectBackground.TabIndex = 34;
            this.btnSelectBackground.Text = "選択...";
            this.btnSelectBackground.UseVisualStyleBackColor = true;
            this.btnSelectBackground.Click += new System.EventHandler(this.btnSelectBackground_Click);
            //
            // picBackgroundPreview
            //
            this.picBackgroundPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picBackgroundPreview.Location = new System.Drawing.Point(595, 153);
            this.picBackgroundPreview.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.picBackgroundPreview.Name = "picBackgroundPreview";
            this.picBackgroundPreview.Size = new System.Drawing.Size(134, 80);
            this.picBackgroundPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picBackgroundPreview.TabIndex = 35;
            this.picBackgroundPreview.TabStop = false;
            //
            // lblBackgroundHint
            //
            this.lblBackgroundHint.AutoSize = true;
            this.lblBackgroundHint.ForeColor = System.Drawing.Color.Gray;
            this.lblBackgroundHint.Location = new System.Drawing.Point(740, 168);
            this.lblBackgroundHint.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblBackgroundHint.Name = "lblBackgroundHint";
            this.lblBackgroundHint.Size = new System.Drawing.Size(108, 12);
            this.lblBackgroundHint.TabIndex = 36;
            this.lblBackgroundHint.Text = "※ 16:9の画像を推奨";
            //
            // lblExecutablePath
            //
            this.lblExecutablePath.AutoSize = true;
            this.lblExecutablePath.Location = new System.Drawing.Point(490, 248);
            this.lblExecutablePath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblExecutablePath.Name = "lblExecutablePath";
            this.lblExecutablePath.Size = new System.Drawing.Size(69, 12);
            this.lblExecutablePath.TabIndex = 37;
            this.lblExecutablePath.Text = "実行ファイル*";
            //
            // txtExecutablePath
            //
            this.txtExecutablePath.Location = new System.Drawing.Point(595, 246);
            this.txtExecutablePath.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtExecutablePath.Name = "txtExecutablePath";
            // (round 5 Phase D) ReadOnly 解除
            this.txtExecutablePath.Size = new System.Drawing.Size(265, 19);
            this.txtExecutablePath.TabIndex = 38;
            //
            // btnSelectExecutable
            //
            this.btnSelectExecutable.Location = new System.Drawing.Point(865, 245);
            this.btnSelectExecutable.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnSelectExecutable.Name = "btnSelectExecutable";
            this.btnSelectExecutable.Size = new System.Drawing.Size(70, 20);
            this.btnSelectExecutable.TabIndex = 39;
            this.btnSelectExecutable.Text = "選択...";
            this.btnSelectExecutable.UseVisualStyleBackColor = true;
            this.btnSelectExecutable.Click += new System.EventHandler(this.btnSelectExecutable_Click);
            //
            // btnTestRun
            //
            this.btnTestRun.Location = new System.Drawing.Point(595, 270);
            this.btnTestRun.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnTestRun.Name = "btnTestRun";
            this.btnTestRun.Size = new System.Drawing.Size(90, 22);
            this.btnTestRun.TabIndex = 40;
            this.btnTestRun.Text = "テスト起動";
            this.btnTestRun.UseVisualStyleBackColor = true;
            this.btnTestRun.Click += new System.EventHandler(this.btnTestRun_Click);
            //
            // lblArguments
            //
            this.lblArguments.AutoSize = true;
            this.lblArguments.Location = new System.Drawing.Point(490, 305);
            this.lblArguments.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblArguments.Name = "lblArguments";
            this.lblArguments.Size = new System.Drawing.Size(72, 12);
            this.lblArguments.TabIndex = 41;
            this.lblArguments.Text = "起動オプション";
            //
            // txtArguments
            //
            this.txtArguments.Location = new System.Drawing.Point(595, 303);
            this.txtArguments.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.txtArguments.Multiline = true;
            this.txtArguments.Name = "txtArguments";
            this.txtArguments.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtArguments.Size = new System.Drawing.Size(340, 36);
            this.txtArguments.TabIndex = 42;
            //
            // lblDevelopers
            //
            this.lblDevelopers.AutoSize = true;
            this.lblDevelopers.Location = new System.Drawing.Point(490, 355);
            this.lblDevelopers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDevelopers.Name = "lblDevelopers";
            this.lblDevelopers.Size = new System.Drawing.Size(65, 12);
            this.lblDevelopers.TabIndex = 43;
            this.lblDevelopers.Text = "製作者情報";
            //
            // dgvDevelopers
            //
            this.dgvDevelopers.AllowUserToResizeRows = false;
            this.dgvDevelopers.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvDevelopers.Location = new System.Drawing.Point(595, 353);
            this.dgvDevelopers.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.dgvDevelopers.Name = "dgvDevelopers";
            this.dgvDevelopers.RowHeadersVisible = false;
            this.dgvDevelopers.RowHeadersWidth = 51;
            this.dgvDevelopers.Size = new System.Drawing.Size(280, 96);
            this.dgvDevelopers.TabIndex = 44;
            //
            // btnAddDeveloper
            //
            this.btnAddDeveloper.Location = new System.Drawing.Point(880, 353);
            this.btnAddDeveloper.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnAddDeveloper.Name = "btnAddDeveloper";
            this.btnAddDeveloper.Size = new System.Drawing.Size(56, 22);
            this.btnAddDeveloper.TabIndex = 45;
            this.btnAddDeveloper.Text = "追加";
            this.btnAddDeveloper.UseVisualStyleBackColor = true;
            this.btnAddDeveloper.Click += new System.EventHandler(this.btnAddDeveloper_Click);
            //
            // btnEditDeveloper
            //
            this.btnEditDeveloper.Location = new System.Drawing.Point(880, 379);
            this.btnEditDeveloper.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnEditDeveloper.Name = "btnEditDeveloper";
            this.btnEditDeveloper.Size = new System.Drawing.Size(56, 22);
            this.btnEditDeveloper.TabIndex = 46;
            this.btnEditDeveloper.Text = "編集";
            this.btnEditDeveloper.UseVisualStyleBackColor = true;
            this.btnEditDeveloper.Click += new System.EventHandler(this.btnEditDeveloper_Click);
            //
            // btnDeleteDeveloper
            //
            this.btnDeleteDeveloper.Location = new System.Drawing.Point(880, 405);
            this.btnDeleteDeveloper.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnDeleteDeveloper.Name = "btnDeleteDeveloper";
            this.btnDeleteDeveloper.Size = new System.Drawing.Size(56, 22);
            this.btnDeleteDeveloper.TabIndex = 47;
            this.btnDeleteDeveloper.Text = "削除";
            this.btnDeleteDeveloper.UseVisualStyleBackColor = true;
            this.btnDeleteDeveloper.Click += new System.EventHandler(this.btnDeleteDeveloper_Click);
            //
            // ===== 下部: OK / キャンセル =====
            //
            // btnOK
            //
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.Location = new System.Drawing.Point(786, 525);
            this.btnOK.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(71, 28);
            this.btnOK.TabIndex = 48;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(862, 525);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(71, 28);
            this.btnCancel.TabIndex = 49;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // AddGameForm
            //
            // (#312) AcceptButton はあえて未設定。リリース年など各フィールドで Enter による確定をしようとした際に
            // 保存(btnOK)が誤発火する事故が多発したため、Enter での自動保存を無効化（保存は btnOK のクリックで明示）。
            // 説明欄の改行は txtDescription.AcceptsReturn=true で確保。CancelButton(=Esc) は残す。
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(950, 570);
            this.Controls.Add(this.btnDeleteDeveloper);
            this.Controls.Add(this.btnEditDeveloper);
            this.Controls.Add(this.btnAddDeveloper);
            this.Controls.Add(this.dgvDevelopers);
            this.Controls.Add(this.lblDevelopers);
            this.Controls.Add(this.txtArguments);
            this.Controls.Add(this.lblArguments);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOK);
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
            this.Controls.Add(this.chkControllerSupport);
            this.Controls.Add(this.cmbPlayTime);
            this.Controls.Add(this.lblPlayTime);
            this.Controls.Add(this.cmbSupportedConnection);
            this.Controls.Add(this.lblSupportedConnection);
            this.Controls.Add(this.cmbDifficulty);
            this.Controls.Add(this.lblDifficulty);
            this.Controls.Add(this.numMaxPlayers);
            this.Controls.Add(this.lblMaxPlayers);
            this.Controls.Add(this.numMinPlayers);
            this.Controls.Add(this.lblMinPlayers);
            this.Controls.Add(this.chkReleaseYearUnknown);
            this.Controls.Add(this.numReleaseYear);
            this.Controls.Add(this.lblReleaseYear);
            this.Controls.Add(this.clbGenre);
            this.Controls.Add(this.lblGenre);
            this.Controls.Add(this.txtDescription);
            this.Controls.Add(this.lblDescription);
            this.Controls.Add(this.txtTitle);
            this.Controls.Add(this.lblTitle);
            this.Controls.Add(this.txtGameId);
            this.Controls.Add(this.lblGameId);
            this.Controls.Add(this.lblGameFolderHint);
            this.Controls.Add(this.btnSelectGameFolder);
            this.Controls.Add(this.txtGameFolder);
            this.Controls.Add(this.lblGameFolder);
            this.Controls.Add(this.lblVersion);
            this.Controls.Add(this.semverInput);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
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
            ((System.ComponentModel.ISupportInitialize)(this.picThumbnailPreview)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picBackgroundPreview)).EndInit();
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
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.TextBox txtTitle;
        private System.Windows.Forms.Label lblDescription;
        private System.Windows.Forms.TextBox txtDescription;
        private System.Windows.Forms.Label lblGenre;
        private System.Windows.Forms.CheckedListBox clbGenre;
        private System.Windows.Forms.Label lblReleaseYear;
        private System.Windows.Forms.NumericUpDown numReleaseYear;
        private System.Windows.Forms.CheckBox chkReleaseYearUnknown;
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
        private System.Windows.Forms.Button btnOK;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblVersion;
        private TonePrism.Manager.Controls.SemverInputControl semverInput;
        private System.Windows.Forms.PictureBox picThumbnailPreview;
        private System.Windows.Forms.PictureBox picBackgroundPreview;
        private System.Windows.Forms.Button btnTestRun;
        private System.Windows.Forms.Label lblThumbnailHint;
        private System.Windows.Forms.Label lblBackgroundHint;
    }
}
