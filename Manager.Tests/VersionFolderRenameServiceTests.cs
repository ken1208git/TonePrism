using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#242 ①) EditGameForm.btnOK_Click から抽出した版フォルダ rename ロジックの回帰テスト。
    /// disk 操作 (Directory.Exists/Move) を fake に差し替えて、衝突判定・予約 slot・topological sort・
    /// path prefix 書換・snapshot 更新・Move 失敗時 rollback を deterministic に検証する。
    /// </summary>
    public class VersionFolderRenameServiceTests
    {
        private const string GF = @"C:\games\g1";
        private static string Dir(string leaf) => Path.Combine(GF, leaf);

        private static GameVersion V(int id, string version, string exe = null, string thumb = null, string bg = null)
            => new GameVersion { Id = id, Version = version, ExecutablePath = exe, ThumbnailPath = thumb, BackgroundPath = bg };

        // existing = 現在 disk に在るフォルダ集合。move fake は src を消して dst を足す (実 disk の挙動を模倣)。
        // failOn が src/dst に一致したら IOException を投げる (Move 失敗 → rollback 経路の検証用)。
        private static VersionFolderRenameService Svc(HashSet<string> existing, List<(string src, string dst)> moves = null, string failOn = null)
        {
            return new VersionFolderRenameService(
                p => existing.Contains(p),
                (src, dst) =>
                {
                    if (failOn != null && (string.Equals(src, failOn, StringComparison.OrdinalIgnoreCase) || string.Equals(dst, failOn, StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("locked: " + src);
                    // 実 Directory.Move は移動先が既存だと IOException を投げる。swap/cycle の「先行 Move が
                    // dst 既存で失敗 → 安全失敗」経路を忠実に再現するため fake も同挙動にする。
                    if (existing.Contains(dst))
                        throw new IOException("移動先が既存: " + dst);
                    moves?.Add((src, dst));
                    existing.Remove(src);
                    existing.Add(dst);
                });
        }

        private static HashSet<string> Set(params string[] dirs) => new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);

        // ===== 純粋 string ヘルパ =====

        [Theory]
        [InlineData(null, "v")]
        [InlineData("1.0.0", "v1.0.0")]
        [InlineData("v1.0.0", "v1.0.0")]
        [InlineData("V1.0.0", "v1.0.0")]
        public void ToVersionLeaf_NormalizesPrefix(string input, string expected)
            => Assert.Equal(expected, VersionFolderRenameService.ToVersionLeaf(input));

        [Theory]
        [InlineData("v1.0.0/main.exe", "1.0.0", "2.0.0", "v2.0.0/main.exe")]   // forward slash
        [InlineData("v1.0.0\\main.exe", "v1.0.0", "v2.0.0", "v2.0.0\\main.exe")] // back slash
        [InlineData("V1.0.0/main.exe", "1.0.0", "2.0.0", "v2.0.0/main.exe")]   // case-insensitive prefix
        [InlineData("other/main.exe", "1.0.0", "2.0.0", "other/main.exe")]     // prefix 無し → 無変更
        [InlineData(null, "1.0.0", "2.0.0", null)]                              // null → null
        public void ReplaceVersionPrefix_RewritesLeadingPrefixOnly(string rel, string oldVer, string newVer, string expected)
            => Assert.Equal(expected, VersionFolderRenameService.ReplaceVersionPrefix(rel, oldVer, newVer));

        // ===== BuildPlan =====

        [Fact]
        public void BuildPlan_SimpleRename_OnePlan()
        {
            var existing = Set(Dir("v1.0.0"));
            var r = Svc(existing).BuildPlan(GF, new[] { V(1, "v1.1.0") }, new Dictionary<int, string> { { 1, "v1.0.0" } });

            Assert.False(r.HasCollision);
            Assert.Single(r.OrderedPlan);
            Assert.Equal(Dir("v1.0.0"), r.OrderedPlan[0].OldDir);
            Assert.Equal(Dir("v1.1.0"), r.OrderedPlan[0].NewDir);
            Assert.True(r.OrderedPlan[0].SourceExists);
        }

        [Fact]
        public void BuildPlan_NoVersionChange_EmptyPlan()
        {
            var r = Svc(Set(Dir("v1.0.0"))).BuildPlan(GF, new[] { V(1, "v1.0.0") }, new Dictionary<int, string> { { 1, "v1.0.0" } });
            Assert.False(r.HasCollision);
            Assert.Empty(r.OrderedPlan);
        }

        [Fact]
        public void BuildPlan_CaseOnlyDiff_Skipped()
        {
            // DB "v1.0.0" → 表示中 "V1.0.0"。OrdinalIgnoreCase で同一 = rename 不要。
            var r = Svc(Set(Dir("v1.0.0"))).BuildPlan(GF, new[] { V(1, "V1.0.0") }, new Dictionary<int, string> { { 1, "v1.0.0" } });
            Assert.Empty(r.OrderedPlan);
        }

        [Fact]
        public void BuildPlan_SameLeafDifferentRaw_Skipped()
        {
            // DB "1.0.0" (prefix 無し) → 表示中 "v1.0.0"。raw は不一致だが leaf は同一 → self-rename 回避で skip。
            var r = Svc(Set(Dir("v1.0.0"))).BuildPlan(GF, new[] { V(1, "v1.0.0") }, new Dictionary<int, string> { { 1, "1.0.0" } });
            Assert.Empty(r.OrderedPlan);
        }

        [Fact]
        public void BuildPlan_TrueCollision_ReportsCollision()
        {
            // v1.0.0 → v2.0.0 だが v2.0.0 が既存 disk にあり、空く予定も無い → 衝突。
            var existing = Set(Dir("v1.0.0"), Dir("v2.0.0"));
            var r = Svc(existing).BuildPlan(GF, new[] { V(1, "v2.0.0") }, new Dictionary<int, string> { { 1, "v1.0.0" } });
            Assert.True(r.HasCollision);
            Assert.Contains("v2.0.0", r.CollisionMessage);
        }

        [Fact]
        public void BuildPlan_Swap_NoCollision_ReservedSlots()
        {
            // v1.0.0↔v2.0.0 の入れ替え。両 newDir が相手の oldDir = 予約済みなので BuildPlan は衝突を返さず 2 plan。
            // ただし swap は実 disk では成立しない (ExecutePlan で安全失敗する。下の ExecutePlan_Swap_FailsSafely 参照)。
            var existing = Set(Dir("v1.0.0"), Dir("v2.0.0"));
            var versions = new[] { V(1, "v2.0.0"), V(2, "v1.0.0") };
            var orig = new Dictionary<int, string> { { 1, "v1.0.0" }, { 2, "v2.0.0" } };
            var r = Svc(existing).BuildPlan(GF, versions, orig);
            Assert.False(r.HasCollision);
            Assert.Equal(2, r.OrderedPlan.Count);
        }

        [Fact]
        public void BuildPlan_ChainedRename_TopologicallyOrdered()
        {
            // A: v1.0.0→v2.0.0, B: v2.0.0→v3.0.0。B (空き先 v3.0.0) を先に実行する順で並ぶ。
            var existing = Set(Dir("v1.0.0"), Dir("v2.0.0"));
            var versions = new[] { V(1, "v2.0.0"), V(2, "v3.0.0") };
            var orig = new Dictionary<int, string> { { 1, "v1.0.0" }, { 2, "v2.0.0" } };
            var r = Svc(existing).BuildPlan(GF, versions, orig);
            Assert.False(r.HasCollision);
            Assert.Equal(2, r.OrderedPlan.Count);
            Assert.Equal(2, r.OrderedPlan[0].Version.Id);   // B (v2.0.0→v3.0.0) が先
            Assert.Equal(1, r.OrderedPlan[1].Version.Id);   // A (v1.0.0→v2.0.0) が後
        }

        [Fact]
        public void BuildPlan_MissingSnapshot_Skipped()
        {
            // snapshot に無い version は rename 対象外 (defensive skip)。
            var r = Svc(Set(Dir("v1.0.0"))).BuildPlan(GF, new[] { V(99, "v1.1.0") }, new Dictionary<int, string>());
            Assert.Empty(r.OrderedPlan);
        }

        [Fact]
        public void BuildPlan_OldDirMissing_PlanWithSourceExistsFalse()
        {
            // 旧 folder 不在 (partial-commit recovery): plan は作るが SourceExists=false (Move skip 予定)。
            var existing = Set(); // 何も無い
            var r = Svc(existing).BuildPlan(GF, new[] { V(1, "v1.1.0") }, new Dictionary<int, string> { { 1, "v1.0.0" } });
            Assert.False(r.HasCollision);
            Assert.Single(r.OrderedPlan);
            Assert.False(r.OrderedPlan[0].SourceExists);
        }

        // ===== ExecutePlan =====

        [Fact]
        public void ExecutePlan_Success_MovesRewritesPathsAndSnapshot()
        {
            var existing = Set(Dir("v1.0.0"));
            var moves = new List<(string, string)>();
            var svc = Svc(existing, moves);
            var v = V(1, "v1.1.0", exe: "v1.0.0/main.exe", thumb: "v1.0.0/thumb.png", bg: "v1.0.0/bg.png");
            var orig = new Dictionary<int, string> { { 1, "v1.0.0" } };

            var plan = svc.BuildPlan(GF, new[] { v }, orig);
            var res = svc.ExecutePlan(plan.OrderedPlan, orig, null);

            Assert.False(res.Failed);
            Assert.Single(moves);
            Assert.Equal((Dir("v1.0.0"), Dir("v1.1.0")), moves[0]);
            Assert.Equal("v1.1.0/main.exe", v.ExecutablePath);
            Assert.Equal("v1.1.0/thumb.png", v.ThumbnailPath);
            Assert.Equal("v1.1.0/bg.png", v.BackgroundPath);
            Assert.Equal("v1.1.0", orig[1]);   // snapshot 更新
        }

        [Fact]
        public void ExecutePlan_FirstMoveFails_NoMutation()
        {
            var existing = Set(Dir("v1.0.0"));
            var svc = Svc(existing, failOn: Dir("v1.0.0"));   // 最初の Move で失敗
            var v = V(1, "v1.1.0", exe: "v1.0.0/main.exe");
            var orig = new Dictionary<int, string> { { 1, "v1.0.0" } };

            var plan = svc.BuildPlan(GF, new[] { v }, orig);
            var res = svc.ExecutePlan(plan.OrderedPlan, orig, null);

            Assert.True(res.Failed);
            Assert.Contains("リネームに失敗", res.ErrorMessage);
            Assert.Equal("v1.0.0/main.exe", v.ExecutablePath);   // 書き換わっていない
            Assert.Equal("v1.0.0", orig[1]);                     // snapshot も元のまま
        }

        [Fact]
        public void ExecutePlan_SecondMoveFails_RollsBackFirst()
        {
            // 独立 2 件 (A: v1.0.0→v1.1.0 成功, B: v2.0.0→v2.1.0 失敗) → 完了済 A を逆順 rollback。
            var existing = Set(Dir("v1.0.0"), Dir("v2.0.0"));
            var moves = new List<(string, string)>();
            var svc = Svc(existing, moves, failOn: Dir("v2.0.0"));
            var vA = V(1, "v1.1.0", exe: "v1.0.0/a.exe");
            var vB = V(2, "v2.1.0", exe: "v2.0.0/b.exe");
            var orig = new Dictionary<int, string> { { 1, "v1.0.0" }, { 2, "v2.0.0" } };

            var plan = svc.BuildPlan(GF, new[] { vA, vB }, orig);
            var res = svc.ExecutePlan(plan.OrderedPlan, orig, null);

            Assert.True(res.Failed);
            // A は forward (v1.0.0→v1.1.0) → rollback (v1.1.0→v1.0.0) の 2 回 move が記録される。
            Assert.Contains((Dir("v1.0.0"), Dir("v1.1.0")), moves);
            Assert.Contains((Dir("v1.1.0"), Dir("v1.0.0")), moves);
            // A の in-memory state は capture 前へ復元。
            Assert.Equal("v1.0.0/a.exe", vA.ExecutablePath);
            Assert.Equal("v1.0.0", orig[1]);
        }

        [Fact]
        public void Rollback_RestoresSnapshotAndPaths_ReverseOrder()
        {
            var existing = Set(Dir("v1.0.0"));
            var moves = new List<(string, string)>();
            var svc = Svc(existing, moves);
            var v = V(1, "v1.1.0", exe: "v1.0.0/main.exe");
            var orig = new Dictionary<int, string> { { 1, "v1.0.0" } };

            var plan = svc.BuildPlan(GF, new[] { v }, orig);
            var res = svc.ExecutePlan(plan.OrderedPlan, orig, null);   // 成功 (snapshot=v1.1.0)
            Assert.False(res.Failed);

            // DB 保存失敗を想定して caller が rollback を呼ぶケース。
            svc.Rollback(res.CompletedRenames, orig, out int rolledBack, out int rollbackFailures);
            Assert.Equal(1, rolledBack);
            Assert.Equal(0, rollbackFailures);
            Assert.Equal("v1.0.0/main.exe", v.ExecutablePath);   // path 復元
            Assert.Equal("v1.0.0", orig[1]);                     // snapshot 復元
        }

        [Fact]
        public void ExecutePlan_Swap_FailsSafely_NoMutation()
        {
            // swap (A: v1.0.0→v2.0.0, B: v2.0.0→v1.0.0) は循環。BuildPlan は衝突を返さず 2 plan を作るが、
            // topological sort が cycle で UI 順 append → ExecutePlan の先行 Move が「移動先 dir 既存」で
            // 失敗 → swap は安全に失敗する仕様 (= disk/in-memory は OK 押下前のまま、部分破壊なし)。
            var existing = Set(Dir("v1.0.0"), Dir("v2.0.0"));
            var moves = new List<(string, string)>();
            var svc = Svc(existing, moves);
            var vA = V(1, "v2.0.0", exe: "v1.0.0/a.exe");
            var vB = V(2, "v1.0.0", exe: "v2.0.0/b.exe");
            var orig = new Dictionary<int, string> { { 1, "v1.0.0" }, { 2, "v2.0.0" } };

            var plan = svc.BuildPlan(GF, new[] { vA, vB }, orig);
            Assert.False(plan.HasCollision);
            Assert.Equal(2, plan.OrderedPlan.Count);

            var res = svc.ExecutePlan(plan.OrderedPlan, orig, null);

            Assert.True(res.Failed);                       // 先行 Move が dst 既存で失敗
            Assert.Empty(moves);                           // 1 件目で即失敗 = disk Move は 1 つも成立せず
            Assert.Equal("v1.0.0/a.exe", vA.ExecutablePath); // path 不変
            Assert.Equal("v2.0.0/b.exe", vB.ExecutablePath);
            Assert.Equal("v1.0.0", orig[1]);                 // snapshot 不変
            Assert.Equal("v2.0.0", orig[2]);
        }
    }
}
