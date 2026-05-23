extends CanvasLayer
## ゲーム実行中の中断オーバーレイ (#30 / #214)。
## 旧実装は透明・最前面の別 OS ウィンドウ (sub-window) だったが、別 viewport ゆえ既存の
## フォーカス/入力処理を共有できず、ライブゲームを透かすために画面キャプチャ機構も必要だった。
## #214 のシーン分割 (プレイ中は軽量 playing シーン) に合わせ、本オーバーレイも **メイン窓内の
## 単一 CanvasLayer** へ作り替え、同 viewport で既存のフォーカス/入力がそのまま効くようにする。
##
## デザイン: Claude Design "B · Side Rail" を **左右反転 (ボタンを画面右)** + **ライトテーマ**化。
## ライトテーマは「ゲーム起動〜終了はライト」方針 (launching_overlay の白オーバーレイ) に合わせる。
## 時計はカルーセル画面 (top_bar) の、ゲームアイコンはゲーム起動中画面 (carousel 選択カード) の
## 位置・サイズを踏襲する (画面間の連続性)。
##
## 表示制御・トリガ配線は autoload OverlayManager。本スクリプトは見た目と入力のみ。
## メイン窓の前面化 (プレイ中はゲーム窓が前面なので overlay を見せるため必要) と、HOME 時の
## ライブゲーム透過 (窓透明化 + playing 背景アート非表示) は OverlayManager / playing 側が担う。

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
const ICON_RADIUS := 36                        # CORNER_RADIUS 20 × 1.8

# overlay の描画レイヤー。playing シーン (layer 0 相当) より前、TransitionManager (layer 100) より後ろ。
const OVERLAY_LAYER := 80

const FONT_BOLD := preload("res://fonts/NotoSansJP-Bold.ttf")
const FONT_REG := preload("res://fonts/NotoSansJP-Regular.ttf")

# メニュー項目 (今動くものだけ: 続ける / ホームに戻る / 退出)
const ITEMS := [
	{"id": "resume", "label": "続ける",       "sub": "ゲームに戻る",        "cap": "A"},
	{"id": "home",   "label": "ホームに戻る", "sub": "ゲーム選択画面へ",    "cap": "B"},
	{"id": "exit",   "label": "退出",         "sub": "スクリーンセーバーへ", "cap": "ESC", "danger": true},
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
var _focus_glow_sb: StyleBoxFlat = null
var _glow_t: float = 0.0
const C_FOCUS_GLOW := Color(0.12, 0.12, 0.12)  # 白グローのライト版 = 濃色グロー


func _ready() -> void:
	layer = OVERLAY_LAYER
	visible = false
	# 中断中に tree.paused にしても glow/入力を効かせる。
	process_mode = Node.PROCESS_MODE_ALWAYS
	_build_ui()


func _build_ui() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	# 背後の playing シーンへクリックが抜けないよう STOP で受け止める。
	_root.mouse_filter = Control.MOUSE_FILTER_STOP
	# 入力ゲートは Control.visible を真実の源にする。CanvasLayer.visible は描画は止めるが配下 Control の
	# 入力を止める保証がなく、非表示中に全画面 STOP の本 Control が裏のクリックを食う事故を防ぐため。
	_root.visible = false
	add_child(_root)

	# ── 全画面ライト veil: 画面全体を薄い白で覆う (ライトテーマ。4b で窓透明化するとライブゲームが薄く透ける) ──
	var veil := ColorRect.new()
	veil.color = Color(C_SCRIM.r, C_SCRIM.g, C_SCRIM.b, 0.5)
	veil.set_anchors_preset(Control.PRESET_FULL_RECT)
	veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(veil)

	# ── レール領域の「濃い白の半透明パネル」(4b でライブゲームが薄く透ける = 濃い白だが透明) ──
	var rail_panel := ColorRect.new()
	rail_panel.color = Color(C_SCRIM.r, C_SCRIM.g, C_SCRIM.b, 0.82)
	rail_panel.anchor_left = 1.0
	rail_panel.anchor_right = 1.0
	rail_panel.anchor_top = 0.0
	rail_panel.anchor_bottom = 1.0
	rail_panel.offset_left = -RAIL_W
	rail_panel.offset_right = 0.0
	rail_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
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
	_root.add_child(icon_panel)

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
	_root.add_child(rail)

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


## 表示: 走行中ゲームのタイトル/サムネを反映し、フォーカスを取得する。
## メイン窓の前面化 (プレイ中はゲーム窓が前面) は OverlayManager が companion 経由で行う。
func show_overlay(game_title: String = "", thumb_path: String = "") -> void:
	if _title_label:
		_title_label.text = game_title
	_set_thumbnail(thumb_path)
	if _clock_label:
		_clock_label.text = _now_hhmm()
	if _clock_timer:
		_clock_timer.start()
	visible = true       # CanvasLayer: 描画 ON
	_root.visible = true # Control: 描画 + 入力 ON
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
	if _root:
		_root.visible = false
	if _clock_timer:
		_clock_timer.stop()


func _now_hhmm() -> String:
	var t := Time.get_time_dict_from_system()
	return "%02d:%02d" % [t.hour, t.minute]


func _process(delta: float) -> void:
	# フォーカスのグロー枠を呼吸させる (launcher GlowAnimator の 0.5+0.3*sin と同等、色は濃色版)。
	if _root == null or not _root.visible or _focus_glow_sb == null:
		return
	_glow_t += delta
	var a: float = 0.45 + 0.3 * sin(_glow_t * 3.0)
	_focus_glow_sb.border_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, a)
	_focus_glow_sb.shadow_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, a * 0.55)


func _input(event: InputEvent) -> void:
	if _root == null or not _root.visible:
		return
	# Esc / コントローラ B (ui_cancel) で再開 (= 閉じる)。
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		resume_requested.emit()
