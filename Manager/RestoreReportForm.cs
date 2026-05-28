using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
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
        private TextBox _body;

        public RestoreReportForm(RestoreReconciliationResult result, string safetyPath)
        {
            _result = result;
            _safetyPath = safetyPath;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "復元後の整合性チェック";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 580);
            MinimumSize = new Size(560, 420);

            string headline;
            Color headColor;
            if (_result.AnalysisFailed)
            {
                headline = "整合性チェックを実行できませんでした";
                headColor = Color.DarkOrange;
            }
            else if (_result.HasCriticalFindings)
            {
                headline = "⚠ 復元後、起動できないゲームがあります（対処が必要）";
                headColor = Color.Firebrick;
            }
            else if (_result.HasAnyFindings)
            {
                headline = "復元は完了しました（軽微なズレあり・起動への影響なし）";
                headColor = Color.DarkGoldenrod;
            }
            else
            {
                headline = "✓ 復元完了：DB とゲームフォルダの整合性に問題はありません";
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

            sb.AppendLine("【まず確認】バックアップ／復元の対象は toneprism.db（データベース）だけです。");
            sb.AppendLine("ゲーム本体の games フォルダは復元されません。そのため、いま復元した DB と、");
            sb.AppendLine("ディスク上の games フォルダの「時点」がズレていると、以下のような食い違いが起きます。");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(_safetyPath))
            {
                sb.AppendLine("復元前の DB は安全のため退避済みです（元に戻したいときはここから）:");
                sb.AppendLine("  " + _safetyPath);
                sb.AppendLine();
            }
            sb.AppendLine(new string('-', 70));
            sb.AppendLine();

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
            sb.AppendLine("  1. すべての展示 PC で Launcher を閉じてください（ファイルを掴んでいると");
            sb.AppendLine("     フォルダの差し替えができません）。");
            sb.AppendLine();
            if (_result.BrokenGames.Count > 0 || _result.MissingVersionFolders.Count > 0)
            {
                sb.AppendLine("  2. 上の「想定パス／想定フォルダ」に当たるゲームフォルダを用意します。");
                sb.AppendLine("     復元した DB と同じ時点の games フォルダが、別 PC・別ドライブ・");
                sb.AppendLine("     共有サーバー等に残っていれば、そのフォルダを games/ 配下にコピーして");
                sb.AppendLine("     ください（フォルダ名＝ゲーム ID／バージョン leaf を一致させる）。");
                sb.AppendLine();
                sb.AppendLine("  3. コピー後に Manager を再起動すると、このチェックが再度かかります。");
                sb.AppendLine("     「起動できないゲーム」が 0 件になれば整合した状態です。");
                sb.AppendLine();
                sb.AppendLine("  4. 当時の games フォルダがもう手に入らない場合は、無理に DB を合わせず、");
                sb.AppendLine("     上の退避ファイル（safety_*.db）からいまの games に合う DB に");
                sb.AppendLine("     戻すのが安全です（バックアップ画面の「復元」で safety を選択）。");
            }
            else
            {
                sb.AppendLine("  2. 余分なフォルダは中身を確認のうえ、不要であれば手動で削除してください。");
                sb.AppendLine("     必要なフォルダを誤って消さないよう、削除前に中身をご確認ください。");
            }
            sb.AppendLine();
            sb.AppendLine("  ※ DB をいまのゲームフォルダに合った状態へ戻したいだけなら、退避ファイル");
            sb.AppendLine("     （safety_*.db）からの再復元が確実です。");

            return sb.ToString();
        }
    }
}
