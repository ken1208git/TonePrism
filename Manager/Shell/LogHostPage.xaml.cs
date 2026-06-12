using System.Windows.Controls;
using TonePrism.Manager.Controls;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5) 既存 WinForms の <see cref="LogSectionPanel"/> を WindowsFormsHost で WPF シェルにホストする
    /// 実証用ページ。本配線では各セクションごとにこの形 (Page 内 WindowsFormsHost で既存パネルをホスト) を取る。
    /// ログパネルは DB 非依存・read-only (ログファイルを読むだけ) なので fresh インスタンスでも開発 DB に無害。
    /// DB に繋がる他パネル (ゲーム/ストア等) は dbManager の受け渡し + 単一インスタンス化が要るため別途 (startup 移管時)。
    /// </summary>
    public partial class LogHostPage : Page
    {
        public LogHostPage()
        {
            InitializeComponent();
            var panel = new LogSectionPanel { Dock = System.Windows.Forms.DockStyle.Fill };
            panel.Initialize(PathManager.LogsRootDirectory);
            Host.Child = panel;
        }
    }
}
