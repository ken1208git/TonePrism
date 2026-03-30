class_name ButtonStyleManager
extends RefCounted
## ボタンスタイル生成を担当
## グローアニメーションは GlowAnimator を参照
## スタイル（StyleBoxFlat）は .tscn で適用済み、ここではアイコンとフォーカス制御のみ

## 終了ボタンのアイコン・フォーカス制御を設定する
func setup_exit_button(exit_button: Button) -> void:
	if not exit_button:
		return

	var icon_tex = load("res://images/exit.jpg")
	if icon_tex:
		exit_button.icon = icon_tex

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
