class_name ButtonStyleManager
extends RefCounted
## ボタンスタイル生成・グローアニメーション管理

var _glow_styles: Array[StyleBoxFlat] = []
var _glow_timer: float = 0.0

## グローアニメーション（ブリージング）を更新する
func update_glow(delta: float) -> void:
	_glow_timer += delta
	var glow_alpha = 0.5 + 0.3 * sin(_glow_timer * 3.0)

	for style in _glow_styles:
		if style:
			var c = Color(1, 1, 1, glow_alpha)
			style.shadow_color = c
			style.border_color = c

## グローアニメーション対象にスタイルを追加する
func add_glow_style(style: StyleBoxFlat) -> void:
	_glow_styles.append(style)

## StaticFocusBorderのスタイルをグローリストに登録する
func register_focus_border(static_focus_border: Panel) -> void:
	if not static_focus_border:
		return
	var style = static_focus_border.get_theme_stylebox("panel")
	if style is StyleBoxFlat:
		add_glow_style(style)

## 終了ボタンのスタイルを設定する
func setup_exit_button(exit_button: Button) -> void:
	if not exit_button:
		return

	var icon_tex = load("res://images/exit.jpg")
	if icon_tex:
		exit_button.icon = icon_tex

	# 通常スタイル（白ボタン）
	var style_normal = StyleBoxFlat.new()
	style_normal.bg_color = Color.WHITE
	style_normal.set_corner_radius_all(12)
	style_normal.shadow_color = Color(0, 0, 0, 0.3)
	style_normal.shadow_size = 4
	style_normal.shadow_offset = Vector2(0, 0)
	style_normal.content_margin_left = 4
	style_normal.content_margin_right = 4
	style_normal.content_margin_top = 4
	style_normal.content_margin_bottom = 4
	exit_button.add_theme_stylebox_override("normal", style_normal)

	# ホバー
	var style_hover = style_normal.duplicate()
	style_hover.bg_color = Color(0.9, 0.9, 0.9, 1.0)
	exit_button.add_theme_stylebox_override("hover", style_hover)

	# 押下
	var style_pressed = style_normal.duplicate()
	style_pressed.bg_color = Color(0.7, 0.7, 0.7, 1.0)
	exit_button.add_theme_stylebox_override("pressed", style_pressed)

	# フォーカス（白い輪っか + グロー）
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_width_left = 0
	style_focus.border_width_top = 0
	style_focus.border_width_right = 0
	style_focus.border_width_bottom = 0
	style_focus.border_color = Color.WHITE
	style_focus.expand_margin_left = 6
	style_focus.expand_margin_right = 6
	style_focus.expand_margin_top = 6
	style_focus.expand_margin_bottom = 6
	style_focus.set_corner_radius_all(18)
	style_focus.shadow_color = Color(1, 1, 1, 0.5)
	style_focus.shadow_size = 12
	style_focus.shadow_offset = Vector2(0, 0)

	add_glow_style(style_focus)
	exit_button.add_theme_stylebox_override("focus", style_focus)

	# フォーカス制御 (Self-loop)
	exit_button.focus_neighbor_left = exit_button.get_path()
	exit_button.focus_neighbor_right = exit_button.get_path()
	exit_button.focus_neighbor_top = exit_button.get_path()

## プレイボタンのスタイルを設定する
func setup_play_button(play_button: Button) -> void:
	if not play_button:
		return

	# 通常スタイル（緑）
	var style_normal = StyleBoxFlat.new()
	style_normal.bg_color = Color(0.0, 0.6, 0.0, 1.0)
	style_normal.set_corner_radius_all(10)
	style_normal.content_margin_left = 20
	style_normal.content_margin_right = 20
	play_button.add_theme_stylebox_override("normal", style_normal)

	# ホバー
	var style_hover = style_normal.duplicate()
	style_hover.bg_color = Color(0.2, 0.8, 0.2, 1.0)
	play_button.add_theme_stylebox_override("hover", style_hover)

	# フォーカス（白枠 + グロー）
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_width_left = 0
	style_focus.border_width_top = 0
	style_focus.border_width_right = 0
	style_focus.border_width_bottom = 0
	style_focus.border_color = Color.WHITE
	var focus_margin = 8
	style_focus.expand_margin_left = focus_margin
	style_focus.expand_margin_right = focus_margin
	style_focus.expand_margin_top = focus_margin
	style_focus.expand_margin_bottom = focus_margin
	style_focus.set_corner_radius_all(10 + focus_margin)
	style_focus.shadow_color = Color(1, 1, 1, 0.5)
	style_focus.shadow_size = 8
	style_focus.shadow_offset = Vector2(0, 0)

	add_glow_style(style_focus)
	play_button.add_theme_stylebox_override("focus", style_focus)

	# 押下
	var style_pressed = style_normal.duplicate()
	style_pressed.bg_color = Color(0.0, 0.4, 0.0, 1.0)
	play_button.add_theme_stylebox_override("pressed", style_pressed)
