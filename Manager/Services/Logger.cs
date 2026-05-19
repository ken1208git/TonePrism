using System;
using System.IO;
using System.Linq;
using System.Text;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// Manager 全体のファイルログ機構 (#116)。
    ///
    /// - 1 起動セッション = 1 ファイル (`manager_<PCname>_<YYYY-MM-DD_HHmmss>.log`)
    /// - 保存先: `<project_root>/logs/manager/`（toneprism.db のあるディレクトリの隣）
    ///   → 共有上の toneprism.db と同じ場所に集約することで、複数 PC の Launcher / Manager ログを 1 箇所で見られる
    ///   → セッション単位でファイルが分かれるので書き込み競合・行間 interleaving が発生しない
    /// - INFO / WARN / ERROR の 3 段階
    /// - Console.SetOut フックで既存 Console.WriteLine も自動的にファイルへ流す (INFO 扱い)
    /// - 古いログファイル削除は `CleanupOldLogs(int retentionDays)` を caller (= Program.Main) が
    ///   `Initialize` 直後に明示呼出する (= Logger 自体は SettingsRepository に依存しない設計)。
    ///   retention 値は Program.Main 側で SQLite 直接 read 経由で取得 (DB 不在時は default 30 日 fallback)。
    ///   旧実装は `Initialize` 内で 30 日 hardcode 削除 + round 1 では MainForm で呼出していたが、
    ///   後者は dbReady=false 等の early-return path で到達不能になる regression があり、
    ///   round 3 H-1 fix で Program.Main 経由に再移動 (= 全起動 path で確実に走る)。
    /// - スレッドセーフ (内部 lock。lock は同一スレッドで再入可能)
    /// - 自身の例外で無限ループ・アプリ起動阻害を起こさない (try-catch で握り潰し)
    ///
    /// 関連: 将来 #85 (Launcher 統一ログ基盤フル仕様) で DEBUG 追加 / [エラーコード] フィールド /
    /// リングバッファ / サービスモード連携が拡張される。本クラスはその土台先行。
    /// </summary>
    public static class Logger
    {
        private const string FileNamePrefix = "manager_";
        private const string FileNameSuffix = ".log";
        private const string LogSubDirectory = "manager";

        private static readonly object _lock = new object();
        private static StreamWriter _writer;
        private static string _currentLogPath;
        private static string _logDirectory;
        private static bool _initialized;
        private static bool _initFailed;
        private static TextWriter _previousConsoleOut;

        /// <summary>
        /// 現在書き込み中のログファイル絶対パス (検証・診断用)
        /// </summary>
        public static string CurrentLogPath
        {
            get { lock (_lock) { return _currentLogPath; } }
        }

        /// <summary>
        /// ロガーを初期化する。Application.Run より前で必ず呼ぶこと。
        /// 失敗してもアプリ起動は止めない (例外を握り潰し、以降の API は no-op)。
        ///
        /// (#170 followup round 1) `customLogDir` が指定された場合は default の
        /// `&lt;project_root&gt;/logs/manager/` の代わりに **`&lt;customLogDir&gt;/` の直配置** を使用する
        /// (= `/manager/` subdir は append しない、backup_destination_path と同 semantic)。
        /// 失敗時は default にフォールバック。Program.Main が `settings.log_destination_path` を
        /// SQLite 直接 read して渡す (Logger は SettingsRepository に依存しない invariant 維持)。
        /// </summary>
        public static void Initialize(string customLogDir = null)
        {
            lock (_lock)
            {
                if (_initialized || _initFailed) return;

                try
                {
                    // (#170 followup round 1) customLogDir 指定時はその full path を直接使用
                    // (backup_destination_path と同 semantic、`logs/manager` を append しない)。
                    // default は `<project_root>/logs/manager/`。
                    if (!string.IsNullOrWhiteSpace(customLogDir))
                    {
                        _logDirectory = customLogDir;
                    }
                    else
                    {
                        string projectRoot = FindProjectRootForLogs();
                        _logDirectory = Path.Combine(projectRoot, "logs", LogSubDirectory);
                    }
                    Directory.CreateDirectory(_logDirectory);

                    OpenSessionFile();

                    // Console.WriteLine の出力を自動キャプチャ
                    // (このフックを置いた以降の Console.WriteLine はすべて INFO としてファイルにも残る)
                    _previousConsoleOut = Console.Out;
                    Console.SetOut(new ConsoleHookWriter());

                    _initialized = true;

                    // 起動イベントは初期化完了後に書く
                    WriteInternal("INFO", $"[Logger] Manager 起動 (PC={Environment.MachineName})");
                    // (#170 followup round 3 H-1) CleanupOldLogs は Initialize の責務外。
                    // Program.Main が `Logger.Initialize(customLogDir)` 直後に SQLite 直接 read で
                    // retention 値を取得して `Logger.CleanupOldLogs(days)` を呼出する設計
                    // (= dbReady=false / SessionConflictDialog Cancel 等の early-return path でも
                    // 確実に走るよう round 3 で MainForm 経路から Program.Main 経路へ再移動)。
                    // 本 Initialize は file open + Console hook + 起動 trail のみに責務を絞る。
                }
                catch (Exception ex)
                {
                    _initFailed = true;
                    try
                    {
                        // ログ機構自体が壊れているので MessageBox に直接出す
                        System.Windows.Forms.MessageBox.Show(
                            $"ログ機構の初期化に失敗しました。アプリは動作しますがログは記録されません。\n\n{ex.Message}",
                            "ログ機構初期化失敗",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning);
                    }
                    catch { /* MessageBox すら出せないなら諦める */ }
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

        /// <summary>
        /// アプリ終了時に必ず呼ぶ (Program.cs の try-finally で)。
        /// 終了イベントを記録して writer を閉じる。
        /// </summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                if (!_initialized) return;
                try
                {
                    WriteInternal("INFO", "[Logger] Manager 終了");
                    if (_writer != null)
                    {
                        _writer.Flush();
                        _writer.Dispose();
                        _writer = null;
                    }
                    if (_previousConsoleOut != null)
                    {
                        Console.SetOut(_previousConsoleOut);
                        _previousConsoleOut = null;
                    }
                    _initialized = false;
                }
                catch { /* swallow */ }
            }
        }

        private static void Write(string level, string message)
        {
            if (!_initialized) return;
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
                // Logger 自身の例外は絶対にログに書かない (無限ループ防止)
            }
        }

        /// <summary>
        /// セッション専用ファイルを 1 つ作って開く。1 起動セッション = 1 ファイルのため、ローテートは不要。
        /// 万一同秒で衝突した場合 (現実的にはダブルクリック等の極限ケース) は連番サフィックスでリトライ。
        /// </summary>
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

            // FileShare.Read のみ: 同 PC 内で同時に複数の Manager が同じファイル名にならない前提
            // (秒単位タイムスタンプ + 連番サフィックスで衝突回避済み)
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(false));
            _currentLogPath = path;
        }

        /// <summary>
        /// PathManager と同じく exe ベースで上に toneprism.db を探すが、
        /// Logger は PathManager より先に動くため軽量実装で重複させる
        /// (Console 出力なし、例外なし、見つからなければ exe 隣にフォールバックして Logger 自体は動かす)
        /// </summary>
        private static string FindProjectRootForLogs()
        {
            string exePath = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                DirectoryInfo dir = new DirectoryInfo(exePath);
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "toneprism.db")))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { /* fall through */ }
            return exePath;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// `retentionDays` 日より古い manager_*.log を削除する。Program.Main が
        /// `Logger.Initialize(customLogDir)` 直後に 1 回呼ぶ。
        ///
        /// (#170 followup round 3 H-1) 呼出元と timing の確定経緯:
        /// - 旧 (本 PR 以前): `Initialize` 内で 30 日 hardcode + 自動実行
        /// - round 1: public + parametrized 化、`MainForm.ContinueLoadAfterSessionCheck` で
        ///   `SettingsRepository.GetInt32(SettingsKeys.LogRetentionDays, DefaultLogRetentionDays)` 経由呼出
        /// - round 3 H-1: dbReady=false / SessionConflictDialog Cancel 等の early-return path で
        ///   到達不能になる regression を解消するため、Program.Main で **SQLite 直接 read**
        ///   (`TryReadLogRetentionDays`) → `CleanupOldLogs` 呼出に再移動 (= Logger 不変条件
        ///   「SettingsRepository に依存しない」を維持しつつ全起動 path で確実に走る)
        ///
        /// retentionDays &lt;= 0 / Logger 未初期化時は no-op。
        /// </summary>
        public static void CleanupOldLogs(int retentionDays)
        {
            lock (_lock)
            {
                if (!_initialized) return;
                if (retentionDays <= 0) return;
                try
                {
                    DateTime cutoff = DateTime.Now.AddDays(-retentionDays);
                    foreach (string path in Directory.EnumerateFiles(_logDirectory, $"{FileNamePrefix}*{FileNameSuffix}"))
                    {
                        try
                        {
                            // 念のため: 今セッションのアクティブファイルは絶対に消さない
                            if (string.Equals(path, _currentLogPath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (File.GetLastWriteTime(path) < cutoff)
                            {
                                File.Delete(path);
                            }
                        }
                        catch (Exception ex)
                        {
                            // ロック・権限エラー等は警告だけ残して続行
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

        /// <summary>
        /// Console.SetOut で差し替える TextWriter。
        /// Console.WriteLine の出力を Logger.Info にリダイレクトする。
        /// 部分書き込み (Write(char) や Write(string)) は内部バッファで行末まで蓄積してから 1 行として出力。
        /// </summary>
        private sealed class ConsoleHookWriter : TextWriter
        {
            private readonly StringBuilder _buffer = new StringBuilder();

            public override Encoding Encoding { get { return Encoding.UTF8; } }

            public override void Write(char value)
            {
                if (value == '\n')
                {
                    string line = _buffer.ToString();
                    _buffer.Clear();
                    // CR を末尾から除去
                    if (line.Length > 0 && line[line.Length - 1] == '\r')
                        line = line.Substring(0, line.Length - 1);
                    Logger.Write("INFO", line);
                }
                else
                {
                    _buffer.Append(value);
                }
            }

            public override void Write(string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                foreach (char c in value)
                {
                    Write(c);
                }
            }

            public override void WriteLine()
            {
                // 末尾までバッファされてた分があれば flush
                string line = _buffer.ToString();
                _buffer.Clear();
                Logger.Write("INFO", line);
            }

            public override void WriteLine(string value)
            {
                // 既存バッファ + value を 1 行として出力
                string line = _buffer.ToString() + (value ?? string.Empty);
                _buffer.Clear();
                Logger.Write("INFO", line);
            }
        }
    }
}
