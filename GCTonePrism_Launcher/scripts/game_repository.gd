## ゲーム・製作者データのクエリを担当
## DatabaseManager のDBインスタンスを使ってデータを取得する

extends RefCounted
class_name GameRepository

var _db_manager: DatabaseManager

func _init(db_manager: DatabaseManager) -> void:
	_db_manager = db_manager

## すべてのゲーム情報を取得（表示対象のみ）
func get_all_games() -> Array[GameInfo]:
	if not _db_manager.is_open():
		if not _db_manager.open():
			return []

	var games: Array[GameInfo] = []
	var query = "SELECT * FROM games WHERE is_visible = 1 ORDER BY display_order ASC, title ASC"

	if _db_manager.db.query(query):
		var result_array = _db_manager.db.get_query_result()
		if result_array != null:
			for row in result_array:
				if row is Dictionary:
					var game = _create_game_info_from_row_dict(row)
					if game != null:
						game.developers = get_developers_by_game_id(game.game_id)
						games.append(game)
	else:
		push_error("[GameRepository] ゲーム情報の取得に失敗しました")

	return games

## 指定したゲームIDのゲーム情報を取得
func get_game_by_id(game_id: String) -> GameInfo:
	if not _db_manager.is_open():
		if not _db_manager.open():
			return null

	var query = "SELECT * FROM games WHERE game_id = ?"
	var bindings = [game_id]

	if _db_manager.db.query_with_bindings(query, bindings):
		var result_array = _db_manager.db.get_query_result()
		if result_array != null and result_array.size() > 0:
			var row = result_array[0]
			if row is Dictionary:
				var game = _create_game_info_from_row_dict(row)
				if game != null:
					game.developers = get_developers_by_game_id(game.game_id)
					return game
		else:
			print("[GameRepository] ゲームが見つかりませんでした: ", game_id)
	else:
		push_error("[GameRepository] ゲーム情報の取得に失敗しました: " + game_id)

	return null

## 指定したゲームIDの製作者情報を取得
func get_developers_by_game_id(game_id: String) -> Array[DeveloperInfo]:
	if not _db_manager.is_open():
		if not _db_manager.open():
			return []

	var developers: Array[DeveloperInfo] = []
	var query = """
		SELECT d.id, d.game_id, d.last_name, d.first_name, d.grade
		FROM developers d
		JOIN game_versions gv ON d.version_id = gv.id
		JOIN games g ON g.game_id = gv.game_id AND g.version = gv.version
		WHERE d.game_id = ?
		ORDER BY d.id ASC
	"""
	var bindings = [game_id]

	if _db_manager.db.query_with_bindings(query, bindings):
		var result_array = _db_manager.db.get_query_result()
		if result_array != null:
			for row in result_array:
				if row is Dictionary:
					var developer = DeveloperInfo.new()
					developer.id = _db_manager.safe_int(row.get("id"), -1)
					developer.game_id = str(row.get("game_id", ""))
					developer.last_name = str(row.get("last_name", ""))
					developer.first_name = str(row.get("first_name", ""))
					developer.grade = str(row.get("grade", ""))
					developers.append(developer)
	else:
		push_error("[GameRepository] 製作者情報の取得に失敗しました: " + game_id)

	return developers

## データベースの行からGameInfoを作成
func _create_game_info_from_row_dict(row: Dictionary) -> GameInfo:
	var game = GameInfo.new()

	game.game_id = str(row.get("game_id", ""))
	game.title = str(row.get("title", ""))
	game.description = str(row.get("description", ""))
	game.thumbnail_path = str(row.get("thumbnail_path", ""))
	game.background_path = str(row.get("background_path", ""))
	game.executable_path = str(row.get("executable_path", ""))
	game.controls = str(row.get("controls", ""))
	game.key_mapping = str(row.get("key_mapping", ""))
	game.arguments = str(row.get("arguments", ""))

	game.release_year = _db_manager.safe_int(row.get("release_year"), -1)
	game.min_players = _db_manager.safe_int(row.get("min_players"), -1)
	game.max_players = _db_manager.safe_int(row.get("max_players"), -1)
	game.difficulty = _db_manager.safe_int(row.get("difficulty"), -1)
	game.play_time = _db_manager.safe_int(row.get("play_time"), -1)
	game.display_order = _db_manager.safe_int(row.get("display_order"), -1)
	game.supported_connection = _db_manager.safe_int(row.get("supported_connection"), 0)

	game.controller_support = _db_manager.safe_bool(row.get("controller_support"), false)
	game.is_visible = _db_manager.safe_bool(row.get("is_visible"), true)

	game.genre = _parse_genre(str(row.get("genre", "")))

	return game

## ジャンル文字列をパース（JSON形式またはカンマ区切り）
func _parse_genre(genre_str: String) -> Array[String]:
	if genre_str.is_empty():
		return []

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

	var genres: Array[String] = []
	var parts = genre_str.split(",")
	for part in parts:
		var trimmed = part.strip_edges()
		if not trimmed.is_empty():
			genres.append(trimmed)

	return genres
