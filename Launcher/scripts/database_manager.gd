## データベース管理クラス
## SQLiteデータベースの接続、バージョン管理、ユーティリティを提供
## ゲームデータのクエリは GameRepository / StoreSectionRepository を使用

extends RefCounted
class_name DatabaseManager

var db: SQLite = null
var db_path: String = ""

# Launcher が想定する DB スキーマバージョン
# Manager 側 SchemaManager.cs の CurrentDbVersion と歩調を合わせること
# (v9, v10, v12 は backup_log 関連で Launcher は触らない / v11 の surveys・play_records
#  新スキーマには Launcher のクエリが既に対応済 → 単に定数追従のみ)
const CURRENT_DB_VERSION: int = 12

## データベースを開く
func open() -> bool:
	if db != null:
		return true

	db_path = PathManager.get_database_path()

	# データベースファイルが存在するか確認
	if not FileAccess.file_exists(db_path):
		print("[DatabaseManager] Error: データベースファイルが見つかりません: " + db_path)
		return false

	# SQLiteインスタンスを作成
	db = SQLite.new()

	# データベースパスを設定
	db.path = db_path

	# データベースを開く（godot-sqliteのAPI）
	db.open_db()

	# データベースが開けたか確認（エラーが発生した場合はdbがnullになる可能性がある）
	if db == null:
		print("[DatabaseManager] Error: データベースを開けませんでした: " + db_path)
		return false

	print("[DatabaseManager] データベースを開きました: ", db_path)

	# SMB ネットワーク共有上での運用安全性のため DELETE モードを使用 (#103)
	# busy_timeout で書き込み競合時の待機列を確保（10000ms）
	db.query("PRAGMA journal_mode=DELETE;")
	db.query("PRAGMA busy_timeout=10000;")

	# データベースのバージョンチェックとマイグレーション
	_check_and_migrate_db()

	return true

## データベースを閉じる
func close() -> void:
	if db != null:
		db.close_db()
		db = null
		print("[DatabaseManager] データベースを閉じました")

## データベースが開いているか確認
func is_open() -> bool:
	return db != null

## データベースのバージョンを取得
func _get_db_version() -> int:
	if db == null:
		return 0

	# PRAGMA user_version を取得
	db.query("PRAGMA user_version")
	var result = db.get_query_result()

	if result != null and result.size() > 0:
		# 結果の形式に合わせて取得（辞書または配列）
		var row = result[0]
		if row is Dictionary:
			var ver = row.get("user_version", 0)
			return int(ver) if ver != null else 0
		elif row is Array and row.size() > 0:
			var ver = row[0]
			return int(ver) if ver != null else 0

	return 0

## データベースのバージョンを設定
func _set_db_version(version: int) -> void:
	if db == null:
		return

	db.query("PRAGMA user_version = %d" % version)
	print("[DatabaseManager] データベースバージョンを %d に更新しました" % version)

## データベースのバージョンチェックとマイグレーションを実行
func _check_and_migrate_db() -> void:
	var current_version = _get_db_version()
	print("[DatabaseManager] 現在のDBバージョン: %d, Launcher期待バージョン: %d" % [current_version, CURRENT_DB_VERSION])

	# バージョンが0の場合（新規作成時など）、本来はManagerで初期化すべきだが、
	# 万が一の場合はManagerに任せる方針とする（Launcherは基本Read-Onlyに近い）
	if current_version == 0:
		print("[DatabaseManager] Warning: DBバージョンが0です。Managerによる初期化が必要な可能性があります。")
		return

	# バージョンが新しい場合（将来のバージョン）
	if current_version > CURRENT_DB_VERSION:
		push_warning("[DatabaseManager] DBバージョン(%d)がLauncherの対応バージョン(%d)より新しいです。一部機能が正常に動作しない可能性があります。" % [current_version, CURRENT_DB_VERSION])
		return

	# マイグレーションが必要な場合（古いバージョン）
	if current_version < CURRENT_DB_VERSION:
		push_warning("[DatabaseManager] Warning: DBバージョン(%d)が古いです。Managerを起動してDBを更新することをお勧めします。" % current_version)

## 値を安全にintに変換する（null対策）
func safe_int(val, default_val: int = 0) -> int:
	if val == null:
		return default_val
	return int(val)

## 値を安全にboolに変換する（null対策）
func safe_bool(val, default_val: bool = false) -> bool:
	if val == null:
		return default_val
	# 数値（0 or 1）として扱われる場合が多いため、int経由で変換
	return bool(int(val))
