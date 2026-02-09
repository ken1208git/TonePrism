using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GCTonePrism.Manager
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
