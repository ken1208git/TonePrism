using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#373 / PR #374) `DatabaseConnection.ToSqliteDataSource` の純粋文字列正規化を固定する。
    /// System.Data.SQLite の native は生 UNC (`\\server\share`) を open できず forward slash
    /// (`//server/share`) なら通るため、UNC のときだけ `\`→`/` 変換する。マップドライブ / ローカル /
    /// null / 既に `/` / extended-length (`\\?\`) は無変換。ネットワーク共有を要しない純ロジックなので
    /// CI で安定。`StartsWith(@"\\")` ガードの境界が将来の誤改変で壊れるのを防ぐ回帰テスト。
    /// </summary>
    public class DatabaseConnectionDataSourceTests
    {
        [Theory]
        [InlineData(@"\\OBS-1M24\TonePrism_Release\TonePrism\toneprism.db",
                    "//OBS-1M24/TonePrism_Release/TonePrism/toneprism.db")]
        [InlineData(@"\\srv\share\toneprism.db", "//srv/share/toneprism.db")]
        public void Unc_IsConvertedToForwardSlash(string input, string expected)
            => Assert.Equal(expected, DatabaseConnection.ToSqliteDataSource(input));

        [Theory]
        [InlineData(@"Z:\TonePrism\toneprism.db")]   // マップドライブ (本番運用形態)
        [InlineData(@"C:\Users\x\TonePrism\toneprism.db")]
        [InlineData(@"D:\toneprism.db")]
        public void MappedOrLocal_IsUnchanged(string path)
            => Assert.Equal(path, DatabaseConnection.ToSqliteDataSource(path));

        [Fact]
        public void Null_IsUnchanged()
            => Assert.Null(DatabaseConnection.ToSqliteDataSource(null));

        [Fact]
        public void ForwardSlashUnc_IsIdempotent()
            // 既に / のものは二重変換しない (\\ 始まりでないため無変換)。
            => Assert.Equal("//srv/share/toneprism.db",
                DatabaseConnection.ToSqliteDataSource("//srv/share/toneprism.db"));

        [Fact]
        public void ExtendedLengthPrefix_IsNotConverted()
            // \\?\ は native が別扱いで変換後 (//?/) の挙動が未実証のため意図的に対象外。
            => Assert.Equal(@"\\?\UNC\srv\share\toneprism.db",
                DatabaseConnection.ToSqliteDataSource(@"\\?\UNC\srv\share\toneprism.db"));
    }
}
