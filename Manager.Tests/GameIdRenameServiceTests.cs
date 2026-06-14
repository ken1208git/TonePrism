using System;
using System.Collections.Generic;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#242 ③) EditGameForm.btnOK_Click から抽出した gameId rename ロジックの回帰テスト。
    /// disk 操作 (Directory.Exists/Move) と DB 更新 (UpdateGameId) を fake に差し替え、衝突判定 3 経路と
    /// Move + DB 更新 + DB 失敗時 rollback を deterministic に検証する。
    /// </summary>
    public class GameIdRenameServiceTests
    {
        private const string Old = @"C:\games\g1";
        private const string New = @"C:\games\g2";

        private static HashSet<string> Set(params string[] dirs) => new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);

        // updateGameId は dbThrows なら InvalidOperationException、そうでなければ dbCalls に記録。
        // move は src を消して dst を足す (実 disk の挙動を模倣) + moves に記録。
        private static GameIdRenameService Svc(HashSet<string> existing,
            List<(string src, string dst)> moves = null, List<(string oldId, string newId)> dbCalls = null, bool dbThrows = false)
        {
            return new GameIdRenameService(
                updateGameId: (oldId, newId) =>
                {
                    if (dbThrows) throw new InvalidOperationException("db fail");
                    dbCalls?.Add((oldId, newId));
                },
                dirExists: p => existing.Contains(p),
                move: (src, dst) =>
                {
                    moves?.Add((src, dst));
                    existing.Remove(src);
                    existing.Add(dst);
                });
        }

        // ===== DecideCollision =====

        [Fact]
        public void DecideCollision_BothExist_Collision()
            => Assert.Equal(GameIdRenameService.CollisionDecision.Collision,
                Svc(Set(Old, New)).DecideCollision(Old, New));

        [Fact]
        public void DecideCollision_OldMissingNewExists_NeedsRecoveryConfirm()
            => Assert.Equal(GameIdRenameService.CollisionDecision.NeedsRecoveryConfirm,
                Svc(Set(New)).DecideCollision(Old, New));

        [Fact]
        public void DecideCollision_NewMissing_Proceed()
            => Assert.Equal(GameIdRenameService.CollisionDecision.Proceed,
                Svc(Set(Old)).DecideCollision(Old, New));

        [Fact]
        public void DecideCollision_BothMissing_Proceed()
            => Assert.Equal(GameIdRenameService.CollisionDecision.Proceed,
                Svc(Set()).DecideCollision(Old, New));

        // ===== Execute =====

        [Fact]
        public void Execute_OldExists_MovesAndUpdatesDb_ReturnsTrue()
        {
            var existing = Set(Old);
            var moves = new List<(string, string)>();
            var dbCalls = new List<(string, string)>();
            var svc = Svc(existing, moves, dbCalls);

            bool moved = svc.Execute("g1", "g2", Old, New, null);

            Assert.True(moved);
            Assert.Single(moves);
            Assert.Equal((Old, New), moves[0]);
            Assert.Single(dbCalls);
            Assert.Equal(("g1", "g2"), dbCalls[0]);
        }

        [Fact]
        public void Execute_OldMissing_SkipsMove_UpdatesDb_ReturnsFalse()
        {
            // (b) recovery で既存フォルダ流用 = 旧フォルダ不在。Move skip、DB のみ更新。
            var existing = Set(New);
            var moves = new List<(string, string)>();
            var dbCalls = new List<(string, string)>();
            var svc = Svc(existing, moves, dbCalls);

            bool moved = svc.Execute("g1", "g2", Old, New, null);

            Assert.False(moved);
            Assert.Empty(moves);
            Assert.Single(dbCalls);
            Assert.Equal(("g1", "g2"), dbCalls[0]);   // recovery (b) 経路でも DB に正しい (old,new) が渡る
        }

        [Fact]
        public void Execute_DbFails_RollsBackMove_Rethrows()
        {
            var existing = Set(Old);
            var moves = new List<(string, string)>();
            var svc = Svc(existing, moves, dbThrows: true);

            Assert.Throws<InvalidOperationException>(() => svc.Execute("g1", "g2", Old, New, null));

            // forward Move (Old→New) → DB 失敗 → rollback Move (New→Old) の 2 件が記録される。
            Assert.Equal(2, moves.Count);
            Assert.Equal((Old, New), moves[0]);
            Assert.Equal((New, Old), moves[1]);
            // disk は元に戻っている (Old が存在、New は無い)。
            Assert.Contains(Old, existing);
            Assert.DoesNotContain(New, existing);
        }
    }
}
