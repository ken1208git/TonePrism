extends Node

# シングルトンとしてAutoLoadに登録する
# DialogManager

const CommonDialogScene = preload("res://scenes/components/common_dialog.tscn")

var _current_dialog: CommonDialog = null

func show_dialog(title: String, message: String) -> CommonDialog:
	# 既存のダイアログがあれば閉じる
	close_current_dialog()
	
	var dialog = CommonDialogScene.instantiate()
	get_tree().root.call_deferred("add_child", dialog)
	
	# add_child完了・ready完了を待つのは難しいので、setupはawaitするか
	# CommonDialog側でready待ちをしているので続けて呼ぶ
	dialog.setup(title, message)
	
	_current_dialog = dialog
	
	# ダイアログ表示中はゲーム全体をポーズ（入力ブロック）
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
