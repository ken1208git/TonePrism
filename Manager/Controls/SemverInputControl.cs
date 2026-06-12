using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TonePrism.Manager.Controls
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
        // (#158 round 4 H-1 + round 7 M-3) NumericUpDown の Min/Max と同期した range constants。
        // `TryNormalize` は static method で NumericUpDown インスタンスにアクセスできないため定数で持つ。
        // round 7 M-3: 本定数を SoT (single source of truth) として ctor 内で `numMajor.Maximum =
        // MaxMajor` 等を上書き設定 (= Designer.cs の値が drift しても無視される、defensive assert
        // 不要)。WinForms Designer は static expression を持てないため逆方向 (Designer から const
        // 参照) は不可、「本定数を変える → ctor 上書きが効く」一方向の SoT。
        public const int MaxMajor = 99;
        public const int MaxMinor = 999;
        public const int MaxPatch = 999;
        public const int MinComponent = 0;

        // suffix 用 regex: SemVer 2.0.0 §9 を概ね準拠 = ドット区切り identifier 列、各 identifier は
        // 英数字 + ハイフンのみ・空 identifier 不可。"foo" / "rc1" / "alpha.2" / "rc-1" は OK、"foo.."
        // / ".foo" / "foo." / ".." / "" は reject (空 identifier 含む)。空 suffix の場合は IsValid 側
        // で別判定 (= suffix 入力なし時はそもそも regex match 不要)。(#158 L-3)
        // (#158 round 5 M-2) docstring 訂正: 厳密には SemVer 2.0.0 §2 の数値 identifier leading zero
        // 禁止 (= "v1.0.0-01" reject) も strict 要件だが本 regex はそこまで check しない (= round 4 L-4
        // で見送り判断)。"§9 strict 準拠" だと完全 strict と誤読されるため「概ね準拠」表現に弱める。
        private const string SuffixRegexPattern = @"^[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)*$";
        private static readonly Regex SuffixRegex = new Regex(SuffixRegexPattern, RegexOptions.Compiled);

        // 入力済み version 全体を parse する regex (setter 用)。
        // (#158 CX-3) IgnoreCase: 過去 DB / 手書きで `V1.2.3` (大文字 V) が入った値を malformed
        // 扱いで silent v0.0.0 fallback すると edit/save 経路で意図せず別 version に化けるため、
        // 大文字 V も受理する (= regex は case-insensitive、出力側 (VersionString getter) は常に
        // 小文字 v で正規化する)。
        // (#158 round 5 M-1) suffix 部分は SuffixRegex の inner pattern と同じ「空 identifier 不可」
        // ルールに揃える。旧 `[a-zA-Z0-9.\-]+` だと `v1.0.0-..foo` 等を VersionRegex は受理して
        // SuffixRegex は reject、Load 警告 vs OK 警告で 2 段検出のずれが生じていた (= 同じ規則で
        // 一段で弾く方が UX 一貫)。
        private static readonly Regex VersionRegex = new Regex(
            @"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<suffix>[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)*))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>VersionString が変更された時 (= Major / Minor / Patch / Suffix のいずれかが更新)</summary>
        public event EventHandler VersionStringChanged;

        // (#158 round 8.6 / #171) TryParseAndSet は内部で numMajor/Minor/Patch/txtSuffix の 4 control
        // を順次代入するため、それぞれの ValueChanged/TextChanged 経由で VersionStringChanged が
        // 1 setter で最大 4 回発火する API consistency 違反があった。本 flag で TryParseAndSet 実行中
        // は child event を集約抑止し、setter 完了後に 1 回だけ OnVersionStringChanged を直接呼ぶ。
        // 現状 caller (= VersionStringChanged を購読する form) は不在だが、将来 wire された時に
        // 「状態変化 = 1 event」の自然な semantics を維持。
        private bool _suspendChangeEvents;

        public SemverInputControl()
        {
            InitializeComponent();
            // (#158 round 7 M-3) const を真の SoT 化: Designer.cs の Min/Max を上書きする。
            // 旧実装は ctor で `InvalidOperationException` の defensive assert を投げて drift 検出
            // していたが、3 form すべてが本 control を Designer.InitializeComponent 経由で new する
            // ため、drift があると Manager 起動直後の dialog 表示で例外伝播 → user は generic exception
            // しか見えず操作不能、の cost が過大だった。const を SoT にして上書き設定すれば drift
            // 自体が観測不能になり assert が不要になる。
            numMajor.Minimum = MinComponent;
            numMajor.Maximum = MaxMajor;
            numMinor.Minimum = MinComponent;
            numMinor.Maximum = MaxMinor;
            numPatch.Minimum = MinComponent;
            numPatch.Maximum = MaxPatch;

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
        // (#258 PR3) child control 委譲の runtime プロパティ (backing field 無し)。designer シリアライズ対象外を
        // 明示し net10 WinForms の WFO1000 (error 化) を解消。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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
        /// 外部 (DB / 手書き等) の version 文字列を正規化形 (`v<Major>.<Minor>.<Patch>[-suffix]`、
        /// 小文字 v 強制) に変換する。`TryParseAndSet` と同じ regex + 同じ range/overflow check を
        /// 共有し、「control に流し込んだ後の VersionString getter が返す形」と完全一致する (= 本
        /// method が true を返す = control に流し込んでも UI 上 silent clamp されない、を保証)。
        ///
        /// 失敗判定: (a) null/空文字、(b) regex 非 match、(c) Int32 overflow、(d) NumericUpDown
        /// Min/Max 超 (#158 round 4 H-1)。(d) を含めるのは LoadVersions の事前 scan が本 method を
        /// 唯一の検証手段として使うため、`v120.0.0` 等を素通しすると後段 LoadGameDataForVersion で
        /// silent clamp されて DB 上書きの corruption になるため。
        ///
        /// 主用途 (#158 M-1): VersionUpForm の重複バージョン dup check で「`semverNext.VersionString`
        /// (= 常に `vX.Y.Z` 形式) と DB 由来の生 `currentVersion` (= 過去の "1.0.0" / "V1.0.0" 等の
        /// ゆれを含みうる)」を直接 string 比較するとすり抜けて重複登録されるため、両辺を本 method
        /// で正規化してから比較する。parse 失敗時は false 返却 + normalized=null。
        /// </summary>
        public static bool TryNormalize(string value, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrEmpty(value)) return false;
            var m = VersionRegex.Match(value.Trim());
            if (!m.Success) return false;
            // (#158 CX-4) int.TryParse 戻り値 check: regex 自体は `\d+` だけなので Int32 範囲外
            // (例: `v999999999999.0.0`) も match してしまう。TryParse が false を返した場合 major
            // は 0 のままになるため caller には silent 0 化として観測される。check しないと
            // TryParseAndSet と同型の silent corruption が本 method 経由でも発生する。
            int major, minor, patch;
            if (!int.TryParse(m.Groups["major"].Value, out major)) return false;
            if (!int.TryParse(m.Groups["minor"].Value, out minor)) return false;
            if (!int.TryParse(m.Groups["patch"].Value, out patch)) return false;
            // (#158 round 4 H-1) NumericUpDown Min/Max range check (上記 docstring (d) 参照)。
            if (major < MinComponent || major > MaxMajor) return false;
            if (minor < MinComponent || minor > MaxMinor) return false;
            if (patch < MinComponent || patch > MaxPatch) return false;
            string suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : "";
            string core = "v" + major + "." + minor + "." + patch;
            normalized = string.IsNullOrEmpty(suffix) ? core : core + "-" + suffix;
            return true;
        }

        /// <summary>
        /// VersionString setter と同じ parse 動作だが、parse 成否を bool で返し、失敗時は error
        /// メッセージを out 引数で受け取れる版 (#158 H2 fix)。caller が外部由来の version 文字列
        /// (例: DB から read した malformed value) を流し込む際は本 method を使い、false が返れば
        /// MessageBox / Logger で警告を出すこと。値自体は失敗時も v0.0.0 に強制設定される (= UI の
        /// 整合性のため)、戻り値で「fallback が走った」事実を caller に伝えるのが本 API の責務。
        ///
        /// 失敗判定パターン: (a) null/空文字、(b) regex 非 match、(c) Int32 overflow (CX-4)、
        /// (d) NumericUpDown Min/Max 超 (CX-2)。(c)(d) は値自体は Clamp で UI 整合のため強制設定するが
        /// 戻り値で fallback を caller に伝える。
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
                    // (#158 CX-4) int.TryParse 戻り値 check。regex の `\d+` は Int32 範囲外も match する
                    // ため、TryParse 戻り値を見ないと huge number で silent 0 化する。
                    bool majorOk = int.TryParse(m.Groups["major"].Value, out major);
                    bool minorOk = int.TryParse(m.Groups["minor"].Value, out minor);
                    bool patchOk = int.TryParse(m.Groups["patch"].Value, out patch);
                    suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : "";
                    if (!majorOk || !minorOk || !patchOk)
                    {
                        error = "バージョン番号が Int32 範囲を超えています: '" + value + "'";
                    }
                    // (#158 CX-2) NumericUpDown の Min/Max を超える値は silent clamp で「parse 成功 +
                    // 値だけ別物」になり、後続 save で全く違う version に化ける silent corruption が
                    // 発生するため、overflow も parse 失敗扱いにして caller に通知する。値自体は
                    // 下記 Clamp で UI 整合のため依然 v0.0.0 / 上限値に強制設定するが、戻り値で
                    // fallback が走った事実を caller (= MessageBox 表示経路) に伝える。
                    // (#158 round 3 M-1) 文言を 3 軸別々に組み立て (Designer 側で Major=99 / Minor=999
                    // / Patch=999 と Maximum が異なるため、共通文言だと user に誤った範囲を伝える)。
                    // (#158 round 5 L-3) 上の if で `!majorOk || !minorOk || !patchOk` を error path に
                    // 流しているため、ここに到達した時点で 3 つとも true 確定 → `majorOk &&` のような
                    // defensive condition は dead code、削除して可読性を上げる。
                    // (#158 round 8 senior Med #2) 範囲 check + 文言を const 直書きに統一。旧実装は
                    // `numMajor.Maximum` 等 instance live property を参照していたが、round 7 M-3 で
                    // SoT を const にした方針と一貫させる。ctor で live property は const に上書きされる
                    // ため現状値は同じだが、将来 form 側で sneak に live property を書き換えるような
                    // path が入っても TryParseAndSet / TryNormalize が同じ閾値で判定するため silent
                    // inconsistency が起きない。
                    else if (major < MinComponent || major > MaxMajor)
                    {
                        error = "Major (= " + major + ") は " + MinComponent + "-" + MaxMajor +
                            " の範囲です: '" + value + "'";
                    }
                    else if (minor < MinComponent || minor > MaxMinor)
                    {
                        error = "Minor (= " + minor + ") は " + MinComponent + "-" + MaxMinor +
                            " の範囲です: '" + value + "'";
                    }
                    else if (patch < MinComponent || patch > MaxPatch)
                    {
                        error = "Patch (= " + patch + ") は " + MinComponent + "-" + MaxPatch +
                            " の範囲です: '" + value + "'";
                    }
                    else
                    {
                        ok = true;
                    }
                }
                else
                {
                    error = "バージョン文字列が SemVer 形式ではありません: '" + value + "'";
                }
            }
            // (#158 round 8.6 / #171) 4 control 順次代入による VersionStringChanged 4 連発を抑止、
            // 末尾で 1 回だけ発火させる。例外で抜けても finally で flag を必ず戻す。
            _suspendChangeEvents = true;
            try
            {
                numMajor.Value = Clamp(major, numMajor.Minimum, numMajor.Maximum);
                numMinor.Value = Clamp(minor, numMinor.Minimum, numMinor.Maximum);
                numPatch.Value = Clamp(patch, numPatch.Minimum, numPatch.Maximum);
                txtSuffix.Text = suffix;
            }
            finally
            {
                _suspendChangeEvents = false;
            }
            OnVersionStringChanged();
            return ok;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Major
        {
            get { return (int)numMajor.Value; }
            set { numMajor.Value = Clamp(value, numMajor.Minimum, numMajor.Maximum); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Minor
        {
            get { return (int)numMinor.Value; }
            set { numMinor.Value = Clamp(value, numMinor.Minimum, numMinor.Maximum); }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Patch
        {
            get { return (int)numPatch.Value; }
            set { numPatch.Value = Clamp(value, numPatch.Minimum, numPatch.Maximum); }
        }

        /// <summary>
        /// pre-release suffix (`-rc1` 等の `-` の後ろ部分)。
        ///
        /// **注意 (#158 round 6 L-3)**: setter は `txtSuffix.Text` に直接代入するだけで内部 validation
        /// を持たない (= `IsSuffixValid` などの check を bypass)。caller は事前に `IsSuffixValid(value)`
        /// で書式 (英数字 + ハイフン + ピリオド区切り、空 identifier 不可) を確認する責務を負う。
        /// 本 PR 内で setter を使う caller は無いが、将来の caller が footgun を踏まないよう明示。
        /// 防御 setter にしたい場合は `TrySetSuffix(value, out error)` パターンへの API 追加を検討。
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Suffix
        {
            get { return (txtSuffix.Text ?? "").Trim(); }
            // (L) setter で長さ制限 (txtSuffix.MaxLength と同じ 32 文字) を enforce。
            // UI tx 入力は MaxLength で物理 block だが、setter は txt 経由しないので素通り。
            // 32 文字超 suffix は version folder leaf 名が MAX_PATH を超え CopyDirectoryRecursive が
            // PathTooLongException で失敗する経路を防ぐ defensive limit (caller drift 対策)。
            set
            {
                string v = value ?? "";
                if (v.Length > txtSuffix.MaxLength)
                {
                    v = v.Substring(0, txtSuffix.MaxLength);
                }
                txtSuffix.Text = v;
            }
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
                // (#158 L-3) SemVer 2.0.0 §9 strict 準拠の文言: 各 identifier は英数字 + ハイフンのみ、
                // ドット区切り、空 identifier 不可 (= "..", ".foo", "foo." は reject)。
                errorMessage = "pre-release suffix は英数字とハイフンの identifier をピリオドで区切る形式のみ" +
                               "使用できます (例: rc1 / beta.2 / rc-1)。空の identifier (`..`, `.foo`, `foo.` 等) は" +
                               "使えません。\n現在の入力: '" + suffix + "'";
                return false;
            }
            errorMessage = null;
            return true;
        }

        /// <summary>
        /// 任意の suffix 文字列が IsValid の suffix 規則 (= SemVer 2.0.0 §9) を満たすかを static で
        /// check する版 (#158 H-1)。EditGameForm.btnOK_Click が cmbVersionList.Items 全件の suffix を
        /// 事前 scan する用途。本 control の表示中 1 個だけでなく「dropdown 切替で in-memory commit
        /// された別 version の suffix」も漏れなく検出するため。空文字 suffix は OK 扱い (= suffix なし)。
        /// </summary>
        public static bool IsSuffixValid(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return true;
            return SuffixRegex.IsMatch(suffix);
        }

        /// <summary>
        /// version 文字列を「core 部分 (`v?<X>.<Y>.<Z>`) と suffix 部分」に分割する static helper
        /// (#158 round 7 L-3)。caller 側で `IndexOf('-')` 直書きすると `v-1.0.0` のような malformed
        /// (数値が `-` 始まり) で suffix を `1.0.0` と誤判定する余地があるため、本 method 経由で
        /// VersionRegex の named capture を使った構造的な split を提供する。
        ///
        /// parse 失敗時は false 返却 + core/suffix=null。caller は raw 文字列で fallback 表示すれば
        /// よい。VersionRegex は CX-3 で IgnoreCase 受理済のため大文字 V も OK、Int32 overflow / range
        /// 外も match 自体は通るので caller が別途 TryNormalize で整合性 check すること。
        /// </summary>
        public static bool TrySplit(string version, out string core, out string suffix)
        {
            core = null;
            suffix = null;
            if (string.IsNullOrEmpty(version)) return false;
            string trimmed = version.Trim();
            var m = VersionRegex.Match(trimmed);
            if (!m.Success) return false;
            if (m.Groups["suffix"].Success)
            {
                int suffixStart = m.Groups["suffix"].Index;
                core = trimmed.Substring(0, suffixStart - 1);
                suffix = m.Groups["suffix"].Value;
            }
            else
            {
                core = trimmed;
                suffix = "";
            }
            return true;
        }

        /// <summary>
        /// Patch bump。VersionUpForm の Form_Load で「現在 vX.Y.Z + Patch+1 = 暗黙の "迷ったら Patch" default」
        /// として使う。Major / Minor 系の bump は #158 round 3 で UI から削除したため API 側も提供しない
        /// (= 復活させたい時は再追加可能、現状 YAGNI)。
        ///
        /// **注意 (#158 M-2)**: Patch が NumericUpDown 上限 (numPatch.Maximum) に達している場合は
        /// silent に saturate する (= 値は変わらず元のまま)。VersionUpForm 側では bump 後の値が
        /// 元と同じ場合は dup check (重複バージョン) で蹴られるため silent corruption にはならないが、
        /// 上限到達時に「Patch+1 されない」のは挙動として明示しておく。Patch=999 を超える運用が
        /// 必要になった場合は Maximum 引き上げか Minor ロールオーバーを検討。
        /// </summary>
        public void BumpPatch()
        {
            int newPatch = Patch + 1;
            if (newPatch > (int)numPatch.Maximum) newPatch = (int)numPatch.Maximum;
            numPatch.Value = newPatch;
        }

        private void OnVersionStringChanged()
        {
            // (#158 round 8.6 / #171) TryParseAndSet 実行中は集約抑止 (末尾で 1 回だけ手動 fire)。
            if (_suspendChangeEvents) return;
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
