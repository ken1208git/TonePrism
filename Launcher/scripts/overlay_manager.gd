extends Node
## Autoload: 中断メニュー (overlay_menu) の表示制御 (#30)。
## LauncherAgent.trigger_received (HOME / Guide) で開閉トグルする。
## Companion の sensor は watch 中 (=ゲーム実行中) のみ発火するため、トリガはゲーム中限定。
## メニューの選択結果 (再開 / 終了して選択画面へ) を signal で再発火し、game_selection 側が
## game_launcher につなぐ (配線)。

signal resume_requested()
signal quit_to_selection_requested()
signal exit_to_screensaver_requested()

## 中断メニューの開閉。playing シーンが購読し、メニュー窓が自前アイコンを出す間は重複する
## 自身のサムネを隠す (二重表示・二重影の回避)。
signal opened()
signal closed()

var _overlay: Window = null
var _open: bool = false
var _showing_quitting: bool = false  # 別のゲーム/退出 で overlay を「終了中」表示に morph 中 (game_exited まで)
var _companion: Node = null

# 走行中ゲームの表示情報 (中断メニューのタイトル/アイコン用)。launch_game 時に set_current_game で設定。
var _game_title: String = ""
var _game_thumb_path: String = ""
var _game_bg_path: String = ""  # 終了中 morph 用の背景アート (playing/カルーセルと揃える)


func _ready() -> void:
	var packed := load("res://scenes/overlay_menu.tscn") as PackedScene
	if packed == null:
		push_warning("[OverlayManager] overlay_menu.tscn の load 失敗、中断メニュー無効")
		return
	_overlay = packed.instantiate()
	add_child(_overlay)
	_overlay.resume_requested.connect(_on_resume)
	_overlay.quit_to_selection_requested.connect(_on_quit)
	_overlay.exit_to_screensaver_requested.connect(_on_exit)


## ゲーム起動時に呼ぶ。中断メニューに表示するタイトルとサムネイル (解決済み絶対パス) を覚える。
func set_current_game(game: GameInfo) -> void:
	if game == null:
		_game_title = ""
		_game_thumb_path = ""
		_game_bg_path = ""
		return
	_game_title = game.title
	_game_thumb_path = GamePathResolver.resolve_path(game.thumbnail_path, game.game_id)
	_game_bg_path = GamePathResolver.resolve_path(game.background_path, game.game_id)

	# トリガ (HOME / Guide) で開閉トグル。autoload 順で LauncherAgent が先に居る前提だが防御的に確認。
	_companion = get_node_or_null("/root/LauncherAgent")
	if _companion:
		# set_current_game はゲーム起動ごとに呼ばれる。重複接続 (Godot の「既に接続済み」エラー連発) を防ぐ。
		if not _companion.trigger_received.is_connected(_on_trigger):
			_companion.trigger_received.connect(_on_trigger)
	else:
		push_warning("[OverlayManager] LauncherAgent 不在、トリガ連携なし")
	# ゲーム終了 (プロセス消失) で「終了中」overlay を隠す handoff 用。重複接続を防ぐ。
	if not GameSession.game_exited.is_connected(_on_game_session_exited):
		GameSession.game_exited.connect(_on_game_session_exited)


func _on_trigger(_source: String) -> void:
	# 終了中 morph 中は HOME/Guide を無視 (終了処理中なので開閉しない)。
	if _showing_quitting:
		return
	toggle()


func is_open() -> bool:
	return _open


func toggle() -> void:
	# 開いている時のトグル閉じは「ゲームを再開」と同じ扱い (ゲーム窓を前面に戻す)。
	# 単に hide するだけだと前面が OS 任せになり、メイン窓が出てしまうため。
	if _open:
		_on_resume()
	else:
		open()


func open() -> void:
	if _open or _overlay == null:
		return
	_open = true

	# 2 枚構成 (#214): overlay は透明・always_on_top の別ウィンドウ。show_overlay でゲームの上へ
	# 一瞬で出る (always_on_top の z-order、foreground-lock 不要)。背面の launcher 本体 (playing) は
	# 不透明なまま据え置き = ウィンドウゲームの隙間を背景アートで埋めるのでデスクトップが透けない。
	# マルチモニタ (部員の動作確認等): ゲーム窓のいるモニタへ overlay を出す (本番=単一モニタでは現状と同じ画面)。
	_overlay.show_overlay(_game_title, _game_thumb_path, _resolve_overlay_screen())
	# playing シーンに通知 (重複する自身のサムネを隠す)。
	opened.emit()
	# ゲームがフォーカスを保持したままだと overlay 窓が入力を取れないため、companion 経由で overlay 窓
	# **だけ** を強制前面化しフォーカスを奪う (PID 指定だとメイン窓を巻き込むため HWND 指定)。窓生成を 1 フレーム待つ。
	await get_tree().process_frame
	if _open and _companion:
		var hwnd: int = _overlay.get_overlay_hwnd()
		if hwnd != 0:
			_companion.focus_hwnd(hwnd)
	print("[OverlayManager] 中断メニューを開いた")


## overlay を出すべきモニタ (screen index) を返す。companion が報告したゲーム窓の中心を含むモニタ。
## 取得できなければランチャーのいるモニタ (current_screen) にフォールバック (本番=単一モニタはここに一致)。
func _resolve_overlay_screen() -> int:
	var fallback: int = get_tree().root.current_screen
	if _companion == null:
		return fallback
	var rect: Rect2i = _companion.get_game_window_rect()
	if rect.size.x <= 0 or rect.size.y <= 0:
		return fallback
	var center := rect.position + rect.size / 2
	for i in range(DisplayServer.get_screen_count()):
		var pos := DisplayServer.screen_get_position(i)
		var sz := DisplayServer.screen_get_size(i)
		if center.x >= pos.x and center.x < pos.x + sz.x and center.y >= pos.y and center.y < pos.y + sz.y:
			return i
	return fallback


func close() -> void:
	if not _open or _overlay == null:
		return
	_open = false
	_overlay.hide_overlay()
	# playing シーンに通知 (隠していた自身のサムネを戻す)。
	closed.emit()
	print("[OverlayManager] 中断メニューを閉じた")


## 中断メニューの選択結果は autoload GameSession を直接呼ぶ (シーン切替で配線が切れないように)。
## 終了後の遷移先 (選択画面/スクリーンセーバー) は GameSession のフラグを見て現シーンが決める。
## いずれも退場アニメを再生し、その完了を待ってからゲーム側の処理に進む (アニメ中にゲームを前面化すると
## launcher が背面化して FPS が落ち、退場モーションがカクつく/一瞬で消えるため)。
func _on_resume() -> void:
	if _showing_quitting:
		return  # 終了中 morph 中は不可視ボタンの誤クリック等を無視
	await _close_then_wait()
	if not _open:
		GameSession.resume()


## 別のゲーム/退出: 閉じずに overlay を「終了中」へ morph (前面のまま=フォーカス移動なしで滑らか)。
## ゲーム消失 (game_exited) で _on_game_session_exited が overlay を隠し、裏のメイン窓の同じ終了中へ
## シームレスに handoff される。
func _on_quit() -> void:
	if not _open or _showing_quitting:
		return
	_enter_quitting()
	GameSession.quit()


func _on_exit() -> void:
	if not _open or _showing_quitting:
		return
	_enter_quitting()
	GameSession.request_exit_to_screensaver()


## 「終了中」morph 開始の共通処理。
func _enter_quitting() -> void:
	_showing_quitting = true
	closed.emit()  # playing の隠していたサムネを戻す (終了中の下地 + handoff のアイコン連続性)
	if _overlay:
		_overlay.show_quitting(_game_title, _game_bg_path)


## ゲーム消失で「終了中」overlay を即時非表示にし、裏のメイン窓 (同じ終了中) へ handoff する。
func _on_game_session_exited() -> void:
	if not (_open or _showing_quitting):
		return
	_open = false
	_showing_quitting = false
	if _overlay:
		_overlay.hide_now()


## 退場アニメを再生し、その長さぶん待つ。待機中はゲームを前面に戻さない (launcher を前面のまま保ち
## 退場モーションを滑らかに見せる)。待機中に再オープンされた場合は呼び出し側が _open で判定して中断する。
func _close_then_wait() -> void:
	if not _open:
		return
	var dur: float = _overlay.get_close_anim_duration() if _overlay else 0.0
	close()
	if dur > 0.0:
		await get_tree().create_timer(dur).timeout
