extends Node

# AppManager
# アプリケーション全体のライフサイクル管理を行うAutoLoad
# ・終了リクエスト(Alt+F4)のハンドリング
# ・全画面共通の処理

func _ready():
	# Alt+F4などで即終了せず、確認ダイアログを出すようにする
	get_tree().set_auto_accept_quit(false)
	print("[AppManager] Auto accept quit disabled. Quit request will be handled manually.")

func _notification(what):
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_handle_quit_request()

func _handle_quit_request():
	# 終了リクエストが来た場合（Alt+F4やウィンドウの×ボタン）
	var msg = "【警告】アプリケーションを終了します\n\nこの操作は管理者（スタッフ）用です。\n通常、この画面を閉じる必要はありません。\n\n本当に終了してもよろしいですか？"
	
	# 既存のダイアログがあれば閉じてから出す
	DialogManager.close_current_dialog()
	
	# ボタンを "終了する", "キャンセル" の順にする
	# Left: 終了する (Index 0), Right: キャンセル (Index 1)
	# Default focus on Cancel (Index 1) is safer? Or Exit?
	# Typically left is affirmative, right is cancel.
	# User screenshot had: OK, Cancel, Exit.
	# Let's do: [終了する, キャンセル]
	
	# キャンセルを左（デフォルトフォーカス）にする
	DialogManager.show_message("終了確認", msg, ["キャンセル", "終了する"], func(idx):
		if idx == 1:
			# 終了する
			_quit_application()
		else:
			# キャンセル
			pass
	)

func _quit_application():
	print("[AppManager] Quitting application...")
	get_tree().quit()
