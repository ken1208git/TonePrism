using System;
using System.IO;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// 単体ファイルの atomic 置換 (Phase 4 の shortcut bat = `<install_parent>/Launcher.bat` /
    /// `<install_parent>/Manager.bat` 用)。
    ///
    /// `Launcher.bat` / `Manager.bat` は通常稼働中ではない (ダブルクリックで cmd が起動して
    /// 即 `start "" ...` → exit する短命 wrapper) ため、Manager から直接書き換えて問題ない。
    /// shortcut bat に対して dir 単位 rename-rollback を使うのは over-engineering なので、
    /// `.bak` 付き single-file rename → File.Copy → 成功時 `.bak` 削除 の最小 atomic 戦略。
    /// </summary>
    internal static class FileReplacer
    {
        /// <summary>
        /// 単体ファイル置換 (atomic-ish)。src が存在しない場合は abort (= false 返し)。
        /// target 不在の case は「初めての配置」とみなして単純コピーで OK (Phase 2 install から
        /// Phase 4 update への移行 path、`<install_parent>/Manager.bat` が存在しないケース等)。
        /// </summary>
        public static bool ReplaceFile(string sourcePath, string targetPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                Services.Logger.Error("[FileReplacer] source file が見つかりません: " + (sourcePath ?? "(null)"));
                return false;
            }
            string targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetParent) && !Directory.Exists(targetParent))
            {
                try
                {
                    Directory.CreateDirectory(targetParent);
                }
                catch (Exception ex)
                {
                    Services.Logger.Error("[FileReplacer] target parent dir 作成失敗 (" + targetParent + "): " + ex.Message);
                    return false;
                }
            }

            string bakPath = targetPath + ".bak";
            // 過去 run の残骸 .bak を best-effort で除去
            if (File.Exists(bakPath))
            {
                try
                {
                    File.Delete(bakPath);
                }
                catch
                {
                    // 古い .bak が消せない場合は新 .bak で上書きを試みる、それも失敗ならその時に判定
                }
            }

            bool renamed = false;
            try
            {
                if (File.Exists(targetPath))
                {
                    File.Move(targetPath, bakPath);
                    renamed = true;
                }
                File.Copy(sourcePath, targetPath, overwrite: false);
            }
            catch (Exception ex)
            {
                Services.Logger.Error("[FileReplacer] ファイル置換失敗 (" + targetPath + "): " + ex.Message);
                // rollback: .bak が残っていれば戻す
                if (renamed)
                {
                    try
                    {
                        if (File.Exists(targetPath)) File.Delete(targetPath);
                        File.Move(bakPath, targetPath);
                    }
                    catch (Exception rex)
                    {
                        Services.Logger.Error("[FileReplacer]   rollback 失敗: " + rex.Message);
                    }
                }
                return false;
            }

            // 成功 → .bak 削除 (best-effort、失敗してもアップデート自体は成功扱い)
            if (renamed && File.Exists(bakPath))
            {
                try
                {
                    File.Delete(bakPath);
                }
                catch
                {
                    // .bak 残骸は手動削除可、致命的でない
                }
            }
            Services.Logger.Info("[FileReplacer] ファイル置換完了: " + targetPath);
            return true;
        }
    }
}
