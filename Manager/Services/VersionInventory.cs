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
    ///   - **Launcher**: `<install>/Launcher/project.godot` の `[application] config/version="X.Y.Z"` を parse (#281)
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
        /// 同期 I/O 呼び出し (CHANGELOG read + project.godot read + FileVersionInfo) が走るので、UI thread から
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

        // (#281) Launcher 版数の SoT は project.godot の `[application] config/version="X.Y.Z"` 1 行。
        // 例:
        //   [application]
        //   config/version="0.10.1"
        // `config/version`(スラッシュ) を literal match する (line 9 の Godot ファイル形式版 `config_version`
        // (アンダースコア) には誤マッチしない)。値は 3 part SemVer (X.Y.Z)。Manager は Godot を実行できない
        // ため、ProjectSettings ではなくファイルを直接 regex parse する。
        private static readonly Regex ConfigVersionRegex = new Regex(
            "^\\s*config/version\\s*=\\s*\"(?<v>\\d+\\.\\d+\\.\\d+)\"\\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public static Version ReadLauncherVersion()
        {
            try
            {
                string path = Path.Combine(PathManager.LauncherDir, "project.godot");
                if (!File.Exists(path))
                {
                    // (#108 Phase 4 round 6 M-2) project.godot 不在の診断 trail。
                    Logger.Warn("[VersionInventory] ReadLauncherVersion: project.godot 不在 (path=" + path + ")");
                    return null;
                }
                string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
                Match m = ConfigVersionRegex.Match(content);
                if (!m.Success)
                {
                    // (#281) config/version 行が見つからない = project.godot format 想定外。
                    // SPEC §3.7.8 の同期チェックリストを更新の手掛かりとして trail を残す。
                    Logger.Warn("[VersionInventory] ReadLauncherVersion: project.godot から config/version を読み取れず "
                        + "(path=" + path + ") — SPEC §3.7.8 / project.godot [application] config/version 参照");
                    return null;
                }
                Version v;
                if (Version.TryParse(m.Groups["v"].Value, out v)) return v;
                Logger.Warn("[VersionInventory] ReadLauncherVersion: config/version parse 失敗 ('" + m.Groups["v"].Value + "')");
                return null;
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
