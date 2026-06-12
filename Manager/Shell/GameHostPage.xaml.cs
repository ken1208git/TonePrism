using System.Windows.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5 startup移管 step2) MainForm の実 GameSectionPanel を WindowsFormsHost で単一インスタンスでホスト。
    /// lifetime 配線 (attach/detach) は <see cref="ShellHostBinder"/> に集約。
    /// </summary>
    public partial class GameHostPage : Page
    {
        public GameHostPage()
        {
            InitializeComponent();
            ShellHostBinder.Bind(this, Host, () => ShellWindow.HostForm?.GameSectionPanel);
        }
    }
}
