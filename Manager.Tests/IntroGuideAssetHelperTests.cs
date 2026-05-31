using System;
using System.IO;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#253) `IntroGuideAssetHelper` の画像取り込みコアの単体テスト。
    /// PathManager 非依存のコア (`CopyImageInto` / `ResolveNonConflictingLeaf` / `DeleteImage`) を
    /// 一時フォルダ上の実ファイルで検証する。
    /// </summary>
    public class IntroGuideAssetHelperTests : IDisposable
    {
        private readonly string _root;
        private readonly string _guide;
        private readonly string _src;

        public IntroGuideAssetHelperTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "tp_guide_" + Guid.NewGuid().ToString("N"));
            _guide = Path.Combine(_root, "guide");
            _src = Path.Combine(_root, "src");
            Directory.CreateDirectory(_src);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
        }

        private string MakeSource(string name, string content = "img")
        {
            string p = Path.Combine(_src, name);
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void CopyImageInto_CopiesFile_ReturnsLeaf()
        {
            string src = MakeSource("welcome.png");
            string leaf = IntroGuideAssetHelper.CopyImageInto(_guide, src);
            Assert.Equal("welcome.png", leaf);
            Assert.True(File.Exists(Path.Combine(_guide, "welcome.png")));
        }

        [Fact]
        public void CopyImageInto_OnNameCollision_AutoSuffixes_NoOverwrite()
        {
            string a = MakeSource("slide.png", "A");
            string leaf1 = IntroGuideAssetHelper.CopyImageInto(_guide, a);
            // 別の場所にある同名ファイルを再取り込み → 自動 suffix で別名コピー (上書きしない)。
            string b = Path.Combine(_src, "sub");
            Directory.CreateDirectory(b);
            string b2 = Path.Combine(b, "slide.png");
            File.WriteAllText(b2, "B");
            string leaf2 = IntroGuideAssetHelper.CopyImageInto(_guide, b2);

            Assert.Equal("slide.png", leaf1);
            Assert.Equal("slide_2.png", leaf2);
            Assert.Equal("A", File.ReadAllText(Path.Combine(_guide, "slide.png")));   // 元は無傷
            Assert.Equal("B", File.ReadAllText(Path.Combine(_guide, "slide_2.png"))); // 新規は別名
        }

        [Fact]
        public void ResolveNonConflictingLeaf_FreeThenIncrement()
        {
            Directory.CreateDirectory(_guide);
            Assert.Equal("a.png", IntroGuideAssetHelper.ResolveNonConflictingLeaf(_guide, "a.png"));
            File.WriteAllText(Path.Combine(_guide, "a.png"), "x");
            Assert.Equal("a_2.png", IntroGuideAssetHelper.ResolveNonConflictingLeaf(_guide, "a.png"));
            File.WriteAllText(Path.Combine(_guide, "a_2.png"), "x");
            Assert.Equal("a_3.png", IntroGuideAssetHelper.ResolveNonConflictingLeaf(_guide, "a.png"));
        }

        [Fact]
        public void CopyImageInto_MissingSource_Throws()
        {
            Assert.Throws<FileNotFoundException>(() =>
                IntroGuideAssetHelper.CopyImageInto(_guide, Path.Combine(_src, "nope.png")));
        }

        [Fact]
        public void DeleteImage_RemovesGuideFile_IgnoresNonGuidePaths()
        {
            string src = MakeSource("del.png");
            string leaf = IntroGuideAssetHelper.CopyImageInto(_guide, src);
            string rel = "guide/" + leaf;
            Assert.True(File.Exists(Path.Combine(_guide, leaf)));

            Assert.True(IntroGuideAssetHelper.DeleteImage(_guide, rel));
            Assert.False(File.Exists(Path.Combine(_guide, leaf)));

            // guide/ 配下でない相対パス・空・ネストは無視して true (実ファイルに触れない)。
            Assert.True(IntroGuideAssetHelper.DeleteImage(_guide, "games/x/thumb.png"));
            Assert.True(IntroGuideAssetHelper.DeleteImage(_guide, ""));
            Assert.True(IntroGuideAssetHelper.DeleteImage(_guide, "guide/sub/x.png"));
        }
    }
}
