using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// Manager UI Phase 4 (#108) で「アップデート」タブに表示する 5 + 1 component のバージョンを採取する。
    ///
    /// 対象:
    ///   - **Bundle**: `<install>/CHANGELOG.md` の最新 `### [Bundle vX.Y.Z]` から抽出 (zip 同梱、§3.7.7)
    ///   - **Manager**: 自身の Assembly.GetName().Version (AssemblyInfo.cs)
    ///   - **Launcher**: `<install>/Launcher/version.gd` の `MAJOR / MINOR / PATCH` 定数を parse
    ///   - **Updater**: `<install>/Companions/Updater/TonePrism_Updater.exe` の FileVersionInfo
    ///   - **DB Schema**: SchemaManager.GetTargetDatabaseVersion() (= CurrentDbVersion)
    ///
    /// 全 component で「読み取り失敗 = `null` を返して UI 側で『不明』表示」の fail-soft 方針。例外を
    /// throw して MainForm_Load を巻き添えに stop させない。
    /// </summary>
    internal static class VersionInventory
    {
        /// <summary>
        /// 全 component のバージョンを 1 度に採取する。各 field は読み取り失敗時 null。
        /// 同期 I/O 呼び出し (CHANGELOG read + version.gd read + FileVersionInfo) が走るので、UI thread から
        /// 呼ぶ場合は MainForm_Load の DB 初期化と同レベルの latency (数 ms オーダー) を想定。
        /// </summary>
        public static InventorySnapshot Snapshot(int? dbSchemaVersion = null)
        {
            return new InventorySnapshot
            {
                Bundle = ReadBundleVersion(),
                Manager = ReadManagerVersion(),
                Launcher = ReadLauncherVersion(),
                Updater = ReadUpdaterVersion(),
                DbSchema = dbSchemaVersion,
            };
        }

        /// <summary>
        /// `<install>/CHANGELOG.md` の最新 Bundle entry を読む。
        /// File 不在 (= pre-Phase 4 install) / 破損 / Bundle entry なしの場合は null。
        /// </summary>
        public static Version ReadBundleVersion()
        {
            try
            {
                BundleEntry entry = ChangelogParser.TryReadLatestFromFile(PathManager.BundleChangelogPath);
                if (entry == null)
                {
                    // (#108 Phase 4 round 6 M-2) silent null trail: CHANGELOG 不在 / parse 失敗時に診断手掛かりを残す。
                    Logger.Warn("[VersionInventory] ReadBundleVersion: CHANGELOG.md parse 失敗 (path=" + PathManager.BundleChangelogPath + ")");
                    return null;
                }
                return entry.Version;
            }
            catch (Exception ex)
            {
                Logger.Warn("[VersionInventory] ReadBundleVersion 例外: " + ex.Message);
                return null;
            }
        }

        public static Version ReadManagerVersion()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
            catch (Exception ex)
            {
                // (#108 Phase 4 round 6 M-2) Assembly version 取得失敗は通常起きないが診断 trail を残す。
                Logger.Warn("[VersionInventory] ReadManagerVersion 例外: " + ex.Message);
                return null;
            }
        }

        // version.gd は GDScript で:
        //   const MAJOR: int = 0
        //   const MINOR: int = 5
        //   const PATCH: int = 17
        // という 3 行を持つ。各行を regex で抽出。コメント / 空行 / 順序入れ替えにも耐える形で書く。
        private static readonly Regex MajorRegex = new Regex(@"^\s*const\s+MAJOR\s*:\s*int\s*=\s*(?<v>\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex MinorRegex = new Regex(@"^\s*const\s+MINOR\s*:\s*int\s*=\s*(?<v>\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex PatchRegex = new Regex(@"^\s*const\s+PATCH\s*:\s*int\s*=\s*(?<v>\d+)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public static Version ReadLauncherVersion()
        {
            try
            {
                string path = Path.Combine(PathManager.LauncherDir, "version.gd");
                if (!File.Exists(path))
                {
                    // (#108 Phase 4 round 6 M-2) version.gd 不在の診断 trail。
                    Logger.Warn("[VersionInventory] ReadLauncherVersion: version.gd 不在 (path=" + path + ")");
                    return null;
                }
                string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
                int? major = TryReadInt(MajorRegex, content);
                int? minor = TryReadInt(MinorRegex, content);
                int? patch = TryReadInt(PatchRegex, content);
                if (!major.HasValue || !minor.HasValue || !patch.HasValue)
                {
                    // (#108 Phase 4 round 6 M-2) 3 regex のどれかが miss = version.gd format 想定外
                    // (型注釈削除 / rename / inline comment 等)。SPEC §3.7.8 の同期チェックリストと
                    // version.gd 側 `DO NOT CHANGE FORMAT` コメントを更新の手掛かりとして trail を残す。
                    Logger.Warn("[VersionInventory] ReadLauncherVersion: version.gd format 想定外 "
                        + "(major=" + (major.HasValue ? major.Value.ToString() : "miss")
                        + " minor=" + (minor.HasValue ? minor.Value.ToString() : "miss")
                        + " patch=" + (patch.HasValue ? patch.Value.ToString() : "miss")
                        + ") — SPEC §3.7.8 / version.gd の DO NOT CHANGE FORMAT 参照");
                    return null;
                }
                return new Version(major.Value, minor.Value, patch.Value);
            }
            catch (Exception ex)
            {
                Logger.Warn("[VersionInventory] ReadLauncherVersion 例外: " + ex.Message);
                return null;
            }
        }

        public static Version ReadUpdaterVersion()
        {
            try
            {
                string path = PathManager.UpdaterExePath;
                if (!File.Exists(path))
                {
                    // (#108 Phase 4 round 6 M-2) Updater 不在 (= pre-Phase 3 install) の診断 trail。
                    Logger.Warn("[VersionInventory] ReadUpdaterVersion: Updater.exe 不在 (path=" + path + ")");
                    return null;
                }
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                // FileVersionInfo.FileVersion は string、ProductVersion とは別。AssemblyFileVersion を反映。
                Version v;
                if (Version.TryParse(info.FileVersion, out v)) return v;
                Logger.Warn("[VersionInventory] ReadUpdaterVersion: FileVersion parse 失敗 ('" + (info.FileVersion ?? "(null)") + "')");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warn("[VersionInventory] ReadUpdaterVersion 例外: " + ex.Message);
                return null;
            }
        }

        private static int? TryReadInt(Regex regex, string content)
        {
            Match m = regex.Match(content);
            if (!m.Success) return null;
            int v;
            return int.TryParse(m.Groups["v"].Value, out v) ? (int?)v : null;
        }
    }

    /// <summary>
    /// `VersionInventory.Snapshot()` の戻り値。各 field は読み取り失敗時 null (UI で「不明」表示)。
    /// </summary>
    internal sealed class InventorySnapshot
    {
        public Version Bundle { get; set; }
        public Version Manager { get; set; }
        public Version Launcher { get; set; }
        public Version Updater { get; set; }
        public int? DbSchema { get; set; }
    }
}
