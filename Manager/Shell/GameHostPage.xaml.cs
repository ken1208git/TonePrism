using System.Windows.Controls;
using TonePrism.Manager.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5 startup移管 step2) MainForm が生成・初期化・イベント配線済みの実 <see cref="GameSectionPanel"/> を
    /// WindowsFormsHost で **単一インスタンス** としてホストする。fresh 生成を廃止し二重インスタンスを解消。
    ///
    /// lifetime: WindowsFormsHost は Dispose 時に Child を Dispose するため、ページ離脱 (Unloaded) で
    /// <c>Host.Child=null</c> に detach して共有実パネルが破棄されるのを防ぐ。再表示 (Loaded) で再 attach。
    /// (NavigationView がページをキャッシュ/再生成のいずれでも安全。実パネルの所有権は MainForm 側。)
    ///
    /// fallback: <see cref="ShellWindow.HostForm"/> / 実パネルが未設定 (シェル単独起動等) の場合のみ従来どおり
    /// fresh インスタンスを生成して read-only 表示する (開発 DB に無害)。
    /// </summary>
    public partial class GameHostPage : Page
    {
        private GameSectionPanel _freshFallback;

        public GameHostPage()
        {
            InitializeComponent();
            Loaded += GameHostPage_Loaded;
            Unloaded += GameHostPage_Unloaded;
        }

        private void GameHostPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            var real = ShellWindow.HostForm?.GameSectionPanel;
            if (real != null)
            {
                // 実パネルを attach (= 単一インスタンス)。他ページから戻った場合の再 attach も同経路。
                if (!ReferenceEquals(Host.Child, real)) Host.Child = real;
                return;
            }

            // fallback: 実パネル未設定 → fresh 生成 (read-only 表示、開発 DB に無害)。
            if (_freshFallback == null && ShellWindow.SharedDb != null)
            {
                _freshFallback = new GameSectionPanel { Dock = System.Windows.Forms.DockStyle.Fill };
                _freshFallback.Initialize(ShellWindow.SharedDb);
                _freshFallback.LoadGames();
            }
            if (_freshFallback != null && !ReferenceEquals(Host.Child, _freshFallback)) Host.Child = _freshFallback;
        }

        private void GameHostPage_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // 共有実パネルを host から外す (= host 破棄で実パネルが Dispose されるのを防ぐ)。
            // fresh fallback はこのページ専属だが、対称に detach して問題ない (次回 Loaded で再 attach)。
            if (Host.Child != null) Host.Child = null;
        }
    }
}
