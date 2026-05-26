extends Node
## スタッフ向けデバッグオーバーレイ (画面隅の常時表示 HUD、マイクラの F3 風)。
## サービスモードの「デバッグオーバーレイ切替」で ON/OFF する。ON の間はサービスモードを閉じても、
## シーンを移動しても画面左上に FPS・メモリ・PC 名・シーン状態・接続状態などを表示し続ける。
## 設定はメモリのみ保持 (再起動で OFF に戻る)。本 autoload は表示の生成と毎フレーム更新を担う。

const REFRESH_FAST := 0.25  # FPS・メモリ等の更新間隔 (秒)
const REFRESH_SLOW := 1.0   # DB ファイル存在チェック等、重めの項目の更新間隔 (秒)

var _enabled: bool = false
var _layer: CanvasLayer = null
var _label: Label = null
var _fast_accum: float = 0.0
var _slow_accum: float = 0.0
var _db_ok: bool = false
var _db_path: String = ""


func _ready() -> void:
	# サービスモード表示中 (tree.paused) でも更新し続ける。
	process_mode = Node.PROCESS_MODE_ALWAYS
	_build()


func is_enabled() -> bool:
	return _enabled


func toggle() -> void:
	set_enabled(not _enabled)


func set_enabled(v: bool) -> void:
	_enabled = v
	if _layer:
		_layer.visible = v
	if v:
		_refresh_slow()
		_update_text()


func _process(delta: float) -> void:
	if not _enabled:
		return
	_fast_accum += delta
	_slow_accum += delta
	if _slow_accum >= REFRESH_SLOW:
		_slow_accum = 0.0
		_refresh_slow()
	if _fast_accum >= REFRESH_FAST:
		_fast_accum = 0.0
		_update_text()


func _build() -> void:
	_layer = CanvasLayer.new()
	_layer.layer = 150  # 通常画面の上 / サービスモード (200) の下
	_layer.visible = false
	add_child(_layer)

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
	_layer.add_child(panel)

	_label = Label.new()
	_label.add_theme_color_override("font_color", Color(0.85, 1.0, 0.85))
	_label.add_theme_font_size_override("font_size", 14)
	panel.add_child(_label)


## 重めの項目 (DB ファイルの存在確認など) を間隔を空けて更新する。
func _refresh_slow() -> void:
	_db_path = PathManager.get_database_path()
	_db_ok = FileAccess.file_exists(_db_path)


## 表示テキストを組み立てて更新する。
func _update_text() -> void:
	var lines: Array[String] = []
	lines.append("=== DEBUG (F3) ===")
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
