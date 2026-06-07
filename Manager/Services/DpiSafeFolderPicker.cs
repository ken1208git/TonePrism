using System;
using System.IO;
using System.Windows.Forms;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// フォルダ選択の共通ヘルパー。WinForms 標準の <see cref="FolderBrowserDialog"/> を使う。
    /// 加えて<b>直近に選んだフォルダを記憶</b>し、次回はそこを起点に開く (bulk 登録で毎回ツリーを
    /// 頭から辿らずに済むようにする緩和策)。
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
        // 直近に選択したフォルダ (プロセス内で記憶)。bulk 登録時に兄弟フォルダをすぐ選べるよう次回の起点に使う。
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
                    _lastSelectedFolder = dialog.SelectedPath; // 次回の起点に記憶
                    return dialog.SelectedPath;
                }
                return null;
            }
        }
    }
}
