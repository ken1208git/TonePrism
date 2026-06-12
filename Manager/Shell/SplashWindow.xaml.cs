using System;
using System.Reflection;
using System.Windows;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#246) 起動スプラッシュ窓。<see cref="SplashScreenHost"/> が専用 UI スレッドで生成・表示する。
    /// 版数は実行アセンブリから読む。ステータス文言は起動 init の進捗に応じて差し替える。
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            try
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                VersionText.Text = v == null ? string.Empty : ("バージョン " + v.Major + "." + v.Minor + "." + v.Build);
            }
            catch { /* 版数取得失敗は無視 (cosmetic) */ }
        }

        /// <summary>ステータス文言を差し替える (呼び出しは SplashScreenHost が Dispatcher 経由でマーシャルする)。</summary>
        public void SetStatus(string text)
        {
            try { StatusText.Text = text ?? string.Empty; }
            catch { /* cosmetic */ }
        }
    }
}
