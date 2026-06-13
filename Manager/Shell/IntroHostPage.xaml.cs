using System.Windows.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5 startup移管 step2) MainForm の実 IntroGuidePanel (初回説明) を単一インスタンスでホスト。
    /// lifetime 配線は <see cref="ShellHostBinder"/> に集約。
    /// </summary>
    public partial class IntroHostPage : Page
    {
        public IntroHostPage()
        {
            InitializeComponent();
            ShellHostBinder.Bind(this, Host, () => ShellWindow.HostForm?.IntroGuidePanel);
        }
    }
}
