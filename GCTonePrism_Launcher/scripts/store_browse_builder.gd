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

	# ヘッダー行: セクション名 + 「すべて見る →」
	var header = HBoxContainer.new()
	header.add_theme_constant_override("separation", 20)

	var title_label = Label.new()
	title_label.text = section.title
	title_label.add_theme_font_override("font", _get_font_bold())
	title_label.add_theme_font_size_override("font_size", 28)
	title_label.add_theme_color_override("font_color", Color.WHITE)
	header.add_child(title_label)

	var spacer = Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(spacer)

	# 画面幅から表示上限を自動計算（タイルサイズ+間隔で割る）
	var available_width = viewport_width - FEATURED_PADDING * 2
	var max_tiles = int((available_width + TILE_GAP) / (TILE_SIZE.x + TILE_GAP))
	var effective_max = mini(max_tiles, section.max_display_count) if section.max_display_count > 0 else max_tiles
	var display_count = mini(section.games.size(), effective_max)

	# ゲーム数が画面に収まるなら「すべて見る」不要
	if section.games.size() > effective_max:
		var view_all_btn = Button.new()
		view_all_btn.name = "ViewAllButton"
		view_all_btn.text = "すべて見る →"
		view_all_btn.flat = true
		view_all_btn.add_theme_font_override("font", _get_font_regular())
		view_all_btn.add_theme_font_size_override("font_size", 20)
		view_all_btn.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
		view_all_btn.add_theme_color_override("font_hover_color", Color.WHITE)
		view_all_btn.focus_mode = Control.FOCUS_ALL
		header.add_child(view_all_btn)

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
static func _create_game_tile(game: GameInfo) -> Control:
	var wrapper = VBoxContainer.new()
	wrapper.name = "Tile_%s" % game.game_id
	wrapper.add_theme_constant_override("separation", 6)

	# サムネイルパネル
	var tile = Panel.new()
	tile.name = "TilePanel"
	tile.custom_minimum_size = TILE_SIZE
	tile.focus_mode = Control.FOCUS_ALL

	# 背景スタイル
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.15, 0.15, 0.15)
	style.set_corner_radius_all(16)
	tile.add_theme_stylebox_override("panel", style)
	tile.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW

	# サムネイル画像
	var tex_rect = TextureRect.new()
	tex_rect.name = "Thumbnail"
	tex_rect.position = Vector2.ZERO
	tex_rect.size = TILE_SIZE
	tex_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	tex_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED

	var thumb_path = _resolve_thumbnail_path(game)
	if not thumb_path.is_empty():
		var image = Image.load_from_file(thumb_path)
		if image != null:
			tex_rect.texture = ImageTexture.create_from_image(image)

	tile.add_child(tex_rect)
	wrapper.add_child(tile)

	# タイトルラベル（アイコン下、中央揃え）
	var title_label = Label.new()
	title_label.name = "TitleLabel"
	title_label.text = game.title
	title_label.add_theme_font_override("font", _get_font_regular())
	title_label.add_theme_font_size_override("font_size", 14)
	title_label.add_theme_color_override("font_color", Color(0.85, 0.85, 0.85))
	title_label.custom_minimum_size = Vector2(TILE_SIZE.x, 0)
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title_label.clip_text = true
	wrapper.add_child(title_label)

	return wrapper

## 「すべてのゲーム」ボタンを構築
static func build_all_games_button(viewport_width: float) -> Button:
	var btn = Button.new()
	btn.name = "AllGamesButton"
	btn.text = "すべてのゲーム →"
	btn.add_theme_font_override("font", _get_font_bold())
	btn.add_theme_font_size_override("font_size", 22)
	btn.add_theme_color_override("font_color", Color(0.9, 0.9, 0.9))
	btn.add_theme_color_override("font_hover_color", Color.WHITE)
	btn.custom_minimum_size = Vector2(viewport_width - FEATURED_PADDING * 2, 60)
	btn.focus_mode = Control.FOCUS_ALL
	# スタイル
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.2, 0.2, 0.2, 0.6)
	style.set_corner_radius_all(12)
	btn.add_theme_stylebox_override("normal", style)
	var hover_style = StyleBoxFlat.new()
	hover_style.bg_color = Color(0.3, 0.3, 0.3, 0.8)
	hover_style.set_corner_radius_all(12)
	btn.add_theme_stylebox_override("hover", hover_style)
	var pressed_style = StyleBoxFlat.new()
	pressed_style.bg_color = Color(0.25, 0.25, 0.25, 0.9)
	pressed_style.set_corner_radius_all(12)
	btn.add_theme_stylebox_override("pressed", pressed_style)
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
