extends Control

# ダイアログのUI要素への参照
@onready var title_label = $Panel/VBoxContainer/TitleLabel
@onready var message_label = $Panel/VBoxContainer/MessageLabel
@onready var code_label = $Panel/VBoxContainer/CodeLabel
@onready var exit_button = $Panel/VBoxContainer/ButtonBox/ExitButton
@onready var retry_button = $Panel/VBoxContainer/ButtonBox/RetryButton

var _error_code: int = 0


func _ready():
	exit_button.pressed.connect(_on_exit_pressed)
	retry_button.pressed.connect(_on_retry_pressed)
	
	# フォーカスを終了ボタンに当てる
	exit_button.grab_focus()
	
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

func _on_exit_pressed():
	print("[ErrorDialog] Exit button pressed. Quitting application.")
	get_tree().quit()

func _on_retry_pressed():
	# リトライ機能は実装次第（現状は非表示）
	print("[ErrorDialog] Retry not implemented.")
