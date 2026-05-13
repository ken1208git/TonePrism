using System;
using System.IO;
using System.Linq;
using System.Text;

namespace GCTonePrism.Updater
{
    /// <summary>
    /// Updater 用 minimal logger。
    ///
    /// Manager の Services/Logger.cs を簡略化:
    /// - ファイル出力 + Console 同時出力 (CLI ツールなので Console 表示も残して spawn 元 Manager が
    ///   stdout を redirect すれば取り込める)
    /// - INFO / WARN / ERROR の 3 段階
    /// - 1 起動 = 1 ファイル: `updater_&lt;PCname&gt;_&lt;YYYY-MM-DD_HHmmss&gt;.log`
    /// - Console.SetOut フックは不要 (この CLI は自前で Logger を呼ぶだけ、サードパーティ stdout 出力なし)
    /// - 30 日 retention は Manager と同じ
    /// - 自身の例外で無限ループしないよう catch で握り潰し
    ///
    /// 関連: SPEC §3.6 ファイルログ基盤要件、SPEC §3.7.4 Updater ログ実装。
    /// </summary>
    public static class Logger
    {
        private const int RetentionDays = 30;
        private const string FileNamePrefix = "updater_";
        private const string FileNameSuffix = ".log";

        private static readonly object _lock = new object();
        private static StreamWriter _writer;
        private static string _currentLogPath;
        private static string _logDirectory;
        private static bool _initialized;

        public static string CurrentLogPath
        {
            get { lock (_lock) { return _currentLogPath; } }
        }

        /// <summary>
        /// ロガーを初期化する。Main の早い段階で呼ぶ。
        /// 失敗しても CLI 実行自体は止めない (例外を握り潰し、以降の API は Console 出力のみ)。
        /// </summary>
        /// <param name="logDirectory">ログ出力先ディレクトリ。通常 Program.cs から `<install>/logs/updater/` を渡される (SPEC §3.7.4)。null/empty の場合は exe-relative fallback (通常運用では到達しないが、drive root 病的入力等の極限ケース用に残してある defensive fallback)。</param>
        public static void Initialize(string logDirectory)
        {
            lock (_lock)
            {
                if (_initialized) return;
                // NOTE: シニアレビュー round 1 L6: `_initFailed` ガードは持たない (Manager Logger と
                // 非対称)。Updater は Main() で 1 回しか Initialize を呼ばない設計なので、init 失敗時
                // に再 init を防ぐガードは不要。冪等性は `_initialized` の単純な return で十分。

                try
                {
                    // round 4 M-4: Console.OutputEncoding を UTF-8 に明示。
                    //   default は Windows console の codepage (日本語 Windows = Shift-JIS / CP932)。
                    //   Phase 4 で Manager UI が `RedirectStandardOutput=true` で stdout を log viewer に
                    //   流す前提だが、Manager UI 側 encoding が CP932 と合っていなければ「[Step 1/3]
                    //   Manager プロセスの終了を待機」が mojibake する path があった。ファイルログは
                    //   既に UTF-8 BOM なし (FileStream + UTF8Encoding(false))、Console も UTF-8 に
                    //   統一して Manager UI / 部員視点の log viewer で文字化けしない規約を確立。
                    //   失敗時 (テスト環境で Console 不在等) は best-effort で握り潰し、CLI 動作には影響なし。
                    try { Console.OutputEncoding = Encoding.UTF8; } catch { /* best-effort */ }

                    // 通常 Program.cs が <install>/logs/updater/ を計算済みで渡してくる (SPEC §3.7.4)。
                    // 病的入力 (manager-target が drive root 等) で空が渡る場合のみ exe-relative
                    // fallback に流れる。シニアレビュー round 1 M1 / L3 で「通常 path は Program 側
                    // 確定、Logger fallback は defensive」方針 (round 3 M5 で「到達不能」表現を訂正、
                    // 極限ケース用に残してある安全網、消さないこと)。
                    _logDirectory = string.IsNullOrEmpty(logDirectory)
                        ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "updater")
                        : logDirectory;
                    Directory.CreateDirectory(_logDirectory);

                    OpenSessionFile();
                    _initialized = true;

                    WriteInternal("INFO", $"[Logger] Updater 起動 (PC={Environment.MachineName})");
                    WriteInternal("INFO", $"[Logger] ログ出力先: {_currentLogPath}");
                    CleanupOldLogs();
                }
                catch (Exception ex)
                {
                    // ファイルログが立てられなくとも Console は生きているので、CLI は続行可能
                    Console.Error.WriteLine($"[WARN] ログファイル初期化失敗、Console のみで続行: {ex.Message}");
                }
            }
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message) { Write("ERROR", message); }

        public static void Error(string message, Exception ex)
        {
            if (ex == null) { Write("ERROR", message); return; }
            Write("ERROR", $"{message}{Environment.NewLine}{ex}");
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                try
                {
                    if (_initialized)
                    {
                        WriteInternal("INFO", "[Logger] Updater 終了");
                    }
                    if (_writer != null)
                    {
                        _writer.Flush();
                        _writer.Dispose();
                        _writer = null;
                    }
                    // round 4 L-2: Shutdown 後に CurrentLogPath が古い path を返す stale 状態を解消。
                    // 本 CLI は Main で 1 回 Shutdown して即 exit するので実害はないが、test code や
                    // 将来の re-initialize で `Shutdown → CurrentLogPath getter` が前回 path を返す
                    // 混乱源を断つ。
                    _currentLogPath = null;
                    _initialized = false;
                }
                catch { /* swallow */ }
            }
        }

        private static void Write(string level, string message)
        {
            // Console は常に出す (Logger 初期化前 / 失敗時でも spawn 元 Manager が拾える)
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string formatted = $"[{ts}] [{level}] {message ?? string.Empty}";
            if (level == "ERROR" || level == "WARN")
            {
                Console.Error.WriteLine(formatted);
            }
            else
            {
                Console.Out.WriteLine(formatted);
            }

            lock (_lock)
            {
                if (!_initialized) return;
                WriteInternal(level, message);
            }
        }

        private static void WriteInternal(string level, string message)
        {
            try
            {
                if (_writer == null) return;
                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _writer.WriteLine($"[{ts}] [{level}] {message ?? string.Empty}");
                _writer.Flush();
            }
            catch
            {
                // Logger 自身の例外は絶対にログに書かない (再入ループ防止)
            }
        }

        private static void OpenSessionFile()
        {
            string pcName = SanitizeFileName(Environment.MachineName);
            string startTs = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            string baseName = $"{FileNamePrefix}{pcName}_{startTs}";
            string path = Path.Combine(_logDirectory, $"{baseName}{FileNameSuffix}");

            int counter = 2;
            while (File.Exists(path) && counter < 100)
            {
                path = Path.Combine(_logDirectory, $"{baseName}_{counter}{FileNameSuffix}");
                counter++;
            }
            // シニアレビュー round 2 L6: counter 100 到達時の silent fallback を可視化。
            // 通常運用 (1 起動 = 1 Initialize) では発火しないが、Phase 4 で Manager UI が retry loop
            // を組んだ場合 / 自動テスト時に発火可能。loop 抜け時はファイル衝突状態のままで
            // CreateNew → IOException → Initialize catch で握り潰し → Console のみで続行、
            // という silent fallback path に流れる。Console に Warn だけは残す。
            //
            // round 3 L3: 「100 件」表記は loop 実体と off-by-one だった (counter は 2 → 99 までで
            // 抜け、試した候補は base name + suffix _2 〜 _99 で計 99 件)。表記を正確化。
            if (counter >= 100 && File.Exists(path))
            {
                Console.Error.WriteLine($"[WARN] [Logger] 同名ログファイル多すぎ (base + suffix _2 〜 _99 の 99 件全て衝突) — Console のみで続行する可能性: {path}");
            }

            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _currentLogPath = path;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            return sb.ToString();
        }

        private static void CleanupOldLogs()
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (string path in Directory.EnumerateFiles(_logDirectory, $"{FileNamePrefix}*{FileNameSuffix}"))
                {
                    try
                    {
                        if (string.Equals(path, _currentLogPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (File.GetLastWriteTime(path) < cutoff)
                        {
                            File.Delete(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteInternal("WARN", $"[Logger] 古いログファイル削除失敗 ({path}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteInternal("WARN", $"[Logger] 古いログ掃除中にエラー: {ex.Message}");
            }
        }
    }
}
