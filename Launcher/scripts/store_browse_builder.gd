## ストアブラウズ画面のUI構築ヘルパー
## セクションタイプに応じたUI要素を動的生成
## スライドショー/タイルグリッド/バナーは StoreBannerBuilder を参照

extends RefCounted
class_name StoreBrowseBuilder

const TILE_SIZE := Vector2(200, 200)
const TILE_GAP := 20
const SECTION_GAP := 40
const FEATURED_HEIGHT := 500
const FEATURED_PADDING := 40  # 左右余白
const BAR_WIDTH := 40
const BAR_HEIGHT := 6
const BAR_GAP := 8
const BAR_RADIUS := 3

# フォント
static var _font_regular: Font = null
static var _font_bold: Font = null

static func _get_font_regular() -> Font:
	if _font_regular == null:
		_font_regular = load("res://fonts/NotoSansJP-Regular.ttf")
	return _font_regular

static func _get_font_bold() -> Font:
	if _font_bold == null:
		_font_bold = load("res://fonts/NotoSansJP-Bold.ttf")
	return _font_bold

## ゲームの表示テキストを取得（display_textがあればそちら、なければゲームタイトル）
static func _get_display_text(game: GameInfo, section: StoreSectionInfo) -> String:
	if section.game_display_texts.has(game.game_id):
		var text = section.game_display_texts[game.game_id]
		if not text.is_empty():
			return text
	return game.title

## 通常セクション行を構築
static func build_normal_section(section: StoreSectionInfo, viewport_width: float) -> Control:
	var container = VBoxContainer.new()
	container.name = "Section_%d" % section.section_id
	container.add_theme_constant_override("separation", 12)

	# ヘッダー行（.tscn テンプレートを使用）
	var header = preload("res://scenes/components/store_section_header.tscn").instantiate()
	header.get_node("SectionTitle").text = section.title

	# 画面幅から表示上限を自動計算（タイルサイズ+間隔で割る）
	var available_width = viewport_width - FEATURED_PADDING * 2
	var max_tiles = int((available_width + TILE_GAP) / (TILE_SIZE.x + TILE_GAP))
	var effective_max = mini(max_tiles, section.max_display_count) if section.max_display_count > 0 else max_tiles
	var display_count = mini(section.games.size(), effective_max)

	# ゲーム数が画面に収まるなら「すべて見る」不要
	if section.games.size() > effective_max:
		var view_all_btn = header.get_node("ViewAllButton")
		view_all_btn.visible = true

	container.add_child(header)

	# サムネイル行
	var thumb_row = HBoxContainer.new()
	thumb_row.name = "ThumbnailRow"
	thumb_row.add_theme_constant_override("separation", TILE_GAP)
	for i in range(display_count):
		var game = section.games[i]
		var tile = _create_game_tile(game)
		thumb_row.add_child(tile)

	container.add_child(thumb_row)

	return container

## ゲームタイル（通常セクション用 200x200 + 下部タイトル）を作成
## 戻り値はVBoxContainer（Panel + Label）、フォーカス対象はPanel部分
## 画像は読み込まず、パスをメタデータに保持して遅延ロードに委ねる
static func _create_game_tile(game: GameInfo) -> Control:
	var wrapper = preload("res://scenes/components/store_game_tile.tscn").instantiate()
	wrapper.name = "Tile_%s" % game.game_id

	# サムネイル画像パスを解決してメタデータに保持（実際の読み込みは遅延）
	var thumb_path = _resolve_thumbnail_path(game)
	if not thumb_path.is_empty():
		wrapper.set_meta("image_path", thumb_path)
		# LOADINGラベルを追加
		var loading_label = _create_loading_label(16)
		wrapper.get_node("TilePanel").add_child(loading_label)
	else:
		# (#316) サムネ未登録 → no-image プレースホルダ（カルーセルと同デザイン）。TilePanel は角丸 16・
		# clip_children=AND_DRAW なので角丸に切り抜かれる。
		wrapper.get_node("TilePanel").add_child(NoImagePlaceholder.make(16, 18))

	# タイトル
	wrapper.get_node("TitleLabel").text = game.title

	return wrapper

## 遅延ロード中に表示する「LOADING」ラベルを生成
## 暗い背景 + LOADING テキストをまとめた Control を返す（名前は "LoadingLabel"）
static func _create_loading_label(font_size: int) -> Control:
	var wrapper = Control.new()
	wrapper.name = "LoadingLabel"
	wrapper.set_anchors_preset(Control.PRESET_FULL_RECT)
	wrapper.mouse_filter = Control.MOUSE_FILTER_IGNORE

	# 暗い背景（アイコンの上にかぶせる）
	var bg = ColorRect.new()
	bg.name = "DimBackground"
	bg.color = Color(0.08, 0.08, 0.08, 1.0)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	wrapper.add_child(bg)

	# LOADING テキスト
	var label = Label.new()
	label.name = "Text"
	label.text = "LOADING"
	label.add_theme_font_override("font", _get_font_bold())
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", Color(1, 1, 1, 0.5))
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.set_anchors_preset(Control.PRESET_FULL_RECT)
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	wrapper.add_child(label)

	return wrapper

## 「すべてのゲーム」ボタンを構築（.tscn テンプレートを使用）
static func build_all_games_button(viewport_width: float) -> Button:
	var btn = preload("res://scenes/components/store_all_games_button.tscn").instantiate()
	btn.custom_minimum_size = Vector2(viewport_width - FEATURED_PADDING * 2, 60)
	return btn

## サムネイルパスを解決
static func _resolve_thumbnail_path(game: GameInfo) -> String:
	if game.thumbnail_path.is_empty():
		return ""
	var path = PathManager.get_game_folder(game.game_id).path_join(game.thumbnail_path)
	if FileAccess.file_exists(path):
		return path
	return ""

## 背景画像パスを解決
static func _resolve_background_path(game: GameInfo) -> String:
	if game.background_path.is_empty():
		return ""
	var path = PathManager.get_game_folder(game.game_id).path_join(game.background_path)
	if FileAccess.file_exists(path):
		return path
	return ""
