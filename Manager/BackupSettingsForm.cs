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
            // (#179 round 7 H-1、round 8 M-2 で comment 整理) 旧実装はここに session conflict check が
            // なく、settings table への 3 件 INSERT OR REPLACE (`backup_destination_path` /
            // `backup_auto_interval_hours` / `backup_retention_count`) が他 PC の同 form と silent に
            // 上書き合戦する path だった。`btnSettings_Click` → `BackupSettingsForm.ShowDialog` 経路は
            // (a) SectionPanel 配下の 1 段目 fence 13 callsite (Game 4 + Store 4 + Settings 2 +
            // Backup 3) は単に form を開くだけで DB write を持たないため check 不要、(b) round 6 案 B
            // で追加された 2 段目 fence (4 Form の OK click) も BackupSettingsForm は対象外 — の
            // **両方からも漏れていた** drift。SPEC §3.8.2 が「対象 button 13 箇所」と enumerate しつつ
            // 本 callsite を accounting に含めていなかったため、reviewer / 将来の保守者から見ても
            // 「14 callsite 目」が grep で発見されない silent skip 路。Form 内 OK click の DB write
            // 直前で check、Cancel 時は **編集画面に戻る** semantic (= `DialogResult.OK` を設定せず
            // Form を閉じない、入力保持) で round 6 案 B / 2 段目 fence と pattern を揃える。
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
