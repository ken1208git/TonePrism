class_name ButtonStyleManager
extends RefCounted
## ボタンスタイル生成を担当
## グローアニメーションは GlowAnimator を参照

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

	# フォーカス（透明 — 共有フォーカス枠で管理）
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_color = Color.TRANSPARENT
	style_focus.shadow_color = Color.TRANSPARENT
	style_focus.shadow_size = 0
	exit_button.add_theme_stylebox_override("focus", style_focus)

	# フォーカス制御 (Self-loop) - シーンツリーに追加後に設定
	if exit_button.is_inside_tree():
		exit_button.focus_neighbor_left = exit_button.get_path()
		exit_button.focus_neighbor_right = exit_button.get_path()
		exit_button.focus_neighbor_top = exit_button.get_path()
	else:
		exit_button.ready.connect(func():
			exit_button.focus_neighbor_left = exit_button.get_path()
			exit_button.focus_neighbor_right = exit_button.get_path()
			exit_button.focus_neighbor_top = exit_button.get_path()
		, CONNECT_ONE_SHOT)

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

	# フォーカス（透明 — 共有フォーカス枠で管理）
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_color = Color.TRANSPARENT
	style_focus.shadow_color = Color.TRANSPARENT
	style_focus.shadow_size = 0
	play_button.add_theme_stylebox_override("focus", style_focus)

	# 押下
	var style_pressed = style_normal.duplicate()
	style_pressed.bg_color = Color(0.0, 0.4, 0.0, 1.0)
	play_button.add_theme_stylebox_override("pressed", style_pressed)
