extends CanvasLayer
## サービスモードの全画面オーバーレイ UI (#74, SPEC §機能23)。
## 質素なデバッグ UI (黒背景 + 白テキスト)。ナビゲーション型: メニュー画面（16 項目の縦リスト）で項目を
## 選ぶと、その項目の画面へ全幅で遷移する。戻る(B/Esc) でメニューへ、メニューで B/Esc でサービスモード終了。
## キーボード / マウス / コントローラーすべてで操作可能 (フォーカスナビ。ui_up/down/accept/cancel は Godot
## 既定で D-pad / A / B にマップ済み)。表示制御・トリガ・60 秒自動復帰は autoload ServiceMode が担う。
##
## UI は項目数が多く動的なため code-built (AGENTS「UI はなるべく .tscn」の例外: デバッグ用途の動的 UI)。

const C_BG := Color(0.05, 0.05, 0.07, 0.98)
const C_TEXT := Color(0.90, 0.90, 0.90)
const C_MUTED := Color(0.55, 0.55, 0.60)
const C_ACCENT := Color(0.40, 0.75, 1.0)
const C_DANGER := Color(1.0, 0.45, 0.40)

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

var _menu_view: Control = null        # メニュー画面 (16 項目リスト)
var _item_view: Control = null        # 項目画面 (選択項目の詳細)
var _item_title: Label = null
var _item_content: VBoxContainer = null
var _item_back_btn: Button = null
var _footer: Label = null
var _menu_buttons: Array[Button] = []
var _current_index: int = -1          # 項目画面で表示中の項目 (戻った時にフォーカス復帰)
var _test_rect: ColorRect = null      # 画面表示テストの全画面色
var _test_active: bool = false
var _exit_armed: bool = false


func _ready() -> void:
	layer = 200  # ランチャー UI より前面
	process_mode = Node.PROCESS_MODE_ALWAYS
	visible = false
	_build_ui()


func open_overlay() -> void:
	visible = true
	_show_menu()


func close_overlay() -> void:
	_hide_test()
	visible = false


func _input(event: InputEvent) -> void:
	if not visible:
		return
	# 画面表示テスト中はクリック / 任意キー / パッドボタンで色面を解除 (サービスモードは閉じない)。
	if _test_active:
		var dismiss: bool = (event is InputEventKey and event.pressed and not event.echo) \
			or (event is InputEventMouseButton and event.pressed) \
			or (event is InputEventJoypadButton and event.pressed)
		if dismiss:
			_hide_test()
			get_viewport().set_input_as_handled()
		return
	# B / Esc: 項目画面ならメニューへ戻る、メニュー画面ならサービスモードを閉じる。
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		if _item_view.visible:
			_show_menu()
		else:
			ServiceMode.close()


func _build_ui() -> void:
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_STOP  # 背後ランチャーへの入力を遮断
	add_child(root)

	var bg := ColorRect.new()
	bg.color = C_BG
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(bg)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 64)
	margin.add_theme_constant_override("margin_right", 64)
	margin.add_theme_constant_override("margin_top", 36)
	margin.add_theme_constant_override("margin_bottom", 28)
	root.add_child(margin)

	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 14)
	margin.add_child(col)

	# ヘッダ (常時)
	var header := Label.new()
	header.text = "サービスモード  —  Launcher %s" % Version.get_version_string()
	header.add_theme_color_override("font_color", C_ACCENT)
	header.add_theme_font_size_override("font_size", 26)
	col.add_child(header)

	# 画面切替エリア (メニュー画面 / 項目画面 のどちらか 1 つを表示)
	var stack := Control.new()
	stack.size_flags_vertical = Control.SIZE_EXPAND_FILL
	stack.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	col.add_child(stack)

	_menu_view = _build_menu_view()
	_menu_view.set_anchors_preset(Control.PRESET_FULL_RECT)
	stack.add_child(_menu_view)

	_item_view = _build_item_view()
	_item_view.set_anchors_preset(Control.PRESET_FULL_RECT)
	stack.add_child(_item_view)

	# フッター (画面に応じて文言を変える)
	_footer = Label.new()
	_footer.add_theme_color_override("font_color", C_MUTED)
	_footer.add_theme_font_size_override("font_size", 14)
	col.add_child(_footer)


func _build_menu_view() -> Control:
	# 全幅の縦リスト。follow_focus でフォーカス項目が常に見えるようスクロール (見切れ防止)。
	var scroll := ScrollContainer.new()
	scroll.follow_focus = true
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var vbox := VBoxContainer.new()
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_theme_constant_override("separation", 6)
	scroll.add_child(vbox)

	_menu_buttons.clear()
	for i in range(ITEMS.size()):
		var btn := Button.new()
		btn.text = "  " + ITEMS[i]["label"] + "  ▶"
		btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
		btn.focus_mode = Control.FOCUS_ALL
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		btn.custom_minimum_size = Vector2(0, 52)
		btn.add_theme_font_size_override("font_size", 20)
		btn.pressed.connect(_enter_item.bind(i))
		vbox.add_child(btn)
		_menu_buttons.append(btn)
	return scroll


func _build_item_view() -> Control:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)

	# ヘッダ行: 戻るボタン + 項目タイトル
	var head := HBoxContainer.new()
	head.add_theme_constant_override("separation", 16)
	vbox.add_child(head)

	_item_back_btn = Button.new()
	_item_back_btn.text = "← 戻る (B)"
	_item_back_btn.focus_mode = Control.FOCUS_ALL
	_item_back_btn.custom_minimum_size = Vector2(140, 44)
	_item_back_btn.pressed.connect(_show_menu)
	head.add_child(_item_back_btn)

	_item_title = Label.new()
	_item_title.add_theme_color_override("font_color", C_ACCENT)
	_item_title.add_theme_font_size_override("font_size", 22)
	_item_title.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	head.add_child(_item_title)

	vbox.add_child(HSeparator.new())

	# 内容 (スクロール可能・follow_focus)
	var scroll := ScrollContainer.new()
	scroll.follow_focus = true
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(scroll)

	_item_content = VBoxContainer.new()
	_item_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_item_content.add_theme_constant_override("separation", 10)
	scroll.add_child(_item_content)
	return vbox


# ---------------- 画面遷移 ----------------

func _show_menu() -> void:
	_exit_armed = false
	_hide_test()
	_item_view.visible = false
	_menu_view.visible = true
	_footer.text = "↑↓ / D-pad で選択   A / Enter / クリックで開く   B / Esc で閉じる"
	# 直前に見ていた項目のボタンへフォーカスを戻す (無ければ先頭)。
	var idx := _current_index if _current_index >= 0 else 0
	if idx < _menu_buttons.size():
		_menu_buttons[idx].grab_focus()


func _enter_item(index: int) -> void:
	if index < 0 or index >= ITEMS.size():
		return
	_current_index = index
	_exit_armed = false
	_menu_view.visible = false
	_item_view.visible = true
	_footer.text = "B / Esc で項目一覧へ戻る"
	_item_title.text = ITEMS[index]["label"]
	_build_item(ITEMS[index]["id"])
	# 内容ビルド後にフォーカスを置く (コントローラーで即操作できるように)。先頭の操作可能 control、無ければ戻るボタン。
	var first := _first_focusable(_item_content)
	if first:
		first.grab_focus()
	else:
		_item_back_btn.grab_focus()


func _build_item(id: String) -> void:
	_clear_content()
	match id:
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
	_add_text("全画面の単色 / グレーを表示します。モニター相性・色・スケーリングの確認用。")
	_add_text("色を選ぶ → 全画面表示。クリック / 任意キー / パッドボタンで戻ります。", C_MUTED)
	var colors := [
		["黒", Color.BLACK], ["白", Color.WHITE], ["赤", Color.RED],
		["緑", Color.GREEN], ["青", Color.BLUE], ["グレー50%", Color(0.5, 0.5, 0.5)],
	]
	for c in colors:
		_add_button("%s を全画面表示" % c[0], _show_test.bind(c[1]))


func _build_fullscreen() -> void:
	var is_fs := _is_fullscreen()
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
	if GameSession.is_running():
		_add_text("⚠ ゲーム実行中のため終了できません (先にゲームを終了してください)。", C_DANGER)
		return
	if _exit_armed:
		_add_text("本当に終了しますか？", C_DANGER)
		var yes := _add_button("終了する", func(): get_tree().quit())
		yes.add_theme_color_override("font_color", C_DANGER)
		_add_button("キャンセル", func(): _exit_armed = false; _build_item("exit"); _focus_first())
		return
	_add_text("ランチャーを終了します。Alt+F4 / × ボタン封印時はここが唯一の終了手段です。")
	var btn := _add_button("アプリを終了", func(): _exit_armed = true; _build_item("exit"); _focus_first())
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
		add_child(_test_rect)  # CanvasLayer 直下 = サービス UI より後 = 最前面
	_test_rect.color = color
	_test_rect.visible = true
	_test_active = true


func _hide_test() -> void:
	if _test_rect:
		_test_rect.visible = false
	_test_active = false


func _is_fullscreen() -> bool:
	var m := DisplayServer.window_get_mode()
	return m == DisplayServer.WINDOW_MODE_FULLSCREEN or m == DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN


func _toggle_fullscreen() -> void:
	DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED if _is_fullscreen() else DisplayServer.WINDOW_MODE_FULLSCREEN)
	_build_item("fullscreen")
	_focus_first()


func _set_monitor(index: int) -> void:
	get_window().current_screen = index
	_build_item("monitor")
	_focus_first()


func _do_reload() -> void:
	if GameSession.is_running():
		return
	ServiceMode.close()
	get_tree().reload_current_scene()


func _do_restart() -> void:
	if GameSession.is_running():
		return
	OS.create_process(OS.get_executable_path(), [])
	get_tree().quit()


# ---------------- UI ヘルパー ----------------

func _clear_content() -> void:
	for c in _item_content.get_children():
		c.queue_free()


func _add_text(text: String, color: Color = C_TEXT) -> void:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color", color)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_item_content.add_child(l)


func _add_button(text: String, on_pressed: Callable) -> Button:
	var b := Button.new()
	b.text = text
	b.alignment = HORIZONTAL_ALIGNMENT_LEFT
	b.focus_mode = Control.FOCUS_ALL
	b.custom_minimum_size = Vector2(0, 40)
	b.pressed.connect(on_pressed)
	_item_content.add_child(b)
	return b


## 内容を作り直した後、先頭の操作可能 control にフォーカスを戻す (再ビルド後の操作継続用)。
func _focus_first() -> void:
	var first := _first_focusable(_item_content)
	if first:
		first.grab_focus()
	elif _item_back_btn:
		_item_back_btn.grab_focus()


## children を走査して最初のフォーカス可能 control を返す (Button 等)。無ければ null。
func _first_focusable(node: Node) -> Control:
	for c in node.get_children():
		if c is Control and (c as Control).focus_mode != Control.FOCUS_NONE:
			return c
	return null
