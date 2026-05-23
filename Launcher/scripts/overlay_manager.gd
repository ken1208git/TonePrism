extends Node
## Autoload: 中断メニュー (overlay_menu) の表示制御 (#30)。
## LauncherAgent.trigger_received (HOME / Guide) で開閉トグルする。
## Companion の sensor は watch 中 (=ゲーム実行中) のみ発火するため、トリガはゲーム中限定。
## メニューの選択結果 (再開 / 終了して選択画面へ) を signal で再発火し、game_selection 側が
## game_launcher につなぐ (配線)。

signal resume_requested()
signal quit_to_selection_requested()
signal exit_to_screensaver_requested()

## 中断オーバーレイの開閉。現シーン (playing) が購読し、ライブゲーム透過 (窓透明化 + 背景アート非表示) を反映する。
signal opened()
signal closed()

var _overlay: CanvasLayer = null
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

	_overlay.show_overlay(_game_title, _game_thumb_path)
	# 現シーン (playing) がライブゲーム透過を反映できるよう通知 (背景アート非表示 + 窓透明化)。
	opened.emit()
	# 透明化した状態を数フレーム コンポジットさせてから前面化する。プレイ中メイン窓はゲームに隠れて
	# (occluded) いて描画キャッシュが「プレイ中」のままなので、即 topmost すると前面化直後に一瞬
	# 「プレイ中」が見えてちらつく。透明フレームをキャッシュへ反映させてから出すことで防ぐ。
	await get_tree().process_frame
	await get_tree().process_frame
	if not _open:
		return
	# 単一ウィンドウ化 (#214): overlay は launcher メイン窓内の CanvasLayer。プレイ中はゲーム窓が前面
	# なので、メイン窓を前へ出さないと overlay が見えない/フォーカスを取れない。
	# 旧オーバーレイは always_on_top の別窓で一瞬で出ていたが、Godot のフルスクリーン窓は always_on_top を
	# 無視するため、companion 経由で **topmost (z-order)** を立てて即座にゲーム窓の上へ。SetForegroundWindow
	# だけだと foreground-lock で大きく遅延/失敗するための対策。topmost に加え focus_hwnd で入力フォーカスも奪う。
	var hwnd: int = _main_window_hwnd()
	if _companion and hwnd != 0:
		_companion.set_topmost(hwnd, true)
		_companion.focus_hwnd(hwnd)
	print("[OverlayManager] 中断メニューを開いた")


## launcher メイン窓の OS ネイティブハンドル (Windows: HWND)。companion に渡して前面化する。
func _main_window_hwnd() -> int:
	var win := get_window()
	if win == null:
		return 0
	return DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE, win.get_window_id())


func close() -> void:
	if not _open or _overlay == null:
		return
	_open = false
	_overlay.hide_overlay()
	# 現シーン (playing) に透過解除を通知 (背景アート復元 + 窓不透明化)。topmost 解除より先に行い、
	# ゲーム窓が前面に戻る前に launcher 側を通常表示へ戻す。
	closed.emit()
	# メイン窓の topmost を解除 (この後 GameSession.resume() がゲーム窓を前面に戻せるように)。
	var hwnd: int = _main_window_hwnd()
	if _companion and hwnd != 0:
		_companion.set_topmost(hwnd, false)
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
