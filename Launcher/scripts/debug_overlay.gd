extends Node
## スタッフ向けデバッグオーバーレイ (画面隅の常時表示 HUD)。
## サービスモードの「デバッグオーバーレイ切替」で ON/OFF する。ON の間はサービスモードを閉じても、
## シーンを移動しても、ゲーム中・中断メニュー中でも画面左上に FPS・メモリ・PC 名・シーン状態・
## 接続状態などを表示し続ける。設定はメモリのみ保持 (再起動で OFF に戻る)。
##
## 何の上にでも出すため、中断メニューと同じ「透明・borderless・最前面 (always_on_top) の別 OS ウィンドウ」
## として実装する。さらにフォーカスを奪わず (unfocusable)・クリックを透過 (mouse passthrough) するので、
## 下のランチャー/ゲーム/サービスモードの操作を一切妨げない。

const REFRESH_FAST := 0.25  # FPS・メモリ等の更新間隔 (秒)
const REFRESH_SLOW := 1.0   # DB ファイル存在チェック・出すモニタの追従など、重めの処理の間隔 (秒)

var _enabled: bool = false
var _win: Window = null
var _label: Label = null
var _fast_accum: float = 0.0
var _slow_accum: float = 0.0
var _db_ok: bool = false
var _db_path: String = ""


var _overlay_mgr: Node = null


func _ready() -> void:
	# サービスモード表示中 (tree.paused) でも更新し続ける。
	process_mode = Node.PROCESS_MODE_ALWAYS
	_build()
	# 中断メニューも always_on_top の別ウィンドウなので、開くと HUD の上に被さることがある。
	# 開いた直後に HUD を最前面へ上げ直して隠れないようにする。
	_overlay_mgr = get_node_or_null("/root/OverlayManager")
	if _overlay_mgr and _overlay_mgr.has_signal("opened"):
		_overlay_mgr.opened.connect(_on_overlay_menu_opened)


func is_enabled() -> bool:
	return _enabled


func toggle() -> void:
	set_enabled(not _enabled)


func set_enabled(v: bool) -> void:
	_enabled = v
	if v:
		_place_window()
		_refresh_slow()
		_update_text()
		_win.visible = true
		_apply_clickthrough()
	elif _win:
		_win.visible = false


## デバッグHUD窓を OS レベルでクリック透過にする。Godot の FLAG_MOUSE_PASSTHROUGH は同一アプリ内
## (Godot の他窓) にしか効かず、外部ゲームプロセスの上ではクリックが吸われ得るため、WS_EX_TRANSPARENT を
## Companion 経由で立ててクロスプロセスでも下のゲームへクリックを通す。表示の度に適用 (HWND は表示後に有効)。
func _apply_clickthrough() -> void:
	if _win == null:
		return
	var hwnd := DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE, _win.get_window_id())
	if hwnd == 0:
		return
	var agent := get_node_or_null("/root/LauncherAgent")
	if agent and agent.has_method("set_clickthrough"):
		agent.set_clickthrough(hwnd)


func _process(delta: float) -> void:
	if not _enabled:
		return
	_fast_accum += delta
	_slow_accum += delta
	if _slow_accum >= REFRESH_SLOW:
		_slow_accum = 0.0
		_refresh_slow()
		_place_window()  # ゲームが別モニタへ移った場合などに追従
		# 中断メニュー表示中は被されやすいので、念のため毎秒最前面へ上げ直す (安全網)。
		if _overlay_mgr and _overlay_mgr.has_method("is_open") and _overlay_mgr.is_open():
			_reassert_top()
	if _fast_accum >= REFRESH_FAST:
		_fast_accum = 0.0
		_update_text()


func _build() -> void:
	_win = Window.new()
	_win.transparent = true
	_win.borderless = true
	_win.always_on_top = true
	_win.unresizable = true
	_win.unfocusable = true  # フォーカスを奪わない (キオスク操作・ゲーム入力を妨げない)
	_win.set_flag(Window.FLAG_MOUSE_PASSTHROUGH, true)  # クリックを下の窓へ透過
	_win.visible = false
	# 別 OS ウィンドウは主窓の content スケーリングを継承しないため、1920 基準を明示 (位置・寸法を主窓と一致)。
	_win.content_scale_size = Vector2i(1920, 1080)
	_win.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	_win.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	_win.process_mode = Node.PROCESS_MODE_ALWAYS
	add_child(_win)

	var panel := PanelContainer.new()
	panel.position = Vector2(12, 12)
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0, 0, 0, 0.55)
	sb.set_corner_radius_all(6)
	sb.content_margin_left = 12
	sb.content_margin_right = 12
	sb.content_margin_top = 8
	sb.content_margin_bottom = 8
	panel.add_theme_stylebox_override("panel", sb)
	_win.add_child(panel)

	_label = Label.new()
	_label.add_theme_color_override("font_color", Color(0.85, 1.0, 0.85))
	_label.add_theme_font_override("font", preload("res://fonts/NotoSansJP-Regular.ttf"))  # 他画面と同じ日本語フォント
	_label.add_theme_font_size_override("font_size", 14)
	panel.add_child(_label)


## HUD ウィンドウを出すモニタへ合わせ、画面いっぱいに配置する。ゲーム実行中はゲーム窓のいるモニタ、
## それ以外はランチャーのいるモニタ (本番=単一モニタではどちらも同じ画面)。
func _place_window() -> void:
	if _win == null:
		return
	var scr := _resolve_screen()
	_win.position = DisplayServer.screen_get_position(scr)
	_win.size = DisplayServer.screen_get_size(scr)


## HUD を出すモニタ (screen index) を決める。ゲーム実行中はゲーム窓の中心を含むモニタ、無ければ
## ランチャーのいるモニタ (本番=単一モニタではどちらも同じ画面)。中断メニューの配置ロジックと同方針。
func _resolve_screen() -> int:
	var fallback: int = get_tree().root.current_screen
	var agent := get_node_or_null("/root/LauncherAgent")
	if agent == null or not GameSession.is_running() or not agent.has_method("get_game_window_rect"):
		return fallback
	var rect: Rect2i = agent.get_game_window_rect()
	if rect.size.x <= 0 or rect.size.y <= 0:
		return fallback
	var center := rect.position + rect.size / 2
	for i in range(DisplayServer.get_screen_count()):
		var pos := DisplayServer.screen_get_position(i)
		var sz := DisplayServer.screen_get_size(i)
		if center.x >= pos.x and center.x < pos.x + sz.x and center.y >= pos.y and center.y < pos.y + sz.y:
			return i
	return fallback


## 中断メニューが開いた直後の処理。メニューが前面を取り切るのを待ってから、数回 HUD を上げ直して
## メニューの裏に隠れるのを防ぐ (1 回だけだと取り合いに負けて「たまに隠れる」ため複数回リトライ)。
func _on_overlay_menu_opened() -> void:
	if not _enabled:
		return
	for i in range(4):
		await get_tree().create_timer(0.12).timeout
		if _enabled and _win and _win.visible:
			_reassert_top()


## HUD ウィンドウを最前面 (always_on_top band の先頭) へ上げ直す。always_on_top を一度 off→on すると
## Windows では再び最前面に来るため、後から出た別の always_on_top 窓 (中断メニュー) の上へ戻せる。
func _reassert_top() -> void:
	if _win == null:
		return
	_win.always_on_top = false
	_win.always_on_top = true


## 重めの項目 (DB ファイルの存在確認など) を間隔を空けて更新する。
func _refresh_slow() -> void:
	_db_path = PathManager.get_database_path()
	_db_ok = FileAccess.file_exists(_db_path)


## 表示テキストを組み立てて更新する。
func _update_text() -> void:
	var lines: Array[String] = []
	lines.append("=== DEBUG ===")
	lines.append("FPS: %d   フレーム: %.1f ms" % [
		Engine.get_frames_per_second(),
		1000.0 / maxf(1.0, Engine.get_frames_per_second())])
	var mem_mb := Performance.get_monitor(Performance.MEMORY_STATIC) / 1048576.0
	lines.append("メモリ: %.1f MB   オブジェクト: %d" % [
		mem_mb, int(Performance.get_monitor(Performance.OBJECT_COUNT))])
	var pc := OS.get_environment("COMPUTERNAME")
	lines.append("PC: %s   Launcher %s" % [pc if pc != "" else "(不明)", Version.get_version_string()])
	var win := get_window()
	lines.append("画面: %s  モニタ#%d  %s" % [
		str(DisplayServer.window_get_size()),
		win.current_screen,
		"全画面" if _is_fullscreen() else "ウィンドウ"])
	lines.append("シーン: %s" % _scene_name())
	lines.append("ゲーム: %s" % ("実行中" if GameSession.is_running() else "停止"))
	lines.append("DB: %s" % ("OK" if _db_ok else "見つからない"))
	lines.append("Monitor: 未実装")
	lines.append("稼働時間: %d 秒" % int(Time.get_ticks_msec() / 1000.0))
	_label.text = "\n".join(lines)


func _scene_name() -> String:
	var cur := get_tree().current_scene
	if cur == null:
		return "(遷移中)"
	var path := cur.scene_file_path
	return path.get_file() if path != "" else cur.name


func _is_fullscreen() -> bool:
	var m := DisplayServer.window_get_mode()
	return m == DisplayServer.WINDOW_MODE_FULLSCREEN or m == DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN
