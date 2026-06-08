namespace TonePrism.Manager
{
    partial class EditGameForm
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
            this.chkIsVisible = new System.Windows.Forms.CheckBox();
            this.lblThumbnailPath = new System.Windows.Forms.Label();
            this.txtThumbnailPath = new System.Windows.Forms.TextBox();
            this.btnSelectThumbnail = new System.Windows.Forms.Button();
            this.lblBackgroundPath = new System.Windows.Forms.Label();
            this.txtBackgroundPath = new System.Windows.Forms.TextBox();
            this.lblVersionManagement = new System.Windows.Forms.Label();
            this.cmbVersionList = new System.Windows.Forms.ComboBox();
            this.btnDeleteVersion = new System.Windows.Forms.Button();
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
            this.lblVersionDescription = new System.Windows.Forms.Label();
            this.txtVersionDescription = new System.Windows.Forms.TextBox();
            this.btnOK = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.picThumbnailPreview = new System.Windows.Forms.PictureBox();
            this.picBackgroundPreview = new System.Windows.Forms.PictureBox();
            this.btnTestRun = new System.Windows.Forms.Button();
            this.lblThumbnailHint = new System.Windows.Forms.Label();
            this.lblBackgroundHint = new System.Windows.Forms.Label();
            this.lblVersionName = new System.Windows.Forms.Label();
            this.semverVersionName = new TonePrism.Manager.Controls.SemverInputControl();
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
            this.lblGameFolder.Size = new System.Drawing.Size(70, 12);
            this.lblGameFolder.TabIndex = 0;
            this.lblGameFolder.Text = "ゲームフォルダ";
            //
            // txtGameFolder
            //
            this.txtGameFolder.Enabled = false;
            this.txtGameFolder.Location = new System.Drawing.Point(110, 10);
            this.txtGameFolder.Margin = new System.Windows.Forms.Padding(2);
            this.txtGameFolder.Name = "txtGameFolder";
            this.txtGameFolder.Size = new System.Drawing.Size(355, 19);
            this.txtGameFolder.TabIndex = 1;
            //
            // lblGameId
            //
            this.lblGameId.AutoSize = true;
            this.lblGameId.Location = new System.Drawing.Point(9, 38);
            this.lblGameId.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGameId.Name = "lblGameId";
            this.lblGameId.Size = new System.Drawing.Size(46, 12);
            this.lblGameId.TabIndex = 2;
            this.lblGameId.Text = "ゲームID";
            //
            // txtGameId
            //
            this.txtGameId.Enabled = true;
            this.txtGameId.Location = new System.Drawing.Point(110, 36);
            this.txtGameId.Margin = new System.Windows.Forms.Padding(2);
            this.txtGameId.Name = "txtGameId";
            this.txtGameId.Size = new System.Drawing.Size(225, 19);
            this.txtGameId.TabIndex = 3;
            //
            // lblTitle
            //
            this.lblTitle.AutoSize = true;
            this.lblTitle.Location = new System.Drawing.Point(9, 64);
            this.lblTitle.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(46, 12);
            this.lblTitle.TabIndex = 5;
            this.lblTitle.Text = "タイトル*";
            //
            // txtTitle
            //
            this.txtTitle.Location = new System.Drawing.Point(110, 62);
            this.txtTitle.Margin = new System.Windows.Forms.Padding(2);
            this.txtTitle.Name = "txtTitle";
            this.txtTitle.Size = new System.Drawing.Size(355, 19);
            this.txtTitle.TabIndex = 6;
            //
            // lblDescription
            //
            this.lblDescription.AutoSize = true;
            this.lblDescription.Location = new System.Drawing.Point(9, 90);
            this.lblDescription.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new System.Drawing.Size(41, 12);
            this.lblDescription.TabIndex = 7;
            this.lblDescription.Text = "説明文";
            //
            // txtDescription
            //
            this.txtDescription.Location = new System.Drawing.Point(110, 88);
            this.txtDescription.Margin = new System.Windows.Forms.Padding(2);
            this.txtDescription.Multiline = true;
            // (#312) Enter を「改行」にする。未設定 (false) だと Multiline でも Enter が AcceptButton(保存) を発火する。
            this.txtDescription.AcceptsReturn = true;
            this.txtDescription.Name = "txtDescription";
            this.txtDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtDescription.Size = new System.Drawing.Size(355, 60);
            this.txtDescription.TabIndex = 8;
            //
            // lblGenre
            //
            this.lblGenre.AutoSize = true;
            this.lblGenre.Location = new System.Drawing.Point(9, 158);
            this.lblGenre.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblGenre.Name = "lblGenre";
            this.lblGenre.Size = new System.Drawing.Size(42, 12);
            this.lblGenre.TabIndex = 9;
            this.lblGenre.Text = "ジャンル";
            //
            // clbGenre
            //
            this.clbGenre.CheckOnClick = true;
            this.clbGenre.FormattingEnabled = true;
            this.clbGenre.Location = new System.Drawing.Point(110, 155);
            this.clbGenre.Margin = new System.Windows.Forms.Padding(2);
            this.clbGenre.Name = "clbGenre";
            this.clbGenre.Size = new System.Drawing.Size(355, 158);
            this.clbGenre.TabIndex = 10;
            //
            // lblReleaseYear
            //
            this.lblReleaseYear.AutoSize = true;
            this.lblReleaseYear.Location = new System.Drawing.Point(9, 322);
            this.lblReleaseYear.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblReleaseYear.Name = "lblReleaseYear";
            this.lblReleaseYear.Size = new System.Drawing.Size(50, 12);
            this.lblReleaseYear.TabIndex = 11;
            this.lblReleaseYear.Text = "リリース年";
            //
            // numReleaseYear
            //
            this.numReleaseYear.Location = new System.Drawing.Point(110, 320);
            this.numReleaseYear.Margin = new System.Windows.Forms.Padding(2);
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
            this.lblMinPlayers.Location = new System.Drawing.Point(9, 348);
            this.lblMinPlayers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblMinPlayers.Name = "lblMinPlayers";
            this.lblMinPlayers.Size = new System.Drawing.Size(88, 12);
            this.lblMinPlayers.TabIndex = 13;
            this.lblMinPlayers.Text = "最小プレイヤー数";
            //
            // numMinPlayers
            //
            this.numMinPlayers.Location = new System.Drawing.Point(110, 346);
            this.numMinPlayers.Margin = new System.Windows.Forms.Padding(2);
            this.numMinPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMinPlayers.Name = "numMinPlayers";
            this.numMinPlayers.Size = new System.Drawing.Size(70, 19);
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
            this.lblMaxPlayers.Location = new System.Drawing.Point(225, 348);
            this.lblMaxPlayers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblMaxPlayers.Name = "lblMaxPlayers";
            this.lblMaxPlayers.Size = new System.Drawing.Size(88, 12);
            this.lblMaxPlayers.TabIndex = 15;
            this.lblMaxPlayers.Text = "最大プレイヤー数";
            //
            // numMaxPlayers
            //
            this.numMaxPlayers.Location = new System.Drawing.Point(325, 346);
            this.numMaxPlayers.Margin = new System.Windows.Forms.Padding(2);
            this.numMaxPlayers.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numMaxPlayers.Name = "numMaxPlayers";
            this.numMaxPlayers.Size = new System.Drawing.Size(70, 19);
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
            this.lblDifficulty.Location = new System.Drawing.Point(9, 374);
            this.lblDifficulty.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDifficulty.Name = "lblDifficulty";
            this.lblDifficulty.Size = new System.Drawing.Size(41, 12);
            this.lblDifficulty.TabIndex = 17;
            this.lblDifficulty.Text = "難易度";
            //
            // cmbDifficulty
            //
            this.cmbDifficulty.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbDifficulty.FormattingEnabled = true;
            this.cmbDifficulty.Location = new System.Drawing.Point(110, 371);
            this.cmbDifficulty.Margin = new System.Windows.Forms.Padding(2);
            this.cmbDifficulty.Name = "cmbDifficulty";
            this.cmbDifficulty.Size = new System.Drawing.Size(135, 20);
            this.cmbDifficulty.TabIndex = 18;
            //
            // lblPlayTime
            //
            this.lblPlayTime.AutoSize = true;
            this.lblPlayTime.Location = new System.Drawing.Point(255, 374);
            this.lblPlayTime.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblPlayTime.Name = "lblPlayTime";
            this.lblPlayTime.Size = new System.Drawing.Size(56, 12);
            this.lblPlayTime.TabIndex = 19;
            this.lblPlayTime.Text = "プレイ時間";
            //
            // cmbPlayTime
            //
            this.cmbPlayTime.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPlayTime.FormattingEnabled = true;
            this.cmbPlayTime.Location = new System.Drawing.Point(325, 371);
            this.cmbPlayTime.Margin = new System.Windows.Forms.Padding(2);
            this.cmbPlayTime.Name = "cmbPlayTime";
            this.cmbPlayTime.Size = new System.Drawing.Size(140, 20);
            this.cmbPlayTime.TabIndex = 20;
            //
            // chkControllerSupport
            //
            this.chkControllerSupport.AutoSize = true;
            this.chkControllerSupport.Location = new System.Drawing.Point(11, 400);
            this.chkControllerSupport.Margin = new System.Windows.Forms.Padding(2);
            this.chkControllerSupport.Name = "chkControllerSupport";
            this.chkControllerSupport.Size = new System.Drawing.Size(124, 16);
            this.chkControllerSupport.TabIndex = 21;
            this.chkControllerSupport.Text = "コントローラーサポート";
            this.chkControllerSupport.UseVisualStyleBackColor = true;
            //
            // chkIsVisible
            //
            this.chkIsVisible.AutoSize = true;
            this.chkIsVisible.Location = new System.Drawing.Point(150, 400);
            this.chkIsVisible.Margin = new System.Windows.Forms.Padding(2);
            this.chkIsVisible.Name = "chkIsVisible";
            this.chkIsVisible.Size = new System.Drawing.Size(120, 16);
            this.chkIsVisible.TabIndex = 22;
            this.chkIsVisible.Text = "ランチャーに表示する";
            this.chkIsVisible.UseVisualStyleBackColor = true;
            //
            // lblSupportedConnection
            //
            this.lblSupportedConnection.AutoSize = true;
            this.lblSupportedConnection.Location = new System.Drawing.Point(9, 426);
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
            this.cmbSupportedConnection.Location = new System.Drawing.Point(110, 423);
            this.cmbSupportedConnection.Margin = new System.Windows.Forms.Padding(2);
            this.cmbSupportedConnection.Name = "cmbSupportedConnection";
            this.cmbSupportedConnection.Size = new System.Drawing.Size(225, 20);
            this.cmbSupportedConnection.TabIndex = 24;
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
            this.lblThumbnailPath.TabIndex = 25;
            this.lblThumbnailPath.Text = "サムネイル画像";
            //
            // txtThumbnailPath
            //
            this.txtThumbnailPath.Location = new System.Drawing.Point(595, 10);
            this.txtThumbnailPath.Margin = new System.Windows.Forms.Padding(2);
            this.txtThumbnailPath.Name = "txtThumbnailPath";
            // (round 5 Phase D) ReadOnly 解除: user が直接 path を編集できるように。
            this.txtThumbnailPath.Size = new System.Drawing.Size(265, 19);
            this.txtThumbnailPath.TabIndex = 26;
            //
            // btnSelectThumbnail
            //
            this.btnSelectThumbnail.Location = new System.Drawing.Point(865, 9);
            this.btnSelectThumbnail.Margin = new System.Windows.Forms.Padding(2);
            this.btnSelectThumbnail.Name = "btnSelectThumbnail";
            this.btnSelectThumbnail.Size = new System.Drawing.Size(70, 20);
            this.btnSelectThumbnail.TabIndex = 27;
            this.btnSelectThumbnail.Text = "選択...";
            this.btnSelectThumbnail.UseVisualStyleBackColor = true;
            this.btnSelectThumbnail.Click += new System.EventHandler(this.btnSelectThumbnail_Click);
            //
            // picThumbnailPreview
            //
            this.picThumbnailPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picThumbnailPreview.Location = new System.Drawing.Point(595, 35);
            this.picThumbnailPreview.Margin = new System.Windows.Forms.Padding(2);
            this.picThumbnailPreview.Name = "picThumbnailPreview";
            this.picThumbnailPreview.Size = new System.Drawing.Size(76, 80);
            this.picThumbnailPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picThumbnailPreview.TabIndex = 28;
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
            this.lblThumbnailHint.TabIndex = 29;
            this.lblThumbnailHint.Text = "※ 正方形（1:1）の画像を推奨";
            //
            // lblBackgroundPath
            //
            this.lblBackgroundPath.AutoSize = true;
            this.lblBackgroundPath.Location = new System.Drawing.Point(490, 130);
            this.lblBackgroundPath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblBackgroundPath.Name = "lblBackgroundPath";
            this.lblBackgroundPath.Size = new System.Drawing.Size(53, 12);
            this.lblBackgroundPath.TabIndex = 30;
            this.lblBackgroundPath.Text = "背景画像";
            //
            // txtBackgroundPath
            //
            this.txtBackgroundPath.Location = new System.Drawing.Point(595, 128);
            this.txtBackgroundPath.Margin = new System.Windows.Forms.Padding(2);
            this.txtBackgroundPath.Name = "txtBackgroundPath";
            // (round 5 Phase D) ReadOnly 解除
            this.txtBackgroundPath.Size = new System.Drawing.Size(265, 19);
            this.txtBackgroundPath.TabIndex = 31;
            //
            // btnSelectBackground
            //
            this.btnSelectBackground.Location = new System.Drawing.Point(865, 127);
            this.btnSelectBackground.Margin = new System.Windows.Forms.Padding(2);
            this.btnSelectBackground.Name = "btnSelectBackground";
            this.btnSelectBackground.Size = new System.Drawing.Size(70, 20);
            this.btnSelectBackground.TabIndex = 32;
            this.btnSelectBackground.Text = "選択...";
            this.btnSelectBackground.UseVisualStyleBackColor = true;
            this.btnSelectBackground.Click += new System.EventHandler(this.btnSelectBackground_Click);
            //
            // picBackgroundPreview
            //
            this.picBackgroundPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picBackgroundPreview.Location = new System.Drawing.Point(595, 153);
            this.picBackgroundPreview.Margin = new System.Windows.Forms.Padding(2);
            this.picBackgroundPreview.Name = "picBackgroundPreview";
            this.picBackgroundPreview.Size = new System.Drawing.Size(134, 80);
            this.picBackgroundPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picBackgroundPreview.TabIndex = 33;
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
            this.lblBackgroundHint.TabIndex = 34;
            this.lblBackgroundHint.Text = "※ 16:9の画像を推奨";
            //
            // lblExecutablePath
            //
            this.lblExecutablePath.AutoSize = true;
            this.lblExecutablePath.Location = new System.Drawing.Point(490, 248);
            this.lblExecutablePath.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblExecutablePath.Name = "lblExecutablePath";
            this.lblExecutablePath.Size = new System.Drawing.Size(69, 12);
            this.lblExecutablePath.TabIndex = 35;
            this.lblExecutablePath.Text = "実行ファイル*";
            //
            // txtExecutablePath
            //
            this.txtExecutablePath.Location = new System.Drawing.Point(595, 246);
            this.txtExecutablePath.Margin = new System.Windows.Forms.Padding(2);
            this.txtExecutablePath.Name = "txtExecutablePath";
            // (round 5 Phase D) ReadOnly 解除
            this.txtExecutablePath.Size = new System.Drawing.Size(265, 19);
            this.txtExecutablePath.TabIndex = 36;
            //
            // btnSelectExecutable
            //
            this.btnSelectExecutable.Location = new System.Drawing.Point(865, 245);
            this.btnSelectExecutable.Margin = new System.Windows.Forms.Padding(2);
            this.btnSelectExecutable.Name = "btnSelectExecutable";
            this.btnSelectExecutable.Size = new System.Drawing.Size(70, 20);
            this.btnSelectExecutable.TabIndex = 37;
            this.btnSelectExecutable.Text = "選択...";
            this.btnSelectExecutable.UseVisualStyleBackColor = true;
            this.btnSelectExecutable.Click += new System.EventHandler(this.btnSelectExecutable_Click);
            //
            // btnTestRun
            //
            this.btnTestRun.Location = new System.Drawing.Point(595, 270);
            this.btnTestRun.Margin = new System.Windows.Forms.Padding(2);
            this.btnTestRun.Name = "btnTestRun";
            this.btnTestRun.Size = new System.Drawing.Size(90, 22);
            this.btnTestRun.TabIndex = 38;
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
            this.lblArguments.TabIndex = 39;
            this.lblArguments.Text = "起動オプション";
            //
            // txtArguments
            //
            this.txtArguments.Location = new System.Drawing.Point(595, 303);
            this.txtArguments.Margin = new System.Windows.Forms.Padding(2);
            this.txtArguments.Multiline = true;
            this.txtArguments.Name = "txtArguments";
            this.txtArguments.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtArguments.Size = new System.Drawing.Size(340, 36);
            this.txtArguments.TabIndex = 40;
            //
            // lblVersionName
            //
            this.lblVersionName.AutoSize = true;
            this.lblVersionName.Location = new System.Drawing.Point(490, 352);
            this.lblVersionName.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblVersionName.Name = "lblVersionName";
            this.lblVersionName.Size = new System.Drawing.Size(74, 12);
            this.lblVersionName.TabIndex = 41;
            this.lblVersionName.Text = "バージョン番号";
            //
            // semverVersionName (#158: NumericUpDown × 3 + suffix で SemVer 形式の typo 排除、txtVersionName 置換)
            //
            this.semverVersionName.Location = new System.Drawing.Point(595, 346);
            this.semverVersionName.Margin = new System.Windows.Forms.Padding(2);
            this.semverVersionName.Name = "semverVersionName";
            this.semverVersionName.Size = new System.Drawing.Size(300, 28);
            this.semverVersionName.TabIndex = 42;
            //
            // lblVersionManagement
            //
            this.lblVersionManagement.Location = new System.Drawing.Point(490, 376);
            this.lblVersionManagement.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblVersionManagement.Name = "lblVersionManagement";
            this.lblVersionManagement.Size = new System.Drawing.Size(98, 28);
            this.lblVersionManagement.TabIndex = 43;
            this.lblVersionManagement.Text = "ランチャーで表示\r\nするバージョン";
            //
            // cmbVersionList
            //
            this.cmbVersionList.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbVersionList.FormattingEnabled = true;
            this.cmbVersionList.Location = new System.Drawing.Point(595, 382);
            this.cmbVersionList.Margin = new System.Windows.Forms.Padding(2);
            this.cmbVersionList.Name = "cmbVersionList";
            this.cmbVersionList.Size = new System.Drawing.Size(200, 20);
            this.cmbVersionList.TabIndex = 44;
            this.cmbVersionList.SelectedIndexChanged += new System.EventHandler(this.cmbVersionList_SelectedIndexChanged);
            //
            // btnDeleteVersion
            //
            this.btnDeleteVersion.Location = new System.Drawing.Point(800, 381);
            this.btnDeleteVersion.Margin = new System.Windows.Forms.Padding(2);
            this.btnDeleteVersion.Name = "btnDeleteVersion";
            this.btnDeleteVersion.Size = new System.Drawing.Size(140, 23);
            this.btnDeleteVersion.TabIndex = 45;
            this.btnDeleteVersion.Text = "このバージョンを削除";
            this.btnDeleteVersion.UseVisualStyleBackColor = true;
            this.btnDeleteVersion.Click += new System.EventHandler(this.btnDeleteVersion_Click);
            //
            // lblVersionDescription
            //
            this.lblVersionDescription.AutoSize = true;
            this.lblVersionDescription.Location = new System.Drawing.Point(490, 415);
            this.lblVersionDescription.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblVersionDescription.Name = "lblVersionDescription";
            this.lblVersionDescription.Size = new System.Drawing.Size(53, 12);
            this.lblVersionDescription.TabIndex = 47;
            this.lblVersionDescription.Text = "更新内容";
            //
            // txtVersionDescription
            //
            this.txtVersionDescription.Location = new System.Drawing.Point(595, 413);
            this.txtVersionDescription.Margin = new System.Windows.Forms.Padding(2);
            this.txtVersionDescription.Multiline = true;
            this.txtVersionDescription.Name = "txtVersionDescription";
            this.txtVersionDescription.ReadOnly = true;
            this.txtVersionDescription.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtVersionDescription.Size = new System.Drawing.Size(340, 50);
            this.txtVersionDescription.TabIndex = 48;
            //
            // lblDevelopers
            //
            this.lblDevelopers.AutoSize = true;
            this.lblDevelopers.Location = new System.Drawing.Point(490, 478);
            this.lblDevelopers.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblDevelopers.Name = "lblDevelopers";
            this.lblDevelopers.Size = new System.Drawing.Size(65, 12);
            this.lblDevelopers.TabIndex = 49;
            this.lblDevelopers.Text = "製作者情報";
            //
            // dgvDevelopers
            //
            this.dgvDevelopers.AllowUserToResizeRows = false;
            this.dgvDevelopers.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvDevelopers.Location = new System.Drawing.Point(595, 476);
            this.dgvDevelopers.Margin = new System.Windows.Forms.Padding(2);
            this.dgvDevelopers.Name = "dgvDevelopers";
            this.dgvDevelopers.RowHeadersVisible = false;
            this.dgvDevelopers.RowHeadersWidth = 51;
            this.dgvDevelopers.Size = new System.Drawing.Size(280, 96);
            this.dgvDevelopers.TabIndex = 50;
            //
            // btnAddDeveloper
            //
            this.btnAddDeveloper.Location = new System.Drawing.Point(880, 476);
            this.btnAddDeveloper.Margin = new System.Windows.Forms.Padding(2);
            this.btnAddDeveloper.Name = "btnAddDeveloper";
            this.btnAddDeveloper.Size = new System.Drawing.Size(56, 22);
            this.btnAddDeveloper.TabIndex = 51;
            this.btnAddDeveloper.Text = "追加";
            this.btnAddDeveloper.UseVisualStyleBackColor = true;
            this.btnAddDeveloper.Click += new System.EventHandler(this.btnAddDeveloper_Click);
            //
            // btnEditDeveloper
            //
            this.btnEditDeveloper.Location = new System.Drawing.Point(880, 502);
            this.btnEditDeveloper.Margin = new System.Windows.Forms.Padding(2);
            this.btnEditDeveloper.Name = "btnEditDeveloper";
            this.btnEditDeveloper.Size = new System.Drawing.Size(56, 22);
            this.btnEditDeveloper.TabIndex = 52;
            this.btnEditDeveloper.Text = "編集";
            this.btnEditDeveloper.UseVisualStyleBackColor = true;
            this.btnEditDeveloper.Click += new System.EventHandler(this.btnEditDeveloper_Click);
            //
            // btnDeleteDeveloper
            //
            this.btnDeleteDeveloper.Location = new System.Drawing.Point(880, 528);
            this.btnDeleteDeveloper.Margin = new System.Windows.Forms.Padding(2);
            this.btnDeleteDeveloper.Name = "btnDeleteDeveloper";
            this.btnDeleteDeveloper.Size = new System.Drawing.Size(56, 22);
            this.btnDeleteDeveloper.TabIndex = 53;
            this.btnDeleteDeveloper.Text = "削除";
            this.btnDeleteDeveloper.UseVisualStyleBackColor = true;
            this.btnDeleteDeveloper.Click += new System.EventHandler(this.btnDeleteDeveloper_Click);
            //
            // ===== 下部: OK / キャンセル =====
            //
            // btnOK
            //
            this.btnOK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOK.Location = new System.Drawing.Point(786, 600);
            this.btnOK.Margin = new System.Windows.Forms.Padding(2);
            this.btnOK.Name = "btnOK";
            this.btnOK.Size = new System.Drawing.Size(71, 28);
            this.btnOK.TabIndex = 54;
            this.btnOK.Text = "OK";
            this.btnOK.UseVisualStyleBackColor = true;
            this.btnOK.Click += new System.EventHandler(this.btnOK_Click);
            //
            // btnCancel
            //
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(862, 600);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(2);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(71, 28);
            this.btnCancel.TabIndex = 55;
            this.btnCancel.Text = "キャンセル";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            //
            // EditGameForm
            //
            // (#312) AcceptButton はあえて未設定。リリース年など各フィールドで Enter による確定をしようとした際に
            // 保存(btnOK)が誤発火する事故が多発したため、Enter での自動保存を無効化（保存は btnOK のクリックで明示）。
            // 説明欄の改行は txtDescription.AcceptsReturn=true で確保。CancelButton(=Esc) は残す。
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(950, 645);
            this.Controls.Add(this.semverVersionName);
            this.Controls.Add(this.lblVersionName);
            this.Controls.Add(this.picThumbnailPreview);
            this.Controls.Add(this.lblThumbnailHint);
            this.Controls.Add(this.picBackgroundPreview);
            this.Controls.Add(this.lblBackgroundHint);
            this.Controls.Add(this.btnTestRun);
            this.Controls.Add(this.txtVersionDescription);
            this.Controls.Add(this.lblVersionDescription);
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
            this.Controls.Add(this.btnSelectBackground);
            this.Controls.Add(this.txtBackgroundPath);
            this.Controls.Add(this.lblBackgroundPath);
            this.Controls.Add(this.btnSelectThumbnail);
            this.Controls.Add(this.txtThumbnailPath);
            this.Controls.Add(this.lblThumbnailPath);
            this.Controls.Add(this.cmbVersionList);
            this.Controls.Add(this.btnDeleteVersion);
            this.Controls.Add(this.lblVersionManagement);
            this.Controls.Add(this.chkIsVisible);
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
            this.Controls.Add(this.txtGameFolder);
            this.Controls.Add(this.lblGameFolder);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(2);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "EditGameForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "ゲーム編集";
            this.Load += new System.EventHandler(this.EditGameForm_Load);
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
        private System.Windows.Forms.CheckBox chkIsVisible;
        private System.Windows.Forms.Label lblThumbnailPath;
        private System.Windows.Forms.TextBox txtThumbnailPath;
        private System.Windows.Forms.Button btnSelectThumbnail;
        private System.Windows.Forms.Label lblBackgroundPath;
        private System.Windows.Forms.TextBox txtBackgroundPath;
        private System.Windows.Forms.Label lblVersionManagement;
        private System.Windows.Forms.ComboBox cmbVersionList;
        private System.Windows.Forms.Button btnDeleteVersion;
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
        private System.Windows.Forms.Label lblVersionDescription;
        private System.Windows.Forms.TextBox txtVersionDescription;
        private System.Windows.Forms.Label lblVersionName;
        private TonePrism.Manager.Controls.SemverInputControl semverVersionName;
        private System.Windows.Forms.PictureBox picThumbnailPreview;
        private System.Windows.Forms.PictureBox picBackgroundPreview;
        private System.Windows.Forms.Button btnTestRun;
        private System.Windows.Forms.Label lblThumbnailHint;
        private System.Windows.Forms.Label lblBackgroundHint;
    }
}
