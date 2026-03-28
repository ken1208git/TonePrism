extends Control

# ダイアログのUI要素への参照
@onready var title_label = $Panel/VBoxContainer/TitleLabel
@onready var message_label = $Panel/VBoxContainer/MessageLabel
@onready var code_label = $Panel/VBoxContainer/CodeLabel

var _error_code: int = 0


func _ready():
	# 初期化時に保留されていた内容があれば反映
	if _error_code != 0:
		_update_ui()

func setup(code: int):
	_error_code = code

	# ノード準備完了していれば即反映
	# 未完了なら_readyで呼ばれるのを待つ
	if is_node_ready():
		_update_ui()

func _update_ui():
	# メッセージはtscnファイルで静的に設定済み
	code_label.text = "Error Code: E-%04d" % _error_code

	print("[ErrorDialog] Showing error E-%04d" % _error_code)
