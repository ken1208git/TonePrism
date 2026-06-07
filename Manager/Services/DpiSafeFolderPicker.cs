using System;
using System.IO;
using System.Windows.Forms;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// <b>ゲームフォルダ選択用</b>のヘルパー (AddGameForm / VersionUpForm)。WinForms 標準の
    /// <see cref="FolderBrowserDialog"/> を使い、加えて<b>直近に選んだフォルダを記憶</b>して次回はそこを起点に
    /// 開く (bulk 登録で毎回ツリーを頭から辿らずに済むようにする緩和策)。
    /// ※ 設定タブのログ/バックアップ参照先など他のフォルダ選択は文脈 (起点) が異なり記憶共有が不適なため
    ///   このヘルパーを経由せず <see cref="FolderBrowserDialog"/> を直接使う (DPI 縮みバグはフォルダ選択
    ///   全般に該当しうるが、本ヘルパーはゲームフォルダ用途に閉じる)。
    ///
    /// <para>背景 (なぜモダンな CommonOpenFileDialog をやめたか): フォルダ選択にはかつて WindowsAPICodePack の
    /// <c>CommonOpenFileDialog</c> (per-monitor DPI 対応のネイティブ COM ダイアログ) を使っていたが、本アプリは
    /// DPI awareness 未宣言 (unaware) のため、拡大率 100% 超 (例: 200%) で開くたびに WinForms 側ウィンドウが
    /// DPI 再スケールして<b>少しずつ縮む</b>バグがあった (画像/実行ファイル選択の標準 OpenFileDialog では起きない)。
    /// 回避を順に試したが不発: (1) スレッド DPI コンテキスト固定 → 縮み止まらず、(2) サイズ復元 → 止まらず、
    /// (3) プロセスへ DPI awareness 宣言 (app.manifest) → 全フォームのレイアウト崩壊 (実機確認)、
    /// (4) 別 STA スレッドで表示 → <c>CommonOpenFileDialog</c> がワーカースレッドで InvalidOperationException。
    /// 結論として「モダンダイアログ維持 × 縮み回避」は DPI aware 化以外に確実な道がなく、それはレイアウト崩壊で
    /// 不可のため、本ダイアログは採用しない。</para>
    ///
    /// <para>採用 (<see cref="FolderBrowserDialog"/>): 旧来のツリー型。WinForms 内で動き per-monitor COM ダイアログを
    /// 使わないため awareness ミスマッチ自体が起きず<b>縮まない</b>。UX が素朴な分は「直近フォルダ記憶」で緩和する。
    /// モダンピッカーの復活は DPI aware 前提でレイアウトを組み直す WPF 移行 (#245) で扱う。</para>
    /// </summary>
    public static class DpiSafeFolderPicker
    {
        // 直近に選択したフォルダ。プロセス内で記憶し、AddGameForm / VersionUpForm 横断で共有する
        // (どちらもゲームフォルダ選択で起点が同じ親になりやすいため共有が有益)。bulk 登録時に兄弟フォルダを
        // すぐ選べるよう次回の起点に使う。
        private static string _lastSelectedFolder;

        /// <summary>
        /// フォルダ選択ダイアログを表示し、選択されたフォルダの絶対パスを返す。キャンセル時は null。
        /// </summary>
        /// <param name="owner">モーダルのオーナー (呼び出し元フォーム)。</param>
        /// <param name="title">説明テキスト (ダイアログ上部に表示)。</param>
        /// <param name="initialDirectory">初期選択フォルダ。指定が無ければ直近選択フォルダを起点にする。null/空可。</param>
        public static string PickFolder(IWin32Window owner, string title, string initialDirectory = null)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = title;
                // 既存ゲームフォルダの選択用途なので「新しいフォルダーの作成」ボタンは出さない。
                dialog.ShowNewFolderButton = false;

                // 起点: 明示指定 > 直近選択 の順 (どちらも実在する場合のみ)。SelectedPath を与えると
                // そのフォルダを選択状態でツリーが展開され、兄弟フォルダが見える状態で開く。
                string start = null;
                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    start = initialDirectory;
                }
                else if (!string.IsNullOrEmpty(_lastSelectedFolder) && Directory.Exists(_lastSelectedFolder))
                {
                    start = _lastSelectedFolder;
                }
                if (start != null)
                {
                    dialog.SelectedPath = start;
                }

                DialogResult result = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    string path = NormalizeTrailingSeparator(dialog.SelectedPath);
                    _lastSelectedFolder = path; // 次回の起点に記憶
                    return path;
                }
                return null;
            }
        }

        /// <summary>
        /// 末尾セパレータを正規化する。<see cref="FolderBrowserDialog.SelectedPath"/> はドライブ直下選択時に
        /// 末尾 <c>\</c> を含む (例: "D:\")。MainForm の前例 (SPEC §3.6 変更履歴 R4 L-4) に倣い末尾 <c>\</c> /
        /// <c>/</c> を除去するが、<b>ドライブルート ("D:\") は除去すると drive-relative path ("D:") に化けて別物に
        /// なるため温存する</b> (長さ &gt; 3 = ドライブルートより長いものだけ trim)。
        /// </summary>
        private static string NormalizeTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= 3)
            {
                return path; // "D:\" 等のドライブルート / 空はそのまま (drive-relative 化を避ける)
            }
            return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }
}
