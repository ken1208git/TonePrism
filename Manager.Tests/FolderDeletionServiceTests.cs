using System;
using System.IO;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#209) `FolderDeletionService.TryDelete` の検証。特に **read-only 属性のディレクトリ/ファイル**
    /// (Unity/Godot 等のゲームプロジェクトフォルダで頻出) を削除できることを固定する。
    /// 実機で Unity ゲーム (Toney_Fox) の版削除が「フォルダ物理削除に失敗 (read-only dir に UnauthorizedAccessException)」
    /// となった回帰を守る。
    /// </summary>
    public class FolderDeletionServiceTests
    {
        [Fact]
        public void TryDelete_ReadOnlyDirsAndFiles_ClearsAttributesAndDeletes()
        {
            string root = Path.Combine(Path.GetTempPath(), "tp_fdel_" + Guid.NewGuid().ToString("N"));
            string assets = Path.Combine(root, "My_project", "Assets");
            Directory.CreateDirectory(assets);
            string file = Path.Combine(assets, "data.bin");
            File.WriteAllText(file, "x");

            // Unity プロジェクト相当: ファイル + 全階層のディレクトリに read-only を付与。
            File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            foreach (var d in new[] { assets, Path.Combine(root, "My_project"), root })
            {
                new DirectoryInfo(d).Attributes |= FileAttributes.ReadOnly;
            }

            FolderDeletionService.Result result;
            try
            {
                result = FolderDeletionService.TryDelete(root);
            }
            finally
            {
                // 失敗時の後始末 (assert 失敗でも temp を残さない)。
                if (Directory.Exists(root))
                {
                    try
                    {
                        foreach (var info in new DirectoryInfo(root).GetFileSystemInfos("*", SearchOption.AllDirectories))
                        {
                            try { info.Attributes &= ~FileAttributes.ReadOnly; } catch { }
                        }
                        new DirectoryInfo(root).Attributes &= ~FileAttributes.ReadOnly;
                        Directory.Delete(root, true);
                    }
                    catch { /* ignore */ }
                }
            }

            Assert.True(result.Success);
            Assert.False(Directory.Exists(root));
        }

        [Fact]
        public void TryDelete_NonExistentPath_ReturnsSuccess()
        {
            string root = Path.Combine(Path.GetTempPath(), "tp_fdel_none_" + Guid.NewGuid().ToString("N"));
            var result = FolderDeletionService.TryDelete(root);
            Assert.True(result.Success);
        }
    }
}
