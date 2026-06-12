using System;
using System.IO;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#362) 設定タブの即時反映で、パス欄を離脱した時に「どう反映するか」を決める純粋な分類ロジック。
    /// UI から切り離して単体テスト可能にする (`SessionConflictPolicy` パターン)。実際の存在確認
    /// (`Directory.Exists`) は呼び出し側から注入し、ロジック自体は FS/ネットワークに依存させない。
    ///
    /// 分類と UI 側の対応 (詳細はメモリ/#362):
    /// - <see cref="SettingsPathKind.Empty"/>: 空 → デフォルト (DB 隣の logs/) 扱いで反映
    /// - <see cref="SettingsPathKind.Relative"/> / <see cref="SettingsPathKind.Invalid"/>: 相対 or 構文不正 →
    ///   反映せずエラー表示 (欄に留まる)
    /// - <see cref="SettingsPathKind.Ok"/>: 絶対 + 存在 → そのまま反映
    /// - <see cref="SettingsPathKind.MissingLocal"/>: 絶対 + 不在だが root (ドライブ/共有) は到達可 = 作成可能 →
    ///   「作成しますか?」prompt → はいで作成 + 反映
    /// - <see cref="SettingsPathKind.Unreachable"/>: 絶対 + root も到達不可 (ドライブ無し/サーバ落ち) →
    ///   注意表示のみで反映は通す (バックアップ先 = 共有ファイルサーバ想定、一時ダウンで誤拒否しないため)
    /// </summary>
    public enum SettingsPathKind { Empty, Relative, Invalid, Ok, MissingLocal, Unreachable }

    public static class SettingsPathPolicy
    {
        /// <param name="path">ユーザー入力のパス (null/空白可)。</param>
        /// <param name="dirExists">ディレクトリ存在確認 (本番 = <c>Directory.Exists</c>、テスト = モック)。</param>
        public static SettingsPathKind Classify(string path, Func<string, bool> dirExists)
        {
            if (dirExists == null) throw new ArgumentNullException(nameof(dirExists));
            if (string.IsNullOrWhiteSpace(path)) return SettingsPathKind.Empty;

            string trimmed = path.Trim();
            bool rooted;
            try { rooted = Path.IsPathRooted(trimmed); }
            catch { return SettingsPathKind.Invalid; } // 不正文字等で例外 → 不正扱い
            if (!rooted) return SettingsPathKind.Relative;

            // 存在すれば OK。存在確認自体が失敗 (権限等) したら不在扱いで下の root 判定へ。
            try { if (dirExists(trimmed)) return SettingsPathKind.Ok; }
            catch { /* fall through */ }

            // 不在: root (ドライブ/共有) が到達可なら作成可能 (MissingLocal)、不可なら Unreachable。
            string root;
            try { root = Path.GetPathRoot(trimmed); }
            catch { return SettingsPathKind.Invalid; }
            if (string.IsNullOrEmpty(root)) return SettingsPathKind.Unreachable;
            try { if (dirExists(root)) return SettingsPathKind.MissingLocal; }
            catch { /* root 確認失敗 = 到達不可扱い */ }
            return SettingsPathKind.Unreachable;
        }
    }
}
