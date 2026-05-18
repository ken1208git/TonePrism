using System;
using System.IO;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// dir 単位の rename-rollback 置換。Phase 4 (#108) の Manager UI が Launcher dir / Companion dir
    /// (Updater 含む) を置換するのに使う。
    ///
    /// Updater の `Companions/Updater/FileReplacer.cs` (round 7-8 改善版) の **論理同型** を C# Manager 側で
    /// 再実装。`internal static class` で Updater assembly に閉じている FileReplacer をそのまま参照すると
    /// circular dependency (Manager → Updater → spawn Manager) になるため shared library 化せず、Phase 3
    /// 8 round senior review 済の安定資産には触らない方針。
    ///
    /// 動作: target dir を `.bak` に rename → staging src を target に copy → 検証 OK なら
    /// `CleanupBak` で `.bak` 削除、検証 NG なら `RollbackFromBak` で `.bak` から復元。`.bak` 不在は
    /// 致命的状態として `InvalidOperationException` throw (round 8 Codex P2-2 と同方針)。
    ///
    /// stateful API: `Replace` が true を返した後、caller は **必ず** CleanupBak / RollbackFromBak の
    /// いずれかを呼ぶこと (FileReplacer.cs と同じ footgun design、PR review で side-by-side 比較必要)。
    /// </summary>
    internal static class DirReplacer
    {
        /// <summary>
        /// (#108 Phase 4 round 3 L-4) Replace の結果を enum 化、caller が状態別に UX 文言を出せるように。
        ///   - Ok            : 正常置換完了 (caller は CleanupBak / RollbackFromBak を呼ぶ責務)
        ///   - InitialDeploy : target 不在で初回 deploy (新 Companion 等)、`.bak` 不要
        ///   - RecoveredAbort: 前回 run の rollback 失敗 (target 不在 + `.bak` のみ存在) を自動復元、本 run は abort、
        ///                     **再実行で完走する** ことを caller が user に伝えるべき
        ///   - Fail          : 純粋失敗 (= caller は throw or 再試行 UX)
        /// </summary>
        public enum ReplaceResult { Ok, InitialDeploy, RecoveredAbort, Fail }

        /// <summary>
        /// rename-rollback 方式で `targetDir` を `sourceDir` で置換する (Step 1: rename + Step 2: copy のみ)。
        /// </summary>
        /// <param name="sourceDir">置換ソース dir (絶対 path、staging 配下の `files/<Name>/`)</param>
        /// <param name="targetDir">置換先 dir (絶対 path、`<install>/<Name>/`)</param>
        /// <param name="allowInitialDeploy">true: target 不在を「初回 deploy」とみなして source を copy のみ
        /// (`.bak` 作成なし、Companion 新規追加経路で使う、round 3 codex P1)。false: target 不在は caller
        /// 引数誤りとして Fail (Launcher / Manager / Updater 等の必須既存 dir 用、誤検出を fail-fast)</param>
        /// <returns>ReplaceResult enum (上記 docstring)</returns>
        /// <exception cref="InvalidOperationException">rollback にも失敗した致命的状態</exception>
        public static ReplaceResult Replace(string sourceDir, string targetDir, bool allowInitialDeploy = false)
        {
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                Services.Logger.Error("[DirReplacer] source dir が見つかりません: " + (sourceDir ?? "(null)"));
                return ReplaceResult.Fail;
            }

            string normalizedTarget = targetDir.TrimEnd('\\', '/');
            string parentDir = Path.GetDirectoryName(normalizedTarget);
            if (string.IsNullOrEmpty(parentDir))
            {
                Services.Logger.Error("[DirReplacer] target から親 dir を計算できません (drive root 等の病的入力疑い): " + targetDir);
                return ReplaceResult.Fail;
            }
            if (!Directory.Exists(parentDir))
            {
                // (#108 Phase 4 round 3 codex P1) 新規 Companion 初回 deploy で `<install>/Companions/<Name>/`
                // が無い case を許容するため、親 dir 不在も `allowInitialDeploy` 経由で作成する。
                if (!allowInitialDeploy)
                {
                    Services.Logger.Error("[DirReplacer] target の親 dir が存在しません: " + parentDir + " (target: " + targetDir + ")");
                    return ReplaceResult.Fail;
                }
                try
                {
                    Directory.CreateDirectory(parentDir);
                    Services.Logger.Info("[DirReplacer] 初回 deploy 用に target の親 dir を作成: " + parentDir);
                }
                catch (Exception ex)
                {
                    Services.Logger.Error("[DirReplacer] target の親 dir 作成失敗: " + parentDir + ": " + ex.Message);
                    return ReplaceResult.Fail;
                }
            }

            string bakDir = normalizedTarget + ".bak";

            // 過去 run の残骸 .bak の扱い (Updater FileReplacer.cs round 7-8 と同方針):
            //   target 不在 + .bak 存在 → 前回 run で Rollback 失敗、`.bak` のみが intact。自動復元してから abort。
            //   target 存在 + .bak 存在 → 前回 run の partial state。target を正とみなして `.bak` 削除して進む。
            if (Directory.Exists(bakDir))
            {
                if (!Directory.Exists(targetDir))
                {
                    Services.Logger.Warn("[DirReplacer] 前回 run の rollback 失敗を検出: target 不在 + .bak のみ存在");
                    Services.Logger.Warn("  .bak から target に自動復元します: " + bakDir + " → " + targetDir);
                    try
                    {
                        Directory.Move(bakDir, targetDir);
                        Services.Logger.Warn("[DirReplacer] 自動復元完了。本 Replace は abort、再実行で続行してください。");
                        return ReplaceResult.RecoveredAbort;
                    }
                    catch (Exception ex)
                    {
                        Services.Logger.Error("[DirReplacer] 自動復元失敗 (致命的状態): " + ex.Message);
                        throw new InvalidOperationException(
                            "前回 run の rollback 失敗 + 本 run での自動復元も失敗。\n" +
                            "  target: " + targetDir + " (不在)\n" +
                            "  bak:    " + bakDir + " (存在、復元失敗)\n" +
                            "手動で `.bak` を target にリネームしてください。\n" +
                            "原因: " + ex.Message, ex);
                    }
                }
                Services.Logger.Info("[DirReplacer] 既存の .bak を削除 (target が正、.bak は前回 partial state の残骸): " + bakDir);
                try
                {
                    Directory.Delete(bakDir, recursive: true);
                }
                catch (Exception ex)
                {
                    Services.Logger.Error("[DirReplacer] 既存 .bak の削除に失敗: " + ex.Message);
                    return ReplaceResult.Fail;
                }
            }

            // [Replace 1/2] target → .bak リネーム
            if (!Directory.Exists(targetDir))
            {
                // (#108 Phase 4 round 3 codex P1) target 不在の扱いは allowInitialDeploy で分岐:
                //   - true (Companion 経路): 新 Companion 初回 deploy として source を copy のみ (.bak 作成なし)
                //   - false (Launcher / Manager / Updater 必須経路): caller 引数誤り or pre-install state で Fail
                if (allowInitialDeploy)
                {
                    Services.Logger.Info("[DirReplacer] 初回 deploy (target 不在): " + sourceDir + " → " + targetDir);
                    try
                    {
                        CopyDirectory(sourceDir, targetDir);
                        Services.Logger.Info("[DirReplacer] 初回 deploy 完了: " + targetDir);
                        return ReplaceResult.InitialDeploy;
                    }
                    catch (Exception ex)
                    {
                        Services.Logger.Error("[DirReplacer] 初回 deploy copy 失敗: " + ex.Message);
                        // 半端な dir を best-effort 削除
                        try { if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true); } catch { }
                        return ReplaceResult.Fail;
                    }
                }
                Services.Logger.Error("[DirReplacer] target dir が存在しません (caller の引数誤り or pre-install state): " + targetDir);
                return ReplaceResult.Fail;
            }
            Services.Logger.Info("[DirReplacer] [Replace 1/2] 既存 dir を .bak にリネーム: " + targetDir + " → " + bakDir);
            try
            {
                Directory.Move(targetDir, bakDir);
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[DirReplacer] Replace 1/2 失敗 (rename): " + ex.Message);
                return ReplaceResult.Fail;
            }

            // [Replace 2/2] source → target へ copy
            Services.Logger.Info("[DirReplacer] [Replace 2/2] source から新 dir をコピー: " + sourceDir + " → " + targetDir);
            try
            {
                CopyDirectory(sourceDir, targetDir);
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[DirReplacer] Replace 2/2 失敗 (copy): " + ex.Message);
                RollbackInternal(targetDir, bakDir, bakExists: true);
                return ReplaceResult.Fail;
            }

            Services.Logger.Info("[DirReplacer] dir 置換完了 (Step 1/2 + 2/2、.bak は CleanupBak まで保持): " + targetDir);
            return ReplaceResult.Ok;
        }

        /// <summary>`.bak` を best-effort 削除。caller が Replace 後の検証成功時に呼ぶ。</summary>
        public static void CleanupBak(string targetDir)
        {
            string bakDir = targetDir.TrimEnd('\\', '/') + ".bak";
            if (!Directory.Exists(bakDir)) return;
            Services.Logger.Info("[DirReplacer] .bak を削除 (best-effort): " + bakDir);
            try
            {
                Directory.Delete(bakDir, recursive: true);
            }
            catch (Exception ex)
            {
                Services.Logger.Warn("[DirReplacer]   .bak 削除失敗 (アップデート自体は成功、手動削除可): " + ex.Message);
                Services.Logger.Warn("[DirReplacer]   残存 path: " + bakDir);
            }
        }

        /// <summary>`.bak` から target を復元 (検証失敗時に呼ぶ)。</summary>
        /// <exception cref="InvalidOperationException">rollback 失敗 = 致命的状態</exception>
        public static void RollbackFromBak(string targetDir)
        {
            string bakDir = targetDir.TrimEnd('\\', '/') + ".bak";
            bool bakExists = Directory.Exists(bakDir);
            RollbackInternal(targetDir, bakDir, bakExists);
        }

        private static void RollbackInternal(string targetDir, string bakDir, bool bakExists)
        {
            if (!bakExists)
            {
                Services.Logger.Error("[DirReplacer] FATAL rollback: `.bak` 不在の pathological state");
                try
                {
                    if (Directory.Exists(targetDir))
                    {
                        Directory.Delete(targetDir, recursive: true);
                        Services.Logger.Warn("[DirReplacer]   target を削除 (中身は新 dir の半端コピー、使えない)");
                    }
                }
                catch (Exception ex)
                {
                    Services.Logger.Error("[DirReplacer]   target 削除も失敗: " + ex.Message);
                }
                throw new InvalidOperationException(
                    "rollback 失敗: `.bak` 不在の pathological state、新 dir も旧 dir も両方無い致命的状態。\n" +
                    "  target: " + targetDir + " (削除済または削除失敗)\n" +
                    "  bak:    not found\n" +
                    "手動で zip 再展開 + Install.bat 再実行が必要");
            }

            Services.Logger.Warn("[DirReplacer] rollback: 旧 dir を .bak から復元: " + targetDir);
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, recursive: true);
                }
                Directory.Move(bakDir, targetDir);
                Services.Logger.Warn("[DirReplacer] rollback 完了: " + targetDir);
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[DirReplacer] rollback 失敗 (致命的状態): " + ex.Message);
                throw new InvalidOperationException(
                    "rollback に失敗しました。手動復旧が必要です。\n" +
                    "  既存 target: " + targetDir + "\n" +
                    "  bak:         " + bakDir + "\n" +
                    "原因: " + ex.Message, ex);
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            // (#108 Phase 4 round 3 L-3) 空 dir も保持。EnumerateFiles だけだと空の subdir (例:
            // `<Companion>/cache/` 等の placeholder) が target に作られない silent drop があった。
            // 現状の Launcher / Updater は空 dir を含まないが将来 Companion で発生しうるため事前対応。
            // (#108 Phase 4 round 4 L-1) 注: 「ファイルを含む subtree」は下の file copy loop の
            // `Directory.CreateDirectory(destParent)` で自動作成されるため、本ループの効果は
            // 「leaf 含めて完全に空の subdir」のみ。redundant 気味だが将来の placeholder dir 用 defensive。
            foreach (string subDir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = subDir.Substring(sourceDir.Length).TrimStart('\\', '/');
                string destSubDir = Path.Combine(destDir, relative);
                Directory.CreateDirectory(destSubDir);
            }
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
