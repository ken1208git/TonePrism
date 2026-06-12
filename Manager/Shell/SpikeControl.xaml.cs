using System.Windows;
using System.Windows.Controls;

namespace TonePrism.Manager.Shell
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

        // (#245 PR5) シェルプレビューに渡す DatabaseManager (MainForm が設定)。DB 接続パネルのホスト用。
        public DatabaseManager Db { get; set; }

        public SpikeControl()
        {
            InitializeComponent();
        }

        private void CountButton_Click(object sender, RoutedEventArgs e)
        {
            _count++;
            CountLabel.Text = $"クリック回数: {_count}";
        }

        // (#245 PR5) Win11 設定アプリ風シェルのプレビュー窓を開く (throwaway)。
        private void ShellPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            // (#245 PR5) シェルが DB パネルをホストできるよう dbManager を共有してから開く。
            ShellWindow.SharedDb = Db;
            new ShellWindow().Show();
        }
    }
}
