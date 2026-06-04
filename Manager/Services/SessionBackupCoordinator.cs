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

        /// <summary>(#299) バックアップ進捗を UI (下部ストリップ) へ伝える任意コールバック。MainForm が設定し UI スレッドへ
        /// marshal する。worker はバックグラウンドスレッドで走るので、これ経由で進捗バー + 現在ファイルを更新する。</summary>
        public Action<ProgressInfo> ProgressReporter { get; set; }

        /// <summary>(#299) バックアップ実行中フラグの変化を UI へ伝える (true=開始→ストリップ表示 / false=終了→非表示)。
        /// MainForm が設定し UI スレッドへ marshal する。</summary>
        public Action<bool> BackupRunningChanged { get; set; }

        // replace-in-session 状態: この起動で前回書いた自動世代のパス。null = まだこのセッションで未取得。
        private string _sessionAutoDbPath;
        private string _sessionAutoManifestPath;

        // (round5 #1) このセッションで「ゲーム本体 (games/guide) の控え」が未完了か (直近のアセット操作が失敗/キャンセル)。
        // true の間は後続の DB-only 成功でも緑「✓」を出さず警告を残す (round2 #3 の sticky 警告が「アセット失敗→以後
        // DB-only 成功」で上書き消失する穴を塞ぐ)。次のアセット操作が成功すると false に戻る (= 回復)。
        // (#299 review round3 #1) **volatile**: getter を `_gate` ロックで読むと、`_gate` を ~6GB 走査の間ずっと保持する
        // RunSessionBackup と競合し、UI スレッド (BackupRunningChanged コールバックで読む) が次 worker の走査完了まで
        // フリーズする (= 非ブロッキング化が自壊する)。単一 bool で他状態と原子的に読む必要は無いため、書込は `_gate`
        // 下のまま (volatile 書込)、読みは lock-free な volatile 読みにして UI を `_gate` から切り離す。
        private volatile bool _sessionAssetCaptureFailed;

        // (#299) 非ブロッキング worker の状態。`_gate` は RunSessionBackup が実行中ずっと保持するため、コアレス判定を
        // UI スレッドから待たせず行えるよう **別ロック** (`_workerLock`) で管理する。
        private readonly object _workerLock = new object();
        private bool _workerRunning;          // バックグラウンド worker が稼働中か
        private bool _dirty;                  // worker 実行中に新たな変更操作が来た (= 終了後にもう 1 回コアレス実行)
        private bool _pendingIncludeAssets;   // 次の (コアレス) 実行で includeAssets にすべきか (OR で蓄積)
        private CancellationTokenSource _cts; // 現在の実行のキャンセル用 (中止ボタン / 閉じる時)

        public SessionBackupCoordinator(BackupService backupService)
        {
            _backupService = backupService;
        }

        /// <summary>(round5 #1) このセッションでゲーム本体の控えが未完了 (直近のアセット操作が失敗/キャンセル) か。
        /// true の間は DB-only 成功でも緑✓を出さず警告を残す。次のアセット操作成功で false に戻る。単体テスト用に公開。</summary>
        public bool SessionAssetCaptureFailed { get { return _sessionAssetCaptureFailed; } } // (review round3 #1) volatile 読み (lock-free。UI を _gate から切り離す)

        /// <summary>(#299) バックアップ worker が稼働中か。MainForm の FormClosing が「中止して閉じますか？」確認に使う。</summary>
        public bool IsBackupRunning { get { lock (_workerLock) { return _workerRunning; } } }

        /// <summary>(#299) 進行中のバックアップをキャンセルする (下部ストリップの「中止」/ 閉じる確認の「中止して閉じる」)。
        /// キャンセルしても操作 (DB commit) は巻き戻らない＝バックアップだけ後回し。round5 で「未バックアップ」警告が残り、
        /// 次のアセット操作 or 「今すぐバックアップ」で回復する。</summary>
        public void CancelCurrentBackup()
        {
            lock (_workerLock)
            {
                // (#299 review B-1) 「中止」はコアレス済みの pending 分も含めて止める。dirty を残すと現 iteration の
                // キャンセル直後に TryContinue が次 iteration を新しい cts で起動し、ストリップが消えず ~6GB 再走査が
                // 走って「中止」の意図 (SMB 混雑時に今は止めたい等) に反する。新たな変更が来れば次操作で再 trigger される。
                // (#299 review round3 #6) DB-only 実行中にアセット変更が pending でコアレスされている状態で中止すると、現 run は
                // includeAssets=false で OCE するため失敗フラグが立たず、破棄される pending アセット変更が「未バックアップ」警告も
                // 復旧ボタンも出ないまま落ちる。pending にアセットが残っていたら失敗フラグを立て、次の BackupRunningChanged(false)
                // で「⚠ ゲームファイルが未バックアップ」+「今すぐバックアップ」を出させる (volatile 書込なので _gate 不要)。
                if (_pendingIncludeAssets) _sessionAssetCaptureFailed = true;
                _dirty = false;
                _pendingIncludeAssets = false;
                try { _cts?.Cancel(); } catch { }
            }
        }

        // (#299) コアレスの状態遷移。スレッド無しで単体テストできるよう worker ループから切り出した 2 メソッド。
        /// <summary>idle なら worker を「稼働中」にして今回走らせる includeAssets を返す (true)。既に稼働中なら dirty を立てて
        /// コアレス (false)。要求の includeAssets は OR で蓄積する (=「途中で 1 回でもアセット変更が来たら次はアセット込み」)。</summary>
        internal bool TryStartRun(bool requestIncludeAssets, out bool runIncludeAssets)
        {
            lock (_workerLock)
            {
                _pendingIncludeAssets |= requestIncludeAssets;
                if (_workerRunning) { _dirty = true; runIncludeAssets = false; return false; }
                _workerRunning = true;
                runIncludeAssets = _pendingIncludeAssets; _pendingIncludeAssets = false;
                return true;
            }
        }

        /// <summary>1 回の実行が終わった worker が呼ぶ。実行中に変更が来ていた (dirty) なら蓄積分でもう 1 回走る (true)、
        /// 無ければ「稼働中」を下ろして停止 (false)。これで「最後の変更の後に必ず 1 回走る」不変条件を満たす。</summary>
        internal bool TryContinue(out bool runIncludeAssets)
        {
            lock (_workerLock)
            {
                if (_dirty)
                {
                    _dirty = false;
                    runIncludeAssets = _pendingIncludeAssets; _pendingIncludeAssets = false;
                    return true;
                }
                _workerRunning = false; runIncludeAssets = false;
                return false;
            }
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
                    // (round6 High) この直後に DeletePreviousGeneration で消す前世代 (prevDb / prevManifest) を渡し、
                    // RunBackupCore 内の retention 母数から除外させる (= 直近 N セッション保持が複数操作で崩れない)。
                    result = _backupService.RunSessionBackup(includeAssets, progress, token, prevDb, prevManifest);
                }
                catch (OperationCanceledException)
                {
                    // キャンセルは前回世代を温存（削除しない）。アセット操作のキャンセル = ゲーム本体は未控え (round5 #1)。
                    if (includeAssets) _sessionAssetCaptureFailed = true;
                    return BackupResult.Skipped("キャンセル");
                }
                catch (Exception ex)
                {
                    if (includeAssets) _sessionAssetCaptureFailed = true;
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
                // (round5 #1) このセッションのゲーム本体控え健全性を更新。アセット操作 (includeAssets) で控えが成功して
                // いなければ「未控え」を記録し、後続の DB-only 成功が緑✓で警告を埋もれさせないようにする。DB-only
                // (includeAssets=false) は games/ を変えないので、直前のアセット世代の健全性をそのまま引き継ぐ (flag 不変)。
                // (round6 Medium #3) restore-lock 等の延期 (IsSkipped=true) は「試行していない」ので flag を触らない
                // (誤って「ゲーム本体未控え」警告を後続に出さない。延期自体は IsDeferred 経由で別途通知される)。
                if (includeAssets && result != null && !result.IsSkipped)
                    _sessionAssetCaptureFailed = !(result.IsSuccess
                        && result.AssetSnapshot != null && result.AssetSnapshot.IsSuccess);
                return result;
            }
        }

        /// <summary>
        /// 各コール地点（データ変更操作の成功直後）が呼ぶ UI ヘルパー。**best-effort**（失敗しても操作は取り消さない）。
        /// (#299) **非ブロッキング**: モーダルは出さず、バックグラウンド worker にバックアップを enqueue して即 return する
        /// (操作を続けられる)。進捗は下部ストリップ (<see cref="ProgressReporter"/>) に出る。実行中に来た変更は **コアレス**
        /// され、現行バックアップ完了後に蓄積分でもう 1 回だけ走る (CAS + replace-in-session で最終世代は必ず正しい)。
        /// <paramref name="assetsChanged"/>=true: 重い games/guide 取得を含める。=false: DB だけ。
        /// 設定で無効なら何もしない。<paramref name="owner"/> はモーダル廃止で未使用 (call site 互換のため残置)。
        /// </summary>
        public void RunAfterOperation(IWin32Window owner, bool assetsChanged, string operationLabel)
        {
            if (!IsEnabled()) return;
            EnqueueBackup(assetsChanged, operationLabel);
        }

        /// <summary>(#299) バックアップを enqueue。idle なら worker 起動、実行中ならコアレス (TryStartRun)。**never block**。</summary>
        private void EnqueueBackup(bool includeAssets, string label)
        {
            if (!TryStartRun(includeAssets, out bool runIncludeAssets)) return; // 実行中の worker がコアレスで拾う
            try { Task.Run(() => WorkerLoop(runIncludeAssets, label)); }
            catch (Exception ex)
            {
                // (#299 review D-1) TryStartRun が _workerRunning=true を立てた後に Task.Run が投げると (スレッドプール枯渇等)
                // WorkerLoop が一度も走らずフラグが永久 true で固まる → 以降の自動バックアップが無言で停止 + IsBackupRunning が
                // 常時 true で FormClosing が毎回「中止して閉じますか？」を誤発火する。稼働中フラグを巻き戻す。
                lock (_workerLock) { _workerRunning = false; }
                Logger.Warn("[SessionBackup] worker の起動に失敗 (稼働フラグを巻き戻し): " + ex.Message);
            }
        }

        /// <summary>(#299) 単一バックグラウンド worker。dirty が立つ限りコアレスで繰り返し、無くなったら停止。
        /// RunSessionBackup は never-throw だが念のため worker 全体も握って UI へ飛ばさない。</summary>
        private void WorkerLoop(bool includeAssets, string label)
        {
            try
            {
                try { BackupRunningChanged?.Invoke(true); } catch { } // ストリップ表示 (MainForm が UI thread へ marshal)
                do
                {
                    CancellationTokenSource cts;
                    lock (_workerLock) { cts = _cts = new CancellationTokenSource(); }
                    BackupResult result;
                    try
                    {
                        var progress = new ActionProgress(p => { try { ProgressReporter?.Invoke(p); } catch { } });
                        result = RunSessionBackup(includeAssets, progress, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("[SessionBackup] " + label + " 後のバックアップで例外: " + ex.Message);
                        result = BackupResult.Failed(ex.Message);
                    }
                    finally
                    {
                        lock (_workerLock) { if (_cts == cts) _cts = null; }
                        try { cts.Dispose(); } catch { }
                    }
                    ReportResult(result, label, assetsRequested: includeAssets);
                }
                while (TryContinue(out includeAssets));
            }
            catch (Exception ex)
            {
                Logger.Warn("[SessionBackup] worker loop 例外: " + ex.Message);
                lock (_workerLock) { _workerRunning = false; } // 稼働中フラグが立ったまま固まらないよう保険
            }
            finally
            {
                try { BackupRunningChanged?.Invoke(false); } catch { } // ストリップ非表示
            }
        }

        /// <summary>(#299) Action を IProgress にする薄いアダプタ。worker (バックグラウンド) で生成しても marshal は
        /// ProgressReporter 実装側 (MainForm) が行うため、ここでは単に呼ぶだけ。</summary>
        private sealed class ActionProgress : IProgress<ProgressInfo>
        {
            private readonly Action<ProgressInfo> _a;
            public ActionProgress(Action<ProgressInfo> a) { _a = a; }
            public void Report(ProgressInfo value) { _a(value); }
        }

        /// <summary>バックアップ結果をログ + ステータスバー (StatusReporter) に反映する。</summary>
        private void ReportResult(BackupResult result, string operationLabel, bool assetsRequested)
        {
            var line = DescribeResult(result, assetsRequested);
            if (line == null) return; // Skipped (DB バックアップが無効 / restore-lock) は何もしない
            string message = line.Value.Message;
            bool ok = line.Value.Ok;
            // (round5 #1) このセッションでゲーム本体の控えが未完了 (失敗/キャンセル) のままなら、後続の DB-only 成功でも
            // 緑「✓」を出さず警告を残す。round2 #3 の sticky 警告が「アセット失敗→以後 DB-only 成功」で緑に上書き消失する
            // 穴を塞ぐ。これにより未控えの間は毎操作で警告が再表示され、運営が見落とせない。回復は次のアセット操作成功時。
            if (ok && SessionAssetCaptureFailed)
            {
                ok = false;
                message = "⚠ ゲームファイルのバックアップが未完了です（DB は保存済み。ゲーム操作をやり直すか保存先を確認してください）";
            }
            if (!ok)
            {
                Logger.Warn("[SessionBackup] " + operationLabel + ": " + message
                    + (result.IsFailed ? " / " + result.Message : "")
                    + (result.AssetSnapshot != null && !result.AssetSnapshot.IsSuccess ? " / ゲームファイル: " + result.AssetSnapshot.Message : ""));
            }
            try { StatusReporter?.Invoke(message, ok); } catch { }
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
            // (round6 Medium #3) restore-lock 等で延期した Skipped は完全 silent にせず警告で知らせる
            // (「変更は保存したがまだ控えていない」= ユーザーが復元完了後に再操作する判断材料になる)。
            if (result.IsDeferred) return ("⚠ " + result.Message, false);
            if (!result.IsSuccess)
            {
                // (round7 #3) ゲーム本体の控えを **要求した** 操作が DB フェーズ中 (進捗 0-10%) にキャンセルされると
                // RunBackupCore が OCE を投げ top-level Skipped("キャンセル") になる (round4 が塞いだのは「DB 成功後の
                // アセット走査キャンセル」で別経路)。完全 silent だと「変更はあるが今セッション未控え」が無表示で閉じうる
                // ため、1 回警告を出す (replace-in-session で次操作が再控えするが、それまでの可視化として)。round5 flag も
                // 立つので後続も再警告される。DB-only (assetsRequested=false) の Skipped は従来どおり沈黙 (無効/no-op 想定)。
                if (assetsRequested) return ("⚠ 変更はまだバックアップされていません（中断されました）", false);
                return null;
            }
            var snap = result.AssetSnapshot;
            if (snap != null && (snap.IsFailed || snap.IsAnomaly))
                return ("⚠ ゲームファイルのバックアップは取得できませんでした (DB は保存済み)", false);
            if (snap != null && snap.IsPartial)
                return ("⚠ ゲームファイルのバックアップで一部を取得できませんでした (" + (snap.SkippedDirCount + snap.SkippedFileCount) + " 個スキップ)", false);
            // (round4 #1) ゲーム本体の控えを **要求した** 操作なのに控えが成功していない (キャンセル / 無効 / null =
            // best-effort で取れなかった) なら緑「✓」にしない。`result.IsSuccess` は DB のみの成否で、アセット走査の
            // キャンセル (CreateSnapshot が OCE を握って Skipped を返す) もここに来るため、緑✓潰しの最後の砦。
            if (assetsRequested && (snap == null || !snap.IsSuccess))
                return ("⚠ ゲームファイルのバックアップは完了しませんでした (DB は保存済み)", false);
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
