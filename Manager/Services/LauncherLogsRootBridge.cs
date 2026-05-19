using System;
using System.IO;
using System.Text;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// `<install>/responses/launcher_logs_root.json` の atomic write helper。
    ///
    /// Manager 側の SoT (= SQLite `logs_root_path` 設定) を Launcher が autoload 最先頭 init 時に
    /// 読み取れる形で公開する drop file pattern (= SessionHeartbeat の atomic write pattern を踏襲、
    /// SPEC §6.5 「Launcher は SQLite write しない」原則を維持しつつ Manager → Launcher 単方向で
    /// 設定値を伝搬する経路)。
    ///
    /// Schema (JSON):
    /// <code>
    /// {
    ///   "schema_version": 1,
    ///   "logs_root_path": "&lt;absolute path or empty for default&gt;",
    ///   "updated_at_unix_ms": 1234567890000
    /// }
    /// </code>
    ///
    /// 呼出箇所:
    /// 1. `Program.Main` の Logger.Initialize 直後 (= 毎起動時 sync、user が直接 file を削除した case を回復)
    /// 2. `SettingsSectionPanel.SaveLogDestIfChanged` 内 (= UI 変更時即時 sync、Launcher 次回起動で picked up)
    ///
    /// 例外は内部で握り潰し、Manager 起動 / UI 操作を阻害しない (= SPEC §3.6 「Logger 自身の障害は
    /// 握り潰す」と同じ defensive 規約)。
    /// </summary>
    public static class LauncherLogsRootBridge
    {
        private const string FileName = "launcher_logs_root.json";

        /// <summary>
        /// 現在の `logs_root_path` 値 (= 設定が空なら "" を渡す) を JSON file に atomic write する。
        /// 書出先 dir が存在しなければ作成、既存 file は overwrite。
        /// </summary>
        public static void WriteCurrentLogsRoot(string logsRootPath)
        {
            try
            {
                string responsesDir = Path.Combine(PathManager.BaseDirectory, "responses");
                Directory.CreateDirectory(responsesDir);
                string targetPath = Path.Combine(responsesDir, FileName);
                string tmpPath = targetPath + ".tmp";

                long unixMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                string normalized = logsRootPath ?? string.Empty;
                string json = "{"
                    + "\"schema_version\":1,"
                    + "\"logs_root_path\":\"" + EscapeJsonString(normalized) + "\","
                    + "\"updated_at_unix_ms\":" + unixMs
                    + "}";

                File.WriteAllText(tmpPath, json, new UTF8Encoding(false));
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tmpPath, targetPath);
            }
            catch (Exception ex)
            {
                // Logger 未初期化の case もあるため try-catch で防御 (= Program.Main 初期段階呼出 path 用)
                try { Logger.Warn("[LauncherLogsRootBridge] launcher_logs_root.json 書込失敗: " + ex.Message); }
                catch { /* swallow (= Logger 自体が未初期化 / 失敗中) */ }
            }
        }

        /// <summary>
        /// minimal JSON string escape (`\` と `"` のみ)。schema_version 1 で文字列値は path のみなので、
        /// surrogate pair / unicode escape まで対応する必要なし (= Windows path に含まれない)。
        /// </summary>
        private static string EscapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
