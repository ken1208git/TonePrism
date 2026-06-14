using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TonePrism.Manager.Shell
{
    /// <summary>
    /// (#245) 左クリックで開くフライアウト (Popup / ContextMenu) を Win11 のドロップダウン流に振る舞わせる共通ヘルパー。
    ///
    /// WPF 既定だと、開いているフライアウトの外側 (= 開いた当のボタン含む) をクリックすると、その click が下の要素まで
    /// 伝わって「閉じる→再オープン」してしまう。これを「**開いている間の本体側クリックは“閉じる”だけに消費し、下の要素を
    /// 反応させない**」(= Win11 挙動) に統一する。
    ///
    /// 重要: Popup/ContextMenu の中身クリックも routed event はメイン木の root まで届く (別 HWND でも logical に繋がる)。
    /// そのため root の PreviewMouseDown では「クリック元が開いてるフライアウトの中身か」(<see cref="IsInsideOpen"/>) を
    /// 先に判定し、中身なら dismiss せず操作させる。中身でない (= ページ本体 / トリガボタン) ときだけ閉じて消費する。
    ///
    /// **使い方 (フライアウトを持つページごと):**
    ///   1) ページ root の <c>PreviewMouseDown</c> → <c>if (FlyoutDismiss.IsInsideOpen(e.OriginalSource as DependencyObject)) return; if (FlyoutDismiss.DismissOpen()) e.Handled = true;</c>
    ///   2) 各 Popup/ContextMenu の <c>Opened</c>→<c>NotifyOpened(...)</c> / <c>Closed</c>→<c>NotifyClosed()</c>
    ///   3) 開くボタンは <c>IsOpen=true</c> だけ。ComboBox はネイティブ自己トグルなので対象外。
    /// </summary>
    internal static class FlyoutDismiss
    {
        private static Action _closeCurrent;
        private static DependencyObject _openContent;   // 開いてるフライアウトの中身ルート (Popup.Child / ContextMenu)

        public static void NotifyOpened(Popup popup) { _closeCurrent = () => popup.IsOpen = false; _openContent = popup.Child; }
        public static void NotifyOpened(ContextMenu menu) { _closeCurrent = () => menu.IsOpen = false; _openContent = menu; }
        public static void NotifyClosed() { _closeCurrent = null; _openContent = null; }

        /// <summary>クリック元 src が開いてるフライアウトの中身 (Popup の中身 / メニュー項目 / その中のコンボ
        /// ドロップダウン等) なら true。visual 親を辿り、Popup 境界等で切れたら logical 親で繋いで中身ルートまで遡る。</summary>
        public static bool IsInsideOpen(DependencyObject src)
        {
            var content = _openContent;
            if (content == null || src == null) return false;
            DependencyObject d = src;
            while (d != null)
            {
                if (ReferenceEquals(d, content)) return true;
                DependencyObject parent = (d is Visual) ? VisualTreeHelper.GetParent(d) : null;
                d = parent ?? LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        /// <summary>開いているフライアウトがあれば閉じて true を返す。呼び出し側は true のとき e.Handled=true で click を消費する。</summary>
        public static bool DismissOpen()
        {
            if (_closeCurrent == null) return false;
            var close = _closeCurrent;
            _closeCurrent = null;   // 多重発火防止: 先に解除してから閉じる
            _openContent = null;
            close();
            return true;
        }
    }
}
