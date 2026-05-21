using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace TonePrism.WindowProbe
{
    /// <summary>
    /// 指定 PID のプロセスツリー (PID + 全子孫) が「可視ウィンドウ」「前面ウィンドウ」を持つかを
    /// Win32 API で判定する。
    ///
    /// なぜプロセスツリーを辿るか:
    ///   Launcher は `cmd.exe /C "cd /d <dir> && game.exe"` でゲームを起動する (作業 dir 設定のため)。
    ///   そのため Launcher が握る PID は cmd.exe で、ゲームウィンドウは子プロセス game.exe に属する。
    ///   さらにゲーム本体が別 exe を spawn する (ランチャー型) ケースも多い。
    ///   よって「PID 単体」ではなく「PID + 全子孫」のウィンドウを探す必要がある。
    /// </summary>
    internal static class Win32Windows
    {
        // ----- 判定結果 (stdout に出す文字列と一致) -----
        public const string NotFound = "not_found";              // プロセス自体が不在
        public const string NotVisible = "not_visible";          // 稼働中だが可視ウィンドウ無し
        public const string VisibleBackground = "visible_background"; // 可視ウィンドウあり、前面でない
        public const string VisibleForeground = "visible_foreground"; // 可視ウィンドウあり、かつ前面

        // ----- 「実ウィンドウ」とみなす最小サイズ (補助ウィンドウ除外用) -----
        private const int MinMeaningfulSize = 200;

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
                if (!IsMeaningfulVisibleWindow(hwnd))
                {
                    return true; // 次のウィンドウへ
                }

                GetWindowThreadProcessId(hwnd, out uint winPid);
                if (!tree.Contains(winPid))
                {
                    return true;
                }

                hasVisible = true;
                if (hwnd == foreground)
                {
                    isForeground = true;
                }
                return true;
            }, IntPtr.Zero);

            if (!hasVisible)
            {
                return NotVisible;
            }
            return isForeground ? VisibleForeground : VisibleBackground;
        }

        /// <summary>
        /// 「ユーザーに見える主ウィンドウ」とみなせるかの判定。
        /// - 可視
        /// - トップレベル (オーナー無し)
        /// - コンソールウィンドウ (cmd.exe / conhost.exe の ConsoleWindowClass) を除外
        ///   → cmd 経由起動時に一瞬出るコンソールで誤って visible 判定しないため
        /// - タイトルあり、または十分な大きさ (不可視ヘルパーウィンドウ除外)
        /// </summary>
        private static bool IsMeaningfulVisibleWindow(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd))
            {
                return false;
            }
            // オーナーウィンドウを持つもの (ダイアログ等) は主ウィンドウとみなさない
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
            {
                return false;
            }

            var cls = new StringBuilder(256);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "ConsoleWindowClass")
            {
                return false;
            }

            if (GetWindowTextLength(hwnd) > 0)
            {
                return true;
            }

            // タイトル無しでも、十分な大きさのウィンドウは実ウィンドウとみなす (フルスクリーンゲーム等)
            if (GetWindowRect(hwnd, out RECT r))
            {
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w >= MinMeaningfulSize && h >= MinMeaningfulSize)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// rootPid + 全子孫 PID の集合を返す。rootPid が存在しなければ null (= プロセス不在)。
        /// CreateToolhelp32Snapshot で全プロセスの (PID, ParentPID) を集め、BFS で子孫を辿る。
        ///
        /// 「プロセス不在」(null 返却 → not_found) と「スナップショット API の失敗」(throw → exit 1 →
        /// 呼び出し元 Launcher では UNAVAILABLE 扱い) を区別する。両者を not_found に丸めると、
        /// プロセス生存中の一時的 API 失敗を「プロセスが消えた／窓が無い」と誤判定し、
        /// 前面化異常 (#216) の誤発報につながりうるため。
        /// </summary>
        private static HashSet<uint> GetProcessTree(uint rootPid)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateToolhelp32Snapshot failed");
            }

            try
            {
                // 親 PID → 子 PID 群
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

                if (!allPids.Contains(rootPid))
                {
                    return null; // プロセス不在
                }

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
                            // PID 再利用による循環を防ぐため visited チェック
                            if (result.Add(kid))
                            {
                                queue.Enqueue(kid);
                            }
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

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const uint GW_OWNER = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

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
