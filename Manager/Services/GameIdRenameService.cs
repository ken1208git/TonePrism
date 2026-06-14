using System;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#242 ③ ゲーム登録フォーム WPF 化) ゲーム ID 変更に伴う games フォルダの rename + DB 更新を
    /// EditGameForm.btnOK_Click から抽出した service。元は god-method 直書きの重ロジック
    /// (CLAUDE.md「UI は薄く、ロジックは外へ」)。挙動は完全保存。
    ///
    /// 2 段構成:
    ///   <see cref="DecideCollision"/> (副作用は dirExists のみ) = 旧/新フォルダの存在から衝突 3 経路を判定。
    ///     (a) 両方存在 = 真の衝突 / (b) 旧不在+新存在 = recovery 確認要 / (c) それ以外 = 通常。
    ///   <see cref="Execute"/> (disk Move + DB) = 旧フォルダがあれば Move → UpdateGameId、DB 失敗時は Move 戻し。
    ///     ProcessingDialog の worker から呼ぶ前提で <see cref="ProgressInfo"/> を report。失敗時は throw
    ///     (caller の worker が rethrow → 外側 catch でエラー表示)。
    ///
    /// (b) recovery 確認の MessageBox と ProcessingDialog の表示は **caller (form) の責務**。本 service は
    /// 判定と disk/DB 操作のみで UI を持たない。disk 操作 / DB 更新は ctor で注入可能 (既定 = System.IO / DatabaseManager)
    /// → 単体テストで fake を差し込み、衝突判定・Move・DB 失敗時 rollback を検証する。
    /// </summary>
    public class GameIdRenameService
    {
        private readonly Func<string, bool> _dirExists;
        private readonly Action<string, string> _move;
        private readonly Action<string, string> _updateGameId;

        /// <summary>本番用。<paramref name="db"/> の UpdateGameId を使う。disk 操作も既定 System.IO。</summary>
        public GameIdRenameService(DatabaseManager db, Func<string, bool> dirExists = null, Action<string, string> move = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            _dirExists = dirExists ?? System.IO.Directory.Exists;
            _move = move ?? System.IO.Directory.Move;
            _updateGameId = db.UpdateGameId;
        }

        /// <summary>単体テスト用。DB 更新 / disk 操作をすべて fake で差し込む。</summary>
        internal GameIdRenameService(Action<string, string> updateGameId, Func<string, bool> dirExists, Action<string, string> move)
        {
            _updateGameId = updateGameId ?? throw new ArgumentNullException(nameof(updateGameId));
            _dirExists = dirExists ?? throw new ArgumentNullException(nameof(dirExists));
            _move = move ?? throw new ArgumentNullException(nameof(move));
        }

        public enum CollisionDecision
        {
            /// <summary>(c) 通常経路。Execute へ進む (旧フォルダがあれば Move、無ければ DB のみ更新)。</summary>
            Proceed,
            /// <summary>(a) 旧・新フォルダ両方存在 = Move 不能の真の衝突。caller は中止 (例外) する。</summary>
            Collision,
            /// <summary>(b) 旧不在 + 新存在 = 前回 rename 中断の残骸 or 手動作成。caller は recovery 可否を user 確認する。</summary>
            NeedsRecoveryConfirm
        }

        /// <summary>
        /// 旧/新 games フォルダの存在から衝突 3 経路 (#158 round 7 L-4 + round 8 codex P2) を判定する。
        /// </summary>
        public CollisionDecision DecideCollision(string oldFolder, string newFolder)
        {
            bool oldExists = _dirExists(oldFolder);
            bool newExists = _dirExists(newFolder);
            if (oldExists && newExists) return CollisionDecision.Collision;
            if (!oldExists && newExists) return CollisionDecision.NeedsRecoveryConfirm;
            return CollisionDecision.Proceed;
        }

        /// <summary>
        /// 旧フォルダが存在すれば <paramref name="newFolder"/> へ Move し、DB の gameId を更新する。DB 更新が失敗したら
        /// Move を元に戻して例外を rethrow する (= caller の ProcessingDialog worker が捕捉し、外側 catch でエラー表示)。
        /// 旧フォルダ不在 (= (b) recovery で user が既存使用を選んだ等) は Move を skip して DB のみ更新する。
        /// </summary>
        /// <returns>実際にフォルダを物理 Move したか (caller は true のとき AssetsChangedOnDisk を立てる)。</returns>
        public bool Execute(string oldGameId, string newGameId, string oldFolder, string newFolder, IProgress<ProgressInfo> progress)
        {
            progress?.Report(new ProgressInfo(-1, "フォルダをリネーム中...", $"{oldFolder} → {newFolder}"));

            bool folderRenamed = false;
            if (_dirExists(oldFolder))
            {
                _move(oldFolder, newFolder);
                folderRenamed = true;
            }

            progress?.Report(new ProgressInfo(-1, "データベースを更新中...", $"ゲームID: {oldGameId} → {newGameId}"));
            try
            {
                _updateGameId(oldGameId, newGameId);
            }
            catch
            {
                // DB 更新失敗時はフォルダを元に戻す (disk と DB の片側だけ進んだ drift を防ぐ)。
                if (folderRenamed)
                {
                    progress?.Report(new ProgressInfo(-1, "ロールバック中...", "フォルダを元の名前に戻しています"));
                    _move(newFolder, oldFolder);
                }
                throw;
            }
            return folderRenamed;
        }
    }
}
