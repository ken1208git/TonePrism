using System.Windows;
using System.Windows.Controls;

namespace TonePrism.Manager.Wpf
{
    /// <summary>
    /// (#245 Phase 0) WPF 共存スパイク。net10 Manager (WinForms primary + UseWPF) で WPF UserControl が
    /// コンパイル・描画・操作できることを確認するための検証用コントロール。ElementHost 経由で WinForms の
    /// タブにホストする。PR5 の WPF シェル化 (WindowsFormsHost で既存 WinForms をホスト) に進む前の de-risk で、
    /// 確認後はこのタブごと撤去するか、PR5 の土台に置き換える throwaway。
    /// </summary>
    public partial class SpikeControl : UserControl
    {
        private int _count;

        public SpikeControl()
        {
            InitializeComponent();
        }

        private void CountButton_Click(object sender, RoutedEventArgs e)
        {
            _count++;
            CountLabel.Text = $"クリック回数: {_count}";
        }
    }
}
