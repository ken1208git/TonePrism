using System;
using System.IO;
using System.Linq;
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
            // (Manager v0.21.0) AutoSize=Fill で列幅を grid 幅にちょうど収め、横スクロールを出さない。
            // ファイルパスは余り幅を埋める可変列 (溢れた分は DataGridView 既定の "…" 省略表示)。固定情報列は
            // MinimumWidth で可読性を確保し、window を狭めても潰れないようにする。
            gridHistory.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            // (Manager v0.21.0) ファイル由来の履歴では開始 / 完了の区別が無いため日時は「作成日時」1 列に統合。
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "created", HeaderText = "作成日時", FillWeight = 150, MinimumWidth = 150 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "pc", HeaderText = "実行PC", FillWeight = 100, MinimumWidth = 90 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "trigger", HeaderText = "トリガ", FillWeight = 55, MinimumWidth = 50 });
            // (#200) 「状態」列は持たない。backup_log 廃止 (v19) 後は失敗はファイルを残さず履歴に出ない
            // (走査結果は実質すべて成功ファイル) ため状態列は情報量ゼロ。失敗通知は status bar / Logger で覆える。
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "サイズ", FillWeight = 75, MinimumWidth = 70 });
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "ファイルパス", FillWeight = 320, MinimumWidth = 120 });
        }

        public void RefreshDisplay()
        {
            if (_dbManager == null) return;

            try
            {
                // 履歴は backups/ フォルダのファイル走査由来 (BackupCatalogService)。DB の backup_log は v19 で
                // 廃止したため、reconcile / register / cleanup の後付け machinery は不要になった
                // (ファイル = 真実、欠落 = 不在、で恒常的にズレない)。
                var catalog = _dbManager.BackupCatalogService;

                // 最終バックアップ表示 (auto / manual / safety のうち最新 1 件)。DB(設定込み)+ゲーム本体(games/guide)は
                // 1 操作でまとめて控える「1 つのバックアップ」。横は左右にボタンがあり狭いので、1 行目=取得日時 /
                // 2 行目(灰)=中身(ゲーム本体のファイル数 + プール実使用=重複排除後の物理サイズ=実際に食っている量) の 2 行で表示。
                var last = catalog.GetLastSuccess();
                if (last == null)
                {
                    lblLastBackup.Text = "最終バックアップ: 未取得";
                    lblLastSnapshot.Text = "";
                }
                else
                {
                    lblLastBackup.Text = $"最終バックアップ: {last.StartedAtLocal:yyyy/MM/dd HH:mm:ss}";
                    var snap = _dbManager.AssetSnapshotService.GetLatestSnapshot();
                    if (snap != null && snap.StartedAtLocal != DateTime.MinValue)
                    {
                        long poolBytes = _dbManager.AssetSnapshotService.GetPoolPhysicalBytes();
                        // (round8 A/L1) .poolsize 未更新/読込失敗時の 0 は「計測中」に倒す (実使用 0 と未計測を区別)。
                        string poolDisp = poolBytes > 0 ? FormatBytes(poolBytes) : "計測中";
                        lblLastSnapshot.Text = $"ゲーム本体 {snap.FileCount} ファイル（実使用 {poolDisp}）";
                    }
                    else
                    {
                        lblLastSnapshot.Text = "ゲーム本体: 未取得";
                    }
                }

                lblDestPath.Text = $"保存先: {_dbManager.BackupService.GetEffectiveDestinationDirectory()}";

                // 履歴: 走査結果は実在ファイルのみなので File.Exists 追加フィルタ不要。新しい順 100 件。
                var entries = catalog.ScanAll().Take(100).ToList();
                gridHistory.Rows.Clear();
                foreach (var entry in entries)
                {
                    string trigger;
                    switch (entry.TriggerType)
                    {
                        case "manual": trigger = "手動"; break;
                        case "auto": trigger = "自動"; break;
                        case "safety": trigger = "退避"; break;
                        case "unknown": trigger = "不明"; break;  // v0.20.0 以前の旧フラット形式 (種類復元不能)
                        default: trigger = entry.TriggerType ?? ""; break;
                    }
                    // ファイル由来の作成日時 (開始 / 完了の区別が無いため 1 列)。
                    string created = entry.StartedAtLocal.ToString("yyyy/MM/dd HH:mm:ss");
                    int rowIndex = gridHistory.Rows.Add(
                        created,
                        entry.PcName,
                        trigger,
                        FormatBytes(entry.FileSizeBytes),
                        entry.FilePath);
                    // 行の Tag に元データを保存（Restore / Delete で取り出す）
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
                string msg = $"バックアップが完了しました。\n\nデータベース: {result.FilePath}\nサイズ: {FormatBytes(result.FileSizeBytes)}";
                // (#250 / レビュー M2・L2) このバックアップに同梱したアセット控えの結果を **result から直接** 使う
                // (GetLatestSnapshot + 文字列マッチは多ホスト同秒で別ホストの世代を拾う恐れがあるため廃止)。
                var snap = result.AssetSnapshot;
                if (snap != null && snap.IsSuccess && snap.FileCount > 0)
                {
                    // 控えは中身を共有プールに集約するので「控え全体の実使用量」を出す (見かけより小さい正直な値)。
                    long poolBytes = _dbManager.AssetSnapshotService.GetPoolPhysicalBytes();
                    string poolDisp = poolBytes > 0 ? FormatBytes(poolBytes) : "計測中"; // (round8 A/L1) 0 B 矛盾表示を回避
                    msg += $"\n\nゲーム本体 (games/guide) もバックアップしました:\n{snap.FileCount} ファイル ／ 全体の実使用: {poolDisp}";
                    // (round8 C1) 深部フォルダの列挙失敗で一部 skip した場合は「部分的な控え」を明示 (完全控えと誤認させない)。
                    if (snap.IsPartial)
                        msg += $"\n\n⚠ ただし {snap.SkippedDirCount} 個のフォルダを列挙できずスキップしました（部分的なバックアップの可能性。SMB 一過性 I/O / 権限等）。";
                }
                else if (snap != null && (snap.IsFailed || snap.IsAnomaly))
                {
                    // (レビュー M2) 失敗/異常は黙らず併記 (DB バックアップ自体は成功)。設定で無効・通常スキップは触れない。
                    msg += $"\n\n⚠ ゲーム本体 (games/guide) のバックアップは取得できませんでした（DB バックアップは成功）。\n{snap.Message}";
                }
                MessageBox.Show(msg, "バックアップ成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var entry = row.Tag as BackupCatalogEntry;
            // カタログ entry の FilePath は走査で得た絶対パスそのもの (パス解決不要)。
            string resolvedPath = entry?.FilePath;
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
                if (confirm.ShowDialog(this) != DialogResult.Yes)
                {
                    Logger.Info($"[BackupSectionPanel] 復元キャンセル (確認ダイアログ): source='{resolvedPath}'");
                    return;
                }
            }

            // (監査ログ) 確認コード入力を通過した時点で復元意思が確定。以降の経路 (session conflict / cancel /
            // abort / success) を Logger に残して事後追跡できるようにする。旧実装は MessageBox エラー文言が
            // 「詳細はログを確認」と促していたのにログ自体が空、という不整合があった。
            Logger.Info($"[BackupSectionPanel] 復元実行: source='{resolvedPath}'");

            // (round 2 High-2) user 確認後、DB write (Restore = safety backup + DB 置換) 直前で session conflict check
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ復元") == DialogResult.Cancel)
            {
                Logger.Info("[BackupSectionPanel] 復元中止 (session conflict check で user がキャンセル)");
                return;
            }

            string safetyPath = null;
            RestoreDbMissingException dbMissing = null;
            DialogResult dr;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                // (レビュー対応 #1) DB 喪失の最悪ケースは専用例外で捕捉して caller に伝える。re-throw して
                // ProcessingDialog には Abort させる (例外 Message = 具体的な復旧手順がそのまま画面表示される)。
                try { safetyPath = _dbManager.RestoreService.Restore(resolvedPath, progress, token); }
                catch (RestoreDbMissingException dmx) { dbMissing = dmx; throw; }
            }))
            {
                dialog.Text = "復元中";
                dr = dialog.ShowDialog(this);
            }

            // (レビュー対応 #1) toneprism.db 喪失の最悪ケース: ProcessingDialog が既に例外 Message の具体的な
            // 復旧手順を表示済。汎用「復元中にエラー（詳細はログ）」で上書きせず、ログを残して中止する。
            if (dbMissing != null)
            {
                Logger.Error("[BackupSectionPanel] 復元中に toneprism.db が失われた可能性 (要手動復旧): " + dbMissing.Message);
                return;
            }

            // ProcessingDialog は OperationCanceledException を Cancel、
            // それ以外の例外を Abort として返す（成功時は OK）。
            // Cancel を OK と区別せず成功扱いにすると、キャンセル後も
            // DatabaseChanged が走って中途半端な状態の DB をリロードしてしまう。
            // （Codex P1 指摘 "Handle restore cancellation before reporting success" 対応）
            if (dr == DialogResult.Cancel)
            {
                Logger.Info("[BackupSectionPanel] 復元キャンセル (ProcessingDialog で user がキャンセル)");
                MessageBox.Show(
                    "復元はキャンセルされました。\n\nデータベースは変更されていません。",
                    "キャンセル", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (dr == DialogResult.Abort)
            {
                // 例外詳細は RestoreService 側で Logger.Error 済 (= MessageBox の「詳細はログを確認」が機能する)。
                Logger.Error("[BackupSectionPanel] 復元失敗 (ProcessingDialog から Abort、詳細は直前の RestoreService ログ参照)");
                MessageBox.Show(
                    "復元中にエラーが発生しました（詳細はログを確認）",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // dr == DialogResult.OK: 復元成功
            // (重要) Analyze の前に必ずスキーマ migration を走らせる。古い schema (例: arguments 列なしの
            // v13 以前) のバックアップを復元した直後は、現行クエリ (v15 前提) が "no such column" で
            // 失敗するため Analyze が空振りする。InitializeDatabase は idempotent なので、後続の
            // DatabaseChanged?.Invoke() 経由 (= OnDatabaseRestored の InitializeDatabase) と二重呼出に
            // なっても害はない。
            // (H4) 同じ理由で audit 行の INSERT も migration 後に行う必要がある (= 古い backup には 'restore'
            // CHECK が無いため、migration 前に INSERT すると CHECK 制約違反になる)。
            try
            {
                _dbManager.InitializeDatabase();
            }
            catch (Exception ex)
            {
                Logger.Error("[BackupSectionPanel] 復元後のスキーマ migration に失敗", ex);
            }

            // (backup_log 廃止) 復元の監査行 INSERT は不要。復元で作られた safety_*.db が証跡を兼ね、
            // 履歴グリッドにも「退避」として走査表示される。

            // (復元ドリフト検出) バックアップ/復元は toneprism.db のみが対象で games/ フォルダは復元
            // されないため、別時点の DB を復元すると DB と実フォルダがズレうる。復元直後に突き合わせて
            // 結果と復元手順を提示する。深刻な問題 (起動不能) があれば必ずレポートを出し、軽微なズレや
            // 問題なしのときは簡潔な成功通知に留める。
            RestoreReconciliationResult reconcile = null;
            try
            {
                reconcile = new Services.RestoreReconciliationService(_dbManager).Analyze();
            }
            catch (Exception ex)
            {
                Logger.Warn("[BackupSectionPanel] 復元後の整合性チェックに失敗: " + ex.Message);
            }

            // (監査ログ) 復元成功 + 整合性チェック結果の要約を残す。findings の内訳が後から追跡できるよう
            // broken / missing / orphan のカウントを 1 行にまとめる。reconcile が null (=チェック自体が
            // 失敗) のときも明示。
            if (reconcile == null)
            {
                Logger.Info($"[BackupSectionPanel] 復元成功: source='{resolvedPath}', safety='{safetyPath}', reconcile=skipped");
            }
            else if (reconcile.AnalysisFailed)
            {
                Logger.Info($"[BackupSectionPanel] 復元成功: source='{resolvedPath}', safety='{safetyPath}', reconcile=analysis_failed: {reconcile.AnalysisError}");
            }
            else
            {
                Logger.Info(
                    $"[BackupSectionPanel] 復元成功: source='{resolvedPath}', safety='{safetyPath}', " +
                    $"reconcile broken={reconcile.BrokenGames.Count}, " +
                    $"missing_versions={reconcile.MissingVersionFolders.Count}, " +
                    $"orphans={reconcile.OrphanFolders.Count}");
            }

            if (reconcile != null && (reconcile.HasAnyFindings || reconcile.AnalysisFailed))
            {
                using (var report = new RestoreReportForm(reconcile, safetyPath))
                {
                    report.ShowDialog(this.FindForm());
                }
            }
            else
            {
                MessageBox.Show(
                    $"復元が完了しました。\n\n復元前のDBは退避されました:\n{safetyPath}\n\n" +
                    "DB とゲームフォルダの整合性に問題はありませんでした。\nManager のデータを再読み込みします。",
                    "復元成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DatabaseChanged?.Invoke();
            RefreshDisplay();
        }

        /// <summary>
        /// 「削除」ボタン: 選択行のバックアップファイルを物理削除する (backup_log 廃止後は DB 行なし)。
        /// </summary>
        private void btnDelete_Click(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            // (round 2 High-2) selection / 削除確認の各 validation を session conflict check より前に倒す。
            // check はファイル削除直前 (削除確認 OK 後) で実行。backup_log 廃止後は in_progress 状態が無いため
            // 旧「実行中は削除不可」ガードは撤去 (走査に出るのは実在ファイルのみ)。
            if (gridHistory.SelectedRows.Count == 0)
            {
                MessageBox.Show("削除したいバックアップを履歴一覧から選択してください。", "未選択",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var row = gridHistory.SelectedRows[0];
            var entry = row.Tag as BackupCatalogEntry;
            if (entry == null || string.IsNullOrEmpty(entry.FilePath))
            {
                MessageBox.Show("選択したエントリの情報が取得できませんでした。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string resolvedPath = entry.FilePath;
            string fileName = Path.GetFileName(resolvedPath);

            var dr = MessageBox.Show(this,
                $"バックアップ「{fileName}」を削除します。\n\n" +
                $"この操作は取り消せません。よろしいですか？",
                "バックアップの削除確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);

            if (dr != DialogResult.OK) return;

            // (round 2 High-2) 削除確認 OK 後、ファイル削除直前で session conflict check
            if (Services.SessionConflictHelper.CheckBeforeWrite(this, "バックアップ削除") == DialogResult.Cancel) return;

            // 物理ファイル削除。削除できなければカタログに残り続けるので、通知して再表示するだけ。
            if (File.Exists(resolvedPath))
            {
                try
                {
                    File.Delete(resolvedPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        $"バックアップファイルの削除に失敗しました。\n\n{ex.Message}",
                        "ファイル削除失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    RefreshDisplay();
                    return;
                }
            }

            // (追加精査 ⑦) auto を削除した場合、last_backup_at を「残った中で最新の auto の開始時刻」に rewind する。
            // さもないと「最新の自動バックアップを消して取り直したい」操作で IsAutoBackupDue が間隔未到達と
            // 誤判定し、次の自動バックアップが skip される。残りが無ければ 0 (= 初回扱い、次回判定で取得される)。
            // 手動 / 退避 (safety) は last_backup_at に無関係なので何もしない。
            if (entry.TriggerType == "auto")
            {
                // File.Delete 後に再走査するので、いま消した分は除外される。
                var newLatest = _dbManager.BackupCatalogService.GetLastAuto();
                long newLastBackupAt = newLatest != null ? newLatest.StartedAt : 0;
                // (累積監査 round 6 M8) 削除中に別 PC が新しい auto を取得して last_backup_at を前進させていた場合に
                // 古い値で上書きして二重バックアップを誘発しないよう、rewind になる (= 現在値より小さくなる) ときだけ更新。
                long currentLastBackupAt = _dbManager.SettingsRepository.GetInt64("last_backup_at", 0);
                if (newLastBackupAt < currentLastBackupAt)
                {
                    _dbManager.SettingsRepository.SetInt64("last_backup_at", newLastBackupAt);
                }
            }

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
