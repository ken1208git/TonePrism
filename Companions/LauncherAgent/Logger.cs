using System;
using System.IO;
using System.Text;

namespace TonePrism.LauncherAgent
{
    /// <summary>
    /// LauncherAgent 専用のファイルロガー (Companions/Updater/Logger.cs を簡略移植)。
    /// - 出力先: &lt;logsRoot&gt;/launchercompanion/launchercompanion_&lt;PC&gt;_&lt;時刻&gt;.log (UTF-8 no BOM)
    /// - 1 起動 = 1 ファイル / 30 日 retention / lock で thread-safe / **自身の例外は握り潰し** (再帰ハング防止)
    /// - WARN / ERROR / Milestone は <see cref="Forwarder"/> 経由で Launcher に転送される
    ///   (Launcher 側が自分のログに [LauncherAgent] 付きで記録 → Manager の Launcher タブに出る)。
    ///   詳細 (Info) は本ファイルのみ。
    /// </summary>
    internal static class Logger
    {
        private static readonly object _lock = new object();
        private static StreamWriter _writer;
        private static bool _disabled;

        /// <summary>(level, msg) を Launcher へ転送するデリゲート。Program が UDP 送信を設定する。</summary>
        public static Action<string, string> Forwarder;

        public static void Init(string logsRoot)
        {
            try
            {
                string dir = string.IsNullOrEmpty(logsRoot)
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "launchercompanion")
                    : Path.Combine(logsRoot, "launchercompanion");
                Directory.CreateDirectory(dir);
                CleanupOld(dir);

                string pc = SafePcName();
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                string path = Path.Combine(dir, "launchercompanion_" + pc + "_" + stamp + ".log");
                _writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
            }
            catch
            {
                _disabled = true; // ログ基盤の失敗はアプリを止めない
            }
        }

        public static void Info(string msg) { Write("INFO", msg, false); }
        /// <summary>主要イベント (起動/終了/トリガ等)。INFO だが Launcher へ転送する。</summary>
        public static void Milestone(string msg) { Write("INFO", msg, true); }
        public static void Warn(string msg) { Write("WARN", msg, true); }
        public static void Error(string msg, Exception ex = null)
        {
            Write("ERROR", ex == null ? msg : msg + ": " + ex.GetType().Name + ": " + ex.Message, true);
        }

        private static void Write(string level, string msg, bool forward)
        {
            try
            {
                if (!_disabled && _writer != null)
                {
                    lock (_lock)
                    {
                        _writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + level + "] " + msg);
                    }
                }
            }
            catch { /* Logger 内部例外は握り潰す */ }

            if (forward)
            {
                try { Forwarder?.Invoke(level, msg); } catch { /* 転送失敗も握り潰す */ }
            }
        }

        private static void CleanupOld(string dir)
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-30);
                foreach (string f in Directory.GetFiles(dir, "launchercompanion_*.log"))
                {
                    try { if (File.GetLastWriteTime(f) < cutoff) File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        private static string SafePcName()
        {
            string name = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (string.IsNullOrEmpty(name)) name = "unknown";
            foreach (char c in new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
