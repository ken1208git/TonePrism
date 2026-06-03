using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#295) データ変更操作の成功直後にバックアップを段取りする coordinator。
    ///
    /// **1 Manager 起動 = 1 自動世代**（replace-in-session）: 同一セッション内で次の操作がバックアップを取るとき、
    /// このセッションが前回書いた自動世代（`.db` + ペア `.manifest`）を消して上書きする。セッション最初の取得は
    /// 前セッションの世代を消さない（＝ retention は「直近 N セッション」を残す）。
    ///
    /// 実バイト書き出しは <see cref="BackupService"/> に委譲し、本クラスは **セッション状態**（この起動の前回
    /// `.db`/`.manifest` パス、メモリ保持）+ **enable gate** + **UI 段取り**を持つ。WinForms 依存をここに閉じ込め、
    /// `BackupService` は UI 非依存に保つ。
    ///
    /// **best-effort**: バックアップは操作のコミット後に走り、失敗しても操作を巻き戻さない（例外を投げず
    /// <see cref="Logger"/> に残すのみ）。
    /// </summary>
    public sealed class SessionBackupCoordinator
    {
        private readonly BackupService _backupService;
        private readonly object _gate = new object();

        /// <summary>
        /// (#295) 操作単位バックアップの完了/失敗を UI (ステータスバー) へ伝える任意コールバック (msg, success)。
        /// MainForm が設定し、UI スレッドへ marshal して表示する。WinForms 型に依存させないため Action で受ける。
        /// DB-only 操作はバックグラウンド実行＝モーダル無しなので、これが唯一の可視 feedback になる。
        /// </summary>
        public Action<string, bool> StatusReporter { get; set; }

        // replace-in-session 状態: この起動で前回書いた自動世代のパス。null = まだこのセッションで未取得。
        private string _sessionAutoDbPath;
        private string _sessionAutoManifestPath;

        public SessionBackupCoordinator(BackupService backupService)
        {
            _backupService = backupService;
        }

        /// <summary>設定の自動バックアップ有効/無効。OFF なら操作単位バックアップを skip（手動は別枠で常に可）。</summary>
        public bool IsEnabled()
        {
            return _backupService.IsAutoBackupEnabled();
        }

        /// <summary>
        /// UI-free のセッションバックアップ。成功したらこのセッションの前回自動世代を消して上書きする。**never throw**。
        /// <paramref name="includeAssets"/>=false の DB-only 操作では games/guide 走査を skip し、既存のアセット世代は
        /// 有効ペアとして温存する（前回 `.manifest` は消さない）。
        /// </summary>
        public BackupResult RunSessionBackup(bool includeAssets, IProgress<ProgressInfo> progress, CancellationToken token)
        {
            // (#295) 設定で自動バックアップ無効なら何もしない (手動は別経路で常に可)。RunAfterOperation でも gate
            // するが、直接 caller のための defense + 単体テスト用。
            if (!IsEnabled()) return BackupResult.Skipped("自動バックアップが無効に設定されています");

            lock (_gate)
            {
                string prevDb = _sessionAutoDbPath;
                string prevManifest = _sessionAutoManifestPath;

                BackupResult result;
                try
                {
                    result = _backupService.RunSessionBackup(includeAssets, progress, token);
                }
                catch (OperationCanceledException)
                {
                    // キャンセルは前回世代を温存（削除しない）。
                    return BackupResult.Skipped("キャンセル");
                }
                catch (Exception ex)
                {
                    Logger.Warn("[SessionBackup] バックアップで予期せぬ例外 (操作自体は成功): " + ex.Message);
                    return BackupResult.Failed(ex.Message);
                }

                if (result != null && result.IsSuccess)
                {
                    // 新しい世代を書けたので、このセッションの前回世代を消す (replace-in-session)。書いてから消す順なので
                    // 「0 世代の瞬間」は無い。
                    // (round3 High) `.manifest` の削除は「**新 manifest を実際に書けたか**」(= includeAssets かつアセット取得が
                    // IsSuccess) で判断する。`result.IsSuccess` は DB の成否のみで、includeAssets=true でもアセット取得が
                    // 失敗/キャンセルだと新 manifest は無い。それなのに前 manifest を消すと、このセッションは「DB はあるが
                    // ゲーム本体の控えが 1 件も無い」状態になり、①で取れていた控えを②の一過性失敗が消してしまう
                    // (round2 #1 が可視化で守ろうとした不変条件を実データ側で破る)。削除と _sessionAutoManifestPath 更新を
                    // 同一述語に揃え、新スナップショット未成功なら前 manifest を温存する。DB-only (includeAssets=false) も同様に温存。
                    bool newManifestWritten = includeAssets && result.AssetSnapshot != null && result.AssetSnapshot.IsSuccess;
                    DeletePreviousGeneration(prevDb, newManifestWritten ? prevManifest : null);
                    _sessionAutoDbPath = result.FilePath;
                    if (newManifestWritten)
                        _sessionAutoManifestPath = result.AssetSnapshot.ManifestPath;
                }
                return result;
            }
        }

        /// <summary>
        /// 各コール地点（データ変更操作の成功直後）が呼ぶ UI ヘルパー。**best-effort**（失敗しても操作は取り消さない）。
        /// <paramref name="assetsChanged"/>=true: 重い games/guide 取得があるので進捗 modal を出す。
        /// =false: DB だけ（サブ秒）なので modal を出さずバックグラウンドで控える（小編集をもたつかせない）。
        /// 設定で無効なら何もしない。
        /// </summary>
        public void RunAfterOperation(IWin32Window owner, bool assetsChanged, string operationLabel)
        {
            if (!IsEnabled()) return;

            if (assetsChanged)
            {
                BackupResult result = null;
                try
                {
                    // ProcessingDialog は worker をバックグラウンドスレッドで回す。RunSessionBackup は never-throw なので
                    // ダイアログが「エラー」ボックス（=操作失敗の誤認）を出すことはない。
                    using (var dialog = new ProcessingDialog((progress, tkn) =>
                    {
                        result = RunSessionBackup(true, progress, tkn);
                    }))
                    {
                        dialog.Text = "変更を保存中（バックアップ）";
                        dialog.ShowDialog(owner);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("[SessionBackup] " + operationLabel + " 後のバックアップ UI で例外 (操作自体は成功): " + ex.Message);
                    return;
                }
                ReportResult(result, operationLabel, assetsRequested: true);
            }
            else
            {
                // DB-only: サブ秒なので UI を出さずバックグラウンドで best-effort。操作はもたつかせない。
                Task.Run(() =>
                {
                    try
                    {
                        ReportResult(RunSessionBackup(false, null, CancellationToken.None), operationLabel, assetsRequested: false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("[SessionBackup] " + operationLabel + " 後の DB バックアップで例外: " + ex.Message);
                    }
                });
            }
        }

        /// <summary>バックアップ結果をログ + ステータスバー (StatusReporter) に反映する。</summary>
        private void ReportResult(BackupResult result, string operationLabel, bool assetsRequested)
        {
            var line = DescribeResult(result, assetsRequested);
            if (line == null) return; // Skipped (DB バックアップが無効 / restore-lock) は何もしない
            if (!line.Value.Ok)
            {
                Logger.Warn("[SessionBackup] " + operationLabel + ": " + line.Value.Message
                    + (result.IsFailed ? " / " + result.Message : "")
                    + (result.AssetSnapshot != null && !result.AssetSnapshot.IsSuccess ? " / ゲーム本体: " + result.AssetSnapshot.Message : ""));
            }
            try { StatusReporter?.Invoke(line.Value.Message, line.Value.Ok); } catch { }
        }

        /// <summary>
        /// (round2 #1) バックアップ結果 → ステータス行 (メッセージ, 成功か)。DB バックアップ自体が Skipped なら null。
        /// **DB 成功でも、ゲーム本体 (games/guide) の控えを要求した操作 (<paramref name="assetsRequested"/>) で控えが
        /// 成功していなければ緑「✓」ではなく警告を返す** (旧 StartAutoBackupIfDue の可視化を移植。round5 #1 / round8 C1 —
        /// SMB 一過性不達・**ユーザーのキャンセル**等でゲーム本体が控えられていないのに「✓ 完全に控えた」と誤認させない)。
        /// 単体テスト用に public static。
        /// </summary>
        public static (string Message, bool Ok)? DescribeResult(BackupResult result, bool assetsRequested)
        {
            if (result == null) return null;
            if (result.IsFailed) return ("⚠ バックアップに失敗しました (変更自体は保存済み)", false);
            if (!result.IsSuccess) return null; // DB バックアップ自体が Skipped
            var snap = result.AssetSnapshot;
            if (snap != null && (snap.IsFailed || snap.IsAnomaly))
                return ("⚠ ゲーム本体のバックアップは取得できませんでした (DB は保存済み)", false);
            if (snap != null && snap.IsPartial)
                return ("⚠ ゲーム本体のバックアップで一部を取得できませんでした (" + snap.SkippedDirCount + " 個スキップ)", false);
            // (round4 #1) ゲーム本体の控えを **要求した** 操作なのに控えが成功していない (キャンセル / 無効 / null =
            // best-effort で取れなかった) なら緑「✓」にしない。`result.IsSuccess` は DB のみの成否で、アセット走査の
            // キャンセル (CreateSnapshot が OCE を握って Skipped を返す) もここに来るため、緑✓潰しの最後の砦。
            if (assetsRequested && (snap == null || !snap.IsSuccess))
                return ("⚠ ゲーム本体のバックアップは完了しませんでした (DB は保存済み)", false);
            return ("✓ 変更をバックアップしました", true);
        }

        private void DeletePreviousGeneration(string dbPath, string manifestPath)
        {
            if (!string.IsNullOrEmpty(dbPath))
            {
                TryDelete(dbPath);
                TryDelete(dbPath + "-wal");
                TryDelete(dbPath + "-shm");
                TryDelete(dbPath + "-journal");
            }
            TryDelete(manifestPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                string safe = FileOperationService.EnsureLongPath(path);
                if (File.Exists(safe)) File.Delete(safe);
            }
            catch (Exception ex)
            {
                // 消せなくても害は小さい（次回 retention / GC が回収する）。
                Logger.Warn("[SessionBackup] 前世代の削除に失敗 (retention/GC が後で回収): " + path + " : " + ex.Message);
            }
        }
    }
}
