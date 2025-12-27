## データベース管理クラス
## SQLiteデータベースの接続、データ読み込みを提供

extends RefCounted
class_name DatabaseManager

var db: SQLite = null
var db_path: String = ""

## データベースを開く
func open() -> bool:
	if db != null:
		return true
	
	db_path = PathManager.get_database_path()
	
	# データベースファイルが存在するか確認
	if not FileAccess.file_exists(db_path):
		push_error("[DatabaseManager] データベースファイルが見つかりません: " + db_path)
		return false
	
	# SQLiteインスタンスを作成
	db = SQLite.new()
	
	# データベースパスを設定
	db.path = db_path
	
	# データベースを開く（godot-sqliteのAPI）
	db.open_db()
	
	# データベースが開けたか確認（エラーが発生した場合はdbがnullになる可能性がある）
	if db == null:
		push_error("[DatabaseManager] データベースを開けませんでした: " + db_path)
		return false
	
	print("[DatabaseManager] データベースを開きました: ", db_path)
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
	var query = """
		SELECT 
			game_id, title, description, release_year, genre,
			min_players, max_players, difficulty, play_time, controller_support,
			thumbnail_path, background_path, executable_path,
			display_order, is_visible, controls, key_mapping
		FROM games
		WHERE is_visible = 1
		ORDER BY display_order ASC, title ASC
	"""
	
	# クエリを実行（godot-sqliteのAPI）
	# query()メソッドはboolを返す
	if db.query(query):
		# 結果を取得（get_query_result()で全行を取得）
		var result_array = db.get_query_result()
		if result_array != null:
			for row in result_array:
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
	
	var query = """
		SELECT 
			game_id, title, description, release_year, genre,
			min_players, max_players, difficulty, play_time, controller_support,
			thumbnail_path, background_path, executable_path,
			display_order, is_visible, controls, key_mapping
		FROM games
		WHERE game_id = ?
	"""
	
	# パラメータ化クエリを実行（godot-sqliteのAPI）
	# query_with_args()メソッドはboolを返す
	if db.query_with_args(query, [game_id]):
		# 結果を取得（get_query_result()で全行を取得）
		var result_array = db.get_query_result()
		if result_array != null and result_array.size() > 0:
			var game = _create_game_info_from_row_dict(result_array[0])
			if game != null:
				# 製作者情報を取得
				game.developers = get_developers_by_game_id(game.game_id)
				return game
		else:
			print("[DatabaseManager] ゲームが見つかりませんでした: ", game_id)
	else:
		push_error("[DatabaseManager] ゲーム情報の取得に失敗しました")
	
	return null

## 指定したゲームIDの製作者情報を取得
## @param game_id: ゲームID
## @return: DeveloperInfoの配列
func get_developers_by_game_id(game_id: String) -> Array[DeveloperInfo]:
	if not is_open():
		if not open():
			return []
	
	var developers: Array[DeveloperInfo] = []
	
	var query = """
		SELECT id, game_id, last_name, first_name, grade
		FROM developers
		WHERE game_id = ?
		ORDER BY id ASC
	"""
	
	# パラメータ化クエリを実行（godot-sqliteのAPI）
	# query_with_args()メソッドはboolを返す
	if db.query_with_args(query, [game_id]):
		# 結果を取得（get_query_result()で全行を取得）
		var result_array = db.get_query_result()
		if result_array != null:
			for row in result_array:
				var developer = DeveloperInfo.new()
				# 結果は辞書形式で返される可能性があるため、両方に対応
				if row is Dictionary:
					developer.id = row.get("id", -1) if row.has("id") else -1
					developer.game_id = row.get("game_id", "") if row.has("game_id") else ""
					developer.last_name = row.get("last_name", "") if row.has("last_name") else ""
					developer.first_name = row.get("first_name", "") if row.has("first_name") else ""
					developer.grade = row.get("grade", "") if row.has("grade") else ""
				else:
					# 配列形式の場合
					developer.id = row[0] if row.size() > 0 and row[0] != null else -1
					developer.game_id = row[1] if row.size() > 1 and row[1] != null else ""
					developer.last_name = row[2] if row.size() > 2 and row[2] != null else ""
					developer.first_name = row[3] if row.size() > 3 and row[3] != null else ""
					developer.grade = row[4] if row.size() > 4 and row[4] != null else ""
				developers.append(developer)
	else:
		push_error("[DatabaseManager] 製作者情報の取得に失敗しました")
	
	return developers

## データベースの行からGameInfoを作成（辞書形式または配列形式）
func _create_game_info_from_row_dict(row) -> GameInfo:
	var game = GameInfo.new()
	
	# 結果は辞書形式で返される可能性があるため、両方に対応
	if row is Dictionary:
		game.game_id = row.get("game_id", "") if row.has("game_id") else ""
		game.title = row.get("title", "") if row.has("title") else ""
		game.description = row.get("description", "") if row.has("description") else ""
		game.release_year = row.get("release_year", -1) if row.has("release_year") else -1
		game.genre = _parse_genre(row.get("genre", "") if row.has("genre") else "")
		game.min_players = row.get("min_players", -1) if row.has("min_players") else -1
		game.max_players = row.get("max_players", -1) if row.has("max_players") else -1
		game.difficulty = row.get("difficulty", -1) if row.has("difficulty") else -1
		game.play_time = row.get("play_time", -1) if row.has("play_time") else -1
		game.controller_support = (row.get("controller_support", 0) if row.has("controller_support") else 0) == 1
		game.thumbnail_path = row.get("thumbnail_path", "") if row.has("thumbnail_path") else ""
		game.background_path = row.get("background_path", "") if row.has("background_path") else ""
		game.executable_path = row.get("executable_path", "") if row.has("executable_path") else ""
		game.display_order = row.get("display_order", -1) if row.has("display_order") else -1
		game.is_visible = (row.get("is_visible", 0) if row.has("is_visible") else 0) == 1
		game.controls = row.get("controls", "") if row.has("controls") else ""
		game.key_mapping = row.get("key_mapping", "") if row.has("key_mapping") else ""
	else:
		# 配列形式の場合
		game.game_id = row[0] if row.size() > 0 and row[0] != null else ""
		game.title = row[1] if row.size() > 1 and row[1] != null else ""
		game.description = row[2] if row.size() > 2 and row[2] != null else ""
		game.release_year = row[3] if row.size() > 3 and row[3] != null else -1
		game.genre = _parse_genre(row[4] if row.size() > 4 and row[4] != null else "")
		game.min_players = row[5] if row.size() > 5 and row[5] != null else -1
		game.max_players = row[6] if row.size() > 6 and row[6] != null else -1
		game.difficulty = row[7] if row.size() > 7 and row[7] != null else -1
		game.play_time = row[8] if row.size() > 8 and row[8] != null else -1
		game.controller_support = (row[9] if row.size() > 9 and row[9] != null else 0) == 1
		game.thumbnail_path = row[10] if row.size() > 10 and row[10] != null else ""
		game.background_path = row[11] if row.size() > 11 and row[11] != null else ""
		game.executable_path = row[12] if row.size() > 12 and row[12] != null else ""
		game.display_order = row[13] if row.size() > 13 and row[13] != null else -1
		game.is_visible = (row[14] if row.size() > 14 and row[14] != null else 0) == 1
		game.controls = row[15] if row.size() > 15 and row[15] != null else ""
		game.key_mapping = row[16] if row.size() > 16 and row[16] != null else ""
	
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
