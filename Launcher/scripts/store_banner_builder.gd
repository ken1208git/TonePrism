## ストアブラウズ画面のバナー・スライドショー・タイルグリッド構築
## StoreBrowseBuilder から分離した大型セクションの構築を担当

extends RefCounted
class_name StoreBannerBuilder

## スライドショーセクションを構築
static func build_slideshow_section(section: StoreSectionInfo, viewport_width: float) -> Control:
	var container = Control.new()
	container.name = "Slideshow_%d" % section.section_id
	var banner_width = viewport_width - StoreBrowseBuilder.FEATURED_PADDING * 2
	container.custom_minimum_size = Vector2(banner_width, StoreBrowseBuilder.FEATURED_HEIGHT + 60)
	container.size = Vector2(banner_width, StoreBrowseBuilder.FEATURED_HEIGHT + 60)

	if section.games.is_empty():
		return container

	# (#212 改 / #211) スライド枚数の上限。手動は max=0 保存=全件 (従来どおり)。ランダム等を許可したので
	# max>0 のときは枚数を絞り「特集 N 枚」が成立するようにする (ガードが無いと全ゲームが延々流れて特集にならない)。
	var slide_count = section.games.size()
	if section.max_display_count > 0:
		slide_count = mini(slide_count, section.max_display_count)
	# 実際に生成するスライド枚数を container に持たせる。store_browse の _switch_slide が wrap-around に使い、
	# section.games.size() で割って存在しない Banner_* に飛ぶのを防ぐ (max で枚数を絞ったランダム等で重要)。
	container.set_meta("slide_count", slide_count)

	# バナークリップ領域（角丸窓）
	var clip_panel = Panel.new()
	clip_panel.name = "BannerClip"
	clip_panel.position = Vector2.ZERO
	clip_panel.size = Vector2(banner_width, StoreBrowseBuilder.FEATURED_HEIGHT)
	clip_panel.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW
	var clip_style = StyleBoxFlat.new()
	clip_style.bg_color = Color(0.1, 0.1, 0.1)
	clip_style.set_corner_radius_all(16)
	clip_panel.add_theme_stylebox_override("panel", clip_style)
	container.add_child(clip_panel)

	# バナー画像をクリップ領域内に配置
	for i in range(slide_count):
		var game = section.games[i]
		var banner = create_banner(game, Vector2(banner_width, StoreBrowseBuilder.FEATURED_HEIGHT))
		banner.name = "Banner_%d" % i
		banner.position = Vector2.ZERO
		banner.visible = (i == 0)
		var banner_style = banner.get_theme_stylebox("panel") as StyleBoxFlat
		if banner_style:
			banner_style.set_corner_radius_all(0)
		banner.clip_children = CanvasItem.CLIP_CHILDREN_DISABLED
		banner.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var inner_title_scroll = banner.get_node_or_null("TitleScroll")
		if inner_title_scroll:
			inner_title_scroll.visible = false
		clip_panel.add_child(banner)

	# バーインジケーター
	var bars = HBoxContainer.new()
	bars.name = "BarIndicator"
	bars.add_theme_constant_override("separation", StoreBrowseBuilder.BAR_GAP)
	for i in range(slide_count):
		var bar = Panel.new()
		bar.name = "Bar_%d" % i
		bar.custom_minimum_size = Vector2(StoreBrowseBuilder.BAR_WIDTH, StoreBrowseBuilder.BAR_HEIGHT)
		bar.size = Vector2(StoreBrowseBuilder.BAR_WIDTH, StoreBrowseBuilder.BAR_HEIGHT)
		var bar_style = StyleBoxFlat.new()
		bar_style.bg_color = Color(1, 1, 1, 0.3)
		bar_style.set_corner_radius_all(StoreBrowseBuilder.BAR_RADIUS)
		bar.add_theme_stylebox_override("panel", bar_style)
		bar.clip_children = CanvasItem.CLIP_CHILDREN_AND_DRAW
		var fill = ColorRect.new()
		fill.name = "Fill"
		fill.position = Vector2.ZERO
		fill.size = Vector2(0, StoreBrowseBuilder.BAR_HEIGHT)
		fill.color = Color.WHITE
		bar.add_child(fill)
		bars.add_child(bar)

	var total_width = slide_count * StoreBrowseBuilder.BAR_WIDTH + (slide_count - 1) * StoreBrowseBuilder.BAR_GAP
	bars.position = Vector2(banner_width / 2 - total_width / 2.0, StoreBrowseBuilder.FEATURED_HEIGHT + 15)
	bars.mouse_filter = Control.MOUSE_FILTER_IGNORE
	container.add_child(bars)

	# 下部グラデーションオーバーレイ（シェーダーは .tres で定義済み）
	var ss_gradient_height := StoreBrowseBuilder.FEATURED_HEIGHT * 0.35
	var ss_gradient = Panel.new()
	ss_gradient.name = "SlideshowGradient"
	ss_gradient.position = Vector2(0, StoreBrowseBuilder.FEATURED_HEIGHT - ss_gradient_height)
	ss_gradient.size = Vector2(banner_width, ss_gradient_height)
	ss_gradient.mouse_filter = Control.MOUSE_FILTER_IGNORE
	ss_gradient.material = preload("res://shaders/gradient_overlay_strong_material.tres")
	var ss_grad_style = StyleBoxFlat.new()
	ss_grad_style.bg_color = Color(1, 1, 1, 1)
	ss_grad_style.corner_radius_bottom_left = 16
	ss_grad_style.corner_radius_bottom_right = 16
	ss_gradient.add_theme_stylebox_override("panel", ss_grad_style)
	clip_panel.add_child(ss_gradient)

	# タイトルオーバーレイ
	var title_scroll = preload("res://scenes/components/auto_scroll_container.gd").new()
	title_scroll.name = "SlideshowTitleScroll"
	title_scroll.position = Vector2(30, StoreBrowseBuilder.FEATURED_HEIGHT - 60)
	title_scroll.size = Vector2(banner_width - 60, 48)
	title_scroll.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var title_label = Label.new()
	title_label.name = "SlideshowTitle"
	title_label.text = StoreBrowseBuilder._get_display_text(section.games[0], section)
	title_label.add_theme_font_override("font", StoreBrowseBuilder._get_font_bold())
	title_label.add_theme_font_size_override("font_size", 36)
	title_label.add_theme_color_override("font_color", Color.WHITE)
	title_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	title_scroll.add_child(title_label)
	clip_panel.add_child(title_scroll)

	# 左右矢印ボタン
	if slide_count > 1:
		_add_arrow_buttons(container, banner_width)

	return container

## タイルグリッドセクションを構築
static func build_tile_grid_section(section: StoreSectionInfo, viewport_width: float) -> Control:
	var container = HBoxContainer.new()
	container.name = "TileGrid_%d" % section.section_id
	container.add_theme_constant_override("separation", StoreBrowseBuilder.TILE_GAP)

	var available_width = viewport_width - StoreBrowseBuilder.FEATURED_PADDING * 2
	# (#211 fix) max_display_count<=0 は「上限なし」(=タイルグリッドの物理上限 3 枚) と解釈する。手動/フィルター系
	# ソースは 0 保存される (Manager #211) ため、ガード無しの mini(0,3)=0 だと tile_count=0 で 1 枚も出ない空グリッド
	# になっていた (= #212 でタイルグリッドが手動固定 → 全タイルグリッドが空表示になる回帰)。通常セクションの
	# `>0 else max_tiles` ガードと同じ扱いに揃える。ランダム等で max>0 のときは従来どおり mini(max,3)。
	var cap = 3 if section.max_display_count <= 0 else mini(section.max_display_count, 3)
	var tile_count = mini(section.games.size(), cap)

	if tile_count == 0:
		return container

	var tile_width = (available_width - StoreBrowseBuilder.TILE_GAP * (tile_count - 1)) / tile_count
	var tile_height: float
	if tile_count == 1:
		tile_height = StoreBrowseBuilder.FEATURED_HEIGHT
	else:
		tile_height = tile_width * 9.0 / 16.0

	for i in range(tile_count):
		var game = section.games[i]
		var custom_text = StoreBrowseBuilder._get_display_text(game, section)
		var tile = create_banner(game, Vector2(tile_width, tile_height), custom_text)
		tile.name = "GridTile_%d" % i
		tile.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		container.add_child(tile)

	container.custom_minimum_size.y = tile_height

	return container

## バナー（スライドショー/タイルグリッド用）を作成
static func create_banner(game: GameInfo, banner_size: Vector2, custom_text: String = "") -> Panel:
	var banner = Panel.new()
	banner.custom_minimum_size = banner_size
	banner.size = banner_size
	banner.focus_mode = Control.FOCUS_ALL

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

	var bg_path = StoreBrowseBuilder._resolve_background_path(game)
	if not bg_path.is_empty():
		banner.set_meta("image_path", bg_path)

	banner.add_child(tex_rect)

	# 画像パスがある場合、LOADINGラベルを追加（遅延ロード中の表示）
	if not bg_path.is_empty():
		var loading_label = StoreBrowseBuilder._create_loading_label(24)
		banner.add_child(loading_label)
	else:
		# (#316) 背景画像なし → no-image プレースホルダ（カルーセルと見た目を揃えた灰箱）。グラデーション/タイトルより
		# 前に add するので、それらは後から上に重なる（タイトルは下部の暗グラデ上＝白文字でも読める）。
		banner.add_child(NoImagePlaceholder.make(16, 40))

	# 下部グラデーションオーバーレイ（シェーダーは .tres で定義済み）
	var gradient_height := banner_size.y * 0.35
	var gradient_panel = Panel.new()
	gradient_panel.name = "GradientOverlay"
	gradient_panel.position = Vector2(0, banner_size.y - gradient_height)
	gradient_panel.size = Vector2(banner_size.x, gradient_height)
	gradient_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	gradient_panel.material = preload("res://shaders/gradient_overlay_material.tres")
	var grad_style = StyleBoxFlat.new()
	grad_style.bg_color = Color(1, 1, 1, 1)
	grad_style.corner_radius_bottom_left = 16
	grad_style.corner_radius_bottom_right = 16
	gradient_panel.add_theme_stylebox_override("panel", grad_style)
	banner.add_child(gradient_panel)

	# タイトルラベル
	var title_scroll = preload("res://scenes/components/auto_scroll_container.gd").new()
	title_scroll.name = "TitleScroll"
	title_scroll.position = Vector2(24, banner_size.y - 56)
	title_scroll.size = Vector2(banner_size.x - 48, 48)
	title_scroll.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var title_label = Label.new()
	title_label.name = "TitleLabel"
	title_label.text = custom_text if not custom_text.is_empty() else game.title
	title_label.add_theme_font_override("font", StoreBrowseBuilder._get_font_bold())
	title_label.add_theme_font_size_override("font_size", 32)
	title_label.add_theme_color_override("font_color", Color.WHITE)
	title_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	title_scroll.add_child(title_label)
	banner.add_child(title_scroll)

	return banner

## 矢印ボタンを追加
static func _add_arrow_buttons(container: Control, banner_width: float) -> void:
	var arrow_size := Vector2(56, 56)
	var arrow_y := StoreBrowseBuilder.FEATURED_HEIGHT / 2.0 - arrow_size.y / 2.0

	for arrow_data in [
		{"name": "SlideshowPrev", "text": "◀", "x": 12},
		{"name": "SlideshowNext", "text": "▶", "x": banner_width - arrow_size.x - 12}
	]:
		var btn = Button.new()
		btn.name = arrow_data["name"]
		btn.flat = true
		btn.position = Vector2(arrow_data["x"], arrow_y)
		btn.custom_minimum_size = arrow_size
		btn.size = arrow_size
		btn.focus_mode = Control.FOCUS_NONE
		btn.mouse_filter = Control.MOUSE_FILTER_STOP
		btn.add_theme_font_size_override("font_size", 28)
		btn.add_theme_color_override("font_color", Color(1, 1, 1, 0.7))
		btn.add_theme_color_override("font_hover_color", Color.WHITE)

		# 画像アイコンの追加
		var icon = TextureRect.new()
		icon.texture = preload("res://resources/icons/arrow.png")
		icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		
		# 色を白にするシェーダー（黒背景・テキスト用の反転）
		var mat = ShaderMaterial.new()
		mat.shader = preload("res://resources/shaders/invert_color.gdshader")
		icon.material = mat
		
		# 左矢印画像が想定と逆だったので、SlideshowNextでフリップする
		if arrow_data["name"] == "SlideshowNext":
			icon.flip_h = true
			
		# 余白を作って中央配置
		var pad = 6.0
		icon.position = Vector2(pad, pad)
		icon.size = arrow_size - Vector2(pad * 2, pad * 2)
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		
		# 基本の透明度は0.7
		icon.modulate.a = 0.7
		btn.add_child(icon)
		
		# ボタンのフォーカス・ホバー状態でアイコンの透明度を更新
		btn.mouse_entered.connect(func(): icon.modulate.a = 1.0)
		btn.mouse_exited.connect(func(): icon.modulate.a = 0.7)
		btn.focus_entered.connect(func(): icon.modulate.a = 1.0)
		btn.focus_exited.connect(func(): icon.modulate.a = 0.7)

		for state_data in [
			{"state": "normal", "alpha": 0.4},
			{"state": "hover", "alpha": 0.6},
			{"state": "pressed", "alpha": 0.7}
		]:
			var s = StyleBoxFlat.new()
			s.bg_color = Color(0, 0, 0, state_data["alpha"])
			s.set_corner_radius_all(28)
			btn.add_theme_stylebox_override(state_data["state"], s)

		container.add_child(btn)
