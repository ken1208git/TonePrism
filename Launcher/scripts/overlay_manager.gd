extends Node
## Autoload: 中断メニュー (overlay_menu) の表示制御 (#30)。
## LauncherCompanion.trigger_received (HOME / Guide) で開閉トグルする。
## Companion の sensor は watch 中 (=ゲーム実行中) のみ発火するため、トリガはゲーム中限定。
## メニューの選択結果 (再開 / 終了して選択画面へ) を signal で再発火し、game_selection 側が
## game_launcher につなぐ (配線)。

signal resume_requested()
signal quit_to_selection_requested()
signal exit_to_screensaver_requested()

var _overlay: Window = null
var _open: bool = false
var _companion: Node = null

# 走行中ゲームの表示情報 (中断メニューのタイトル/アイコン用)。launch_game 時に set_current_game で設定。
var _game_title: String = ""
var _game_thumb_path: String = ""

# すりガラス背景用キャプチャ (companion 撮影) の待ち合わせ。
var _capture_done: bool = false
var _capture_ok: bool = false


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

	# トリガ (HOME / Guide) で開閉トグル。autoload 順で LauncherCompanion が先に居る前提だが防御的に確認。
	_companion = get_node_or_null("/root/LauncherCompanion")
	if _companion:
		_companion.trigger_received.connect(_on_trigger)
		_companion.capture_ready.connect(_on_capture_ready)
	else:
		push_warning("[OverlayManager] LauncherCompanion 不在、トリガ連携なし")


func _on_capture_ready(_path: String, ok: bool) -> void:
	_capture_done = true
	_capture_ok = ok


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

	# 背景は「ライブのゲーム + 濃い白の半透明パネル」方式 (動的・静止画なし)。
	# ぼかしは別プロセスのゲームをライブに blur できず静止キャプチャが必要で「止まって見える」ため不採用。
	# (capture 機構自体は残置・休眠。再びぼかしにする時は set_backdrop にパスを渡す。)
	if _overlay.has_method("set_backdrop"):
		_overlay.set_backdrop("")
	_overlay.show_overlay(_game_title, _game_thumb_path)
	# ゲームが前面 (フォーカス保持) のままだと overlay が前に出ない/フォーカスを取れないため、
	# companion 経由で **overlay 窓だけ** を強制前面化し foreground-lock を回避する
	# (PID 指定だとメインのランチャー窓を巻き込むため HWND 指定にする)。窓生成を 1 フレーム待つ。
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
