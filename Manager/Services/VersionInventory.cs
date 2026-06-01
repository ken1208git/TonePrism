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
    ///   - **Launcher**: `<install>/Launcher/TonePrism_Launcher.exe` の FileVersionInfo を読む (#283、Updater と同方式)。
    ///     exe 不在の dev では `<repo>/Launcher/project.godot` の `config/version` parse に fallback (#281 の機構を dev 用に残置)
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
        /// 同期 I/O 呼び出し (CHANGELOG read + Launcher/Updater exe の FileVersionInfo + dev は project.godot read) が走るので、UI thread から
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

        // (#281/#283) dev fallback 用の project.godot config/version parser。
        // SoT は project.godot の `[application] config/version="X.Y.Z"` 1 行 (例: `config/version="0.10.2"`)。
        // `config/version`(スラッシュ) を literal match する (line 9 の Godot ファイル形式版 `config_version`
        // (アンダースコア) には誤マッチしない)。値は 3 part SemVer (X.Y.Z)。
        // ※同一パターンを Release.ps1 `Assert-LauncherVersion` も持つ。project.godot の format が変わったら
        //   両方を同期更新すること (SPEC §3.7.8 チェックリスト)。本パターンの回帰は `VersionInventoryTests` が守る。
        private static readonly Regex ConfigVersionRegex = new Regex(
            "^\\s*config/version\\s*=\\s*\"(?<v>\\d+\\.\\d+\\.\\d+)\"\\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Launcher 版数を読む。
        /// (#283) **prod**: エクスポート済み `TonePrism_Launcher.exe` の FileVersionInfo を読む。**読み取り API** は
        /// Updater の <see cref="ReadUpdaterVersion"/> と同じ FileVersionInfo 方式だが、**版数を exe に焼く機構は別系統**:
        /// Updater は .NET の AssemblyFileVersion 属性、Launcher は export_presets.cfg の `application/file_version` を
        /// Release.ps1 `Set-ExportPresetVersions` が SoT (project.godot config/version) から stamp → Godot/rcedit が
        /// exe の VERSIONINFO リソースに焼いた派生値。後者は本変更で初めて prod の版数 SoT として consume するため、
        /// stamp 経路の不発を `Release.ps1 Assert-ExportedLauncherVersion` が公開前に hard fail で守る。
        /// **dev**: リポジトリには exe を置かないので、exe 不在時は `<repo>/Launcher/project.godot` の
        /// config/version 直 parse に fallback する (= dev でも版数が「不明」にならない、#281 の機構を残置)。
        /// 失敗時は null (UI で「不明」表示) の fail-soft。
        /// </summary>
        public static Version ReadLauncherVersion()
        {
            try
            {
                // prod: exe の FileVersionInfo を優先。
                string exePath = PathManager.LauncherExePath;
                bool exeExists = File.Exists(exePath);
                if (exeExists)
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                    Version exeVer;
                    if (Version.TryParse(info.FileVersion, out exeVer)) return exeVer;
                    // exe はあるが FileVersion が読めない (= rcedit stamp 不発等)。prod に project.godot は
                    // 同梱しないため下の fallback は no-op になり最終 warn に流れる。診断 trail を残す。
                    Logger.Warn("[VersionInventory] ReadLauncherVersion: Launcher exe の FileVersion を parse できず ('"
                        + (info.FileVersion ?? "(null)") + "', path=" + exePath + ")");
                }

                // dev fallback: exe 不在 (リポジトリ作業時) は repo の project.godot を直 parse。
                string projPath = Path.Combine(PathManager.LauncherDir, "project.godot");
                if (File.Exists(projPath))
                {
                    Version projVer = ParseConfigVersion(File.ReadAllText(projPath, System.Text.Encoding.UTF8));
                    if (projVer == null)
                    {
                        Logger.Warn("[VersionInventory] ReadLauncherVersion: project.godot fallback でも config/version を読み取れず "
                            + "(path=" + projPath + ") — SPEC §3.7.8 / project.godot [application] config/version 参照");
                    }
                    return projVer;
                }

                // 版数を特定できず。exe の有無で原因を分けて trail を残す (exe あり=FileVersion 読取不可で
                // rcedit stamp 不発の疑い / exe 不在=broken install の疑い)。前者は project.godot 同梱廃止 (#283) の
                // ため fallback で拾えない経路だが、本来 Assert-ExportedLauncherVersion が公開前に止めるべきもの。
                Logger.Warn("[VersionInventory] ReadLauncherVersion: 版数を特定できず — "
                    + (exeExists
                        ? "Launcher exe (" + exePath + ") はあるが FileVersion 読取不可、かつ project.godot fallback も不在 (prod の rcedit stamp 不発の疑い)"
                        : "Launcher exe (" + exePath + ") も project.godot も不在 (broken install の疑い)"));
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warn("[VersionInventory] ReadLauncherVersion 例外: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// (#281) project.godot の本文から `config/version="X.Y.Z"` を抽出する純関数 (テスト可能なように I/O から分離)。
        /// (#283 で dev fallback 専用になったが parser 自体は不変)。該当行なし / 値が 3 part SemVer でない /
        /// 各 part が Int32 超過 (TryParse 失敗) の場合は null。
        /// </summary>
        internal static Version ParseConfigVersion(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            Match m = ConfigVersionRegex.Match(content);
            if (!m.Success) return null;
            Version v;
            return Version.TryParse(m.Groups["v"].Value, out v) ? v : null;
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
