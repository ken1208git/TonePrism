using System;
using System.Collections.Generic;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#362) `SettingsPathPolicy.Classify` の純ロジック検証。設定タブ即時反映でパス欄離脱時の分類
    /// (空/相対/不正/Ok/ローカル不在=作成可/到達不能) を固定する。存在確認はモック注入。
    /// </summary>
    public class SettingsPathPolicyTests
    {
        // dirExists モック: 指定 set に (末尾の \ / を無視して) 含まれれば true。
        private static Func<string, bool> Exists(params string[] existing)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in existing) set.Add(e.TrimEnd('\\', '/'));
            return p => p != null && set.Contains(p.TrimEnd('\\', '/'));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Empty_WhenBlank(string path)
            => Assert.Equal(SettingsPathKind.Empty, SettingsPathPolicy.Classify(path, Exists()));

        [Theory]
        [InlineData(@"logs")]
        [InlineData(@"logs\sub")]
        [InlineData(@"..\logs")]
        public void Relative_WhenNotRooted(string path)
            => Assert.Equal(SettingsPathKind.Relative, SettingsPathPolicy.Classify(path, Exists()));

        [Fact]
        public void Ok_WhenAbsoluteAndExists()
            => Assert.Equal(SettingsPathKind.Ok,
                SettingsPathPolicy.Classify(@"D:\TonePrism\logs", Exists(@"D:\TonePrism\logs")));

        [Fact]
        public void MissingLocal_WhenDriveExistsButFolderMissing()
            => Assert.Equal(SettingsPathKind.MissingLocal,
                SettingsPathPolicy.Classify(@"D:\TonePrism\logs", Exists(@"D:\")));

        [Fact]
        public void Unreachable_WhenDriveMissing()
            => Assert.Equal(SettingsPathKind.Unreachable,
                SettingsPathPolicy.Classify(@"Z:\logs", Exists()));

        [Fact]
        public void MissingLocal_Unc_WhenShareReachableButFolderMissing()
            => Assert.Equal(SettingsPathKind.MissingLocal,
                SettingsPathPolicy.Classify(@"\\srv\share\logs", Exists(@"\\srv\share")));

        [Fact]
        public void Unreachable_Unc_WhenServerDown()
            => Assert.Equal(SettingsPathKind.Unreachable,
                SettingsPathPolicy.Classify(@"\\srv\share\logs", Exists()));
    }
}
