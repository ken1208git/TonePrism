extends CanvasLayer
## サービスモードの全画面オーバーレイ UI (#74, SPEC §機能23)。
## 質素なデバッグ UI (黒背景 + 白テキスト)。左=項目メニュー / 右=選択項目の詳細。
## ランチャー本体のテーマとは独立。表示制御・トリガ・60 秒自動復帰は autoload ServiceMode が担う。
##
## UI は項目数が多く動的なため code-built (AGENTS「UI はなるべく .tscn」の例外: デバッグ用途の動的 UI)。

const C_BG := Color(0.05, 0.05, 0.07, 0.98)
const C_PANEL := Color(0.10, 0.10, 0.13, 1.0)
const C_TEXT := Color(0.90, 0.90, 0.90)
const C_MUTED := Color(0.55, 0.55, 0.60)
const C_ACCENT := Color(0.40, 0.75, 1.0)
const C_DANGER := Color(1.0, 0.45, 0.40)
const C_OK := Color(0.45, 0.90, 0.55)

# SPEC §機能23 の 16 項目。未実装は _build_item の match で stub 表示。
const ITEMS := [
	{"id": "input",        "label": "1. 入力チェック / コントローラー"},
	{"id": "audio",        "label": "2. 音声チェック"},
	{"id": "screen_test",  "label": "3. 画面表示テスト"},
	{"id": "games_check",  "label": "4. ゲーム一覧 状態確認"},
	{"id": "launch_test",  "label": "5. ゲーム起動テスト"},
	{"id": "network",      "label": "6. ネットワーク接続テスト"},
	{"id": "db_check",     "label": "7. データベース整合性チェック"},
	{"id": "log_view",     "label": "8. 簡易ログ確認"},
	{"id": "error_manual", "label": "9. エラー内容 + マニュアル"},
	{"id": "system_info",  "label": "10. システム情報"},
	{"id": "debug_overlay","label": "11. デバッグオーバーレイ切替"},
	{"id": "fullscreen",   "label": "12. フルスクリーン切替"},
	{"id": "monitor",      "label": "13. ランチャー表示モニタ選択"},
	{"id": "reload",       "label": "14. ランチャーの再読み込み"},
	{"id": "restart",      "label": "15. アプリの再起動"},
	{"id": "exit",         "label": "16. アプリ終了"},
]

var _root: Control = null
var _detail: VBoxContainer = null
var _menu_buttons: Array[Button] = []
var _test_rect: ColorRect = null      # 画面表示テストの全画面色 (表示中は Esc/クリックで戻る)
var _test_active: bool = false
var _exit_armed: bool = false         # アプリ終了の 2 段階確認 (1 回目で arm、2 回目で実行)


func _ready() -> void:
	layer = 200  # ランチャー UI より前面
	process_mode = Node.PROCESS_MODE_ALWAYS
	visible = false
	_build_ui()


func open_overlay() -> void:
	visible = true
	_exit_armed = false
	if not _menu_buttons.is_empty():
		_menu_buttons[0].grab_focus()
		_select(0)


func close_overlay() -> void:
	_hide_test()
	visible = false


func _input(event: InputEvent) -> void:
	if not visible:
		return
	# 画面表示テスト中はクリック/任意キーで色面を解除 (サービスモード自体は閉じない)。
	if _test_active:
		if event is InputEventKey and event.pressed and not event.echo:
			_hide_test()
			get_viewport().set_input_as_handled()
		elif event is InputEventMouseButton and event.pressed:
			_hide_test()
			get_viewport().set_input_as_handled()
		return
	# Esc で閉じる (ServiceMode 経由)。
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		ServiceMode.close()


func _build_ui() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_STOP  # 背後のランチャーへの入力を遮断
	add_child(_root)

	var bg := ColorRect.new()
	bg.color = C_BG
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(bg)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 48)
	margin.add_theme_constant_override("margin_right", 48)
	margin.add_theme_constant_override("margin_top", 32)
	margin.add_theme_constant_override("margin_bottom", 28)
	_root.add_child(margin)

	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 16)
	margin.add_child(col)

	# ヘッダ
	var header := Label.new()
	header.text = "サービスモード  —  Launcher %s  (Esc / Ctrl+Alt+F12 で閉じる)" % Version.get_version_string()
	header.add_theme_color_override("font_color", C_ACCENT)
	header.add_theme_font_size_override("font_size", 24)
	col.add_child(header)

	# 本体: 左メニュー + 右詳細
	var body := HBoxContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 24)
	col.add_child(body)

	# 左: メニュー (スクロール可能)
	var menu_scroll := ScrollContainer.new()
	menu_scroll.custom_minimum_size = Vector2(380, 0)
	menu_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_child(menu_scroll)

	var menu_vbox := VBoxContainer.new()
	menu_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	menu_vbox.add_theme_constant_override("separation", 4)
	menu_scroll.add_child(menu_vbox)

	_menu_buttons.clear()
	for i in range(ITEMS.size()):
		var btn := Button.new()
		btn.text = ITEMS[i]["label"]
		btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
		btn.focus_mode = Control.FOCUS_ALL
		btn.custom_minimum_size = Vector2(0, 40)
		btn.pressed.connect(_select.bind(i))
		btn.focus_entered.connect(_select.bind(i))
		menu_vbox.add_child(btn)
		_menu_buttons.append(btn)

	# 右: 詳細パネル (スクロール可能)
	var detail_panel := PanelContainer.new()
	detail_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	detail_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	var panel_sb := StyleBoxFlat.new()
	panel_sb.bg_color = C_PANEL
	panel_sb.set_corner_radius_all(8)
	panel_sb.content_margin_left = 24
	panel_sb.content_margin_right = 24
	panel_sb.content_margin_top = 20
	panel_sb.content_margin_bottom = 20
	detail_panel.add_theme_stylebox_override("panel", panel_sb)
	body.add_child(detail_panel)

	var detail_scroll := ScrollContainer.new()
	detail_panel.add_child(detail_scroll)

	_detail = VBoxContainer.new()
	_detail.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_detail.add_theme_constant_override("separation", 10)
	detail_scroll.add_child(_detail)

	# フッター
	var footer := Label.new()
	footer.text = "↑↓ / クリックで項目選択   Esc で閉じる   60 秒無操作で自動的に通常画面へ戻ります"
	footer.add_theme_color_override("font_color", C_MUTED)
	footer.add_theme_font_size_override("font_size", 14)
	col.add_child(footer)


## メニュー項目を選んで詳細を構築する。
func _select(index: int) -> void:
	if not visible or index < 0 or index >= ITEMS.size():
		return
	_exit_armed = false
	_clear_detail()
	var item: Dictionary = ITEMS[index]
	_add_title(item["label"])
	match item["id"]:
		"system_info": _build_system_info()
		"screen_test": _build_screen_test()
		"fullscreen": _build_fullscreen()
		"monitor": _build_monitor()
		"reload": _build_reload()
		"restart": _build_restart()
		"exit": _build_exit()
		_: _build_stub()


# ---------------- 実装済み項目 ----------------

func _build_system_info() -> void:
	var screen := get_window().current_screen
	var ev := Engine.get_version_info()
	var pc := OS.get_environment("COMPUTERNAME")
	if pc.is_empty():
		pc = "(不明)"
	var lines := [
		"PC 名: %s" % pc,
		"OS: %s" % OS.get_name(),
		"Launcher バージョン: %s" % Version.get_version_string(),
		"Godot バージョン: %s" % str(ev.get("string", "?")),
		"現在のモニタ: #%d  (%s)" % [screen, str(DisplayServer.screen_get_size(screen))],
		"ウィンドウサイズ: %s" % str(DisplayServer.window_get_size()),
		"モニタ数: %d" % DisplayServer.get_screen_count(),
		"プロセス PID: %d" % OS.get_process_id(),
	]
	for ln in lines:
		_add_text(ln)


func _build_screen_test() -> void:
	_add_text("全画面の単色 / グリッドを表示します。モニター相性・色・スケーリングの確認用。")
	_add_text("色をクリック → 全画面表示。クリックまたは任意キーで戻ります。", C_MUTED)
	var colors := [
		["黒", Color.BLACK], ["白", Color.WHITE], ["赤", Color.RED],
		["緑", Color.GREEN], ["青", Color.BLUE], ["グレー50%", Color(0.5, 0.5, 0.5)],
	]
	for c in colors:
		_add_button("%s を全画面表示" % c[0], _show_test.bind(c[1]))


func _build_fullscreen() -> void:
	var mode := DisplayServer.window_get_mode()
	var is_fs := mode == DisplayServer.WINDOW_MODE_FULLSCREEN or mode == DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN
	_add_text("現在: %s" % ("フルスクリーン" if is_fs else "ウィンドウ"))
	_add_text("モニター違いでの表示崩れ対応用。再起動で project 設定 (フルスクリーン) に戻ります。", C_MUTED)
	_add_button("フルスクリーン ⇔ ウィンドウ を切り替え", _toggle_fullscreen)


func _build_monitor() -> void:
	var cur := get_window().current_screen
	_add_text("現在のモニタ: #%d" % cur)
	_add_text("複数モニタ環境用。メモリのみ保持 (再起動でプライマリに戻る)。", C_MUTED)
	for i in range(DisplayServer.get_screen_count()):
		var sz := DisplayServer.screen_get_size(i)
		var label := "モニタ #%d  (%dx%d)%s" % [i, sz.x, sz.y, "  ← 現在" if i == cur else ""]
		_add_button(label, _set_monitor.bind(i))


func _build_reload() -> void:
	_add_text("DB を再読み込みして現在の画面を作り直します (再起動より軽い)。")
	_add_text("ゲーム実行中は無効 (プレイ中シーンの破棄を避ける)。", C_MUTED)
	if GameSession.is_running():
		_add_text("⚠ ゲーム実行中のため再読み込みできません。", C_DANGER)
	else:
		_add_button("ランチャーを再読み込み", _do_reload)


func _build_restart() -> void:
	_add_text("ランチャーを OS プロセスとして再起動します。")
	if GameSession.is_running():
		_add_text("⚠ ゲーム実行中のため再起動できません。", C_DANGER)
	else:
		_add_button("アプリを再起動", _do_restart)


func _build_exit() -> void:
	_add_text("ランチャーを終了します。Alt+F4 / × ボタン封印時はここが唯一の終了手段です。")
	if GameSession.is_running():
		_add_text("⚠ ゲーム実行中のため終了できません (先にゲームを終了してください)。", C_DANGER)
		return
	var btn := _add_button("アプリを終了", _on_exit_pressed)
	btn.add_theme_color_override("font_color", C_DANGER)


func _build_stub() -> void:
	_add_text("（実装予定）", C_MUTED)
	_add_text("この項目は SPEC §機能23 で定義済み・実装はこれから (#74)。", C_MUTED)


# ---------------- アクション ----------------

func _show_test(color: Color) -> void:
	if _test_rect == null:
		_test_rect = ColorRect.new()
		_test_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
		_test_rect.mouse_filter = Control.MOUSE_FILTER_STOP
		_root.add_child(_test_rect)
	_test_rect.color = color
	_test_rect.visible = true
	_test_active = true


func _hide_test() -> void:
	if _test_rect:
		_test_rect.visible = false
	_test_active = false


func _toggle_fullscreen() -> void:
	var mode := DisplayServer.window_get_mode()
	var is_fs := mode == DisplayServer.WINDOW_MODE_FULLSCREEN or mode == DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN
	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED if is_fs else DisplayServer.WINDOW_MODE_FULLSCREEN)
	_reselect_current()


func _set_monitor(index: int) -> void:
	get_window().current_screen = index
	_reselect_current()


func _do_reload() -> void:
	if GameSession.is_running():
		return
	ServiceMode.close()
	get_tree().reload_current_scene()


func _do_restart() -> void:
	if GameSession.is_running():
		return
	var exe := OS.get_executable_path()
	OS.create_process(exe, [])
	get_tree().quit()


func _on_exit_pressed() -> void:
	if GameSession.is_running():
		return
	if not _exit_armed:
		_exit_armed = true
		_clear_detail()
		_add_title("16. アプリ終了")
		_add_text("本当に終了しますか？ もう一度「終了する」を押すと終了します。", C_DANGER)
		var yes := _add_button("終了する", _on_exit_pressed)
		yes.add_theme_color_override("font_color", C_DANGER)
		_add_button("キャンセル", func(): _exit_armed = false; _select_by_id("exit"))
		return
	get_tree().quit()


# ---------------- UI ヘルパー ----------------

func _clear_detail() -> void:
	for c in _detail.get_children():
		c.queue_free()


func _add_title(text: String) -> void:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color", C_TEXT)
	l.add_theme_font_size_override("font_size", 22)
	_detail.add_child(l)
	var sep := HSeparator.new()
	_detail.add_child(sep)


func _add_text(text: String, color: Color = C_TEXT) -> void:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color", color)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_detail.add_child(l)


func _add_button(text: String, on_pressed: Callable) -> Button:
	var b := Button.new()
	b.text = text
	b.alignment = HORIZONTAL_ALIGNMENT_LEFT
	b.custom_minimum_size = Vector2(0, 36)
	b.pressed.connect(on_pressed)
	_detail.add_child(b)
	return b


## 現在選択中のメニュー項目を再構築する (状態変化後の表示更新用)。
func _reselect_current() -> void:
	for i in range(_menu_buttons.size()):
		if _menu_buttons[i].has_focus():
			_select(i)
			return


func _select_by_id(id: String) -> void:
	for i in range(ITEMS.size()):
		if ITEMS[i]["id"] == id:
			_select(i)
			return
