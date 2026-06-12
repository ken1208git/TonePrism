using System.Windows.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5 startup移管 step2) MainForm の実 UpdateSectionPanel を単一インスタンスでホスト。
    /// lifetime 配線は <see cref="ShellHostBinder"/> に集約。
    /// </summary>
    public partial class UpdateHostPage : Page
    {
        public UpdateHostPage()
        {
            InitializeComponent();
            ShellHostBinder.Bind(this, Host, () => ShellWindow.HostForm?.UpdateSectionPanel);
        }
    }
}
