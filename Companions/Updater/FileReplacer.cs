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
        /// ⚠ **stateful API**: Replace 成功 (true 返却) 後、caller は **必ず** 以下のいずれかを呼ぶ:
        ///   - 成功確定時: <see cref="CleanupBak"/> で `.bak` を best-effort 削除
        ///   - 検証 (restart-exe 存在等) 失敗時: <see cref="RollbackFromBak"/> で旧 Manager 復元
        ///
        /// caller がどちらも呼ばないと `.bak` がディスクに残留し、次回 Replace で「過去 run の残骸
        /// .bak を先に掃除」branch に流れる (動作 OK だが浪費)。**新規 caller を追加する場合は
        /// 必ず CleanupBak/RollbackFromBak の呼び出しをペアで実装すること** (シニアレビュー round 2 L3
        /// で stateful API の footgun として明示化)。
        ///
        /// シニアレビュー round 1 H1 対応の経緯: 旧実装は `.bak` 削除も同関数内で行っていたが、
        /// caller (Program.cs) が restart-exe 存在検証を行う前に `.bak` が消えてしまい、
        /// release packaging bug 等で新 target に restart-exe が無いケースで「旧 Manager 消失 +
        /// 新 Manager 不在」の復旧不能 broken state に陥っていた。Replace を Step 1/2 のみに
        /// 絞り、`.bak` 削除を別 API に分離して caller の検証 hook を挟む構造に。
        /// </summary>
        /// <param name="stagingDir">staging dir のルート (中の `files/Manager/` をソースに使う)</param>
        /// <param name="managerTargetDir">置換先の既存 Manager dir (絶対 path 必須、CliArgs で GetFullPath 済を期待)</param>
        /// <returns>true: Step 1/2 成功、`.bak` は target+".bak" に存在。**caller は CleanupBak または RollbackFromBak を必ず呼ぶこと** / false: 失敗 (Replace 内で自動 Rollback 実施済 = 旧 Manager が復元、`.bak` も消費済)</returns>
        /// <exception cref="InvalidOperationException">rollback にも失敗した致命的状態</exception>
        public static bool Replace(string stagingDir, string managerTargetDir)
        {
            string stagingManagerSrc = Path.Combine(stagingDir, "files", "Manager");
            if (!Directory.Exists(stagingManagerSrc))
            {
                Logger.Error($"staging の Manager ソースが見つかりません: {stagingManagerSrc}");
                return false;
            }

            // シニアレビュー round 2 M1: trailing-slash 付き path に対する parent 計算の silent
            // divergence を解消。`Path.GetDirectoryName("D:\Manager\")` は `"D:\Manager"` (Manager
            // dir 自身) を返すので、TrimEnd で trailing slash を剥がしてから親計算する必要がある。
            // Program.cs:64 の log-dir 計算と同じ pattern。CliArgs の Path.GetFullPath は trailing
            // slash を保持するため、ここで明示 TrimEnd しないと bug 化する。
            string parentDir = Path.GetDirectoryName(managerTargetDir.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                Logger.Error($"manager-target の親 dir が存在しません: {parentDir}");
                return false;
            }

            string bakDir = managerTargetDir.TrimEnd('\\', '/') + ".bak";
            // 過去 run の残骸 .bak の扱い (Codex round 2 P1 #3 対応):
            //   target 不在 + .bak 存在 → 前回 run で Rollback も失敗、`.bak` のみが intact な
            //                              Manager。`.bak` を消すと旧 Manager 消失 + 新 Manager
            //                              不在の復旧不能 broken state になる。自動復元してから
            //                              Replace を fail で抜け、次回 run で正常 path に乗せる。
            //   target 存在 + .bak 存在 → 前回 run で copy 中断 + rollback も走らなかった等の
            //                              partial state。target を正とみなして `.bak` を残骸として
            //                              削除して進める。
            //   target 不在 + .bak 不在 → 新規 install 扱いだが Updater は更新 spawn 専用なので
            //                              targetExisted=false 経路で round 2 L2 fix が Error 返す。
            if (Directory.Exists(bakDir))
            {
                if (!Directory.Exists(managerTargetDir))
                {
                    Logger.Warn($"前回 run の rollback 失敗を検出: target 不在 + .bak のみ存在");
                    Logger.Warn($"  .bak から target に自動復元します: {bakDir} → {managerTargetDir}");
                    try
                    {
                        Directory.Move(bakDir, managerTargetDir);
                        Logger.Warn($"自動復元完了。本 Replace は abort、Install.bat 再実行か Updater 再実行で続行してください。");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"自動復元失敗 (致命的状態): {ex.Message}");
                        throw new InvalidOperationException(
                            $"前回 run の rollback 失敗 + 本 run での自動復元も失敗。\n" +
                            $"  target: {managerTargetDir} (不在)\n" +
                            $"  bak:    {bakDir} (存在、復元失敗)\n" +
                            $"手動で `.bak` を target にリネームしてください。\n" +
                            $"原因: {ex.Message}", ex);
                    }
                }
                Logger.Info($"既存の .bak を削除 (target が正、.bak は前回 partial state の残骸): {bakDir}");
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
            //
            // round 2 L2: target dir 不在は **caller の引数誤り** として fail back する。SPEC §3.7.4
            // で Updater は Manager UI からの更新用 spawn 専用 (新規 install は Install.bat 担当)
            // なので、target 不在は typo (--manager-target に間違った path 渡し) しかありえない。
            // 旧実装は Warn だけで copy を進めていたため、`<install>/typo/Manager` のような誤 path
            // に新規 install してしまう silent typo 吸収 path があった。Error + return false で
            // Manager UI 側に引数エラーとして検出させる。
            if (!Directory.Exists(managerTargetDir))
            {
                Logger.Error($"target dir が存在しません (caller の引数誤り疑い): {managerTargetDir}");
                Logger.Error("Updater は Manager UI からの更新 spawn 専用です。新規 install は Install.bat を使用してください。");
                return false;
            }
            // round 3 L1: 上記 early return により以降の経路では target は必ず存在する。
            // Rollback() への `bakExists` 引数は常に true で渡せる。
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
                // rollback (Step 1 で作った .bak から復元、target は早期 return で存在保証済 → bakExists=true)
                Rollback(managerTargetDir, bakDir, bakExists: true);
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
            // round 3 L2: round 2 L2 (target 不在 case を Replace で塞いだ) 後は、Replace 成功 →
            // 検証失敗で本関数が呼ばれる時点で `.bak` は実質的に存在する。それでも defensive check
            // として bakExists を計算して Rollback に渡す (Rollback 内の bakExists=false branch は
            // 外部の手動呼出し等で本関数が想定外状況で呼ばれた場合の fallback として残す)。
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
        ///
        /// <para>**attribute の扱い** (round 4 L-4): `File.Copy(..., overwrite: true)` は内容のみコピーし、
        /// source の ReadOnly / Hidden / System 等のファイル attribute は **preserve しない**。Manager 配下は
        /// 全て通常 attribute の .exe / .dll / .config / native DLL なので現状実害なし。将来「user data
        /// 残し更新」path で attribute 維持が必要な場合は `File.GetAttributes` + `File.SetAttributes` の
        /// 明示コピーが要る (本 PR scope 外)。なお staging から copy された ReadOnly 属性付き dest
        /// 残骸への上書きは UnauthorizedAccessException を起こすが、本実装では target を `.bak` rename
        /// した直後の新規 dir に書き込むので dest 残骸は通常存在しない。</para>
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
