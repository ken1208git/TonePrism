using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TonePrism.LauncherAgent
{
    /// <summary>
    /// TonePrism_LauncherAgent エントリ。Launcher 系 Win32 機能を集約した常駐エージェント。
    ///
    /// 起動 (Launcher が OS.create_process で 1 個だけ常駐起動):
    ///   TonePrism_LauncherAgent.exe --event-port &lt;L&gt; [--cmd-port &lt;C&gt;] --parent-pid &lt;P&gt; [--logs-root &lt;path&gt;]
    ///     --cmd-port C   : この port で Launcher からのコマンド (テキスト) を受信。省略時は OS 任せの空きポートを
    ///                      bind し hello イベントで Launcher に通知する (実際の Launcher 呼び出しは省略する)
    ///     --event-port L : この port (127.0.0.1) へ event (JSON) を送信
    ///     --parent-pid P : Launcher の PID。消失したら self-exit (孤児防止)
    ///
    /// コマンド (Launcher → companion, 1 行テキスト):
    ///   watch &lt;pid&gt;          : 指定ゲーム PID の窓状態監視 + HOME/Guide 検知を開始
    ///   unwatch              : 監視・検知を停止 (常駐は維持)
    ///   focus &lt;pid&gt;          : 指定 PID ツリーの窓を強制前面化
    ///   focus_hwnd &lt;hwnd&gt;    : 指定 HWND だけを強制前面化
    ///   quit                 : 終了
    ///
    /// イベント (companion → Launcher, 1 行 JSON):
    ///   {"type":"window","state":"...","x":..,"y":..,"w":..,"h":..,"at_unix_ms":..}  窓状態変化 (#101/#216 駆動) + 1秒 keepalive。x/y/w/h=代表ゲーム窓の画面矩形 (overlay のモニタ決定用)
    ///   {"type":"trigger","seq":N,"event":"home|guide",..} メニュー開閉トリガ (保険で連送)
    ///   {"type":"log","level":"..","msg":"..","at_unix_ms":..} WARN/ERROR/主要イベントの転送
    /// </summary>
    internal static class Program
    {
        private const int ProbeIntervalMs = 150;
        private const int WindowKeepaliveMs = 1000;
        private const int ParentCheckMs = 1000;

        private static UdpClient _eventSender;
        private static IPEndPoint _eventEp;
        private static int _seq;

        // 速度計測 (サービスモードのネットワークテスト用)。1 回約5秒の並列DL + 共有のキャッシュ無し読み。
        // bytes は各接続が5秒間「流しっぱなし」になる十分な大きさにする (小さいと再リクエストの
        // 隙間 + スロースタート再開で大幅に過小評価される)。締め切りで読み取りを打ち切る。
        // 上限は 75MB: Cloudflare `__down` は bytes が大きすぎると 403 を返す (75MB はOK / 100MB は403)。
        private const string SpeedUrl = "https://speed.cloudflare.com/__down?bytes=75000000";
        private static volatile bool _speedRunning;

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            int eventPort = GetIntArg(args, "--event-port", 0);
            int cmdPort = GetIntArg(args, "--cmd-port", 0);
            int parentPid = GetIntArg(args, "--parent-pid", 0);
            string logsRoot = GetStrArg(args, "--logs-root", null);

            if (eventPort <= 0)
            {
                Console.Error.WriteLine("usage: TonePrism_LauncherAgent.exe --event-port <L> [--cmd-port <C>] [--parent-pid <P>] [--logs-root <path>]");
                return 2;
            }

            Logger.Init(logsRoot);
            Logger.Forwarder = SendLog;

            UdpClient cmdReceiver;
            int actualCmdPort;
            try
            {
                // cmd-port 省略/0 なら OS 任せの空きポートを使い、hello イベントで Launcher に通知する。
                cmdReceiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, cmdPort < 0 ? 0 : cmdPort));
                actualCmdPort = ((IPEndPoint)cmdReceiver.Client.LocalEndPoint).Port;
                _eventSender = new UdpClient();
                _eventEp = new IPEndPoint(IPAddress.Loopback, eventPort);
            }
            catch (Exception ex)
            {
                Logger.Error("[main] UDP bind 失敗、終了", ex);
                return 1;
            }
            Logger.Milestone("[main] 起動 event-port=" + eventPort + " cmd-port=" + actualCmdPort + " parent-pid=" + parentPid);

            // hello を起動直後に連送 + cmd を受け取るまで毎秒再送 (取りこぼし対策、Launcher に cmd-port を確実に伝える)。
            bool cmdReceived = false;
            int lastHello = Environment.TickCount;
            for (int i = 0; i < 3; i++) SendHello(actualCmdPort);

            var sensor = new InputSensor();
            sensor.OnTrigger = SendTrigger;

            int watchPid = 0;
            // ゲーム窓をランチャーのモニタへ寄せる (#30 B案) ための状態。watch で受け取り、最初に可視になった
            // 時に 1 回だけ適用する (毎フレーム動かすとゲームと喧嘩するため placed で 1 回限定)。
            bool placeValid = false;
            bool placed = false;
            int placeX = 0, placeY = 0, placeW = 0, placeH = 0;
            string lastState = null;
            int lastProbe = Environment.TickCount;
            int lastKeepalive = Environment.TickCount;
            int lastParentCheck = Environment.TickCount;
            var remoteEp = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    // (1) WH_KEYBOARD_LL 配送のためメッセージポンプ
                    while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }

                    // (2) コマンド受信 (非ブロッキング)
                    while (cmdReceiver.Available > 0)
                    {
                        byte[] data = cmdReceiver.Receive(ref remoteEp);
                        string line = Encoding.UTF8.GetString(data).Trim();
                        if (line.Length == 0) continue;
                        cmdReceived = true; // hello 再送を止める
                        string[] parts = line.Split(' ');
                        switch (parts[0])
                        {
                            case "watch":
                                if (parts.Length >= 2 && int.TryParse(parts[1], out int wpid) && wpid > 0)
                                {
                                    watchPid = wpid;
                                    lastState = null; // 次 probe で必ず emit
                                    // 任意引数: watch <pid> <x> <y> <w> <h> = ランチャーのモニタ矩形。最初の可視窓を
                                    // このモニタ中央へ寄せる (#30 マルチモニタ B案)。省略時は移動しない。
                                    placeValid = false;
                                    placed = false;
                                    if (parts.Length >= 6
                                        && int.TryParse(parts[2], out placeX) && int.TryParse(parts[3], out placeY)
                                        && int.TryParse(parts[4], out placeW) && int.TryParse(parts[5], out placeH)
                                        && placeW > 0 && placeH > 0)
                                    {
                                        placeValid = true;
                                    }
                                    sensor.Start();
                                    Logger.Milestone("[main] watch 開始 pid=" + watchPid
                                        + (placeValid ? " place=(" + placeX + "," + placeY + "," + placeW + "," + placeH + ")" : ""));
                                }
                                break;
                            case "unwatch":
                                watchPid = 0;
                                placeValid = false;
                                placed = false;
                                sensor.Stop();
                                Logger.Milestone("[main] unwatch");
                                break;
                            case "focus":
                                if (parts.Length >= 2 && int.TryParse(parts[1], out int fpid) && fpid > 0)
                                {
                                    bool ok = Win32Windows.ForceForeground(fpid);
                                    Logger.Milestone("[main] focus pid=" + fpid + " ok=" + ok);
                                }
                                break;
                            case "focus_hwnd":
                                // overlay 窓など特定 HWND だけを前面化 (メイン窓を巻き込まない)。HWND は 64bit ありうるので long parse。
                                if (parts.Length >= 2 && long.TryParse(parts[1], out long hwndVal) && hwndVal != 0)
                                {
                                    bool ok = Win32Windows.ForceForegroundHwnd(new IntPtr(hwndVal));
                                    Logger.Milestone("[main] focus_hwnd " + hwndVal + " ok=" + ok);
                                }
                                break;
                            case "clickthrough":
                                // clickthrough <hwnd>。指定窓を OS レベルでクリック透過 (WS_EX_TRANSPARENT) にする。
                                // デバッグHUDが外部ゲームの上にある時でもクリックを下へ通すため (Godot の
                                // FLAG_MOUSE_PASSTHROUGH は同一プロセス内限定なので補完)。
                                if (parts.Length >= 2 && long.TryParse(parts[1], out long cthwnd) && cthwnd != 0)
                                {
                                    bool ok = Win32Windows.SetClickThrough(new IntPtr(cthwnd));
                                    Logger.Milestone("[main] clickthrough " + cthwnd + " ok=" + ok);
                                }
                                break;
                            case "speedtest":
                                // speedtest <run_id> <共有ファイルパス>。run_id は結果を Launcher 側の現在の run と
                                // 照合するための識別子 (遅延した古い結果の取り違え防止)。パスは空白を含みうるので
                                // run_id の次の空白以降を全部パスとして扱う。
                                string rest = line.Substring(parts[0].Length).Trim(); // "speedtest" 以降 (コマンド名長から算出)
                                int runId = 0;
                                string spath = "";
                                int sepIdx = rest.IndexOf(' ');
                                if (sepIdx > 0)
                                {
                                    int.TryParse(rest.Substring(0, sepIdx), out runId);
                                    spath = rest.Substring(sepIdx + 1).Trim();
                                }
                                else
                                {
                                    int.TryParse(rest, out runId); // パス省略時 (run_id のみ)
                                }
                                StartSpeedTest(runId, spath);
                                break;
                            case "quit":
                                Logger.Milestone("[main] quit コマンド受信、終了");
                                sensor.Stop();
                                return 0;
                        }
                    }

                    int now = Environment.TickCount;

                    // (2.5) cmd 未受信なら hello を毎秒再送 (Launcher に cmd-port を確実に届ける)
                    if (!cmdReceived && TickElapsed(lastHello, now, 1000))
                    {
                        lastHello = now;
                        SendHello(actualCmdPort);
                    }

                    // (3) watch 中: ゲームパッド poll + 窓状態 probe
                    if (watchPid > 0)
                    {
                        sensor.PollGamepad();

                        if (TickElapsed(lastProbe, now, ProbeIntervalMs))
                        {
                            lastProbe = now;
                            string state = Win32Windows.Probe(watchPid, out int wx, out int wy, out int ww, out int wh);
                            bool changed = state != lastState;
                            if (changed || TickElapsed(lastKeepalive, now, WindowKeepaliveMs))
                            {
                                lastState = state;
                                lastKeepalive = now;
                                SendWindow(state, wx, wy, ww, wh);
                            }

                            // 最初に可視になったら 1 回だけ、ゲーム窓をランチャーのモニタ中央へ寄せる (#30 B案)。
                            if (placeValid && !placed
                                && (state == Win32Windows.VisibleForeground || state == Win32Windows.VisibleBackground))
                            {
                                placed = true; // 1 回限定 (毎フレーム動かしてゲームと喧嘩しないため)
                                bool moved = Win32Windows.PlaceWindowCentered(watchPid, placeX, placeY, placeW, placeH);
                                Logger.Milestone("[main] ゲーム窓をランチャーのモニタへ寄せた ok=" + moved);
                            }
                        }
                    }

                    // (4) 親プロセス監視 (孤児防止)
                    if (parentPid > 0 && TickElapsed(lastParentCheck, now, ParentCheckMs))
                    {
                        lastParentCheck = now;
                        if (!IsProcessAlive(parentPid))
                        {
                            Logger.Milestone("[main] 親プロセス(" + parentPid + ")消失、self-exit");
                            sensor.Stop();
                            return 0;
                        }
                    }

                    Thread.Sleep(15);
                }
                catch (Exception ex)
                {
                    Logger.Error("[main] ループ内例外 (継続)", ex);
                    Thread.Sleep(50);
                }
            }
        }

        // ---- event 送信 ----
        private static void SendHello(int cmdPort)
        {
            Send("{\"type\":\"hello\",\"cmd_port\":" + cmdPort + ",\"at_unix_ms\":" + UnixMs() + "}");
        }

        private static void SendWindow(string state, int x, int y, int w, int h)
        {
            // x/y/w/h = 代表ゲーム窓の画面矩形 (マルチモニタで overlay をゲーム窓のモニタへ出すため。窓無しは 0)。
            Send("{\"type\":\"window\",\"state\":\"" + state + "\",\"x\":" + x + ",\"y\":" + y + ",\"w\":" + w + ",\"h\":" + h + ",\"at_unix_ms\":" + UnixMs() + "}");
        }

        private static void SendTrigger(string source)
        {
            int seq = Interlocked.Increment(ref _seq);
            string json = "{\"type\":\"trigger\",\"seq\":" + seq + ",\"event\":\"" + source + "\",\"at_unix_ms\":" + UnixMs() + "}";
            for (int i = 0; i < 3; i++) Send(json); // loopback でも保険で連送、受信側は seq で重複吸収
            Logger.Milestone("[main] trigger seq=" + seq + " event=" + source);
        }

        // ---- 速度計測 ----
        private static void StartSpeedTest(int runId, string sharePath)
        {
            if (_speedRunning)
            {
                // 前回の計測が in-flight。無言で捨てると Launcher 側が結果を受け取れず固着する
                // (Launcher 側は結果到着 or タイムアウトまでロック保持)。busy を ok:false で即返して復帰させる。
                SendSpeedtest("internet", false, "前回の計測中です。数秒待って再実行してください", runId);
                SendSpeedtest("server", false, "前回の計測中です。数秒待って再実行してください", runId);
                return;
            }
            _speedRunning = true;
            var t = new Thread(() => RunSpeedTest(runId, sharePath)) { IsBackground = true };
            t.Start();
            Logger.Milestone("[speed] 計測開始 run=" + runId + " share=" + sharePath);
        }

        private static void RunSpeedTest(int runId, string sharePath)
        {
            try
            {
                double mbps;
                if (SpeedTest.Internet(5000, 6, SpeedUrl, out mbps))
                    SendSpeedtest("internet", true, "約 " + Math.Round(mbps) + " Mbps", runId);
                else
                    SendSpeedtest("internet", false, "測定不可 (" + (string.IsNullOrEmpty(SpeedTest.LastError) ? "0B" : SpeedTest.LastError) + ")", runId);

                double mbsec; long bytes;
                // 受け取ったパスがディレクトリなら配下で最大のファイルを測る (exe は小さく本体は .pck/.mp4/.resS 側)。
                string target = SpeedTest.ResolveReadTarget(sharePath, 5000);
                if (!string.IsNullOrEmpty(target) && SpeedTest.ServerRead(target, 100L * 1024 * 1024, SERVER_READ_CAP_MS, out mbsec, out bytes))
                {
                    string sz = bytes >= 1048576 ? (bytes / 1048576) + "MB" : (Math.Max(1, bytes / 1024)) + "KB";
                    SendSpeedtest("server", true, "約 " + Math.Round(mbsec, 1) + " MB/秒 (" + sz + " 読込)", runId);
                }
                else
                    SendSpeedtest("server", false, string.IsNullOrEmpty(sharePath) ? "対象パス不明" : "測定不可", runId);
            }
            catch (Exception ex)
            {
                Logger.Error("[speed] 計測中に例外", ex);
                SendSpeedtest("internet", false, "測定不可", runId);
                SendSpeedtest("server", false, "測定不可", runId);
            }
            finally
            {
                _speedRunning = false;
            }
        }

        // server read の経過時間上限 (ms)。遅い共有でも有界時間で「読めた分」から速度を出し、
        // Launcher 側の速度待ちタイムアウトに当たって「測定不可」化するのを防ぐ。
        private const int SERVER_READ_CAP_MS = 10000;

        private static void SendSpeedtest(string kind, bool ok, string text, int runId)
        {
            Send("{\"type\":\"speedtest\",\"kind\":\"" + kind + "\",\"ok\":" + (ok ? "true" : "false")
                + ",\"text\":\"" + JsonEscape(text) + "\",\"run\":" + runId + ",\"at_unix_ms\":" + UnixMs() + "}");
        }

        private static void SendLog(string level, string msg)
        {
            // Logger.Forwarder 経由。ここでは Logger を呼ばない (再帰防止)。
            Send("{\"type\":\"log\",\"level\":\"" + level.ToLowerInvariant() + "\",\"msg\":\"" + JsonEscape(msg) + "\",\"at_unix_ms\":" + UnixMs() + "}");
        }

        private static void Send(string json)
        {
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(json);
                _eventSender.Send(b, b.Length, _eventEp);
            }
            catch { /* UDP 送信失敗は握り潰す (Logger に出すと再帰の恐れ) */ }
        }

        // ---- helpers ----
        private static long UnixMs() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }

        private static bool TickElapsed(int last, int now, int interval)
        {
            return unchecked(now - last) >= interval; // TickCount ラップアラウンドも unchecked 差分で正しい
        }

        private static bool IsProcessAlive(int pid)
        {
            try { using (Process.GetProcessById(pid)) return true; }
            catch { return false; }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static int GetIntArg(string[] args, string name, int def)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name && int.TryParse(args[i + 1], out int v)) return v;
            return def;
        }

        private static string GetStrArg(string[] args, string name, string def)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return def;
        }

        // ---- P/Invoke (メッセージポンプ) ----
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd; public uint message; public IntPtr wParam;
            public IntPtr lParam; public uint time; public POINT pt;
        }
        private const uint PM_REMOVE = 0x0001;
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    }

    /// <summary>
    /// 速度計測。インターネットは複数並列DLで実速に近づけ、共有サーバーは FILE_FLAG_NO_BUFFERING で
    /// OS キャッシュを回避して実際の読み込み速度を測る (Godot の FileAccess では不可能なため Companion で実施)。
    /// </summary>
    internal static class SpeedTest
    {
        private static long _dlBytes;
        // 失敗時の理由 (UI とログで原因切り分けに使う)。最後に観測したエラーを保持。
        internal static string LastError = "";

        private static void RecordError(Exception ex)
        {
            try
            {
                var we = ex as WebException;
                if (we != null)
                {
                    var hr = we.Response as HttpWebResponse;
                    LastError = hr != null ? ("HTTP " + (int)hr.StatusCode) : ("接続:" + we.Status);
                }
                else
                {
                    LastError = ex.GetType().Name;
                }
            }
            catch { LastError = "err"; }
        }

        // インターネット速度 (Mbps)。durationMs の間、connections 本を並列でDLし続けて合計バイト/秒を測る。
        public static bool Internet(int durationMs, int connections, string url, out double mbps)
        {
            mbps = 0;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 64;
            }
            catch { }
            Interlocked.Exchange(ref _dlBytes, 0);
            LastError = "";
            int deadline = unchecked(Environment.TickCount + durationMs);
            var threads = new Thread[connections];
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < connections; i++)
            {
                int idx = i;
                threads[i] = new Thread(() => DownloadLoop(url, idx, deadline)) { IsBackground = true };
                threads[i].Start();
            }
            for (int i = 0; i < connections; i++) threads[i].Join(durationMs + 10000);
            sw.Stop();
            long total = Interlocked.Read(ref _dlBytes);
            double secs = sw.Elapsed.TotalSeconds;
            if (total <= 0 || secs <= 0) return false;
            // 403 等のエラーが起きつつ最初の数KBだけ受信したケースを「成功(緑)」と誤認しない。
            // エラーが記録されていて受信量が極端に少ない (< 1MB) なら測定失敗扱いにする
            // (エラー無しで <1MB は実際に遅い回線の正当な測定値なので通す)。
            if (!string.IsNullOrEmpty(LastError) && total < 1048576) return false;
            mbps = (total * 8.0) / secs / 1e6;
            return true;
        }

        private static void DownloadLoop(string url, int idx, int deadline)
        {
            var buf = new byte[65536];
            while (unchecked(Environment.TickCount - deadline) < 0)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url + "&n=" + idx + "_" + Environment.TickCount);
                    req.Timeout = 8000;
                    req.ReadWriteTimeout = 8000;
                    req.KeepAlive = true;
                    req.AllowAutoRedirect = true;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var s = resp.GetResponseStream())
                    {
                        int n;
                        while (unchecked(Environment.TickCount - deadline) < 0 && (n = s.Read(buf, 0, buf.Length)) > 0)
                            Interlocked.Add(ref _dlBytes, n);
                    }
                }
                catch (Exception ex) { RecordError(ex); Thread.Sleep(150); }
            }
        }

        // 読み込み速度の計測対象を決める。ファイルならそのまま、ディレクトリなら配下で最も大きいファイルを返す。
        // ゲーム本体は exe でなく .pck / .mp4 (プレビュー動画) / Unity の .resS 等の巨大データ側にあるため、
        // ツリーを走査して最大ファイルを選ぶことで意味のある量を読めるようにする。
        // DirectoryInfo.EnumerateFiles の FileInfo は列挙時にサイズを保持する (WIN32_FIND_DATA 由来) ので
        // SMB 上でもファイル毎の追加 stat が発生せず速い。巨大ツリー対策に budgetMs の時間予算を設ける。
        public static string ResolveReadTarget(string pathOrDir, int budgetMs)
        {
            try
            {
                if (File.Exists(pathOrDir)) return pathOrDir;
                if (!Directory.Exists(pathOrDir)) return pathOrDir; // 渡されたまま ServerRead 側で失敗扱いさせる
                FileInfo best = null;
                var sw = Stopwatch.StartNew();
                try
                {
                    foreach (var f in new DirectoryInfo(pathOrDir).EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        if (best == null || f.Length > best.Length) best = f;
                        if (sw.ElapsedMilliseconds > budgetMs) break; // 予算超過時はそれまでの最大で打ち切り
                    }
                }
                catch { /* アクセス拒否等で列挙が中断しても best にそれまでの最大が残る */ }
                return best != null ? best.FullName : pathOrDir;
            }
            catch { return pathOrDir; }
        }

        // 共有サーバーの読み込み速度 (MB/秒)。FILE_FLAG_NO_BUFFERING でキャッシュを使わず実読み込みを測る。
        // capBytes (最大読込量) と capMs (経過時間上限) のどちらかに達したら打ち切り、読めた分から速度を出す。
        // 時間上限により、遅い共有でも有界時間で完了し Launcher 側の速度待ちタイムアウトに当たらない。
        public static bool ServerRead(string path, long capBytes, int capMs, out double mbPerSec, out long bytes)
        {
            mbPerSec = 0;
            bytes = 0;
            // GENERIC_READ / SHARE_READ|WRITE / OPEN_EXISTING / NO_BUFFERING|SEQUENTIAL_SCAN
            IntPtr h = CreateFile(path, 0x80000000, 0x00000001 | 0x00000002, IntPtr.Zero, 3, 0x20000000 | 0x08000000, IntPtr.Zero);
            if (h == new IntPtr(-1) || h == IntPtr.Zero) return false;
            int bufSize = 1 << 20; // 1MB (セクタ4096の倍数)
            IntPtr buf = VirtualAlloc(IntPtr.Zero, (UIntPtr)(uint)bufSize, 0x1000 | 0x2000, 0x04); // COMMIT|RESERVE / READWRITE (page境界=セクタ境界)
            if (buf == IntPtr.Zero) { CloseHandle(h); return false; }
            var sw = Stopwatch.StartNew();
            long total = 0;
            try
            {
                while (total < capBytes && sw.ElapsedMilliseconds < capMs)
                {
                    uint read;
                    if (!ReadFile(h, buf, (uint)bufSize, out read, IntPtr.Zero)) break;
                    if (read == 0) break;
                    total += read;
                    if (read < (uint)bufSize) break; // EOF
                }
            }
            finally
            {
                VirtualFree(buf, UIntPtr.Zero, 0x8000); // MEM_RELEASE
                CloseHandle(h);
            }
            sw.Stop();
            double secs = sw.Elapsed.TotalSeconds;
            if (total <= 0 || secs <= 0) return false;
            mbPerSec = (total / 1048576.0) / secs;
            bytes = total;
            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);
    }
}
