using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TonePrism.Manager
{
    public partial class ProcessingDialog : Form
    {
        private Action<IProgress<ProgressInfo>, CancellationToken> _worker;
        private CancellationTokenSource _cts;
        private Task _workerTask;
        private string _lastMessage = string.Empty; // (#299関連) lblMessage に「フェーズ + N%」を毎回再構成するため、直近メッセージを保持

        public ProcessingDialog(Action<IProgress<ProgressInfo>, CancellationToken> worker)
        {
            InitializeComponent();
            _worker = worker;
            _cts = new CancellationTokenSource();
            // (#245 PR5) 進捗はシェルのタスクバーボタン (緑バー) に集約するため、このモーダル自身は
            // タスクバーに別ボタンを出さない (二重ボタン回避)。窓内の pbProgress は従来どおり表示。
            this.ShowInTaskbar = false;
        }

        /// <summary>
        /// 進捗が定量化できない処理向け。Marquee（流れる）スタイルにする。
        /// </summary>
        // (#258 PR3) child control 委譲の runtime プロパティ (backing field 無し)。designer シリアライズ対象外
        // を明示し net10 WinForms の WFO1000 (error 化) を解消。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool MarqueeMode
        {
            get { return pbProgress.Style == ProgressBarStyle.Marquee; }
            set
            {
                pbProgress.Style = value ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
                if (value) pbProgress.MarqueeAnimationSpeed = 30;
            }
        }

        /// <summary>
        /// キャンセル可能かどうか。途中で中断できない処理（Directory.Move 等）では false に設定する。
        /// **注意 (M2)**: setter は UI thread でのみ呼ぶこと。worker thread から実行中に切り替える場合は
        /// 代わりに <see cref="DisableCancelFromWorker"/> を使う (Invoke で UI thread に marshal)。
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool AllowCancel
        {
            get { return btnCancel.Visible; }
            set { btnCancel.Visible = value; }
        }

        /// <summary>
        /// (#108 Phase 4 round 1 M2 fix) worker thread から「ここから先はキャンセル不可」に切り替える。
        /// 旧実装は AllowCancel = true 固定で「置換境界より後」では cancel 押下が無視される設計だったが、
        /// UI 上はボタンが見えてユーザに「キャンセル可能」と誤誘導する path があった。worker 内の置換境界
        /// entry でこの method を呼んで btnCancel を hide すれば UI と実装が整合する。Invoke で UI thread
        /// に marshal するので worker thread から安全に呼べる。
        /// </summary>
        public void DisableCancelFromWorker()
        {
            try
            {
                if (btnCancel.InvokeRequired)
                {
                    btnCancel.BeginInvoke(new Action(() => btnCancel.Visible = false));
                }
                else
                {
                    btnCancel.Visible = false;
                }
            }
            catch (Exception) { /* form 破棄済み等は握り潰す、UX cosmetic のため致命的でない */ }
        }

        private async void ProcessingDialog_Shown(object sender, EventArgs e)
        {
            var progress = new Progress<ProgressInfo>(ReportProgress);

            // (#245 PR5) タスクバー進捗を開始時に点灯 (Marquee=不定 / 定量=0%)。終了時は finally で必ず消す。
            UpdateTaskbarProgress(0);

            try
            {
                _workerTask = Task.Run(() => _worker(progress, _cts.Token));
                await _workerTask;
                this.DialogResult = DialogResult.OK;
            }
            catch (OperationCanceledException)
            {
                this.DialogResult = DialogResult.Cancel;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.DialogResult = DialogResult.Abort;
            }
            finally
            {
                ClearTaskbarProgress();
                this.Close();
            }
        }

        private void ReportProgress(ProgressInfo info)
        {
            if (pbProgress.InvokeRequired)
            {
                pbProgress.Invoke(new Action<ProgressInfo>(ReportProgress), info);
                return;
            }

            if (info.Percentage >= 0 && info.Percentage <= 100)
            {
                pbProgress.Value = info.Percentage;
            }

            if (!string.IsNullOrEmpty(info.Message))
            {
                _lastMessage = info.Message;
            }

            // (#299関連) 進捗バーに数字の % も併記する (定量進捗のときだけ。Marquee=不定のときは出さない)。
            // lblMessage は毎回「直近フェーズ + N%」で再構成する (古い % を strip する必要がない)。
            bool showPct = pbProgress.Style != ProgressBarStyle.Marquee && info.Percentage >= 0 && info.Percentage <= 100;
            // (#299 review round4 L-3) _lastMessage 未到達 (空) のとき先頭スペース付き "  0%" にならないよう結合を分ける。
            lblMessage.Text = showPct
                ? (string.IsNullOrEmpty(_lastMessage) ? info.Percentage + "%" : _lastMessage + "  " + info.Percentage + "%")
                : _lastMessage;

            if (!string.IsNullOrEmpty(info.Detail))
            {
                lblDetail.Text = info.Detail;
            }

            // (#245 PR5) 窓内バーと同じ値をシェルのタスクバー進捗にも反映する。
            UpdateTaskbarProgress(info.Percentage);
        }

        // (#245 PR5) タスクバー進捗 (シェルの TaskbarItemInfo) への中継。Marquee=不定として渡す。
        // シェル不在 (純 WinForms 起動経路など) では Instance==null で no-op。cosmetic なので失敗は握り潰す。
        private void UpdateTaskbarProgress(int percentage)
        {
            try
            {
                bool marquee = pbProgress.Style == ProgressBarStyle.Marquee;
                Shell.ShellWindow.Instance?.SetTaskbarProgress(percentage, marquee);
            }
            catch { /* cosmetic */ }
        }

        private static void ClearTaskbarProgress()
        {
            try { Shell.ShellWindow.Instance?.ClearTaskbarProgress(); }
            catch { /* cosmetic */ }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("処理を中止しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _cts.Cancel();
                lblMessage.Text = "キャンセル中...";
                btnCancel.Enabled = false;
            }
        }
    }


}
