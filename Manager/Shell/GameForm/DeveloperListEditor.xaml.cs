using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinForms = System.Windows.Forms;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) 製作者リスト編集 UserControl。一覧は VM.Developers(DeveloperEditViewModel) にバインドし、
    /// 各カードで 姓/名/期生 を直接インライン編集する (旧 WinForms DeveloperForm ダイアログは廃止)。
    /// 追加 = 空カードを足す、削除 = カードの🗑、左端ハンドルで掴んでドラッグ並べ替え (リスト順 = クレジット順で保存)。
    /// 並べ替えは OLE DragDrop ではなく手動 (マウスキャプチャ + RenderTransform): ドラッグ中カードはカーソルに 1:1 追従、
    /// 他カードはアニメで寄ける。collection の Move は離した時に確定 (ドラッグ中の container 再生成で追従が切れるのを防ぐ)。
    /// </summary>
    public partial class DeveloperListEditor : UserControl
    {
        private GameFormViewModel _vm;

        public DeveloperListEditor()
        {
            InitializeComponent();
            // ページ再利用 (NavigationView の type 単位 cache) でも別ゲームの一覧へ正しく追従するため、
            // DataContext 変更で Developers の購読を張り替え + 空状態を更新する (GameMetadataEditor のジャンルと同パターン)。
            DataContextChanged += OnDataContextChanged;
            // 手動ドラッグ: ハンドル押下で DevItems にマウスをキャプチャ → 移動/離す/キャプチャ喪失を DevItems で受ける。
            DevItems.MouseMove += OnDragMove;
            DevItems.PreviewMouseLeftButtonUp += OnDragEnd;
            DevItems.LostMouseCapture += (_, __) => { if (_dragging) EndDrag(); };
        }

        private GameFormViewModel Vm => DataContext as GameFormViewModel;
        private WinForms.IWin32Window Owner => ShellOwner.For(this);

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.Developers.CollectionChanged -= OnDevelopersChanged;
            _vm = DataContext as GameFormViewModel;
            if (_vm != null) _vm.Developers.CollectionChanged += OnDevelopersChanged;
            UpdateEmptyState();
        }

        private void OnDevelopersChanged(object sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

        private void UpdateEmptyState()
            => EmptyHint.Visibility = (_vm == null || _vm.Developers.Count == 0) ? Visibility.Visible : Visibility.Collapsed;

        // 追加 = 既定 (1期生) の空カードを足す。姓名を入れずに保存しても CommitToVersion 側で除外される。
        private void Add_Click(object sender, RoutedEventArgs e)
            => Vm?.Developers.Add(new DeveloperEditViewModel());

        // ===== ハンドルを掴んでカードをカーソルに追従させながら並べ替え (確定は離した時) =====
        private bool _dragging;
        private ContentPresenter _dragContainer;   // ドラッグ中カードの container (ItemsControl の ContentPresenter)
        private int _dragFrom, _dragTo;            // 元 index / 現在の挿入先 index
        private double _startY, _slot;             // 掴んだ時のカーソル Y / カード中心間距離 (スロット高)

        private void Handle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Vm == null || Vm.Developers.Count < 2
                || !((sender as FrameworkElement)?.DataContext is DeveloperEditViewModel dev)) return;
            if (!(DevItems.ItemContainerGenerator.ContainerFromItem(dev) is ContentPresenter container)) return;

            _dragging = true;
            _dragContainer = container;
            _dragFrom = _dragTo = Vm.Developers.IndexOf(dev);
            _startY = e.GetPosition(DevItems).Y;
            _slot = MeasureSlot();

            EnsureTransform(container);
            Panel.SetZIndex(container, 50);   // 掴んだカードを最前面に
            container.Opacity = 0.9;
            DevItems.CaptureMouse();
            e.Handled = true;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || _dragContainer == null || _slot <= 0) return;

            double delta = e.GetPosition(DevItems).Y - _startY;
            EnsureTransform(_dragContainer).Y = delta;   // ドラッグ中カードはカーソルに 1:1 追従 (アニメ無し)

            int count = Vm.Developers.Count;
            int newTo = _dragFrom + (int)Math.Round(delta / _slot);
            if (newTo < 0) newTo = 0; else if (newTo > count - 1) newTo = count - 1;
            if (newTo != _dragTo)
            {
                _dragTo = newTo;
                RelayoutOthers();   // 他カードをアニメで寄ける
            }
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            EndDrag();
            e.Handled = true;
        }

        private void EndDrag()
        {
            _dragging = false;
            if (DevItems.IsMouseCaptured) DevItems.ReleaseMouseCapture();

            int from = _dragFrom, to = _dragTo, count = Vm?.Developers.Count ?? 0;
            // 全カードの transform / ZIndex / Opacity をリセット (走行中アニメも停止)。
            for (int i = 0; i < count; i++)
            {
                if (DevItems.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe)
                {
                    (fe.RenderTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.YProperty, null);
                    fe.RenderTransform = new TranslateTransform();   // frozen 共有 Identity を避け、次の EnsureTransform で再利用可に
                    Panel.SetZIndex(fe, 0);
                    fe.Opacity = 1.0;
                }
            }
            _dragContainer = null;
            // 視覚はリセット済み。collection を Move して実順序を確定 (container は新順序へ再配置される)。
            if (Vm != null && from != to && from >= 0 && to >= 0) Vm.Developers.Move(from, to);
        }

        // ドラッグ元を _dragTo に挿入するため、間に挟まれるカードを 1 スロットずつアニメで寄ける。
        private void RelayoutOthers()
        {
            int count = Vm.Developers.Count;
            for (int i = 0; i < count; i++)
            {
                if (i == _dragFrom) continue;
                if (!(DevItems.ItemContainerGenerator.ContainerFromIndex(i) is UIElement c)) continue;
                double target = 0;
                if (_dragFrom < _dragTo && i > _dragFrom && i <= _dragTo) target = -_slot;       // 下方向: 間のカードは上へ
                else if (_dragFrom > _dragTo && i >= _dragTo && i < _dragFrom) target = _slot;    // 上方向: 間のカードは下へ
                Animate(c, target);
            }
        }

        private static void Animate(UIElement el, double targetY)
            => EnsureTransform(el).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(targetY, new Duration(TimeSpan.FromMilliseconds(140)))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });

        private static TranslateTransform EnsureTransform(UIElement el)
        {
            if (el.RenderTransform is TranslateTransform tt) return tt;
            tt = new TranslateTransform();
            el.RenderTransform = tt;
            return tt;
        }

        // カード中心間の距離 (= スロット高)。隣接 2 container の位置差から実測、無理なら高さ + 余白(8) で代替。
        private double MeasureSlot()
        {
            if (DevItems.ItemContainerGenerator.ContainerFromIndex(0) is UIElement c0
                && DevItems.ItemContainerGenerator.ContainerFromIndex(1) is UIElement c1)
            {
                double d = c1.TranslatePoint(new Point(0, 0), DevItems).Y - c0.TranslatePoint(new Point(0, 0), DevItems).Y;
                if (d > 0) return d;
            }
            return (_dragContainer?.ActualHeight ?? 0) + 8;
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (Vm == null || !((sender as FrameworkElement)?.DataContext is DeveloperEditViewModel dev)) return;
            // 中身のあるカードのみ確認する (空カードは誤追加なので無確認で消す)。
            if (!dev.IsBlank)
            {
                string name = ((dev.LastName ?? "") + " " + (dev.FirstName ?? "")).Trim();
                if (WinForms.MessageBox.Show(Owner, "製作者「" + name + "」を削除しますか？", "削除確認",
                        WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question) != WinForms.DialogResult.Yes)
                    return;
            }
            Vm.Developers.Remove(dev);
        }
    }
}
