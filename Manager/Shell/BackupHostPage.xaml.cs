using System.Windows.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5 startup移管 step2) MainForm の実 BackupSectionPanel を単一インスタンスでホスト。
    /// lifetime 配線は <see cref="ShellHostBinder"/> に集約。
    /// </summary>
    public partial class BackupHostPage : Page
    {
        public BackupHostPage()
        {
            InitializeComponent();
            ShellHostBinder.Bind(this, Host, () => ShellWindow.HostForm?.BackupSectionPanel);
        }
    }
}
