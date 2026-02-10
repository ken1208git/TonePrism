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
	# 既存のダイアログがあれば閉じる
	close_current_dialog()
	
	var dialog = CommonDialogScene.instantiate()
	# CanvasLayer（自分自身）の下に追加
	add_child(dialog)
	
	dialog.setup(title, message)
	
	_current_dialog = dialog
	
	# ダイアログ表示中はゲーム自体をポーズ（入力を制限するが、ダイアログ自身は動く）
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
