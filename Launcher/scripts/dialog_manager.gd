extends CanvasLayer

# シングルトンとしてAutoLoadに登録する
# DialogManager

func _ready():
	# エラーダイアログよりは下のレイヤーに設定
	layer = 127
	# プロセスモードを常に実行（ポーズ中も表示可能）に設定
	process_mode = Node.PROCESS_MODE_ALWAYS

const CommonDialogScene = preload("res://scenes/components/common_dialog.tscn")

var _current_dialog: CommonDialog = null

func show_dialog(title: String, message: String) -> CommonDialog:
	var dialog = _create_base_dialog()
	dialog.setup(title, message)
	dialog.add_button("OK", func(): close_current_dialog(), true)
	return dialog

func show_message(title: String, message: String, buttons: Array = [], callback: Callable = Callable(), button_colors: Array = []) -> CommonDialog:
	var dialog = _create_base_dialog()
	dialog.setup(title, message)

	if buttons.is_empty():
		dialog.add_button("OK", func():
			if callback.is_valid(): callback.call(0)
			close_current_dialog()
		, true)
	else:
		for i in range(buttons.size()):
			var btn_text = buttons[i]
			var btn_color = Color(0.2, 0.2, 0.2, 1.0)
			if i < button_colors.size():
				btn_color = button_colors[i]

			dialog.add_button(btn_text, func(idx = i):
				if callback.is_valid(): callback.call(idx)
				close_current_dialog()
			, i == 0, btn_color)

	return dialog

func _create_base_dialog() -> CommonDialog:
	close_current_dialog()

	var dialog = CommonDialogScene.instantiate()
	add_child(dialog)
	_current_dialog = dialog

	get_tree().paused = true

	DialogAnimator.animate_in(dialog, self)

	return dialog

func close_current_dialog():
	if _current_dialog != null:
		var dialog = _current_dialog
		_current_dialog = null
		DialogAnimator.animate_out(dialog, self, func():
			if _current_dialog == null:
				get_tree().paused = false
		)

func is_dialog_showing() -> bool:
	return _current_dialog != null
