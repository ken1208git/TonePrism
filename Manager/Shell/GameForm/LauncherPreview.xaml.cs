using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) ランチャーの実表示を模した合成プレビュー。背景 (PC の縦横比の枠) の上に正方形サムネを左寄せ配置し、
    /// どちらも <c>UniformToFill</c> (= Godot launcher の KEEP_ASPECT_COVERED) で比を保ったまま枠いっぱいに拡大表示する。
    /// <see cref="ThumbnailPath"/> / <see cref="BackgroundPath"/> を host が VM の各 path に束縛 (読取専用)。
    /// </summary>
    public partial class LauncherPreview : UserControl
    {
        // プレビュー枠の高さ (px)。幅は画面アスペクトで算出。サムネタイルは ThumbSize 固定の正方 (枠より独立)。
        private const double FrameHeight = 290;   // 背景をもう少し大きく
        private const double ThumbSize = 105;     // サムネは絶対サイズ固定 (背景を広げてもタイルの大きさは据え置き)

        public LauncherPreview()
        {
            InitializeComponent();
            // 背景枠の縦横比をこの PC のプライマリ画面に合わせる (ランチャーはフルスクリーン表示のため)。
            // 高さ固定 → 幅 = 高さ * アスペクト。画面取得不可時は 16:9 にフォールバック。
            double aspect = 16.0 / 9.0;
            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
            if (sw > 0 && sh > 0) aspect = sw / sh;
            BgFrame.Height = FrameHeight;
            BgFrame.Width = FrameHeight * aspect;
            ThumbFrame.Height = ThumbSize;   // 幅は XAML で ActualHeight に束縛 (正方)
        }

        public static readonly DependencyProperty ThumbnailPathProperty =
            DependencyProperty.Register(nameof(ThumbnailPath), typeof(string), typeof(LauncherPreview),
                new PropertyMetadata(null, OnThumbnailPathChanged));

        public string ThumbnailPath
        {
            get => (string)GetValue(ThumbnailPathProperty);
            set => SetValue(ThumbnailPathProperty, value);
        }

        public static readonly DependencyProperty BackgroundPathProperty =
            DependencyProperty.Register(nameof(BackgroundPath), typeof(string), typeof(LauncherPreview),
                new PropertyMetadata(null, OnBackgroundPathChanged));

        public string BackgroundPath
        {
            get => (string)GetValue(BackgroundPathProperty);
            set => SetValue(BackgroundPathProperty, value);
        }

        private static void OnThumbnailPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (LauncherPreview)d;
            c.ThumbBrush.ImageSource = LoadBitmap((string)e.NewValue, 512);
        }

        private static void OnBackgroundPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (LauncherPreview)d;
            c.BgBrush.ImageSource = LoadBitmap((string)e.NewValue, 768);
        }

        // 絶対 path を Frozen BitmapImage で読む (欠落/失敗は null = 空表示)。読込後にファイルを掴まない。
        private static BitmapImage LoadBitmap(string path, int decodePixelWidth)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = decodePixelWidth;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                Logger.Warn("[LauncherPreview] プレビュー読込失敗 (" + path + "): " + ex.Message);
                return null;
            }
        }
    }
}
