using System;
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

        public ProcessingDialog(Action<IProgress<ProgressInfo>, CancellationToken> worker)
        {
            InitializeComponent();
            _worker = worker;
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 進捗が定量化できない処理向け。Marquee（流れる）スタイルにする。
        /// </summary>
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
                lblMessage.Text = info.Message;
            }

            if (!string.IsNullOrEmpty(info.Detail))
            {
                lblDetail.Text = info.Detail;
            }
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
