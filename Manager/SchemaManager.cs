using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using TonePrism.Manager.Services;

namespace TonePrism.Manager
{
    /// <summary>
    /// テーブル作成・スキーママイグレーション・バージョン管理
    /// </summary>
    public class SchemaManager
    {
        private readonly DatabaseConnection _conn;

        // 現在のデータベースバージョン
        // 構造変更があるたびにインクリメントする
        // v11: SPEC v1.5.1 (2026-03-28) で変更された surveys / play_records スキーマの drift 修正（v0.8.1）
        // v12: backup_log に relative_path 列追加 (#127、v0.8.2)
        // v13: manager_sessions テーブル新設 (#179、Manager LAN-wide 同時起動検出、v0.10.0)
        // v14: games.arguments を CreateTables 内アドホック ALTER から正規 MigrateV13ToV14 に移設
        //      (累積レビュー / AGENTS.md スキーマ drift 規約準拠、v0.16.3)。最終スキーマは不変。
        // v15: game_versions(game_id, version) に UNIQUE INDEX を追加 (#234 ②)。同一ゲームに同一
        //      バージョン番号が 2 行入る silent corruption を DB レベルで防ぐ最後の砦。重複残存時は
        //      throw せず skip + 警告 (V10→V11 と同じ "data residual → retry" パターン)。
        // v16: backup_log.trigger_type CHECK に 'restore' を追加 (H4)。リストアイベントの監査ログを
        //      backup_log に記録できるようにする。既存行は影響なし (CHECK 拡張のみ)。
        // v17: game_versions UNIQUE INDEX を COLLATE NOCASE で作り直す (M3)。`v1.0.0` と `V1.0.0` の
        //      case 違いを semantic dup として弾く。重複残存時は v14→v15 と同じ skip + retry パターン。
        // v18: developers.version_id に FK + ON DELETE CASCADE を追加 + dead table game_genres を DROP (Medium-22)。
        // v19: backup_log テーブルを DROP。バックアップ履歴を DB から外し backups/ フォルダ走査
        //      (BackupCatalogService) 由来に変更。reconcile / register / drift 対策コードを全廃し、失敗復元が
        //      success 化する欠陥を根治。既存行は破棄されるが物理ファイルは残り初回走査で履歴に復活する。
        private const int CurrentDbVersion = 22;

        public SchemaManager(DatabaseConnection conn)
        {
            _conn = conn;
        }

        public int GetTargetDatabaseVersion()
        {
            return CurrentDbVersion;
        }

        public int GetActualDatabaseVersion()
        {
            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);
                    return GetDbVersion(connection);
                }
            });
        }

        public bool TablesExist()
        {
            if (!_conn.DatabaseExists()) return false;

            return _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    using (var command = new SQLiteCommand(
                        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='games'",
                        connection))
                    {
                        long count = (long)command.ExecuteScalar();
                        return count > 0;
                    }
                }
            });
        }

        public void InitializeDatabase()
        {
            _conn.ExecuteWithRetry(() =>
            {
                using (var connection = new SQLiteConnection(_conn.ConnectionString))
                {
                    _conn.OpenConnectionWithJournalMode(connection);

                    // (#247) migration / 初期 stamp が必要なときだけ foreign_keys を OFF にしてから transaction を
                    // 開始する。理由: v19→v20 の games (親テーブル) recreate は `DROP TABLE games` の暗黙 DELETE が
                    // ON DELETE CASCADE を発火させ子テーブル (game_versions / developers / ...) を全消去する。
                    // `defer_foreign_keys` は検査を遅延するだけで CASCADE action は止めない (sqlite3 実測で確認) ため
                    // foreign_keys=OFF が必須。PRAGMA foreign_keys は transaction 内で変更できないので BeginTransaction
                    // より前に設定する。通常起動 (version == CurrentDbVersion) では FK=ON のまま = 既存挙動を維持し、
                    // blast radius を「実際に migration が走る起動」のみに限定する。OFF にした migration では commit 前に
                    // foreign_key_check で整合を検証する (SQLite 公式のスキーマ変更手順)。
                    bool fkDisabledForMigration = false;
                    int versionBeforeMigration = GetDbVersion(connection);
                    if (versionBeforeMigration < CurrentDbVersion)
                    {
                        using (var cmd = new SQLiteCommand("PRAGMA foreign_keys=OFF", connection))
                        {
                            cmd.ExecuteNonQuery();
                        }
                        fkDisabledForMigration = true;
                        Logger.Info("[DatabaseManager] (#247) migration 検出 (v" + versionBeforeMigration + " < v" + CurrentDbVersion + ")、foreign_keys=OFF で実行 (親テーブル recreate の CASCADE 暴発防止)");
                    }

                    // (PR #236 レビュー対応 #5) FK=ON 復帰を finally に置く。旧実装は復帰が using(transaction) の
                    // 後・try の外にあり、migration が例外で抜けると ON 復帰を通らず接続が FK=OFF のまま close していた。
                    // 現状は pooling 無効 (接続文字列に Pooling=True 無し) + 次回 open 時 ON 再設定で self-healing の
                    // ため実害は無いが、将来 pooling 有効化時に FK=OFF 接続がプールへ還る穴を構造的に閉じる。
                    try
                    {
                        using (var transaction = connection.BeginTransaction())
                        {
                            try
                            {
                                CreateTables(connection, transaction);
                                MigrateDevelopersTable(connection, transaction);
                                Logger.Info("[DatabaseManager] Calling MigrateGamesTable...");
                                MigrateGamesTable(connection, transaction);
                                MigrateSurveysTable(connection, transaction);
                                MigrateGameVersionsTable(connection, transaction);
                                CheckAndMigrateDatabase(connection, transaction);

                                // (#247) FK を OFF にして migration した場合、commit 前に foreign_key_check で整合を
                                // 検証する。違反は warn ログのみで起動は継続 (VerifySchema と同じ非破壊方針)。
                                if (fkDisabledForMigration)
                                {
                                    VerifyForeignKeyIntegrity(connection, transaction);
                                }

                                // 全マイグレーション完了後にスキーマ整合性を検証する。
                                // drift があった場合でも例外は投げず警告ログのみ。
                                // （AGENTS.md "Database Schema Management" 参照）
                                VerifySchema(connection, transaction);

                                transaction.Commit();
                            }
                            catch (Exception)
                            {
                                transaction.Rollback();
                                throw;
                            }
                        }
                    }
                    finally
                    {
                        // (#247 / #5) FK を OFF にしていた場合は commit / rollback / 例外いずれの経路でも必ず ON に
                        // 戻す。transaction は using を抜けて解放済のため PRAGMA が有効。復帰自体の失敗は握り潰す
                        // (OpenConnectionWithJournalMode が次回 open 時に ON を再設定する self-healing が効くため)。
                        if (fkDisabledForMigration)
                        {
                            try
                            {
                                using (var cmd = new SQLiteCommand("PRAGMA foreign_keys=ON", connection))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            catch (Exception fkEx)
                            {
                                Logger.Warn("[DatabaseManager] (#5) foreign_keys=ON 復帰に失敗 (次回 open で self-heal): " + fkEx.Message);
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// データベースを完全初期化する (rename rollback 方式)。
        /// (1) games/ を pending-delete-{guid} に rename で退避、
        /// (2) toneprism.db を削除、(3) 退避フォルダを物理削除、
        /// (4) games/ を再作成 + DB 再構築。
        /// 隣接する backups/ などには触らない（復元用に残す）。
        /// 確認画面 (ResetDatabaseConfirmForm) と挙動を一致させるための実装 (#119)。
        ///
        /// 退避 rename を使う理由 (Codex P1 指摘 #121):
        /// 「games 物理削除 → DB 削除」順だと、DB 削除でロック等で失敗した場合に
        /// games が消えたまま DB に古いレコードが残る broken partial-reset 状態になる。
        /// rename ならフォルダ実体は退避先に残っているので、DB 削除失敗時は rename を
        /// 戻して「何も変わってない」状態にロールバックできる。同一ボリューム rename は
        /// SMB 上でも事実上 atomic。ただし Launcher が games/ 内ファイルをロック中なら
        /// rename 自体が失敗するが、その場合も DB は無傷のまま中止できる。
        /// </summary>
        /// <returns>
        /// 退避フォルダ物理削除の結果。Success=true なら完全成功 (退避フォルダも消えた)。
        /// Success=false なら DB / games/ は再構築済みだが退避フォルダだけ残っている状態
        /// (LastError と Path に詳細あり)。呼び出し側は Result を見て再試行 UI を出すか
        /// 警告だけ表示するかを判断する (#122 Group C)。
        /// 真に失敗 (rename 失敗 / DB 削除失敗 / 再初期化失敗) した場合は IOException 等を throw する。
        /// </returns>
        public Services.FolderDeletionService.Result ResetDatabase()
        {
            string dbPath = _conn.DbPath;
            string gamesFolder = PathManager.GamesFolder;
            string pendingDeleteFolder = gamesFolder + ".pending-delete-" + Guid.NewGuid().ToString("N");

            // (1) games/ を pending-delete-{guid}/ に rename して退避
            //     失敗 = Launcher 等がフォルダ内ファイルをロック中。DB は無事なので中止
            bool gamesRenamed = false;
            if (Directory.Exists(gamesFolder))
            {
                try
                {
                    Directory.Move(gamesFolder, pendingDeleteFolder);
                    gamesRenamed = true;
                }
                catch (IOException ioEx)
                {
                    throw new IOException(
                        $"games フォルダの退避（リネーム）に失敗しました。Launcher など他のプロセスがファイルを使用していないか確認してください。\n\n{ioEx.Message}",
                        ioEx);
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    throw new UnauthorizedAccessException(
                        $"games フォルダへのアクセスが拒否されました。フォルダのアクセス権限を確認してください。\n\n{uaEx.Message}",
                        uaEx);
                }
            }

            // (2) DB ファイル削除
            //     失敗時は (1) でやった rename を戻して「何も変わってない」状態にロールバック
            try
            {
                if (File.Exists(dbPath))
                {
                    try
                    {
                        File.Delete(dbPath);
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(500);
                        File.Delete(dbPath);
                    }
                }
            }
            catch (Exception dbEx)
            {
                // ロールバック: pending-delete を games/ に戻す
                if (gamesRenamed && Directory.Exists(pendingDeleteFolder) && !Directory.Exists(gamesFolder))
                {
                    try
                    {
                        Directory.Move(pendingDeleteFolder, gamesFolder);
                    }
                    catch
                    {
                        // ロールバック自体が失敗するケースは極めて稀だが、握りつぶさず元の例外と一緒に通知
                        throw new IOException(
                            $"toneprism.db の削除に失敗し、games フォルダの復元（ロールバック）にも失敗しました。\n" +
                            $"以下のフォルダを手動で確認してください:\n  退避先: {pendingDeleteFolder}\n  本来の場所: {gamesFolder}\n\n" +
                            $"元のエラー: {dbEx.Message}", dbEx);
                    }
                }
                throw new IOException(
                    $"toneprism.db の削除に失敗しました。Launcher など他のプロセスが DB を使用していないか確認してください。games フォルダは元に戻されています。\n\n{dbEx.Message}",
                    dbEx);
            }

            // (3) 新しい games/ を作成して DB 再初期化
            //     ここで失敗 (権限・ディスクフル・SQLite エラー等) すると DB / games が
            //     完全に壊れた状態になるため、可能な範囲でロールバックする
            //     (Codex P1 #121 への 4 度目の対応)。退避フォルダはまだ手元にあるので戻せる
            try
            {
                Directory.CreateDirectory(gamesFolder);
                InitializeDatabase();
            }
            catch (Exception initEx)
            {
                // ロールバック: 部分作成された games/ を消す
                if (Directory.Exists(gamesFolder))
                {
                    try { Directory.Delete(gamesFolder, true); } catch { /* best effort */ }
                }
                // ロールバック: 部分作成された toneprism.db を消す (壊れた DB を残さない)
                if (File.Exists(dbPath))
                {
                    try { File.Delete(dbPath); } catch { /* best effort */ }
                }
                // ロールバック: 退避フォルダを games/ に戻す
                string rollbackHint;
                if (gamesRenamed && Directory.Exists(pendingDeleteFolder) && !Directory.Exists(gamesFolder))
                {
                    try
                    {
                        Directory.Move(pendingDeleteFolder, gamesFolder);
                        rollbackHint = "古い games フォルダは元の場所に復元されました。バックアップ機能 (#96) から toneprism.db を復元してください。";
                    }
                    catch
                    {
                        rollbackHint = $"古い games フォルダの復元（ロールバック）にも失敗しました。手動で以下のフォルダを確認してください:\n  退避先: {pendingDeleteFolder}\n  本来の場所: {gamesFolder}";
                    }
                }
                else
                {
                    rollbackHint = "(games フォルダは元々存在しなかったため、ロールバック対象なし)";
                }

                throw new IOException(
                    $"games/ 再作成または DB 再初期化に失敗しました。\n\n{initEx.Message}\n\n{rollbackHint}",
                    initEx);
            }

            // (4) 退避フォルダを物理削除を試みる（失敗しても DB / games は再構築済みなので
            //     呼び出し側に Result を返すだけにする。Codex P2 #121: 例外でなく戻り値で表現）
            //     rename はファイルロックを解除しないため、Launcher が起動中のゲームの
            //     実行ファイルを掴んでいるとここで IOException が出る可能性がある。
            //     FolderDeletionService が内部で 5 × 200ms リトライしてから結果を返す (#122)
            return Services.FolderDeletionService.TryDelete(pendingDeleteFolder);
        }

        private void CreateTables(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // gamesテーブル作成
            string createGamesTable = @"
                CREATE TABLE IF NOT EXISTS games (
                    game_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    release_year INTEGER,
                    genre TEXT,
                    min_players INTEGER,
                    max_players INTEGER,
                    difficulty INTEGER CHECK(difficulty BETWEEN 1 AND 3),
                    play_time INTEGER CHECK(play_time BETWEEN 1 AND 3),
                    controller_support INTEGER DEFAULT 0,
                    supported_connection INTEGER DEFAULT 0,
                    thumbnail_path TEXT,
                    background_path TEXT,
                    executable_path TEXT,
                    display_order INTEGER DEFAULT 0,
                    is_visible INTEGER DEFAULT 1,
                    controls TEXT,
                    key_mapping TEXT,
                    arguments TEXT,
                    version TEXT
                )";

            using (var command = new SQLiteCommand(createGamesTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // games.arguments の既存 DB への retrofit は MigrateV13ToV14 (version chain) で行う。
            // 新規 DB は上の CREATE TABLE で arguments を持つため CreateTables 側での ALTER は不要。

            // game_versionsテーブル作成
            string createGameVersionsTable = @"
                CREATE TABLE IF NOT EXISTS game_versions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT NOT NULL,
                    version TEXT NOT NULL,
                    executable_path TEXT NOT NULL,
                    arguments TEXT,
                    description TEXT,
                    title TEXT,
                    genre TEXT,
                    min_players INTEGER,
                    max_players INTEGER,
                    difficulty INTEGER,
                    play_time INTEGER,
                    controller_support INTEGER DEFAULT 0,
                    supported_connection INTEGER DEFAULT 0,
                    thumbnail_path TEXT,
                    background_path TEXT,
                    update_note TEXT,
                    registered_at TEXT NOT NULL,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createGameVersionsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // (#234 ②) 同一ゲームに同一バージョン番号が 2 行入る silent corruption を DB レベルで防ぐ
            // UNIQUE INDEX。新規 DB はここで作成 (空テーブルなので重複なし)。既存 DB は MigrateV14ToV15 が
            // dedup-skip 安全付きで追加する。重複残存時も throw しない (= 起動継続、戻り値は無視)。
            EnsureGameVersionsVersionUniqueIndex(connection, transaction);

            // developersテーブル作成
            // (累積監査 round 4 Medium-22) v18 で version_id に FK + ON DELETE CASCADE を追加した。
            // 旧 schema は version_id INTEGER (FK なし) で、将来「単一版削除」機能 (#101 / #30 関連) が
            // 入った時にその版に紐付く developers 行が silent orphan になる経路があった。本 FK で構造的に閉鎖。
            string createDevelopersTable = @"
                CREATE TABLE IF NOT EXISTS developers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    last_name TEXT,
                    first_name TEXT,
                    grade TEXT,
                    version_id INTEGER,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE,
                    FOREIGN KEY(version_id) REFERENCES game_versions(id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createDevelopersTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // (累積監査 round 4 Low-28/29) game_genres は dead table のため v18 で DROP 済 (MigrateV17ToV18 参照)。
            // 新規 install では作成しない。SoT は `games.genre` のカンマ区切り文字列 (GameRepository が直接 read/write)。

            // play_recordsテーブル作成（MigrateV10ToV11 でも再利用するため helper メソッド化）
            CreatePlayRecordsTable(connection, transaction);

            // surveysテーブル作成（MigrateV10ToV11 でも再利用するため helper メソッド化）
            CreateSurveysTable(connection, transaction);

            // launcher_surveysテーブル作成
            string createLauncherSurveysTable = @"
                CREATE TABLE IF NOT EXISTS launcher_surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    favorite_game_id TEXT,
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(favorite_game_id) REFERENCES games(game_id) ON DELETE SET NULL
                )";

            using (var command = new SQLiteCommand(createLauncherSurveysTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // settingsテーブル作成
            string createSettingsTable = @"
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT
                )";

            using (var command = new SQLiteCommand(createSettingsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // settings テーブルが古いスキーマ（id / color_theme / launcher_settings / filter_settings の単一行型）の場合、
            // KVS スキーマへ移行する。SPECIFICATION 1.3.1 (2026-02-08) で KVS 化されたが、
            // 既存DB向けマイグレーションが実装されていなかったため Manager v0.8.0 でフォローする。
            EnsureSettingsTableIsKvsSchema(connection, transaction);

            // store_sectionsテーブル作成
            string createStoreSectionsTable = @"
                CREATE TABLE IF NOT EXISTS store_sections (
                    section_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    section_type INTEGER DEFAULT 0,
                    section_source TEXT DEFAULT 'manual',
                    display_order INTEGER DEFAULT 0,
                    max_display_count INTEGER DEFAULT 5,
                    is_visible INTEGER DEFAULT 1
                )";

            using (var command = new SQLiteCommand(createStoreSectionsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // store_section_gamesテーブル作成
            string createStoreSectionGamesTable = @"
                CREATE TABLE IF NOT EXISTS store_section_games (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    section_id INTEGER NOT NULL,
                    game_id TEXT NOT NULL,
                    display_order INTEGER DEFAULT 0,
                    display_text TEXT DEFAULT '',
                    FOREIGN KEY(section_id) REFERENCES store_sections(section_id) ON DELETE CASCADE,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE,
                    UNIQUE(section_id, game_id)
                )";

            using (var command = new SQLiteCommand(createStoreSectionGamesTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            // backup_log は v19 で DROP (履歴を backups/ フォルダ走査 = BackupCatalogService に移行)。新規 DB では
            // 作成しない。CreateBackupLogTable 本体は古い DB の v9 段階移行 (MigrateV8ToV9) が呼ぶため残置する。

            // (#179) manager_sessions テーブル作成 (v13 で追加、MigrateV12ToV13 でも再利用する helper)
            CreateManagerSessionsTable(connection, transaction);

            // (#253) intro_slides テーブル作成 (v21 で追加、MigrateV20ToV21 でも再利用する helper)
            CreateIntroSlidesTable(connection, transaction);

            // 新規DB向けにバックアップ関連の設定デフォルト値を投入
            InsertBackupDefaults(connection, transaction);
        }

        /// <summary>
        /// settings テーブルが古いスキーマの場合、KVS スキーマへ移行する。
        /// 古いスキーマ（id / color_theme / launcher_settings / filter_settings 等）には
        /// 実コードからの読み書きが存在しなかったため、データロスは発生しない。
        /// 念のため `settings_legacy_v8_or_earlier` としてリネームしてから新規作成する。
        /// </summary>
        private void EnsureSettingsTableIsKvsSchema(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 1. settings テーブルが存在するか
            bool settingsExists;
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='settings'",
                connection, transaction))
            {
                long count = (long)cmd.ExecuteScalar();
                settingsExists = count > 0;
            }
            if (!settingsExists)
            {
                // 直前の CREATE TABLE IF NOT EXISTS で必ず作成されているはずだが、念のため。
                return;
            }

            // 2. 'key' カラムがあるか
            bool hasKeyColumn = false;
            using (var cmd = new SQLiteCommand("PRAGMA table_info(settings)", connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == "key")
                    {
                        hasKeyColumn = true;
                        break;
                    }
                }
            }

            if (hasKeyColumn) return;

            // 3. 古いスキーマ → リネームして新規作成
            Logger.Warn("[DatabaseManager] settings テーブルが古いスキーマです。KVS方式に移行します。");

            // 既に legacy テーブルが残っていたら削除（過去に失敗した移行の残骸を掃除）
            using (var cmd = new SQLiteCommand(
                "DROP TABLE IF EXISTS settings_legacy_v8_or_earlier", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "ALTER TABLE settings RENAME TO settings_legacy_v8_or_earlier", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT)", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            Logger.Info("[DatabaseManager] settings テーブルを KVS 方式で再作成しました。" +
                              "旧データは settings_legacy_v8_or_earlier に保管されています。");
        }

        /// <summary>
        /// backup_log テーブルを作成（IF NOT EXISTS で冪等）。
        /// trigger_type は 'manual' / 'auto' / 'safety' / 'restore' の4種 (v16 で 'restore' を追加)。
        /// </summary>
        private void CreateBackupLogTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS backup_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at INTEGER NOT NULL,
                    completed_at INTEGER,
                    pc_name TEXT NOT NULL,
                    file_path TEXT,
                    relative_path TEXT,
                    file_size_bytes INTEGER,
                    status TEXT NOT NULL CHECK (status IN ('in_progress','success','failed')),
                    error_message TEXT,
                    trigger_type TEXT NOT NULL CHECK (trigger_type IN ('manual','auto','safety','restore'))
                )";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// settings テーブルにバックアップ関連のデフォルトキーを INSERT OR IGNORE で投入
        /// </summary>
        private void InsertBackupDefaults(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string[][] defaults = new[]
            {
                new[] { "last_backup_at", "0" },
                new[] { "backup_destination_path", "" },
                new[] { "backup_auto_interval_hours", "24" },
                new[] { "backup_retention_count", "30" },
                // (#250 PR1 / round9) ゲーム本体バックアップの ON/OFF (隠し既定 true、UI 無し)。保持世代数は
                // backup_retention_count に統一したため専用 key は廃止。既存 v22 DB には migration では入らないが
                // GetString の default で吸収 (settings は K/V data、schema 版不変)。
                new[] { "asset_snapshot_enabled", "true" }
            };

            foreach (var kv in defaults)
            {
                using (var command = new SQLiteCommand(
                    "INSERT OR IGNORE INTO settings (key, value) VALUES (@key, @value)",
                    connection, transaction))
                {
                    command.Parameters.AddWithValue("@key", kv[0]);
                    command.Parameters.AddWithValue("@value", kv[1]);
                    command.ExecuteNonQuery();
                }
            }
        }

        private void MigrateDevelopersTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            List<string> columns = new List<string>();
            using (var command = new SQLiteCommand("PRAGMA table_info(developers)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString());
                    }
                }
            }

            if (!columns.Contains("last_name"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN last_name TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (!columns.Contains("first_name"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN first_name TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }

            if (!columns.Contains("grade"))
            {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN grade TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void MigrateGamesTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            List<string> columns = new List<string>();
            using (var command = new SQLiteCommand("PRAGMA table_info(games)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string colName = reader["name"].ToString();
                        columns.Add(colName);
                    }
                }
            }

            Logger.Info($"[DatabaseManager] Current columns in games: {string.Join(", ", columns)}");

            if (!columns.Contains("supported_connection"))
            {
                Logger.Info("[DatabaseManager] 'supported_connection' column missing. Adding...");
                using (var command = new SQLiteCommand("ALTER TABLE games ADD COLUMN supported_connection INTEGER DEFAULT 0", connection, transaction))
                {
                    command.ExecuteNonQuery();
                    Logger.Info("[DatabaseManager] 'supported_connection' column added successfully.");
                }
            }
            else
            {
                Logger.Info("[DatabaseManager] 'supported_connection' column already exists.");
            }

            if (!columns.Contains("version"))
            {
                Logger.Info("[DatabaseManager] 'version' column missing. Adding...");
                using (var command = new SQLiteCommand("ALTER TABLE games ADD COLUMN version TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                    Logger.Info("[DatabaseManager] 'version' column added successfully.");
                }
            }
        }

        private void MigrateSurveysTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 将来的な拡張のためにメソッドを残す
        }

        private void MigrateGameVersionsTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            using (var checkCommand = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='game_versions'",
                connection, transaction))
            {
                var result = checkCommand.ExecuteScalar();
                if (result == null)
                {
                    Logger.Warn("[DatabaseManager] game_versions table does not exist. Skipping migration.");
                    return;
                }
            }

            List<string> columns = new List<string>();
            using (var command = new SQLiteCommand("PRAGMA table_info(game_versions)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string colName = reader["name"].ToString();
                        columns.Add(colName);
                    }
                }
            }

            Logger.Info($"[DatabaseManager] Current columns in game_versions: {string.Join(", ", columns)}");

            if (!columns.Contains("arguments"))
            {
                Logger.Info("[DatabaseManager] 'arguments' column missing in game_versions. Adding...");
                using (var command = new SQLiteCommand("ALTER TABLE game_versions ADD COLUMN arguments TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                    Logger.Info("[DatabaseManager] 'arguments' column added to game_versions successfully.");
                }
            }
            else
            {
                Logger.Info("[DatabaseManager] 'arguments' column already exists in game_versions.");
            }
        }

        private void CheckAndMigrateDatabase(SQLiteConnection connection, SQLiteTransaction transaction = null)
        {
            int currentVersion = GetDbVersion(connection, transaction);
            Logger.Info($"[DatabaseManager] 現在のDBバージョン: {currentVersion}, 最新バージョン: {CurrentDbVersion}");

            if (currentVersion == 0)
            {
                // 新規 DB は CreateTables が最新スキーマを作るので stamp して返すだけでよい。
                // ただし versioning 導入前 (user_version=0 のまま) に games テーブルだけ存在する
                // 旧 DB は games.arguments を欠く場合がある。この列は他の games 列
                // (supported_connection / version、MigrateGamesTable で無条件 backfill) と違い
                // version chain (MigrateV13ToV14) 管理に移したため、v0 で chain を skip すると
                // 永久に追加されず GameRepository の SELECT/INSERT が "no such column: arguments"
                // で失敗する (Codex P1)。idempotent な MigrateV13ToV14 を stamp 前に明示実行して
                // 旧実装 (CreateTables 内 retrofit) と同じ v0 カバレッジを保つ。
                MigrateV13ToV14(connection, transaction);

                // (累積監査 round 6 #13) versioning 導入前 (user_version=0 のまま) から developers テーブルが
                // 存在する旧 DB は、CreateTables の CREATE TABLE IF NOT EXISTS が既存 developers を温存するため、
                // v18 で追加した version_id / game_id の FK + ON DELETE CASCADE が付かないまま user_version
                // だけ 18 に刻印される schema drift があった (VerifySchema は列名のみ検証で FK 欠落を見逃す)。
                // 新規 DB は CreateTables が FK 付きで作るので問題ない。developers に期待 FK が無い場合のみ
                // MigrateV17ToV18 で retrofit する (新規 DB では FK 検出で skip されるため、毎回の table
                // recreate コストを common path に乗せない)。
                if (!DevelopersHasVersionIdForeignKey(connection, transaction))
                {
                    Logger.Warn("[DatabaseManager] (#13) v0 DB の developers に version_id FK が無いため MigrateV17ToV18 で retrofit します");
                    MigrateV17ToV18(connection, transaction);
                }

                // versioning 導入前 (user_version=0) から surveys / play_records テーブルが存在する旧 DB は、
                // CreateTables の CREATE TABLE IF NOT EXISTS が旧スキーマ (surveys: submitted_at/responses、
                // play_records: play_count/total_play_time) を温存するため、本来 MigrateV10ToV11 が直す drift が
                // 残ったまま user_version=19 が刻まれていた (一度刻むと再 migration 経路に乗らず永久固定)。
                // arguments 列 (V13ToV14) / developers FK (V17ToV18) / settings KVS (EnsureSettingsTableIsKvsSchema)
                // は v0 path で明示 retrofit するのに surveys/play_records だけ抜けていた非対称を解消する。
                // MigrateV10ToV11 は冪等: 新規 DB (新スキーマ) では no-op で true、旧スキーマ空テーブルは再作成、
                // データ残存時のみ skip + 警告で false (自動変換不能なため手動退避を促す。VerifySchema が以後も
                // 列名 drift を警告し続ける)。stamp は他 retrofit と同じく実行する (= 残りのスキーマは v19 で確定済、
                // surveys/play_records だけ古いまま 19 を名乗るのは元の挙動と同じだが、空テーブルは本 fix で救済される)。
                if (!MigrateV10ToV11(connection, transaction))
                {
                    Logger.Warn("[DatabaseManager] (v0 path) surveys / play_records にデータが残存し drift 修正を skip しました。" +
                        "tools/sqlite3/sqlite3.exe で旧データを退避のうえ手動で新スキーマへ移行してください。");
                }

                // (PR #236 レビュー対応) v0 fast-path も MigrateV19ToV20 (games.play_time に CHECK(1-3)) を明示 retrofit する。
                // arguments (V13ToV14) / developers FK (V17ToV18) / surveys・play_records (V10ToV11) は v0 path で明示
                // retrofit するのに play_time CHECK だけ抜けており、versioning 導入前から games テーブルを持つ旧 v0 DB は
                // CreateTables の CREATE TABLE IF NOT EXISTS が CHECK 無し games を温存するため、CHECK が付かないまま
                // user_version=20 を刻む非対称 (surveys と同型の drift) が残っていた。新規 DB は CreateTables が CHECK 付きで
                // 作るため MigrateV19ToV20 の冪等ガードで no-op。範囲外 play_time 残存時は MigrateV10ToV11 と同じく
                // warn + stamp 継続 (起動を止めない、是正後の再起動で適用)。
                if (!MigrateV19ToV20(connection, transaction))
                {
                    Logger.Warn("[DatabaseManager] (v0 path) games.play_time に範囲外 (1-3 以外) の値が残存し CHECK 追加を skip しました。" +
                        "tools/sqlite3/sqlite3.exe で値を 1-3 または NULL に是正してください。");
                }

                SetDbVersion(connection, CurrentDbVersion, transaction);
                return;
            }

            if (currentVersion < CurrentDbVersion)
            {
                Logger.Info($"[DatabaseManager] マイグレーションを開始します: v{currentVersion} -> v{CurrentDbVersion}");

                bool localTransaction = (transaction == null);
                SQLiteTransaction migTransaction = transaction;

                if (localTransaction)
                {
                    migTransaction = connection.BeginTransaction();
                }

                try
                {
                    if (currentVersion < 2)
                    {
                        MigrateV1ToV2(connection, migTransaction);
                        currentVersion = 2;
                    }

                    if (currentVersion < 3)
                    {
                        MigrateV2ToV3(connection, migTransaction);
                        currentVersion = 3;
                    }

                    if (currentVersion < 4)
                    {
                        MigrateV3ToV4(connection, migTransaction);
                        currentVersion = 4;
                    }

                    if (currentVersion < 5)
                    {
                        MigrateV4ToV5(connection, migTransaction);
                        currentVersion = 5;
                    }

                    if (currentVersion < 6)
                    {
                        MigrateV5ToV6(connection, migTransaction);
                        currentVersion = 6;
                    }

                    if (currentVersion < 7)
                    {
                        MigrateV6ToV7(connection, migTransaction);
                        currentVersion = 7;
                    }

                    if (currentVersion < 8)
                    {
                        MigrateV7ToV8(connection, migTransaction);
                        currentVersion = 8;
                    }

                    if (currentVersion < 9)
                    {
                        MigrateV8ToV9(connection, migTransaction);
                        currentVersion = 9;
                    }

                    if (currentVersion < 10)
                    {
                        MigrateV9ToV10(connection, migTransaction);
                        currentVersion = 10;
                    }

                    if (currentVersion < 11)
                    {
                        if (MigrateV10ToV11(connection, migTransaction))
                        {
                            currentVersion = 11;
                        }
                        // 失敗（データ残存でスキップ）時は currentVersion = 10 のまま。
                        // SetDbVersion で実際に達成した currentVersion を書き込むため
                        // user_version は 10 のまま保持され、次回起動時に再試行される。
                    }

                    if (currentVersion < 12)
                    {
                        // v11 (surveys/play_records ドリフト修正) と v12 (backup_log への列追加) は
                        // 本来独立。v11 がデータ残存でスキップされた場合でも、v12 の純粋な
                        // ALTER TABLE ADD COLUMN は無害なので必ず実行する。
                        // (Codex P1 #127: 実行しないと InsertInProgress が "no such column:
                        //  relative_path" で常時失敗し、バックアップが取れなくなる)
                        // MigrateV11ToV12 は idempotent なので、列が既にあればスキップされる。
                        MigrateV11ToV12(connection, migTransaction);

                        // user_version の更新は v11 が完了している時だけ。v11 が未完なら
                        // currentVersion を 10 のまま据え置き、SetDbVersion(10) で書き込んで
                        // 次回起動時に v10→v11 を再試行させる（v12 ALTER は idempotent
                        // なので再度走っても無害）。
                        if (currentVersion >= 11)
                        {
                            currentVersion = 12;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] v10→v11 が未完のため user_version は 10 のまま据え置き（v12 物理変更のみ先行適用）");
                        }
                    }

                    if (currentVersion < 13)
                    {
                        // (#179) v12 → v13: manager_sessions table 新設。CREATE TABLE IF NOT EXISTS
                        // で idempotent (= CreateTables 先行 path で既に作られていれば no-op)。物理変更
                        // (= table 作成) 自体は先行実行する。
                        MigrateV12ToV13(connection, migTransaction);

                        // (round 3 H-1 fix) v10→v11 / v11→v12 と同じ guard pattern: 直前の migration が
                        // 未完なら currentVersion bump を見送り、user_version は据え置きで次回起動時に
                        // 再試行させる。MigrateV12ToV13 自体は CREATE IF NOT EXISTS で idempotent なので
                        // 物理変更が先行適用されても害なし。
                        if (currentVersion >= 12)
                        {
                            currentVersion = 13;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため user_version は " + currentVersion + " のまま据え置き (v13 物理変更のみ先行適用)");
                        }
                    }

                    if (currentVersion < 14)
                    {
                        // v13 → v14: games.arguments を正規 migration 化 (旧 CreateTables 内アドホック
                        // ALTER から移設)。TableHasColumn で idempotent (= 既に列があれば no-op)。
                        // games.arguments は他 migration と独立かつ最終スキーマ不変なので、v12/v13 と同じ
                        // guard pattern で「直前 migration 未完なら user_version 据え置き」を踏襲。
                        MigrateV13ToV14(connection, migTransaction);

                        if (currentVersion >= 13)
                        {
                            currentVersion = 14;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため user_version は " + currentVersion + " のまま据え置き (v14 物理変更のみ先行適用)");
                        }
                    }

                    if (currentVersion < 15)
                    {
                        // v14 → v15: game_versions(game_id, version) に UNIQUE INDEX を追加 (#234 ②)。
                        // 重複行が残存する場合は index を作らず false を返す。その場合 user_version を
                        // 14 のまま据え置いて次回起動時に再試行する (V10→V11 と同じ "data residual →
                        // skip + warn + retry" パターン、起動は継続)。index 作成自体は他 migration と独立。
                        bool indexOk = MigrateV14ToV15(connection, migTransaction);
                        if (currentVersion >= 14 && indexOk)
                        {
                            currentVersion = 15;
                        }
                        else if (!indexOk)
                        {
                            Logger.Warn("[DatabaseManager] v14→v15 が未完 (game_versions に重複残存) のため user_version は " + currentVersion + " のまま据え置き、次回起動時に再試行します");
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため user_version は " + currentVersion + " のまま据え置き (v15 物理変更のみ先行適用)");
                        }
                    }

                    if (currentVersion < 16)
                    {
                        // v15 → v16: backup_log.trigger_type CHECK 拡張 ('restore' 追加、H4)。
                        // 既存行は trigger_type が 'manual' / 'auto' / 'safety' のみなので新 CHECK に違反しない。
                        // V9→V10 と同じ pattern (テーブル recreate)。
                        //
                        // (累積監査 round 4 Medium-21) 旧実装は v14→v15 が skip された場合でも v15→v16 を
                        // 無条件実行していたため、backup_log を毎起動で DROP+RECREATE+INSERT-SELECT する
                        // 高コスト処理が走り続けていた (= SMB 上 + 数千行で起動遅延)。前提 step が完了
                        // (currentVersion >= 15) のときだけ走らせて idempotency を確保する。
                        if (currentVersion >= 15)
                        {
                            MigrateV15ToV16(connection, migTransaction);
                            currentVersion = 16;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] (Medium-21) v14→v15 が未完のため v15→v16 も skip、user_version は " + currentVersion + " のまま据え置き");
                        }
                    }

                    if (currentVersion < 17)
                    {
                        // v16 → v17: game_versions UNIQUE INDEX を COLLATE NOCASE 化 (M3)。
                        // v14→v15 と同じく重複残存時は skip + retry。case 違い重複も新たに弾くため
                        // 旧 BINARY INDEX では通っていた `v1.0.0` + `V1.0.0` の共存があると失敗しうる。
                        bool nocaseOk = MigrateV16ToV17(connection, migTransaction);
                        if (currentVersion >= 16 && nocaseOk)
                        {
                            currentVersion = 17;
                        }
                        else if (!nocaseOk)
                        {
                            Logger.Warn("[DatabaseManager] v16→v17 が未完 (game_versions に case 違い重複残存) のため user_version は " + currentVersion + " のまま据え置き、次回起動時に再試行します");
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため user_version は " + currentVersion + " のまま据え置き (v17 物理変更のみ先行適用)");
                        }
                    }

                    if (currentVersion < 18)
                    {
                        // v17 → v18: developers.version_id に FK + ON DELETE CASCADE を追加 (Medium-22)。
                        // SQLite は ALTER で FK 追加不能のため table recreate。orphan 行 (version_id が
                        // game_versions に存在しない) は INSERT-SELECT で除外して silent に掃除する。
                        if (currentVersion >= 17)
                        {
                            MigrateV17ToV18(connection, migTransaction);
                            currentVersion = 18;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] (Medium-22) 直前の migration が未完のため v17→v18 も skip、user_version は " + currentVersion + " のまま据え置き");
                        }
                    }

                    if (currentVersion < 19)
                    {
                        // v18 → v19: backup_log テーブルを DROP (履歴を backups/ フォルダ走査に移行)。
                        // game_genres v18 DROP と同じく、前段 migration が完了している場合のみ実行する。
                        if (currentVersion >= 18)
                        {
                            MigrateV18ToV19(connection, migTransaction);
                            currentVersion = 19;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため v18→v19 も skip、user_version は " + currentVersion + " のまま据え置き");
                        }
                    }

                    if (currentVersion < 20)
                    {
                        // v19 → v20: games.play_time に CHECK(1-3) を追加 (#247)。SQLite は ALTER で CHECK 追加
                        // 不能のため games テーブル recreate。**games は親テーブル**で game_versions / developers /
                        // play_records / surveys / store_section_games が ON DELETE CASCADE、launcher_surveys が
                        // ON DELETE SET NULL で参照しているため、`DROP TABLE games` の暗黙 DELETE が CASCADE 発火 →
                        // 子テーブル全消滅という致命的経路がある。`defer_foreign_keys` は検査を遅延するだけで CASCADE
                        // action は止めない (sqlite3 実測で確認) ため、本 migration は **foreign_keys=OFF** を前提とする
                        // (InitializeDatabase が migration 検出時に transaction 開始前へ OFF 設定 + commit 前に
                        // foreign_key_check で整合検証)。前段 migration が完了している場合のみ実行する。
                        if (currentVersion >= 19)
                        {
                            // 範囲外 play_time 残存時は false (skip + retry)、それ以外は CHECK 追加して true。
                            bool checkOk = MigrateV19ToV20(connection, migTransaction);
                            if (checkOk)
                            {
                                currentVersion = 20;
                            }
                            else
                            {
                                Logger.Warn("[DatabaseManager] v19→v20 が未完 (games.play_time に範囲外値が残存) のため user_version は " + currentVersion + " のまま据え置き、次回起動時に再試行します");
                            }
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため v19→v20 も skip、user_version は " + currentVersion + " のまま据え置き");
                        }
                    }

                    if (currentVersion < 21)
                    {
                        // v20 → v21: intro_slides table 新設 (#253、イントロガイドのスライド)。他テーブルへの FK が無い
                        // 独立テーブルなので manager_sessions (v13) と同じ単純 path、CREATE TABLE IF NOT EXISTS で
                        // idempotent。前段 migration が完了している場合のみ user_version を bump (v12→v13 等と同 guard)。
                        if (currentVersion >= 20)
                        {
                            MigrateV20ToV21(connection, migTransaction);
                            currentVersion = 21;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため v20→v21 も skip、user_version は " + currentVersion + " のまま据え置き");
                        }
                    }

                    if (currentVersion < 22)
                    {
                        // v21 → v22: intro_slides から duration_sec 削除 (#253 design 変更で自動送り廃止)。
                        // table recreate (CHECK 付き列は DROP COLUMN 不可) だが FK / 子テーブル無しで安全。前段完了時のみ bump。
                        if (currentVersion >= 21)
                        {
                            MigrateV21ToV22(connection, migTransaction);
                            currentVersion = 22;
                        }
                        else
                        {
                            Logger.Warn("[DatabaseManager] 直前の migration が未完のため v21→v22 も skip、user_version は " + currentVersion + " のまま据え置き");
                        }
                    }

                    // 達成バージョン（CurrentDbVersion ではなく currentVersion）を書き込む。
                    // 全 migration が成功していれば currentVersion == CurrentDbVersion。
                    // 部分的にスキップされた場合は、達成した最大バージョンが書き込まれる。
                    SetDbVersion(connection, currentVersion, migTransaction);

                    if (localTransaction)
                    {
                        migTransaction.Commit();
                    }

                    Logger.Info("[DatabaseManager] マイグレーションが完了しました");
                }
                catch (Exception ex)
                {
                    if (localTransaction)
                    {
                        migTransaction.Rollback();
                    }

                    Logger.Error($"[DatabaseManager] マイグレーションに失敗しました", ex);
                    throw;
                }
            }
        }

        private int GetDbVersion(SQLiteConnection connection, SQLiteTransaction transaction = null)
        {
            using (var command = new SQLiteCommand("PRAGMA user_version", connection, transaction))
            {
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result);
            }
        }

        private void SetDbVersion(SQLiteConnection connection, int version, SQLiteTransaction transaction = null)
        {
            var sql = $"PRAGMA user_version = {version}";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
            Logger.Info($"[DatabaseManager] データベースバージョンを {version} に更新しました");
        }

        private void MigrateV1ToV2(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V1 -> V2");

            string dropSurveys = "DROP TABLE IF EXISTS surveys";
            using (var command = new SQLiteCommand(dropSurveys, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string dropLauncherSurveys = "DROP TABLE IF EXISTS launcher_surveys";
            using (var command = new SQLiteCommand(dropLauncherSurveys, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createSurveysTable = @"
                CREATE TABLE IF NOT EXISTS surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createSurveysTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createLauncherSurveysTable = @"
                CREATE TABLE IF NOT EXISTS launcher_surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    favorite_game_id TEXT,
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(favorite_game_id) REFERENCES games(game_id) ON DELETE SET NULL
                )";

            using (var command = new SQLiteCommand(createLauncherSurveysTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createGameGenresTable = @"
                CREATE TABLE IF NOT EXISTS game_genres (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    genre TEXT,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";

            using (var command = new SQLiteCommand(createGameGenresTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            Logger.Info("[DatabaseManager] Migrating genres from games table to game_genres table...");
            string selectGames = "SELECT game_id, genre FROM games";
            using (var command = new SQLiteCommand(selectGames, connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string gameId = reader["game_id"].ToString();
                        string genreStr = reader["genre"] is DBNull ? "" : reader["genre"].ToString();

                        if (!string.IsNullOrEmpty(genreStr))
                        {
                            var genres = genreStr.Split(new[] { ',', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(g => g.Trim())
                                                 .Where(g => !string.IsNullOrEmpty(g) && g != ",");

                            foreach (var genre in genres)
                            {
                                string insertGenre = "INSERT INTO game_genres (game_id, genre) VALUES (@gameId, @genre)";
                                using (var insertCmd = new SQLiteCommand(insertGenre, connection, transaction))
                                {
                                    insertCmd.Parameters.AddWithValue("@gameId", gameId);
                                    insertCmd.Parameters.AddWithValue("@genre", genre);
                                    insertCmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
            }

            bool hasSupportedConnection = false;
            using (var command = new SQLiteCommand("PRAGMA table_info(games)", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader["name"].ToString() == "supported_connection")
                        {
                            hasSupportedConnection = true;
                            break;
                        }
                    }
                }
            }

            if (!hasSupportedConnection)
            {
                string addColumn = "ALTER TABLE games ADD COLUMN supported_connection INTEGER DEFAULT 0";
                using (var command = new SQLiteCommand(addColumn, connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void MigrateV2ToV3(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V2 -> V3");

            string[] newColumns = {
                "title TEXT", "genre TEXT",
                "min_players INTEGER", "max_players INTEGER",
                "difficulty INTEGER", "play_time INTEGER",
                "controller_support INTEGER DEFAULT 0", "supported_connection INTEGER DEFAULT 0",
                "thumbnail_path TEXT", "background_path TEXT"
            };

            foreach (var col in newColumns)
            {
                try {
                    using (var command = new SQLiteCommand($"ALTER TABLE game_versions ADD COLUMN {col}", connection, transaction))
                    {
                        command.ExecuteNonQuery();
                    }
                } catch (Exception ex) {
                    Logger.Warn($"[DatabaseManager] Warning adding column to game_versions: {ex.Message}");
                }
            }

            try {
                using (var command = new SQLiteCommand("ALTER TABLE developers ADD COLUMN version_id INTEGER", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            } catch (Exception ex) {
                Logger.Warn($"[DatabaseManager] Warning adding version_id to developers: {ex.Message}");
            }

            var versionsToUpdate = new List<dynamic>();
            using (var command = new SQLiteCommand("SELECT id, game_id FROM game_versions", connection, transaction))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        versionsToUpdate.Add(new { Id = Convert.ToInt32(reader["id"]), GameId = reader["game_id"].ToString() });
                    }
                }
            }

            foreach (var v in versionsToUpdate)
            {
                string getGameSql = "SELECT * FROM games WHERE game_id = @gameId";
                using (var cmd = new SQLiteCommand(getGameSql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@gameId", v.GameId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string updateSql = @"
                                UPDATE game_versions SET
                                    title = @title, genre = @genre,
                                    min_players = @minPlayers, max_players = @maxPlayers,
                                    difficulty = @difficulty, play_time = @playTime,
                                    controller_support = @controllerSupport, supported_connection = @supportedConnection,
                                    thumbnail_path = @thumbnailPath, background_path = @backgroundPath
                                WHERE id = @id";

                            using (var updateCmd = new SQLiteCommand(updateSql, connection, transaction))
                            {
                                updateCmd.Parameters.AddWithValue("@title", reader["title"]);
                                updateCmd.Parameters.AddWithValue("@genre", reader["genre"]);
                                updateCmd.Parameters.AddWithValue("@minPlayers", reader["min_players"]);
                                updateCmd.Parameters.AddWithValue("@maxPlayers", reader["max_players"]);
                                updateCmd.Parameters.AddWithValue("@difficulty", reader["difficulty"]);
                                updateCmd.Parameters.AddWithValue("@playTime", reader["play_time"]);
                                updateCmd.Parameters.AddWithValue("@controllerSupport", reader["controller_support"]);
                                updateCmd.Parameters.AddWithValue("@supportedConnection", reader["supported_connection"]);
                                updateCmd.Parameters.AddWithValue("@thumbnailPath", reader["thumbnail_path"]);
                                updateCmd.Parameters.AddWithValue("@backgroundPath", reader["background_path"]);
                                updateCmd.Parameters.AddWithValue("@id", v.Id);
                                updateCmd.ExecuteNonQuery();
                            }

                            CopyDevelopersToVersion(connection, transaction, v.GameId, v.Id);
                        }
                    }
                }
            }

            Logger.Info("[DatabaseManager] Migration V2 -> V3 completed.");
        }

        private void MigrateV3ToV4(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V3 -> V4 (Fixing missing versions)");

            var gamesWithoutVersions = new List<string>();
            string findOrphanedGames = @"
                SELECT g.game_id
                FROM games g
                LEFT JOIN game_versions v ON g.game_id = v.game_id
                WHERE v.id IS NULL";

            using (var command = new SQLiteCommand(findOrphanedGames, connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    gamesWithoutVersions.Add(reader["game_id"].ToString());
                }
            }

            if (gamesWithoutVersions.Count == 0)
            {
                Logger.Warn("[DatabaseManager] No orphaned games found. Skipping fix.");
                return;
            }

            Logger.Info($"[DatabaseManager] Found {gamesWithoutVersions.Count} games without versions. Creating default 1.0.0 versions...");

            foreach (string gameId in gamesWithoutVersions)
            {
                string createVersionSql = @"
                    INSERT INTO game_versions (
                        game_id, version, executable_path,
                        title, genre, min_players, max_players,
                        difficulty, play_time, controller_support, supported_connection,
                        thumbnail_path, background_path, registered_at, description
                    )
                    SELECT
                        game_id, '1.0.0', executable_path,
                        title, genre, min_players, max_players,
                        difficulty, play_time, controller_support, supported_connection,
                        thumbnail_path, background_path, CURRENT_TIMESTAMP, NULL
                    FROM games
                    WHERE game_id = @gameId";

                long newVersionId;
                using (var cmd = new SQLiteCommand(createVersionSql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@gameId", gameId);
                    cmd.ExecuteNonQuery();
                    newVersionId = connection.LastInsertRowId;
                }

                CopyDevelopersToVersion(connection, transaction, gameId, (int)newVersionId);
            }
        }

        private void MigrateV4ToV5(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V4 -> V5 (Clearing description for v1.0.0)");

            string clearDescriptionSql = @"
                UPDATE game_versions
                SET description = NULL
                WHERE version = '1.0.0'";

            using (var command = new SQLiteCommand(clearDescriptionSql, connection, transaction))
            {
                int rows = command.ExecuteNonQuery();
                Logger.Info($"[DatabaseManager] Cleared description for {rows} version records (v1.0.0).");
            }
        }

        private void MigrateV5ToV6(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V5 -> V6 (Adding update_note column)");
            string sql = "ALTER TABLE game_versions ADD COLUMN update_note TEXT";
            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private void MigrateV6ToV7(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V6 -> V7 (Adding store_sections and store_section_games tables)");

            string createStoreSectionsTable = @"
                CREATE TABLE IF NOT EXISTS store_sections (
                    section_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    section_type INTEGER DEFAULT 0,
                    section_source TEXT DEFAULT 'manual',
                    display_order INTEGER DEFAULT 0,
                    max_display_count INTEGER DEFAULT 5,
                    is_visible INTEGER DEFAULT 1
                )";

            using (var command = new SQLiteCommand(createStoreSectionsTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }

            string createStoreSectionGamesTable = @"
                CREATE TABLE IF NOT EXISTS store_section_games (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    section_id INTEGER NOT NULL,
                    game_id TEXT NOT NULL,
                    display_order INTEGER DEFAULT 0,
                    FOREIGN KEY(section_id) REFERENCES store_sections(section_id) ON DELETE CASCADE,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE,
                    UNIQUE(section_id, game_id)
                )";

            using (var command = new SQLiteCommand(createStoreSectionGamesTable, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        private void MigrateV7ToV8(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V7 -> V8 (Adding display_text to store_section_games)");

            try
            {
                string sql = "ALTER TABLE store_section_games ADD COLUMN display_text TEXT DEFAULT ''";
                using (var command = new SQLiteCommand(sql, connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                // カラムが既に存在する場合はスキップ
                Logger.Warn($"[DatabaseManager] Warning adding display_text: {ex.Message}");
            }
        }

        private void MigrateV8ToV9(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V8 -> V9 (Adding backup_log table and backup-related settings)");

            // backup_log テーブル作成（CreateTables 側でも IF NOT EXISTS で作成されるが、明示的に呼ぶ）
            CreateBackupLogTable(connection, transaction);

            // バックアップ関連の設定デフォルトを投入
            InsertBackupDefaults(connection, transaction);

            Logger.Info("[DatabaseManager] Migration V8 -> V9 completed.");
        }

        private void MigrateV9ToV10(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V9 -> V10 (Extending backup_log.trigger_type CHECK to allow 'safety')");

            // SQLite の CHECK 制約は ALTER TABLE で変更できないため、テーブルを作り直す。
            // 既存行は trigger_type が 'manual' / 'auto' のみなので新CHECKに違反しない。
            string createNew = @"
                CREATE TABLE backup_log_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at INTEGER NOT NULL,
                    completed_at INTEGER,
                    pc_name TEXT NOT NULL,
                    file_path TEXT,
                    file_size_bytes INTEGER,
                    status TEXT NOT NULL CHECK (status IN ('in_progress','success','failed')),
                    error_message TEXT,
                    trigger_type TEXT NOT NULL CHECK (trigger_type IN ('manual','auto','safety'))
                )";
            using (var cmd = new SQLiteCommand(createNew, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // データを丸ごとコピー（id を維持するため列を明示）
            using (var cmd = new SQLiteCommand(
                "INSERT INTO backup_log_new (id, started_at, completed_at, pc_name, file_path, " +
                "file_size_bytes, status, error_message, trigger_type) " +
                "SELECT id, started_at, completed_at, pc_name, file_path, " +
                "file_size_bytes, status, error_message, trigger_type FROM backup_log",
                connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand("DROP TABLE backup_log", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "ALTER TABLE backup_log_new RENAME TO backup_log", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            Logger.Info("[DatabaseManager] Migration V9 -> V10 completed.");
        }

        /// <summary>
        /// V10 -> V11: surveys / play_records スキーマ drift 修正
        /// SPEC v1.5.1 (2026-03-28) で surveys（JSON 形式 → ★評価+コメント）、
        /// play_records（累計方式 → イベントログ方式）に変更されたが、対応する
        /// マイグレーションが書かれていなかったため、CREATE TABLE IF NOT EXISTS の
        /// 仕様により旧スキーマのテーブルが温存されていた。本マイグレーションで修正。
        ///
        /// データがあるテーブルは破壊しないため、空テーブルのみ DROP & CREATE する。
        /// データがある場合は警告ログを出して false を返し、`user_version` を 10 のまま
        /// 保持することで次回起動時に再試行する（Manager 自体は正常起動を継続）。
        /// </summary>
        /// <returns>全 drift fix が成功（または不要）なら true、データ残存でスキップしたら false</returns>
        private bool MigrateV10ToV11(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Warn("[DatabaseManager] Executing migration V10 -> V11 (Fix surveys/play_records schema drift from SPEC v1.5.1)");

            bool surveysOk = FixSurveysSchemaDrift(connection, transaction);
            bool playRecordsOk = FixPlayRecordsSchemaDrift(connection, transaction);

            bool allOk = surveysOk && playRecordsOk;
            if (allOk)
            {
                Logger.Info("[DatabaseManager] Migration V10 -> V11 completed.");
            }
            else
            {
                Logger.Warn("[DatabaseManager] WARNING: Migration V10 -> V11 partially skipped (data exists in legacy-schema tables). user_version stays at 10. Next startup will retry.");
            }
            return allOk;
        }

        /// <summary>
        /// surveys テーブルが旧 JSON 形式スキーマ（submitted_at / responses 列を持つ）の場合、
        /// 新スキーマ（rating / comment / created_at）へ修正する。
        /// データが残存している場合は警告ログを出して false を返す（Manager 起動は継続）。
        /// （Codex P1 指摘 "Avoid marking DB v11 when drift migration is skipped" +
        /// "Avoid hard-failing startup on non-empty drift tables" 対応）
        /// </summary>
        /// <returns>新スキーマ／drift fix 成功なら true、データ残存でスキップなら false</returns>
        private bool FixSurveysSchemaDrift(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 旧スキーマ判定: 'submitted_at' 列が存在するか
            bool isOldSchema = TableHasColumn(connection, transaction, "surveys", "submitted_at");

            if (!isOldSchema)
            {
                Logger.Info("[DatabaseManager] surveys は新スキーマです。マイグレーション不要。");
                return true;
            }

            long rowCount = GetTableRowCount(connection, transaction, "surveys");
            Logger.Warn($"[DatabaseManager] surveys に旧スキーマを検出 (行数: {rowCount})");

            if (rowCount > 0)
            {
                // 旧 JSON 形式 → 新 ★評価+コメント形式の自動変換は responses JSON のスキーマが
                // 不定でリスクが高いため未実装。Manager 起動は継続させ、警告ログで手動対応を促す。
                // 呼び出し側の MigrateV10ToV11 が false を伝播し、user_version は 10 のまま維持される。
                // 次回起動時にこの migration が再試行されるため、手動でデータを退避すれば次回 fix される。
                Logger.Warn(
                    $"[DatabaseManager] WARNING: surveys に {rowCount} 行のデータが残存。自動マイグレーションをスキップします。" +
                    "tools/sqlite3/sqlite3.exe で旧データ（responses 列の JSON）を確認・退避してから手動で新スキーマへ移行してください。");
                return false;
            }

            using (var cmd = new SQLiteCommand("DROP TABLE surveys", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            CreateSurveysTable(connection, transaction);
            Logger.Info("[DatabaseManager] surveys を新スキーマで再作成しました。");
            return true;
        }

        /// <summary>
        /// play_records テーブルが旧累計方式スキーマ（play_count / total_play_time 列を持つ）の場合、
        /// 新イベントログ方式スキーマ（start_time / end_time / play_duration / player_count）へ修正する。
        /// データが残存している場合は警告ログを出して false を返す（Manager 起動は継続）。
        /// （Codex P1 指摘 "Avoid marking DB v11 when drift migration is skipped" +
        /// "Avoid hard-failing startup on non-empty drift tables" 対応）
        /// </summary>
        /// <returns>新スキーマ／drift fix 成功なら true、データ残存でスキップなら false</returns>
        private bool FixPlayRecordsSchemaDrift(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 旧スキーマ判定: 'play_count' 列が存在するか
            bool isOldSchema = TableHasColumn(connection, transaction, "play_records", "play_count");

            if (!isOldSchema)
            {
                Logger.Info("[DatabaseManager] play_records は新スキーマです。マイグレーション不要。");
                return true;
            }

            long rowCount = GetTableRowCount(connection, transaction, "play_records");
            Logger.Warn($"[DatabaseManager] play_records に旧スキーマを検出 (行数: {rowCount})");

            if (rowCount > 0)
            {
                // 旧累計方式 → 新イベントログ方式の自動変換は元情報が失われているため不可能。
                // Manager 起動は継続させ、警告ログで手動対応を促す。
                // 呼び出し側の MigrateV10ToV11 が false を伝播し、user_version は 10 のまま維持される。
                Logger.Warn(
                    $"[DatabaseManager] WARNING: play_records に {rowCount} 行のデータが残存。自動マイグレーションをスキップします。" +
                    "tools/sqlite3/sqlite3.exe で累計値（play_count / total_play_time）を退避してから手動で新スキーマへ移行してください。");
                return false;
            }

            using (var cmd = new SQLiteCommand("DROP TABLE play_records", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            CreatePlayRecordsTable(connection, transaction);
            Logger.Info("[DatabaseManager] play_records を新スキーマで再作成しました。");
            return true;
        }

        /// <summary>
        /// v11 → v12: backup_log テーブルに relative_path 列を追加 (#126)
        /// プロジェクト場所の移動に追従できるよう、toneprism.db からの相対パスを記録する。
        /// 既存レコードの relative_path は NULL のまま（呼び出し側で file_path にフォールバック）。
        /// </summary>
        private void MigrateV11ToV12(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // backup_log に relative_path 列が既に存在する場合はスキップ（手動先行追加対応）
            bool alreadyExists = false;
            using (var cmd = new SQLiteCommand("PRAGMA table_info(backup_log)", connection, transaction))
            {
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string colName = reader["name"].ToString();
                        if (colName == "relative_path")
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                }
            }

            if (alreadyExists)
            {
                Logger.Warn("[DatabaseManager] backup_log.relative_path は既に存在 → MigrateV11ToV12 をスキップ");
                return;
            }

            using (var cmd = new SQLiteCommand(
                "ALTER TABLE backup_log ADD COLUMN relative_path TEXT", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            Logger.Info("[DatabaseManager] backup_log に relative_path 列を追加しました (v11 → v12)");
        }

        /// <summary>
        /// (#179) v12 → v13: manager_sessions テーブル新設。
        /// Manager の LAN-wide 同時起動検出 + 競合 risk 操作前 dialog のための SoT。
        /// 各 PC で稼働中の Manager process が self row を heartbeat update、起動時の stale cleanup +
        /// 他 PC row 検出に使う。SPEC §3.8 / §7.3 参照。
        /// CreateTables で既に作成済みの場合 (= 新規 DB を v13 で作る場合) は CREATE TABLE IF NOT EXISTS
        /// が黙って skip するため idempotent。
        /// </summary>
        private void MigrateV12ToV13(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // CREATE TABLE IF NOT EXISTS で idempotent。table 既存時 (= dev test で手動 INSERT 済 / 部分
            // migration 後の再実行) も silent skip。log は「migration 完了」状態表現で「作成しました」と
            // 誤読されない表記 (round 2 Info-2)。
            CreateManagerSessionsTable(connection, transaction);
            Logger.Info("[DatabaseManager] v12 → v13 migration 完了 (manager_sessions table 確保)");
        }

        /// <summary>
        /// v13 → v14: games.arguments 列を追加。以前は CreateTables() 内のアドホック ALTER
        /// (user_version 非連動・毎起動の存在チェック・失敗握り潰し) だったものを version chain に
        /// 移設し、AGENTS.md「CreateTables() を編集したら必ず MigrateVxToVy」規約に整合させたもの。
        /// 新規 DB は CreateTables の CREATE TABLE で既に arguments を持つため、本 migration は
        /// arguments 列を持たない旧 DB の retrofit 専用。TableHasColumn で idempotent。
        /// 失敗時は例外を伝播させ、呼び出し元 (CheckAndMigrateDatabase / InitializeDatabase) の
        /// トランザクションが rollback される (旧実装の silent な握り潰しを廃止)。
        /// </summary>
        private void MigrateV13ToV14(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            if (!TableHasColumn(connection, transaction, "games", "arguments"))
            {
                Logger.Info("[DatabaseManager] v13 → v14: games.arguments 列を追加します");
                using (var command = new SQLiteCommand("ALTER TABLE games ADD COLUMN arguments TEXT", connection, transaction))
                {
                    command.ExecuteNonQuery();
                }
                Logger.Info("[DatabaseManager] v13 → v14 migration 完了 (games.arguments 追加)");
            }
            else
            {
                Logger.Info("[DatabaseManager] v13 → v14: games.arguments は既に存在 (skip)");
            }
        }

        /// <summary>
        /// v14 → v15: game_versions(game_id, version) に UNIQUE INDEX を追加 (#234 ②)。
        /// 同一ゲームに同一バージョン番号が 2 行 INSERT される silent corruption をアプリ層 dup-check
        /// (VersionUpForm / EditGameForm / GameSectionPanel) の最後の砦として DB レベルで封じる。
        /// 複数 PC 同時操作時の「check → write」間 race のように app-level guard で塞ぎきれない経路を
        /// DB 制約で確実に弾く。重複行が残存する場合は throw せず false を返し、user_version 据え置きで
        /// 次回起動時に再試行する (V10→V11 と同じパターン、起動は継続)。EnsureGameVersionsVersionUniqueIndex
        /// が CreateTables (新規 DB) と共通の実体。
        /// </summary>
        /// <returns>index 作成成功 / 既存なら true、重複残存で skip なら false</returns>
        private bool MigrateV14ToV15(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V14 -> V15 (game_versions に (game_id, version) UNIQUE INDEX を追加, #234 ②)");
            return EnsureGameVersionsVersionUniqueIndex(connection, transaction);
        }

        /// <summary>
        /// v15 → v16: backup_log.trigger_type CHECK を 'restore' 受け入れに拡張 (H4)。
        /// V9 → V10 と同じ pattern: SQLite の CHECK は ALTER で変更不能のため、新スキーマで table を recreate
        /// + 全行 INSERT コピー + DROP/RENAME。既存行は 'manual' / 'auto' / 'safety' のみで新 CHECK に違反しない。
        /// v11 で追加された relative_path 列も保持する (V9 → V10 の列セットからの drift に注意)。
        /// </summary>
        /// <summary>
        /// (累積監査 round 4 Medium-22) v17 → v18: developers.version_id に FK + ON DELETE CASCADE を追加。
        /// SQLite は ALTER で FK 追加不能のため、テーブル recreate + INSERT-SELECT で対応する。
        /// orphan 行 (version_id が non-null だが game_versions に該当 id が無い) は INSERT-SELECT で除外することで
        /// silent に掃除する (現状 single-version 削除コードが無いので通常 orphan は発生していないはずだが、
        /// 過去 migration の残党 / 外部ツール直 DML での garbage を念のため除去する defensive sweep)。
        /// </summary>
        /// <summary>
        /// (累積監査 round 6 #13) developers テーブルが version_id への FOREIGN KEY を持つか判定する。
        /// v0 DB の retrofit 要否判定に使う。`PRAGMA foreign_key_list(developers)` の各行の `from` 列が
        /// "version_id" のものがあれば true。検出に失敗した場合は安全側 (= retrofit を走らせない) に倒して
        /// true を返す (= 余計な table recreate を避ける。新規 DB では CreateTables が FK 付きで作るため、
        /// 検出失敗で skip しても実害なし)。
        /// </summary>
        private bool DevelopersHasVersionIdForeignKey(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            try
            {
                using (var cmd = new SQLiteCommand("PRAGMA foreign_key_list(developers)", connection, transaction))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string from = reader["from"] is DBNull ? null : reader["from"].ToString();
                        if (string.Equals(from, "version_id", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("[DatabaseManager] (#13) developers FK 検出に失敗、retrofit を skip (安全側): " + ex.Message);
                return true;
            }
            return false;
        }

        private void MigrateV17ToV18(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V17 -> V18 (developers.version_id に FK + ON DELETE CASCADE 追加 / game_genres dead table 除去, Medium-22 / Low-28/29)");

            // (round 5 M1) SQLite 公式の「FK ありテーブル recreate」推奨手順に従い、transaction 内では
            // foreign_keys check を deferred モードに切替する。`PRAGMA foreign_keys` は transaction 内で
            // 変更できないため `defer_foreign_keys` を使う (3.7.5+)。これで INSERT-SELECT 中に新テーブルの
            // FK 違反が即時 throw されず、COMMIT 直前にまとめて check される。これにより game_id orphan が
            // 1 件でも残っている過去 DB (= 外部ツール直 DML 経験あり / round 4 以前の UpdateGameId 中断履歴)
            // で migration 全体が即死する path を回避する。
            using (var cmd = new SQLiteCommand("PRAGMA defer_foreign_keys = ON", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // (累積監査 round 4 Low-28/29) game_genres は v2 で追加されたが GameRepository.Add/Update は
            // 一切書き込まず `games.genre` のカンマ区切り文字列が SoT として動いている dead table。
            // UpdateGameId だけが child table list に含めて更新しているため過去 v2 migration 経由の DB では
            // 「rename 時だけ古い行が追従」する半端な状態が残っていた。本 migration で DROP して
            // SoT を 1 本化する (CreateTables / ExpectedSchema からも同時に除去済)。
            using (var cmd = new SQLiteCommand("DROP TABLE IF EXISTS game_genres", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            string createNew = @"
                CREATE TABLE developers_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    last_name TEXT,
                    first_name TEXT,
                    grade TEXT,
                    version_id INTEGER,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE,
                    FOREIGN KEY(version_id) REFERENCES game_versions(id) ON DELETE CASCADE
                )";
            using (var cmd = new SQLiteCommand(createNew, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // (round 5 M1) orphan filter を拡張: 旧実装は version_id orphan のみ除外で、game_id orphan
            // (= games に存在しない game_id を持つ developers 行) は除外していなかった。新テーブルの
            // game_id 側にも FK が付くため orphan 残存で COMMIT 時 FK check 失敗 → migration 全体即死の
            // 経路があった。version_id と同パターンで game_id 側にも EXISTS filter を追加し、
            // 「game_id IS NULL (= 製作者が特定ゲーム未紐付け) または対応 games 行が存在」かつ
            // 「version_id IS NULL または対応 game_versions 行が存在」の両条件を満たす行のみ移行する。
            using (var cmd = new SQLiteCommand(
                "INSERT INTO developers_new (id, game_id, last_name, first_name, grade, version_id) " +
                "SELECT d.id, d.game_id, d.last_name, d.first_name, d.grade, d.version_id " +
                "FROM developers d " +
                "WHERE (d.game_id IS NULL " +
                "       OR EXISTS (SELECT 1 FROM games g WHERE g.game_id = d.game_id)) " +
                "  AND (d.version_id IS NULL " +
                "       OR EXISTS (SELECT 1 FROM game_versions gv WHERE gv.id = d.version_id))",
                connection, transaction))
            {
                int copied = cmd.ExecuteNonQuery();
                Logger.Info("[DatabaseManager] (round 5 M1 / Medium-22) developers 行を新テーブルへコピー: " + copied + " 件");
            }

            // orphan 件数を別途 log に残す (= silent sweep の trail)。version_id / game_id を別カウントで記録。
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM developers WHERE version_id IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM game_versions gv WHERE gv.id = developers.version_id)",
                connection, transaction))
            {
                var r = cmd.ExecuteScalar();
                long orphans = r is DBNull ? 0 : Convert.ToInt64(r);
                if (orphans > 0)
                {
                    Logger.Warn("[DatabaseManager] (Medium-22) developers.version_id 孤児行を migration で除去: " + orphans + " 件");
                }
            }
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM developers WHERE game_id IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM games g WHERE g.game_id = developers.game_id)",
                connection, transaction))
            {
                var r = cmd.ExecuteScalar();
                long orphans = r is DBNull ? 0 : Convert.ToInt64(r);
                if (orphans > 0)
                {
                    Logger.Warn("[DatabaseManager] (round 5 M1) developers.game_id 孤児行を migration で除去: " + orphans + " 件");
                }
            }

            using (var cmd = new SQLiteCommand("DROP TABLE developers", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SQLiteCommand("ALTER TABLE developers_new RENAME TO developers", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            Logger.Info("[DatabaseManager] Migration V17 -> V18 completed.");
        }

        /// <summary>
        /// v18 → v19: backup_log テーブルを DROP する。
        ///
        /// バックアップ履歴のメタデータを「バックアップ対象である toneprism.db の中」に持つ設計が、復元で DB が
        /// 丸ごと置き換わるたびに履歴とディスク実ファイルのズレを生み、それを埋める reconcile / register 系の
        /// 後付けコードがバグの温床になっていた (失敗復元が success 化する等)。履歴を backups/ フォルダ走査
        /// (BackupCatalogService) 由来に切り替えたことで本テーブルは不要になった。
        ///
        /// 既存行は破棄されるが、物理バックアップファイルは残り、初回走査で履歴に復活する (失われるのは
        /// 失敗履歴と復元監査行のみ = いずれも要件上不要)。LAN 協調 (last_backup_at lease / restore_lock_owner)
        /// は settings テーブルにあり本 migration の影響を受けない。game_genres v18 DROP と同じパターン。
        /// </summary>
        private void MigrateV18ToV19(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V18 -> V19 (backup_log テーブルを DROP, 履歴を file-scan 化)");
            using (var cmd = new SQLiteCommand("DROP TABLE IF EXISTS backup_log", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            Logger.Info("[DatabaseManager] Migration V18 -> V19 completed.");
        }

        /// <summary>
        /// v19 → v20: `games.play_time` に `CHECK(play_time BETWEEN 1 AND 3)` を追加する (#247)。
        ///
        /// 背景: 初代スキーマ / SPEC §7.3 は play_time に CHECK(1-3) を持っていたが、現行 `games` テーブル定義は
        /// difficulty の CHECK は残しつつ play_time の CHECK を取りこぼしていた (新規DB/旧DB/SPEC の三者 drift)。
        /// 値は Manager のコンボボックスで 1-3 に強制済みで実害は無いが、difficulty との非対称解消 + SPEC 整合の
        /// ため DB レベルでも強制する。
        ///
        /// **重要 (FK)**: SQLite は ALTER TABLE で CHECK を追加できないため games テーブルを recreate するが、
        /// games は親テーブルで複数の子が ON DELETE CASCADE / SET NULL で参照している。`DROP TABLE games` は
        /// foreign_keys=ON だと暗黙 DELETE → CASCADE 発火で子行を全消去するため、本 migration は
        /// **foreign_keys=OFF を前提**とする (InitializeDatabase が migration 検出時に transaction 開始前へ
        /// PRAGMA foreign_keys=OFF を設定し、commit 前に PRAGMA foreign_key_check で整合を検証する)。
        /// `defer_foreign_keys` では CASCADE action を止められない (sqlite3 実測で確認済) ため不可。
        ///
        /// 冪等性: 既に play_time CHECK を持つ games (= 新規DB が CreateTables で作った形 / 再実行) では skip する。
        /// 列順・列名は CreateTables の games 定義と一致させること (drift すると INSERT-SELECT が壊れる)。
        ///
        /// データ起因の skip: 範囲外 play_time (NULL でも 1-3 でもない値) を持つ既存行があると新 CHECK で
        /// INSERT-SELECT が失敗する。UI は 1-3 しか書かないため通常発生しないが、外部ツール直 DML 等で混入した
        /// 場合に **migration を hard-fail させて Manager 起動を止めない** よう (展示運用での可用性優先)、事前検出して
        /// skip + 警告 + false 返却にする (V14→V15 / V16→V17 の重複時 skip+retry と同パターン)。user_version は
        /// 19 のまま据え置かれ、該当行を sqlite3 で是正すれば次回起動で適用される。
        /// </summary>
        /// <returns>CHECK 追加成功 / 既存(冪等) なら true、範囲外データ残存で skip なら false (user_version 据え置き)</returns>
        private bool MigrateV19ToV20(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V19 -> V20 (games.play_time に CHECK(1-3) を追加, #247)");

            // 冪等ガード: games の DDL に play_time の CHECK が既にあれば何もしない (新規DB / 再実行)。
            string gamesSql = null;
            using (var cmd = new SQLiteCommand(
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='games'", connection, transaction))
            {
                var r = cmd.ExecuteScalar();
                gamesSql = r is DBNull ? null : r?.ToString();
            }
            if (gamesSql != null && gamesSql.IndexOf("play_time", StringComparison.OrdinalIgnoreCase) >= 0
                && gamesSql.IndexOf("play_time INTEGER CHECK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Info("[DatabaseManager] games.play_time に CHECK が既存のため V19 -> V20 を skip (冪等)");
                return true;
            }

            // データ起因 skip 判定: 範囲外 play_time / difficulty を持つ行を事前検出 (あれば INSERT-SELECT が
            // CHECK 違反で失敗するため、hard-fail で起動を止めず skip + 警告 + retry に倒す)。NULL は CHECK 許容なので除外。
            // (PR #236 レビュー対応 #3) games_new は play_time / difficulty の **両方** に CHECK を強制するため、
            // 事前検査も両方を見る。play_time は本 migration まで CHECK が無く範囲外データが既存しうる一方、
            // difficulty は CreateTables で長く CHECK 済のため範囲外は通常存在しないが、CHECK 不在の旧 v0 DB 等で
            // 範囲外 difficulty があると INSERT-SELECT が throw → 起動失敗 (= play_time の skip+warn と真逆) になる
            // 非対称を防ぐため difficulty も同じ skip+warn 経路に乗せる。
            long outOfRange = 0;
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM games WHERE (play_time IS NOT NULL AND play_time NOT BETWEEN 1 AND 3) " +
                "OR (difficulty IS NOT NULL AND difficulty NOT BETWEEN 1 AND 3)",
                connection, transaction))
            {
                var r = cmd.ExecuteScalar();
                outOfRange = r is DBNull ? 0 : Convert.ToInt64(r);
            }
            if (outOfRange > 0)
            {
                Logger.Warn(
                    "[DatabaseManager] WARNING: games.play_time / difficulty に範囲外 (1-3 以外) の値を持つ行が " + outOfRange + " 件あるため " +
                    "V19 -> V20 (CHECK 追加) を skip します (user_version 据え置き、次回起動時に再試行)。" +
                    "tools/sqlite3/sqlite3.exe で `SELECT game_id, play_time, difficulty FROM games WHERE (play_time IS NOT NULL AND play_time NOT BETWEEN 1 AND 3) OR (difficulty IS NOT NULL AND difficulty NOT BETWEEN 1 AND 3);` " +
                    "を確認し、値を 1-3 または NULL に是正してから Manager を再起動してください。");
                return false;
            }

            // 前回中断の残骸があれば掃除 (transaction 内なので通常は発生しないが防御)。
            using (var cmd = new SQLiteCommand("DROP TABLE IF EXISTS games_new", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // 新 games を CreateTables と同一の列構成 + play_time CHECK 付きで作成。
            string createNew = @"
                CREATE TABLE games_new (
                    game_id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    description TEXT,
                    release_year INTEGER,
                    genre TEXT,
                    min_players INTEGER,
                    max_players INTEGER,
                    difficulty INTEGER CHECK(difficulty BETWEEN 1 AND 3),
                    play_time INTEGER CHECK(play_time BETWEEN 1 AND 3),
                    controller_support INTEGER DEFAULT 0,
                    supported_connection INTEGER DEFAULT 0,
                    thumbnail_path TEXT,
                    background_path TEXT,
                    executable_path TEXT,
                    display_order INTEGER DEFAULT 0,
                    is_visible INTEGER DEFAULT 1,
                    controls TEXT,
                    key_mapping TEXT,
                    arguments TEXT,
                    version TEXT
                )";
            using (var cmd = new SQLiteCommand(createNew, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // 全列を明示列挙してコピー (SELECT * 依存を避ける)。play_time が範囲外の既存行があると CHECK 違反で
            // ここで throw → migration 全体 rollback。UI は 1-3 しか書かないため通常は発生しないが、外部ツール
            // 直 INSERT 等で範囲外値があれば fail-fast させ (silent な値書き換えはしない)、手動是正を促す。
            using (var cmd = new SQLiteCommand(
                "INSERT INTO games_new (game_id, title, description, release_year, genre, min_players, max_players, " +
                "difficulty, play_time, controller_support, supported_connection, thumbnail_path, background_path, " +
                "executable_path, display_order, is_visible, controls, key_mapping, arguments, version) " +
                "SELECT game_id, title, description, release_year, genre, min_players, max_players, " +
                "difficulty, play_time, controller_support, supported_connection, thumbnail_path, background_path, " +
                "executable_path, display_order, is_visible, controls, key_mapping, arguments, version FROM games",
                connection, transaction))
            {
                int copied = cmd.ExecuteNonQuery();
                Logger.Info("[DatabaseManager] (#247) games 行を新テーブルへコピー: " + copied + " 件");
            }

            using (var cmd = new SQLiteCommand("DROP TABLE games", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SQLiteCommand("ALTER TABLE games_new RENAME TO games", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            // games には PRIMARY KEY 以外の secondary index / trigger は無い (UNIQUE INDEX は game_versions 側)
            // ため再作成は不要。子テーブルの FK 定義はテーブル名 'games' で解決されるため rename 後も有効
            // (foreign_key_check は InitializeDatabase が commit 前に実行して整合を検証する)。
            Logger.Info("[DatabaseManager] Migration V19 -> V20 completed.");
            return true;
        }

        /// <summary>
        /// (#247) `PRAGMA foreign_key_check` で FK 整合を検証する。foreign_keys=OFF にして親テーブルを recreate
        /// した migration (v19→v20) の commit 直前に呼び、CASCADE/SET NULL 参照が rename 後も健全かを確認する。
        /// 違反行があっても throw せず warn ログのみ (起動継続優先、VerifySchema と同方針)。foreign_key_check は
        /// 全テーブルを scan して (子テーブル, rowid, 親テーブル, fkid) を返す。検証は enforcement 設定 (ON/OFF) に
        /// 依存せず常に scan する。
        /// </summary>
        private void VerifyForeignKeyIntegrity(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            var violations = new List<string>();
            using (var cmd = new SQLiteCommand("PRAGMA foreign_key_check", connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string tbl = reader.IsDBNull(0) ? "?" : reader.GetValue(0).ToString();
                    string rowid = reader.IsDBNull(1) ? "?" : reader.GetValue(1).ToString();
                    string parent = reader.IsDBNull(2) ? "?" : reader.GetValue(2).ToString();
                    violations.Add("  - table=" + tbl + " rowid=" + rowid + " parent=" + parent);
                    if (violations.Count >= 50) break; // ログ肥大防止 (通常は 0 件)
                }
            }
            if (violations.Count > 0)
            {
                Logger.Warn("[DatabaseManager] (#247) foreign_key_check で FK 不整合を検出 (migration 後)。起動は継続しますが手動確認を推奨:\n" + string.Join("\n", violations));
            }
            else
            {
                Logger.Info("[DatabaseManager] (#247) foreign_key_check: FK 整合 OK");
            }
        }

        private void MigrateV15ToV16(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V15 -> V16 (backup_log.trigger_type CHECK 拡張: 'restore' 追加, H4)");

            string createNew = @"
                CREATE TABLE backup_log_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at INTEGER NOT NULL,
                    completed_at INTEGER,
                    pc_name TEXT NOT NULL,
                    file_path TEXT,
                    relative_path TEXT,
                    file_size_bytes INTEGER,
                    status TEXT NOT NULL CHECK (status IN ('in_progress','success','failed')),
                    error_message TEXT,
                    trigger_type TEXT NOT NULL CHECK (trigger_type IN ('manual','auto','safety','restore'))
                )";
            using (var cmd = new SQLiteCommand(createNew, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "INSERT INTO backup_log_new (id, started_at, completed_at, pc_name, file_path, relative_path, " +
                "file_size_bytes, status, error_message, trigger_type) " +
                "SELECT id, started_at, completed_at, pc_name, file_path, relative_path, " +
                "file_size_bytes, status, error_message, trigger_type FROM backup_log",
                connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand("DROP TABLE backup_log", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SQLiteCommand(
                "ALTER TABLE backup_log_new RENAME TO backup_log", connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }

            Logger.Info("[DatabaseManager] Migration V15 -> V16 completed.");
        }

        /// <summary>
        /// v16 → v17: game_versions UNIQUE INDEX を COLLATE NOCASE 化 (M3)。
        /// 旧 BINARY collation index は `v1.0.0` と `V1.0.0` を別行として許容していた。SemverInputControl が
        /// 大文字 V を受理する一方、UI 層 dup-check は OrdinalIgnoreCase のため外部ツール直 INSERT や
        /// レガシー復元データで case 違い重複が DB に入る経路があった。NOCASE INDEX で DB レベルでも弾く。
        /// 重複残存時は MigrateV14ToV15 と同じ skip + retry パターン。
        /// </summary>
        /// <returns>NOCASE INDEX 作成成功なら true、case 違い重複残存で skip なら false</returns>
        private bool MigrateV16ToV17(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            Logger.Info("[DatabaseManager] Executing migration V16 -> V17 (game_versions UNIQUE INDEX を COLLATE NOCASE 化, M3)");

            // NOCASE 重複検出 (LOWER で GROUP BY)
            var dups = new List<string>();
            using (var cmd = new SQLiteCommand(
                "SELECT game_id, LOWER(version) AS v_lower, COUNT(*) AS cnt FROM game_versions " +
                "GROUP BY game_id, LOWER(version) HAVING cnt > 1 ORDER BY game_id, v_lower",
                connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    dups.Add("  - game_id='" + reader["game_id"] + "', version (NOCASE)='" + reader["v_lower"] + "' (" + reader["cnt"] + " 行)");
                }
            }

            if (dups.Count > 0)
            {
                Logger.Warn(
                    "[DatabaseManager] WARNING: game_versions に case 違い重複を検出。NOCASE UNIQUE INDEX 作成を skip します " +
                    "(user_version 据え置き、次回起動時に再試行)。tools/sqlite3/sqlite3.exe で重複行を確認し、不要な行を削除してから " +
                    "Manager を再起動してください:\n" + string.Join("\n", dups));
                // 既存の BINARY INDEX は維持 (drop しない、= 部分的 fence は継続)。
                return false;
            }

            // 旧 BINARY INDEX を drop して NOCASE で作り直す。drop は idempotent (IF EXISTS)。
            using (var cmd = new SQLiteCommand(
                "DROP INDEX IF EXISTS " + GameVersionsVersionUniqueIndexName, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SQLiteCommand(
                "CREATE UNIQUE INDEX IF NOT EXISTS " + GameVersionsVersionUniqueIndexName +
                " ON game_versions(game_id, version COLLATE NOCASE)",
                connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            Logger.Info("[DatabaseManager] game_versions(game_id, version COLLATE NOCASE) UNIQUE INDEX を作成しました (M3)");
            return true;
        }

        /// <summary>(#234 ②) game_versions(game_id, version) UNIQUE INDEX 名。CreateTables / Migrate 共通。</summary>
        private const string GameVersionsVersionUniqueIndexName = "idx_game_versions_game_id_version";

        /// <summary>
        /// game_versions(game_id, version) の UNIQUE INDEX を作成する (#234 ②)。CreateTables (新規 DB)
        /// と MigrateV14ToV15 (既存 DB) の共通処理。version 文字列は raw 比較 (BINARY collation = index と
        /// 同じ) で重複判定する (= 意味的正規化 "v1.0.0"/"1.0.0" の同一視はアプリ層の責務、DB は raw 一致
        /// のみ保証)。重複 (game_id, version) が残存する場合は index 作成が制約違反で失敗するため、事前に
        /// 検出して throw せず警告ログ + false 返却で skip する (= 起動を壊さない、V10→V11 踏襲)。CreateTables
        /// 側は戻り値を無視 (警告のみで起動継続)、migration 側は false を user_version 据え置きへ伝播する。
        /// </summary>
        /// <returns>index 作成成功 / 既存なら true、重複残存で skip なら false</returns>
        private bool EnsureGameVersionsVersionUniqueIndex(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            // 既に index があれば no-op (idempotent)。
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@name", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@name", GameVersionsVersionUniqueIndexName);
                if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) return true;
            }

            // 重複 (game_id, version) を検出。あれば UNIQUE INDEX 作成は制約違反で失敗するので、
            // 事前検出して throw せず skip + 警告 (起動継続)。
            var dups = new List<string>();
            using (var cmd = new SQLiteCommand(
                "SELECT game_id, version, COUNT(*) AS cnt FROM game_versions GROUP BY game_id, version HAVING cnt > 1 ORDER BY game_id, version",
                connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    dups.Add("  - game_id='" + reader["game_id"] + "', version='" + reader["version"] + "' (" + reader["cnt"] + " 行)");
                }
            }

            if (dups.Count > 0)
            {
                Logger.Warn(
                    "[DatabaseManager] WARNING: game_versions に (game_id, version) の重複行を検出。UNIQUE INDEX 作成を skip します " +
                    "(user_version 据え置き、次回起動時に再試行)。tools/sqlite3/sqlite3.exe で重複行を確認し、不要な行を削除してから " +
                    "Manager を再起動してください:\n" + string.Join("\n", dups));
                return false;
            }

            // (累積監査 round 3 追加 fix) COLLATE NOCASE で作る。CreateTables 経路 (新規 DB) は
            // currentVersion=0 → SetDbVersion(17) へ直接 jump するため MigrateV16ToV17 (NOCASE rebuild) が
            // 永久に走らない経路があった。本番 2026-05-27 まっさら install の DB は BINARY index のまま
            // 動いており、M3 で導入した case 違い重複 fence (`v1.0.0` と `V1.0.0` の同一視) が無効化されて
            // いた回帰を、本関数を NOCASE で揃えることで構造的に閉じる。
            using (var cmd = new SQLiteCommand(
                "CREATE UNIQUE INDEX IF NOT EXISTS " + GameVersionsVersionUniqueIndexName + " ON game_versions(game_id, version COLLATE NOCASE)",
                connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
            Logger.Info("[DatabaseManager] game_versions(game_id, version COLLATE NOCASE) に UNIQUE INDEX を作成しました (#234 ②, M3)");
            return true;
        }

        /// <summary>
        /// (#179) manager_sessions テーブル作成 (CreateTables / MigrateV12ToV13 共通)。
        /// schema は SPEC §7.3 参照。`pc_name` を PRIMARY KEY、同 PC は 1 row のみ (重複起動は Named
        /// Mutex で物理 block する設計、SPEC §3.8)。
        /// </summary>
        private static void CreateManagerSessionsTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS manager_sessions (
                    pc_name TEXT PRIMARY KEY,
                    started_at_unix_ms INTEGER NOT NULL,
                    last_heartbeat_at_unix_ms INTEGER NOT NULL,
                    pid INTEGER NOT NULL,
                    manager_version TEXT NOT NULL
                )";
            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// surveys テーブル作成（CreateTables / MigrateV10ToV11 共通）
        /// </summary>
        private static void CreateSurveysTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS surveys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    rating INTEGER CHECK(rating BETWEEN 1 AND 5),
                    comment TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";
            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// play_records テーブル作成（CreateTables / MigrateV10ToV11 共通）
        /// </summary>
        private static void CreatePlayRecordsTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS play_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    game_id TEXT,
                    start_time TEXT,
                    end_time TEXT,
                    play_duration INTEGER,
                    player_count INTEGER,
                    FOREIGN KEY(game_id) REFERENCES games(game_id) ON DELETE CASCADE
                )";
            using (var cmd = new SQLiteCommand(sql, connection, transaction))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 指定テーブルに指定列が存在するかチェック（PRAGMA table_info 経由）
        /// </summary>
        private static bool TableHasColumn(SQLiteConnection connection, SQLiteTransaction transaction, string tableName, string columnName)
        {
            using (var cmd = new SQLiteCommand($"PRAGMA table_info({tableName})", connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == columnName) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 指定テーブルの行数を取得（COUNT(*)）
        /// </summary>
        private static long GetTableRowCount(SQLiteConnection connection, SQLiteTransaction transaction, string tableName)
        {
            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM {tableName}", connection, transaction))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private void CopyDevelopersToVersion(SQLiteConnection connection, SQLiteTransaction transaction, string gameId, int versionId)
        {
            string insertSql = @"
                INSERT INTO developers (game_id, last_name, first_name, grade, version_id)
                SELECT game_id, last_name, first_name, grade, @versionId
                FROM developers
                WHERE game_id = @gameId AND version_id IS NULL";

            using (var command = new SQLiteCommand(insertSql, connection, transaction))
            {
                command.Parameters.AddWithValue("@gameId", gameId);
                command.Parameters.AddWithValue("@versionId", versionId);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// (#253) v20 → v21: intro_slides table 新設 (イントロガイドのスライド)。CREATE TABLE IF NOT EXISTS で
        /// idempotent (= table 既存時も silent skip)。manager_sessions (v13) と同型の独立テーブル新設 migration。
        /// </summary>
        private void MigrateV20ToV21(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            CreateIntroSlidesTable(connection, transaction);
            Logger.Info("[DatabaseManager] v20 → v21 migration 完了 (intro_slides table 確保)");
        }

        /// <summary>
        /// (#253) v21 → v22: intro_slides から `duration_sec` を削除 (design 変更で自動送りを廃止、Launcher は手動ナビ)。
        /// `duration_sec` に CHECK 制約があり `ALTER TABLE DROP COLUMN` は不可 (SQLite は CHECK 参照列を drop 不能) の
        /// ため table recreate で削除する。intro_slides は FK / 子テーブル無しの独立テーブルなので DROP TABLE の
        /// CASCADE 暴発は無い。idempotent: `duration_sec` が既に無ければ no-op (新規 DB は CreateIntroSlidesTable が
        /// 最初から列を持たない)。
        /// </summary>
        private void MigrateV21ToV22(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            if (!TableHasColumn(connection, transaction, "intro_slides", "duration_sec"))
            {
                Logger.Info("[DatabaseManager] v21 → v22: intro_slides に duration_sec 無し、no-op");
                return;
            }
            string[] sqls =
            {
                @"CREATE TABLE intro_slides_new (
                    slide_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    display_order INTEGER DEFAULT 0,
                    body_text TEXT DEFAULT '',
                    image_path TEXT,
                    is_visible INTEGER DEFAULT 1
                )",
                @"INSERT INTO intro_slides_new (slide_id, display_order, body_text, image_path, is_visible)
                  SELECT slide_id, display_order, body_text, image_path, is_visible FROM intro_slides",
                "DROP TABLE intro_slides",
                "ALTER TABLE intro_slides_new RENAME TO intro_slides"
            };
            foreach (var sql in sqls)
            {
                using (var cmd = new SQLiteCommand(sql, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            Logger.Info("[DatabaseManager] v21 → v22 migration 完了 (intro_slides から duration_sec 削除、recreate)");
        }

        /// <summary>
        /// (#253) intro_slides テーブルを作成 (CreateTables / MigrateV20ToV21 共用 helper)。
        /// スクリーンセーバー → ブラウズ間に表示するイントロガイドのスライド群。画像は `guide/` にファイル別管理し、
        /// DB には相対パス (`image_path`) のみ持つ (games のサムネ/背景と同流儀)。他テーブルへの FK は無い独立テーブル。
        /// `body_text` は text-only スライド可で DEFAULT ''、`image_path` は image-only スライド可で NULL 許容。
        /// `is_visible` で削除せず一時非表示 (store_sections と同 pattern)。(#253 design 変更: 自動送り `duration_sec`
        /// は v22 で廃止、Launcher は手動ナビ = 全スキップ + 次へ/戻る。)
        /// </summary>
        private void CreateIntroSlidesTable(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS intro_slides (
                    slide_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    display_order INTEGER DEFAULT 0,
                    body_text TEXT DEFAULT '',
                    image_path TEXT,
                    is_visible INTEGER DEFAULT 1
                )";

            using (var command = new SQLiteCommand(sql, connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 各テーブルが持つべき列名一覧（VerifySchema で使用）。
        /// SchemaManager.CreateTables() および各 MigrateVxToVy で作る最終形と一致させること。
        /// スキーマ変更時はこの定義も同時に更新する（AGENTS.md "Database Schema Management" 参照）。
        /// </summary>
        private static readonly Dictionary<string, string[]> ExpectedSchema = new Dictionary<string, string[]>
        {
            { "games", new[] { "game_id", "title", "description", "release_year", "genre", "min_players", "max_players", "difficulty", "play_time", "controller_support", "supported_connection", "thumbnail_path", "background_path", "executable_path", "display_order", "is_visible", "controls", "key_mapping", "arguments", "version" } },
            { "game_versions", new[] { "id", "game_id", "version", "executable_path", "arguments", "description", "title", "genre", "min_players", "max_players", "difficulty", "play_time", "controller_support", "supported_connection", "thumbnail_path", "background_path", "update_note", "registered_at" } },
            { "developers", new[] { "id", "game_id", "last_name", "first_name", "grade", "version_id" } },
            // (累積監査 round 4 Low-28/29) game_genres は v18 で DROP した dead table のため除去 (MigrateV17ToV18 参照)。
            { "play_records", new[] { "id", "game_id", "start_time", "end_time", "play_duration", "player_count" } },
            { "surveys", new[] { "id", "game_id", "rating", "comment", "created_at" } },
            { "launcher_surveys", new[] { "id", "rating", "favorite_game_id", "comment", "created_at" } },
            { "settings", new[] { "key", "value" } },
            { "store_sections", new[] { "section_id", "title", "section_type", "section_source", "display_order", "max_display_count", "is_visible" } },
            { "store_section_games", new[] { "id", "section_id", "game_id", "display_order", "display_text" } },
            // backup_log は v19 で DROP した (MigrateV18ToV19 参照、履歴を BackupCatalogService の file-scan に移行)。
            // VerifySchema の検証対象外 = drop 済 DB / 新規 DB の両方で PASS する。
            { "manager_sessions", new[] { "pc_name", "started_at_unix_ms", "last_heartbeat_at_unix_ms", "pid", "manager_version" } },
            { "intro_slides", new[] { "slide_id", "display_order", "body_text", "image_path", "is_visible" } },
        };

        /// <summary>
        /// 全テーブルのスキーマが ExpectedSchema と一致するか検証し、不一致があればログ出力する。
        /// CreateTables() / マイグレーション完了後に呼び出すことを想定（InitializeDatabase 末尾）。
        /// drift があった場合でも例外は投げず、警告ログのみ。アプリ動作はそのまま継続する。
        /// </summary>
        /// <returns>すべてのテーブルが期待通り = true、1 つでも drift があれば false</returns>
        private bool VerifySchema(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            int driftCount = 0;
            foreach (var pair in ExpectedSchema)
            {
                if (!VerifyTableColumns(connection, transaction, pair.Key, pair.Value))
                {
                    driftCount++;
                }
            }

            if (driftCount > 0)
            {
                Logger.Warn($"[VerifySchema] {driftCount} 個のテーブルでスキーマ drift を検出しました。AGENTS.md の Database Schema Management セクションを参照して対応してください。");
                return false;
            }

            Logger.Info($"[VerifySchema] 全 {ExpectedSchema.Count} テーブルのスキーマ整合性 OK");
            return true;
        }

        /// <summary>
        /// 指定テーブルの列名一覧が期待値と一致するか検証する。
        /// 不足列・余分列があればログ出力する。
        /// </summary>
        private static bool VerifyTableColumns(SQLiteConnection connection, SQLiteTransaction transaction, string tableName, string[] expectedColumns)
        {
            var actualColumns = new HashSet<string>();
            using (var cmd = new SQLiteCommand($"PRAGMA table_info({tableName})", connection, transaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    actualColumns.Add(reader["name"].ToString());
                }
            }

            if (actualColumns.Count == 0)
            {
                Logger.Warn($"[VerifySchema] WARNING: テーブル '{tableName}' が存在しません。");
                return false;
            }

            var expectedSet = new HashSet<string>(expectedColumns);
            var missing = new List<string>();
            foreach (var col in expectedColumns)
            {
                if (!actualColumns.Contains(col)) missing.Add(col);
            }
            var extra = new List<string>();
            foreach (var col in actualColumns)
            {
                if (!expectedSet.Contains(col)) extra.Add(col);
            }

            if (missing.Count == 0 && extra.Count == 0)
            {
                return true;
            }

            Logger.Warn($"[VerifySchema] WARNING: テーブル '{tableName}' のスキーマが期待値と一致しません");
            if (missing.Count > 0)
            {
                Logger.Warn($"  期待されるが存在しない列: {string.Join(", ", missing)}");
            }
            if (extra.Count > 0)
            {
                Logger.Warn($"  期待されない余分な列: {string.Join(", ", extra)}");
            }
            return false;
        }
    }
}
