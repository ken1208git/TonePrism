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
                        lblLastSnapshot.Text = $"ゲームファイル {snap.FileCount} 個（実使用 {poolDisp}）";
                    }
                    else
                    {
                        lblLastSnapshot.Text = "ゲームファイル: 未取得";
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
                // (UX) クリーン成功時に「ゲーム本体もバックアップしました（N ファイル / 実使用 X）」を明示するのは冗長
                // （「バックアップ＝全部まとめて控える」が当たり前に取られる）。「便りがないのは良い便り」で、**問題があるときだけ**
                // 出す。部分取得・失敗・異常は黙らず併記して「DB 成功 ≠ ゲーム本体の控えあり」を隠さない（レビュー round2-7 の
                // 不変条件は維持）。件数・実使用サイズはバックアップタブ（lblLastSnapshot）で常時確認できる。
                if (snap != null && snap.IsSuccess && snap.IsPartial)
                {
                    // (round8 C1) 深部フォルダの列挙失敗で一部 skip した場合は「部分的な控え」を明示 (完全控えと誤認させない)。
                    msg += $"\n\n⚠ ゲームファイル (games/guide) のバックアップでフォルダ {snap.SkippedDirCount} 個 / ファイル {snap.SkippedFileCount} 個を控えられずスキップしました（部分的なバックアップの可能性。SMB 一過性 I/O / 権限 / 並行編集での消失等）。";
                }
                else if (snap != null && (snap.IsFailed || snap.IsAnomaly))
                {
                    // (レビュー M2) 失敗/異常は黙らず併記 (DB バックアップ自体は成功)。設定で無効・通常スキップは触れない。
                    msg += $"\n\n⚠ ゲームファイル (games/guide) のバックアップは取得できませんでした（DB バックアップは成功）。\n{snap.Message}";
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

            // (#299 review round3 #3) 復元前に復元元の整合性を quick_check し、壊れていれば UI スレッドで案内する。
            // **open すらできない** (切り詰め / 非 DB) = 本当に使えない破損 → 中止 (override 無し。置換しても使えない)。
            // **open はできるが quick_check 非 ok** = 不健全 → 復元は「最後の手段」なので「それでも復元しますか？」を確認し、
            // ユーザーが Yes のときだけ allowIntegrityWarnings=true で続行する (現データは復元前に safety 退避されるので可逆)。
            bool allowIntegrityWarnings = false;
            var integrity = Services.RestoreService.CheckIntegrity(resolvedPath);
            if (!integrity.Openable)
            {
                Logger.Warn("[BackupSectionPanel] 復元中止: 復元元が開けない (壊れている可能性): " + resolvedPath);
                MessageBox.Show(
                    "このバックアップは壊れていて復元できません（ファイルとして開けませんでした）。\n別の世代を選んでください。現在のデータベースは変更していません。",
                    "復元できません", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!string.Equals(integrity.QuickCheckResult, "ok", StringComparison.OrdinalIgnoreCase))
            {
                string detail = integrity.QuickCheckResult ?? "(結果なし)";
                if (detail.Length > 300) detail = detail.Substring(0, 300) + " …";
                var ans = MessageBox.Show(
                    "このバックアップは整合性チェックに問題があります（壊れている可能性）:\n\n" + detail
                    + "\n\nそれでも復元しますか？\n（現在のデータは復元前に退避されるので、結果がおかしければ元に戻せます）",
                    "整合性の警告", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (ans != DialogResult.Yes)
                {
                    Logger.Info("[BackupSectionPanel] 復元中止: 整合性警告でユーザーが復元しない選択");
                    return;
                }
                allowIntegrityWarnings = true;
                Logger.Warn("[BackupSectionPanel] 整合性に問題のあるバックアップをユーザー確認のうえ復元: " + resolvedPath);
            }

            // (#299 review #2) 非ブロッキング化で、操作直後の自動バックアップ worker が走っている最中でも復元を起動できる
            // ようになった。復元は live toneprism.db を File.Replace で置換するため、worker が同じ DB を開いている (DB コピー
            // フェーズ) と File.Replace が衝突しうる。復元は排他操作なので進行中の自動バックアップを先に協調キャンセルする
            // (best-effort。復元の safety 退避 + temp コピーの間に worker は cancel token を観測して DB を解放するので、
            //  File.Replace 時点では衝突しない)。手動バックアップは共有プール CAS + 24h grace で worker と同時実行しても
            //  安全なため gate しない (SPEC §機能12 参照)。
            try
            {
                var runningCoord = _dbManager.SessionBackupCoordinator;
                if (runningCoord != null && runningCoord.IsBackupRunning)
                {
                    Logger.Info("[BackupSectionPanel] 復元前に進行中の自動バックアップを中止します (DB 置換との衝突回避)");
                    // (round4 L-1) 復元起点のキャンセルは未バックアップ警告を立てない (これから現データを置換するので spurious)。
                    runningCoord.CancelCurrentBackup(flagPendingAssetsUnhealthy: false);
                }
            }
            catch (Exception ex) { Logger.Warn("[BackupSectionPanel] 復元前の自動バックアップ中止に失敗 (続行): " + ex.Message); }

            string safetyPath = null;
            RestoreDbMissingException dbMissing = null;
            DialogResult dr;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                // (レビュー対応 #1) DB 喪失の最悪ケースは専用例外で捕捉して caller に伝える。re-throw して
                // ProcessingDialog には Abort させる (例外 Message = 具体的な復旧手順がそのまま画面表示される)。
                try { safetyPath = _dbManager.RestoreService.Restore(resolvedPath, progress, token, allowIntegrityWarnings); }
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

            // (#295) 旧実装はここで auto 削除時に last_backup_at を rewind していた (起動時の IsAutoBackupDue が
            // 「間隔未到達」と誤判定して次の自動バックアップを skip するのを防ぐため)。#295 で起動時の時間トリガを
            // 廃止し last_backup_at は **もはやトリガ gate ではない** (= 表示は GetLastSuccess のファイル走査由来) ため、
            // rewind は不要になった (= デッドロジックなので撤去)。
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
