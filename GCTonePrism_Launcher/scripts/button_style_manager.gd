class_name ButtonStyleManager
extends RefCounted
## ボタンスタイル生成を担当
## グローアニメーションは GlowAnimator を参照
## スタイル（StyleBoxFlat）は .tscn で適用済み、ここではアイコンとフォーカス制御のみ

## 終了ボタンのアイコン・フォーカス制御を設定する
func setup_exit_button(exit_button: Button) -> void:
	if not exit_button:
		return

	var icon_tex = load("res://images/exit.png")
	if icon_tex:
		exit_button.icon = icon_tex
		var img = icon_tex.get_image()
		
		for y in range(img.get_height()):
			for x in range(img.get_width()):
				var c = img.get_pixel(x, y)
				if c.a > 0.0:
					img.set_pixel(x, y, Color(1, 1, 1, c.a))
		
		exit_button.icon = ImageTexture.create_from_image(img)
		exit_button.material = null
		# .tscnで expand_icon = true になっているため、icon_max_widthでサイズを制限して縮小
		exit_button.add_theme_constant_override("icon_max_width", 32)

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

## プレイボタンのスタイルを設定する（スタイルは .tscn で適用済み）
func setup_play_button(_play_button: Button) -> void:
	pass
