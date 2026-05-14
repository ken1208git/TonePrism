using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GCTonePrism.Manager.Controls
{
    /// <summary>
    /// SemVer 形式のバージョン入力コントロール (#158)。
    /// `[Major] . [Minor] . [Patch] -[suffix]` の 3 NumericUpDown + optional suffix TextBox。
    ///
    /// 設計意図:
    /// - 単一 TextBox の自由入力だと `1.0.0` / `v1.0.0` / `1.0` / 全角ピリオド / 空白混入 等のフォーマット
    ///   ゆれを silent に DB に入れてしまう。NumericUpDown × 3 で構造的に typo / ゆれを排除。
    /// - SemVer 知識のない部員が「Major って何?」と迷わないよう、別途 SemverHelpControl で解説 panel を
    ///   並べる運用を想定。
    /// - pre-release suffix (`-rc1` 等) は通常運用では使わないため optional。AGENTS.md / SPEC でも
    ///   pre-release suffix 付き Bundle version は skip warn する旨記載済 (= 想定外運用)。
    /// </summary>
    public partial class SemverInputControl : UserControl
    {
        // suffix 用 regex: `-` の後に英数字 / ピリオド / ハイフンのみ (SemVer 2.0.0 仕様準拠)
        private static readonly Regex SuffixRegex = new Regex(@"^[a-zA-Z0-9.\-]*$", RegexOptions.Compiled);

        // 入力済み version 全体を parse する regex (setter 用)
        private static readonly Regex VersionRegex = new Regex(
            @"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<suffix>[a-zA-Z0-9.\-]+))?$",
            RegexOptions.Compiled);

        /// <summary>VersionString が変更された時 (= Major / Minor / Patch / Suffix のいずれかが更新)</summary>
        public event EventHandler VersionStringChanged;

        public SemverInputControl()
        {
            InitializeComponent();
            numMajor.ValueChanged += (s, e) => OnVersionStringChanged();
            numMinor.ValueChanged += (s, e) => OnVersionStringChanged();
            numPatch.ValueChanged += (s, e) => OnVersionStringChanged();
            txtSuffix.TextChanged += (s, e) => OnSuffixChanged();
        }

        /// <summary>v prefix 付きの SemVer 文字列 (例: "v1.2.3" / "v1.2.3-rc1")。setter は v 有無両方受理、null/空文字で v0.0.0 にリセット。</summary>
        public string VersionString
        {
            get
            {
                string core = "v" + (int)numMajor.Value + "." + (int)numMinor.Value + "." + (int)numPatch.Value;
                string suffix = (txtSuffix.Text ?? "").Trim();
                return string.IsNullOrEmpty(suffix) ? core : core + "-" + suffix;
            }
            set
            {
                int major = 0, minor = 0, patch = 0;
                string suffix = "";
                if (!string.IsNullOrEmpty(value))
                {
                    var m = VersionRegex.Match(value.Trim());
                    if (m.Success)
                    {
                        int.TryParse(m.Groups["major"].Value, out major);
                        int.TryParse(m.Groups["minor"].Value, out minor);
                        int.TryParse(m.Groups["patch"].Value, out patch);
                        suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : "";
                    }
                    // parse 失敗時は 0.0.0 にフォールバック (silent danger 排除のため警告は caller が出す)
                }
                numMajor.Value = Clamp(major, numMajor.Minimum, numMajor.Maximum);
                numMinor.Value = Clamp(minor, numMinor.Minimum, numMinor.Maximum);
                numPatch.Value = Clamp(patch, numPatch.Minimum, numPatch.Maximum);
                txtSuffix.Text = suffix;
            }
        }

        public int Major
        {
            get { return (int)numMajor.Value; }
            set { numMajor.Value = Clamp(value, numMajor.Minimum, numMajor.Maximum); }
        }

        public int Minor
        {
            get { return (int)numMinor.Value; }
            set { numMinor.Value = Clamp(value, numMinor.Minimum, numMinor.Maximum); }
        }

        public int Patch
        {
            get { return (int)numPatch.Value; }
            set { numPatch.Value = Clamp(value, numPatch.Minimum, numPatch.Maximum); }
        }

        public string Suffix
        {
            get { return (txtSuffix.Text ?? "").Trim(); }
            set { txtSuffix.Text = value ?? ""; }
        }

        /// <summary>
        /// バージョンが SemVer 形式として有効かを検証する。本コントロールは NumericUpDown で
        /// 数値部の typo を構造的に排除しているため、validation 対象は suffix のみ。
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            string suffix = Suffix;
            if (!string.IsNullOrEmpty(suffix) && !SuffixRegex.IsMatch(suffix))
            {
                errorMessage = "pre-release suffix は英数字・ピリオド・ハイフンのみ使用できます (例: rc1 / beta.2)。\n" +
                               "現在の入力: '" + suffix + "'";
                return false;
            }
            errorMessage = null;
            return true;
        }

        /// <summary>Major bump (Minor / Patch を 0 にリセット)。</summary>
        public void BumpMajor()
        {
            int newMajor = Major + 1;
            if (newMajor > (int)numMajor.Maximum) newMajor = (int)numMajor.Maximum;
            // NumericUpDown.ValueChanged が個別に発火しないようまとめて変更
            numMajor.Value = newMajor;
            numMinor.Value = 0;
            numPatch.Value = 0;
        }

        /// <summary>Minor bump (Patch を 0 にリセット)。</summary>
        public void BumpMinor()
        {
            int newMinor = Minor + 1;
            if (newMinor > (int)numMinor.Maximum) newMinor = (int)numMinor.Maximum;
            numMinor.Value = newMinor;
            numPatch.Value = 0;
        }

        /// <summary>Patch bump。</summary>
        public void BumpPatch()
        {
            int newPatch = Patch + 1;
            if (newPatch > (int)numPatch.Maximum) newPatch = (int)numPatch.Maximum;
            numPatch.Value = newPatch;
        }

        private void OnVersionStringChanged()
        {
            var handler = VersionStringChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnSuffixChanged()
        {
            // suffix の文字検証は IsValid で行う。値変更だけ通知。
            OnVersionStringChanged();
        }

        private static decimal Clamp(int v, decimal min, decimal max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
