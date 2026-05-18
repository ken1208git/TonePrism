namespace TonePrism.Manager.Controls
{
    partial class UpdateSectionPanel
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null) components.Dispose();
                // (#108 Phase 4 round 2 L8) _checkCts は連打時の中間 Cancel + Dispose で「最後の 1 個」
                // だけ leak していたため Form 廃棄時にも Dispose を hook。CancellationTokenSource は
                // 内部 WaitHandle を持つ IDisposable。
                if (_checkCts != null)
                {
                    try { _checkCts.Dispose(); } catch { }
                    _checkCts = null;
                }
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.grpVersions = new System.Windows.Forms.GroupBox();
            this.tableVersions = new System.Windows.Forms.TableLayoutPanel();
            this.lblBundleCaption = new System.Windows.Forms.Label();
            this.lblBundleVersion = new System.Windows.Forms.Label();
            this.lblManagerCaption = new System.Windows.Forms.Label();
            this.lblManagerVersion = new System.Windows.Forms.Label();
            this.lblLauncherCaption = new System.Windows.Forms.Label();
            this.lblLauncherVersion = new System.Windows.Forms.Label();
            this.lblUpdaterCaption = new System.Windows.Forms.Label();
            this.lblUpdaterVersion = new System.Windows.Forms.Label();
            this.lblDbSchemaCaption = new System.Windows.Forms.Label();
            this.lblDbSchemaVersion = new System.Windows.Forms.Label();

            this.grpLatest = new System.Windows.Forms.GroupBox();
            this.lblLatestCaption = new System.Windows.Forms.Label();
            this.lblLatestVersion = new System.Windows.Forms.Label();
            this.lblLatestDate = new System.Windows.Forms.Label();
            this.lblStatusMessage = new System.Windows.Forms.Label();
            this.lblPreviousResult = new System.Windows.Forms.Label();

            this.grpNotes = new System.Windows.Forms.GroupBox();
            this.webReleaseNotes = new System.Windows.Forms.WebBrowser();

            this.pnlButtons = new System.Windows.Forms.Panel();
            this.btnCheckNow = new System.Windows.Forms.Button();
            this.btnUpdateNow = new System.Windows.Forms.Button();
            this.btnSkip = new System.Windows.Forms.Button();
            this.btnOpenBrowser = new System.Windows.Forms.Button();

            this.grpVersions.SuspendLayout();
            this.tableVersions.SuspendLayout();
            this.grpLatest.SuspendLayout();
            this.grpNotes.SuspendLayout();
            this.pnlButtons.SuspendLayout();
            this.SuspendLayout();

            // grpVersions
            this.grpVersions.Text = "現在のバージョン";
            this.grpVersions.Location = new System.Drawing.Point(8, 8);
            this.grpVersions.Size = new System.Drawing.Size(360, 180);
            this.grpVersions.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.grpVersions.Controls.Add(this.tableVersions);

            // tableVersions
            this.tableVersions.ColumnCount = 2;
            this.tableVersions.RowCount = 5;
            this.tableVersions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableVersions.Padding = new System.Windows.Forms.Padding(4);
            this.tableVersions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 120));
            this.tableVersions.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            for (int i = 0; i < 5; i++) this.tableVersions.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
            this.tableVersions.Controls.Add(this.lblBundleCaption, 0, 0);
            this.tableVersions.Controls.Add(this.lblBundleVersion, 1, 0);
            this.tableVersions.Controls.Add(this.lblManagerCaption, 0, 1);
            this.tableVersions.Controls.Add(this.lblManagerVersion, 1, 1);
            this.tableVersions.Controls.Add(this.lblLauncherCaption, 0, 2);
            this.tableVersions.Controls.Add(this.lblLauncherVersion, 1, 2);
            this.tableVersions.Controls.Add(this.lblUpdaterCaption, 0, 3);
            this.tableVersions.Controls.Add(this.lblUpdaterVersion, 1, 3);
            this.tableVersions.Controls.Add(this.lblDbSchemaCaption, 0, 4);
            this.tableVersions.Controls.Add(this.lblDbSchemaVersion, 1, 4);

            this.lblBundleCaption.Text = "Bundle (全体):";
            this.lblBundleCaption.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblBundleCaption.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblBundleVersion.Text = "—";
            this.lblBundleVersion.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblBundleVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblManagerCaption.Text = "Manager:";
            this.lblManagerCaption.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblManagerCaption.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblManagerVersion.Text = "—";
            this.lblManagerVersion.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblManagerVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblLauncherCaption.Text = "Launcher:";
            this.lblLauncherCaption.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblLauncherCaption.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblLauncherVersion.Text = "—";
            this.lblLauncherVersion.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblLauncherVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblUpdaterCaption.Text = "Updater:";
            this.lblUpdaterCaption.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblUpdaterCaption.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblUpdaterVersion.Text = "—";
            this.lblUpdaterVersion.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblUpdaterVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            this.lblDbSchemaCaption.Text = "DB スキーマ:";
            this.lblDbSchemaCaption.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDbSchemaCaption.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblDbSchemaVersion.Text = "—";
            this.lblDbSchemaVersion.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDbSchemaVersion.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // grpLatest
            this.grpLatest.Text = "最新バージョン";
            this.grpLatest.Location = new System.Drawing.Point(376, 8);
            this.grpLatest.Size = new System.Drawing.Size(700, 180);
            this.grpLatest.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.lblLatestCaption.Text = "GitHub Releases の最新:";
            this.lblLatestCaption.Location = new System.Drawing.Point(12, 28);
            this.lblLatestCaption.Size = new System.Drawing.Size(180, 20);
            this.lblLatestCaption.AutoSize = true;

            this.lblLatestVersion.Text = "未確認";
            this.lblLatestVersion.Location = new System.Drawing.Point(200, 28);
            this.lblLatestVersion.AutoSize = true;
            this.lblLatestVersion.Font = new System.Drawing.Font("Meiryo UI", 10F, System.Drawing.FontStyle.Bold);

            this.lblLatestDate.Text = string.Empty;
            this.lblLatestDate.Location = new System.Drawing.Point(12, 52);
            this.lblLatestDate.AutoSize = true;
            this.lblLatestDate.ForeColor = System.Drawing.Color.Gray;

            this.lblStatusMessage.Text = string.Empty;
            this.lblStatusMessage.Location = new System.Drawing.Point(12, 80);
            this.lblStatusMessage.Size = new System.Drawing.Size(670, 50);
            this.lblStatusMessage.AutoEllipsis = true;
            this.lblStatusMessage.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.lblPreviousResult.Text = string.Empty;
            this.lblPreviousResult.Location = new System.Drawing.Point(12, 138);
            this.lblPreviousResult.Size = new System.Drawing.Size(670, 36);
            this.lblPreviousResult.Visible = false;
            this.lblPreviousResult.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.grpLatest.Controls.Add(this.lblLatestCaption);
            this.grpLatest.Controls.Add(this.lblLatestVersion);
            this.grpLatest.Controls.Add(this.lblLatestDate);
            this.grpLatest.Controls.Add(this.lblStatusMessage);
            this.grpLatest.Controls.Add(this.lblPreviousResult);

            // grpNotes
            this.grpNotes.Text = "リリースノート";
            this.grpNotes.Location = new System.Drawing.Point(8, 196);
            this.grpNotes.Size = new System.Drawing.Size(1068, 350);
            this.grpNotes.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.webReleaseNotes.Dock = System.Windows.Forms.DockStyle.Fill;
            this.webReleaseNotes.AllowNavigation = true;
            this.webReleaseNotes.IsWebBrowserContextMenuEnabled = false;
            this.webReleaseNotes.ScriptErrorsSuppressed = true;
            this.webReleaseNotes.WebBrowserShortcutsEnabled = false;
            this.webReleaseNotes.Navigating += new System.Windows.Forms.WebBrowserNavigatingEventHandler(this.webReleaseNotes_Navigating);
            this.grpNotes.Controls.Add(this.webReleaseNotes);

            // pnlButtons
            this.pnlButtons.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlButtons.Height = 48;
            this.pnlButtons.Padding = new System.Windows.Forms.Padding(8, 4, 8, 8);

            this.btnCheckNow.Text = "更新を確認";
            this.btnCheckNow.Location = new System.Drawing.Point(8, 8);
            this.btnCheckNow.Size = new System.Drawing.Size(120, 32);
            this.btnCheckNow.Click += new System.EventHandler(this.btnCheckNow_Click);

            this.btnUpdateNow.Text = "今すぐアップデート";
            this.btnUpdateNow.Location = new System.Drawing.Point(140, 8);
            this.btnUpdateNow.Size = new System.Drawing.Size(150, 32);
            this.btnUpdateNow.Enabled = false;
            this.btnUpdateNow.Click += new System.EventHandler(this.btnUpdateNow_Click);

            this.btnSkip.Text = "このバージョンをスキップ";
            this.btnSkip.Location = new System.Drawing.Point(300, 8);
            this.btnSkip.Size = new System.Drawing.Size(180, 32);
            this.btnSkip.Enabled = false;
            this.btnSkip.Click += new System.EventHandler(this.btnSkip_Click);

            this.btnOpenBrowser.Text = "ブラウザで詳細を見る";
            this.btnOpenBrowser.Location = new System.Drawing.Point(490, 8);
            this.btnOpenBrowser.Size = new System.Drawing.Size(160, 32);
            this.btnOpenBrowser.Click += new System.EventHandler(this.btnOpenBrowser_Click);

            this.pnlButtons.Controls.Add(this.btnCheckNow);
            this.pnlButtons.Controls.Add(this.btnUpdateNow);
            this.pnlButtons.Controls.Add(this.btnSkip);
            this.pnlButtons.Controls.Add(this.btnOpenBrowser);

            // UpdateSectionPanel
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.grpVersions);
            this.Controls.Add(this.grpLatest);
            this.Controls.Add(this.grpNotes);
            this.Controls.Add(this.pnlButtons);
            this.Name = "UpdateSectionPanel";
            this.Size = new System.Drawing.Size(1084, 594);

            this.grpVersions.ResumeLayout(false);
            this.tableVersions.ResumeLayout(false);
            this.grpLatest.ResumeLayout(false);
            this.grpNotes.ResumeLayout(false);
            this.pnlButtons.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.GroupBox grpVersions;
        private System.Windows.Forms.TableLayoutPanel tableVersions;
        private System.Windows.Forms.Label lblBundleCaption;
        private System.Windows.Forms.Label lblBundleVersion;
        private System.Windows.Forms.Label lblManagerCaption;
        private System.Windows.Forms.Label lblManagerVersion;
        private System.Windows.Forms.Label lblLauncherCaption;
        private System.Windows.Forms.Label lblLauncherVersion;
        private System.Windows.Forms.Label lblUpdaterCaption;
        private System.Windows.Forms.Label lblUpdaterVersion;
        private System.Windows.Forms.Label lblDbSchemaCaption;
        private System.Windows.Forms.Label lblDbSchemaVersion;

        private System.Windows.Forms.GroupBox grpLatest;
        private System.Windows.Forms.Label lblLatestCaption;
        private System.Windows.Forms.Label lblLatestVersion;
        private System.Windows.Forms.Label lblLatestDate;
        private System.Windows.Forms.Label lblStatusMessage;
        private System.Windows.Forms.Label lblPreviousResult;

        private System.Windows.Forms.GroupBox grpNotes;
        private System.Windows.Forms.WebBrowser webReleaseNotes;

        private System.Windows.Forms.Panel pnlButtons;
        private System.Windows.Forms.Button btnCheckNow;
        private System.Windows.Forms.Button btnUpdateNow;
        private System.Windows.Forms.Button btnSkip;
        private System.Windows.Forms.Button btnOpenBrowser;
    }
}
