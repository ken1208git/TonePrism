extends Window
## ゲーム実行中の中断オーバーレイ (#30)。
## 透明・最前面・borderless の実 OS ウィンドウ (project: embed_subwindows=false) として
## 走っているゲームの上に重ねる。フォーカスを取り上下＋決定で操作する (排他入力)。
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
const C_FOCUS_BG := Color(0, 0, 0, 0.06)
const C_FOCUS_BAR := Color(0.1, 0.1, 0.1)

# ── 既存画面からの完コピ寸法 ──
const RAIL_W := 620.0                          # デザイン B のレール幅
const CLOCK_POS := Vector2(40, 20)             # top_bar: margin_left 40 / margin_top 20
const CLOCK_FONT_SIZE := 32                    # top_bar ClockLabel
const ICON_CENTER_X := 250.0                   # carousel: container 幅500 / 2
const ICON_SIZE := 360.0                       # carousel: CARD_SIZE 200 × SCALE_ACTIVE 1.8
const ICON_RADIUS := 36                        # CORNER_RADIUS 20 × 1.8

const FONT_BOLD := preload("res://fonts/NotoSansJP-Bold.ttf")
const FONT_REG := preload("res://fonts/NotoSansJP-Regular.ttf")

# メニュー項目 (今動くものだけ: 続ける / ホームに戻る / 退出)
const ITEMS := [
	{"id": "resume", "label": "続ける",       "sub": "ゲームに戻る",        "cap": "A"},
	{"id": "home",   "label": "ホームに戻る", "sub": "ゲーム選択画面へ",    "cap": "B"},
	{"id": "exit",   "label": "退出",         "sub": "スクリーンセーバーへ", "cap": "ESC", "danger": true},
]

var _clock_label: Label = null
var _title_label: Label = null
var _icon_tex: TextureRect = null
var _icon_placeholder: Label = null
var _backdrop: TextureRect = null
var _backdrop_sharp: TextureRect = null
var _buttons: Array[Button] = []
var _clock_timer: Timer = null
# フォーカスのグロー枠 (launcher の白グローを濃色アレンジ)。全ボタン共有 1 個を毎フレーム明滅させ、
# Godot は focus 中の Control にだけ focus stylebox を描くので、結果フォーカス行だけが光る。
var _focus_glow_sb: StyleBoxFlat = null
var _glow_t: float = 0.0
const C_FOCUS_GLOW := Color(0.12, 0.12, 0.12)  # 白グローのライト版 = 濃色グロー


func _ready() -> void:
	transparent = true
	borderless = true
	always_on_top = true
	unresizable = true
	visible = false
	# 別 OS ウィンドウ (sub-window) はメイン窓の content スケーリングを継承しないため、
	# project 設定 (base 1920×1080 / canvas_items / expand) と同じ拡大をこの窓にも明示設定する。
	# これで高解像度モニターでも 1920 基準の寸法が画面比に拡大され、時計/アイコンの位置も
	# ランチャー本体と一致し続ける (= 完コピ位置が解像度非依存になる)。
	content_scale_size = Vector2i(1920, 1080)
	content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	content_scale_aspect = Window.CONTENT_SCALE_ASPECT_EXPAND
	_build_ui()


func _build_ui() -> void:
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(root)

	# ── フリーズした全画面背景 (キャプチャ・シャープ): 最下層 ──
	# ゲームは中断中も裏で動き続けることがあり、「ぼかし部=静止 / 非ぼかし部=ライブで動く」のズレが
	# 違和感になる。非ぼかし部も同じ静止フレームで覆い、画面全体を静止させて一貫させる。
	# capture 不在時は非表示 (透過でライブが見える fallback)。
	_backdrop_sharp = TextureRect.new()
	_backdrop_sharp.set_anchors_preset(Control.PRESET_FULL_RECT)
	_backdrop_sharp.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_backdrop_sharp.stretch_mode = TextureRect.STRETCH_SCALE
	_backdrop_sharp.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_backdrop_sharp.visible = false
	root.add_child(_backdrop_sharp)

	# ── 全画面ライト veil: 画面全体を薄い白で覆う (ゲームは薄く透ける、ライトテーマ) ──
	var veil := ColorRect.new()
	veil.color = Color(C_SCRIM.r, C_SCRIM.g, C_SCRIM.b, 0.5)
	veil.set_anchors_preset(Control.PRESET_FULL_RECT)
	veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(veil)

	# ── レール領域の「濃い白の半透明パネル」(ライブゲームが薄く透ける = 濃い白だが透明) ──
	var rail_panel := ColorRect.new()
	rail_panel.color = Color(C_SCRIM.r, C_SCRIM.g, C_SCRIM.b, 0.82)
	rail_panel.anchor_left = 1.0
	rail_panel.anchor_right = 1.0
	rail_panel.anchor_top = 0.0
	rail_panel.anchor_bottom = 1.0
	rail_panel.offset_left = -RAIL_W
	rail_panel.offset_right = 0.0
	rail_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(rail_panel)

	# ── すりガラス背景: 窓いっぱいの TextureRect (STRETCH_SCALE) にキャプチャを敷き、
	# frosted_image シェーダーでぼかし + レール領域だけ表示 (mask_left)。
	# 窓全体に貼ることで実ゲーム (透過で見える) と画素単位で整列する (content-scale 非依存)。
	_backdrop = TextureRect.new()
	_backdrop.set_anchors_preset(Control.PRESET_FULL_RECT)
	_backdrop.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_backdrop.stretch_mode = TextureRect.STRETCH_SCALE
	_backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_backdrop.visible = false
	var mat := ShaderMaterial.new()
	mat.shader = preload("res://shaders/frosted_image.gdshader")
	_backdrop.material = mat
	root.add_child(_backdrop)

	# ── 時計 (top_bar 完コピ位置/サイズ。ライトなので色のみ濃色化) ──
	_clock_label = Label.new()
	_clock_label.add_theme_font_override("font", FONT_REG)
	_clock_label.add_theme_font_size_override("font_size", CLOCK_FONT_SIZE)
	_clock_label.add_theme_color_override("font_color", C_TEXT)
	_clock_label.position = CLOCK_POS
	_clock_label.text = _now_hhmm()
	_clock_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(_clock_label)

	# ── ゲームアイコン (carousel 選択カード完コピ: 中心(250, h/2)・360×360・角丸36) ──
	var icon_panel := Panel.new()
	icon_panel.clip_children = CanvasItem.CLIP_CHILDREN_ONLY
	var icon_sb := StyleBoxFlat.new()
	icon_sb.bg_color = Color(0.1, 0.1, 0.1, 1)
	icon_sb.set_corner_radius_all(ICON_RADIUS)
	icon_panel.add_theme_stylebox_override("panel", icon_sb)
	icon_panel.set_anchors_preset(Control.PRESET_CENTER_LEFT)
	icon_panel.anchor_top = 0.5
	icon_panel.anchor_bottom = 0.5
	icon_panel.offset_left = ICON_CENTER_X - ICON_SIZE / 2.0
	icon_panel.offset_right = ICON_CENTER_X + ICON_SIZE / 2.0
	icon_panel.offset_top = -ICON_SIZE / 2.0
	icon_panel.offset_bottom = ICON_SIZE / 2.0
	icon_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(icon_panel)

	_icon_tex = TextureRect.new()
	_icon_tex.set_anchors_preset(Control.PRESET_FULL_RECT)
	_icon_tex.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_icon_tex.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	_icon_tex.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_panel.add_child(_icon_tex)

	_icon_placeholder = Label.new()
	_icon_placeholder.text = "NO IMAGE"
	_icon_placeholder.add_theme_font_override("font", FONT_BOLD)
	_icon_placeholder.add_theme_font_size_override("font_size", 28)
	_icon_placeholder.add_theme_color_override("font_color", Color(0.5, 0.5, 0.5))
	_icon_placeholder.set_anchors_preset(Control.PRESET_FULL_RECT)
	_icon_placeholder.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_icon_placeholder.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	icon_panel.add_child(_icon_placeholder)

	# ── 右レール (デザイン B を左右反転 → 画面右) ──
	var rail := Control.new()
	rail.anchor_left = 1.0
	rail.anchor_right = 1.0
	rail.anchor_top = 0.0
	rail.anchor_bottom = 1.0
	rail.offset_left = -RAIL_W
	rail.offset_right = 0.0
	rail.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(rail)

	var rail_margin := MarginContainer.new()
	rail_margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	rail_margin.add_theme_constant_override("margin_left", 60)
	rail_margin.add_theme_constant_override("margin_right", 60)
	rail_margin.add_theme_constant_override("margin_top", 100)
	rail_margin.add_theme_constant_override("margin_bottom", 60)
	rail.add_child(rail_margin)

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

	# フォーカスのグロー枠 (共有 1 個。_process で border/shadow を明滅 = 呼吸グロー)
	_focus_glow_sb = StyleBoxFlat.new()
	_focus_glow_sb.bg_color = Color(1, 1, 1, 0.45)   # focus 行を少し明るく浮かせる
	_focus_glow_sb.set_corner_radius_all(12)
	_focus_glow_sb.set_border_width_all(2)
	_focus_glow_sb.border_color = C_FOCUS_GLOW
	_focus_glow_sb.shadow_size = 10                  # グローの広がり
	_focus_glow_sb.shadow_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, 0.4)

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
	# normal: 透明 / hover・focus・pressed: 共有の呼吸グロー枠 (launcher 白グローの濃色版)
	var sb_normal := StyleBoxFlat.new()
	sb_normal.bg_color = Color(0, 0, 0, 0)
	sb_normal.set_corner_radius_all(12)
	btn.add_theme_stylebox_override("normal", sb_normal)
	btn.add_theme_stylebox_override("hover", _focus_glow_sb)
	btn.add_theme_stylebox_override("focus", _focus_glow_sb)
	btn.add_theme_stylebox_override("pressed", _focus_glow_sb)
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

	hb.add_child(_make_keycap(item["cap"]))
	return btn


func _make_keycap(text: String) -> Control:
	var pc := PanelContainer.new()
	pc.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0, 0, 0, 0.07)
	sb.border_color = Color(0, 0, 0, 0.22)
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(6)
	sb.content_margin_left = 8
	sb.content_margin_right = 8
	sb.content_margin_top = 4
	sb.content_margin_bottom = 4
	pc.add_theme_stylebox_override("panel", sb)
	var l := Label.new()
	l.text = text
	l.add_theme_font_override("font", FONT_BOLD)
	l.add_theme_font_size_override("font_size", 16)
	l.add_theme_color_override("font_color", Color(0.2, 0.2, 0.2))
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	pc.add_child(l)
	return pc


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

	var scr := get_tree().root.current_screen
	position = DisplayServer.screen_get_position(scr)
	size = DisplayServer.screen_get_size(scr)
	visible = true
	# すりガラスの表示範囲をレール領域 (右 RAIL_W) に合わせる。content-scale 後の実ビューポート幅で
	# 割ることで、高解像度/アスペクト非依存にレール左端 UV を決める。
	if _backdrop and _backdrop.material is ShaderMaterial:
		var vp_w: float = get_viewport().get_visible_rect().size.x
		var mask_left: float = (vp_w - RAIL_W) / vp_w if vp_w > 0.0 else 0.0
		(_backdrop.material as ShaderMaterial).set_shader_parameter("mask_left", mask_left)
	# spike #218: 初回 show では前面に出ないことがあるため最前面を再アサート。
	move_to_foreground()
	# 誤決定が安全側になるよう「続ける」を初期フォーカスに。
	if not _buttons.is_empty():
		_buttons[0].grab_focus()


## キャプチャ画像 (companion 撮影) を背景に敷く。全画面シャープ (フリーズ) + レール背面すりガラスの両方に。
## 空/失敗時は両方非表示 (透過でライブが見える fallback)。
func set_backdrop(path: String) -> void:
	var tex: Texture2D = null
	if path != "" and FileAccess.file_exists(path):
		var img := Image.load_from_file(path)
		if img != null and not img.is_empty():
			tex = ImageTexture.create_from_image(img)
	if _backdrop:
		_backdrop.texture = tex
		_backdrop.visible = tex != null
	if _backdrop_sharp:
		_backdrop_sharp.texture = tex
		_backdrop_sharp.visible = tex != null


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
	# Esc / コントローラ B (ui_cancel) で再開 (= 閉じる)。
	if event.is_action_pressed("ui_cancel"):
		set_input_as_handled()
		resume_requested.emit()
