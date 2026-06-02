using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#250 C1) 長パスプレフィックス付与の文字列変換を検証する。最重要は **UNC 分岐**:
    /// 旧実装は "\\?\" + path を常に付け、UNC (\\server\share) に対し "\\?\\\server\..." という Win32 構文不正
    /// パスを生成していた。これは SMB 上の Directory.GetFiles 等を "syntax is incorrect" で全件失敗させ、アセット
    /// 控えを silent に空のまま Success 扱いにする欠陥だった。実 SMB が無くても変換結果は決定的に検証できるので、
    /// ここで回帰を固定する (実列挙は実機 SMB 検証に委ねる、F-1)。
    /// </summary>
    public class FileOperationServiceLongPathTests
    {
        [Fact]
        public void ForceLongPath_LocalPath_AddsBackslashQmark()
        {
            Assert.Equal(@"\\?\C:\foo\bar", FileOperationService.ForceLongPath(@"C:\foo\bar"));
        }

        [Fact]
        public void ForceLongPath_UncPath_UsesUncPrefix()
        {
            // \\server\share\games → \\?\UNC\server\share\games (旧実装は \\?\\\server\... で構文不正だった)
            Assert.Equal(@"\\?\UNC\server\share\games", FileOperationService.ForceLongPath(@"\\server\share\games"));
        }

        [Fact]
        public void ForceLongPath_AlreadyPrefixed_LeftUnchanged()
        {
            Assert.Equal(@"\\?\C:\x", FileOperationService.ForceLongPath(@"\\?\C:\x"));
            Assert.Equal(@"\\?\UNC\server\share\x", FileOperationService.ForceLongPath(@"\\?\UNC\server\share\x"));
        }

        [Fact]
        public void EnsureLongPath_ShortUnc_NoPrefix()
        {
            // 240 字未満の UNC は素のまま (素の UNC は通常の Win32 API で問題なく開ける)
            Assert.Equal(@"\\server\share\games", FileOperationService.EnsureLongPath(@"\\server\share\games"));
        }

        [Fact]
        public void EnsureLongPath_LongUnc_UsesUncPrefix()
        {
            string longUnc = @"\\server\share\" + new string('a', 240);
            Assert.Equal(@"\\?\UNC\server\share\" + new string('a', 240), FileOperationService.EnsureLongPath(longUnc));
        }

        [Fact]
        public void ForceLongPath_VeryLongLocalPath_DoesNotThrow_AndPrefixes()
        {
            // (round9 B-1) 260 字超の入力で legacy path handling の Path.GetFullPath が PathTooLongException を投げても、
            // 長パス安全化関数自身が落ちず正しい \\?\ 形を返す (自己矛盾の解消)。長パス対応が有効な環境では GetFullPath が
            // throw しないが、出力が正しいことはどちらの経路でも保証される。
            string longLocal = @"C:\" + new string('a', 300);
            Assert.Equal(@"\\?\C:\" + new string('a', 300), FileOperationService.ForceLongPath(longLocal));
        }

        [Fact]
        public void ForceLongPath_VeryLongUncPath_DoesNotThrow_AndUncPrefixes()
        {
            string longUnc = @"\\srv\share\" + new string('a', 300);
            Assert.Equal(@"\\?\UNC\srv\share\" + new string('a', 300), FileOperationService.ForceLongPath(longUnc));
        }

        [Fact]
        public void NormalizePath_StripsUncLongPrefix_Symmetric()
        {
            // ForceLongPath/EnsureLongPath が付ける UNC プレフィックスの逆変換 (剥がし漏れで相対パス化しないこと)
            Assert.Equal(@"\\server\share\x", FileOperationService.NormalizePath(@"\\?\UNC\server\share\x"));
        }

        [Fact]
        public void NormalizePath_StripsLocalLongPrefix()
        {
            Assert.Equal(@"C:\x", FileOperationService.NormalizePath(@"\\?\C:\x"));
        }
    }
}
