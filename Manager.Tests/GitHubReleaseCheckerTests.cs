using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#258) `GitHubReleaseChecker` の JSON parse テスト。移行前は test が無く、JavaScriptSerializer →
    /// System.Text.Json (`JsonCompat.DeserializeToObjectTree`) 切替の最も複雑な consumer (nested assets[]
    /// 走査 + 動的型) を pin する。`AsString` / `AsLong` / `ToStringObjectDict` の走査が converter の出力型
    /// (Dictionary&lt;string,object&gt; / object[] / string / long) で正しく動くことを end-to-end で確認。
    /// </summary>
    public class GitHubReleaseCheckerTests
    {
        private const string SingleRelease = @"{
            ""tag_name"": ""v1.2.3"",
            ""body"": ""## release notes"",
            ""html_url"": ""https://example.com/releases/tag/v1.2.3"",
            ""prerelease"": false,
            ""draft"": false,
            ""published_at"": ""2026-06-11T12:00:00Z"",
            ""assets"": [
                { ""name"": ""readme.txt"", ""browser_download_url"": ""https://example.com/readme.txt"", ""size"": 10 },
                { ""name"": ""TonePrism_v1.2.3.zip"", ""browser_download_url"": ""https://example.com/TonePrism_v1.2.3.zip"", ""size"": 9876543 }
            ]
        }";

        [Fact]
        public void ParseRelease_ExtractsFields_AndMatchesTonePrismZipAsset()
        {
            var info = GitHubReleaseChecker.ParseRelease(SingleRelease);
            Assert.NotNull(info);
            Assert.Equal("v1.2.3", info.TagName);
            Assert.Equal("## release notes", info.Body);
            Assert.False(info.IsPrerelease);
            Assert.False(info.IsDraft);
            Assert.NotNull(info.Version);
            // assets[] から TonePrism_v*.zip だけ matched (readme.txt は skip)。size は long で読む (#258 converter)。
            Assert.Equal("https://example.com/TonePrism_v1.2.3.zip", info.ZipAssetUrl);
            Assert.Equal(9876543L, info.ZipSizeBytes);
        }

        [Fact]
        public void ParseReleaseArray_ParsesEachElement()
        {
            string arr = "[" + SingleRelease + "," + SingleRelease.Replace("v1.2.3", "v1.3.0") + "]";
            var list = GitHubReleaseChecker.ParseReleaseArray(arr);
            Assert.Equal(2, list.Count);
            Assert.Contains(list, r => r.TagName == "v1.2.3");
            Assert.Contains(list, r => r.TagName == "v1.3.0");
        }

        [Fact]
        public void ParseRelease_MissingTagName_ReturnsNull()
        {
            // BuildFromDict は tag_name 欠落で null を返す (= ParseRelease も null、旧挙動)。
            var info = GitHubReleaseChecker.ParseRelease(@"{""body"":""x"",""assets"":[]}");
            Assert.Null(info);
        }

        [Fact]
        public void ParseRelease_NotJsonObject_Throws()
        {
            // object でない JSON (array 等) は旧 Deserialize<Dictionary> が throw していた挙動を維持。
            Assert.Throws<GitHubReleaseException>(() => GitHubReleaseChecker.ParseRelease("[1,2,3]"));
        }
    }
}
