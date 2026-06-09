using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TonePrism.LauncherAgent
{
    /// <summary>
    /// 指定 PID のプロセスツリー (PID + 全子孫) の「可視ウィンドウ / 前面ウィンドウ」状態の判定 (probe) と、
    /// そのツリーのトップレベル窓を強制前面化する処理 (focus) を Win32 API で行う。
    /// (旧 Companions/WindowProbe/Win32Windows.cs のロジックを移植・統合。)
    ///
    /// なぜプロセスツリーを辿るか:
    ///   Launcher は `cmd.exe /C "cd /d <dir> && game.exe"` でゲームを起動するため、Launcher が握る PID は
    ///   cmd.exe で、ゲーム窓は子プロセス game.exe (さらに孫 exe の場合も) に属する。よって「PID + 全子孫」の
    ///   窓を探す必要がある。
    /// </summary>
    internal static class Win32Windows
    {
        // ----- 判定結果 (event JSON の state 値と一致) -----
        public const string NotFound = "not_found";                   // プロセス自体が不在
        public const string NotVisible = "not_visible";               // 稼働中だが可視ウィンドウ無し
        public const string VisibleBackground = "visible_background";  // 可視ウィンドウあり、前面でない
        public const string VisibleForeground = "visible_foreground";  // 可視ウィンドウあり、かつ前面

        private const int MinMeaningfulSize = 200; // 「実ウィンドウ」とみなす最小サイズ (補助窓除外)

        // ============================ probe ============================

        /// <summary>
        /// rootPid + 全子孫の窓状態を 1 回判定する。あわせて代表窓 (前面があれば前面、無ければ最初の可視窓) の
        /// 画面矩形を out で返す (マルチモニタで中断オーバーレイをゲーム窓のいるモニタに出すため)。窓が無ければ 0。
        /// </summary>
        public static string Probe(int rootPid, out int winX, out int winY, out int winW, out int winH)
        {
            winX = winY = winW = winH = 0;
            HashSet<uint> tree = GetProcessTree((uint)rootPid);
            if (tree == null || tree.Count == 0)
            {
                return NotFound;
            }

            IntPtr foreground = GetForegroundWindow();
            bool hasVisible = false;
            bool isForeground = false;
            IntPtr chosen = IntPtr.Zero; // 代表窓: 前面の対象窓があればそれ、無ければ最初の可視窓

            EnumWindows((hwnd, lparam) =>
            {
                if (!IsMeaningfulVisibleWindow(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out uint winPid);
                if (!tree.Contains(winPid)) return true;
                hasVisible = true;
                if (chosen == IntPtr.Zero) chosen = hwnd;
                if (hwnd == foreground) { isForeground = true; chosen = hwnd; }
                return true;
            }, IntPtr.Zero);

            if (!hasVisible) return NotVisible;
            if (chosen != IntPtr.Zero && GetWindowRect(chosen, out RECT r))
            {
                winX = r.Left; winY = r.Top; winW = r.Right - r.Left; winH = r.Bottom - r.Top;
            }
            return isForeground ? VisibleForeground : VisibleBackground;
        }

        // ============================ focus ============================

        /// <summary>
        /// rootPid ツリーのトップレベル可視窓を強制前面化する。foreground-lock 制限を
        /// AttachThreadInput で回避する。戻り値: 前面化に成功した (前面が対象窓になった) か。
        /// </summary>
        public static bool ForceForeground(int rootPid)
        {
            HashSet<uint> tree = GetProcessTree((uint)rootPid);
            if (tree == null) return false;
            IntPtr target = FindTopLevelWindow(tree);
            if (target == IntPtr.Zero) return false;
            return ForceForegroundHwnd(target);
        }

        /// <summary>
        /// 指定 HWND を直接強制前面化する (PID 列挙せず、その窓だけを対象)。
        /// overlay 窓のように「同一プロセスの特定の窓だけ前面化したい (メイン窓を巻き込みたくない)」用途。
        /// </summary>
        public static bool ForceForegroundHwnd(IntPtr target)
        {
            if (target == IntPtr.Zero) return false;

            // (#314) Windows の foreground-lock や、排他フルスクリーンゲームが最小化する直後の前面遷移レースで
            // 1 回の SetForegroundWindow が弾かれ、「フォーカスが動かない / オーバーレイが出ずプレイ中画面の
            // まま」になることがある。そこで前面化が反映 (recovered) されるまで数回リトライする。各試行で
            // foreground スレッドが変わりうる (ゲーム最小化で別窓が前面化する等) ため AttachThreadInput は
            // 毎回張り直す。1 回目で成功すれば従来どおり 60ms 1 回分で抜ける (失敗時のみリトライぶん延びる)。
            const int maxAttempts = 4;
            bool recovered = false;
            for (int attempt = 0; attempt < maxAttempts && !recovered; attempt++)
            {
                IntPtr beforeFg = GetForegroundWindow();
                if (beforeFg == target) { recovered = true; break; }

                uint fgThread = GetWindowThreadProcessId(beforeFg, out _);
                uint thisThread = GetCurrentThreadId();
                bool attached = false;
                if (fgThread != 0 && fgThread != thisThread)
                {
                    attached = AttachThreadInput(fgThread, thisThread, true);
                }

                // 最小化されていれば SW_RESTORE で復元する。WOLF RPG (ウディタ) 等の排他フルスクリーンゲームは
                // HOME でオーバーレイ窓に前面を奪われると自分から最小化するため、SW_SHOW (最小化を解かない) だと
                // resume でゲームが最小化のまま=プレイ中シーンに取り残される。最大化は巻き込みたくないので
                // 最小化時のみ SW_RESTORE、それ以外は従来どおり SW_SHOW。
                ShowWindow(target, IsIconic(target) ? SW_RESTORE : SW_SHOW);
                BringWindowToTop(target);
                SetForegroundWindow(target);

                if (attached) AttachThreadInput(fgThread, thisThread, false);

                // 反映待ち (Program の単一ループスレッド上で実行。失敗時のみリトライぶん延びるが、open/resume
                // 直後で連打される場面ではないので許容)。GetForegroundWindow で実際に前面化できたか確認する
                // (SetForegroundWindow の戻り値は foreground-lock 下で信頼できないため最終判定には使わない)。
                Thread.Sleep(60);
                recovered = GetForegroundWindow() == target;
            }
            return recovered;
        }

        /// <summary>
        /// rootPid ツリーのトップレベル可視窓を、指定矩形 (= ランチャーのモニタ) の中央へ移動する (リサイズはしない)。
        /// マルチモニタで、ゲームが別モニタに開いた場合にランチャーと同じモニタへ寄せ、2 枚構成
        /// (不透明背景 + ゲーム + overlay) を同一モニタに揃えるため (#30 B案)。排他的フルスクリーンは
        /// そもそも overlay 非対応 + 移動不可なので対象外 (no-op になるだけで害はない)。
        /// </summary>
        public static bool PlaceWindowCentered(int rootPid, int x, int y, int w, int h)
        {
            HashSet<uint> tree = GetProcessTree((uint)rootPid);
            if (tree == null) return false;
            IntPtr hwnd = FindTopLevelWindow(tree);
            if (hwnd == IntPtr.Zero) return false;
            if (!GetWindowRect(hwnd, out RECT r)) return false;
            int winW = r.Right - r.Left;
            int winH = r.Bottom - r.Top;
            // 窓の中心が既に対象モニタ内にあるなら動かさない。これにより単一モニタ (= 必ず対象内) や
            // 既に正しいモニタに開いた窓を無駄に動かさず、別モニタに開いた時だけ寄せる (windowed 含め真の no-op)。
            int cx = r.Left + winW / 2;
            int cy = r.Top + winH / 2;
            if (cx >= x && cx < x + w && cy >= y && cy < y + h) return true;
            int newX = x + (w - winW) / 2;
            int newY = y + (h - winH) / 2;
            // 移動のみ (サイズ/z-order/アクティブ化は変えない = ゲーム描画やフォーカスと喧嘩しない)。
            return SetWindowPos(hwnd, IntPtr.Zero, newX, newY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private static IntPtr FindTopLevelWindow(HashSet<uint> tree)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hwnd, lparam) =>
            {
                if (!IsMeaningfulVisibleWindow(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out uint winPid);
                if (!tree.Contains(winPid)) return true;
                found = hwnd;
                return false; // 最初の 1 個で打ち切り
            }, IntPtr.Zero);
            return found;
        }

        // ============================ 共通 ============================

        private static bool IsMeaningfulVisibleWindow(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd)) return false;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false; // オーナー持ち (ダイアログ等) 除外

            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "ConsoleWindowClass") return false; // cmd 経由起動の一瞬のコンソール除外

            if (GetWindowTextLength(hwnd) > 0) return true;

            if (GetWindowRect(hwnd, out RECT r))
            {
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w >= MinMeaningfulSize && h >= MinMeaningfulSize) return true; // タイトル無しでも十分大きければ実窓
            }
            return false;
        }

        /// <summary>rootPid + 全子孫 PID の集合を返す。rootPid が不在なら null。</summary>
        private static HashSet<uint> GetProcessTree(uint rootPid)
        {
            // toolhelp スナップショットはプロセス churn 下で一時的に失敗しうる (ERROR_BAD_LENGTH 等)。
            // 例外を投げると Program の main ループ catch に飛んで 50ms スリープ + その tick の窓状態が
            // 送られず、PLAYING 確定 / 前面化異常検知が滞る。そこで失敗時は null を返し、Probe 側で
            // 1 tick だけ not_found 扱い (= 害がなく次 probe で復帰) にする。
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE)
            {
                return null;
            }
            try
            {
                var childrenByParent = new Dictionary<uint, List<uint>>();
                var allPids = new HashSet<uint>();
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snapshot, ref entry))
                {
                    return null;
                }
                do
                {
                    allPids.Add(entry.th32ProcessID);
                    if (!childrenByParent.TryGetValue(entry.th32ParentProcessID, out List<uint> list))
                    {
                        list = new List<uint>();
                        childrenByParent[entry.th32ParentProcessID] = list;
                    }
                    list.Add(entry.th32ProcessID);
                }
                while (Process32Next(snapshot, ref entry));

                if (!allPids.Contains(rootPid)) return null;

                var result = new HashSet<uint>();
                var queue = new Queue<uint>();
                queue.Enqueue(rootPid);
                result.Add(rootPid);
                while (queue.Count > 0)
                {
                    uint cur = queue.Dequeue();
                    if (childrenByParent.TryGetValue(cur, out List<uint> kids))
                    {
                        foreach (uint kid in kids)
                        {
                            if (result.Add(kid)) queue.Enqueue(kid);
                        }
                    }
                }
                return result;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        // 指定 HWND を OS レベルでクリック透過にする (WS_EX_LAYERED | WS_EX_TRANSPARENT)。
        // Godot の FLAG_MOUSE_PASSTHROUGH は同一プロセス内の窓にしか効かないため、デバッグHUDが
        // 外部ゲーム (別プロセス) の上にある時でもクリックを下のゲームへ通すために使う。
        // WS_EX_TRANSPARENT はヒットテスト透過のみで描画には影響しない (透過描画用の WS_EX_LAYERED は
        // Godot の per-pixel transparency 窓で既に立っている)。
        public static bool SetClickThrough(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
                return true;
            }
            catch { return false; }
        }

        // ----- P/Invoke -----
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);  // (#314) 最小化判定
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const uint GW_OWNER = 4;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;  // (#314) 最小化ウィンドウの復元 (resume でゲームを前面に戻す)
        // クリック透過 (SetClickThrough) 用
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        // SetWindowPos フラグ (PlaceWindowCentered の移動用: サイズ/z-order/アクティブ化は変えない)
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

        private const uint TH32CS_SNAPPROCESS = 0x00000002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }
    }
}
