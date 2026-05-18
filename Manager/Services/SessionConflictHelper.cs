using System;
using System.Windows.Forms;

namespace GCTonePrism.Manager.Services
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
            var mainForm = caller.FindForm() as MainForm;
            if (mainForm == null)
            {
                // SectionPanel が MainForm 以外にホストされた場合の drift path。silent skip は維持するが
                // Warn 出力で debug 容易化。将来 panel をネスト dialog に embed する設計変更時に検出。
                Logger.Warn("[SessionConflictHelper] caller.FindForm() が MainForm でない (type=" + (caller.FindForm()?.GetType().Name ?? "null") + ")、check skip (fail-soft で OK 返却): op=" + operationDescription);
                return DialogResult.OK;
            }
            return mainForm.CheckSessionConflictBeforeWrite(operationDescription);
        }
    }
}
