extends Node

# AppManager
# アプリケーション全体のライフサイクル管理を行うAutoLoad
# ・終了リクエスト(Alt+F4 / ×ボタン)の封印。ランチャーの終了はサービスモードからのみ行える
# ・全画面共通の処理

func _ready():
	# 窓タイトル (Alt+Tab / タスクバーでスタッフが見る名前)。exe / project.godot config/name は OS 衝突回避の
	# 規約どおり技術名 "TonePrism_Launcher" のままだが、見える名前は Manager の "TonePrism 管理ソフト" と
	# トーンを揃え "TonePrism ランチャー" にする (システム名 TonePrism + 役割。Launcher だけが TonePrism 全部
	# ではないので "TonePrism" を名乗りきらない)。全画面キオスク中は表示されないが Alt+Tab 時に出る。
	get_window().title = "TonePrism ランチャー"
	# Alt+F4 / × で即終了せず、終了要求を自前でハンドリングする。
	get_tree().set_auto_accept_quit(false)
	print("[AppManager] Auto accept quit disabled. Quit request will be handled manually.")

func _notification(what):
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		_handle_quit_request()

func _handle_quit_request():
	# サービスモードはスタッフしか開けない隠し画面なので、その中でだけ Alt+F4 での終了を許可する
	# (項目14 まで辿らず素早く終了できる。生徒は開けないので誤終了の心配はない)。
	# ただしゲーム実行中はゲームを孤立させないため終了しない (項目14 の終了ガードと同じ)。
	if ServiceMode.is_open():
		if not GameSession.is_running():
			# 起動/試遊テストで起動したゲームは GameSession 非経由のため is_running()=false だが、
			# このまま quit すると孤児プロセスとして残る。quit 直前に停止 (taskkill) を通す。
			ServiceMode.cleanup_for_quit()
			get_tree().quit()
		return

	# 通常画面では Alt+F4 / × ボタンによる終了を完全に封印する。終了はサービスモード
	# (Ctrl+Alt+F12 →「ランチャー終了」) からのみ可能にし、生徒の誤操作や勝手な終了を防ぐ。
	# 終了要求は「サービスモードから終了して」の案内に変える。サービスモードはエラー表示中でも
	# 開けるので、どんな状況でも終了の入り口をサービスモードに一本化できる。
	if ErrorManager.is_error_showing():
		# 既にエラーダイアログ表示中。案内ダイアログを重ねず黙殺する (終了はサービスモードから)。
		return

	var msg = "この画面からランチャーを終了することはできません。\n\n【スタッフの方へ】\n終了するにはサービスモードを開き (Ctrl + Alt + F12)、\n「ランチャー終了」から行ってください。"

	DialogManager.close_current_dialog()
	DialogManager.show_message("終了について", msg, ["閉じる"], func(_idx): pass)
