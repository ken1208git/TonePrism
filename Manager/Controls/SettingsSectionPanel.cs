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

        public SettingsSectionPanel()
        {
            InitializeComponent();
        }

        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            UpdateVersionInfo();
            LoadLogSettings();
        }

        /// <summary>
        /// (#170 followup) 設定タブ「ログ」section の初期化。`numLogRetention.ValueChanged` event を
        /// **LoadLogSettings 後に hook** して、初期 SetValue 時に handler が発火しない (= CheckBeforeWrite
        /// dialog が起動時に意図せず出る path) を防ぐ。
        /// </summary>
        private void LoadLogSettings()
        {
            if (_dbManager == null) return;
            // ValueChanged を hook する前に初期値を SetValue (= 起動時 spurious 発火を防ぐ)
            numLogRetention.ValueChanged -= NumLogRetention_ValueChanged;
            try
            {
                int days = _dbManager.SettingsRepository.GetInt32(
                    SettingsKeys.LogRetentionDays, SettingsKeys.DefaultLogRetentionDays);
                if (days < numLogRetention.Minimum) days = (int)numLogRetention.Minimum;
                if (days > numLogRetention.Maximum) days = (int)numLogRetention.Maximum;
                numLogRetention.Value = days;
            }
            catch (Exception ex)
            {
                Logger.Warn("[SettingsSectionPanel] LoadLogSettings 読込失敗: " + ex.Message);
            }
            numLogRetention.ValueChanged += NumLogRetention_ValueChanged;
        }

        private void NumLogRetention_ValueChanged(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            // (#170 followup) user 意図的な destructive (= settings table 書込) 操作前に CheckBeforeWrite。
            // Cancel 時は値を元に戻して silent abort (= 他 PC 競合中の編集を中止)。
            int newValue = (int)numLogRetention.Value;
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "ログ保存日数変更") == DialogResult.Cancel)
            {
                // 値を rollback (= event 再発火を避けるため hook 一時 detach)
                numLogRetention.ValueChanged -= NumLogRetention_ValueChanged;
                try
                {
                    int previous = _dbManager.SettingsRepository.GetInt32(
                        SettingsKeys.LogRetentionDays, SettingsKeys.DefaultLogRetentionDays);
                    if (previous < numLogRetention.Minimum) previous = (int)numLogRetention.Minimum;
                    if (previous > numLogRetention.Maximum) previous = (int)numLogRetention.Maximum;
                    numLogRetention.Value = previous;
                }
                catch { /* swallow、UI 上は中途半端な値で残るが致命でない */ }
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
