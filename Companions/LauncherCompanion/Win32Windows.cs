using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TonePrism.LauncherCompanion
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

        /// <summary>rootPid + 全子孫の窓状態を 1 回判定する。</summary>
        public static string Probe(int rootPid)
        {
            HashSet<uint> tree = GetProcessTree((uint)rootPid);
            if (tree == null || tree.Count == 0)
            {
                return NotFound;
            }

            IntPtr foreground = GetForegroundWindow();
            bool hasVisible = false;
            bool isForeground = false;

            EnumWindows((hwnd, lparam) =>
            {
                if (!IsMeaningfulVisibleWindow(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out uint winPid);
                if (!tree.Contains(winPid)) return true;
                hasVisible = true;
                if (hwnd == foreground) isForeground = true;
                return true;
            }, IntPtr.Zero);

            if (!hasVisible) return NotVisible;
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
            IntPtr beforeFg = GetForegroundWindow();
            uint fgThread = GetWindowThreadProcessId(beforeFg, out _);
            uint thisThread = GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != thisThread)
            {
                attached = AttachThreadInput(fgThread, thisThread, true);
            }

            ShowWindow(target, SW_SHOW);
            BringWindowToTop(target);
            bool ok = SetForegroundWindow(target);

            if (attached) AttachThreadInput(fgThread, thisThread, false);

            Thread.Sleep(60); // 反映待ち
            bool recovered = GetForegroundWindow() == target;
            return ok && recovered;
        }

        /// <summary>
        /// 指定 HWND の最前面 (topmost) フラグを on/off する。SetForegroundWindow と違い z-order の
        /// 変更なので foreground-lock の制約を受けず、背面の既存窓 (フルスクリーンの launcher 等) を
        /// 即座にゲーム窓の上へ出せる。中断オーバーレイ表示時に launcher メイン窓へ使う。
        /// </summary>
        public static void SetTopmost(IntPtr hwnd, bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            IntPtr insertAfter = on ? HWND_TOPMOST : HWND_NOTOPMOST;
            SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
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
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateToolhelp32Snapshot failed");
            }
            try
            {
                var childrenByParent = new Dictionary<uint, List<uint>>();
                var allPids = new HashSet<uint>();
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snapshot, ref entry))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Process32First failed");
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

        // ----- P/Invoke -----
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint GW_OWNER = 4;
        private const int SW_SHOW = 5;

        // SetWindowPos: topmost 切替 (z-order のみ、移動/リサイズ/アクティブ化はしない)
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
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
