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
var _companion: Node = null

# 走行中ゲームの表示情報 (中断メニューのタイトル/アイコン用)。launch_game 時に set_current_game で設定。
var _game_title: String = ""
var _game_thumb_path: String = ""


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
		return
	_game_title = game.title
	_game_thumb_path = GamePathResolver.resolve_path(game.thumbnail_path, game.game_id)

	# トリガ (HOME / Guide) で開閉トグル。autoload 順で LauncherAgent が先に居る前提だが防御的に確認。
	_companion = get_node_or_null("/root/LauncherAgent")
	if _companion:
		_companion.trigger_received.connect(_on_trigger)
	else:
		push_warning("[OverlayManager] LauncherAgent 不在、トリガ連携なし")


func _on_trigger(_source: String) -> void:
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
	_overlay.show_overlay(_game_title, _game_thumb_path)
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
func _on_resume() -> void:
	close()
	GameSession.resume()


func _on_quit() -> void:
	close()
	GameSession.quit()


func _on_exit() -> void:
	close()
	GameSession.request_exit_to_screensaver()
