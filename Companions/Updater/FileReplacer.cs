using System;
using System.IO;

namespace GCTonePrism.Updater
{
    /// <summary>
    /// `Manager/` dir 単位の rename-rollback 置換。
    ///
    /// SPEC §3.7.4 [責務 3] + §3.7.3 失敗時のロールバック: dir 全体を一度 .bak リネームし、新ファイルを
    /// 元 dir に展開、成功なら .bak 削除、失敗なら .bak から復元する atomic 戦略。
    ///
    /// なぜ dir 単位 atomic か:
    ///   - Manager 関連は `GCTonePrism_Manager.exe` + 多数の DLL (System.Data.SQLite, Microsoft.WindowsAPICodePack
    ///     等) で構成されていて、個別ファイル単位の置換だと「.exe は新、DLL は旧」の半分置換状態で起動して
    ///     ロード失敗する path がある
    ///   - dir rename は Windows API レベルで atomic (NTFS の MFT エントリ更新 1 回)、部分失敗が起こりにくい
    ///   - 失敗時 .bak から rename 戻すだけで元の状態に戻る、シンプル
    ///
    /// ユーザーデータ保護 (`prism.db` / `games/` / `backups/` / `responses/` / `logs/`):
    ///   SPEC §3.7.3 「保護の仕組み」で **構造的保護** として定義されている。実態:
    ///   - user data は `<install>/` 直下に配置される (例: `<install>/prism.db`、§7.5.1)
    ///   - **`<install>/Manager/` の中ではない** ため、Manager dir の置換と物理的に無関係に維持される
    ///   - `.bak` は **binary atomic rollback 用** であって user data 保護とは別仕組み
    ///   - 従って本 FileReplacer は user data の carry-over ロジックを持たない (= 持つ必要がない、
    ///     `<install>/Manager/` の中に user data は元々存在しないから)
    /// </summary>
    internal static class FileReplacer
    {
        /// <summary>
        /// rename-rollback 方式で Manager dir を置換する (Step 1: rename + Step 2: copy のみ)。
        ///
        /// シニアレビュー round 1 H1 対応: 旧実装は `.bak` 削除も同関数内で行っていたが、
        /// caller (Program.cs) が restart-exe 存在検証を行う前に `.bak` が消えてしまい、
        /// release packaging bug 等で新 target に restart-exe が無いケースで「旧 Manager 消失 +
        /// 新 Manager 不在」の復旧不能 broken state に陥っていた。Replace を Step 1/2 のみに
        /// 絞り、`.bak` 削除は caller が `restart-exe` 検証 OK 後に <see cref="CleanupBak"/> で
        /// 明示的に呼ぶ形に分離。Step 2 失敗時は Replace 内で自動 Rollback、検証段階の失敗は
        /// caller が <see cref="RollbackFromBak"/> を呼んで `.bak` から復元できる API に。
        /// </summary>
        /// <param name="stagingDir">staging dir のルート (中の `files/Manager/` をソースに使う)</param>
        /// <param name="managerTargetDir">置換先の既存 Manager dir</param>
        /// <returns>true: Step 1/2 成功 (`.bak` は target+".bak" に存在、caller が CleanupBak を呼ぶ責務) / false: 失敗 (rollback 実施済 = 旧 Manager が復元されている)</returns>
        /// <exception cref="InvalidOperationException">rollback にも失敗した致命的状態</exception>
        public static bool Replace(string stagingDir, string managerTargetDir)
        {
            string stagingManagerSrc = Path.Combine(stagingDir, "files", "Manager");
            if (!Directory.Exists(stagingManagerSrc))
            {
                Logger.Error($"staging の Manager ソースが見つかりません: {stagingManagerSrc}");
                return false;
            }

            string parentDir = Path.GetDirectoryName(managerTargetDir);
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                Logger.Error($"manager-target の親 dir が存在しません: {parentDir}");
                return false;
            }

            string bakDir = managerTargetDir.TrimEnd('\\', '/') + ".bak";
            // 過去 run の残骸 .bak を先に掃除
            if (Directory.Exists(bakDir))
            {
                Logger.Info($"既存の .bak を削除: {bakDir}");
                try
                {
                    Directory.Delete(bakDir, recursive: true);
                }
                catch (Exception ex)
                {
                    Logger.Error($"既存 .bak の削除に失敗: {ex.Message}");
                    return false;
                }
            }

            // [Replace 1/2] target → .bak リネーム (rollback 元のスナップショット作成)
            // シニアレビュー round 1 M2: Program.cs の outer Step と語彙衝突しないよう、inner は
            // [Replace X/Y] のラベルを使う。outer = Program.cs の [Step 1/3] (Manager 待機) /
            // [Step 2/3] (本 Replace 呼出し) / [Step 3/3] (起動)、inner = 本 FileReplacer の
            // [Replace 1/2] (rename) / [Replace 2/2] (copy)、CleanupBak 内は単独メッセージ。
            bool targetExisted = Directory.Exists(managerTargetDir);
            if (targetExisted)
            {
                Logger.Info($"[Replace 1/2] 既存 Manager dir を .bak にリネーム");
                Logger.Info($"  {managerTargetDir} → {bakDir}");
                try
                {
                    Directory.Move(managerTargetDir, bakDir);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Replace 1/2 失敗 (rename): {ex.Message}");
                    return false;
                }
            }
            else
            {
                Logger.Warn($"target dir が存在しません (新規インストール扱い): {managerTargetDir}");
            }

            // [Replace 2/2] staging → target へ copy
            Logger.Info($"[Replace 2/2] staging から新 Manager dir をコピー");
            Logger.Info($"  {stagingManagerSrc} → {managerTargetDir}");
            try
            {
                CopyDirectory(stagingManagerSrc, managerTargetDir);
            }
            catch (Exception ex)
            {
                Logger.Error($"Replace 2/2 失敗 (copy): {ex.Message}");
                // rollback (Step 1 で作った .bak から復元)
                Rollback(managerTargetDir, bakDir, targetExisted);
                return false;
            }

            // NOTE: .bak 削除はここでは行わない (round 1 H1 修正)。caller が restart-exe 検証 OK
            // 後に CleanupBak を呼ぶ責務。検証失敗時は RollbackFromBak で .bak から復元する。
            // user data は <install>/ 直下にあり Manager dir の外なので、.bak の中に user data は
            // 入っていない (SPEC §3.7.3 「保護の仕組み」参照)。`.bak` は binary rollback 用の
            // 一時的スナップショット。

            Logger.Info("Manager dir 置換完了 (Step 1/2 + 2/2、.bak は CleanupBak まで保持)");
            return true;
        }

        /// <summary>
        /// `.bak` を削除する (best-effort)。caller (Program.cs) が restart-exe 検証 OK 後に呼ぶ。
        /// 失敗してもアップデート自体は成功扱い (`.bak` 残骸は手動削除可能、または次回 Replace で
        /// 自動的に削除される)。
        /// </summary>
        /// <param name="managerTargetDir">置換先の Manager dir (実体側、`.bak` の元になった dir)</param>
        public static void CleanupBak(string managerTargetDir)
        {
            string bakDir = managerTargetDir.TrimEnd('\\', '/') + ".bak";
            if (!Directory.Exists(bakDir))
            {
                // 新規インストール時など、そもそも `.bak` が作られていないケース
                return;
            }
            Logger.Info($".bak を削除 (best-effort): {bakDir}");
            try
            {
                Directory.Delete(bakDir, recursive: true);
                Logger.Info($"  .bak 削除完了");
            }
            catch (Exception ex)
            {
                Logger.Warn($"  .bak 削除失敗 (アップデート自体は成功、手動削除可): {ex.Message}");
                Logger.Warn($"  残存 path: {bakDir}");
            }
        }

        /// <summary>
        /// `.bak` から target に復元 (round 1 H1 修正)。Replace は成功したが、その後の検証
        /// (restart-exe 存在など) で失敗した場合に caller が呼ぶ。Replace 内の Rollback
        /// (private) を public 化したもので、シグネチャは bakExists 引数なし版。
        /// </summary>
        /// <param name="managerTargetDir">置換先の Manager dir</param>
        /// <exception cref="InvalidOperationException">rollback 失敗 = 致命的状態 (旧 Manager 消失 + 新 Manager 不在)</exception>
        public static void RollbackFromBak(string managerTargetDir)
        {
            string bakDir = managerTargetDir.TrimEnd('\\', '/') + ".bak";
            // bak が存在しない = Replace が「新規インストール扱い」で動いた case。target を消すだけ。
            bool bakExists = Directory.Exists(bakDir);
            Rollback(managerTargetDir, bakDir, bakExists);
        }

        /// <summary>
        /// rollback: 新コピーが入った target を削除し、.bak を target にリネームで戻す。
        /// </summary>
        private static void Rollback(string managerTargetDir, string bakDir, bool bakExists)
        {
            if (!bakExists)
            {
                // 新規インストールの copy 失敗 → target を消すだけ
                Logger.Warn("rollback: 新規インストール用の target を削除");
                try { if (Directory.Exists(managerTargetDir)) Directory.Delete(managerTargetDir, recursive: true); }
                catch (Exception ex) { Logger.Error($"  target 削除失敗: {ex.Message}"); }
                return;
            }

            Logger.Warn("rollback: 旧 Manager dir を .bak から復元");
            try
            {
                if (Directory.Exists(managerTargetDir))
                {
                    Directory.Delete(managerTargetDir, recursive: true);
                }
                Directory.Move(bakDir, managerTargetDir);
                Logger.Warn($"rollback 完了 (旧 Manager 復元): {managerTargetDir}");
            }
            catch (Exception ex)
            {
                Logger.Error($"rollback 失敗 (致命的状態)", ex);
                throw new InvalidOperationException(
                    $"rollback に失敗しました。手動復旧が必要です。\n" +
                    $"  既存 target: {managerTargetDir}\n" +
                    $"  bak:         {bakDir}\n" +
                    $"原因: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Directory.Copy は標準にないので手動。NTFS の hardlink / シンボリックリンクは current scope 外。
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = filePath.Substring(sourceDir.Length).TrimStart('\\', '/');
                string destPath = Path.Combine(destDir, relative);
                string destParent = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destParent))
                {
                    Directory.CreateDirectory(destParent);
                }
                File.Copy(filePath, destPath, overwrite: true);
            }
        }
    }
}
