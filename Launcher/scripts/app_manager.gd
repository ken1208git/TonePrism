extends Node

# AppManager
# アプリケーション全体のライフサイクル管理を行うAutoLoad
# ・終了リクエスト(Alt+F4 / ×ボタン)の封印 (#84 終了制御。終了はサービスモードからのみ)
# ・全画面共通の処理

func _ready():
	# Alt+F4 / × で即終了せず、終了要求を自前でハンドリングする (#84)。
	get_tree().set_auto_accept_quit(false)
	print("[AppManager] Auto accept quit disabled. Quit request will be handled manually.")

func _notification(what):
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_handle_quit_request()

func _handle_quit_request():
	# #84: Alt+F4 / × ボタンによる終了を完全に封印する。終了はサービスモード (Ctrl+Alt+F12 →「アプリ終了」)
	# からのみ可能にし、生徒の誤終了・意図的な終了を防ぐ。終了要求は「サービスモードから終了して」の案内に変える。
	# サービスモードはエラー表示中 (overlay layer 200 > エラー 128, PROCESS_MODE_ALWAYS) でも開けるため、
	# どの状況でも終了経路をサービスモードに一本化できる (旧「エラー時 即終了」安全弁は不要なので撤去)。
	if ErrorManager.is_error_showing():
		# 既にエラーダイアログ表示中。案内ダイアログを重ねず黙殺する (終了はサービスモードから)。
		return

	var msg = "この画面からアプリを終了することはできません。\n\n【スタッフの方へ】\n終了するにはサービスモードを開き (Ctrl + Alt + F12)、\n「アプリ終了」から行ってください。"

	DialogManager.close_current_dialog()
	DialogManager.show_message("終了について", msg, ["閉じる"], func(_idx): pass)
