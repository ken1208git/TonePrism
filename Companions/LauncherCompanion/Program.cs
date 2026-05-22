using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TonePrism.LauncherCompanion
{
    /// <summary>
    /// TonePrism_LauncherCompanion エントリ。Launcher 系 Win32 機能を集約した常駐エージェント。
    ///
    /// 起動 (Launcher が OS.create_process で 1 個だけ常駐起動):
    ///   TonePrism_LauncherCompanion.exe --event-port &lt;L&gt; --cmd-port &lt;C&gt; --parent-pid &lt;P&gt; [--logs-root &lt;path&gt;]
    ///     --cmd-port C   : この port で Launcher からのコマンド (テキスト) を受信
    ///     --event-port L : この port (127.0.0.1) へ event (JSON) を送信
    ///     --parent-pid P : Launcher の PID。消失したら self-exit (孤児防止)
    ///
    /// コマンド (Launcher → companion, 1 行テキスト):
    ///   watch &lt;pid&gt;   : 指定ゲーム PID の窓状態監視 + HOME/Guide 検知を開始
    ///   unwatch       : 監視・検知を停止 (常駐は維持)
    ///   focus &lt;pid&gt;   : 指定 PID ツリーの窓を強制前面化
    ///   quit          : 終了
    ///
    /// イベント (companion → Launcher, 1 行 JSON):
    ///   {"type":"window","state":"...","at_unix_ms":..}    窓状態変化 (#101/#216 駆動) + 1秒 keepalive
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

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            int eventPort = GetIntArg(args, "--event-port", 0);
            int cmdPort = GetIntArg(args, "--cmd-port", 0);
            int parentPid = GetIntArg(args, "--parent-pid", 0);
            string logsRoot = GetStrArg(args, "--logs-root", null);

            if (eventPort <= 0)
            {
                Console.Error.WriteLine("usage: TonePrism_LauncherCompanion.exe --event-port <L> [--cmd-port <C>] [--parent-pid <P>] [--logs-root <path>]");
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
                                    sensor.Start();
                                    Logger.Milestone("[main] watch 開始 pid=" + watchPid);
                                }
                                break;
                            case "unwatch":
                                watchPid = 0;
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
                            string state = Win32Windows.Probe(watchPid);
                            bool changed = state != lastState;
                            if (changed || TickElapsed(lastKeepalive, now, WindowKeepaliveMs))
                            {
                                lastState = state;
                                lastKeepalive = now;
                                SendWindow(state);
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

        private static void SendWindow(string state)
        {
            Send("{\"type\":\"window\",\"state\":\"" + state + "\",\"at_unix_ms\":" + UnixMs() + "}");
        }

        private static void SendTrigger(string source)
        {
            int seq = Interlocked.Increment(ref _seq);
            string json = "{\"type\":\"trigger\",\"seq\":" + seq + ",\"event\":\"" + source + "\",\"at_unix_ms\":" + UnixMs() + "}";
            for (int i = 0; i < 3; i++) Send(json); // loopback でも保険で連送、受信側は seq で重複吸収
            Logger.Milestone("[main] trigger seq=" + seq + " event=" + source);
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
}
