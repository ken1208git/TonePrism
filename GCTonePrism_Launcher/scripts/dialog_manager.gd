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
	# 既存のシンプルなダイアログ表示（OKボタンのみなど、デフォルト挙動が必要ならここで追加）
	# 現状の実装ではボタンを追加していないため、呼び出し元で追加する必要がある
	# 互換性のため残すが、推奨はshow_message
	var dialog = _create_base_dialog()
	dialog.setup(title, message)
	dialog.add_button("OK", func(): close_current_dialog(), true)
	return dialog

func show_message(title: String, message: String, buttons: Array = [], callback: Callable = Callable(), button_colors: Array = []) -> CommonDialog:
	var dialog = _create_base_dialog()
	dialog.setup(title, message)
	
	if buttons.is_empty():
		# デフォルトはOKボタン
		dialog.add_button("OK", func(): 
			if callback.is_valid(): callback.call(0)
			close_current_dialog()
		, true)
	else:
		for i in range(buttons.size()):
			var btn_text = buttons[i]
			var btn_color = Color(0.2, 0.2, 0.2, 1.0) # デフォルトはダークグレー（不透明）
			if i < button_colors.size():
				btn_color = button_colors[i]
				
			# コールバックにはインデックスを渡す
			# ボタンが押されたらダイアログを閉じるのは共通
			# lambdaでiをキャプチャするためにデフォルト引数を使用
			dialog.add_button(btn_text, func(idx = i):
				if callback.is_valid(): callback.call(idx)
				close_current_dialog()
			, i == 0, btn_color) # 最初のボタンにフォーカス
			
	return dialog

func _create_base_dialog() -> CommonDialog:
	# 既存のダイアログがあれば閉じる
	close_current_dialog()
	
	var dialog = CommonDialogScene.instantiate()
	# CanvasLayer（自分自身）の下に追加
	add_child(dialog)
	_current_dialog = dialog
	
	# ダイアログ表示中はゲーム自体をポーズ
	get_tree().paused = true
	
	return dialog

func close_current_dialog():
	if _current_dialog != null:
		_current_dialog.queue_free()
		_current_dialog = null
		
		# ポーズ解除
		get_tree().paused = false

func is_dialog_showing() -> bool:
	return _current_dialog != null
