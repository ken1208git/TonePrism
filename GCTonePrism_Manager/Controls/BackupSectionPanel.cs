using System;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;
using GCTonePrism.Manager.Services;

namespace GCTonePrism.Manager.Controls
{
    public partial class BackupSectionPanel : UserControl
    {
        private DatabaseManager _dbManager;

        /// <summary>
        /// バックアップやリストアによってDB状態が変化したときに通知される
        /// </summary>
        public event Action DatabaseChanged;

        public BackupSectionPanel()
        {
            InitializeComponent();
            ConfigureGrid();
        }

        public void Initialize(DatabaseManager dbManager)
        {
            _dbManager = dbManager;
            RefreshDisplay();
        }

        private void ConfigureGrid()
        {
            gridHistory.AutoGenerateColumns = false;
            gridHistory.Columns.Clear();
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "started", HeaderText = "開始日時", Width = 150 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "completed", HeaderText = "完了日時", Width = 150 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "pc", HeaderText = "実行PC", Width = 110 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "trigger", HeaderText = "トリガ", Width = 60 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = "状態", Width = 70 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "サイズ", Width = 90 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "ファイルパス", Width = 380 });
        }

        public void RefreshDisplay()
        {
            if (_dbManager == null) return;

            try
            {
                // 接続プールに古いスナップショットが残ると新しい書き込みが見えないことがあるので
                // プールを掃除して常に最新コミット状態を読みに行くようにする
                SQLiteConnection.ClearAllPools();

                // 表示更新前にリコンサイル：
                //   - in_progress 行 → ファイル実在で success/failed 判定（閾値なし=全件）
                //   - failed 行のうちファイルが見つかるもの → success に救済
                //   - file_path が空の failed 行 → バックアップフォルダをスキャンして
                //     started_at と一致するファイルがあれば success に復元（旧版ゴースト救済）
                //   - backups/safety/ の未登録ファイルを backup_log に登録
                //   - 残った failed 行 → 物理ファイル + DB から自動掃除 (#126)
                try
                {
                    var (recoveredSuccess, markedFailed) = _dbManager.BackupLogRepository.ReconcileInProgressEntries(
                        "バックアップファイルが見つかりませんでした",
                        thresholdSeconds: null,
                        recoverFailedWithExistingFile: true);
                    int legacyRecovered = _dbManager.BackupLogRepository.RecoverLegacyFailedEntriesByFolderScan(
                        _dbManager.BackupService.GetEffectiveDestinationDirectory());
                    int safetyAdded = _dbManager.BackupLogRepository.RegisterUnknownSafetyFiles(
                        _dbManager.BackupService.GetSafetyDirectory(),
                        Environment.MachineName);
                    int failedCleaned = CleanupFailedEntries();
                    if (recoveredSuccess > 0 || markedFailed > 0 || legacyRecovered > 0 || safetyAdded > 0 || failedCleaned > 0)
                    {
                        Console.WriteLine($"[BackupSectionPanel] 更新時リコンサイル: 成功化 {recoveredSuccess} / 失敗化 {markedFailed} / 旧版救済 {legacyRecovered} / 退避新規 {safetyAdded} / 失敗自動掃除 {failedCleaned}");
                    }
                }
                catch (Exception reconcileEx)
                {
                    Console.WriteLine($"[BackupSectionPanel] リコンサイル中にエラー: {reconcileEx.Message}");
                }

                // 最終バックアップ表示
                var last = _dbManager.BackupLogRepository.GetLastSuccess();
                if (last != null)
                {
                    string sizeStr = last.FileSizeBytes.HasValue ? FormatBytes(last.FileSizeBytes.Value) : "-";
                    lblLastBackup.Text = $"最終バックアップ: {last.StartedAtLocal:yyyy/MM/dd HH:mm:ss} ({sizeStr})";
                }
                else
                {
                    lblLastBackup.Text = "最終バックアップ: 未取得";
                }

                lblDestPath.Text = $"保存先: {_dbManager.BackupService.GetEffectiveDestinationDirectory()}";

                // 履歴 (#126: パス解決 + 実在チェックして不在は非表示)
                var entries = _dbManager.BackupLogRepository.GetRecent(100);
                long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string dbPath = _dbManager.DatabasePath;
                gridHistory.Rows.Clear();
                foreach (var entry in entries)
                {
                    // パス解決 (relative_path 優先、無ければ file_path)
                    string resolvedPath = BackupPathResolver.ResolveAbsolutePath(entry, dbPath);

                    // 実在チェック: 不在なら表示しない (in_progress は実行直後で File.Exists 前のことがあるため例外)
                    if (entry.Status != "in_progress" &&
                        (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath)))
                    {
                        continue;
                    }

                    string status = GetStatusDisplay(entry, nowUnix);
                    string statusTooltip = GetStatusTooltip(entry, nowUnix);
                    string trigger;
                    switch (entry.TriggerType)
                    {
                        case "manual": trigger = "手動"; break;
                        case "auto": trigger = "自動"; break;
                        case "safety": trigger = "退避"; break;
                        default: trigger = entry.TriggerType ?? ""; break;
                    }
                    string size = entry.FileSizeBytes.HasValue ? FormatBytes(entry.FileSizeBytes.Value) : "-";
                    string completed = entry.CompletedAtLocal.HasValue
                        ? entry.CompletedAtLocal.Value.ToString("yyyy/MM/dd HH:mm:ss")
                        : "-";
                    int rowIndex = gridHistory.Rows.Add(
                        entry.StartedAtLocal.ToString("yyyy/MM/dd HH:mm:ss"),
                        completed,
                        entry.PcName,
                        trigger,
                        status,
                        size,
                        resolvedPath);
                    // 行のTagに元データを保存（Restore で取り出す）
                    gridHistory.Rows[rowIndex].Tag = entry;
                    // 状態セルにツールチップと色を適用
                    var statusCell = gridHistory.Rows[rowIndex].Cells["status"];
                    if (!string.IsNullOrEmpty(statusTooltip))
                    {
                        statusCell.ToolTipText = statusTooltip;
                    }
                    statusCell.Style.BackColor = GetStatusBackColor(entry);
                    statusCell.Style.SelectionBackColor = GetStatusSelectionBackColor(entry);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"履歴の取得に失敗しました: {ex.Message}", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnBackupNow_Click(object sender, EventArgs e)
        {
            if (_dbManager == null) return;

            BackupResult result = null;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                result = _dbManager.BackupService.RunManualBackup(progress, token);
            }))
            {
                dialog.Text = "バックアップ実行中";
                dialog.ShowDialog(this);
            }

            if (result == null) return;

            if (result.IsSuccess)
            {
                MessageBox.Show(
                    $"バックアップが完了しました。\n\nファイル: {result.FilePath}\nサイズ: {FormatBytes(result.FileSizeBytes)}",
                    "バックアップ成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (result.IsFailed)
            {
                MessageBox.Show(
                    $"バックアップに失敗しました。\n\n{result.Message}",
                    "バックアップ失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            RefreshDisplay();
        }

        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshDisplay();
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            if (_dbManager == null) return;

            using (var form = new BackupSettingsForm(_dbManager.SettingsRepository, _dbManager.BackupService))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshDisplay();
                }
            }
        }

        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            if (gridHistory.SelectedRows.Count == 0)
            {
                MessageBox.Show("復元したいバックアップを履歴一覧から選択してください。", "未選択",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = gridHistory.SelectedRows[0];
            var entry = row.Tag as BackupLogEntry;
            // パス解決 (#126: relative_path 優先、無ければ file_path)
            string resolvedPath = BackupPathResolver.ResolveAbsolutePath(entry, _dbManager.DatabasePath);
            if (entry == null || string.IsNullOrEmpty(resolvedPath))
            {
                MessageBox.Show("選択したエントリにはファイルパス情報がありません。", "情報なし",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!File.Exists(resolvedPath))
            {
                MessageBox.Show($"バックアップファイルが見つかりません:\n{resolvedPath}", "ファイルなし",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var confirm = new RestoreConfirmForm(entry))
            {
                if (confirm.ShowDialog(this) != DialogResult.Yes) return;
            }

            string safetyPath = null;
            DialogResult dr;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                safetyPath = _dbManager.RestoreService.Restore(resolvedPath, progress, token);
            }))
            {
                dialog.Text = "復元中";
                dr = dialog.ShowDialog(this);
            }

            // ProcessingDialog は OperationCanceledException を Cancel、
            // それ以外の例外を Abort として返す（成功時は OK）。
            // Cancel を OK と区別せず成功扱いにすると、キャンセル後も
            // DatabaseChanged が走って中途半端な状態の DB をリロードしてしまう。
            // （Codex P1 指摘 "Handle restore cancellation before reporting success" 対応）
            if (dr == DialogResult.Cancel)
            {
                MessageBox.Show(
                    "復元はキャンセルされました。\n\nデータベースは変更されていません。",
                    "キャンセル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (dr == DialogResult.Abort)
            {
                MessageBox.Show(
                    "復元中にエラーが発生しました（詳細はログを確認）",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // dr == DialogResult.OK: 復元成功
            MessageBox.Show(
                $"復元が完了しました。\n\n復元前のDBは退避されました:\n{safetyPath}\n\nManager のデータを再読み込みします。",
                "復元成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DatabaseChanged?.Invoke();
            RefreshDisplay();
        }

        /// <summary>
        /// failed 状態のレコードを物理ファイル + DB から自動削除する (#126)。
        /// 失敗したバックアップの履歴は残してもユーザーには情報価値が無く、
        /// 古いプロジェクトパスのゴミがそのまま残り続ける主因にもなるため。
        ///
        /// 二重防御 (#127 Codex P1): Reconcile が誤って failed 化した行で実ファイルが
        /// 残っているケースを掃除で物理削除しないよう、BackupPathResolver で解決した
        /// パスにファイルが実在する場合は DB レコードもファイルも触らない（安全側）。
        /// </summary>
        /// <returns>削除した件数</returns>
        private int CleanupFailedEntries()
        {
            int cleaned = 0;
            string dbPath = _dbManager.DatabasePath;
            var failedEntries = _dbManager.BackupLogRepository.GetByStatus("failed");
            foreach (var entry in failedEntries)
            {
                string path = BackupPathResolver.ResolveAbsolutePath(entry, dbPath);

                // セーフティ: failed なのに実ファイルが残っている場合は触らない
                // （Reconcile の取りこぼしで誤分類された可能性があり、ユーザーの貴重な
                // バックアップを誤って削除しないため）
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    Console.WriteLine($"[BackupSectionPanel] failed だがファイル実在のため掃除スキップ (id={entry.Id}, path={path})");
                    continue;
                }

                _dbManager.BackupLogRepository.DeleteById(entry.Id);
                cleaned++;
            }
            return cleaned;
        }

        /// <summary>
        /// 「削除」ボタン: 選択行のバックアップを物理ファイル + DB レコード両方削除 (#126)
        /// </summary>
        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            if (gridHistory.SelectedRows.Count == 0)
            {
                MessageBox.Show("削除したいバックアップを履歴一覧から選択してください。", "未選択",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = gridHistory.SelectedRows[0];
            var entry = row.Tag as BackupLogEntry;
            if (entry == null)
            {
                MessageBox.Show("選択したエントリの情報が取得できませんでした。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string resolvedPath = BackupPathResolver.ResolveAbsolutePath(entry, _dbManager.DatabasePath);
            string fileName = !string.IsNullOrEmpty(resolvedPath) ? Path.GetFileName(resolvedPath) : $"id={entry.Id}";

            var dr = MessageBox.Show(this,
                $"バックアップ「{fileName}」を削除します。\n\n" +
                $"この操作は取り消せません。よろしいですか？",
                "バックアップの削除確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (dr != DialogResult.OK) return;

            // 物理ファイル削除 (失敗しても DB レコードは消す)
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                try
                {
                    File.Delete(resolvedPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        $"バックアップファイルの削除に失敗しましたが、履歴レコードは削除します。\n\n{ex.Message}",
                        "ファイル削除失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            // DB レコード削除
            _dbManager.BackupLogRepository.DeleteById(entry.Id);

            RefreshDisplay();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:F1} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:F2} MB";
            double gb = mb / 1024.0;
            return $"{gb:F2} GB";
        }

        /// <summary>
        /// 状態の表示文字列。DB上は3状態 (success / failed / in_progress) のままシンプルに保つ。
        /// </summary>
        private static string GetStatusDisplay(BackupLogEntry entry, long nowUnixSec)
        {
            switch (entry.Status)
            {
                case "success": return "成功";
                case "failed": return "失敗";
                case "in_progress": return "実行中";
                default: return entry.Status ?? "";
            }
        }

        /// <summary>
        /// 状態セルのツールチップ。in_progress については経過時間と「本当に実行中 vs 残骸の可能性」を文脈で出す。
        /// </summary>
        private static string GetStatusTooltip(BackupLogEntry entry, long nowUnixSec)
        {
            switch (entry.Status)
            {
                case "success":
                    return "バックアップは完了済みです。この行から復元できます。";
                case "failed":
                    return string.IsNullOrEmpty(entry.ErrorMessage)
                        ? "バックアップに失敗しました。実ファイルが存在しないため復元には使えません。"
                        : $"中断理由: {entry.ErrorMessage}";
                case "in_progress":
                    long age = nowUnixSec - entry.StartedAt;
                    if (age < 30)
                    {
                        return $"現在実行中です（経過: {age} 秒）。完了まで少々お待ちください。";
                    }
                    string elapsed = age < 60
                        ? $"{age} 秒"
                        : (age < 3600 ? $"{age / 60} 分" : $"{age / 3600} 時間");
                    return $"前回 Manager 異常終了の残骸の可能性があります（経過: {elapsed}）。" +
                           "更新ボタンを押すと実ファイルの有無で 成功/失敗 が確定します。";
                default:
                    return "";
            }
        }

        /// <summary>
        /// 状態セルの背景色（非選択時）
        /// </summary>
        private static Color GetStatusBackColor(BackupLogEntry entry)
        {
            switch (entry.Status)
            {
                case "success": return Color.FromArgb(220, 245, 220); // 淡い緑
                case "failed": return Color.FromArgb(250, 220, 220);  // 淡い赤
                case "in_progress": return Color.FromArgb(220, 232, 250); // 淡い青
                default: return Color.White;
            }
        }

        /// <summary>
        /// 状態セルの背景色（選択時）。標準の青選択色だと色分けが見えなくなるので、選択中も色味を保つ。
        /// </summary>
        private static Color GetStatusSelectionBackColor(BackupLogEntry entry)
        {
            switch (entry.Status)
            {
                case "success": return Color.FromArgb(150, 210, 150); // 中緑
                case "failed": return Color.FromArgb(220, 150, 150);  // 中赤
                case "in_progress": return Color.FromArgb(150, 180, 220); // 中青
                default: return Color.LightGray;
            }
        }
    }
}
