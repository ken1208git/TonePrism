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
            // (ユーザー指摘) ヘッダー「トリガ」は列幅が狭く「トリ／ガ」と 2 行折り返しになり、かつ技術用語で分かりにくい。
            // 値は「自動／手動／退避」＝バックアップの種類なので「種類」に変更 (2 文字で折り返さず、意味も平易)。
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "trigger", HeaderText = "種類", FillWeight = 55, MinimumWidth = 56 });
            // (#200) 「状態」列は持たない。backup_log 廃止 (v19) 後は失敗はファイルを残さず履歴に出ない
            // (走査結果は実質すべて成功ファイル) ため状態列は情報量ゼロ。失敗通知は status bar / Logger で覆える。
            // (ユーザー指摘) この列は .db ファイル単体のサイズ (DBのみ、数百KB級)。世代全体 (ゲームファイル込み) と
            // 誤解されるのを避けるため「DBサイズ」と明示する。ゲームファイル込みの実使用量は共有プール (CAS) で重複排除
            // されるため世代別には出せず、下の「最終バックアップ」欄の実使用 (プール全体) で確認する。
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "DBサイズ", FillWeight = 80, MinimumWidth = 72 });
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
                    msg += $"\n\n⚠ ゲームファイルや初回説明の画像のバックアップでフォルダ {snap.SkippedDirCount} 個 / ファイル {snap.SkippedFileCount} 個を控えられずスキップしました（部分的なバックアップの可能性。SMB 一過性 I/O / 権限 / 並行編集での消失等）。";
                }
                else if (snap != null && (snap.IsFailed || snap.IsAnomaly))
                {
                    // (レビュー M2) 失敗/異常は黙らず併記 (DB バックアップ自体は成功)。設定で無効・通常スキップは触れない。
                    msg += $"\n\n⚠ ゲームファイルや初回説明の画像のバックアップは取得できませんでした（DB バックアップは成功）。\n{snap.Message}";
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

            // (#250 PR3b) この DB 世代とペアになるアセット控え (manifest) を解決する。.db と .manifest は別ファイルで厳密な
            // 紐づけ ID を持たないため、trigger type ごとに解決方法を変える。null = ペア無し → 確認ダイアログで「DBのみ」固定。
            //
            // - **auto/manual** (review #1): DB バックアップ成功直後に同一 timestamp で manifest を co-create するため、
            //   「T 以下で最大時刻」(replace-in-session で .db>manifest を正しく拾う) で解決。誤って近接の別世代を拾わないよう
            //   FindSnapshotForBackup は auto/manual のみ走査する。
            // - **safety** (round3): 復元の直前に退避した DB。本 PR では復元時に **その時点専用のアセット safety 控えを必ず
            //   co-create** する (下のワーカー参照) ので、同 timestamp/host の専用ペアを **完全一致** で引く (FindSafetyManifestForBackup)。
            //   時刻 fallback を使わない＝review #1 の「safety を近接世代に誤ペアしてゲームファイルを誤削除」穴を構造的に回避。
            //   これにより safety_db を「復元の取り消し (undo)」したとき、games も当時へ正しく戻る。
            // - **unknown** (v0.20.0 以前の旧フラット形式): 対の控えを持たないため常に DBのみ復元。
            AssetSnapshotInfo pairedSnap = null;
            try
            {
                if (Services.RestorePairingPolicy.IsAssetPairingEligible(entry.TriggerType))
                    pairedSnap = _dbManager.AssetSnapshotService?.FindSnapshotForBackup(entry.StartedAtLocal, entry.PcName);
                else if (string.Equals(entry.TriggerType, "safety", StringComparison.OrdinalIgnoreCase))
                    pairedSnap = _dbManager.AssetSnapshotService?.FindSafetyManifestForBackup(
                        entry.StartedAtLocal.ToString("yyyyMMdd_HHmmss"), entry.PcName);
            }
            catch (Exception ex) { Logger.Warn("[BackupSectionPanel] アセット控えのペアリングに失敗 (DBのみ復元へフォールバック): " + ex.Message); }

            using (var confirm = new RestoreConfirmForm(entry, pairedSnap))
            {
                if (confirm.ShowDialog(this) != DialogResult.Yes)
                {
                    Logger.Info($"[BackupSectionPanel] 復元キャンセル (確認ダイアログ): source='{resolvedPath}'");
                    return;
                }
            }
            // (#250 PR3b round2: ユーザー判断) チェックボックスは廃止し「復元＝一貫時点復元」に一本化。控えのある世代
            // (auto/manual でペアリング成立) なら games/guide も常に戻す。控えが無ければ DBのみ。
            string assetManifestPath = pairedSnap?.ManifestPath;

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
            AssetRestoreResult assetResult = null;
            // (#250 PR3b round3) アセット reconcile は games/ を破壊的に書き換える (削除含む)。DB は RestoreService が
            // safety_*.db に退避＆自動削除する (DefaultSafetyRetentionCount 件) ので、ここでは **games/guide だけ** を
            // safety_*.db と同 timestamp/host の「アセット safety 控え」として退避し、二重退避を避けつつ可逆化する。
            // これで safety_*.db を「復元の取り消し (undo)」したとき、ペアのアセット safety 控えから games も当時へ戻る。
            // 順序: DB 復元 → (現 games を) アセット退避 → reconcile。退避は DB 置換後・reconcile 前なので「キャンセル＝不変」
            // の罠 (round3 #1) は構造的に発生せず、退避自体は非キャンセル (DB 置換済の後戻り不可点以降)。退避が不完全なら
            // 破壊的 reconcile を行わず DBのみ復元へ degrade する (games 無変更=安全)。
            bool assetRetreatAttempted = assetManifestPath != null;
            bool assetRetreatFailed = false;
            // (review round5 #3) 退避で実際にアセット控え(manifest)が書かれたか。live games が空の世代は
            // CreateSnapshot が manifest を書かず Success(ManifestPath=null) を返す＝退避は成立扱いだが undo で games は
            // 戻せない。undo 案内 (undoHint) を「ゲームファイルも戻せる」と過剰約束しないよう、これを真実の源にする。
            bool assetRetreatHasControl = false;
            DialogResult dr;
            using (var dialog = new ProcessingDialog((progress, token) =>
            {
                // (レビュー対応 #1) DB 喪失の最悪ケースは専用例外で捕捉して caller に伝える。re-throw して
                // ProcessingDialog には Abort させる (例外 Message = 具体的な復旧手順がそのまま画面表示される)。
                try { safetyPath = _dbManager.RestoreService.Restore(resolvedPath, progress, token, allowIntegrityWarnings); }
                catch (RestoreDbMissingException dmx) { dbMissing = dmx; throw; }

                // (#250 PR3b round3) DB 置換成功後・reconcile 前に、現 games/guide を safety_*.db と同 ts/host のアセット
                // safety 控えへ退避する (undo 用)。**非キャンセル** (DB 置換済=後戻り不可点以降)。退避が不完全 (退避時刻の
                // 解析失敗 / 列挙失敗 / 部分取得) なら破壊的 reconcile を行わず DBのみ復元へ degrade。games が空 (新規 install
                // 相当=ManifestPath null) の Success は「失うものが無い」ので退避成立扱いで OK (reconcile は削除せずコピーのみ)。
                // (review round4 C-2 既知の非対称) 退避時に live games が空の世代は対のアセット safety 控えが作られないため、
                // 後で safety_*.db を undo 復元しても games は DBのみ復元になり、reconcile で増えた games は live に残る
                // (= 孤児。整合性チェックで検出可能・データ消失ではない)。新規 install 直後に過去世代を復元してすぐ undo する
                // 稀ケースに限る。完全対称化 (空でも空 manifest を書く) は CreateSnapshot 改修を伴うため別 issue (S-1 と併せて)。
                if (assetManifestPath != null)
                {
                    progress?.Report(new ProgressInfo(0, "復元前に現在のゲームファイルを退避中（やり直し用）..."));
                    string retreatTs = Services.RestorePairingPolicy.ParseSafetyTimestamp(safetyPath);
                    var pre = retreatTs != null
                        ? _dbManager.AssetSnapshotService.CreateSnapshot(retreatTs, "safety", progress, CancellationToken.None)
                        : null;
                    bool retreatOk = pre != null && pre.IsSuccess && !pre.IsPartial;
                    if (retreatOk)
                    {
                        // (review round5 #3 / round6 #1) 「実際に undo で games を戻せる控えが書かれた」かを真実の源にする。
                        // ManifestPath != null だけでは不十分: games/ が「存在するが空」(fresh install で EnsureDirectoriesExist が
                        // 作る) と CreateSnapshot は **0 エントリの manifest** を書き ManifestPath != null を返す。この空の控えで
                        // undo すると RestoreFromManifest の空ガード (非空 live を 0 エントリで全消去する暴発防止) に当たり Failed に
                        // なる＝undo で games を戻せない。よって **FileCount > 0** も条件に加え、空退避を「undo 可能」と誤案内しない。
                        assetRetreatHasControl = pre.ManifestPath != null && pre.FileCount > 0;
                        _dbManager.AssetSnapshotService.PruneSafetySnapshots(Services.RestoreService.DefaultSafetyRetentionCount);
                    }
                    else
                    {
                        assetRetreatFailed = true;
                        assetManifestPath = null;
                        Logger.Warn("[BackupSectionPanel] 復元前のゲームファイル退避が不完全のため、安全のため reconcile を行わず DBのみ復元します: "
                            + (pre == null ? "(退避時刻の解析に失敗)" : pre.Message));
                    }
                }

                // (#250 PR3b) アセット復元 (reconcile-in-place)。**非キャンセル (CancellationToken.None)**: DB 置換済の
                // 後戻り不可点以降で、途中キャンセルの「DB は変更されていません」誤報告を避ける (reconcile は冪等)。
                // best-effort で per-file 失敗を throw せず assetResult に集計するため DB 復元の成功判定を覆さない。
                //
                // (review round5 #1) 退避＋reconcile は `RestoreService.Restore` が advisory restore-lock を解放して return した
                // **後**＝lock 外で走る (破壊的 games 反映が lock 外)。**自己発火は構造的に起きない**: この phase 中は
                // ProcessingDialog がモーダルで Manager UI を操作不能＝#295 の操作単位 auto/session バックアップは発火しない
                // (時間トリガは #295 で撤去済)。**別 PC の同時復元**は (1) btnRestore_Click 冒頭の SessionConflictHelper が
                // 「別 Manager 稼働中」を警告、(2) DB phase は restore-lock で相互排他、で抑止する。reconcile phase (lock 外) で
                // 別 PC が restore-lock を取れる窓は残るが、reconcile は pool でなく live のみ書く＝pool 破損には至らず被害は当該
                // PC の当該世代に限定 (別世代/pool は無事)。multi-PC 同時復元は稀＋pre-release で実機確認 (F6)、厳密化は #250 の
                // heartbeat-lease 領域 (将来)。
                if (assetManifestPath != null)
                {
                    // (review #5) DB phase の 100% から進捗が 0 へ戻り、かつアセット phase は中断不可。ここで明示してユーザーの
                    // 「固まった/中止が効かない」誤解を防ぐ (進捗自体は RestoreFromManifest が relpath 付きで更新する)。
                    progress?.Report(new ProgressInfo(0, "ゲームファイルを反映中（この処理は中断できません）"));
                    assetResult = _dbManager.AssetRestoreService.RestoreFromManifest(assetManifestPath, progress, CancellationToken.None);
                }
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

            // (#250 PR3b round3 監査ログ) 復元前退避 (アセット safety 控え) の結果。成功なら safety_*.db とペアの undo 点が
            // 揃った、失敗なら DBのみ degrade。
            if (assetRetreatAttempted)
            {
                if (assetRetreatFailed)
                    Logger.Warn("[BackupSectionPanel] 復元前のゲームファイル退避が不完全 → ゲームファイルは復元せず DBのみ復元 (games 無変更)");
                else
                    Logger.Info($"[BackupSectionPanel] 復元前のゲームファイル退避 完了 (undo 用、safety_*.db とペア): safety='{safetyPath}'");
            }

            // (#250 PR3b 監査ログ) アセット復元を行った場合はその要約も残す (copied/skipped/deleted/failed + 欠落数)。
            if (assetResult != null)
            {
                if (assetResult.IsFailed)
                    Logger.Warn($"[BackupSectionPanel] アセット復元 失敗: manifest='{assetManifestPath}', reason={assetResult.Message}");
                else
                    Logger.Info(
                        $"[BackupSectionPanel] アセット復元 完了: manifest='{assetManifestPath}', " +
                        $"copied={assetResult.CopiedCount}, skipped={assetResult.SkippedCount}, deleted={assetResult.DeletedCount}, " +
                        $"failed={assetResult.FailedCount}, missing_blobs={assetResult.MissingBlobRelPaths.Count}, deletion_suppressed={assetResult.DeletionSuppressed}");
            }

            // (#250 PR3b round2) 退避失敗で DBのみに degrade したことは安全上必ず明示する (ユーザーは DB+ゲームを期待していた)。
            string retreatFailedNote = assetRetreatFailed
                ? "\n\n⚠ ゲームファイルの退避（やり直し用バックアップ）に失敗したため、安全のためゲームファイルは復元していません。"
                + "\nデータベースのみ復元し、ディスク上のゲームファイルは現在のままです。共有サーバーの状態を確認のうえ、もう一度お試しください。"
                : "";

            // (#250 PR3b) アセット復元で「対処が必要」級 (全体失敗 / プール実体欠落) or 軽微な不完全 (per-file 失敗 /
            // 削除抑止) があれば、reconcile がクリーンでもレポートを出して内訳を見せる。
            bool assetHasIssues = assetResult != null &&
                (assetResult.IsFailed || assetResult.IsPartial || assetResult.MissingBlobRelPaths.Count > 0);

            if (reconcile != null && (reconcile.HasAnyFindings || reconcile.AnalysisFailed || assetHasIssues))
            {
                using (var report = new RestoreReportForm(reconcile, safetyPath, postRestore: true, assetResult: assetResult))
                {
                    report.ShowDialog(this.FindForm());
                }
                // レポートには退避失敗の slot が無いので、degrade したときは別ダイアログで必ず知らせる。
                if (assetRetreatFailed)
                    MessageBox.Show(retreatFailedNote.TrimStart(), "ゲームファイルは復元していません",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                // reconcile==null (チェック自体が失敗/skip) でアセット問題があるときの fallback も含め、簡潔通知。
                string assetLine;
                if (assetResult == null)
                    // (review round3 #4) 退避失敗 degrade のときは「整合性に問題なし」を出すと retreatFailedNote
                    // (DBのみ復元した旨) と噛み合わないので、degrade では「DBのみ復元」とだけ言い詳細は note に委ねる。
                    // (review round6 #4) この else は reconcile==null (整合性チェック自体が失敗/skip) も通る。チェックが
                    // 走っていないのに「問題はありませんでした」と断定すると silent pass になるため、reconcile==null は断定しない。
                    assetLine = assetRetreatFailed
                        ? "データベースのみ復元しました。"
                        : reconcile == null
                            ? "（整合性チェックは実行されませんでした。詳細はログを確認してください。）"
                            : "DB とゲームフォルダの整合性に問題はありませんでした。";
                else if (assetHasIssues)
                    // (review #3) reconcile==null で詳細レポートを出せない fallback。件数を併記して UI からも実態を読めるようにする
                    // (どの relpath が欠落したかの一覧はログに出る)。
                    assetLine = $"ゲームファイルの復元で問題がありました（コピー {assetResult.CopiedCount} / 削除 {assetResult.DeletedCount} / 失敗 {assetResult.FailedCount} / 控え欠落 {assetResult.MissingBlobRelPaths.Count}）。詳細はログを確認してください。";
                else
                    assetLine = $"ゲームファイルも復元しました（コピー {assetResult.CopiedCount} / 変更なし {assetResult.SkippedCount} / 削除 {assetResult.DeletedCount}）。";

                // (#250 PR3b round3 / review round5 #3) ゲームファイルも戻したときの「やり直し」案内。undo 点は退避された
                // safety_*.db (= 復元前の DB) で、これを履歴から「復元」すると DB もゲームファイルも復元前へ戻る (ペアのアセット
                // safety 控え経由)。**実際にアセット控えが書かれた (assetRetreatHasControl) ときだけ**「ゲームファイルも戻せる」と
                // 案内する＝空 games 退避 (控え無し) で過剰約束しない。
                string undoHint = (!assetRetreatFailed && assetRetreatHasControl)
                    ? "\n\nやり直したいときは、退避された safety_*.db を履歴から「復元」すると、データベースもゲームファイルも元に戻せます。"
                    : "";

                MessageBox.Show(
                    $"復元が完了しました。\n\n復元前のDBは退避されました:\n{safetyPath}\n\n" +
                    assetLine + "\nManager のデータを再読み込みします。" + undoHint + retreatFailedNote,
                    "復元成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DatabaseChanged?.Invoke();
            RefreshDisplay();
        }

        /// <summary>
        /// (#250 PR2)「整合性チェック」ボタン: 復元を伴わず、現在の DB ↔ games/guide のズレをオンデマンドで突き合わせ、
        /// 同じレポート (RestoreReportForm) を出す。整合性チェックは復元直後にしか走らないため、復元レポートの
        /// 「修正後に再チェック」を Manager 再起動に頼らず正しく行えるようにする。手動なので safetyPath=null /
        /// postRestore=false で「復元完了」等の復元前提の文言を避ける。
        /// </summary>
        private void btnReconcile_Click(object sender, EventArgs e)
        {
            if (_dbManager == null) return;
            RestoreReconciliationResult reconcile;
            // (review #3) Analyze() は全ゲーム×全版に Directory/File.Exists を回す同期処理で、本番は games/guide が
            // SMB 上のため登録数次第で一瞬 UI が固まりうる。最低限 wait カーソルを出す (完全な非同期化は復元直後経路も
            // 同期なため将来課題、SMB 体感は pre-release で確認)。
            Cursor prev = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                reconcile = new Services.RestoreReconciliationService(_dbManager).Analyze();
            }
            catch (Exception ex)
            {
                Logger.Error("[BackupSectionPanel] 整合性チェックに失敗", ex);
                MessageBox.Show("整合性チェックの実行中にエラーが発生しました（詳細はログを確認）",
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor.Current = prev;
            }
            using (var report = new RestoreReportForm(reconcile, safetyPath: null, postRestore: false))
            {
                report.ShowDialog(this.FindForm());
            }
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
