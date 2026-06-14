using System.Linq;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#242 ②) EditGameForm.btnOK_Click から抽出した版セット入力検証の回帰テスト。
    /// 空 / 不正version / 正規化重複(NOCASE) / 人数 min>max の分類と per-entry 整形を検証する。
    /// (malformed-suffix 行は VersionRegex≡SuffixRegex 統一で現状 dead path のため assert しない。
    ///  不正 suffix は numeric 行に落ちる＝下の MalformedVersion_ で確認。)
    /// </summary>
    public class GameVersionSetValidatorTests
    {
        private static GameVersion V(int id, string version, int? min = null, int? max = null)
            => new GameVersion { Id = id, Version = version, MinPlayers = min, MaxPlayers = max };

        private static GameVersionSetValidator.Result Validate(params GameVersion[] versions)
            => new GameVersionSetValidator().Validate(versions);

        [Fact]
        public void AllValid_NoViolations()
        {
            var r = Validate(V(1, "v1.0.0"), V(2, "v2.1.3", 1, 4), V(3, "v1.0.0-rc1"));
            Assert.Equal(0, r.VersionStringIssueCount);
            Assert.Empty(r.DuplicateVersions);
            Assert.Empty(r.PlayerCountViolations);
        }

        [Fact]
        public void EmptyOrNullVersion_ListedInEmptyIds()
        {
            var r = Validate(V(5, ""), V(6, null));
            Assert.Equal(2, r.EmptyIds.Count);
            Assert.Contains("(id=5)", r.EmptyIds);
            Assert.Contains("(id=6)", r.EmptyIds);
            Assert.Equal(2, r.VersionStringIssueCount);
        }

        [Theory]
        [InlineData("abc")]          // 数値部なし
        [InlineData("1.2")]          // patch 欠落
        [InlineData("v1.0.0-rc..1")] // 空 identifier の suffix → VersionRegex 不一致 → numeric 行
        [InlineData("v1.0.0-rc@1")]  // 不正文字 suffix → 同上
        public void MalformedVersion_ListedInNumericEntries(string ver)
        {
            var r = Validate(V(7, ver));
            Assert.Contains(r.MalformedNumericEntries, e => e.Contains("id=7"));
            Assert.True(r.VersionStringIssueCount > 0);
        }

        [Fact]
        public void NormalizedDuplicate_DetectedAcrossPrefixAndCase()
        {
            // "v1.0.0" / "1.0.0" / "V1.0.0" は正規化後同一 = 重複 1 グループ (DB の UNIQUE NOCASE と整合)。
            var r = Validate(V(1, "v1.0.0"), V(2, "1.0.0"), V(3, "V1.0.0"));
            Assert.Single(r.DuplicateVersions);
        }

        [Fact]
        public void DistinctVersions_NoDuplicate()
        {
            var r = Validate(V(1, "v1.0.0"), V(2, "v1.0.1"), V(3, "v2.0.0"));
            Assert.Empty(r.DuplicateVersions);
        }

        [Fact]
        public void PlayerCount_MinGreaterThanMax_Violation()
        {
            var r = Validate(V(1, "v1.0.0", min: 4, max: 2));
            Assert.Single(r.PlayerCountViolations);
            Assert.Contains("最小 4 > 最大 2", r.PlayerCountViolations[0]);
        }

        [Fact]
        public void PlayerCount_OkOrNull_NoViolation()
        {
            var r = Validate(
                V(1, "v1.0.0", min: 1, max: 4),    // ok
                V(2, "v2.0.0", min: 2, max: 2),    // ok (等しい)
                V(3, "v3.0.0", min: null, max: 2), // null → skip
                V(4, "v4.0.0", min: 4, max: null)  // null → skip
            );
            Assert.Empty(r.PlayerCountViolations);
        }

        [Fact]
        public void NullEntries_SkippedWithoutCrash()
        {
            var r = new GameVersionSetValidator().Validate(new GameVersion[] { null, V(1, "v1.0.0"), null });
            Assert.Equal(0, r.VersionStringIssueCount);
            Assert.Empty(r.DuplicateVersions);
            Assert.Empty(r.PlayerCountViolations);
        }

        [Fact]
        public void MultipleCategories_AggregatedIndependently()
        {
            // 空 / numeric malformed / 正規化重複 / 人数違反 が独立カテゴリに集計される。
            var r = Validate(
                V(1, ""),                        // empty
                V(2, "abc"),                     // numeric malformed
                V(3, "v1.0.0"), V(4, "1.0.0"),   // 重複 (正規化後同一)
                V(5, "v2.0.0", min: 5, max: 1)   // 人数違反
            );
            Assert.Single(r.EmptyIds);
            Assert.True(r.MalformedNumericEntries.Count >= 1);
            Assert.Single(r.DuplicateVersions);
            Assert.Single(r.PlayerCountViolations);
        }
    }
}
