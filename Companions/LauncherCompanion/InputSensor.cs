using System;
using System.Runtime.InteropServices;

namespace TonePrism.LauncherCompanion
{
    /// <summary>
    /// ゲーム前面時でも拾えるグローバルホットキー検知。
    /// - HOME キー: 低レベルキーフック WH_KEYBOARD_LL (down 遷移で 1 回だけ発火 = auto-repeat 無視)。
    /// - コントローラ Guide: XInputGetStateEx (xinput1_4.dll ordinal #100) を毎ループ poll、down 遷移で発火。
    ///
    /// フックはこれを Install したスレッドにメッセージとして配送されるため、呼び出し元 (Program) は
    /// 同一スレッドで PeekMessage ループを回すこと。<see cref="OnTrigger"/> はそのスレッド上で呼ばれる。
    /// 同時押しコンボ (L3+R3 / START+BACK) は #30 議論で不採用 (HOME と Guide の 2 系統)。
    /// </summary>
    internal sealed class InputSensor
    {
        private const int VK_HOME = 0x24;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYUP = 0x0105;
        private const int XINPUT_GAMEPAD_GUIDE = 0x0400;

        /// <summary>"home" / "guide" を受け取るコールバック (Program が UDP trigger 送信を設定)。</summary>
        public Action<string> OnTrigger;

        private LowLevelKeyboardProc _hookProc; // GC 回収防止のため保持
        private IntPtr _hook = IntPtr.Zero;
        private bool _homeDown;
        private bool _guidePrev;
        private XInputGetStateExDelegate _xinput;

        public bool Active { get; private set; }

        public void Start()
        {
            if (Active) return;
            _hookProc = HookCallback;
            using (var p = System.Diagnostics.Process.GetCurrentProcess())
            using (var m = p.MainModule)
            {
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(m.ModuleName), 0);
            }
            if (_hook == IntPtr.Zero)
            {
                Logger.Warn("[sensor] SetWindowsHookEx 失敗 (HOME 検知不可): " + Marshal.GetLastWin32Error());
            }
            ResolveXInput();
            _homeDown = false;
            _guidePrev = false;
            Active = true;
            Logger.Milestone("[sensor] 開始 (HOME / Guide 検知)");
        }

        public void Stop()
        {
            if (!Active) return;
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
            Active = false;
            Logger.Milestone("[sensor] 停止");
        }

        /// <summary>main ループから毎回呼ぶ。Guide ボタンの down 遷移を検知して発火。</summary>
        public void PollGamepad()
        {
            if (!Active || _xinput == null) return;
            bool guideNow = false;
            for (int i = 0; i < 4; i++)
            {
                if (_xinput(i, out XINPUT_STATE st) == 0 && (st.Gamepad.wButtons & XINPUT_GAMEPAD_GUIDE) != 0)
                {
                    guideNow = true;
                    break;
                }
            }
            if (guideNow && !_guidePrev) Fire("guide");
            _guidePrev = guideNow;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == VK_HOME)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (!_homeDown) { _homeDown = true; Fire("home"); } // down 遷移のみ
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        _homeDown = false;
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void Fire(string source)
        {
            try { OnTrigger?.Invoke(source); } catch (Exception ex) { Logger.Error("[sensor] OnTrigger 例外", ex); }
        }

        private void ResolveXInput()
        {
            foreach (string dll in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
            {
                IntPtr h = LoadLibrary(dll);
                if (h == IntPtr.Zero) continue;
                IntPtr proc = GetProcAddress(h, (IntPtr)100); // ordinal 100 = XInputGetStateEx (Guide 取得可)
                if (proc != IntPtr.Zero)
                {
                    _xinput = (XInputGetStateExDelegate)Marshal.GetDelegateForFunctionPointer(proc, typeof(XInputGetStateExDelegate));
                    return;
                }
            }
            Logger.Warn("[sensor] XInputGetStateEx 解決不可 (Guide 検知はスキップ、HOME のみ)");
        }

        // ----- P/Invoke -----
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate int XInputGetStateExDelegate(int dwUserIndex, out XINPUT_STATE pState);

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] private static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr ordinal);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons; public byte bLeftTrigger; public byte bRightTrigger;
            public short sThumbLX; public short sThumbLY; public short sThumbRX; public short sThumbRY;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
    }
}
