using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) 版管理 (edit 専用) UserControl。バインドのみで完結 (版切替の commit→load は VM.SelectedVersion setter)。
    /// 版即時削除は #324 follow-up で追加予定。
    /// </summary>
    public partial class VersionEditor : UserControl
    {
        public VersionEditor()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // (レビュー C-1) ページが type 単位で cache 再利用されると、DataContext が旧VM→新VMに差し替わる際、版 ComboBox の
        // ItemsSource が新コレクションに替わり、現 SelectedItem (旧VM の GameVersion 実体) が新リストに無いため一旦 null に
        // なる。SelectedItem は TwoWay なので、その null が新 VM.SelectedVersion へ書き戻ると版がロードされず空のままになる
        // ハザードがある。バインド更新の嵐が収まった後 (Loaded priority) に、選択が落ちていれば ctor で確定済みの起動対象版
        // (InitialSelectedVersionId、ComboBox に依存しない) を再適用して防ぐ。ハザードが起きなければ SelectedVersion は
        // 非 null のままなので no-op (ページが毎回 fresh 生成される実装でも安全)。
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is EditViewModel vm)
                Dispatcher.BeginInvoke(new Action(() => RestoreSelectionIfDropped(vm)), DispatcherPriority.Loaded);
        }

        private static void RestoreSelectionIfDropped(EditViewModel vm)
        {
            if (vm.SelectedVersion != null || vm.Versions.Count == 0) return;
            var target = vm.InitialSelectedVersionId.HasValue
                ? vm.Versions.FirstOrDefault(v => v.Id == vm.InitialSelectedVersionId.Value)
                : null;
            vm.SelectedVersion = target ?? vm.Versions[0];
        }
    }
}
