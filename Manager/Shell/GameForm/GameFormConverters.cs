using System;
using System.Globalization;
using System.Windows.Data;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) <c>int?</c> モデル値 ↔ WPF-UI NumberBox の <c>double?</c> Value 変換。null は null のまま通す
    /// (= 「未設定」を NumberBox の空表示に対応させる)。
    /// </summary>
    public class NullableIntToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? null : (object)System.Convert.ToDouble(value);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? null : (object)(int)Math.Round(System.Convert.ToDouble(value));
    }

    /// <summary>
    /// (#324) 難易度/プレイ時間の <c>int?</c>(1-3) ↔ ComboBox.SelectedIndex(0-2) 変換。原本 WinForms 同様
    /// 「未設定」項目は持たず 3 段階のみ (易/普/難 = 1/2/3、index = 値-1)。null/範囲外は「普通」(index 1) を
    /// 表示するが、ユーザーが選択を変えない限り ConvertBack は発火しないため元の null は保持される
    /// (= 旧 <c>_versionDifficultyWasNullOnLoad</c> スナップショット glue 相当を binding の自然な挙動で代替)。
    /// 選択を変えたときだけ常に 1-3 を書き戻す。
    /// </summary>
    public class LevelToIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is int v && v >= 1 && v <= 3) ? v - 1 : 1; // null/範囲外 → 普通

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int idx = value is int i ? i : 1;
            if (idx < 0) idx = 1;
            return idx + 1; // 0→1, 1→2, 2→3 (常に 1-3)
        }
    }

    /// <summary>(#324 PR1) bool 反転。リリース年「不明」チェック時に年 NumberBox を無効化する等に使う。</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !(value is bool b && b);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => !(value is bool b && b);
    }

    /// <summary>(#324) true (=無効/不明) のとき Opacity を 0.4 へ落として「グレーアウト」を視覚化する。
    /// WPF-UI の NumberBox は IsEnabled=false でも背景しか変化せず、数値文字・スピナーが明るいままで disabled に
    /// 見えない。IsEnabled での操作抑止に加え本コンバータで全体を減衰させ、無効状態を一目で分かるようにする。</summary>
    public class BooleanToDimOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? 0.4 : 1.0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
