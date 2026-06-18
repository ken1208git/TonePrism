using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) 人数/難易度/プレイ時間/通信/コントローラ/ジャンルの共通メタデータ編集 UserControl。
    /// 単純項目は XAML バインド (DataContext = host から継承した GameFormViewModel)。ジャンルは複数選択なので
    /// GenreList から CheckBox を code-behind 生成し、VM.SelectedGenres と双方向同期する (GameListPage の
    /// フィルタジャンルと同パターン)。版切替等で SelectedGenres が外部更新されたら CollectionChanged で再同期。
    /// </summary>
    public partial class GameMetadataEditor : UserControl
    {
        private GameFormViewModel _vm;
        private readonly List<CheckBox> _genreChecks = new List<CheckBox>();
        private bool _syncingGenre;

        public GameMetadataEditor()
        {
            InitializeComponent();
            BuildGenreChecks();
            DataContextChanged += OnDataContextChanged;
        }

        private void BuildGenreChecks()
        {
            var fg = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC));
            foreach (var genre in GenreList.AvailableGenres)
            {
                var cb = new CheckBox
                {
                    Content = genre,
                    Foreground = fg,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 14, 7)
                };
                cb.Checked += GenreToggled;
                cb.Unchecked += GenreToggled;
                _genreChecks.Add(cb);
                GenrePanel.Children.Add(cb);
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.SelectedGenres.CollectionChanged -= OnVmGenresChanged;
            _vm = DataContext as GameFormViewModel;
            if (_vm != null) _vm.SelectedGenres.CollectionChanged += OnVmGenresChanged;
            SyncChecksFromVm();
        }

        private void OnVmGenresChanged(object sender, NotifyCollectionChangedEventArgs e) => SyncChecksFromVm();

        private void SyncChecksFromVm()
        {
            if (_vm == null) return;
            _syncingGenre = true;
            var set = new HashSet<string>(_vm.SelectedGenres);
            foreach (var cb in _genreChecks)
                cb.IsChecked = cb.Content is string g && set.Contains(g);
            _syncingGenre = false;
        }

        private void GenreToggled(object sender, RoutedEventArgs e)
        {
            if (_syncingGenre || _vm == null) return;
            if (sender is CheckBox cb && cb.Content is string g)
            {
                if (cb.IsChecked == true && !_vm.SelectedGenres.Contains(g)) _vm.SelectedGenres.Add(g);
                else if (cb.IsChecked != true && _vm.SelectedGenres.Contains(g)) _vm.SelectedGenres.Remove(g);
            }
        }
    }
}
