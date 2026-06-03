using System;
using System.Drawing;
using System.Windows.Forms;

namespace TonePrism.Manager.Controls
{
    /// <summary>
    /// (#299) MainForm 下部に常設するバックアップ進捗ストリップ。非ブロッキングのバックアップ実行中に
    /// 進捗バー + 現在ファイル + 「中止」を出し（操作はそのまま続けられる）、未バックアップ（失敗/中断）の
    /// ときは警告 + 「今すぐバックアップ」で 1 クリック復旧させる。表示中だけ Visible=true。
    ///
    /// SessionBackupCoordinator の <c>ProgressReporter</c> / <c>BackupRunningChanged</c> コールバックを
    /// MainForm が UI スレッドへ marshal して、本コントロールの公開メソッド（ShowRunning / UpdateProgress /
    /// ShowUnhealthy / HideStrip）を呼ぶ。本コントロールは UI 描画のみで、バックアップ logic は持たない。
    /// </summary>
    public sealed class BackupProgressStrip : UserControl
    {
        private readonly ProgressBar _bar;
        private readonly Label _label;
        private readonly Button _cancelBtn;
        private readonly Button _recaptureBtn;

        /// <summary>「中止」押下 = 進行中バックアップのキャンセル（MainForm が CancelCurrentBackup へ）。</summary>
        public event EventHandler CancelRequested;
        /// <summary>「今すぐバックアップ」押下 = 未バックアップからの再取得（MainForm が RunAfterOperation(asset) へ）。</summary>
        public event EventHandler RecaptureRequested;

        public BackupProgressStrip()
        {
            Dock = DockStyle.Bottom;
            Height = 28;
            Visible = false;
            BackColor = SystemColors.Control;
            Padding = new Padding(6, 3, 6, 3);

            // Dock 順: Fill ラベルは最後に docking されるよう **最初に Add**（= 最背面 z-order）。
            // 右ボタン群は後に Add した方が外側（右端）になる → recapture, cancel の順で Add（cancel が最右）。
            _label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Padding = new Padding(8, 0, 8, 0)
            };
            _bar = new ProgressBar
            {
                Dock = DockStyle.Left,
                Width = 180,
                Style = ProgressBarStyle.Continuous,
                Minimum = 0,
                Maximum = 100
            };
            _recaptureBtn = new Button { Dock = DockStyle.Right, Text = "今すぐバックアップ", Width = 130, FlatStyle = FlatStyle.System, Visible = false };
            _cancelBtn = new Button { Dock = DockStyle.Right, Text = "中止", Width = 64, FlatStyle = FlatStyle.System };

            _cancelBtn.Click += (s, e) => CancelRequested?.Invoke(this, EventArgs.Empty);
            _recaptureBtn.Click += (s, e) => RecaptureRequested?.Invoke(this, EventArgs.Empty);

            Controls.Add(_label);        // 最背面 = Fill が中央の残りを取る
            Controls.Add(_bar);          // 左
            Controls.Add(_recaptureBtn); // 右（内側）
            Controls.Add(_cancelBtn);    // 右（最外＝右端）
        }

        /// <summary>実行中表示: 進捗バー + 現在ファイル + 中止。UI スレッドで呼ぶこと。</summary>
        public void ShowRunning()
        {
            _bar.Visible = true;
            _cancelBtn.Visible = true;
            _recaptureBtn.Visible = false;
            _label.ForeColor = SystemColors.ControlText;
            Visible = true;
        }

        /// <summary>進捗更新。percent 0-100、text = 現在のフェーズ/ファイル。UI スレッドで呼ぶこと。</summary>
        public void UpdateProgress(int percent, string text)
        {
            int p = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
            try { _bar.Value = p; } catch { /* Maximum 変更レース等は無害 */ }
            if (!string.IsNullOrEmpty(text)) _label.Text = text;
        }

        /// <summary>未バックアップ警告 + 「今すぐバックアップ」(1 クリック復旧) を表示。UI スレッドで呼ぶこと。</summary>
        public void ShowUnhealthy(string message)
        {
            _bar.Visible = false;
            _cancelBtn.Visible = false;
            _recaptureBtn.Visible = true;
            _label.ForeColor = Color.DarkOrange;
            _label.Text = message;
            Visible = true;
        }

        /// <summary>非表示（idle かつ健全）。UI スレッドで呼ぶこと。</summary>
        public void HideStrip()
        {
            Visible = false;
        }
    }
}
