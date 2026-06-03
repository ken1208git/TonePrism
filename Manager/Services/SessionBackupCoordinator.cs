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
                    // 新しい世代を書けたので、このセッションの前回世代を消す (replace-in-session)。
                    // 書いてから消す順なので「0 世代の瞬間」は無い。DB-only 操作 (includeAssets=false) は前回 `.db` だけ
                    // 消し、前回 `.manifest` は温存する（既存アセット世代が新 `.db` と有効ペアになる、R3）。
                    DeletePreviousGeneration(prevDb, includeAssets ? prevManifest : null);
                    _sessionAutoDbPath = result.FilePath;
                    if (includeAssets && result.AssetSnapshot != null && result.AssetSnapshot.IsSuccess)
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
                ReportResult(result, operationLabel);
            }
            else
            {
                // DB-only: サブ秒なので UI を出さずバックグラウンドで best-effort。操作はもたつかせない。
                Task.Run(() =>
                {
                    try
                    {
                        ReportResult(RunSessionBackup(false, null, CancellationToken.None), operationLabel);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("[SessionBackup] " + operationLabel + " 後の DB バックアップで例外: " + ex.Message);
                    }
                });
            }
        }

        /// <summary>バックアップ結果をログ + ステータスバー (StatusReporter) に反映する。Skipped は何もしない。</summary>
        private void ReportResult(BackupResult result, string operationLabel)
        {
            if (result == null) return;
            var reporter = StatusReporter;
            if (result.IsSuccess)
            {
                try { reporter?.Invoke("✓ 変更をバックアップしました", true); } catch { }
            }
            else if (result.IsFailed)
            {
                Logger.Warn("[SessionBackup] " + operationLabel + " 後のバックアップに失敗 (操作自体は成功): " + result.Message);
                try { reporter?.Invoke("⚠ バックアップに失敗しました (変更自体は保存済み)", false); } catch { }
            }
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
