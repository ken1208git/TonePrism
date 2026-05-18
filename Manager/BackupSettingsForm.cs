using System;
using System.Windows.Forms;
using GCTonePrism.Manager.Repositories;
using GCTonePrism.Manager.Services;

namespace GCTonePrism.Manager
{
    /// <summary>
    /// バックアップ機能の設定ダイアログ。
    /// 保存先パス、自動バックアップ間隔、世代数を設定する。
    /// </summary>
    public partial class BackupSettingsForm : Form
    {
        private readonly SettingsRepository _settingsRepo;
        private readonly BackupService _backupService;

        public BackupSettingsForm(SettingsRepository settingsRepo, BackupService backupService)
        {
            InitializeComponent();
            _settingsRepo = settingsRepo;
            _backupService = backupService;
            LoadSettings();
        }

        private void LoadSettings()
        {
            txtDestPath.Text = _settingsRepo.GetString("backup_destination_path", "");

            int interval = _settingsRepo.GetInt32("backup_auto_interval_hours", 24);
            if (interval < numInterval.Minimum) interval = (int)numInterval.Minimum;
            if (interval > numInterval.Maximum) interval = (int)numInterval.Maximum;
            numInterval.Value = interval;

            int retention = _settingsRepo.GetInt32("backup_retention_count", 30);
            if (retention < numRetention.Minimum) retention = (int)numRetention.Minimum;
            if (retention > numRetention.Maximum) retention = (int)numRetention.Maximum;
            numRetention.Value = retention;
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "バックアップ保存先フォルダを選択してください";
                if (!string.IsNullOrEmpty(txtDestPath.Text))
                {
                    dialog.SelectedPath = txtDestPath.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtDestPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            // (#179 round 7 H-1) 旧実装はここに session conflict check がなく、settings table への
            // 3 件 INSERT OR REPLACE (`backup_destination_path` / `backup_auto_interval_hours` /
            // `backup_retention_count`) が他 PC の同 form と silent に上書き合戦する path だった。
            // SectionPanel の他 12 callsite は SessionConflictHelper.CheckBeforeWrite で gate 済だが
            // `btnSettings` だけ漏れていた (= PR description / SPEC §3.8.2 が「13 callsite」と言いつつ
            // 14 callsite 目が抜けていた drift)。Form 内 OK click の DB write 直前で check、Cancel 時は
            // **編集画面に戻る** semantic (= `DialogResult.OK` を設定せず Form を閉じない、入力保持)。
            if (SessionConflictHelper.CheckBeforeWrite(this, "バックアップ設定変更") == DialogResult.Cancel)
            {
                return;
            }

            try
            {
                _settingsRepo.SetString("backup_destination_path", txtDestPath.Text.Trim());
                _settingsRepo.SetInt32("backup_auto_interval_hours", (int)numInterval.Value);
                _settingsRepo.SetInt32("backup_retention_count", (int)numRetention.Value);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました: {ex.Message}", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
