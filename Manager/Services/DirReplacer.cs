using System;
using System.IO;

namespace GCTonePrism.Manager.Services
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
        /// rename-rollback 方式で `targetDir` を `sourceDir` で置換する (Step 1: rename + Step 2: copy のみ)。
        /// </summary>
        /// <param name="sourceDir">置換ソース dir (絶対 path、staging 配下の `files/<Name>/`)</param>
        /// <param name="targetDir">置換先 dir (絶対 path、`<install>/<Name>/`)</param>
        /// <returns>true: Step 1/2 成功、`.bak` は target+".bak" に存在。caller は CleanupBak / RollbackFromBak を呼ぶ責務 / false: 失敗 (Replace 内で自動 Rollback 実施済、`.bak` も消費済)</returns>
        /// <exception cref="InvalidOperationException">rollback にも失敗した致命的状態</exception>
        public static bool Replace(string sourceDir, string targetDir)
        {
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                Services.Logger.Error("[DirReplacer] source dir が見つかりません: " + (sourceDir ?? "(null)"));
                return false;
            }

            string normalizedTarget = targetDir.TrimEnd('\\', '/');
            string parentDir = Path.GetDirectoryName(normalizedTarget);
            if (string.IsNullOrEmpty(parentDir))
            {
                Services.Logger.Error("[DirReplacer] target から親 dir を計算できません (drive root 等の病的入力疑い): " + targetDir);
                return false;
            }
            if (!Directory.Exists(parentDir))
            {
                Services.Logger.Error("[DirReplacer] target の親 dir が存在しません: " + parentDir + " (target: " + targetDir + ")");
                return false;
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
                        return false;
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
                    return false;
                }
            }

            // [Replace 1/2] target → .bak リネーム
            if (!Directory.Exists(targetDir))
            {
                // Manager UI Phase 4 の呼出経路では target は常に存在する (Install.bat で initialize 済)。
                // 不在は caller の引数誤り or pre-install state、いずれも fail back する。
                Services.Logger.Error("[DirReplacer] target dir が存在しません (caller の引数誤り or pre-install state): " + targetDir);
                return false;
            }
            Services.Logger.Info("[DirReplacer] [Replace 1/2] 既存 dir を .bak にリネーム: " + targetDir + " → " + bakDir);
            try
            {
                Directory.Move(targetDir, bakDir);
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[DirReplacer] Replace 1/2 失敗 (rename): " + ex.Message);
                return false;
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
                return false;
            }

            Services.Logger.Info("[DirReplacer] dir 置換完了 (Step 1/2 + 2/2、.bak は CleanupBak まで保持): " + targetDir);
            return true;
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
