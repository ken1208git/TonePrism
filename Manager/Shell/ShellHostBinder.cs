using System;
using System.Windows.Controls;
using System.Windows.Forms.Integration;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5 startup移管 step2) シェルの各 host ページが MainForm の実セクションパネルを
    /// WindowsFormsHost に **単一インスタンス** で attach/detach するための共通配線。
    ///
    /// lifetime: WindowsFormsHost は Dispose 時に Child を Dispose するため、ページ離脱 (Unloaded) で
    /// <c>Host.Child=null</c> に detach し、再表示 (Loaded) で再 attach する。これで共有実パネルが
    /// ページのキャッシュ/再生成 (NavigationView の挙動に依らず) で Dispose されない。実パネルの所有権は
    /// MainForm 側。<see cref="ShellWindow.HostForm"/>/実パネル未設定時は host を空のままにする
    /// (シェルは ShowShellAsMain からのみ開かれ HostForm は常に設定されるため、空表示は事実上発生しない)。
    /// </summary>
    internal static class ShellHostBinder
    {
        /// <param name="page">host ページ。</param>
        /// <param name="host">page 内の WindowsFormsHost。</param>
        /// <param name="getRealPanel">MainForm の実パネルを返す getter (未設定なら null)。</param>
        /// <param name="onShown">attach 後に毎回走らせたい処理 (例: ストアの LoadSections)。省略可。</param>
        public static void Bind(Page page, WindowsFormsHost host, Func<System.Windows.Forms.Control> getRealPanel, Action onShown = null)
        {
            if (page == null || host == null) return;
            page.Loaded += (_, _) =>
            {
                var real = getRealPanel?.Invoke();
                if (real != null && !ReferenceEquals(host.Child, real)) host.Child = real;
                onShown?.Invoke();
            };
            page.Unloaded += (_, _) =>
            {
                // 共有実パネルを host から外す (= host 破棄で実パネルが Dispose されるのを防ぐ)。
                if (host.Child != null) host.Child = null;
            };
        }
    }
}
