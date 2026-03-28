## ストアブラウズ画面のUI構築ヘルパー
## セクションタイプに応じたUI要素を動的生成

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

## スライドショーセクションを構築
static func build_slideshow_section(section: StoreSectionInfo, viewport_width: float) -> Control:
	var container = Control.new()
	container.name = "Slideshow_%d" % section.section_id
	var banner_width = viewport_width - FEATURED_PADDING * 2
	container.custom_minimum_size = Vector2(banner_width, FEATURED_HEIGHT + 60)
	container.size = Vector2(banner_width, FEATURED_HEIGHT + 60)

	if section.games.is_empty():
		return container

	# バナークリップ領域（角丸窓）
	var clip_panel = Panel.new()
	clip_panel.name = "BannerClip"
	clip_panel.position = Vector2.ZERO
	clip_panel.size = Vector2(banner_width, FEATURED_HEIGHT)
	clip_panel.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW
	var clip_style = StyleBoxFlat.new()
	clip_style.bg_color = Color(0.1, 0.1, 0.1)
	clip_style.set_corner_radius_all(16)
	clip_panel.add_theme_stylebox_override("panel", clip_style)
	container.add_child(clip_panel)

	# バナー画像をクリップ領域内に配置
	for i in range(section.games.size()):
		var game = section.games[i]
		var banner = _create_banner(game, Vector2(banner_width, FEATURED_HEIGHT))
		banner.name = "Banner_%d" % i
		banner.position = Vector2.ZERO
		banner.visible = (i == 0)
		# スライドショーではバナーの角丸を外す（窓側で制御）
		var banner_style = banner.get_theme_stylebox("panel") as StyleBoxFlat
		if banner_style:
			banner_style.set_corner_radius_all(0)
		banner.clip_children = CanvasItem.CLIP_CHILDREN_DISABLED
		banner.mouse_filter = Control.MOUSE_FILTER_IGNORE
		# バナー内蔵タイトルを非表示（SlideshowTitleScrollで一元管理）
		var inner_title_scroll = banner.get_node_or_null("TitleScroll")
		if inner_title_scroll:
			inner_title_scroll.visible = false
		clip_panel.add_child(banner)

	# バーインジケーター（ゲージ付き）
	var bars = HBoxContainer.new()
	bars.name = "BarIndicator"
	bars.add_theme_constant_override("separation", BAR_GAP)
	for i in range(section.games.size()):
		var bar = Panel.new()
		bar.name = "Bar_%d" % i
		bar.custom_minimum_size = Vector2(BAR_WIDTH, BAR_HEIGHT)
		bar.size = Vector2(BAR_WIDTH, BAR_HEIGHT)
		var bar_style = StyleBoxFlat.new()
		bar_style.bg_color = Color(1, 1, 1, 0.3)
		bar_style.set_corner_radius_all(BAR_RADIUS)
		bar.add_theme_stylebox_override("panel", bar_style)
		bar.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW
		# フィルバー（ゲージ進行部分）- ColorRectで描画
		var fill = ColorRect.new()
		fill.name = "Fill"
		fill.position = Vector2.ZERO
		fill.size = Vector2(0, BAR_HEIGHT)
		fill.color = Color.WHITE
		bar.add_child(fill)
		bars.add_child(bar)

	var total_width = section.games.size() * BAR_WIDTH + (section.games.size() - 1) * BAR_GAP
	bars.position = Vector2(banner_width / 2 - total_width / 2.0, FEATURED_HEIGHT + 15)
	bars.mouse_filter = Control.MOUSE_FILTER_IGNORE
	container.add_child(bars)

	# 下部グラデーションオーバーレイ（スライドショー用、バナーの上にかぶせる）
	var ss_gradient_height := FEATURED_HEIGHT * 0.35
	var ss_gradient = Panel.new()
	ss_gradient.name = "SlideshowGradient"
	ss_gradient.position = Vector2(0, FEATURED_HEIGHT - ss_gradient_height)
	ss_gradient.size = Vector2(banner_width, ss_gradient_height)
	ss_gradient.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var ss_shader_code = """
shader_type canvas_item;
void fragment() {
	float alpha = smoothstep(0.0, 1.0, UV.y);
	COLOR = vec4(0.0, 0.0, 0.0, alpha * 0.85 * COLOR.a);
}
"""
	var ss_shader = Shader.new()
	ss_shader.code = ss_shader_code
	var ss_mat = ShaderMaterial.new()
	ss_mat.shader = ss_shader
	ss_gradient.material = ss_mat
	var ss_grad_style = StyleBoxFlat.new()
	ss_grad_style.bg_color = Color(1, 1, 1, 1)
	ss_grad_style.corner_radius_bottom_left = 16
	ss_grad_style.corner_radius_bottom_right = 16
	ss_gradient.add_theme_stylebox_override("panel", ss_grad_style)
	clip_panel.add_child(ss_gradient)

	# タイトルオーバーレイ（AutoScrollContainer内、BannerClip内でクリップ）
	var title_scroll = preload("res://scenes/components/auto_scroll_container.gd").new()
	title_scroll.name = "SlideshowTitleScroll"
	title_scroll.position = Vector2(30, FEATURED_HEIGHT - 60)
	title_scroll.size = Vector2(banner_width - 60, 48)
	title_scroll.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var title_label = Label.new()
	title_label.name = "SlideshowTitle"
	title_label.text = _get_display_text(section.games[0], section)
	title_label.add_theme_font_override("font", _get_font_bold())
	title_label.add_theme_font_size_override("font_size", 36)
	title_label.add_theme_color_override("font_color", Color.WHITE)
	title_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	title_scroll.add_child(title_label)
	clip_panel.add_child(title_scroll)

	# 左右矢印ボタン（マウスクリック対応）
	if section.games.size() > 1:
		var arrow_size := Vector2(48, 48)
		var arrow_y := FEATURED_HEIGHT / 2.0 - arrow_size.y / 2.0

		var prev_btn = Button.new()
		prev_btn.name = "SlideshowPrev"
		prev_btn.text = "◀"
		prev_btn.flat = true
		prev_btn.position = Vector2(12, arrow_y)
		prev_btn.custom_minimum_size = arrow_size
		prev_btn.size = arrow_size
		prev_btn.focus_mode = Control.FOCUS_NONE
		prev_btn.mouse_filter = Control.MOUSE_FILTER_STOP
		prev_btn.add_theme_font_size_override("font_size", 28)
		prev_btn.add_theme_color_override("font_color", Color(1, 1, 1, 0.7))
		prev_btn.add_theme_color_override("font_hover_color", Color.WHITE)
		# 半透明背景
		var prev_style = StyleBoxFlat.new()
		prev_style.bg_color = Color(0, 0, 0, 0.4)
		prev_style.set_corner_radius_all(24)
		prev_btn.add_theme_stylebox_override("normal", prev_style)
		var prev_hover = StyleBoxFlat.new()
		prev_hover.bg_color = Color(0, 0, 0, 0.6)
		prev_hover.set_corner_radius_all(24)
		prev_btn.add_theme_stylebox_override("hover", prev_hover)
		var prev_pressed = StyleBoxFlat.new()
		prev_pressed.bg_color = Color(0, 0, 0, 0.7)
		prev_pressed.set_corner_radius_all(24)
		prev_btn.add_theme_stylebox_override("pressed", prev_pressed)
		container.add_child(prev_btn)

		var next_btn = Button.new()
		next_btn.name = "SlideshowNext"
		next_btn.text = "▶"
		next_btn.flat = true
		next_btn.position = Vector2(banner_width - arrow_size.x - 12, arrow_y)
		next_btn.custom_minimum_size = arrow_size
		next_btn.size = arrow_size
		next_btn.focus_mode = Control.FOCUS_NONE
		next_btn.mouse_filter = Control.MOUSE_FILTER_STOP
		next_btn.add_theme_font_size_override("font_size", 28)
		next_btn.add_theme_color_override("font_color", Color(1, 1, 1, 0.7))
		next_btn.add_theme_color_override("font_hover_color", Color.WHITE)
		var next_style = StyleBoxFlat.new()
		next_style.bg_color = Color(0, 0, 0, 0.4)
		next_style.set_corner_radius_all(24)
		next_btn.add_theme_stylebox_override("normal", next_style)
		var next_hover = StyleBoxFlat.new()
		next_hover.bg_color = Color(0, 0, 0, 0.6)
		next_hover.set_corner_radius_all(24)
		next_btn.add_theme_stylebox_override("hover", next_hover)
		var next_pressed = StyleBoxFlat.new()
		next_pressed.bg_color = Color(0, 0, 0, 0.7)
		next_pressed.set_corner_radius_all(24)
		next_btn.add_theme_stylebox_override("pressed", next_pressed)
		container.add_child(next_btn)

	return container

## タイルグリッドセクションを構築
static func build_tile_grid_section(section: StoreSectionInfo, viewport_width: float) -> Control:
	var container = HBoxContainer.new()
	container.name = "TileGrid_%d" % section.section_id
	container.add_theme_constant_override("separation", TILE_GAP)

	var available_width = viewport_width - FEATURED_PADDING * 2
	var tile_count = mini(section.games.size(), mini(section.max_display_count, 3))

	if tile_count == 0:
		return container

	var tile_width = (available_width - TILE_GAP * (tile_count - 1)) / tile_count
	var tile_height: float
	if tile_count == 1:
		tile_height = FEATURED_HEIGHT
	else:
		tile_height = tile_width * 9.0 / 16.0  # 16:9比率

	for i in range(tile_count):
		var game = section.games[i]
		var custom_text = _get_display_text(game, section)
		var tile = _create_banner(game, Vector2(tile_width, tile_height), custom_text)
		tile.name = "GridTile_%d" % i
		tile.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		container.add_child(tile)

	container.custom_minimum_size.y = tile_height

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

## バナー（スライドショー/タイルグリッド用）を作成
## custom_text: 空でなければゲームタイトルの代わりに表示
static func _create_banner(game: GameInfo, banner_size: Vector2, custom_text: String = "") -> Panel:
	var banner = Panel.new()
	banner.custom_minimum_size = banner_size
	banner.size = banner_size
	banner.focus_mode = Control.FOCUS_ALL

	# 背景スタイル
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.1, 0.1, 0.1)
	style.set_corner_radius_all(16)
	banner.add_theme_stylebox_override("panel", style)
	banner.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW

	# 背景画像
	var tex_rect = TextureRect.new()
	tex_rect.name = "BackgroundImage"
	tex_rect.position = Vector2.ZERO
	tex_rect.size = banner_size
	tex_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	tex_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	tex_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var bg_path = _resolve_background_path(game)
	if not bg_path.is_empty():
		var image = Image.load_from_file(bg_path)
		if image != null:
			tex_rect.texture = ImageTexture.create_from_image(image)

	banner.add_child(tex_rect)

	# 下部グラデーションオーバーレイ
	var gradient_height := banner_size.y * 0.35
	var gradient_panel = Panel.new()
	gradient_panel.name = "GradientOverlay"
	gradient_panel.position = Vector2(0, banner_size.y - gradient_height)
	gradient_panel.size = Vector2(banner_size.x, gradient_height)
	gradient_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# GDShaderで上から透明→下が黒のグラデーション（控えめ）
	var shader_code = """
shader_type canvas_item;
void fragment() {
	float alpha = smoothstep(0.0, 1.0, UV.y);
	COLOR = vec4(0.0, 0.0, 0.0, alpha * 0.5 * COLOR.a);
}
"""
	var shader = Shader.new()
	shader.code = shader_code
	var mat = ShaderMaterial.new()
	mat.shader = shader
	gradient_panel.material = mat
	# 角丸に合わせるためStyleBoxを透明に
	var grad_style = StyleBoxFlat.new()
	grad_style.bg_color = Color(1, 1, 1, 1)  # シェーダーが上書きするので白でOK
	grad_style.corner_radius_bottom_left = 16
	grad_style.corner_radius_bottom_right = 16
	gradient_panel.add_theme_stylebox_override("panel", grad_style)
	banner.add_child(gradient_panel)

	# タイトルラベル（AutoScrollContainer内、グラデーション上に配置）
	var title_scroll = preload("res://scenes/components/auto_scroll_container.gd").new()
	title_scroll.name = "TitleScroll"
	title_scroll.position = Vector2(24, banner_size.y - 56)
	title_scroll.size = Vector2(banner_size.x - 48, 48)
	title_scroll.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var title_label = Label.new()
	title_label.name = "TitleLabel"
	title_label.text = custom_text if not custom_text.is_empty() else game.title
	title_label.add_theme_font_override("font", _get_font_bold())
	title_label.add_theme_font_size_override("font_size", 32)
	title_label.add_theme_color_override("font_color", Color.WHITE)
	title_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	title_scroll.add_child(title_label)
	banner.add_child(title_scroll)

	return banner

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
