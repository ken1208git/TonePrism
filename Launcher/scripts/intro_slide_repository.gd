## 初回説明スライド (intro_slides) のクエリを担当 (#253)
## DatabaseManager のDBインスタンスを使ってデータを取得する。
## Manager 側 (IntroSlideRepository.cs) が書き込み、Launcher は読み取り専用。

extends RefCounted
class_name IntroSlideRepository

var _db_manager: DatabaseManager

func _init(db_manager: DatabaseManager) -> void:
	_db_manager = db_manager

## 表示対象スライドを表示順 (display_order 昇順 → slide_id 昇順) で取得（is_visible = 1 のみ）
func get_visible_slides() -> Array[IntroSlideInfo]:
	if not _db_manager.is_open():
		if not _db_manager.open():
			return []

	var slides: Array[IntroSlideInfo] = []
	var query = "SELECT slide_id, display_order, body_text, image_path, is_visible FROM intro_slides WHERE is_visible = 1 ORDER BY display_order ASC, slide_id ASC"

	if _db_manager.db.query(query):
		var result_array = _db_manager.db.get_query_result()
		if result_array != null:
			for row in result_array:
				if row is Dictionary:
					slides.append(_create_slide_from_row(row))
	else:
		push_error("[IntroSlideRepository] スライド情報の取得に失敗しました")

	return slides

## 表示対象スライドが1件以上存在するか（screensaver の遷移先ルーティング用の軽量チェック）。
## 全件読まずに COUNT で判定し、空なら screensaver が intro_guide を挟まず直接ストアへ遷移する。
func has_visible_slides() -> bool:
	if not _db_manager.is_open():
		if not _db_manager.open():
			return false

	var query = "SELECT COUNT(*) AS cnt FROM intro_slides WHERE is_visible = 1"
	if _db_manager.db.query(query):
		var result_array = _db_manager.db.get_query_result()
		if result_array != null and result_array.size() > 0 and result_array[0] is Dictionary:
			return _db_manager.safe_int(result_array[0].get("cnt"), 0) > 0
	else:
		push_error("[IntroSlideRepository] スライド件数の取得に失敗しました")

	return false

## データベースの行から IntroSlideInfo を作成
func _create_slide_from_row(row: Dictionary) -> IntroSlideInfo:
	var slide = IntroSlideInfo.new()

	slide.slide_id = _db_manager.safe_int(row.get("slide_id"), -1)
	slide.display_order = _db_manager.safe_int(row.get("display_order"), -1)
	# (#318) Manager の WinForms TextBox は改行を CRLF (\r\n) で保存するが、Godot の Label は \r を
	# 独立した改行として描画するため CRLF が 2 行ぶんに見える。game_repository.description と同様、
	# 読み込み層で LF へ正規化しておけば body_text の全表示経路（現状 _make_body_label 経由、将来の
	# 別経路も）を一括で守れる。
	slide.body_text = str(row.get("body_text", "")).replace("\r\n", "\n").replace("\r", "\n")

	# image_path は Manager 側で「画像なし → NULL」に正規化される。godot-sqlite は NULL を
	# Variant null で返すため、str(null) == "<null>" になるのを避けて明示的に空文字へ畳む。
	var img = row.get("image_path")
	slide.image_path = "" if img == null else str(img)

	slide.is_visible = _db_manager.safe_bool(row.get("is_visible"), true)

	return slide
