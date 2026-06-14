using System;
using System.Windows;
using System.Windows.Interop;
using IWin32Window = System.Windows.Forms.IWin32Window;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 ② ゲーム一覧 WPF 化) WPF ページから WinForms モーダルダイアログ (AddGameForm / EditGameForm /
    /// VersionUpForm / MessageBox) を開くときの owner を供給する。WPF には Form が無いので、ページが載っている
    /// 可視シェル窓の HWND を <see cref="IWin32Window"/> に包んで渡す。これで背後のシェルが無効化され、可視窓に
    /// 対して正しくモーダルになる (隠し MainForm を owner にすると可視シェルが無効化されず擬似モーダルになる)。
    /// アプリのメッセージループは WinForms (Program.Main の Application.Run(MainForm)) なので WinForms の
    /// ShowDialog はそのまま機能する。
    /// </summary>
    internal sealed class ShellOwner : IWin32Window
    {
        public IntPtr Handle { get; }

        private ShellOwner(IntPtr handle) { Handle = handle; }

        /// <summary>
        /// WPF 要素が属する窓の HWND を owner として返す。窓 / HWND が取得できなければ null
        /// (= ShowDialog はアクティブ窓を owner にする既定動作に倒れる)。
        /// </summary>
        public static IWin32Window For(DependencyObject element)
        {
            var window = element != null ? Window.GetWindow(element) : null;
            if (window == null) return null;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            return hwnd == IntPtr.Zero ? null : new ShellOwner(hwnd);
        }
    }
}
