using Wpf.Ui.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5) Win11 設定アプリ風シェルのプレビュー窓 (throwaway)。FluentWindow(Mica/ダーク) +
    /// NavigationView(左サイドバー) の見た目を実機確認するためのもの。本配線では各 NavigationView ページに
    /// 既存 WinForms セクションパネルを WindowsFormsHost でホストし、これが Manager の本シェルになる。
    /// 見た目 OK なら本シェルへ発展、ダメなら撤去。
    /// </summary>
    public partial class ShellWindow : FluentWindow
    {
        public ShellWindow()
        {
            InitializeComponent();
            // 起動時に最初のページへナビゲートして content 領域を埋める。
            Loaded += (_, _) => RootNavigation.Navigate(typeof(PreviewPage));
        }
    }
}
