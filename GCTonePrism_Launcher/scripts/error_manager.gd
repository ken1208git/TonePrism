extends CanvasLayer

# ErrorManager (AutoLoad)
# アプリケーション全体のエラー表示を管理します

func _ready():
	# 最前面に表示されるようにレイヤーを設定
	layer = 128
	# プロセスモードを常に実行（ポーズ中も表示可能）に設定
	process_mode = Node.PROCESS_MODE_ALWAYS

var _dialog_scene = preload("res://scenes/components/error_dialog.tscn")
var _current_dialog: Control = null

func show_error(code: int):
	# 既に表示されている場合は何もしない
	if _current_dialog != null:
		return
	
	# コードが0（初期値やOK）の場合は、不明なエラーとして扱う
	if code == 0:
		code = ErrorCode.SYSTEM_UNKNOWN_ERROR
		
	var dialog = _dialog_scene.instantiate()
	
	# CanvasLayer（自分自身）の下に追加することで、layer設定が有効になる
	add_child(dialog)
	
	# セットアップ
	dialog.setup(code)
	
	_current_dialog = dialog
