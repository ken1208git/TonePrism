using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#278 ①) `SessionConflictPolicy.ShouldWarn` の純ロジック検証。
    /// 「Launcher 単独稼働＋通常編集は警告しない／別 Manager または DB ファイル差し替え操作は警告」を固定する。
    /// </summary>
    public class SessionConflictPolicyTests
    {
        [Fact]
        public void Nobody_NoWarn()
        {
            Assert.False(SessionConflictPolicy.ShouldWarn(0, 0, "ゲーム編集"));
            Assert.False(SessionConflictPolicy.ShouldWarn(0, 0, "バックアップ復元"));
        }

        [Theory]
        [InlineData("ゲーム編集")]
        [InlineData("バックアップ復元")]      // 別 Manager がいれば操作種別を問わず警告
        [InlineData("データベース初期化")]
        public void OtherManager_AlwaysWarn(string op)
        {
            Assert.True(SessionConflictPolicy.ShouldWarn(1, 0, op));   // Manager のみ
            Assert.True(SessionConflictPolicy.ShouldWarn(1, 2, op));   // Manager + Launcher
        }

        [Theory]
        [InlineData("ゲーム追加")]
        [InlineData("ゲーム編集")]
        [InlineData("ゲームのバージョンアップ")]
        [InlineData("ゲーム削除")]
        [InlineData("バージョン追加")]
        [InlineData("ストアセクション追加")]
        [InlineData("ストアセクション編集")]
        [InlineData("ストアセクション削除")]
        [InlineData("ストアセクション並び替え")]
        [InlineData("初回説明 スライド追加")]
        [InlineData("初回説明 スライド編集")]
        [InlineData("初回説明 スライド削除")]
        [InlineData("初回説明 スライド並び替え")]
        [InlineData("ログ設定の適用")]
        [InlineData("バックアップ設定の適用")]
        [InlineData("バックアップ作成")]
        [InlineData("バックアップ削除")]
        [InlineData("アップデートスキップ")]
        public void LauncherOnly_RoutineEdit_NoWarn(string op)
        {
            // Launcher 単独稼働 (別 Manager なし) の通常編集 → 警告しない (文化祭当日に編集してよい)。
            Assert.False(SessionConflictPolicy.ShouldWarn(0, 1, op));
            Assert.False(SessionConflictPolicy.ShouldWarn(0, 3, op));
        }

        [Theory]
        [InlineData("バックアップ復元")]      // toneprism.db を File.Replace
        [InlineData("データベース初期化")]    // DB + games/ を再作成
        public void LauncherOnly_WholeDbReplacing_Warn(string op)
        {
            // Launcher がストア表示中に DB ファイルを開いているとファイル差し替えが衝突しうる → 警告維持。
            Assert.True(SessionConflictPolicy.ShouldWarn(0, 1, op));
        }

        [Theory]
        [InlineData("バックアップ復元", true)]
        [InlineData("データベース初期化", true)]
        [InlineData("ゲーム編集", false)]
        [InlineData("バックアップ作成", false)]
        [InlineData("バックアップ削除", false)]
        public void IsWholeDbReplacingOperation_Classifies(string op, bool expected)
        {
            Assert.Equal(expected, SessionConflictPolicy.IsWholeDbReplacingOperation(op));
        }
    }
}
