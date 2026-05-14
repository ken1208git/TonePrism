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
    /// - SemVer 知識のない部員が「Major って何?」と迷う対策は、本 control では UI 解説を持たず、
    ///   #133 ゲーム制作ガイドライン (GAME_SUBMISSION_GUIDE.md) で文書として解説する方針 (round 3 で
    ///   collapsible help panel + bump button を撤去、ガイドライン doc に集約)。本 control は SemVer
    ///   形式の入力 UI に専念。
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

        /// <summary>
        /// v prefix 付きの SemVer 文字列 (例: "v1.2.3" / "v1.2.3-rc1")。setter は v 有無両方受理、
        /// null/空文字 / parse 失敗で v0.0.0 にリセット (silent fallback)。
        ///
        /// **重要 (#158 H2)**: setter は parse 失敗時 silent に v0.0.0 にする。caller-knows-valid な
        /// 用途 (= AddGameForm の hardcoded "v1.0.0" default 等) のみで使うこと。DB 等の **外部由来の
        /// 値** を流し込む場合は <see cref="TryParseAndSet"/> を使い、parse 失敗を caller 側で警告
        /// 表示すること (= LoadGameDataForVersion / VersionUpForm ctor)。setter で silent fallback
        /// すると malformed DB 値が user 操作なしに v0.0.0 に化けて DB に書き戻る silent corruption が
        /// 発生する。
        /// </summary>
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
                string ignored;
                TryParseAndSet(value, out ignored);
            }
        }

        /// <summary>
        /// VersionString setter と同じ parse 動作だが、parse 成否を bool で返し、失敗時は error
        /// メッセージを out 引数で受け取れる版 (#158 H2 fix)。caller が外部由来の version 文字列
        /// (例: DB から read した malformed value) を流し込む際は本 method を使い、false が返れば
        /// MessageBox / Logger で警告を出すこと。値自体は失敗時も v0.0.0 に強制設定される (= UI の
        /// 整合性のため)、戻り値で「fallback が走った」事実を caller に伝えるのが本 API の責務。
        /// </summary>
        public bool TryParseAndSet(string value, out string error)
        {
            error = null;
            int major = 0, minor = 0, patch = 0;
            string suffix = "";
            bool ok = false;
            if (string.IsNullOrEmpty(value))
            {
                error = "バージョン文字列が空です";
            }
            else
            {
                var m = VersionRegex.Match(value.Trim());
                if (m.Success)
                {
                    int.TryParse(m.Groups["major"].Value, out major);
                    int.TryParse(m.Groups["minor"].Value, out minor);
                    int.TryParse(m.Groups["patch"].Value, out patch);
                    suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : "";
                    ok = true;
                }
                else
                {
                    error = "バージョン文字列が SemVer 形式ではありません: '" + value + "'";
                }
            }
            numMajor.Value = Clamp(major, numMajor.Minimum, numMajor.Maximum);
            numMinor.Value = Clamp(minor, numMinor.Minimum, numMinor.Maximum);
            numPatch.Value = Clamp(patch, numPatch.Minimum, numPatch.Maximum);
            txtSuffix.Text = suffix;
            return ok;
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

        /// <summary>
        /// Patch bump。VersionUpForm の Form_Load で「現在 vX.Y.Z + Patch+1 = 暗黙の "迷ったら Patch" default」
        /// として使う。Major / Minor 系の bump は #158 round 3 で UI から削除したため API 側も提供しない
        /// (= 復活させたい時は再追加可能、現状 YAGNI)。
        /// </summary>
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
