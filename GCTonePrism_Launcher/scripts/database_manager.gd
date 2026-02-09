## データベース管理クラス
## SQLiteデータベースの接続、データ読み込みを提供

extends RefCounted
class_name DatabaseManager

var db: SQLite = null
var db_path: String = ""

# 現在のデータベースバージョン
# 構造変更があるたびにインクリメントする
const CURRENT_DB_VERSION: int = 2

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

## SQL文字列をエスケープする（SQLインジェクション対策）
## @param value: エスケープする文字列
## @return: エスケープされた文字列
func _escape_sql_string(value: String) -> String:
	# シングルクォートを2つのシングルクォートに置き換える
	return value.replace("'", "''")

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
	
	# SQLインジェクション対策: 値をエスケープしてクエリに埋め込む
	var escaped_game_id = _escape_sql_string(game_id)
	var query = "SELECT * FROM games WHERE game_id = '%s'" % escaped_game_id
	
	# クエリを実行（godot-sqliteのAPI）
	# query()メソッドはboolを返す
	if db.query(query):
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
	
	# SQLインジェクション対策: 値をエスケープしてクエリに埋め込む
	var escaped_game_id = _escape_sql_string(game_id)
	var query = """
		SELECT id, game_id, last_name, first_name, grade
		FROM developers
		WHERE game_id = '%s'
		ORDER BY id ASC
	""" % escaped_game_id
	
	# クエリを実行（godot-sqliteのAPI）
	# query()メソッドはboolを返す
	if db.query(query):
		# 結果を取得（get_query_result()で全行を取得）
		var result_array = db.get_query_result()
		if result_array != null:
			for row in result_array:
				var developer = DeveloperInfo.new()
				# 結果は辞書形式で返される可能性があるため、両方に対応
				if row is Dictionary:
					var id_val = row.get("id", -1) if row.has("id") else -1
					developer.id = int(id_val) if id_val != null else -1
					
					var game_id_val = row.get("game_id", "") if row.has("game_id") else ""
					developer.game_id = game_id_val if game_id_val != null else ""
					
					var last_name_val = row.get("last_name", "") if row.has("last_name") else ""
					developer.last_name = last_name_val if last_name_val != null else ""
					
					var first_name_val = row.get("first_name", "") if row.has("first_name") else ""
					developer.first_name = first_name_val if first_name_val != null else ""
					
					var grade_val = row.get("grade", "") if row.has("grade") else ""
					developer.grade = grade_val if grade_val != null else ""
				else:
					# 配列形式の場合
					var id_val = row[0] if row.size() > 0 else -1
					developer.id = int(id_val) if id_val != null else -1
					
					var game_id_val = row[1] if row.size() > 1 else ""
					developer.game_id = game_id_val if game_id_val != null else ""
					
					var last_name_val = row[2] if row.size() > 2 else ""
					developer.last_name = last_name_val if last_name_val != null else ""
					
					var first_name_val = row[3] if row.size() > 3 else ""
					developer.first_name = first_name_val if first_name_val != null else ""
					
					var grade_val = row[4] if row.size() > 4 else ""
					developer.grade = grade_val if grade_val != null else ""
				developers.append(developer)
	else:
		push_error("[DatabaseManager] 製作者情報の取得に失敗しました")
	
	return developers

## データベースの行からGameInfoを作成（辞書形式または配列形式）
func _create_game_info_from_row_dict(row) -> GameInfo:
	var game = GameInfo.new()
	
	# 結果は辞書形式で返される可能性があるため、両方に対応
	# 結果は辞書形式で返される可能性があるため、両方に対応
	if row is Dictionary:
		# 文字列型プロパティはnullチェックが必要
		var game_id_value = row.get("game_id", "") if row.has("game_id") else ""
		game.game_id = game_id_value if game_id_value != null else ""
		var title_value = row.get("title", "") if row.has("title") else ""
		game.title = title_value if title_value != null else ""
		var description_value = row.get("description", "") if row.has("description") else ""
		game.description = description_value if description_value != null else ""
		
		# 数値型・ブール型のnullチェック
		var release_year_val = row.get("release_year", -1) if row.has("release_year") else -1
		game.release_year = int(release_year_val) if release_year_val != null else -1
		
		var genre_value = row.get("genre", "") if row.has("genre") else ""
		game.genre = _parse_genre(genre_value if genre_value != null else "")
		
		var min_players_val = row.get("min_players", -1) if row.has("min_players") else -1
		game.min_players = int(min_players_val) if min_players_val != null else -1
		
		var max_players_val = row.get("max_players", -1) if row.has("max_players") else -1
		game.max_players = int(max_players_val) if max_players_val != null else -1
		
		var difficulty_val = row.get("difficulty", -1) if row.has("difficulty") else -1
		game.difficulty = int(difficulty_val) if difficulty_val != null else -1
		
		var play_time_val = row.get("play_time", -1) if row.has("play_time") else -1
		game.play_time = int(play_time_val) if play_time_val != null else -1
		
		var controller_support_val = row.get("controller_support", 0) if row.has("controller_support") else 0
		game.controller_support = (int(controller_support_val) == 1) if controller_support_val != null else false
		
		var supported_connection_val = row.get("supported_connection", 0) if row.has("supported_connection") else 0
		game.supported_connection = int(supported_connection_val) if supported_connection_val != null else 0
		
		var thumbnail_path_value = row.get("thumbnail_path", "") if row.has("thumbnail_path") else ""
		game.thumbnail_path = thumbnail_path_value if thumbnail_path_value != null else ""
		var background_path_value = row.get("background_path", "") if row.has("background_path") else ""
		game.background_path = background_path_value if background_path_value != null else ""
		var executable_path_value = row.get("executable_path", "") if row.has("executable_path") else ""
		game.executable_path = executable_path_value if executable_path_value != null else ""
		
		var display_order_val = row.get("display_order", -1) if row.has("display_order") else -1
		game.display_order = int(display_order_val) if display_order_val != null else -1
		
		var is_visible_val = row.get("is_visible", 0) if row.has("is_visible") else 0
		game.is_visible = (int(is_visible_val) == 1) if is_visible_val != null else true
		
		var controls_value = row.get("controls", "") if row.has("controls") else ""
		game.controls = controls_value if controls_value != null else ""
		var key_mapping_value = row.get("key_mapping", "") if row.has("key_mapping") else ""
		game.key_mapping = key_mapping_value if key_mapping_value != null else ""
		var arguments_value = row.get("arguments", "") if row.has("arguments") else ""
		game.arguments = arguments_value if arguments_value != null else ""
	else:
		# 配列形式の場合
		# 文字列型プロパティはnullチェックが必要
		var game_id_value = row[0] if row.size() > 0 and row[0] != null else ""
		game.game_id = game_id_value if game_id_value != null else ""
		var title_value = row[1] if row.size() > 1 and row[1] != null else ""
		game.title = title_value if title_value != null else ""
		var description_value = row[2] if row.size() > 2 and row[2] != null else ""
		game.description = description_value if description_value != null else ""
		
		var release_year_val = row[3] if row.size() > 3 else -1
		game.release_year = int(release_year_val) if release_year_val != null else -1
		
		var genre_value = row[4] if row.size() > 4 and row[4] != null else ""
		game.genre = _parse_genre(genre_value if genre_value != null else "")
		
		var min_players_val = row[5] if row.size() > 5 else -1
		game.min_players = int(min_players_val) if min_players_val != null else -1
		
		var max_players_val = row[6] if row.size() > 6 else -1
		game.max_players = int(max_players_val) if max_players_val != null else -1
		
		var difficulty_val = row[7] if row.size() > 7 else -1
		game.difficulty = int(difficulty_val) if difficulty_val != null else -1
		
		var play_time_val = row[8] if row.size() > 8 else -1
		game.play_time = int(play_time_val) if play_time_val != null else -1
		
		var controller_support_val = row[9] if row.size() > 9 else 0
		game.controller_support = (int(controller_support_val) == 1) if controller_support_val != null else false
		
		var supported_connection_val = row[10] if row.size() > 10 else 0
		game.supported_connection = int(supported_connection_val) if supported_connection_val != null else 0
		
		var thumbnail_path_value = row[11] if row.size() > 11 and row[11] != null else ""
		game.thumbnail_path = thumbnail_path_value if thumbnail_path_value != null else ""
		var background_path_value = row[12] if row.size() > 12 and row[12] != null else ""
		game.background_path = background_path_value if background_path_value != null else ""
		var executable_path_value = row[13] if row.size() > 13 and row[13] != null else ""
		game.executable_path = executable_path_value if executable_path_value != null else ""
		
		var display_order_val = row[14] if row.size() > 14 else -1
		game.display_order = int(display_order_val) if display_order_val != null else -1
		
		var is_visible_val = row[15] if row.size() > 15 else 0
		game.is_visible = (int(is_visible_val) == 1) if is_visible_val != null else true
		
		var controls_value = row[16] if row.size() > 16 and row[16] != null else ""
		game.controls = controls_value if controls_value != null else ""
		var key_mapping_value = row[17] if row.size() > 17 and row[17] != null else ""
		game.key_mapping = key_mapping_value if key_mapping_value != null else ""
		
		var arguments_value = row[18] if row.size() > 18 and row[18] != null else ""
		game.arguments = arguments_value if arguments_value != null else ""
	
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
	print("[DatabaseManager] 現在のDBバージョン: %d, 最新バージョン: %d" % [current_version, CURRENT_DB_VERSION])
	
	# バージョンが0の場合（新規作成時など）、最新バージョンを設定
	if current_version == 0:
		_set_db_version(CURRENT_DB_VERSION)
		return
	
	# マイグレーションが必要な場合
	if current_version < CURRENT_DB_VERSION:
		print("[DatabaseManager] マイグレーションを開始します: v%d -> v%d" % [current_version, CURRENT_DB_VERSION])
		
		# トランザクション開始
		db.query("BEGIN TRANSACTION")
		
		# バージョンごとにマイグレーションを実行
		# 例: v1 -> v2
		# if current_version < 2:
		# 	if _migrate_v1_to_v2():
		# 		current_version = 2
		# 	else:
		# 		db.query("ROLLBACK")
		# 		push_error("[DatabaseManager] マイグレーション(v1->v2)に失敗しました")
		# 		return
		
		# 最新バージョンに更新
		_set_db_version(CURRENT_DB_VERSION)
		
		# コミット
		db.query("COMMIT")
		print("[DatabaseManager] マイグレーションが完了しました")
