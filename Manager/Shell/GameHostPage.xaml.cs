using System.Windows.Controls;
using TonePrism.Manager.Controls;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245 PR5) 既存 WinForms の <see cref="GameSectionPanel"/> を WindowsFormsHost で WPF シェルにホストする
    /// 実証用ページ。DB 接続パネルなので dbManager (<see cref="ShellWindow.SharedDb"/>) を渡して初期化する。
    /// <c>Initialize</c> は参照保持のみ・<c>LoadGames</c> は読み取り表示のみで、保存系は user の編集操作時にしか
    /// 走らないため fresh インスタンスでも開発 DB に無害 (編集・保存しなければ)。startup 移管で単一インスタンス化する。
    /// </summary>
    public partial class GameHostPage : Page
    {
        public GameHostPage()
        {
            InitializeComponent();
            if (ShellWindow.SharedDb == null) return; // dbManager 未設定 (シェル直接起動等) は空表示で安全に抜ける
            var panel = new GameSectionPanel { Dock = System.Windows.Forms.DockStyle.Fill };
            panel.Initialize(ShellWindow.SharedDb);
            panel.LoadGames();
            Host.Child = panel;
        }
    }
}
