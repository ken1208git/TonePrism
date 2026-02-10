## データベース管理クラス
## SQLiteデータベースの接続、データ読み込みを提供

extends RefCounted
class_name DatabaseManager

var db: SQLite = null
var db_path: String = ""

# 現在のデータベースバージョン
# 構造変更があるたびにインクリメントする
const CURRENT_DB_VERSION: int = 6

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
	
	# WALモードを有効化（同時書き込み対策）
	db.query("PRAGMA journal_mode=WAL;")
	
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

## すべてのゲーム情報を取得（表示対象のみ）
## @return: GameInfoの配列
func get_all_games() -> Array[GameInfo]:
	if not is_open():
		if not open():
			return []
	
	var games: Array[GameInfo] = []
	
	# 表示対象のゲームのみを取得（is_visible = 1）
	# display_orderでソート
	var query = "SELECT * FROM games WHERE is_visible = 1 ORDER BY display_order ASC, title ASC"
	
	# クエリを実行（godot-sqliteのAPI）
	if db.query(query):
		# 結果を取得（godot-sqliteはデフォルトで辞書の配列を返す）
		var result_array = db.get_query_result()
		if result_array != null:
			for row in result_array:
				# 結果は辞書形式であることを前提とする
				if row is Dictionary:
					var game = _create_game_info_from_row_dict(row)
					if game != null:
						# 製作者情報を取得
						game.developers = get_developers_by_game_id(game.game_id)
						games.append(game)
	else:
		push_error("[DatabaseManager] ゲーム情報の取得に失敗しました")
	
	return games

## 指定したゲームIDのゲーム情報を取得
## @param game_id: ゲームID
## @return: GameInfo（見つからない場合はnull）
func get_game_by_id(game_id: String) -> GameInfo:
	if not is_open():
		if not open():
			return null
	
	# SQLインジェクション対策: プレースホルダーとバインディングを使用
	var query = "SELECT * FROM games WHERE game_id = ?"
	var bindings = [game_id]
	
	# クエリを実行（godot-sqliteのAPI）
	if db.query_with_bindings(query, bindings):
		# 結果を取得
		var result_array = db.get_query_result()
		if result_array != null and result_array.size() > 0:
			var row = result_array[0]
			if row is Dictionary:
				var game = _create_game_info_from_row_dict(row)
				if game != null:
					# 製作者情報を取得
					game.developers = get_developers_by_game_id(game.game_id)
					return game
		else:
			print("[DatabaseManager] ゲームが見つかりませんでした: ", game_id)
	else:
		push_error("[DatabaseManager] ゲーム情報の取得に失敗しました: " + game_id)
	
	return null

## 指定したゲームIDの製作者情報を取得
## @param game_id: ゲームID
## @return: DeveloperInfoの配列
func get_developers_by_game_id(game_id: String) -> Array[DeveloperInfo]:
	if not is_open():
		if not open():
			return []
	
	var developers: Array[DeveloperInfo] = []
	
	# SQLインジェクション対策: プレースホルダーとバインディングを使用
	var query = """
		SELECT id, game_id, last_name, first_name, grade
		FROM developers
		WHERE game_id = ?
		ORDER BY id ASC
	"""
	var bindings = [game_id]
	
	# クエリを実行
	if db.query_with_bindings(query, bindings):
		# 結果を取得
		var result_array = db.get_query_result()
		if result_array != null:
			for row in result_array:
				if row is Dictionary:
					var developer = DeveloperInfo.new()
					developer.id = int(row.get("id", -1))
					developer.game_id = str(row.get("game_id", ""))
					developer.last_name = str(row.get("last_name", ""))
					developer.first_name = str(row.get("first_name", ""))
					developer.grade = str(row.get("grade", ""))
					developers.append(developer)
	else:
		push_error("[DatabaseManager] 製作者情報の取得に失敗しました: " + game_id)
	
	return developers

## データベースの行からGameInfoを作成（辞書形式のみ対応）
func _create_game_info_from_row_dict(row: Dictionary) -> GameInfo:
	var game = GameInfo.new()
	
	# 文字列型 (デフォルト値: "")
	game.game_id = str(row.get("game_id", ""))
	game.title = str(row.get("title", ""))
	game.description = str(row.get("description", ""))
	game.thumbnail_path = str(row.get("thumbnail_path", ""))
	game.background_path = str(row.get("background_path", ""))
	game.executable_path = str(row.get("executable_path", ""))
	game.controls = str(row.get("controls", ""))
	game.key_mapping = str(row.get("key_mapping", ""))
	game.arguments = str(row.get("arguments", ""))
	
	# 数値型 (デフォルト値: -1 または 0)
	game.release_year = int(row.get("release_year", -1))
	game.min_players = int(row.get("min_players", -1))
	game.max_players = int(row.get("max_players", -1))
	game.difficulty = int(row.get("difficulty", -1))
	game.play_time = int(row.get("play_time", -1))
	game.display_order = int(row.get("display_order", -1))
	game.supported_connection = int(row.get("supported_connection", 0))
	
	# ブール型 (0/1 から変換)
	game.controller_support = bool(row.get("controller_support", 0))
	game.is_visible = bool(row.get("is_visible", 1)) # デフォルト表示
	
	# 特殊な型（ジャンル）
	game.genre = _parse_genre(str(row.get("genre", "")))
	
	return game

## ジャンル文字列をパース（JSON形式またはカンマ区切り）
func _parse_genre(genre_str: String) -> Array[String]:
	if genre_str.is_empty():
		return []
	
	# JSON形式を試す
	var json = JSON.new()
	var parse_result = json.parse(genre_str)
	if parse_result == OK:
		var parsed = json.data
		if parsed is Array:
			var result: Array[String] = []
			for item in parsed:
				if item is String:
					result.append(item)
			return result
	
	# カンマ区切りの場合
	var genres: Array[String] = []
	var parts = genre_str.split(",")
	for part in parts:
		var trimmed = part.strip_edges()
		if not trimmed.is_empty():
			genres.append(trimmed)
	
	return genres

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
		# Manager側でマイグレーションが行われているはずなので、ここは本来通らないはずだが、
		# もし通った場合は、Launcher側で無理にマイグレーションせず、警告を出すに留めるのが安全。
		# （ManagerとLauncherでマイグレーションロジックの二重管理を避けるため）
		push_warning("[DatabaseManager] Warning: DBバージョン(%d)が古いです。Managerを起動してDBを更新することをお勧めします。" % current_version)
		
		# トランザクション開始
		# db.query("BEGIN TRANSACTION")
		# ... マイグレーションロジックが必要ならここに追加 ...
		# db.query("COMMIT")
