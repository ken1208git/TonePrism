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
        // (#245 PR5) ホストする DB 接続パネル (ゲーム/ストア等) に渡す dbManager。NavigationView の Page は
        // parameterless ctor で生成されるため、ページ側がここから取得する。暫定 static 共有 (startup 移管で
        // シェルが dbManager を正式所有したら整理する)。
        internal static DatabaseManager SharedDb;

        public ShellWindow()
        {
            InitializeComponent();
            // 起動時に最初のページへナビゲートして content 領域を埋める。
            Loaded += (_, _) => RootNavigation.Navigate(typeof(PreviewPage));
        }
    }
}
