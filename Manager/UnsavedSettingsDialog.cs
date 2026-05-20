using System;
using System.Drawing;
using System.Windows.Forms;

namespace TonePrism.Manager
{
    /// <summary>
    /// (#201) 設定タブの未保存変更を解決する 3-button 確認 dialog。
    ///
    /// WinForms 標準 `MessageBox` は button label を「はい / いいえ / キャンセル」固定でしか出せず、
    /// 「保存 / 破棄」と明示できない。本 custom Form で button に直接「保存」「破棄」「キャンセル」を
    /// 表示する (= 既存 `ResetDatabaseConfirmForm` / `SessionConflictDialog` 等の custom dialog pattern と同様)。
    ///
    /// 戻り値は `ShowDialog` の `DialogResult`:
    /// - `DialogResult.Yes` = 保存して移動
    /// - `DialogResult.No` = 破棄して移動
    /// - `DialogResult.Cancel` = このタブに留まる (× ボタン / Esc も同じ)
    /// </summary>
    public sealed class UnsavedSettingsDialog : Form
    {
        public UnsavedSettingsDialog()
        {
            Text = "未保存の変更";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 140);
            Font = new Font("Meiryo UI", 9.5F);

            var icon = new PictureBox
            {
                Image = SystemIcons.Warning.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point(20, 24)
            };

            var message = new Label
            {
                Text = "設定に未保存の変更があります。\nこのタブから移動する前に保存しますか？",
                AutoSize = true,
                MaximumSize = new Size(350, 0),
                Location = new Point(64, 26)
            };

            // ボタン行 (右寄せ、保存 / 破棄 / キャンセル の順)
            var btnCancel = new Button
            {
                Text = "キャンセル",
                DialogResult = DialogResult.Cancel,
                Size = new Size(100, 32),
                Location = new Point(330, 95)
            };
            var btnDiscard = new Button
            {
                Text = "破棄",
                DialogResult = DialogResult.No,
                Size = new Size(95, 32),
                Location = new Point(228, 95)
            };
            var btnSave = new Button
            {
                Text = "保存",
                DialogResult = DialogResult.Yes,
                Size = new Size(95, 32),
                Location = new Point(126, 95)
            };

            Controls.Add(icon);
            Controls.Add(message);
            Controls.Add(btnSave);
            Controls.Add(btnDiscard);
            Controls.Add(btnCancel);

            AcceptButton = btnSave;   // Enter = 保存
            CancelButton = btnCancel; // Esc / × = キャンセル (留まる、= 安全側 default)
        }
    }
}
