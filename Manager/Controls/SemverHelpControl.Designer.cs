namespace GCTonePrism.Manager.Controls
{
    partial class SemverHelpControl
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

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this.btnToggle = new System.Windows.Forms.Button();
            this.pnlContent = new System.Windows.Forms.Panel();
            this.lblHelpText = new System.Windows.Forms.Label();

            this.pnlContent.SuspendLayout();
            this.SuspendLayout();

            // btnToggle
            this.btnToggle.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnToggle.FlatAppearance.BorderSize = 0;
            this.btnToggle.Location = new System.Drawing.Point(0, 0);
            this.btnToggle.Name = "btnToggle";
            this.btnToggle.Size = new System.Drawing.Size(540, 24);
            this.btnToggle.TabIndex = 0;
            this.btnToggle.Text = "▶ バージョン番号 (SemVer) とは?";
            this.btnToggle.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.btnToggle.UseVisualStyleBackColor = false;
            this.btnToggle.BackColor = System.Drawing.Color.FromArgb(245, 245, 250);
            this.btnToggle.ForeColor = System.Drawing.Color.FromArgb(60, 60, 100);
            this.btnToggle.Cursor = System.Windows.Forms.Cursors.Hand;

            // lblHelpText
            this.lblHelpText.AutoSize = false;
            this.lblHelpText.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblHelpText.Padding = new System.Windows.Forms.Padding(8, 4, 8, 4);
            this.lblHelpText.Name = "lblHelpText";
            this.lblHelpText.Text =
                "Major (大きな変更): ゲームのジャンル変更、操作方法の大幅変更など、" +
                "プレイヤーが「別ゲーム」と感じるレベルの変更。\r\n" +
                "  例) ステージ 1 つだけのゲーム → 5 ステージ + ストーリーモード追加。\r\n" +
                "\r\n" +
                "Minor (機能追加): 既存のゲームプレイは維持しつつ、新ステージ・新キャラ・新設定など" +
                "を追加した時。\r\n" +
                "  例) ステージ追加、新キャラ追加、設定項目追加。\r\n" +
                "\r\n" +
                "Patch (バグ修正・調整): バグの修正、誤字修正、当たり判定や難易度の小さな調整のみ。\r\n" +
                "  例) 当たり判定の修正、誤字修正、難易度バランス調整。\r\n" +
                "\r\n" +
                "迷ったら Patch を選んでください (= 小さい変更扱い、後で気が変わっても問題なし)。";
            this.lblHelpText.Font = new System.Drawing.Font("Meiryo UI", 9F);
            this.lblHelpText.ForeColor = System.Drawing.Color.FromArgb(40, 40, 60);

            // pnlContent
            this.pnlContent.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pnlContent.Location = new System.Drawing.Point(0, 24);
            this.pnlContent.Name = "pnlContent";
            this.pnlContent.Size = new System.Drawing.Size(540, 200);
            this.pnlContent.BackColor = System.Drawing.Color.FromArgb(252, 252, 255);
            this.pnlContent.Controls.Add(this.lblHelpText);

            // SemverHelpControl
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.btnToggle);
            this.Controls.Add(this.pnlContent);
            this.Name = "SemverHelpControl";
            this.Size = new System.Drawing.Size(540, 28);

            this.pnlContent.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Button btnToggle;
        private System.Windows.Forms.Panel pnlContent;
        private System.Windows.Forms.Label lblHelpText;
    }
}
