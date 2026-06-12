using System.Windows.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5) ShellWindow の NavigationView content 領域に表示する placeholder ページ。
    /// 本配線では各セクションごとのページに既存 WinForms パネルを WindowsFormsHost でホストする。
    /// </summary>
    public partial class PreviewPage : Page
    {
        public PreviewPage()
        {
            InitializeComponent();
        }
    }
}
