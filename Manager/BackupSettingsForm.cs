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
