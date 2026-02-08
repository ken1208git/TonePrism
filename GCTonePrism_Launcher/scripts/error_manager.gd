extends Node

# ErrorManager (AutoLoad)
# アプリケーション全体のエラー表示を管理します

var _dialog_scene = preload("res://scenes/components/error_dialog.tscn")
var _current_dialog: Control = null

func show_error(code: int):
	# 既に表示されている場合は何もしない（あるいは内容を更新するなど）
	# 既に表示されている場合は何もしない（あるいは内容を更新するなど）
	if _current_dialog != null:
		return
	
	# コードが0（初期値やOK）の場合は、不明なエラーとして扱う
	# これにより、画面に "Error Code: E-0000" が表示されるのを防ぐ
	if code == 0:
		code = ErrorCode.SYSTEM_UNKNOWN_ERROR
	var dialog = _dialog_scene.instantiate()
	
	# シーンツリーのルートに追加（最前面に表示）
	# get_tree().root.add_child(dialog) だとカレントシーンと並列になる
	# CanvasLayerを使って最前面を保証するのがベストだが、今回は簡易的にルートに追加
	get_tree().root.call_deferred("add_child", dialog)
	
	# セットアップ（次のフレームで実行されるようにcall_deferred後に設定が必要だが、
	# processフレームのタイミング次第なので、_readyで初期化されるのを待つか、
	# プロパティにセットして_readyで読む形にする）
	
	# ここではインスタンス化直後にメソッドを呼ぶ（_ready前だが、ノード構造はあるので@onready変数が解決される前）
	# なので、dialogスクリプト側で工夫するか、add_child後にアクセスする
	
	# add_childの後なら_readyが呼ばれているはず
	dialog.setup(code)
	
	_current_dialog = dialog
