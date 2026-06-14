using System;
using System.Windows.Forms;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#179 / #178 (c)) SectionPanel から `MainForm.CheckSessionConflictBeforeWrite` を呼ぶための
    /// 共通 helper。**null 経路の silent skip を物理閉鎖** する目的:
    ///
    /// 旧 pattern: `(this.FindForm() as MainForm)?.CheckSessionConflictBeforeWrite("op") == DialogResult.Cancel`
    ///   は `FindForm()` が null / 非 MainForm を返すと expression 全体が `null` → `null == Cancel` は
    ///   `false` → check を skip して保存処理に流れる silent fallback path があった (PR #184 round 1 M-5)。
    ///   現状は SectionPanel が MainForm 直下にしかホストされていないため実害ゼロだが、将来 sub-dialog 等
    ///   に embed された瞬間に 13 箇所すべてが無音で check を skip し始める drift path。
    ///
    /// 新 pattern (本 helper): `FindForm` が null / 非 MainForm を返した場合は **Logger.Warn で trail
    /// 残しつつ `DialogResult.OK` 返却** (= operation 続行)。silent skip は維持するが log で flag するため
    /// debug 時に検出可能。完全 abort (= `DialogResult.Cancel` 返却で operation 全部 block) する案も
    /// 検討余地あるが、現状の fail-soft 戦略 (SPEC §3.8.5、DB 不到達等は OK 返却で Manager 動作継続)
    /// と一貫させるため OK 返却を維持。
    /// </summary>
    internal static class SessionConflictHelper
    {
        /// <summary>
        /// SectionPanel から呼ぶ便利 wrapper。caller の owner Control から MainForm を辿って
        /// `CheckSessionConflictBeforeWrite` を呼ぶ。null path は Logger.Warn で flag。
        /// </summary>
        /// <param name="caller">呼び出し元 Control (SectionPanel 等)。</param>
        /// <param name="operationDescription">操作内容 (例: "ゲーム編集")。</param>
        /// <returns>`DialogResult.OK` = 続行 / `DialogResult.Cancel` = 中止。null path は OK + Warn。</returns>
        public static DialogResult CheckBeforeWrite(Control caller, string operationDescription)
        {
            if (caller == null)
            {
                Logger.Warn("[SessionConflictHelper] caller=null、check skip (fail-soft で OK 返却): op=" + operationDescription);
                return DialogResult.OK;
            }
            // (round 2 Low-3) FindForm は parent walk なので 1 回呼出に整理 (旧実装は cast 用 + Warn message
            // 内で 2 回呼出していた)。
            // (round 6) 3 段階で MainForm 探索:
            //   (1) caller.FindForm() が MainForm = SectionPanel から呼んだ場合
            //   (2) caller 自身が modal Form (AddGameForm 等) で .Owner が MainForm = Form から呼んだ場合
            //   (3) Application.OpenForms 内に MainForm が存在 = fallback (Owner が null の race 等)
            // 案 B fence で AddGameForm / EditGameForm / VersionUpForm / StoreSectionForm の OK button
            // click handler 内から呼ぶ pattern を支援。
            var form = caller.FindForm();
            var mainForm = form as MainForm;
            if (mainForm == null && form is Form modalForm)
            {
                mainForm = modalForm.Owner as MainForm;
            }
            if (mainForm == null)
            {
                // 3 段目 fallback: Application.OpenForms から MainForm を辿る (Owner が null の race 等)。
                // enumeration race は FindMainFormViaOpenForms 内で握り潰し済 (詳細は同メソッド)。
                mainForm = FindMainFormViaOpenForms();
            }
            if (mainForm == null)
            {
                // 全 fallback 失敗 = MainForm 不在 (= 起動直後 / 単体 test 等の異常 case)。silent skip は
                // 維持するが Warn 出力で debug 容易化。
                Logger.Warn("[SessionConflictHelper] MainForm 取得失敗 (FindForm type=" + (form?.GetType().Name ?? "null") + ")、check skip (fail-soft で OK 返却): op=" + operationDescription);
                return DialogResult.OK;
            }
            return mainForm.CheckSessionConflictBeforeWrite(operationDescription);
        }

        /// <summary>
        /// (#245 ② ゲーム一覧 WPF 化) WinForms <see cref="Control"/> を持たない WPF ページ用 overload。caller
        /// Control が無いので <see cref="Application.OpenForms"/> から MainForm を辿る (= Control 版の 3 段目
        /// fallback と同じ)。MainForm 不在なら fail-soft で OK + Warn (Control 版と同方針＝SPEC §3.8.5)。
        /// </summary>
        public static DialogResult CheckBeforeWrite(string operationDescription)
        {
            var mainForm = FindMainFormViaOpenForms();
            if (mainForm == null)
            {
                Logger.Warn("[SessionConflictHelper] MainForm 取得失敗 (WPF caller)、check skip (fail-soft で OK 返却): op=" + operationDescription);
                return DialogResult.OK;
            }
            return mainForm.CheckSessionConflictBeforeWrite(operationDescription);
        }

        /// <summary>
        /// <see cref="Application.OpenForms"/> から MainForm を辿る共通 lookup。enumeration 中に form が
        /// Close / Show されると .NET WinForms の既知 race で InvalidOperationException ("Collection was
        /// modified") を投げるため握り潰して null 返却 (button click handler / WPF ページを落とさない)。
        /// Control 版の 3 段目 fallback と WPF 版 overload で共有する。
        /// </summary>
        private static MainForm FindMainFormViaOpenForms()
        {
            try
            {
                foreach (Form f in Application.OpenForms)
                {
                    if (f is MainForm mf) return mf;
                }
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn("[SessionConflictHelper] Application.OpenForms enumeration race (collection modified during enumeration): " + ex.Message);
            }
            return null;
        }
    }
}
