using System;
using TonePrism.Manager.Services;
using Xunit;

namespace TonePrism.Manager.Tests
{
    /// <summary>
    /// (#281) `VersionInventory.ParseConfigVersion` の純ロジック検証。
    /// Launcher 版数の SoT が project.godot の `[application] config/version="X.Y.Z"` に移行したため、
    /// その 1 行を抽出する regex の挙動を固定する。特に: `config/version`(スラッシュ) を読み
    /// `config_version`(アンダースコア、Godot ファイル形式版) には誤マッチしない、という cross-component
    /// 結合点を回帰から守る (同パターンを Release.ps1 `Assert-LauncherVersion` も持つ、SPEC §3.7.8)。
    /// </summary>
    public class VersionInventoryTests
    {
        // 実物に近い project.godot 断片 (先頭の `config_version=5` 形式版 + [application] の config/version)。
        private const string RealisticProjectGodot =
            "; Engine configuration file.\n" +
            "config_version=5\n" +
            "\n" +
            "[application]\n" +
            "\n" +
            "config/name=\"TonePrism_Launcher\"\n" +
            "config/version=\"0.10.2\"\n" +
            "run/main_scene=\"res://scenes/screensaver.tscn\"\n";

        [Fact]
        public void RealisticFile_ReturnsApplicationConfigVersion_NotFileFormatVersion()
        {
            Version v = VersionInventory.ParseConfigVersion(RealisticProjectGodot);
            Assert.Equal(new Version(0, 10, 2), v);   // config/version="0.10.2" を拾い、config_version=5 は無視
        }

        [Theory]
        [InlineData("config/version=\"0.10.2\"", 0, 10, 2)]
        [InlineData("config/version=\"1.2.3\"", 1, 2, 3)]
        [InlineData("  config/version=\"4.5.6\"  ", 4, 5, 6)]   // 前後の空白を許容 (^\s* / \s*$)
        [InlineData("config/version = \"7.8.9\"", 7, 8, 9)]      // = 周りの空白を許容
        public void ValidThreePart_Parses(string line, int major, int minor, int patch)
        {
            Assert.Equal(new Version(major, minor, patch), VersionInventory.ParseConfigVersion(line));
        }

        [Fact]
        public void CrlfLineEndings_StillMatch()
        {
            string crlf = "[application]\r\nconfig/version=\"0.10.2\"\r\nrun/main_scene=\"x\"\r\n";
            Assert.Equal(new Version(0, 10, 2), VersionInventory.ParseConfigVersion(crlf));
        }

        [Theory]
        [InlineData("config_version=5")]                   // Godot ファイル形式版 (アンダースコア) は対象外
        [InlineData("config/version=\"0.10\"")]            // 2 part は不可 (3 part 必須)
        [InlineData("config/version=\"0.10.2.0\"")]        // 4 part は不可
        [InlineData("config/version='0.10.2'")]            // シングルクォートは不可
        [InlineData("config/version=0.10.2")]              // クォートなしは不可
        [InlineData("#config/version=\"0.10.2\"")]         // 行頭が config/version でない (コメント等)
        [InlineData("")]                                    // 空
        [InlineData("[application]\nconfig/name=\"x\"\n")] // config/version 行なし
        public void Invalid_ReturnsNull(string content)
        {
            Assert.Null(VersionInventory.ParseConfigVersion(content));
        }

        [Fact]
        public void NullContent_ReturnsNull()
        {
            Assert.Null(VersionInventory.ParseConfigVersion(null));
        }

        [Fact]
        public void Int32Overflow_ReturnsNull()
        {
            // regex (\d+\.\d+\.\d+) は match するが System.Version の各 part は Int32 なので TryParse 失敗 → null。
            Assert.Null(VersionInventory.ParseConfigVersion("config/version=\"99999999999.0.0\""));
        }
    }
}
