extends CanvasLayer
## サービスモードの全画面メニュー画面。スタッフが現地で動作確認・診断・終了操作を行うための画面。
## 黒背景 + 白文字の素朴な画面。左に項目の一覧、右に選んだ項目の詳細を同時に表示する。
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

# サービスモードに並ぶ項目の一覧。準備中の項目は詳細欄に「実装予定」と表示される。
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
	{"id": "restart",      "label": "13. ランチャーの再起動"},
	{"id": "exit",         "label": "14. ランチャー終了"},
]

# 画面表示テストで順番に表示するパターン。先頭から 1 つずつ全画面表示し、キー送りで次へ進む。
const SCREEN_SEQ := [
	{"mode": "grid",     "color": Color.BLACK,            "label": "グリッド + セーフエリア"},
	{"mode": "colorbar", "color": Color.BLACK,            "label": "カラーバー"},
	{"mode": "solid",    "color": Color.WHITE,            "label": "白"},
	{"mode": "solid",    "color": Color.BLACK,            "label": "黒"},
	{"mode": "solid",    "color": Color.RED,              "label": "赤"},
	{"mode": "solid",    "color": Color.GREEN,            "label": "緑"},
	{"mode": "solid",    "color": Color.BLUE,             "label": "青"},
	{"mode": "solid",    "color": Color(0.5, 0.5, 0.5),   "label": "グレー50%"},
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
var _test_canvas: Control = null     # 画面表示テストの全画面描画ノード (単色 / グリッド / カラーバー / グラデ)
var _test_mode: String = "solid"     # 現在のテスト描画モード ("solid" / "grid" / "colorbar")
var _test_color: Color = Color.BLACK # solid モードの表示色
var _test_active: bool = false
var _seq_index: int = 0              # 画面表示テストで今表示しているパターンの位置
var _exit_armed: bool = false
var _games_test_mode: String = ""    # 「ゲーム動作確認」の選択中モード ("" / exists / auto / play)
var _audio_player: AudioStreamPlayer = null  # 音声チェックのテスト音再生用
var _test_tone: AudioStreamWAV = null        # 生成したテスト音 (キャッシュ)


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
	# 画面表示テスト中: Esc / B で中断してメニューへ。それ以外のキー / クリック / パッドボタンで次へ送る
	# (最後まで行くと自動でメニューに戻る)。
	if _test_active:
		if event.is_action_pressed("ui_cancel"):
			_hide_test()
			get_viewport().set_input_as_handled()
			return
		var advance: bool = (event is InputEventKey and event.pressed and not event.echo) \
			or (event is InputEventMouseButton and event.pressed) \
			or (event is InputEventJoypadButton and event.pressed)
		if advance:
			_advance_screen_seq()
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
		btn.focus_entered.connect(_select.bind(i))   # ↑↓ で移動すると詳細がライブ更新
		btn.pressed.connect(_on_menu_pressed.bind(i)) # A/Enter/クリックで詳細ペインへ
		menu_vbox.add_child(btn)
		_apply_focus_style_to(btn)  # ツリー追加後に呼ぶ (get_theme_stylebox がテーマを正しく解決するため)
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
		"audio": _build_audio_check()
		"screen_test": _build_screen_test()
		"fullscreen": _build_fullscreen()
		"monitor": _build_monitor()
		"reload": _build_reload()
		"restart": _build_restart()
		"exit": _build_exit()
		_: _build_stub()
	_constrain_detail_focus()


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
## ランチャーはデータベースを読むだけ (書き込みはしない) ので、読み取りテストのみ行う。
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


## 音声チェック: 正弦波のテスト音を生成して再生し、音声出力が機能しているかを確認する。
## (音声ファイルを持たないので毎回その場で作る。音量調整は今後ここに追加予定。)
func _build_audio_check() -> void:
	_add_text("テスト音を再生して音声出力を確認します。音が出ない問題の切り分け用。", C_MUTED)
	_add_button("テスト音を再生 (880Hz / 0.6秒)", _play_test_tone)
	_add_text("音が聞こえない場合: スピーカー/ヘッドホン接続、OS のミュート/音量、出力デバイスを確認してください。", C_MUTED)
	_add_text("（音量調整は今後ここにも追加予定）", C_MUTED)


func _play_test_tone() -> void:
	if _audio_player == null:
		# overlay (PROCESS_MODE_ALWAYS) 配下に置くので、サービスモードの tree pause 中でも再生される。
		_audio_player = AudioStreamPlayer.new()
		add_child(_audio_player)
	if _test_tone == null:
		_test_tone = _generate_tone(880.0, 0.6)
	_audio_player.stream = _test_tone
	_audio_player.play()


## 指定周波数・長さの 16bit モノラル正弦波を生成する (端は短くフェードしてクリック音を防ぐ)。
func _generate_tone(freq: float, dur: float) -> AudioStreamWAV:
	var rate := 44100
	var count := int(rate * dur)
	var data := PackedByteArray()
	data.resize(count * 2)
	for i in count:
		var t := float(i) / rate
		var env := minf(1.0, minf(t / 0.02, (dur - t) / 0.02))  # 立ち上がり/終わりをフェード
		var s := sin(TAU * freq * t) * env * 0.6
		data.encode_s16(i * 2, int(clampf(s, -1.0, 1.0) * 32767.0))
	var wav := AudioStreamWAV.new()
	wav.format = AudioStreamWAV.FORMAT_16_BITS
	wav.mix_rate = rate
	wav.stereo = false
	wav.data = data
	return wav


func _build_screen_test() -> void:
	_add_text("テスト用の表示を全画面で順番に出します。モニター相性・色・スケーリングの確認用。")
	_add_text("開始すると次の順で表示します:", C_MUTED)
	var names: Array[String] = []
	for p in SCREEN_SEQ:
		names.append(str(p["label"]))
	_add_text("　" + " → ".join(names), C_MUTED)
	_add_text("次へ: 任意のキー / クリック / パッドボタン　　中断: Esc / B", C_MUTED)
	_add_button("テストを開始（順番に表示）", _start_screen_seq)


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
		_add_button("ランチャーを再起動", _do_restart)


func _build_exit() -> void:
	if GameSession.is_running():
		_add_text("⚠ ゲーム実行中のため終了できません (先にゲームを終了してください)。", C_DANGER)
		return
	if _exit_armed:
		_add_text("本当に終了しますか？", C_DANGER)
		var yes := _add_button("終了する", func(): get_tree().quit())
		_set_button_font_color(yes, C_DANGER)
		_add_button("キャンセル", func(): _exit_armed = false; _build_detail("exit"); _refocus_detail())
		return
	_add_text("ランチャーを終了します。Alt+F4 / × ボタン封印時はここが唯一の終了手段です。")
	var btn := _add_button("ランチャーを終了", func(): _exit_armed = true; _build_detail("exit"); _refocus_detail())
	_set_button_font_color(btn, C_DANGER)


func _build_stub() -> void:
	_add_text("（実装予定）", C_MUTED)
	_add_text("この項目は準備中です。", C_MUTED)


# ---------------- アクション ----------------

## 全画面テスト表示の描画ノードを用意する (初回のみ生成)。CanvasLayer 直下に置くので
## サービスモード UI より後 = 最前面に出る。描画内容は _draw_test が _test_mode を見て決める。
func _ensure_test_canvas() -> void:
	if _test_canvas != null:
		return
	_test_canvas = Control.new()
	_test_canvas.set_anchors_preset(Control.PRESET_FULL_RECT)
	_test_canvas.mouse_filter = Control.MOUSE_FILTER_STOP
	_test_canvas.draw.connect(_draw_test)
	add_child(_test_canvas)


## 画面表示テストを先頭パターンから開始する。
func _start_screen_seq() -> void:
	_seq_index = 0
	_show_seq_current()


## 現在の _seq_index のパターンを全画面表示する。
func _show_seq_current() -> void:
	_ensure_test_canvas()
	var p: Dictionary = SCREEN_SEQ[_seq_index]
	_test_mode = str(p["mode"])
	_test_color = p["color"]
	_test_canvas.visible = true
	_test_canvas.queue_redraw()
	_test_active = true


## 次のパターンへ送る。最後まで行ったらテストを終了してメニューへ戻る。
func _advance_screen_seq() -> void:
	_seq_index += 1
	if _seq_index >= SCREEN_SEQ.size():
		_hide_test()
	else:
		_show_seq_current()


func _hide_test() -> void:
	if _test_canvas:
		_test_canvas.visible = false
	_test_active = false
	_refocus_detail()  # メニューに戻った時、開始ボタンへフォーカスを戻す


## 全画面テストの実描画。_test_canvas の draw シグナルから呼ばれる。画面サイズに追従するので
## どの解像度・モニタでもくっきり描ける (画像アセット不要)。
func _draw_test() -> void:
	var size := _test_canvas.size
	match _test_mode:
		"grid": _draw_test_grid(size)
		"colorbar": _draw_test_colorbar(size)
		_: _test_canvas.draw_rect(Rect2(Vector2.ZERO, size), _test_color)
	_draw_test_caption(size)


## 左上に小さく「何枚目 / 全体 + パターン名 + 操作ヒント」を出す。背景に半透明の黒帯を敷くので
## 白・黒どちらのパターン上でも読める。パターンの邪魔をしないよう隅に小さく描く。
func _draw_test_caption(size: Vector2) -> void:
	if _seq_index < 0 or _seq_index >= SCREEN_SEQ.size():
		return
	var p: Dictionary = SCREEN_SEQ[_seq_index]
	var text := "[%d/%d] %s    次へ:任意キー / 中断:Esc" % [
		_seq_index + 1, SCREEN_SEQ.size(), str(p["label"])]
	var font: Font = _test_canvas.get_theme_default_font()
	var fs := 18
	var tw := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs).x
	_test_canvas.draw_rect(Rect2(0, 0, tw + 24.0, 34.0), Color(0, 0, 0, 0.6))
	_test_canvas.draw_string(font, Vector2(12, 24), text,
		HORIZONTAL_ALIGNMENT_LEFT, -1, fs, Color.WHITE)


## ジオメトリ/クロスハッチ: 放送のモニタ調整パターン風。クロスハッチ (細密+粗) + 四隅対角線 +
## 中央&四隅の真円 (画素アスペクト/歪みの確認: 真円が楕円に見えたら縦横比がおかしい) + 中央十字 +
## セーフエリア枠 (90% / 80%)。解像度・拡大率・歪み・端の見切れをまとめて確認する。
func _draw_test_grid(size: Vector2) -> void:
	var cv := _test_canvas
	cv.draw_rect(Rect2(Vector2.ZERO, size), Color.BLACK)
	var center := size * 0.5
	# クロスハッチ: 細密 (32px) を淡く、粗 (128px) を濃く 2 段で描く
	_draw_grid_lines(size, 32.0, Color(0.16, 0.16, 0.16))
	_draw_grid_lines(size, 128.0, Color(0.42, 0.42, 0.42))
	# 四隅対角線 (中心ずれ・台形歪みの確認)
	var diag := Color(0.25, 0.25, 0.25)
	cv.draw_line(Vector2.ZERO, size, diag, 1.0)
	cv.draw_line(Vector2(size.x, 0), Vector2(0, size.y), diag, 1.0)
	# 真円群 (画素アスペクト・幾何歪みの確認)。半径は短辺基準で等倍 → 正しければ真円に見える
	var unit := minf(size.x, size.y)
	var circ := Color(0.55, 0.55, 0.55)
	cv.draw_arc(center, unit * 0.46, 0, TAU, 128, circ, 1.5)
	cv.draw_arc(center, unit * 0.30, 0, TAU, 96, circ, 1.5)
	var r_corner := unit * 0.12
	for cpt in [Vector2(r_corner, r_corner), Vector2(size.x - r_corner, r_corner),
			Vector2(r_corner, size.y - r_corner), Vector2(size.x - r_corner, size.y - r_corner)]:
		cv.draw_arc(cpt, r_corner, 0, TAU, 48, circ, 1.5)
	# 中央十字 + 中央点
	cv.draw_line(Vector2(center.x, 0), Vector2(center.x, size.y), C_ACCENT, 2.0)
	cv.draw_line(Vector2(0, center.y), Vector2(size.x, center.y), C_ACCENT, 2.0)
	cv.draw_circle(center, 4.0, C_ACCENT)
	# セーフエリア枠
	_draw_safe_frame(size, 0.90, Color(0.95, 0.85, 0.30), "90%")
	_draw_safe_frame(size, 0.80, Color(0.95, 0.55, 0.25), "80%")


## 指定間隔の縦横グリッド線を描く。
func _draw_grid_lines(size: Vector2, step: float, color: Color) -> void:
	var x := step
	while x < size.x:
		_test_canvas.draw_line(Vector2(x, 0), Vector2(x, size.y), color, 1.0)
		x += step
	var y := step
	while y < size.y:
		_test_canvas.draw_line(Vector2(0, y), Vector2(size.x, y), color, 1.0)
		y += step


func _draw_safe_frame(size: Vector2, ratio: float, color: Color, label: String) -> void:
	var inset := size * (1.0 - ratio) * 0.5
	var rect := Rect2(inset, size - inset * 2.0)
	_test_canvas.draw_rect(rect, color, false, 2.0)
	var font: Font = _test_canvas.get_theme_default_font()
	_test_canvas.draw_string(font, rect.position + Vector2(8, 22), label,
		HORIZONTAL_ALIGNMENT_LEFT, -1, 18, color)


## カラーバー: 放送規格 (SMPTE RP 219 / ARIB) の HD カラーバー。色再現・色順・黒レベルの確認用。
## 4 段構成: 上=75% カラーバー + 両端 40% グレー袖 (7/12) / リバース (1/12) / ランプ (1/12) /
## PLUGE = 黒レベル調整 (3/12)。横幅 a=画面幅、中央帯 x=3/4a を 7 列 (各 c=x/7) に分ける。
func _draw_test_colorbar(size: Vector2) -> void:
	var cv := _test_canvas
	var a := size.x
	var y := size.y
	var side := a / 8.0          # 両端の袖幅 (a/8)
	var x := a * 3.0 / 4.0       # 中央のカラー帯領域 (3/4 a)
	var c := x / 7.0             # 1 列の幅 (x/7)
	var r1 := y * 7.0 / 12.0     # 上段 (カラーバー) 高さ
	var r2 := y / 12.0           # リバース段
	var r3 := y / 12.0           # ランプ段
	var y2 := r1
	var y3 := r1 + r2
	var y4 := r1 + r2 + r3
	var r4 := y - y4             # PLUGE 段 (3/12)
	var W75 := Color(0.75, 0.75, 0.75)
	var W40 := Color(0.40, 0.40, 0.40)
	cv.draw_rect(Rect2(0, 0, a, y), Color.BLACK)

	# 上段: 左袖 40%W + 75% 7色 (白黄シアン緑マゼンタ赤青) + 右袖 40%W
	cv.draw_rect(Rect2(0, 0, side, r1), W40)
	var cols := [W75, Color(0.75, 0.75, 0), Color(0, 0.75, 0.75), Color(0, 0.75, 0),
		Color(0.75, 0, 0.75), Color(0.75, 0, 0), Color(0, 0, 0.75)]
	for i in range(7):
		cv.draw_rect(Rect2(side + c * i, 0, c + 1.0, r1), cols[i])
	cv.draw_rect(Rect2(side + x, 0, side, r1), W40)

	# リバース段: 100%Cy | 100%W(1列) | 75%W(6列) | 100%B
	cv.draw_rect(Rect2(0, y2, side, r2), Color(0, 1, 1))
	cv.draw_rect(Rect2(side, y2, c, r2), Color.WHITE)
	cv.draw_rect(Rect2(side + c, y2, c * 6.0, r2), W75)
	cv.draw_rect(Rect2(side + x, y2, side, r2), Color(0, 0, 1))

	# ランプ段: 100%Y | 0→100% 黒白ランプ | 100%R
	cv.draw_rect(Rect2(0, y3, side, r3), Color(1, 1, 0))
	var steps := 128
	var sw := x / steps
	for i in range(steps):
		var v := float(i) / (steps - 1)
		cv.draw_rect(Rect2(side + sw * i, y3, sw + 1.0, r3), Color(v, v, v))
	cv.draw_rect(Rect2(side + x, y3, side, r3), Color(1, 0, 0))

	# PLUGE 段: 両袖 15%W。中央は Bk(1.5c) | 100%W(2c) | 黒地 + PLUGE(-2/0/+2/0/+4%) | Bk(1c)
	cv.draw_rect(Rect2(0, y4, side, r4), Color(0.15, 0.15, 0.15))
	cv.draw_rect(Rect2(side, y4, c * 1.5, r4), Color.BLACK)
	cv.draw_rect(Rect2(side + c * 1.5, y4, c * 2.0, r4), Color.WHITE)
	var p := c / 3.0             # PLUGE 各バー幅 (x/21)
	var px := side + c * 3.5 + c * (5.0 / 6.0)  # 黒地のフィラーぶん右へ寄せる
	var pluge := [Color.BLACK, Color.BLACK, Color(0.02, 0.02, 0.02),
		Color.BLACK, Color(0.04, 0.04, 0.04)]
	for pc in pluge:
		cv.draw_rect(Rect2(px, y4, p, r4), pc)
		px += p
	cv.draw_rect(Rect2(side + x, y4, side, r4), Color(0.15, 0.15, 0.15))


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


## 現在の _using_mouse に応じて 1 ボタンの focus / hover スタイルを切り替える。
## キーボード・パッド操作中はマウスホバーのハイライトも消す: カーソルを隠してもマウスは物理的に
## ボタンの上に残るため、そのままだとホバー表示が消え残り、キーボードのフォーカス枠と二重に光って
## 見える。ホバーを通常表示と同じにすることで、残ったホバーを見えなくする。
func _apply_focus_style_to(b: Button) -> void:
	if _using_mouse:
		b.add_theme_stylebox_override("focus", _focus_off_sb)
		b.remove_theme_stylebox_override("hover")
	else:
		b.add_theme_stylebox_override("focus", _focus_sb)
		b.add_theme_stylebox_override("hover", b.get_theme_stylebox("normal"))


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
	# フォーカス喪失で詰む (キーボード操作で「ランチャーを終了」→確認画面のフォーカスが出ない不具合)。
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
	b.pressed.connect(on_pressed)
	_detail_content.add_child(b)
	_apply_focus_style_to(b)  # ツリー追加後に呼ぶ (get_theme_stylebox がテーマを正しく解決するため)
	return b


## ボタンの文字色を全状態 (通常/フォーカス/ホバー/押下) に適用する。font_color だけ上書きすると
## フォーカス時に既定の font_focus_color (白系) が使われて色が変わってしまうため、全状態を揃える。
func _set_button_font_color(b: Button, color: Color) -> void:
	for state in ["font_color", "font_focus_color", "font_hover_color",
			"font_pressed_color", "font_hover_pressed_color"]:
		b.add_theme_color_override(state, color)


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


## 詳細ペイン内の上下フォーカス移動を詰める。先頭の操作項目で↑、末尾で↓を押しても
## 左の項目一覧 (外側) へフォーカスが逃げないよう、上下の neighbor を自分自身に向ける
## (Godot の自動 neighbor が幾何的に左リストを拾って選択が変わるのを防ぐ)。
## 詳細から一覧へ戻るのは ← / B / Esc に一本化する。
func _constrain_detail_focus() -> void:
	var focusables: Array[Control] = []
	for c in _detail_content.get_children():
		if c is Control and (c as Control).focus_mode != Control.FOCUS_NONE:
			focusables.append(c)
	if focusables.is_empty():
		return
	focusables[0].focus_neighbor_top = focusables[0].get_path()
	focusables[-1].focus_neighbor_bottom = focusables[-1].get_path()
