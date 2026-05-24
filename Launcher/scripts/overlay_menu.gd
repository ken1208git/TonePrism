extends Window
## ゲーム実行中の中断オーバーレイ (#30 / #214)。
## **透明・最前面 (always_on_top)・borderless の別 OS ウィンドウ** (sub-window、project:
## embed_subwindows=false) として、走っているゲームの上に重ねる。
##
## 2 枚構成 (#214): 背面に launcher 本体 (playing シーン、不透明・全画面の背景アート) → 中間に
## ゲーム窓 → 最前面に本オーバーレイ窓 (透明) を重ねる。本窓の透明部分は「ゲーム窓のところ=ライブ
## ゲーム」「ウィンドウゲームの隙間=playing の背景アート」を映すため、デスクトップが透けない。
## (単一ウィンドウ透明化 (旧 4b) はゲームの“前”に launcher 窓を出すため、ウィンドウゲームの隙間に
##  デスクトップが漏れる退行があり、本 2 枚構成に戻した。)
##
## デザイン: Claude Design "B · Side Rail" を **左右反転 (ボタンを画面右)** + **ライトテーマ**化。
## ライトテーマは「ゲーム起動〜終了はライト」方針 (launching_overlay の白オーバーレイ) に合わせる。
## 時計はカルーセル画面 (top_bar) の、ゲームアイコンはゲーム起動中画面 (carousel 選択カード) の
## 位置・サイズを踏襲する (画面間の連続性)。
##
## 表示制御・トリガ配線は autoload OverlayManager。本スクリプトは見た目と入力のみ。

signal resume_requested()              ## 続ける (ゲームに戻る)
signal quit_to_selection_requested()   ## ホームに戻る (ゲーム選択画面へ)
signal exit_to_screensaver_requested() ## 退出 (スクリーンセーバーへ)

# ── ライトテーマ パレット (launching_overlay / colors_and_type.css 由来) ──
const C_SCRIM := Color(0.941, 0.933, 0.914)   # #f0eee9 デザイン body 背景
const C_TITLE := Color(0.05, 0.05, 0.05)      # 見出し (起動中画面 TitleLabel と同値)
const C_TEXT := Color(0.15, 0.15, 0.15)       # 本文 (起動中画面 StatusLabel と同値)
const C_MUTED := Color(0.45, 0.45, 0.45)      # サブ・ヒント
const C_DANGER := Color(0.85, 0.15, 0.15)     # 退出 (--accent-exit)

# ── 既存画面からの完コピ寸法 ──
const RAIL_W := 620.0                          # デザイン B のレール幅
const CLOCK_POS := Vector2(40, 20)             # top_bar: margin_left 40 / margin_top 20
const CLOCK_FONT_SIZE := 32                    # top_bar ClockLabel
const ICON_CENTER_X := 250.0                   # carousel: container 幅500 / 2
const ICON_SIZE := 360.0                       # carousel: CARD_SIZE 200 × SCALE_ACTIVE 1.8
const ICON_RADIUS := 29                        # カルーセルのカードbg角丸 16 × 1.8 ≈ 29 (影と角丸をカード厳密一致)

const FONT_BOLD := preload("res://fonts/NotoSansJP-Bold.ttf")
const FONT_REG := preload("res://fonts/NotoSansJP-Regular.ttf")

# メニュー項目 (今動くものだけ: 続ける / 別のゲームをあそぶ / 退出する)
const ITEMS := [
	{"id": "resume", "label": "続ける",           "sub": "ゲームに戻る"},
	{"id": "home",   "label": "別のゲームをあそぶ", "sub": "ゲーム選択画面に戻る"},
	{"id": "exit",   "label": "退出する",          "sub": "プレイを終了して席を離れる", "danger": true},
]

var _root: Control = null
var _clock_label: Label = null
var _title_label: Label = null
var _icon_tex: TextureRect = null
var _icon_placeholder: Label = null
var _buttons: Array[Button] = []
var _clock_timer: Timer = null
# フォーカスのグロー枠 (launcher の白グローを濃色アレンジ)。全ボタン共有 1 個を毎フレーム明滅させ、
# Godot は focus 中の Control にだけ focus stylebox を描くので、結果フォーカス行だけが光る。
var _focus_glow_sb: StyleBoxFlat = null  # キー/パッド時の focus グロー (枠リング)
var _focus_off_sb: StyleBoxFlat = null   # マウス時の focus (透明 = グロー出さない)
var _hover_sb: StyleBoxFlat = null       # マウスホバー時の薄黒塗り
var _press_sb: StyleBoxFlat = null       # マウスクリック時の薄黒塗り (グロー無し)
var _glow_t: float = 0.0
var _using_mouse: bool = false  # マウス操作中はキーフォーカスのグローを出さない (他画面と同じ分離)
const C_FOCUS_GLOW := Color(0.12, 0.12, 0.12)  # 白グローのライト版 = 濃色グロー


func _ready() -> void:
	transparent = true
	borderless = true
	always_on_top = true
	unresizable = true
	visible = false
	# 別 OS ウィンドウ (sub-window) はメイン窓の content スケーリングを継承しないため、
	# project 設定 (base 1920×1080 / canvas_items / expand) と同じ拡大をこの窓にも明示設定する
	# (高解像度モニターでも 1920 基準の寸法・角丸パネル位置がランチャー本体と一致し続ける)。
	content_scale_size = Vector2i(1920, 1080)
	content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	# 中断中に tree.paused にしても glow/入力を効かせる。
	process_mode = Node.PROCESS_MODE_ALWAYS
	_build_ui()


func _build_ui() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_root)

	# ── 全画面ライト veil: 画面全体を薄い白で覆う (ライトテーマ。透明窓なのでライブゲームが薄く透ける) ──
	var veil := ColorRect.new()
	veil.color = Color(C_SCRIM.r, C_SCRIM.g, C_SCRIM.b, 0.5)
	veil.set_anchors_preset(Control.PRESET_FULL_RECT)
	veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(veil)

	# ── 中断メニューのパネル: 右端から生えてくる一枚の角丸四角形 ──
	# 画面右端に flush (右角は直角 = 壁に張り付く) + 左側を大きく角丸 + 上下を画面端から離して、
	# 「右の壁から角丸四角形がせり出している」見た目に。ライブゲームが薄く透ける濃いめの白。
	var rail_panel := Panel.new()
	rail_panel.anchor_left = 1.0
	rail_panel.anchor_right = 1.0
	rail_panel.anchor_top = 0.0
	rail_panel.anchor_bottom = 1.0
	rail_panel.offset_left = -RAIL_W
	rail_panel.offset_right = 0.0
	rail_panel.offset_top = 70.0
	rail_panel.offset_bottom = -70.0
	rail_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var rail_sb := StyleBoxFlat.new()
	rail_sb.bg_color = Color(C_SCRIM.r, C_SCRIM.g, C_SCRIM.b, 0.85)
	rail_sb.corner_radius_top_left = 40
	rail_sb.corner_radius_bottom_left = 40
	rail_sb.corner_radius_top_right = 0
	rail_sb.corner_radius_bottom_right = 0
	rail_panel.add_theme_stylebox_override("panel", rail_sb)
	_root.add_child(rail_panel)

	# ── 時計 (top_bar 完コピ位置/サイズ。ライトなので色のみ濃色化) ──
	_clock_label = Label.new()
	_clock_label.add_theme_font_override("font", FONT_REG)
	_clock_label.add_theme_font_size_override("font_size", CLOCK_FONT_SIZE)
	_clock_label.add_theme_color_override("font_color", C_TEXT)
	_clock_label.position = CLOCK_POS
	_clock_label.text = _now_hhmm()
	_clock_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(_clock_label)

	# ── ゲームアイコン (carousel 選択カード完コピ: 中心(250, h/2)・360×360・角丸36) ──
	# カルーセルの Card→Clipper→Icon と同じ2層: 外=影付き角丸カード(clipしない→影が角丸に追従)、
	# 内=角丸クリップ用 (アイコンを角丸に切り抜く)。1枚に clip と影を同居させると clip がパネル矩形で
	# 影を四角く切ってしまうため分ける。
	var icon_panel := Panel.new()
	var icon_sb := StyleBoxFlat.new()
	icon_sb.bg_color = Color(0.1, 0.1, 0.1, 1)
	icon_sb.set_corner_radius_all(ICON_RADIUS)
	# カルーセルのカードと同じ影 (card: shadow 0.25 / size4 ×1.8scale ≒ 7px。等倍 360px なので size 7)。
	icon_sb.shadow_color = Color(0, 0, 0, 0.25)
	icon_sb.shadow_size = 7
	icon_panel.add_theme_stylebox_override("panel", icon_sb)
	icon_panel.set_anchors_preset(Control.PRESET_CENTER_LEFT)
	icon_panel.anchor_top = 0.5
	icon_panel.anchor_bottom = 0.5
	icon_panel.offset_left = ICON_CENTER_X - ICON_SIZE / 2.0
	icon_panel.offset_right = ICON_CENTER_X + ICON_SIZE / 2.0
	icon_panel.offset_top = -ICON_SIZE / 2.0
	icon_panel.offset_bottom = ICON_SIZE / 2.0
	icon_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(icon_panel)

	var icon_clip := Panel.new()
	icon_clip.clip_children = CanvasItem.CLIP_CHILDREN_ONLY
	var icon_clip_sb := StyleBoxFlat.new()
	icon_clip_sb.bg_color = Color(0.1, 0.1, 0.1, 1)
	icon_clip_sb.set_corner_radius_all(ICON_RADIUS)
	icon_clip.add_theme_stylebox_override("panel", icon_clip_sb)
	icon_clip.set_anchors_preset(Control.PRESET_FULL_RECT)
	icon_clip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_panel.add_child(icon_clip)

	_icon_tex = TextureRect.new()
	_icon_tex.set_anchors_preset(Control.PRESET_FULL_RECT)
	_icon_tex.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon_tex.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	_icon_tex.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_clip.add_child(_icon_tex)

	_icon_placeholder = Label.new()
	_icon_placeholder.text = "NO IMAGE"
	_icon_placeholder.add_theme_font_override("font", FONT_BOLD)
	_icon_placeholder.add_theme_font_size_override("font_size", 28)
	_icon_placeholder.add_theme_color_override("font_color", Color(0.5, 0.5, 0.5))
	_icon_placeholder.set_anchors_preset(Control.PRESET_FULL_RECT)
	_icon_placeholder.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_icon_placeholder.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	icon_clip.add_child(_icon_placeholder)

	# ── パネル内のコンテンツ (rail_panel の内側 padding) ──
	var rail_margin := MarginContainer.new()
	rail_margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	rail_margin.add_theme_constant_override("margin_left", 56)
	rail_margin.add_theme_constant_override("margin_right", 48)
	rail_margin.add_theme_constant_override("margin_top", 44)
	rail_margin.add_theme_constant_override("margin_bottom", 44)
	rail_panel.add_child(rail_margin)

	var vb := VBoxContainer.new()
	vb.add_theme_constant_override("separation", 28)
	rail_margin.add_child(vb)

	# ヘッダ
	var kicker := Label.new()
	kicker.text = "HOME / 中断メニュー"
	kicker.add_theme_font_override("font", FONT_REG)
	kicker.add_theme_font_size_override("font_size", 14)
	kicker.add_theme_color_override("font_color", C_MUTED)
	vb.add_child(kicker)

	var title := Label.new()
	title.text = "一時停止中"
	title.add_theme_font_override("font", FONT_BOLD)
	title.add_theme_font_size_override("font_size", 64)
	title.add_theme_color_override("font_color", C_TITLE)
	vb.add_child(title)

	_title_label = Label.new()
	_title_label.add_theme_font_override("font", FONT_REG)
	_title_label.add_theme_font_size_override("font_size", 18)
	_title_label.add_theme_color_override("font_color", C_MUTED)
	_title_label.clip_text = true
	vb.add_child(_title_label)

	var divider := ColorRect.new()
	divider.color = Color(0, 0, 0, 0.12)
	divider.custom_minimum_size = Vector2(0, 1)
	vb.add_child(divider)

	# フォーカスのグロー枠 (共有 1 個。_process で border/shadow を明滅 = 呼吸グロー)。
	# 他画面の白フォーカスと同じく **枠 + グローのリングだけ** (本体は塗らない)。draw_center=false で
	# ボタン本体の塗り (薄い fill) を消し、border + shadow の輪郭グローのみにする。
	_focus_glow_sb = StyleBoxFlat.new()
	_focus_glow_sb.draw_center = false
	_focus_glow_sb.bg_color = Color(0, 0, 0, 0)
	_focus_glow_sb.set_corner_radius_all(14)
	_focus_glow_sb.set_border_width_all(2)
	_focus_glow_sb.border_color = C_FOCUS_GLOW
	_focus_glow_sb.shadow_size = 10                  # グローの広がり
	_focus_glow_sb.shadow_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, 0.4)

	# マウス時の focus は透明 (グローを出さない)。マウスのホバー/クリックは「全体が薄く黒くなる」塗りにする。
	_focus_off_sb = StyleBoxFlat.new()
	_focus_off_sb.bg_color = Color(0, 0, 0, 0)
	_focus_off_sb.set_corner_radius_all(14)
	_hover_sb = StyleBoxFlat.new()
	_hover_sb.bg_color = Color(0, 0, 0, 0.10)
	_hover_sb.set_corner_radius_all(14)
	_press_sb = StyleBoxFlat.new()
	_press_sb.bg_color = Color(0, 0, 0, 0.16)
	_press_sb.set_corner_radius_all(14)

	# 項目リスト
	var list := VBoxContainer.new()
	list.add_theme_constant_override("separation", 6)
	vb.add_child(list)
	_buttons.clear()
	for i in range(ITEMS.size()):
		var btn := _make_item(i, ITEMS[i])
		list.add_child(btn)
		_buttons.append(btn)

	# フッターヒント
	var spacer := Control.new()
	spacer.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vb.add_child(spacer)
	var footer := Label.new()
	footer.text = "HOME をもう一度押すとゲームに戻ります"
	footer.add_theme_font_override("font", FONT_REG)
	footer.add_theme_font_size_override("font_size", 14)
	footer.add_theme_color_override("font_color", C_MUTED)
	vb.add_child(footer)

	# 時計更新タイマー
	_clock_timer = Timer.new()
	_clock_timer.wait_time = 10.0
	_clock_timer.timeout.connect(func(): if _clock_label: _clock_label.text = _now_hhmm())
	add_child(_clock_timer)


## 1 項目 = 番号 + (ラベル/サブ) + KeyCap を持つ Button (キーボード/パッドのフォーカスナビ用)。
func _make_item(index: int, item: Dictionary) -> Button:
	var danger: bool = item.get("danger", false)
	var label_color := C_DANGER if danger else C_TEXT

	var btn := Button.new()
	btn.focus_mode = Control.FOCUS_ALL
	btn.custom_minimum_size = Vector2(0, 76)
	# normal=透明 / hover・pressed=薄黒の塗り (マウス操作のフィードバック) / focus=キーグロー。
	# focus は _set_focus_glow_enabled でキー時=グロー / マウス時=透明 に差し替える (クリックでグローを出さない)。
	var sb_normal := StyleBoxFlat.new()
	sb_normal.bg_color = Color(0, 0, 0, 0)
	sb_normal.set_corner_radius_all(14)
	btn.add_theme_stylebox_override("normal", sb_normal)
	btn.add_theme_stylebox_override("hover", _hover_sb)
	btn.add_theme_stylebox_override("focus", _focus_glow_sb)
	btn.add_theme_stylebox_override("pressed", _press_sb)
	btn.pressed.connect(func(): _activate(item["id"]))

	var hb := HBoxContainer.new()
	hb.set_anchors_preset(Control.PRESET_FULL_RECT)
	hb.add_theme_constant_override("separation", 18)
	hb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hb.offset_left = 18
	hb.offset_right = -18
	btn.add_child(hb)

	var num := Label.new()
	num.text = "%02d" % (index + 1)
	num.add_theme_font_override("font", FONT_REG)
	num.add_theme_font_size_override("font_size", 14)
	num.add_theme_color_override("font_color", C_MUTED)
	num.custom_minimum_size = Vector2(30, 0)
	num.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	hb.add_child(num)

	var texts := VBoxContainer.new()
	texts.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	texts.add_theme_constant_override("separation", 2)
	texts.alignment = BoxContainer.ALIGNMENT_CENTER
	hb.add_child(texts)

	var lbl := Label.new()
	lbl.text = item["label"]
	lbl.add_theme_font_override("font", FONT_BOLD)
	lbl.add_theme_font_size_override("font_size", 26)
	lbl.add_theme_color_override("font_color", label_color)
	texts.add_child(lbl)

	var sub := Label.new()
	sub.text = item["sub"]
	sub.add_theme_font_override("font", FONT_REG)
	sub.add_theme_font_size_override("font_size", 14)
	sub.add_theme_color_override("font_color", C_MUTED)
	texts.add_child(sub)

	return btn


## 入力デバイスに応じてボタンの見た目を切り替え、常に「グロー」か「薄黒塗り」の一方だけにする。
## enabled=true (キー/パッド): focus=グロー、hover/pressed=透明 (薄黒塗りを出さない)。
## enabled=false (マウス): focus=透明 (クリックでグローを出さない)、hover/pressed=薄黒塗り。
## ※ hover はカーソル位置依存で残るため、キーに戻した時に塗りが残ってグローと重なるのを防ぐ。
func _set_focus_glow_enabled(enabled: bool) -> void:
	for b in _buttons:
		if enabled:
			b.add_theme_stylebox_override("focus", _focus_glow_sb)
			b.add_theme_stylebox_override("hover", _focus_off_sb)
			b.add_theme_stylebox_override("pressed", _focus_off_sb)
		else:
			b.add_theme_stylebox_override("focus", _focus_off_sb)
			b.add_theme_stylebox_override("hover", _hover_sb)
			b.add_theme_stylebox_override("pressed", _press_sb)


func _activate(id: String) -> void:
	match id:
		"resume":
			resume_requested.emit()
		"home":
			quit_to_selection_requested.emit()
		"exit":
			exit_to_screensaver_requested.emit()


## 表示: 走行中ゲームのタイトル/サムネを反映し、画面全面を覆って最前面化＋フォーカス取得。
func show_overlay(game_title: String = "", thumb_path: String = "") -> void:
	if _title_label:
		_title_label.text = game_title
	_set_thumbnail(thumb_path)
	if _clock_label:
		_clock_label.text = _now_hhmm()
	if _clock_timer:
		_clock_timer.start()

	# ランチャー本体 (playing) のある画面に合わせて、この透明窓を全面に広げる。
	var scr := get_tree().root.current_screen
	position = DisplayServer.screen_get_position(scr)
	size = DisplayServer.screen_get_size(scr)
	visible = true
	# always_on_top でゲームの上に出るが、初回 show で前面に出ないことがあるため再アサート。
	move_to_foreground()
	# 開いた時点はキー/パッド操作 (HOME/Guide で開く) とみなす: カーソル非表示 + フォーカスグロー表示。
	# マウスを動かすと _input が VISIBLE + focus 透明化に切り替える (他画面と同じ分離)。
	_using_mouse = false
	Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
	_set_focus_glow_enabled(true)
	# 誤決定が安全側になるよう「続ける」を初期フォーカスに。
	if not _buttons.is_empty():
		_buttons[0].grab_focus()


func _set_thumbnail(thumb_path: String) -> void:
	if _icon_tex == null:
		return
	var tex: Texture2D = null
	if thumb_path != "" and FileAccess.file_exists(thumb_path):
		var img := Image.load_from_file(thumb_path)
		if img != null and not img.is_empty():
			tex = ImageTexture.create_from_image(img)
	_icon_tex.texture = tex
	_icon_tex.visible = tex != null
	if _icon_placeholder:
		_icon_placeholder.visible = tex == null


func hide_overlay() -> void:
	visible = false
	if _clock_timer:
		_clock_timer.stop()


## この overlay 窓の OS ネイティブハンドル (Windows: HWND)。companion に渡して overlay 窓だけ前面化する。
func get_overlay_hwnd() -> int:
	return DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE, get_window_id())


func _now_hhmm() -> String:
	var t := Time.get_time_dict_from_system()
	return "%02d:%02d" % [t.hour, t.minute]


func _process(delta: float) -> void:
	# フォーカスのグロー枠を呼吸させる (launcher GlowAnimator の 0.5+0.3*sin と同等、色は濃色版)。
	if not visible or _focus_glow_sb == null:
		return
	_glow_t += delta
	var a: float = 0.45 + 0.3 * sin(_glow_t * 3.0)
	_focus_glow_sb.border_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, a)
	_focus_glow_sb.shadow_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, a * 0.55)


func _input(event: InputEvent) -> void:
	if not visible:
		return
	# 入力デバイスでフォーカス表示を分離 (他画面と同じ):
	#  キー/パッド = カーソル隠す + フォーカスグロー表示 / マウス = カーソル出す + キーフォーカス解除 (グロー消す)。
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
		if _using_mouse:
			_using_mouse = false
			_set_focus_glow_enabled(true)   # キーに戻ったら focus=グローへ
		# フォーカスが無ければ復帰させてグローを出す。
		if get_viewport().gui_get_focus_owner() == null and not _buttons.is_empty():
			_buttons[0].grab_focus()
	elif event is InputEventMouseButton:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		if not _using_mouse:
			_using_mouse = true
			_set_focus_glow_enabled(false)  # マウスは focus=透明 (クリックでグローを出さない)
	elif event is InputEventMouseMotion and event.relative.length() > 1.0:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		if not _using_mouse:
			_using_mouse = true
			_set_focus_glow_enabled(false)
	# Esc / コントローラ B (ui_cancel) で再開 (= 閉じる)。
	if event.is_action_pressed("ui_cancel"):
		set_input_as_handled()
		resume_requested.emit()
