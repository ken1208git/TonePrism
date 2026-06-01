using System;
using System.IO;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#209) 個別ゲームバージョンの削除を「フォルダ物理削除 + DB 行削除 + アクティブ版付け替え」まで
    /// 一貫して行うオーケストレーション。EditGameForm を太らせない (#242 god-file 対策、AGENTS「ロジックは外へ」)
    /// ため UI から分離した純ロジックとして切り出す。UI ダイアログ (確認 / 進捗 / 結果通知) は呼び出し側が持つ。
    ///
    /// 既存の「ゲームごと削除」(GameSectionPanel.btnDeleteGame_Click) と同じ 3-phase + rollback 方針:
    ///   Phase 1: 版フォルダ <install>/games/{gameId}/v{version}/ を `.pending-delete-{GUID}` にリネーム退避。
    ///            Launcher がその版をプレイ中だとファイルロックで失敗しうる → FolderLocked を返す (DB 未変更)。
    ///   Phase 2: DB から版行を削除 + active 付け替え (DatabaseManager.DeleteGameVersionAndReassignActive、1 transaction)。
    ///            失敗時は退避フォルダを元に戻す (rollback)。
    ///   Phase 3: 退避フォルダを物理削除 (best-effort、FolderDeletionService)。失敗しても DB は確定済なので
    ///            PhysicalDeleteDeferred を返し、呼び出し側が「後で手動削除」を案内する。
    ///
    /// 「最後の 1 版は削除不可」は呼び出し側 (UI) がガードする契約。
    /// </summary>
    public static class GameVersionDeletionService
    {
        public enum Outcome
        {
            /// <summary>版を削除し、退避フォルダの物理削除まで完了。</summary>
            Success,
            /// <summary>フォルダ退避リネームに失敗 (Launcher 使用中等)。DB は未変更、再試行可。</summary>
            FolderLocked,
            /// <summary>DB 削除に失敗。退避フォルダは元に戻した (DB・フォルダとも削除前の状態)。</summary>
            DbFailedRolledBack,
            /// <summary>DB 削除に失敗し、退避フォルダの復元にも失敗 (手動復旧が必要)。</summary>
            DbFailedRollbackAlsoFailed,
            /// <summary>DB 削除は成功したが退避フォルダの物理削除に失敗 (orphan が残る、手動削除を案内)。</summary>
            PhysicalDeleteDeferred,
        }

        public sealed class Result
        {
            public Outcome Outcome { get; set; }
            /// <summary>削除後の games.version (= アクティブ版数文字列、DB の値)。Success / PhysicalDeleteDeferred 時に有効。</summary>
            public string NewActiveVersion { get; set; }
            /// <summary>失敗時の例外 (FolderLocked / DbFailed* / PhysicalDeleteDeferred)。</summary>
            public Exception Error { get; set; }
            /// <summary>退避フォルダの実パス (DbFailedRollbackAlsoFailed / PhysicalDeleteDeferred 時の手動復旧案内用)。</summary>
            public string PendingFolderPath { get; set; }
        }

        /// <summary>
        /// 指定ゲームの指定バージョンを削除する。スレッドをブロックする (フォルダ I/O + DB)。
        /// 呼び出し側は ProcessingDialog のワーカー等から呼ぶこと。
        /// </summary>
        /// <param name="db">DatabaseManager。</param>
        /// <param name="gameId">ゲーム ID。</param>
        /// <param name="versionId">削除する game_versions.id。</param>
        /// <param name="versionFolder">削除する版フォルダの絶対パス (`games/&lt;gameId&gt;/v&lt;version&gt;/`)。呼び出し側が
        /// PathManager.GetVersionFolder で解決して渡す (= 本サービスを PathManager 非依存にしてテスト可能にする)。
        /// disk 上のフォルダ名は DB/disk 確定の版数なので、フォーム上の pending リネーム版数でなく確定版数で解決すること。</param>
        public static Result Delete(DatabaseManager db, string gameId, int versionId, string versionFolder)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (string.IsNullOrEmpty(versionFolder)) throw new ArgumentException("versionFolder が空です。", nameof(versionFolder));

            string pendingFolder = versionFolder + ".pending-delete-" + Guid.NewGuid().ToString("N");
            bool renamed = false;

            // ===== Phase 1: フォルダ退避リネーム =====
            if (Directory.Exists(versionFolder))
            {
                try
                {
                    Directory.Move(versionFolder, pendingFolder);
                    renamed = true;
                }
                catch (DirectoryNotFoundException)
                {
                    // race: check と Move の間に他プロセスが消した。既に無いので退避不要で続行。
                    renamed = false;
                }
                catch (Exception ex)
                {
                    Logger.Warn("[GameVersionDeletionService] (#209) 版フォルダの退避に失敗 (Launcher 使用中の疑い): "
                        + versionFolder + ": " + ex.Message);
                    return new Result { Outcome = Outcome.FolderLocked, Error = ex };
                }
            }
            else
            {
                // フォルダが元々無い (通常運用で前版フォルダが残らない設定 / 既に手動削除済 等)。DB だけ消す。
                Logger.Info("[GameVersionDeletionService] (#209) 版フォルダが存在しないため DB のみ削除: " + versionFolder);
            }

            // ===== Phase 2: DB 削除 + active 付け替え (失敗時はフォルダを戻す) =====
            string newActive;
            try
            {
                newActive = db.DeleteGameVersionAndReassignActive(gameId, versionId);
            }
            catch (Exception ex)
            {
                Logger.Error("[GameVersionDeletionService] (#209) DB 版削除に失敗: gameId=" + gameId + " versionId=" + versionId, ex);
                if (renamed && Directory.Exists(pendingFolder) && !Directory.Exists(versionFolder))
                {
                    try
                    {
                        Directory.Move(pendingFolder, versionFolder); // rollback
                    }
                    catch (Exception rbEx)
                    {
                        Logger.Error("[GameVersionDeletionService] (#209) rollback (退避フォルダ復元) にも失敗: "
                            + pendingFolder + " → " + versionFolder, rbEx);
                        return new Result { Outcome = Outcome.DbFailedRollbackAlsoFailed, Error = ex, PendingFolderPath = pendingFolder };
                    }
                }
                else if (renamed && Directory.Exists(versionFolder))
                {
                    // race: 元の場所が他プロセスで再作成された。安全に戻せない。
                    Logger.Error("[GameVersionDeletionService] (#209) rollback 先が既に存在し復元不可: " + versionFolder);
                    return new Result { Outcome = Outcome.DbFailedRollbackAlsoFailed, Error = ex, PendingFolderPath = pendingFolder };
                }
                return new Result { Outcome = Outcome.DbFailedRolledBack, Error = ex };
            }

            // ===== Phase 3: 退避フォルダの物理削除 (best-effort) =====
            if (renamed)
            {
                FolderDeletionService.Result del = FolderDeletionService.TryDelete(pendingFolder);
                if (!del.Success)
                {
                    Logger.Warn("[GameVersionDeletionService] (#209) DB 削除済だが退避フォルダの物理削除に失敗 (orphan 残存): "
                        + pendingFolder + ": " + (del.LastError != null ? del.LastError.Message : "(不明)"));
                    return new Result
                    {
                        Outcome = Outcome.PhysicalDeleteDeferred,
                        NewActiveVersion = newActive,
                        Error = del.LastError,
                        PendingFolderPath = pendingFolder,
                    };
                }
            }

            return new Result { Outcome = Outcome.Success, NewActiveVersion = newActive };
        }
    }
}
