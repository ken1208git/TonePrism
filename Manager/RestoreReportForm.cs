using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TonePrism.Manager.Models;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
{
    /// <summary>
    /// DB 復元後の整合性チェック結果と、ズレを解消するための復元手順を表示するダイアログ。
    ///
    /// バックアップ/復元は toneprism.db のみが対象で games/ フォルダは復元されないため、別時点の DB を
    /// 復元すると DB と実フォルダがズレうる (RestoreReconciliationService 参照)。本ダイアログはズレの中身を
    /// 「起動できないゲーム / ディスクに無いバージョン / DB に無い余分なフォルダ」に分類して提示し、
    /// 何をすれば整合した状態に戻せるかを番号付き手順で案内する。Designer は使わずコードで UI を組む
    /// (項目数が可変のテキストレポート主体のため)。
    /// </summary>
    public class RestoreReportForm : Form
    {
        private readonly RestoreReconciliationResult _result;
        private readonly string _safetyPath;
        // (#250 PR2) true = 復元直後の自動チェック / false = バックアップタブの「整合性チェック」ボタンからの手動実行。
        // 文言を切替える (手動時は「復元完了」等の復元前提の言い回しを避ける)。
        private readonly bool _postRestore;
        // (#250 PR3b) DB と一緒にゲームファイル (games/+guide/) を復元したときの結果。null = アセット復元なし
        // (DBのみ復元 / 手動整合性チェック)。非 null のとき copied/deleted/missing 等のアセット節を追記する。
        private readonly AssetRestoreResult _assetResult;
        private TextBox _body;

        public RestoreReportForm(RestoreReconciliationResult result, string safetyPath, bool postRestore = true, AssetRestoreResult assetResult = null)
        {
            _result = result;
            _safetyPath = safetyPath;
            _postRestore = postRestore;
            _assetResult = assetResult;
            BuildUi();
        }

        /// <summary>アセット復元で「対処が必要」級の問題 (全体失敗 or プール実体欠落) があったか。headline を赤に格上げ。</summary>
        private bool AssetCritical => _assetResult != null && (_assetResult.IsFailed || _assetResult.MissingBlobRelPaths.Count > 0);
        /// <summary>アセット復元で軽微な不完全 (per-file 失敗 or 削除抑止) があったか (起動への致命傷ではない)。</summary>
        private bool AssetMinor => _assetResult != null && !AssetCritical && (_assetResult.IsPartial || _assetResult.DeletionSuppressed);

        private void BuildUi()
        {
            Text = _postRestore ? "復元後の整合性チェック" : "整合性チェック";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 580);
            MinimumSize = new Size(560, 420);

            string headline;
            Color headColor;
            // (#250 PR3b review #4) DB 側 (AnalysisFailed / DB-critical) が見出しを取るとき、アセット側の重大問題が
            // 見出しから消えないよう suffix で併記する (本文 AppendAssetSection には元々出るが、見出しの強調が漏れる)。
            string assetCriticalSuffix = AssetCritical ? "／ゲームファイルの復元にも問題あり" : "";
            if (_result.AnalysisFailed)
            {
                // (レビュー対応 #2) 整合性チェック自体の失敗は「スキーマで DB が読めない」級の深刻状態
                // (復元後 migration 失敗等)。orange ではなく critical 同等の赤で「対処が必要」と明示する。
                headline = "⚠ 整合性チェックを実行できませんでした（対処が必要）" + assetCriticalSuffix;
                headColor = Color.Firebrick;
            }
            else if (_result.HasCriticalFindings)
            {
                headline = (_postRestore
                    ? "⚠ 復元後、起動できないゲームがあります（対処が必要）"
                    : "⚠ 起動できないゲームがあります（対処が必要）") + assetCriticalSuffix;
                headColor = Color.Firebrick;
            }
            else if (AssetCritical)
            {
                // (#250 PR3b) DB の整合性は問題ないが、ゲームファイルの復元で実体欠落 / 全体失敗があった。
                headline = _assetResult.IsFailed
                    ? "⚠ ゲームファイルの復元に失敗しました（対処が必要）"
                    : "⚠ 一部のゲームファイルを復元できませんでした（対処が必要）";
                headColor = Color.Firebrick;
            }
            else if (_result.HasAnyFindings || AssetMinor)
            {
                headline = _postRestore
                    ? "復元は完了しました（軽微なズレあり・起動への影響なし）"
                    : "軽微なズレがあります（起動への影響なし）";
                headColor = Color.DarkGoldenrod;
            }
            else
            {
                headline = _postRestore
                    ? "✓ 復元完了：DB とゲームフォルダの整合性に問題はありません"
                    : "✓ DB とゲームフォルダの整合性に問題はありません";
                headColor = Color.ForestGreen;
            }

            var lblHead = new Label
            {
                Text = headline,
                Dock = DockStyle.Top,
                Height = 40,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = headColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 8, 12, 0)
            };

            _body = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = new Font("Consolas", 9.5f),
                BackColor = Color.White,
                Text = BuildReportText()
            };
            _body.Select(0, 0);

            var btnCopy = new Button
            {
                Text = "内容をコピー",
                AutoSize = true,
                Padding = new Padding(8, 2, 8, 2)
            };
            btnCopy.Click += (s, e) =>
            {
                try { Clipboard.SetText(_body.Text); }
                catch { /* クリップボード失敗は無視 */ }
            };

            var btnClose = new Button
            {
                Text = "閉じる",
                AutoSize = true,
                Padding = new Padding(16, 2, 16, 2),
                DialogResult = DialogResult.OK
            };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 48,
                Padding = new Padding(10, 8, 10, 8)
            };
            bottom.Controls.Add(btnClose);
            bottom.Controls.Add(btnCopy);

            // 追加順と Dock の z-order に注意: Fill を最初に Add し、その後 Top/Bottom を被せる。
            Controls.Add(_body);
            Controls.Add(lblHead);
            Controls.Add(bottom);

            AcceptButton = btnClose;
            CancelButton = btnClose;
        }

        private string BuildReportText()
        {
            var sb = new StringBuilder();

            if (_assetResult != null && _assetResult.IsFailed)
            {
                // (#250 PR3b review #2) ゲームファイルも復元しようとしたが**全体失敗**したケース。DBのみ前提の「games は
                // 含まれない」案内も「一緒に揃えた」案内も誤りになるので、専用の前置きにする (詳細は下のアセット節)。
                sb.AppendLine("【まず確認】ゲームファイル本体や初回説明の画像も一緒に復元しようとしましたが、復元できませんでした。");
                sb.AppendLine("データベースの復元は完了しています。下の「ゲームファイルの復元結果」に失敗の内容と対処を示します。");
            }
            else if (_assetResult != null)
            {
                // (#250 PR3b) DB とゲームファイルを一緒に復元したケース。「games は含まれない」という DBのみ前提の
                // 案内は誤りになるので、一緒に戻した旨と「控え欠落で食い違いが残りうる」点に切替える。
                sb.AppendLine("【まず確認】今回はゲームの登録情報（データベース）に加えて、ゲームファイル本体や初回説明の画像も一緒に復元しました。");
                sb.AppendLine("通常は両者の「時点」が揃いますが、控え（プール）に一部の実体が無かった場合などは");
                sb.AppendLine("食い違いが残ることがあります。以下にその有無と内訳を示します。");
            }
            else
            {
                sb.AppendLine("【まず確認】バックアップ／復元の対象は toneprism.db（データベース）だけで、");
                sb.AppendLine("ゲームのファイルや初回説明の画像は含まれません。そのため、" + (_postRestore ? "いま復元した DB と、" : "現在の DB と、"));
                sb.AppendLine("ディスク上のゲームファイルの「時点」がズレていると、以下のような食い違いが起きます。");
            }
            sb.AppendLine();
            if (!string.IsNullOrEmpty(_safetyPath))
            {
                sb.AppendLine("復元前の DB は安全のため退避済みです（元に戻したいときはここから）:");
                sb.AppendLine("  " + _safetyPath);
                sb.AppendLine();
            }
            sb.AppendLine(new string('-', 70));
            sb.AppendLine();

            // (#250 PR3b) アセット復元の結果節 (reconcile の早期 return より前に出すことで、DB 整合は綺麗でも
            // ゲームファイル側に問題があるケースを必ず表示する)。
            AppendAssetSection(sb);

            if (_result.AnalysisFailed)
            {
                sb.AppendLine("整合性チェックの実行中にエラーが発生しました:");
                sb.AppendLine("  " + (_result.AnalysisError ?? "(詳細不明)"));
                sb.AppendLine();
                sb.AppendLine("Manager を再起動し、ゲーム一覧が正しく表示されるか確認してください。");
                return sb.ToString();
            }

            if (!_result.HasAnyFindings)
            {
                sb.AppendLine("DB に登録された全ゲーム・全バージョンのフォルダがディスク上に揃っており、");
                sb.AppendLine("DB に無い余分なフォルダもありませんでした。そのまま運用を再開できます。");
                return sb.ToString();
            }

            // (0) スキーマ未完 (追加精査 ③)。v14→v15 のような migration が「重複行残存」等で partial skip
            //     した状態で復元したケース。user_version は据え置きで起動継続するが UNIQUE 制約等が未適用。
            if (_result.SchemaIncomplete)
            {
                sb.AppendLine("■ DB スキーマが未完です（対処が必要）");
                sb.AppendLine("  復元した DB のスキーマバージョンが、この Manager が想定する最新版より");
                sb.AppendLine("  古い状態です。一部の整合性制約が未適用のまま起動しているため、");
                sb.AppendLine("  この状態でゲーム編集を続けるとデータ重複等の問題が発生する可能性があります。");
                sb.AppendLine();
                sb.AppendLine("    現在の DB バージョン: v" + _result.ActualSchemaVersion);
                sb.AppendLine("    想定 DB バージョン:   v" + _result.ExpectedSchemaVersion);
                sb.AppendLine();
                sb.AppendLine("  原因として多いのは『古い DB に残った重複データ』です。Manager を再起動すると");
                sb.AppendLine("  再度マイグレーションを試行します。それでも未完のままなら、ログ");
                sb.AppendLine("  （Manager.log）の WARN 行で具体的なテーブル名 / 制約が確認できます。");
                sb.AppendLine();
            }

            // (1) 起動できないゲーム
            if (_result.BrokenGames.Count > 0)
            {
                sb.AppendLine("■ 起動できないゲーム（" + _result.BrokenGames.Count + " 件）— 対処が必要");
                sb.AppendLine("  復元した DB が指す実行ファイルがディスクに見つかりません。このままでは");
                sb.AppendLine("  ランチャーで起動できません。");
                sb.AppendLine();
                foreach (var b in _result.BrokenGames)
                {
                    sb.AppendLine("  ・" + b.Title + "  [ID: " + b.GameId + "]"
                        + (string.IsNullOrEmpty(b.ActiveVersion) ? "" : "  版: " + b.ActiveVersion));
                    sb.AppendLine("      想定パス: " + b.ExpectedExecutable);
                    if (!b.GameFolderExists)
                        sb.AppendLine("      （ゲームフォルダ自体がありません）");
                    sb.AppendLine();
                }
            }

            // (2) ディスクに無いバージョン
            if (_result.MissingVersionFolders.Count > 0)
            {
                sb.AppendLine("■ ディスクに無いバージョン（" + _result.MissingVersionFolders.Count + " 件）");
                sb.AppendLine("  DB には登録がありますが、そのバージョンのフォルダがディスクにありません。");
                sb.AppendLine("  そのバージョンに切り替えると起動できません（アクティブ版でなければ当面の");
                sb.AppendLine("  起動には影響しません）。");
                sb.AppendLine();
                foreach (var m in _result.MissingVersionFolders)
                {
                    sb.AppendLine("  ・" + m.Title + "  [ID: " + m.GameId + "]  版: " + m.Version);
                    sb.AppendLine("      想定フォルダ: " + m.ExpectedFolder);
                }
                sb.AppendLine();
            }

            // (2b) 非アクティブ版で実行ファイルが解決できない (累積監査 round 4 High-7)
            if (_result.BrokenVersions.Count > 0)
            {
                sb.AppendLine("■ 起動できない非アクティブ版（" + _result.BrokenVersions.Count + " 件）— 対処が必要");
                sb.AppendLine("  フォルダはディスクに在りますが、実行ファイルが見つからない版があります。");
                sb.AppendLine("  この版に切替えると起動できなくなります（アクティブ版でなければ当面の");
                sb.AppendLine("  起動には影響しません）。");
                sb.AppendLine();
                foreach (var bv in _result.BrokenVersions)
                {
                    sb.AppendLine("  ・" + bv.Title + "  [ID: " + bv.GameId + "]  版: " + bv.Version);
                    sb.AppendLine("      想定パス: " + bv.ExpectedExecutable);
                }
                sb.AppendLine();
            }

            // (2c) thumbnail / background asset の欠落 (累積監査 round 4 High-7)
            if (_result.BrokenAssets.Count > 0)
            {
                sb.AppendLine("■ サムネイル／背景画像の欠落（" + _result.BrokenAssets.Count + " 件）— 起動への影響なし");
                sb.AppendLine("  DB が指す画像ファイルがディスクに見つかりません。Launcher の見た目が");
                sb.AppendLine("  劣化しますが、ゲーム自体は起動できます。");
                sb.AppendLine();
                foreach (var ba in _result.BrokenAssets)
                {
                    sb.AppendLine("  ・" + ba.Title + "  [ID: " + ba.GameId + "]  版: " + (ba.Version ?? "(不明)") + "  種別: " + ba.AssetKind);
                    sb.AppendLine("      想定パス: " + ba.ExpectedPath);
                }
                sb.AppendLine();
            }

            // (2d) イントロガイド (初回説明) スライド画像の欠落 (#250 PR2)
            if (_result.BrokenIntroSlides.Count > 0)
            {
                sb.AppendLine("■ 初回説明スライドの画像の欠落（" + _result.BrokenIntroSlides.Count + " 件）— 起動への影響なし");
                sb.AppendLine("  DB が指すスライド画像（guide フォルダ）がディスクに見つかりません。本文のあるスライドは");
                sb.AppendLine("  画像なしで表示され、本文のないスライド（画像のみ）はイントロガイドから外れます。");
                sb.AppendLine("  いずれもゲームの起動には影響しません。");
                sb.AppendLine();
                foreach (var bs in _result.BrokenIntroSlides)
                {
                    sb.AppendLine("  ・スライド表示順 " + (bs.DisplayOrder + 1) + "  [slide_id: " + bs.SlideId + "]");
                    sb.AppendLine("      想定パス: " + bs.ExpectedPath);
                }
                sb.AppendLine();
            }

            // (3) 孤児フォルダ
            var orphanGames = _result.OrphanFolders.Where(o => o.Kind == OrphanKind.Game).ToList();
            var orphanVersions = _result.OrphanFolders.Where(o => o.Kind == OrphanKind.Version).ToList();
            if (orphanGames.Count > 0 || orphanVersions.Count > 0)
            {
                sb.AppendLine("■ DB に無い余分なフォルダ（ゲーム " + orphanGames.Count + " 件 / バージョン "
                    + orphanVersions.Count + " 件）— 起動への影響なし");
                sb.AppendLine("  復元した DB には登録されていないフォルダです。残しても起動には影響しません");
                sb.AppendLine("  が、ディスク容量を消費します。中身を確認のうえ不要なら手動で削除できます。");
                sb.AppendLine();
                foreach (var o in orphanGames)
                    sb.AppendLine("  ・[ゲーム] " + o.Path);
                foreach (var o in orphanVersions)
                    sb.AppendLine("  ・[バージョン] " + o.Path);
                sb.AppendLine();
            }

            // 復元手順
            sb.AppendLine(new string('-', 70));
            sb.AppendLine();
            sb.AppendLine("【整合した状態に戻す手順】");
            sb.AppendLine();
            // (review round2 #3) 手順1「Launcher を閉じる」はフォルダの差し替え/削除が要る finding (起動不能ゲーム /
            // 無い版 / 孤児フォルダ) のときだけ出す。画像欠落だけのとき (スライド編集で貼り直す等) は不要で、手順1だけが
            // 浮くのを避ける。
            if (_result.BrokenGames.Count > 0 || _result.MissingVersionFolders.Count > 0 || _result.OrphanFolders.Count > 0)
            {
                sb.AppendLine("  1. すべての展示 PC で Launcher（来場者向けの画面）を閉じてください");
                sb.AppendLine("     （開いているとファイルを掴んでいて、復元やフォルダの差し替えができません）。");
                sb.AppendLine();
            }
            if (_result.BrokenGames.Count > 0 || _result.MissingVersionFolders.Count > 0)
            {
                // (ユーザー指摘) 部員はふだん Manager (GUI) で操作し、生の games フォルダをエクスプローラーで触る機会は
                // ほぼ無い。PR3b で「控えのある世代を復元すれば DB+ゲームファイルが一貫してそろう」「safety_*.db で undo」
                // が GUI で完結するようになったので、**GUI 操作を主・手動フォルダコピーを最終手段**に並べ替える。
                sb.AppendLine("  2. バックアップ履歴から、ゲームファイルの控えがある世代を選んで「復元」してください。");
                sb.AppendLine("     登録データとゲームファイルが同じ時点でそろい、起動できないゲームが解消します");
                sb.AppendLine("     （確認画面に「ゲームファイル本体や初回説明の画像も…戻します」と出る世代が対象です）。");
                sb.AppendLine();
                if (_postRestore)
                {
                    // safety_*.db = 復元の直前に退避した状態。戻すのは「今回の復元を取り消す」操作で、元のデータに問題が
                    // あって復元したのならその問題も一緒に戻る点を明示 (round2 のユーザー指摘)。
                    sb.AppendLine("  3. 今回の復元自体を取り消したいだけなら、復元の直前に自動退避された safety_*.db を");
                    sb.AppendLine("     履歴から「復元」してください。登録データもゲームファイルも復元前の状態に戻ります。");
                    sb.AppendLine("     ※ただし、元のデータに問題があって今回復元したのなら、戻すとその問題も一緒に戻ります。");
                    sb.AppendLine("       その場合は手順 2（当時の控えから戻す）が本筋です。");
                    sb.AppendLine();
                }
                sb.AppendLine("  ・（最終手段・ふだんは使いません）当時のゲームファイルがどのバックアップにも無く、");
                sb.AppendLine("    別 PC・別ドライブ・共有サーバー等にしか残っていない場合は、そのフォルダを games/ 配下へ");
                sb.AppendLine("    手動でコピーし（フォルダ名＝ゲーム ID／バージョン leaf を一致）、バックアップ画面の");
                sb.AppendLine("    「整合性チェック」で「起動できないゲーム」が 0 件になったか確認します。");
            }
            else if (_result.OrphanFolders.Count > 0)
            {
                // (review #2 / ユーザー指摘) 孤児フォルダは起動に影響しないので「基本そのままで問題なし」を前面に。
                // 手動削除は容量が気になるときの任意操作 (部員にフォルダ操作を強いない)。
                sb.AppendLine("  2. 「登録に無い余分なフォルダ」は起動には影響しないので、基本はそのままで問題ありません。");
                sb.AppendLine("     ディスク容量が気になるときだけ、中身を確認のうえ手動で削除してください");
                sb.AppendLine("     （必要なフォルダを誤って消さないよう、削除前に中身をご確認ください）。");
            }
            // (review #2) 画像 (サムネ/背景/初回説明スライド) 欠落の直し方を明示する。旧実装は画像のみの finding でも
            // games フォルダ系の手順 or 孤児削除しか出さず、画像欠落の修正手順が一切無かった。
            if (_result.BrokenAssets.Count > 0 || _result.BrokenIntroSlides.Count > 0)
            {
                sb.AppendLine("  ・画像の欠落（サムネイル／背景／初回説明スライド）は、当時のゲームや初回説明の");
                sb.AppendLine("    画像ファイルを補うか、ゲーム編集・スライド編集で画像を選び直すと解消できます。");
            }
            sb.AppendLine();
            if (!_postRestore)
            {
                sb.AppendLine("  ※ いまのゲームファイルに合う状態に戻したいときは、バックアップ履歴から合致する時点を");
                sb.AppendLine("     「復元」してください。");
            }
            else if (_result.BrokenGames.Count == 0 && _result.MissingVersionFolders.Count == 0)
            {
                // postRestore で broken/missing が無い (= 上の手順 3 で safety を案内していない) ケースのみ補足。重複回避。
                sb.AppendLine("  ※ 今回の復元自体を取り消したいだけなら、退避ファイル（safety_*.db）を「復元」すると、");
                sb.AppendLine("     登録データもゲームファイルも復元前の状態に戻ります（ただし、元のデータに問題があって");
                sb.AppendLine("     復元したのなら、その問題も戻ります）。");
            }

            return sb.ToString();
        }

        /// <summary>(#250 PR3b) アセット (games/+guide/) 復元の結果を追記する。null のとき何もしない。</summary>
        private void AppendAssetSection(StringBuilder sb)
        {
            if (_assetResult == null) return;

            sb.AppendLine("■ ゲームファイル本体や初回説明の画像の復元結果");
            if (_assetResult.IsFailed)
            {
                sb.AppendLine("  ゲームファイルの復元を実行できませんでした:");
                sb.AppendLine("    " + (_assetResult.Message ?? "(詳細不明)"));
                sb.AppendLine();
                sb.AppendLine("  データベースの復元は完了しています。ゲームファイルだけ別の世代でやり直すか、");
                sb.AppendLine("  当時の games フォルダを手動で補ってください。");
            }
            else
            {
                sb.AppendLine($"  コピー: {_assetResult.CopiedCount} 件 ／ 変更なし: {_assetResult.SkippedCount} 件 ／ 削除: {_assetResult.DeletedCount} 件");
                if (_assetResult.FailedCount > 0)
                    sb.AppendLine($"  復元できなかったファイル: {_assetResult.FailedCount} 件");

                if (_assetResult.MissingBlobRelPaths.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  控え（プール）に実体が無く復元できなかったファイル（現在のファイルは保持）:");
                    foreach (var rel in _assetResult.MissingBlobRelPaths.Take(20))
                        sb.AppendLine("    ・" + rel);
                    if (_assetResult.MissingBlobRelPaths.Count > 20)
                        sb.AppendLine($"    …ほか {_assetResult.MissingBlobRelPaths.Count - 20} 件");
                    sb.AppendLine();
                    sb.AppendLine("  これらに対応するゲームは起動できない / 表示が欠ける可能性があります。");
                    sb.AppendLine("  別の世代を「ゲームファイルも一緒に復元」でやり直すと解消することがあります。");
                }

                if (_assetResult.DeletionSuppressed)
                {
                    sb.AppendLine();
                    sb.AppendLine("  ※ 控えの目録が不完全だったため、余分なファイルの削除は行いませんでした");
                    sb.AppendLine("    （この世代に無いファイルが現在のフォルダに残っている可能性があります）。");
                }
            }
            sb.AppendLine();
            sb.AppendLine(new string('-', 70));
            sb.AppendLine();
        }
    }
}
