using System;
using System.Windows;
using System.Windows.Controls;
using TonePrism.Manager.Controls;
using TonePrism.Manager.Services;

namespace TonePrism.Manager.Shell.GameForm
{
    /// <summary>
    /// (#324 PR1) 版番号の構造化入力 UserControl。NumberBox×3 (major/minor/patch) + suffix TextBox を内蔵し、
    /// <see cref="VersionString"/>(TwoWay DP) を介して ViewModel の VersionName と双方向バインドする。
    /// parse(文字列→3数値+suffix) / compose(→"vX.Y.Z[-suffix]") は WinForms 版 SemverInputControl の static ロジック
    /// (<see cref="SemverInputControl.TrySplit"/>) に委譲し、検証規則の二重実装を避ける。_syncing で外部⇄内部の
    /// 双方向更新フィードバックループを抑止。
    /// </summary>
    public partial class SemverInput : UserControl
    {
        private bool _syncing;

        public SemverInput()
        {
            InitializeComponent();
            MajorBox.ValueChanged += (_, _) => Recompose();
            MinorBox.ValueChanged += (_, _) => Recompose();
            PatchBox.ValueChanged += (_, _) => Recompose();
            SuffixBox.TextChanged += (_, _) => Recompose();
        }

        public static readonly DependencyProperty VersionStringProperty =
            DependencyProperty.Register(nameof(VersionString), typeof(string), typeof(SemverInput),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnVersionStringChanged));

        public string VersionString
        {
            get => (string)GetValue(VersionStringProperty);
            set => SetValue(VersionStringProperty, value);
        }

        private static void OnVersionStringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((SemverInput)d).ParseInto((string)e.NewValue);

        // 外部 (VM) → 内部コントロール。
        private void ParseInto(string version)
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                int major = 0, minor = 0, patch = 0;
                string suffix = "";
                if (SemverInputControl.TrySplit(version ?? "", out string core, out string sfx))
                {
                    var parts = core.TrimStart('v', 'V').Split('.');
                    if (parts.Length >= 1) int.TryParse(parts[0], out major);
                    if (parts.Length >= 2) int.TryParse(parts[1], out minor);
                    if (parts.Length >= 3) int.TryParse(parts[2], out patch);
                    suffix = sfx ?? "";
                }
                // (レビュー L-1) 数値部 999 超過は clamp するが、grade clamp と同様にログを残す (silent 矯正を避ける)。
                if (major > 999 || minor > 999 || patch > 999)
                    Logger.Warn("[SemverInput] 版番号の数値部が上限 999 を超過、clamp します: " + (version ?? "(null)"));
                MajorBox.Value = Clamp(major, 0, 999);
                MinorBox.Value = Clamp(minor, 0, 999);
                PatchBox.Value = Clamp(patch, 0, 999);
                SuffixBox.Text = suffix;
            }
            finally { _syncing = false; }
        }

        // 内部コントロール → 外部 (VM)。
        private void Recompose()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                int major = (int)(MajorBox.Value ?? 0);
                int minor = (int)(MinorBox.Value ?? 0);
                int patch = (int)(PatchBox.Value ?? 0);
                string suffix = (SuffixBox.Text ?? "").Trim();
                string s = "v" + major + "." + minor + "." + patch;
                if (suffix.Length > 0) s += "-" + suffix;
                VersionString = s;
            }
            finally { _syncing = false; }
        }

        private static double Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
    }
}
