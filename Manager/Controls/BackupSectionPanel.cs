using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Controls
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
            // (#200) 「状態」列は削除。failed は AutoCleanupFailedEntries で DB + 物理削除され、in_progress は
            // 数秒〜数十秒のみのため、grid を開いた時点で実質ほぼ全行「成功」になり情報量ゼロだった。
            // 失敗通知は status bar (PR #196 G 系) で覆える。DB schema / status reconcile logic は不変。
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
                //   - in_progress 行 → 開始から 300 秒以上経過したものだけファイル実在で success/failed 判定
                //     (#127 Codex P1: 閾値なしだと別PC/別タスクで進行中の正常バックアップを
                //      誤って failed 化 → CleanupFailedEntries が DB レコード削除 → 後続の
                //      MarkSuccess が 0 件更新で成功履歴が消失するため、現実的なバックアップ
                //      所要時間より長い 300 秒を閾値にして進行中行を保護する)
                //   - failed 行のうちファイルが見つかるもの → success に救済
                //   - file_path が空の failed 行 → バックアップフォルダをスキャンして
                //     started_at と一致するファイルがあれば success に復元（旧版ゴースト救済）
                //   - backups/safety/ の未登録ファイルを backup_log に登録
                //   - 残った failed 行 → 物理ファイル + DB から自動掃除 (#126)
                try
                {
                    var (recoveredSuccess, markedFailed) = _dbManager.BackupLogRepository.ReconcileInProgressEntries(
                        "バックアップファイルが見つかりませんでした",
                        thresholdSeconds: 300,
                        recoverFailedWithExistingFile: true);
                    int legacyRecovered = _dbManager.BackupLogRepository.RecoverLegacyFailedEntriesByFolderScan(
                        _dbManager.BackupService.GetEffectiveDestinationDirectory());
                    int safetyAdded = _dbManager.BackupLogRepository.RegisterUnknownSafetyFiles(
                        _dbManager.BackupService.GetSafetyDirectory(),
                        Environment.MachineName);
                    int failedCleaned = CleanupFailedEntries();
                    if (recoveredSuccess > 0 || markedFailed > 0 || legacyRecovered > 0 || safetyAdded > 0 || failedCleaned > 0)
                    {
                        Logger.Info($"[BackupSectionPanel] 更新時リコンサイル: 成功化 {recoveredSuccess} / 失敗化 {markedFailed} / 旧版救済 {legacyRecovered} / 退避新規 {safetyAdded} / 失敗自動掃除 {failedCleaned}");
                    }
                }
                catch (Exception reconcileEx)
                {
                    Logger.Error("[BackupSectionPanel] リコンサイル中にエラー", reconcileEx);
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
                    // (#200) 「状態」列削除に伴い status / color / tooltip の cell 設定も撤去。
                    int rowIndex = gridHistory.Rows.Add(
                        entry.StartedAtLocal.ToString("yyyy/MM/dd HH:mm:ss"),
                        completed,
                        entry.PcName,
                        trigger,
                        size,
                        resolvedPath);
                    // 行のTagに元データを保存（Restore で取り出す）
                    gridHistory.Rows[rowIndex].Tag = entry;
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
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ作成") == DialogResult.Cancel) return;

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

        // (#170 followup round 1) btnSettings はバックアップタブから完全に削除。
        // 旧 btnSettings_Click はここで modal を開く処理 → info dialog 案内 → 完全削除と段階的に縮退、
        // 最終的に動線を一本化 (= 設定はすべて「設定タブ」)。

        private void btnRestore_Click(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            // (round 2 High-2) selection 依存 validation を session conflict check より前に倒す
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

            // (round 2 High-2) user 確認後、DB write (Restore = safety backup + DB 置換) 直前で session conflict check
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ復元") == DialogResult.Cancel) return;

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
                    Logger.Warn($"[BackupSectionPanel] failed だがファイル実在のため掃除スキップ (id={entry.Id}, path={path})");
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
            // (round 2 High-2) selection / in_progress / 削除確認の各 validation を session conflict
            // check より前に倒す。check は DB write 直前 (削除確認 OK 後) で実行。
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

            // 進行中の行は削除不可 (Codex P2 #127):
            // 別 PC や他タスクで現在進行中のバックアップ行を消すと、後で MarkSuccess が
            // 対象を見失って成功履歴が消える + 書き込み中のファイルが削除される
            if (entry.Status == "in_progress")
            {
                MessageBox.Show(this,
                    "このバックアップは現在実行中（または別 PC からの進行中）の可能性があるため削除できません。\n\n" +
                    "更新ボタンで状態を再確認してから、成功または失敗が確定したものを削除してください。",
                    "実行中のため削除不可",
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

            // (round 2 High-2) 削除確認 OK 後、DB write (file + log delete) 直前で session conflict check
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ削除") == DialogResult.Cancel) return;

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
    }
}
