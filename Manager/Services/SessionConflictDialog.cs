using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using GCTonePrism.Manager.Models;

namespace GCTonePrism.Manager.Services
{
    /// <summary>
    /// (#179 / #178 (c)) 他 PC で Manager が起動中の時に表示する modal dialog の context。
    /// </summary>
    internal enum SessionConflictDialogContext
    {
        /// <summary>Manager 起動時、`MainForm.Load` 冒頭で他 PC 検出した場合。Cancel = Manager 終了。</summary>
        Startup,
        /// <summary>編集操作前 (ゲーム追加/編集/削除、ストア編集、設定変更、Backup/Restore 等の DB write 直前)。Cancel = その操作を中止。</summary>
        EditOperation,
    }

    /// <summary>
    /// (#179 / #178 (c)) 他 PC で Manager 起動中を検出した時の modal 警告 dialog の SoT。
    ///
    /// 文言は **「データ破損」「競合」のような技術用語を避け、部員が「何が起きるか」を想像できる
    /// 具体表現** に統一 (「お互いに上書きされて消える恐れ」「他 PC の人に確認してから」)。
    /// `MessageBoxIcon.Stop` + `MessageBoxButtons.OKCancel` で「OK = 続行 (data 喪失 risk 承知)」/
    /// 「Cancel = 中止 (context に応じて Manager 終了 or その操作中止)」を user 判断に委ねる設計。
    ///
    /// 詳細仕様は SPECIFICATION.md §3.8 参照。
    /// </summary>
    internal static class SessionConflictDialog
    {
        /// <summary>
        /// 他 PC 検出時の dialog を表示。OK で続行、Cancel で abort。
        /// </summary>
        /// <param name="owner">親 form (modal の親、null も可)。</param>
        /// <param name="context">context (Startup / EditOperation) で文言切替。</param>
        /// <param name="others">検出した他 PC session list (空でなく 1 件以上)。</param>
        /// <param name="operationDescription">EditOperation 時の操作名 (例: "ゲーム編集")、Startup 時は null。</param>
        /// <returns>user 選択 (DialogResult.OK / DialogResult.Cancel)。</returns>
        public static DialogResult Show(
            IWin32Window owner,
            SessionConflictDialogContext context,
            IReadOnlyList<ManagerSessionInfo> others,
            string operationDescription = null)
        {
            // (round 5 L-4) 空 list / null の事前 guard は caller の contract に倒す:
            //   - `MainForm.CheckSessionConflictBeforeWrite`: `others.Count == 0` で early return
            //   - `MainForm_Load` startup check: `otherSessionsAtStartup.Count > 0` 内側でのみ呼出
            // 同 assembly internal sealed class で caller は grep で全数確認可能、defensive 二重を撤去。
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string detectedListLines = BuildDetectedList(others, nowMs);

            // (round 2 High-1) MessageBox は markdown を解釈しないため `**...**` 等の強調記法は literal 表示
            // される。本文は plain text で OS button label (= 「OK」/「キャンセル」) と一貫させる。
            // (round 2 Medium-1) operationDescription が「ゲーム削除」「データベース初期化」等の場合に
            // 「{op} の内容と...」が日本語として grammatical に合わない問題を解消するため、template を
            // 「このまま {op} を実行すると...」の汎用文に統一 (op は名詞句のままで grammar 違和感なし)。
            // (round 8 L-3) 文言の semantic 修正: 「他 PC の **編集内容と** お互いに上書きされて消える」
            // は ゲーム編集 / Store 編集 / Settings 系には正しいが、「バックアップ作成」「バックアップ削除」
            // 「バックアップ復元」「バックアップ設定変更」では「他 PC の編集内容を上書きする」関係性が
            // 薄く文脈と乖離していた (= 13 callsite のうち 4 件 = BackupSection 3 + BackupSettings 1 が
            // 該当)。「他 PC の **作業と競合して** データ破損や保存内容の喪失が起きる恐れ」のように
            // 「上書き」「編集内容」依存を撤回した一般語に書換え、全 callsite で semantic と整合させる。
            // Startup 側も同方針 (= 「編集内容や バックアップが お互いに上書き」を「データや バックアップが
            // 競合して破損したり 消えたりする」に書換え)。
            string title;
            string body;
            if (context == SessionConflictDialogContext.Startup)
            {
                title = "【危険】他 PC で Manager が起動中です";
                body =
                    detectedListLines + "\n\n" +
                    "両方の PC で同時に Manager を使うと、保存中のデータや\n" +
                    "バックアップが競合して、データが破損したり消えたりする恐れがあります。\n\n" +
                    "「OK」を押す: このまま起動する (データが消える可能性を承知)\n" +
                    "「キャンセル」を押す: Manager を終了する (他 PC の人に確認してから起動する)";
            }
            else
            {
                title = "【危険】他 PC で誰かが作業中です";
                string opLabel = string.IsNullOrEmpty(operationDescription) ? "この操作" : operationDescription;
                body =
                    detectedListLines + "\n\n" +
                    "このまま " + opLabel + " を実行すると、\n" +
                    "他 PC の作業と競合して、データが破損したり保存内容が消えたりする恐れがあります。\n\n" +
                    "「OK」を押す: このまま実行する (データが消える可能性を承知)\n" +
                    "「キャンセル」を押す: 実行を中止する (他 PC の人に確認してから実行する)";
            }

            Logger.Warn("[SessionConflictDialog] " + context + " context で他 PC 検出 (" + others.Count + " 件) → dialog 表示");

            // (#186) Startup context は MainForm_Load 中 (= MainForm がまだ Show されていない state) で
            // 呼ばれるため、`MessageBox.Show(owner, ...)` だと modal child / 親 (= MainForm) のどちらも
            // taskbar entry を持たず、別 window が前面に来ると user が dialog を見失う silent UI bug が
            // あった (PR #184 verify session で発覚: Claude Code window をクリックした瞬間 dialog が裏に
            // 行ってタスクバーにも entry がなく見失う、Alt+Tab で能動的に探さないと辿り着けない)。
            //
            // 修正方針: `MessageBoxOptions.DefaultDesktopOnly` を Startup context **限定** で適用。
            // - 効果: dialog が default desktop 最前面に固定 → focus 失っても裏に行かない
            // - 制約: DefaultDesktopOnly は owner との併用不可 (= 内部で `ShowDialog(IntPtr.Zero, ...)` に
            //   倒れる、owner 渡しても無視) なので Startup 経路は `owner` 引数を捨てて null overload を使う
            //
            // EditOperation context は MainForm が **visible 状態** で呼ばれるため、owner-modal child の
            // 標準 WinForms 挙動で十分 (= MainForm に紐づいた taskbar entry経由でアクセス可能、
            // DefaultDesktopOnly に切替えると owner 関係喪失で「MainForm を最前面に持って来ても dialog
            // は別の場所に留まる」UX 退行になるため適用しない)。
            //
            // 詳細: SPEC §3.8.2「dialog button」項 + #186 issue 本文の (a) MessageBoxOptions.DefaultDesktopOnly 候補。
            if (context == SessionConflictDialogContext.Startup)
            {
                return MessageBox.Show(
                    body,
                    title,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Stop,
                    MessageBoxDefaultButton.Button2 /* Cancel を default に倒して反射押下で続行する path を抑制 */,
                    MessageBoxOptions.DefaultDesktopOnly /* (#186) 最前面固定で focus 喪失 → 見失う drift 解消 */);
            }
            return MessageBox.Show(
                owner,
                body,
                title,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Stop,
                MessageBoxDefaultButton.Button2 /* Cancel を default に倒して反射押下で続行する path を抑制 */);
        }

        private static string BuildDetectedList(IReadOnlyList<ManagerSessionInfo> others, long nowMs)
        {
            // (round 1 L-4) 検出した PC 一覧を「pc_name (Manager v0.X.Y、最終確認: N 秒前)」形式で列挙、
            // 最大 5 件表示で残りは件数で要約。manager_version を embed することで「他 PC が古い version
            // で開いている」case を user が認知可能に (= compatibility 警告の材料)。
            var sb = new StringBuilder();
            sb.Append("検出した PC:");
            int maxShown = Math.Min(others.Count, 5);
            for (int i = 0; i < maxShown; i++)
            {
                var info = others[i];
                int sec = info.SecondsSinceLastHeartbeat(nowMs);
                string version = string.IsNullOrEmpty(info.ManagerVersion) ? "(version 不明)" : "Manager v" + info.ManagerVersion;
                sb.Append("\n  - " + info.PcName + " (" + version + "、最終確認: " + sec + " 秒前)");
            }
            if (others.Count > maxShown)
            {
                sb.Append("\n  ...他 " + (others.Count - maxShown) + " 件");
            }
            return sb.ToString();
        }
    }
}
