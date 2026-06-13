using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;

namespace TonePrism.Manager
{
    /// <summary>
    /// SQLite接続管理・WALモード・リトライロジック
    /// </summary>
    public class DatabaseConnection
    {
        private readonly string connectionString;
        private readonly string dbPath;

        public string ConnectionString => connectionString;
        public string DbPath => dbPath;

        public DatabaseConnection() : this(PathManager.DatabasePath) { }

        /// <summary>
        /// (#239 テスト基盤) 任意の DB パスを指定して接続を作る。production は `PathManager.DatabasePath` を使う
        /// 既定 ctor 経由で、本 ctor は主にテストが一時 DB を指すために使う (PathManager のプロジェクトルート
        /// 検出に依存せず `SchemaManager` / repository を単体で回せるようにする)。本体挙動は不変。
        /// </summary>
        public DatabaseConnection(string dbPath)
        {
            this.dbPath = dbPath;
            // SMB ネットワーク共有上での運用安全性のため journal_mode=DELETE を使用 (#103)
            // Busy Timeout はライブラリ側にもフォールバックとして指定する。
            // Data Source は ToSqliteDataSource で UNC 正規化 (\→/) する (理由は同メソッド docstring)。
            connectionString = $"Data Source={ToSqliteDataSource(dbPath)};Version=3;Busy Timeout=10000;";
        }

        /// <summary>
        /// (UNC fix #373 / PR #374) SQLite の `Data Source=` に渡すパスを正規化する。System.Data.SQLite の native
        /// (3.46.1) は生 UNC (`\\server\share\...`) を open できない ("unable to open database file") が、forward
        /// slash (`//server/share/...`) なら通る (実測ハーネスで `\\`=NG / `//`=OK を確認。sqlite3.exe は別ビルドで
        /// `\\` も通る)。UNC 直起動 (ネットワーク場所から exe を起動) でも開けるよう、UNC のときだけ `\` を `/` に
        /// 変換する (SQLite は Windows で `/` を受け付けるため安全)。マップドライブ (`Z:\`) / ローカル (`C:\`) は
        /// `\\` 始まりでないので無変換＝既存挙動ゼロ変化 (本番はマップドライブ運用で元々この経路を踏まない)。
        /// extended-length prefix (`\\?\`) は native が別扱いで変換後 (`//?/`) の挙動が未実証のため対象外
        /// (`PathManager` はこれを生成しない)。
        ///
        /// **重要**: DB 本体の接続だけでなく backup / restore / 整合性チェック等、`Data Source=` を組み立てる
        /// すべての箇所で本メソッドを通すこと。1 箇所でも生パスを渡すと、UNC 直起動時にその経路だけ open に失敗し
        /// 「起動はするがバックアップ/復元だけ落ちる」等の部分破綻になる (PR #374 review #1)。
        ///
        /// 本ヘルパーは `Program.cs` の起動時ログ設定読み出し（**Logger 初期化前**）からも呼ばれるため、Logger 等に
        /// 依存しない純粋関数に保つ。未対応入力（extended-length `\\?\` 等）でも例外/ログを足さず raw を返す設計＝
        /// 呼び出し側の既存 try-catch に degrade を委ねる（PR #374 review #1 の「`\\?\` で明示ログ/例外」提案は、
        /// この pre-Logger 制約と「PathManager は `\\?\` を生成しない」前提により見送り）。
        /// </summary>
        internal static string ToSqliteDataSource(string path)
        {
            if (path != null
                && path.StartsWith(@"\\", StringComparison.Ordinal)
                && !path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path.Replace('\\', '/');
            }
            return path;
        }

        public bool DatabaseExists()
        {
            return File.Exists(dbPath);
        }

        /// <summary>
        /// ジャーナルモードと PRAGMA を設定して接続を開く
        /// SMB 共有上では WAL モードが動作保証外のため DELETE モードを使用 (#103)
        /// </summary>
        public void OpenConnectionWithJournalMode(SQLiteConnection connection)
        {
            connection.Open();

            using (var command = new SQLiteCommand("PRAGMA journal_mode=DELETE;", connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("PRAGMA busy_timeout=10000;", connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("PRAGMA synchronous=NORMAL;", connection))
            {
                command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("PRAGMA foreign_keys=ON;", connection))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// データベース操作をリトライ付きで実行するヘルパーメソッド
        /// </summary>
        public T ExecuteWithRetry<T>(Func<T> action, int maxRetries = 3, int delayMs = 100)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    return action();
                }
                catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Busy || ex.ResultCode == SQLiteErrorCode.Locked)
                {
                    if (i == maxRetries - 1) throw;
                    Thread.Sleep(delayMs);
                }
            }
            // ループは「成功時 return」「最終 retry で throw」のいずれかで必ず抜けるため到達しない。
            // 万一 maxRetries<=0 等で到達した場合に default(T) を silent に返さず、明示的に失敗させる。
            throw new InvalidOperationException("ExecuteWithRetry: unreachable (maxRetries 指定が不正の可能性)");
        }

        /// <summary>
        /// データベース操作をリトライ付きで実行するヘルパーメソッド（戻り値なし）
        /// </summary>
        public void ExecuteWithRetry(Action action, int maxRetries = 3, int delayMs = 100)
        {
            ExecuteWithRetry<object>(() => { action(); return null; }, maxRetries, delayMs);
        }

        /// <summary>
        /// SQLiteエラーのユーザー向けメッセージ変換
        /// </summary>
        public static string GetUserFriendlyErrorMessage(SQLiteException ex)
        {
            switch (ex.ResultCode)
            {
                case SQLiteErrorCode.Constraint:
                    if (ex.Message.Contains("UNIQUE constraint failed"))
                        return "ユニーク制約違反です。すでに存在するIDを使用している可能性があります。";
                    return "データベースの制約に違反しています。";
                case SQLiteErrorCode.Locked:
                case SQLiteErrorCode.Busy:
                    return "データベースがロックされています。他のアプリケーションが使用中か確認してください。";
                case SQLiteErrorCode.ReadOnly:
                    return "データベースは読み取り専用です。書き込み権限を確認してください。";
                case SQLiteErrorCode.Corrupt:
                    return "データベースファイルが破損しています。";
                case SQLiteErrorCode.Full:
                    return "ディスク容量が不足しています。";
                default:
                    return $"データベースエラーが発生しました (Code: {ex.ResultCode}): {ex.Message}";
            }
        }
    }
}
