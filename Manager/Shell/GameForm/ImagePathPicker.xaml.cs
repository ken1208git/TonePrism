using System.Windows;
using System.Windows.Controls;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) 画像 path 入力部品 (ラベル + パス TextBox + 参照/クリア)。プレビューは <see cref="LauncherPreview"/> に分離。
    /// <see cref="Path"/>(TwoWay DP) を host が VM の ThumbnailPath / BackgroundPath に束縛し、<see cref="Label"/> で
    /// 見出しを差し替える。参照は WPF ネイティブ OpenFileDialog。
    /// </summary>
    public partial class ImagePathPicker : UserControl
    {
        public ImagePathPicker()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty PathProperty =
            DependencyProperty.Register(nameof(Path), typeof(string), typeof(ImagePathPicker),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string Path
        {
            get => (string)GetValue(PathProperty);
            set => SetValue(PathProperty, value);
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(ImagePathPicker),
                new PropertyMetadata(""));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = string.IsNullOrEmpty(Label) ? "画像を選択" : Label + "を選択",
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|すべてのファイル (*.*)|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true) Path = dlg.FileName;
        }

        private void Clear_Click(object sender, RoutedEventArgs e) => Path = "";
    }
}
