using System;
using System.Drawing;
using System.Windows.Forms;

namespace GCTonePrism.Manager.Controls
{
    /// <summary>
    /// SemVer (バージョン番号) の Major / Minor / Patch を初心者向けに解説する collapsible help panel (#158)。
    /// AddGameForm / VersionUpForm の version 入力欄の近くに配置することで、SemVer 知識のない部員が
    /// 「Major って何?」と迷った時にすぐに説明を読める形にする。
    ///
    /// 設計:
    /// - default 折り畳み (Collapsed = true)。ヘッダ button で expand/collapse。
    /// - 縦サイズが動的に変わるため、配置先 form 側で Anchor / 上下要素のレイアウトを考慮する必要あり。
    /// - 文言は SPEC §2.2 機能 1 (ゲーム追加) の例に沿った文化祭ゲーム向けの具体例。
    /// </summary>
    public partial class SemverHelpControl : UserControl
    {
        private const int CollapsedHeight = 28;
        private const int ExpandedHeight = 230;

        public SemverHelpControl()
        {
            InitializeComponent();
            ApplyCollapsedState();
            btnToggle.Click += BtnToggle_Click;
        }

        /// <summary>折り畳み状態。true = ヘッダのみ、false = 解説本文展開。</summary>
        public bool Collapsed { get; private set; } = true;

        private void BtnToggle_Click(object sender, EventArgs e)
        {
            Collapsed = !Collapsed;
            ApplyCollapsedState();
        }

        private void ApplyCollapsedState()
        {
            pnlContent.Visible = !Collapsed;
            this.Height = Collapsed ? CollapsedHeight : ExpandedHeight;
            btnToggle.Text = (Collapsed ? "▶ " : "▼ ") + "バージョン番号 (SemVer) とは?";
        }
    }
}
