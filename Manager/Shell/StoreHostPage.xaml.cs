using System.Windows.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5 startup移管 step2) MainForm の実 StoreSectionPanel を単一インスタンスでホスト。
    /// ストアは MainForm ではタブ選択時に LoadSections する遅延ロードのため、表示時 (onShown) に LoadSections を呼ぶ。
    /// lifetime 配線は <see cref="ShellHostBinder"/> に集約。
    /// </summary>
    public partial class StoreHostPage : Page
    {
        public StoreHostPage()
        {
            InitializeComponent();
            ShellHostBinder.Bind(this, Host,
                () => ShellWindow.HostForm?.StoreSectionPanel,
                () => ShellWindow.HostForm?.StoreSectionPanel?.LoadSections());
        }
    }
}
