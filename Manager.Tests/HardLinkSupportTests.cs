using System;
using System.IO;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#250 PR1) `HardLinkSupport` の検証。%TEMP% が NTFS 前提 (Windows 開発/展示機の通常構成)。
    /// プローブ・リンク作成・同一実体判定を固定する。
    /// </summary>
    public class HardLinkSupportTests : IDisposable
    {
        private readonly string _dir;

        public HardLinkSupportTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "tp_hl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void Probe_OnNtfsTemp_ReturnsTrue_AndLeavesNoProbeFiles()
        {
            bool ok = HardLinkSupport.ProbeHardLinkSupport(_dir);
            Assert.True(ok); // %TEMP% は通常 NTFS
            // プローブ用一時ファイルが後始末されている
            Assert.Empty(Directory.GetFiles(_dir, ".hlprobe_*"));
        }

        [Fact]
        public void CreateHardLink_SharesSameUnderlyingFile()
        {
            string src = Path.Combine(_dir, "src.bin");
            string link = Path.Combine(_dir, "link.bin");
            File.WriteAllText(src, "hello");

            HardLinkSupport.CreateHardLink(link, src);

            Assert.True(File.Exists(link));
            Assert.True(HardLinkSupport.AreSameFile(src, link));
            // 片方への書込が他方に波及する = 同一実体
            File.WriteAllText(src, "changed-content");
            Assert.Equal("changed-content", File.ReadAllText(link));
        }

        [Fact]
        public void AreSameFile_DistinctFiles_ReturnsFalse()
        {
            string a = Path.Combine(_dir, "a.bin");
            string b = Path.Combine(_dir, "b.bin");
            File.WriteAllText(a, "same-bytes");
            File.WriteAllText(b, "same-bytes"); // 内容同一でも別実体
            Assert.False(HardLinkSupport.AreSameFile(a, b));
        }

        [Fact]
        public void CreateHardLink_NonexistentSource_Throws()
        {
            string src = Path.Combine(_dir, "missing.bin");
            string link = Path.Combine(_dir, "link.bin");
            Assert.ThrowsAny<Exception>(() => HardLinkSupport.CreateHardLink(link, src));
        }
    }
}
