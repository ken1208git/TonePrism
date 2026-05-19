using System;
using System.Reflection;
using System.Windows.Forms;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Controls
{
    public partial class SettingsSectionPanel : UserControl
    {
        private DatabaseManager _dbManager;

        public event Action DatabaseReset;

        // (#170 followup round 1) Text 系 control の Leave event は focus 移動ごとに発火するため、
        // 値が前回 save 時から変更されていない場合は save を skip する。これらの field は
        // 「最後に DB に書込んだ値」を tracking する。
        private string _lastSavedLogDest = "";
        private string _lastSavedBackupDest = "";
        // (#170 followup round 1) 単位 ComboBox の SelectedIndexChanged は値の rollback で
        // 再帰発火するため、現在の選択を tracking して「実際の unit change か revert か」を区別する。
        private string _prevIntervalUnit = "時間";

        public SettingsSectionPanel()
        {
            InitializeComponent();
        }

        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            UpdateVersionInfo();
            LoadLogSettings();
            LoadBackupSettings();
        }

        // ----- ログ section -----

        /// <summary>
        /// (#170 followup round 1) ログ section の初期化。保存先 path + 保存日数 を load。
        /// event hook は LoadLogSettings 完了後に attach (= 起動時 SetValue で spurious 発火回避)。
        /// </summary>
        private void LoadLogSettings()
        {
            if (_dbManager == null) return;
            // hook 一時 detach
            numLogRetention.ValueChanged -= NumLogRetention_ValueChanged;
            txtLogDest.Leave -= TxtLogDest_Leave;
            try
            {
                var repo = _dbManager.SettingsRepository;
                _lastSavedLogDest = repo.GetString(SettingsKeys.LogDestinationPath, "");
                txtLogDest.Text = _lastSavedLogDest;

                int days = repo.GetInt32(SettingsKeys.LogRetentionDays, SettingsKeys.DefaultLogRetentionDays);
                if (days < numLogRetention.Minimum) days = (int)numLogRetention.Minimum;
                if (days > numLogRetention.Maximum) days = (int)numLogRetention.Maximum;
                numLogRetention.Value = days;
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] LoadLogSettings 読込失敗: " + ex.Message);
            }
            numLogRetention.ValueChanged += NumLogRetention_ValueChanged;
            txtLogDest.Leave += TxtLogDest_Leave;
        }

        private void NumLogRetention_ValueChanged(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            int newValue = (int)numLogRetention.Value;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ログ保存日数変更") == DialogResult.Cancel)
            {
                // rollback
                numLogRetention.ValueChanged -= NumLogRetention_ValueChanged;
                try
                {
                    int previous = _dbManager.SettingsRepository.GetInt32(
                        SettingsKeys.LogRetentionDays, SettingsKeys.DefaultLogRetentionDays);
                    if (previous < numLogRetention.Minimum) previous = (int)numLogRetention.Minimum;
                    if (previous > numLogRetention.Maximum) previous = (int)numLogRetention.Maximum;
                    numLogRetention.Value = previous;
                }
                catch { }
                numLogRetention.ValueChanged += NumLogRetention_ValueChanged;
                return;
            }
            try
            {
                _dbManager.SettingsRepository.SetInt32(SettingsKeys.LogRetentionDays, newValue);
                Logger.Info("[SettingsSectionPanel] ログ保存日数を " + newValue + " 日に変更 (次回起動時反映)");
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] LogRetentionDays 書込失敗: " + ex.Message);
                MessageBox.Show("ログ保存日数の保存に失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnLogBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "ログ保存先フォルダを選択してください";
                if (!string.IsNullOrEmpty(txtLogDest.Text))
                {
                    dialog.SelectedPath = txtLogDest.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtLogDest.Text = dialog.SelectedPath;
                    SaveLogDestIfChanged();
                }
            }
        }

        private void TxtLogDest_Leave(object sender, EventArgs e)
        {
            SaveLogDestIfChanged();
        }

        private void SaveLogDestIfChanged()
        {
            if (_dbManager == null) return;
            string newValue = (txtLogDest.Text ?? string.Empty).Trim();
            if (newValue == _lastSavedLogDest) return;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ログ保存先変更") == DialogResult.Cancel)
            {
                // rollback
                txtLogDest.Leave -= TxtLogDest_Leave;
                txtLogDest.Text = _lastSavedLogDest;
                txtLogDest.Leave += TxtLogDest_Leave;
                return;
            }
            try
            {
                _dbManager.SettingsRepository.SetString(SettingsKeys.LogDestinationPath, newValue);
                _lastSavedLogDest = newValue;
                Logger.Info("[SettingsSectionPanel] ログ保存先を変更 (次回起動時反映): " + newValue);
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] LogDestinationPath 書込失敗: " + ex.Message);
                MessageBox.Show("ログ保存先の保存に失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ----- バックアップ section -----

        /// <summary>
        /// (#170 followup) バックアップ section の初期化 + per-control event hook attach。
        /// 旧 BackupSettingsForm.LoadSettings の置換、3 値 + 単位 ComboBox の 4 制御を load。
        /// </summary>
        private void LoadBackupSettings()
        {
            if (_dbManager == null) return;
            // hook 一時 detach
            txtBackupDest.Leave -= TxtBackupDest_Leave;
            chkBackupAutoEnabled.CheckedChanged -= ChkBackupAutoEnabled_CheckedChanged;
            numBackupInterval.ValueChanged -= NumBackupInterval_ValueChanged;
            cmbBackupIntervalUnit.SelectedIndexChanged -= CmbBackupIntervalUnit_SelectedIndexChanged;
            numBackupRetention.ValueChanged -= NumBackupRetention_ValueChanged;
            try
            {
                var repo = _dbManager.SettingsRepository;
                _lastSavedBackupDest = repo.GetString("backup_destination_path", "");
                txtBackupDest.Text = _lastSavedBackupDest;

                // (#170 followup round 2) 自動バックアップ有効/無効 checkbox の load
                string enabledStr = repo.GetString(SettingsKeys.BackupAutoEnabled, "true");
                chkBackupAutoEnabled.Checked = !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);

                int hours = repo.GetInt32("backup_auto_interval_hours", 24);
                string unit = repo.GetString(SettingsKeys.BackupAutoIntervalUnit, SettingsKeys.BackupAutoIntervalUnitHours);
                // 単位 ComboBox に display unit を設定 (= 「時間」or「日」)
                string displayUnit = unit == SettingsKeys.BackupAutoIntervalUnitDays ? "日" : "時間";
                cmbBackupIntervalUnit.SelectedItem = displayUnit;
                if (cmbBackupIntervalUnit.SelectedIndex < 0) cmbBackupIntervalUnit.SelectedIndex = 0;
                _prevIntervalUnit = (string)cmbBackupIntervalUnit.SelectedItem;
                // 単位に応じて Max を変える + displayValue を hours から換算
                ApplyIntervalUnitBounds(_prevIntervalUnit);
                int factor = _prevIntervalUnit == "日" ? 24 : 1;
                int displayValue = Math.Max(1, hours / factor);
                if (displayValue > numBackupInterval.Maximum) displayValue = (int)numBackupInterval.Maximum;
                numBackupInterval.Value = displayValue;

                int retention = repo.GetInt32("backup_retention_count", 30);
                if (retention < numBackupRetention.Minimum) retention = (int)numBackupRetention.Minimum;
                if (retention > numBackupRetention.Maximum) retention = (int)numBackupRetention.Maximum;
                numBackupRetention.Value = retention;

                // checkbox に従って interval section の enable/disable
                ApplyAutoBackupEnabledUi(chkBackupAutoEnabled.Checked);
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] LoadBackupSettings 読込失敗: " + ex.Message);
            }
            txtBackupDest.Leave += TxtBackupDest_Leave;
            chkBackupAutoEnabled.CheckedChanged += ChkBackupAutoEnabled_CheckedChanged;
            numBackupInterval.ValueChanged += NumBackupInterval_ValueChanged;
            cmbBackupIntervalUnit.SelectedIndexChanged += CmbBackupIntervalUnit_SelectedIndexChanged;
            numBackupRetention.ValueChanged += NumBackupRetention_ValueChanged;
        }

        /// <summary>
        /// (#170 followup round 2) 自動バックアップ checkbox の状態に応じて interval section の
        /// control を enable/disable する。OFF 時は user 視点で「設定しても無効化されている」のが明確になる。
        /// 保存先 / 保持世代数は手動バックアップでも使うため対象外。
        /// </summary>
        private void ApplyAutoBackupEnabledUi(bool enabled)
        {
            lblBackupInterval.Enabled = enabled;
            numBackupInterval.Enabled = enabled;
            cmbBackupIntervalUnit.Enabled = enabled;
            lblBackupIntervalUnit.Enabled = enabled;
        }

        private void ChkBackupAutoEnabled_CheckedChanged(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            bool newValue = chkBackupAutoEnabled.Checked;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "自動バックアップ有効化変更") == DialogResult.Cancel)
            {
                // rollback (= event 再帰回避のため hook 一時 detach)
                chkBackupAutoEnabled.CheckedChanged -= ChkBackupAutoEnabled_CheckedChanged;
                chkBackupAutoEnabled.Checked = !newValue;
                chkBackupAutoEnabled.CheckedChanged += ChkBackupAutoEnabled_CheckedChanged;
                return;
            }
            try
            {
                _dbManager.SettingsRepository.SetString(SettingsKeys.BackupAutoEnabled, newValue ? "true" : "false");
                ApplyAutoBackupEnabledUi(newValue);
                Logger.Info("[SettingsSectionPanel] 自動バックアップを " + (newValue ? "有効" : "無効") + " に変更");
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] BackupAutoEnabled 書込失敗: " + ex.Message);
                MessageBox.Show("自動バックアップ設定の保存に失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 単位 (「時間」/「日」) に応じて numBackupInterval の Maximum を切替える。
        /// 「時間」mode: 1-720 (= 30 日相当)。「日」mode: 1-30。
        /// </summary>
        private void ApplyIntervalUnitBounds(string unit)
        {
            int max = unit == "日" ? 30 : 720;
            numBackupInterval.Maximum = max;
            // current Value が新 Max を超えていたら clamp
            if (numBackupInterval.Value > max) numBackupInterval.Value = max;
        }

        private void btnBackupBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "バックアップ保存先フォルダを選択してください";
                if (!string.IsNullOrEmpty(txtBackupDest.Text))
                {
                    dialog.SelectedPath = txtBackupDest.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtBackupDest.Text = dialog.SelectedPath;
                    SaveBackupDestIfChanged();
                }
            }
        }

        private void TxtBackupDest_Leave(object sender, EventArgs e)
        {
            SaveBackupDestIfChanged();
        }

        private void SaveBackupDestIfChanged()
        {
            if (_dbManager == null) return;
            string newValue = (txtBackupDest.Text ?? string.Empty).Trim();
            if (newValue == _lastSavedBackupDest) return;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ保存先変更") == DialogResult.Cancel)
            {
                txtBackupDest.Leave -= TxtBackupDest_Leave;
                txtBackupDest.Text = _lastSavedBackupDest;
                txtBackupDest.Leave += TxtBackupDest_Leave;
                return;
            }
            try
            {
                _dbManager.SettingsRepository.SetString("backup_destination_path", newValue);
                _lastSavedBackupDest = newValue;
                Logger.Info("[SettingsSectionPanel] バックアップ保存先を変更: " + newValue);
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] backup_destination_path 書込失敗: " + ex.Message);
                MessageBox.Show("バックアップ保存先の保存に失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void NumBackupInterval_ValueChanged(object sender, EventArgs e)
        {
            SaveBackupIntervalWithGuard();
        }

        private void CmbBackupIntervalUnit_SelectedIndexChanged(object sender, EventArgs e)
        {
            string newUnit = cmbBackupIntervalUnit.SelectedItem as string ?? "時間";
            if (newUnit == _prevIntervalUnit) return;
            if (_dbManager == null)
            {
                _prevIntervalUnit = newUnit;
                return;
            }
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ間隔単位変更") == DialogResult.Cancel)
            {
                // revert ComboBox (= event 再帰回避のため hook 一時 detach)
                cmbBackupIntervalUnit.SelectedIndexChanged -= CmbBackupIntervalUnit_SelectedIndexChanged;
                cmbBackupIntervalUnit.SelectedItem = _prevIntervalUnit;
                cmbBackupIntervalUnit.SelectedIndexChanged += CmbBackupIntervalUnit_SelectedIndexChanged;
                return;
            }
            // 換算: 現在 displayed 値 × 旧 factor = effective hours → 新 factor で割って新 display
            int oldFactor = _prevIntervalUnit == "日" ? 24 : 1;
            int newFactor = newUnit == "日" ? 24 : 1;
            int effectiveHours = (int)numBackupInterval.Value * oldFactor;
            // bounds 更新 (= 単位による Max 変更) — ValueChanged 発火を suppress するため event detach
            numBackupInterval.ValueChanged -= NumBackupInterval_ValueChanged;
            ApplyIntervalUnitBounds(newUnit);
            int newDisplay = Math.Max(1, effectiveHours / newFactor);
            if (newDisplay > numBackupInterval.Maximum) newDisplay = (int)numBackupInterval.Maximum;
            numBackupInterval.Value = newDisplay;
            numBackupInterval.ValueChanged += NumBackupInterval_ValueChanged;
            _prevIntervalUnit = newUnit;
            // 単位 + 換算後 hours を一括 save
            SaveBackupIntervalDirect();
        }

        private void SaveBackupIntervalWithGuard()
        {
            if (_dbManager == null) return;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ間隔変更") == DialogResult.Cancel)
            {
                // rollback to saved value
                numBackupInterval.ValueChanged -= NumBackupInterval_ValueChanged;
                try
                {
                    int hours = _dbManager.SettingsRepository.GetInt32("backup_auto_interval_hours", 24);
                    int factor = _prevIntervalUnit == "日" ? 24 : 1;
                    int displayValue = Math.Max(1, hours / factor);
                    if (displayValue > numBackupInterval.Maximum) displayValue = (int)numBackupInterval.Maximum;
                    numBackupInterval.Value = displayValue;
                }
                catch { }
                numBackupInterval.ValueChanged += NumBackupInterval_ValueChanged;
                return;
            }
            SaveBackupIntervalDirect();
        }

        private void SaveBackupIntervalDirect()
        {
            if (_dbManager == null) return;
            try
            {
                int factor = _prevIntervalUnit == "日" ? 24 : 1;
                int hours = (int)numBackupInterval.Value * factor;
                _dbManager.SettingsRepository.SetInt32("backup_auto_interval_hours", hours);
                string unitKey = _prevIntervalUnit == "日"
                    ? SettingsKeys.BackupAutoIntervalUnitDays
                    : SettingsKeys.BackupAutoIntervalUnitHours;
                _dbManager.SettingsRepository.SetString(SettingsKeys.BackupAutoIntervalUnit, unitKey);
                Logger.Info("[SettingsSectionPanel] バックアップ間隔を変更: " + (int)numBackupInterval.Value + " " + _prevIntervalUnit + " (= " + hours + " 時間)");
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] backup_auto_interval_hours 書込失敗: " + ex.Message);
                MessageBox.Show("バックアップ間隔の保存に失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void NumBackupRetention_ValueChanged(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            int newValue = (int)numBackupRetention.Value;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ世代数変更") == DialogResult.Cancel)
            {
                numBackupRetention.ValueChanged -= NumBackupRetention_ValueChanged;
                try
                {
                    int previous = _dbManager.SettingsRepository.GetInt32("backup_retention_count", 30);
                    if (previous < numBackupRetention.Minimum) previous = (int)numBackupRetention.Minimum;
                    if (previous > numBackupRetention.Maximum) previous = (int)numBackupRetention.Maximum;
                    numBackupRetention.Value = previous;
                }
                catch { }
                numBackupRetention.ValueChanged += NumBackupRetention_ValueChanged;
                return;
            }
            try
            {
                _dbManager.SettingsRepository.SetInt32("backup_retention_count", newValue);
                Logger.Info("[SettingsSectionPanel] バックアップ世代数を " + newValue + " 個に変更");
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] backup_retention_count 書込失敗: " + ex.Message);
                MessageBox.Show("バックアップ世代数の保存に失敗しました: " + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ----- バージョン情報 + DB リセット -----

        public void UpdateVersionInfo()
        {
            if (_dbManager == null) return;

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                AssemblyName assemblyName = assembly.GetName();
                Version version = assemblyName.Version;

                string productName = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "TonePrism 管理ソフト";

                string versionStr = $"{version.Major}.{version.Minor}.{version.Build}";
                if (version.Revision > 0)
                    versionStr += $".{version.Revision}";

                int targetVersion = _dbManager.GetTargetDatabaseVersion();
                int actualVersion = _dbManager.GetActualDatabaseVersion();

                // AssemblyCopyright を SoT として Reflection 取得 (= AssemblyInfo.cs:13 が単一 SoT、
                // UI 側に literal を直書きしないので drift しない)。
                // 折返しは WinForms の word-wrap に委任 — `MaximumSize.Width` を grpInfo 幅基準で
                // 設定して `AutoSize=true` と組み合わせると、Label が幅で wrap + 高さ自動拡張する。
                // AssemblyInfo の文字列内容に対する coupling を持たないため、将来 holder 文字列を
                // 改変しても表示が壊れない (PR #194 round 2 review M-2 対応)。
                string copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
                lblVersionInfo.MaximumSize = new System.Drawing.Size(grpInfo.ClientSize.Width - 40, 0);

                lblVersionInfo.Text =
                    $"製品名: {productName}\n" +
                    $"バージョン: {versionStr}\n" +
                    $"データベース構造: v{actualVersion} (ターゲット: v{targetVersion})\n" +
                    "\n" +
                    $"{copyright}\n" +
                    "ライセンス: MIT License";
            }
            catch (Exception ex)
            {
                // AGENTS.md Cross-component Standards: 新規 catch path は Logger 経由で WARN/ERROR を出力する。
                // 本 catch は version + copyright 両方の取得失敗を吸収するため、debug 容易化のため例外詳細を残す
                // (round 2 review L-4 対応)。Logger 自体の例外は Logger 内部で握り潰される (再帰ハング回避)。
                Logger.Warn("[SettingsSectionPanel] バージョン情報の取得に失敗: " + ex.Message);
                lblVersionInfo.Text = "バージョン情報の取得に失敗しました。";
            }
        }

        private void btnResetDatabase_Click(object sender, EventArgs e)
        {
            // (round 5 M-1) 最 destructive (DB 全削除 + 再構築) なので **2 段階 check**:
            //   (1) ConfirmForm 開く前 = user 親切性 (= 他 PC 起動中なら confirm 開かず早期 abort)
            //   (2) ConfirmForm OK 後 + ProcessingDialog 起動前 = race fence (= confirm 読んでる間に
            //       他 PC が起動した case を catch)。confirm を user が長時間読む可能性があるので
            //       race window が無視できない、本 callsite のみ 2 段階。
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "データベース初期化") == DialogResult.Cancel) return;
            using (var confirmForm = new ResetDatabaseConfirmForm())
            {
                if (confirmForm.ShowDialog() != DialogResult.Yes) return;
            }
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "データベース初期化") == DialogResult.Cancel) return;

            // ResetDatabase は DBファイル削除 + games フォルダ再構築 + テーブル再作成 +
            // マイグレーション再実行を行う。共有フォルダ越しでは時間がかかるので進捗バー表示。
            // 戻り値は退避フォルダ物理削除の Result。Success=false なら DB / games は再構築済みだが
            // 退避フォルダだけ残る状態なので、再試行 UI で対処する (#122 Group C)。
            // 真に失敗した場合は例外が ProcessingDialog 内でハンドリングされ DialogResult.Abort になる。
            Exception caught = null;
            FolderDeletionService.Result resetResult = null;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                try
                {
                    progress?.Report(new ProgressInfo(-1, "データベースをリセット中...", "ファイル削除と再作成を実行しています"));
                    resetResult = _dbManager.ResetDatabase();
                }
                catch (Exception ex)
                {
                    caught = ex;
                    throw;
                }
            })
            {
                Text = "データベースリセット中",
                MarqueeMode = true,
                AllowCancel = false
            })
            {
                var dr = dialog.ShowDialog(this);
                if (dr != DialogResult.OK)
                {
                    return;
                }
            }

            // ここまで来た = DB / games は再構築済み。UI リフレッシュは結果に関わらず実行する
            // (Codex P2 #121: 警告を例外で表現すると ProcessingDialog で Abort 扱いされて
            //  リフレッシュフックがスキップされ、UI が古いまま「失敗」と誤報告されていたため)
            UpdateVersionInfo();
            DatabaseReset?.Invoke();

            // 退避フォルダ削除に失敗した場合は再試行 UI を出す (#122)
            // ユーザーが Launcher を閉じてから「再試行」を押せばロックが解放されて削除成功する想定
            while (resetResult != null && !resetResult.Success)
            {
                using (var failDialog = new FolderDeletionFailureDialog(resetResult.Path, resetResult.LastError))
                {
                    var dr = failDialog.ShowDialog(this);
                    if (dr == DialogResult.Retry)
                    {
                        resetResult = FolderDeletionService.TryDelete(resetResult.Path);
                    }
                    else
                    {
                        // 諦めた場合は警告 MessageBox を出して終了 (退避フォルダはゴミとして残る)
                        MessageBox.Show(this,
                            "データベースのリセットは完了しましたが、退避済みの旧 games フォルダの削除を諦めました。\n" +
                            "後で手動削除してください:\n  " + resetResult.Path,
                            "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            MessageBox.Show(this,
                "データベースのリセットが完了しました。",
                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
