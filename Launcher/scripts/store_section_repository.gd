## ストアセクションのクエリを担当
## DatabaseManager のDBインスタンスを使ってセクションデータを取得する

extends RefCounted
class_name StoreSectionRepository

var _db_manager: DatabaseManager
var _game_repo: GameRepository

func _init(db_manager: DatabaseManager, game_repo: GameRepository) -> void:
	_db_manager = db_manager
	_game_repo = game_repo

## ストアセクション一覧を取得
func get_store_sections() -> Array[StoreSectionInfo]:
	if not _db_manager.is_open():
		if not _db_manager.open():
			return []

	var sections: Array[StoreSectionInfo] = []

	var query = """
		SELECT section_id, title, section_type, section_source,
		       display_order, max_display_count, is_visible
		FROM store_sections
		WHERE is_visible = 1
		ORDER BY display_order ASC, section_id ASC
	"""

	if _db_manager.db.query(query):
		var result_array = _db_manager.db.get_query_result()
		if result_array != null:
			for row in result_array:
				if row is Dictionary:
					var section = StoreSectionInfo.new()
					section.section_id = _db_manager.safe_int(row.get("section_id"), -1)
					section.title = str(row.get("title", ""))
					section.section_type = _db_manager.safe_int(row.get("section_type"), 0)
					section.section_source = str(row.get("section_source", "manual"))
					section.display_order = _db_manager.safe_int(row.get("display_order"), 0)
					section.max_display_count = _db_manager.safe_int(row.get("max_display_count"), 5)
					section.is_visible = _db_manager.safe_bool(row.get("is_visible"), true)

					section.games = _get_games_for_section(section)
					if not section.games.is_empty():
						sections.append(section)
	else:
		push_error("[StoreSectionRepository] ストアセクションの取得に失敗しました")

	return sections

## (#315) 可視な店セクションが1つでも存在するか（軽量チェック）。入口ルーティング (StoreEntryRouter) で
## 「空ストアなら store_browse を挟まずカルーセル直行」を判断するのに使う。get_store_sections と違い
## games の有無や developer まではロードしない（中身が空/0タイルのセクションは store_browse 側の
## fallback が拾う）ため、入口判定の二重ロードを避けられる。
func has_visible_sections() -> bool:
	if not _db_manager.is_open():
		if not _db_manager.open():
			return false
	if _db_manager.db.query("SELECT 1 FROM store_sections WHERE is_visible = 1 LIMIT 1"):
		var result = _db_manager.db.get_query_result()
		return result != null and result.size() > 0
	return false

## セクションソースに応じてゲーム一覧を取得
func _get_games_for_section(section: StoreSectionInfo) -> Array[GameInfo]:
	# (#278 ②) DB が閉じていたら開く。store_browse は表示中 DB を閉じておき「すべて見る」
	# (get_all_games_for_section) で初めて再接続する経路があるため、game_repository / get_store_sections と
	# 同じ自動再接続ガードを置く（無いと close 後に _db_manager.db=null を叩いて空配列/エラーになる）。
	if not _db_manager.is_open():
		if not _db_manager.open():
			return []
	var source = section.section_source
	var query: String = ""
	var bindings: Array = []

	# フィルター系ソース (genre/players_*/difficulty/play_time/online/controller) の並びは
	# `release_year DESC, title ASC` = 「なるべく最新の制作年を頭に + 同年内は名前順」(ユーザー要望)。
	# games には作成日時列が無く release_year が唯一の新しさ指標 (年単位なので "なるべく")。release_year=NULL は
	# DESC で末尾に来る (= 不明年は最後)。manual=割当順 / popular・recently_played=ランキング / random=シャッフル /
	# 単年フィルター (recent=今年・release_year:YYYY) は「最新が頭」が無意味なので名前順 (title)。(#329, 旧: display_order 順)

	if source == "manual":
		query = """
			SELECT g.*, ssg.display_text FROM games g
			JOIN store_section_games ssg ON g.game_id = ssg.game_id
			WHERE ssg.section_id = ? AND g.is_visible = 1
			ORDER BY ssg.display_order ASC
		"""
		bindings = [section.section_id]
	elif source == "popular":
		# (#297 PR1) play_records テーブルは DB v23 で撤去したため JOIN 不可。PR1 は暫定で「表示ゲームを安定順」で返す
		# (順位は仮)。PR2 で responses/play_records/ の in-memory 集計 (play_stats_service) に差し替えて実データ化する。
		query = """
			SELECT * FROM games
			WHERE is_visible = 1
			ORDER BY title ASC
		"""
	elif source == "recent":
		var current_year = Time.get_date_dict_from_system()["year"]
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND release_year = ?
			ORDER BY title ASC
		"""
		bindings = [current_year]
	elif source == "recently_played":
		# (#297 PR1) play_records 撤去によりプレイ履歴データが無い → 0 行を返す。get_store_sections は空セクションを
		# 表示一覧から落とすため、データが揃うまで「最近プレイ」セクションは自動的に非表示になる (UI 非破壊)。
		# PR2 で responses/ の in-memory 集計 (最新 start_time 上位 N) に差し替えて実データ化する。
		query = """
			SELECT * FROM games WHERE 1 = 0
		"""
	elif source.begins_with("genre:"):
		var genre_name = source.substr(6)
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND genre LIKE ?
			ORDER BY release_year DESC, title ASC
		"""
		bindings = ["%" + genre_name + "%"]
	elif source.begins_with("players_min:"):
		var n = int(source.substr(12))
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND min_players <= ? AND max_players >= ?
			ORDER BY release_year DESC, title ASC
		"""
		bindings = [n, n]
	elif source.begins_with("players_max:"):
		var n = int(source.substr(12))
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND max_players <= ?
			ORDER BY release_year DESC, title ASC
		"""
		bindings = [n]
	elif source.begins_with("difficulty:"):
		var n = int(source.substr(11))
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND difficulty = ?
			ORDER BY release_year DESC, title ASC
		"""
		bindings = [n]
	elif source.begins_with("play_time:"):
		var n = int(source.substr(10))
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND play_time = ?
			ORDER BY release_year DESC, title ASC
		"""
		bindings = [n]
	elif source == "online":
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND supported_connection > 0
			ORDER BY release_year DESC, title ASC
		"""
	elif source == "random":
		query = """
			SELECT * FROM games
			WHERE is_visible = 1
			ORDER BY RANDOM()
		"""
	elif source == "controller":
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND controller_support = 1
			ORDER BY release_year DESC, title ASC
		"""
	elif source.begins_with("release_year:"):
		# (#291) 制作年指定: 指定した release_year のゲームを全件 (名前順)。新作(recent)が「今年固定」なのに対し
		# こちらは任意年を指定できる汎用フィルター。max_display_count はグレーアウト=0(全件) で保存される (Manager #211)。
		var year = int(source.substr(13))
		query = """
			SELECT * FROM games
			WHERE is_visible = 1 AND release_year = ?
			ORDER BY title ASC
		"""
		bindings = [year]
	else:
		return []

	var games: Array[GameInfo] = []
	var is_manual = (source == "manual")
	var success = false
	if bindings.is_empty():
		success = _db_manager.db.query(query)
	else:
		success = _db_manager.db.query_with_bindings(query, bindings)

	if success:
		var result_array = _db_manager.db.get_query_result()
		if result_array != null:
			for row in result_array:
				if row is Dictionary:
					var game = _game_repo._create_game_info_from_row_dict(row)
					if game != null:
						game.developers = _game_repo.get_developers_by_game_id(game.game_id)
						games.append(game)
						if is_manual:
							var display_text = str(row.get("display_text", ""))
							if not display_text.is_empty():
								section.game_display_texts[game.game_id] = display_text
	return games

## セクションの全ゲームを取得（カルーセル遷移時用）
func get_all_games_for_section(section: StoreSectionInfo) -> Array[GameInfo]:
	return _get_games_for_section(section)
