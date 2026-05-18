namespace TonePrism.Manager.Controls
{
    partial class SemverInputControl
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
            this.lblV = new System.Windows.Forms.Label();
            this.numMajor = new System.Windows.Forms.NumericUpDown();
            this.lblDot1 = new System.Windows.Forms.Label();
            this.numMinor = new System.Windows.Forms.NumericUpDown();
            this.lblDot2 = new System.Windows.Forms.Label();
            this.numPatch = new System.Windows.Forms.NumericUpDown();
            this.lblDash = new System.Windows.Forms.Label();
            this.txtSuffix = new System.Windows.Forms.TextBox();
            ((System.ComponentModel.ISupportInitialize)(this.numMajor)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinor)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPatch)).BeginInit();
            this.SuspendLayout();

            // lblV ("v" 表示)
            this.lblV.AutoSize = true;
            this.lblV.Location = new System.Drawing.Point(0, 5);
            this.lblV.Name = "lblV";
            this.lblV.Size = new System.Drawing.Size(13, 13);
            this.lblV.Text = "v";
            this.lblV.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);

            // numMajor
            // (#158 round 7 M-3 + round 8 senior Low #5) Maximum / Minimum はここでは Designer surface
            // 用 (= デザイナで開いた時の見た目)。実行時は SemverInputControl.cs ctor で MaxMajor /
            // MaxMinor / MaxPatch / MinComponent const に上書きされるため、値を変えたい場合は本ファイル
            // ではなく SemverInputControl.cs の const を変えること (= 一方向 SoT、SemverInputControl.cs
            // 24-32 行のコメント参照)。
            this.numMajor.Location = new System.Drawing.Point(15, 2);
            this.numMajor.Maximum = new decimal(new int[] { 99, 0, 0, 0 });
            this.numMajor.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numMajor.Name = "numMajor";
            this.numMajor.Size = new System.Drawing.Size(45, 22);
            this.numMajor.TabIndex = 0;
            this.numMajor.Value = new decimal(new int[] { 1, 0, 0, 0 });

            // lblDot1 (".")
            this.lblDot1.AutoSize = true;
            this.lblDot1.Location = new System.Drawing.Point(62, 5);
            this.lblDot1.Name = "lblDot1";
            this.lblDot1.Size = new System.Drawing.Size(10, 13);
            this.lblDot1.Text = ".";
            this.lblDot1.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);

            // numMinor
            this.numMinor.Location = new System.Drawing.Point(75, 2);
            this.numMinor.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            this.numMinor.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numMinor.Name = "numMinor";
            this.numMinor.Size = new System.Drawing.Size(55, 22);
            this.numMinor.TabIndex = 1;

            // lblDot2 (".")
            this.lblDot2.AutoSize = true;
            this.lblDot2.Location = new System.Drawing.Point(132, 5);
            this.lblDot2.Name = "lblDot2";
            this.lblDot2.Size = new System.Drawing.Size(10, 13);
            this.lblDot2.Text = ".";
            this.lblDot2.Font = new System.Drawing.Font("Meiryo UI", 9F, System.Drawing.FontStyle.Bold);

            // numPatch
            this.numPatch.Location = new System.Drawing.Point(145, 2);
            this.numPatch.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            this.numPatch.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
            this.numPatch.Name = "numPatch";
            this.numPatch.Size = new System.Drawing.Size(55, 22);
            this.numPatch.TabIndex = 2;

            // lblDash ("-")
            this.lblDash.AutoSize = true;
            this.lblDash.Location = new System.Drawing.Point(202, 5);
            this.lblDash.Name = "lblDash";
            this.lblDash.Size = new System.Drawing.Size(11, 13);
            this.lblDash.Text = "-";
            this.lblDash.ForeColor = System.Drawing.Color.Gray;

            // txtSuffix
            // (#158 round 6 L-2) MaxLength=32: SemVer 2.0.0 自体は suffix 長制限なしだが本 project の
            // 運用想定 (#133 ガイドラインの「rc1 / beta.2 程度」) に合わせた reasonable 上限。長文
            // suffix は folder 名肥大化 / UI 視認性低下を招くので構造的排除に揃える。
            this.txtSuffix.Location = new System.Drawing.Point(215, 2);
            this.txtSuffix.MaxLength = 32;
            this.txtSuffix.Name = "txtSuffix";
            this.txtSuffix.Size = new System.Drawing.Size(80, 22);
            this.txtSuffix.TabIndex = 3;

            // SemverInputControl
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.lblV);
            this.Controls.Add(this.numMajor);
            this.Controls.Add(this.lblDot1);
            this.Controls.Add(this.numMinor);
            this.Controls.Add(this.lblDot2);
            this.Controls.Add(this.numPatch);
            this.Controls.Add(this.lblDash);
            this.Controls.Add(this.txtSuffix);
            this.Name = "SemverInputControl";
            this.Size = new System.Drawing.Size(300, 28);

            ((System.ComponentModel.ISupportInitialize)(this.numMajor)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinor)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPatch)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblV;
        private System.Windows.Forms.NumericUpDown numMajor;
        private System.Windows.Forms.Label lblDot1;
        private System.Windows.Forms.NumericUpDown numMinor;
        private System.Windows.Forms.Label lblDot2;
        private System.Windows.Forms.NumericUpDown numPatch;
        private System.Windows.Forms.Label lblDash;
        private System.Windows.Forms.TextBox txtSuffix;
    }
}
