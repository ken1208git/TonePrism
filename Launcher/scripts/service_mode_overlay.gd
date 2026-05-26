extends CanvasLayer
## サービスモードの全画面オーバーレイ UI (#74, SPEC §機能23)。
## 質素なデバッグ UI (黒背景 + 白テキスト)。リスト型 (master-detail): 左に 16 項目リスト、右に選択項目の詳細を
## 同時表示する。
##
## 操作 (キーボード / マウス / コントローラー共通。ui_* は Godot 既定で 矢印/Enter/Esc = D-pad/A/B):
##  - 左リスト: ↑↓ で項目選択 (右の詳細がライブ更新) / → or A/Enter で詳細ペインへ / B/Esc でサービスモード終了
##  - 右詳細: ↑↓ で操作項目移動・A/Enter で実行 / ← or B/Esc で左リストへ戻る
##
## 表示制御・トリガ・60 秒自動復帰は autoload ServiceMode。UI は項目数が多く動的なため code-built。

const C_BG := Color(0.05, 0.05, 0.07, 1.0)  # 完全不透明 (裏のシーンは pause + 不可視で凍結)
const C_PANEL := Color(0.10, 0.10, 0.13, 1.0)
const C_TEXT := Color(0.90, 0.90, 0.90)
const C_MUTED := Color(0.55, 0.55, 0.60)
const C_ACCENT := Color(0.40, 0.75, 1.0)
const C_DANGER := Color(1.0, 0.45, 0.40)
const C_OK := Color(0.45, 0.90, 0.55)

# SPEC §機能23 の 16 項目。未実装は _build_detail の match で stub 表示。
const ITEMS := [
	{"id": "input",        "label": "1. 入力チェック / コントローラー"},
	{"id": "audio",        "label": "2. 音声チェック"},
	{"id": "screen_test",  "label": "3. 画面表示テスト"},
	{"id": "games_test",   "label": "4. ゲーム動作確認"},
	{"id": "network",      "label": "5. ネットワーク接続テスト"},
	{"id": "db_check",     "label": "6. データベース整合性チェック"},
	{"id": "log_view",     "label": "7. 簡易ログ確認"},
	{"id": "system_info",  "label": "8. システム情報"},
	{"id": "debug_overlay","label": "9. デバッグオーバーレイ切替"},
	{"id": "fullscreen",   "label": "10. フルスクリーン切替"},
	{"id": "monitor",      "label": "11. ランチャー表示モニタ選択"},
	{"id": "reload",       "label": "12. ランチャーの再読み込み"},
	{"id": "restart",      "label": "13. アプリの再起動"},
	{"id": "exit",         "label": "14. アプリ終了"},
]

var _root: Control = null
var _focus_sb: StyleBoxFlat = null   # フォーカス枠 (ボタン内側に描く=ScrollContainer のクリップで見切れない)
var _focus_off_sb: StyleBox = null   # マウス操作時の透明 focus (枠を出さない)
var _using_mouse: bool = false       # マウス操作中はフォーカス枠を出さない (他画面と同じ分離)
var _detail_title: Label = null
var _detail_content: VBoxContainer = null
var _footer: Label = null
var _menu_buttons: Array[Button] = []
var _selected_index: int = -1        # 左リストで選択中の項目
var _in_detail: bool = false         # フォーカスが右詳細ペインにあるか
var _test_rect: ColorRect = null
var _test_active: bool = false
var _exit_armed: bool = false
var _games_test_mode: String = ""    # 「ゲーム動作確認」の選択中モード ("" / exists / auto / play)


func _ready() -> void:
	layer = 200
	process_mode = Node.PROCESS_MODE_ALWAYS
	visible = false
	_build_ui()


func open_overlay() -> void:
	visible = true
	_in_detail = false
	_exit_armed = false
	_using_mouse = false  # Ctrl+Alt+F12 (キーボード) で開く → フォーカス枠あり
	_apply_focus_style()
	# 先頭項目を選択 + 詳細表示 + 左リストにフォーカス。
	_selected_index = -1
	if not _menu_buttons.is_empty():
		_menu_buttons[0].grab_focus()  # focus_entered で _select(0) が走る
	_update_footer()


func close_overlay() -> void:
	_hide_test()
	visible = false


func _input(event: InputEvent) -> void:
	if not visible:
		return
	# 画面表示テスト中はクリック / 任意キー / パッドボタンで色面を解除。
	if _test_active:
		var dismiss: bool = (event is InputEventKey and event.pressed and not event.echo) \
			or (event is InputEventMouseButton and event.pressed) \
			or (event is InputEventJoypadButton and event.pressed)
		if dismiss:
			_hide_test()
			get_viewport().set_input_as_handled()
		return
	# 入力デバイス分離 (ランチャー他画面と同じ): マウス=カーソル表示+フォーカス枠なし /
	# キー・パッド=カーソル非表示+フォーカス枠あり。
	if event is InputEventMouseMotion and event.relative.length() > 1.0:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		_set_using_mouse(true)
	elif event is InputEventMouseButton and event.pressed:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		_set_using_mouse(true)
	elif event is InputEventKey and event.pressed and not event.echo:
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
		_set_using_mouse(false)
	elif event is InputEventJoypadButton and event.pressed:
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
		_set_using_mouse(false)
	elif event is InputEventJoypadMotion and absf(event.axis_value) > 0.5:
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
		_set_using_mouse(false)
	# B / Esc: 詳細ペインなら左リストへ戻る、左リストならサービスモード終了。
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		if _in_detail:
			_exit_detail()
		else:
			ServiceMode.close()
		return
	# → で左リスト→詳細へ、← で詳細→左リストへ (水平のペイン移動)。消費して Godot 自動 h-ナビを抑止。
	if not _in_detail and event.is_action_pressed("ui_right"):
		_enter_detail()
		get_viewport().set_input_as_handled()
	elif _in_detail and event.is_action_pressed("ui_left"):
		_exit_detail()
		get_viewport().set_input_as_handled()


func _build_ui() -> void:
	# フォーカス枠: ボタン矩形の内側にアクセント色のボーダーを描く。デフォルトの focus 枠は矩形の外側に
	# はみ出して描かれ、全幅ボタン + ScrollContainer のクリップで左右が見切れるため自前に差し替える。
	_focus_sb = StyleBoxFlat.new()
	_focus_sb.draw_center = false
	_focus_sb.bg_color = Color(0, 0, 0, 0)
	_focus_sb.set_border_width_all(2)
	_focus_sb.border_color = C_ACCENT
	_focus_sb.set_corner_radius_all(4)
	_focus_off_sb = StyleBoxEmpty.new()  # マウス時: 何も描かない focus 枠

	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_root)

	var bg := ColorRect.new()
	bg.color = C_BG
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(bg)

	var margin := MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 56)
	margin.add_theme_constant_override("margin_right", 56)
	margin.add_theme_constant_override("margin_top", 32)
	margin.add_theme_constant_override("margin_bottom", 28)
	_root.add_child(margin)

	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 14)
	margin.add_child(col)

	var header := Label.new()
	header.text = "サービスモード  —  Launcher %s" % Version.get_version_string()
	header.add_theme_color_override("font_color", C_ACCENT)
	header.add_theme_font_size_override("font_size", 26)
	col.add_child(header)

	var body := HBoxContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_theme_constant_override("separation", 24)
	col.add_child(body)

	# 左: 項目リスト (scrollable・follow_focus で見切れ防止)
	var menu_scroll := ScrollContainer.new()
	menu_scroll.follow_focus = true
	menu_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	menu_scroll.custom_minimum_size = Vector2(420, 0)
	menu_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	body.add_child(menu_scroll)

	var menu_vbox := VBoxContainer.new()
	menu_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	menu_vbox.add_theme_constant_override("separation", 4)
	menu_scroll.add_child(menu_vbox)

	_menu_buttons.clear()
	for i in range(ITEMS.size()):
		var btn := Button.new()
		btn.text = " " + ITEMS[i]["label"]
		btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
		btn.focus_mode = Control.FOCUS_ALL
		btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		btn.custom_minimum_size = Vector2(0, 44)
		btn.add_theme_font_size_override("font_size", 18)
		_apply_focus_style_to(btn)  # キー/パッド時=枠 / マウス時=透明
		btn.focus_entered.connect(_select.bind(i))   # ↑↓ で移動すると詳細がライブ更新
		btn.pressed.connect(_on_menu_pressed.bind(i)) # A/Enter/クリックで詳細ペインへ
		menu_vbox.add_child(btn)
		_menu_buttons.append(btn)

	# 右: 詳細パネル
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

	var detail_box := VBoxContainer.new()
	detail_box.add_theme_constant_override("separation", 10)
	detail_panel.add_child(detail_box)

	_detail_title = Label.new()
	_detail_title.add_theme_color_override("font_color", C_ACCENT)
	_detail_title.add_theme_font_size_override("font_size", 22)
	detail_box.add_child(_detail_title)
	detail_box.add_child(HSeparator.new())

	var detail_scroll := ScrollContainer.new()
	detail_scroll.follow_focus = true
	detail_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	detail_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	detail_box.add_child(detail_scroll)

	_detail_content = VBoxContainer.new()
	_detail_content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_detail_content.add_theme_constant_override("separation", 10)
	detail_scroll.add_child(_detail_content)

	_footer = Label.new()
	_footer.add_theme_color_override("font_color", C_MUTED)
	_footer.add_theme_font_size_override("font_size", 14)
	col.add_child(_footer)


# ---------------- 選択 / ペイン移動 ----------------

## 左リストの選択変更 (focus_entered)。選択が変わった時だけ右詳細を作り直す (戻り時の無駄な再構築防止)。
func _select(index: int) -> void:
	if not visible or index == _selected_index:
		return
	_selected_index = index
	_exit_armed = false
	_games_test_mode = ""  # 項目を切り替えたらゲーム動作確認のモード選択もリセット
	_detail_title.text = ITEMS[index]["label"]
	_build_detail(ITEMS[index]["id"])


## 左リストで A/Enter/クリック → 詳細ペインへ移動。
func _on_menu_pressed(index: int) -> void:
	_select(index)
	_enter_detail()


## 右詳細ペインへフォーカスを移す。操作可能 control が無ければ移動しない (= false)。
func _enter_detail() -> bool:
	var first := _first_focusable(_detail_content)
	if first == null:
		return false
	_in_detail = true
	first.grab_focus()
	_update_footer()
	return true


## 左リストへフォーカスを戻す。
func _exit_detail() -> void:
	_in_detail = false
	if _selected_index >= 0 and _selected_index < _menu_buttons.size():
		_menu_buttons[_selected_index].grab_focus()
	_update_footer()


func _update_footer() -> void:
	if _in_detail:
		_footer.text = "↑↓ で操作   A/Enter で実行   ← / B / Esc で項目一覧へ戻る"
	else:
		_footer.text = "↑↓ で項目選択   → / A/Enter で詳細へ   B / Esc で閉じる   (60 秒無操作で自動復帰)"


# ---------------- 詳細ビルド ----------------

func _build_detail(id: String) -> void:
	_clear_content()
	match id:
		"system_info": _build_system_info()
		"games_test": _build_games_test()
		"db_check": _build_db_check()
		"log_view": _build_log_view()
		"screen_test": _build_screen_test()
		"fullscreen": _build_fullscreen()
		"monitor": _build_monitor()
		"reload": _build_reload()
		"restart": _build_restart()
		"exit": _build_exit()
		_: _build_stub()


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


## ゲーム動作確認: 3 段階の確認方法を選ばせ、選択モードの内容を下に表示する。
func _build_games_test() -> void:
	_add_text("ゲームの動作を 3 段階で確認できます。確認方法を選んでください。", C_MUTED)
	_add_button("① ファイル存在チェック（起動しない・全件一括）",
		func(): _games_test_mode = "exists"; _build_detail("games_test"); _refocus_detail())
	_add_button("② 起動テスト（自動で起動→終了し、起動可否だけ確認）",
		func(): _games_test_mode = "auto"; _build_detail("games_test"); _refocus_detail())
	_add_button("③ 試遊確認（起動して実際に遊ぶ・終了は手動）",
		func(): _games_test_mode = "play"; _build_detail("games_test"); _refocus_detail())
	_detail_content.add_child(HSeparator.new())
	match _games_test_mode:
		"exists": _render_games_exists_check()
		"auto":
			_add_text("（実装予定）各ゲームを自動で起動 → ウィンドウ生成を確認 → 自動終了し、", C_MUTED)
			_add_text("起動可否 (OK/NG) を一覧表示します。DLL 不足・起動時クラッシュ等の検出用。", C_MUTED)
		"play":
			_add_text("（実装予定）選んだ 1 本を実際に起動して試遊します。終了は手動 (プレイ後に閉じる)。", C_MUTED)
			_add_text("「ファイルはあるが実際に遊べるか」の最終確認用。", C_MUTED)
		_:
			_add_text("↑ から確認方法を選んでください。", C_MUTED)


## モード①: 各ゲームの実行ファイル(exe)が存在するか (= パス切れ/ファイル欠落がないか) を起動せずチェック。
func _render_games_exists_check() -> void:
	var db := DatabaseManager.new()
	if not db.open():
		_add_text("⚠ データベースを開けませんでした。Manager での初期化が必要かもしれません。", C_DANGER)
		return
	var repo := GameRepository.new(db)
	var games := repo.get_all_games()
	db.close()
	if games.is_empty():
		_add_text("登録ゲームがありません (または DB 読み込みに失敗)。", C_MUTED)
		return
	var ng := 0
	for g in games:
		if GamePathResolver.find_executable(g) == "":
			ng += 1
	_add_text("登録 %d 件中  OK %d / NG %d" % [games.size(), games.size() - ng, ng],
		C_OK if ng == 0 else C_DANGER)
	_add_text("（各ゲームの exe が存在するかを確認。NG はパス切れ/ファイル欠落。起動はしません）", C_MUTED)
	# 一覧は ItemList で表示。フォーカス可能なのでキーボード/コントローラー (↑↓) でもスクロールできる
	# (Label の羅列だとフォーカスが移れず長い一覧をスクロールできないため)。
	var list := ItemList.new()
	list.focus_mode = Control.FOCUS_ALL
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.custom_minimum_size = Vector2(0, 360)
	for g in games:
		var ok := GamePathResolver.find_executable(g) != ""
		var line := "✓ %s  [%s]" % [g.title, g.game_id] if ok \
			else "✗ %s  [%s]  exe 不明: %s" % [g.title, g.game_id, g.executable_path]
		var idx := list.add_item(line)
		list.set_item_custom_fg_color(idx, C_OK if ok else C_DANGER)
		list.set_item_selectable(idx, true)
	_detail_content.add_child(list)


## データベース整合性チェック: ファイル存在→接続→バージョン→テーブル存在/件数→読み取りテスト。
## Launcher は read-only (SPEC §6.5) のため書き込みテストはしない。
## 簡易ログ確認: 現セッションの直近ログ (Logger のメモリバッファ) を一覧表示。WARN/ERROR は色分け。
## Logger は組み込み Logger クラスと名前衝突するため、autoload ノードを /root/Logger 経由で参照する。
func _build_log_view() -> void:
	var logger := get_node_or_null("/root/Logger")
	if logger == null or not logger.has_method("get_recent_logs"):
		_add_text("ログ機能が利用できません。", C_DANGER)
		return
	_add_button("最新の状態に更新", func(): _build_detail("log_view"); _refocus_detail())
	var lines: Array = logger.get_recent_logs()
	if lines.is_empty():
		_add_text("ログがありません。", C_MUTED)
		return
	_add_text("現セッションの直近ログ %d 行 (古い順。最新は最下部)" % lines.size(), C_MUTED)
	var list := ItemList.new()
	list.focus_mode = Control.FOCUS_ALL
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.custom_minimum_size = Vector2(0, 420)
	for ln in lines:
		var idx := list.add_item(str(ln))
		if "[ERROR]" in ln:
			list.set_item_custom_fg_color(idx, C_DANGER)
		elif "[WARN]" in ln:
			list.set_item_custom_fg_color(idx, Color(1.0, 0.8, 0.4))
	_detail_content.add_child(list)
	# 最新行 (最下部) を見せる。
	if list.item_count > 0:
		list.select(list.item_count - 1)
		list.ensure_current_is_visible()


func _build_db_check() -> void:
	var db_path := PathManager.get_database_path()
	if not FileAccess.file_exists(db_path):
		_add_text("✗ DB ファイルが見つかりません: %s" % db_path, C_DANGER)
		_add_text("Manager での初期化が必要です。", C_MUTED)
		return
	_add_text("✓ DB ファイル: %s" % db_path, C_OK)

	var dbm := DatabaseManager.new()
	if not dbm.open():
		_add_text("✗ DB に接続できませんでした。", C_DANGER)
		return
	_add_text("✓ DB 接続 OK（読み取りテスト成功）", C_OK)

	# バージョン
	dbm.db.query("PRAGMA user_version")
	var vres := dbm.db.get_query_result()
	var ver := 0
	if vres and vres.size() > 0:
		ver = int(vres[0].get("user_version", 0))
	_add_text("DB バージョン (user_version): %d  /  Launcher 期待: %d" % [ver, DatabaseManager.CURRENT_DB_VERSION],
		C_OK if ver == DatabaseManager.CURRENT_DB_VERSION else C_MUTED)
	if ver > DatabaseManager.CURRENT_DB_VERSION:
		_add_text("  → DB が Launcher より新しい (Manager が先行更新。通常は無害)", C_MUTED)
	elif ver != 0 and ver < DatabaseManager.CURRENT_DB_VERSION:
		_add_text("  → DB が古い。Manager で更新してください", C_DANGER)

	# テーブル一覧
	dbm.db.query("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")
	var trows := dbm.db.get_query_result()
	var names: Array[String] = []
	if trows:
		for r in trows:
			names.append(str(r.get("name", "")))

	# 主要テーブルの存在
	for req in ["games", "developers", "store_sections"]:
		_add_text(("✓ テーブル %s あり" % req) if req in names else ("✗ 必須テーブル %s が見つかりません" % req),
			C_OK if req in names else C_DANGER)

	_add_text("テーブル別レコード数:", C_TEXT)
	var list := ItemList.new()
	list.focus_mode = Control.FOCUS_ALL
	list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	list.custom_minimum_size = Vector2(0, 280)
	for n in names:
		dbm.db.query('SELECT COUNT(*) AS c FROM "%s"' % n)
		var cres := dbm.db.get_query_result()
		var cnt := -1
		if cres and cres.size() > 0:
			cnt = int(cres[0].get("c", 0))
		list.add_item("%s: %s" % [n, "%d 件" % cnt if cnt >= 0 else "読み取り失敗"])
	_detail_content.add_child(list)
	dbm.close()


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
	_add_text("現在: %s" % ("フルスクリーン" if _is_fullscreen() else "ウィンドウ"))
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
		_add_button("キャンセル", func(): _exit_armed = false; _build_detail("exit"); _refocus_detail())
		return
	_add_text("ランチャーを終了します。Alt+F4 / × ボタン封印時はここが唯一の終了手段です。")
	var btn := _add_button("アプリを終了", func(): _exit_armed = true; _build_detail("exit"); _refocus_detail())
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
	_build_detail("fullscreen")
	_refocus_detail()


func _set_monitor(index: int) -> void:
	get_window().current_screen = index
	_build_detail("monitor")
	_refocus_detail()


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

## 入力デバイス変更時にフォーカス枠の表示/非表示を全ボタンへ反映する。
func _set_using_mouse(v: bool) -> void:
	if _using_mouse == v:
		return
	_using_mouse = v
	_apply_focus_style()


## 現在の _using_mouse に応じて 1 ボタンの focus stylebox を切り替える。
func _apply_focus_style_to(b: Button) -> void:
	b.add_theme_stylebox_override("focus", _focus_off_sb if _using_mouse else _focus_sb)


## メニュー + 詳細の全ボタンに現在のフォーカス枠スタイルを適用する。
func _apply_focus_style() -> void:
	for b in _menu_buttons:
		_apply_focus_style_to(b)
	if _detail_content:
		for c in _detail_content.get_children():
			if c is Button:
				_apply_focus_style_to(c)


func _clear_content() -> void:
	# remove_child を即時に行ってから queue_free する。queue_free だけだとフレーム末まで子が get_children に
	# 残り、直後の _first_focusable が「解放予定の古いボタン」を掴んでフォーカスを当て、フレーム末に消えて
	# フォーカス喪失で詰む (キーボード操作で「アプリを終了」→確認画面のフォーカスが出ない不具合)。
	for c in _detail_content.get_children():
		_detail_content.remove_child(c)
		c.queue_free()


func _add_text(text: String, color: Color = C_TEXT) -> void:
	var l := Label.new()
	l.text = text
	l.add_theme_color_override("font_color", color)
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_detail_content.add_child(l)


func _add_button(text: String, on_pressed: Callable) -> Button:
	var b := Button.new()
	b.text = text
	b.alignment = HORIZONTAL_ALIGNMENT_LEFT
	b.focus_mode = Control.FOCUS_ALL
	b.custom_minimum_size = Vector2(0, 40)
	_apply_focus_style_to(b)  # キー/パッド時=枠 / マウス時=透明
	b.pressed.connect(on_pressed)
	_detail_content.add_child(b)
	return b


## 詳細を作り直した後、詳細ペインに居るなら先頭の操作可能 control へフォーカスを戻す。
func _refocus_detail() -> void:
	if not _in_detail:
		return
	var first := _first_focusable(_detail_content)
	if first:
		first.grab_focus()
	else:
		_in_detail = false
		_exit_detail()


func _first_focusable(node: Node) -> Control:
	for c in node.get_children():
		if c is Control and (c as Control).focus_mode != Control.FOCUS_NONE:
			return c
	return null
