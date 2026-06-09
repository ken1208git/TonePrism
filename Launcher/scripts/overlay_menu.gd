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
const ICON_CENTER_X := 250.0                   # carousel: container 幅500 / 2
const ICON_SIZE := 360.0                       # carousel: CARD_SIZE 200 × SCALE_ACTIVE 1.8
const ICON_RADIUS := 29                        # カルーセルのカードbg角丸 16 × 1.8 ≈ 29 (影と角丸をカード厳密一致)

# ── 開閉アニメ ──
# 速度は起動/復帰モーション (game_launcher.LAUNCH_TRANSITION_DURATION = 0.55, QUINT/EASE_OUT) と統一。
const ANIM_OPEN_DUR := 0.55    # 開く: パネルが少し右からフェードイン / アイコン拡大フェード / veil フェード
const ANIM_CLOSE_DUR := 0.45   # 閉じる: 逆再生 (少しだけ速め)
const PANEL_SLIDE := 90.0      # パネルの移動量 (完全な画面外でなく途中から出る)
const ICON_POP_SCALE := 0.85   # アイコンのポップ開始スケール (拡大のみ、バウンス無し)
const VEIL_ALPHA := 0.5        # veil の不透明度 (resting)
const BG_ZOOM := 1.05          # 終了中の背景アート拡大率 (playing.BG_ZOOM と一致 = handoff の連続性)
const QUIT_ZOOM := 1.05        # 終了中 morph の登場ズーム率 (TransitionManager のブラウズ→カルーセルと同じ 1.05→等倍)

const FONT_BOLD := preload("res://fonts/NotoSansJP-Bold.ttf")
const FONT_REG := preload("res://fonts/NotoSansJP-Regular.ttf")

# メニュー項目 (今動くものだけ: 続ける / 別のゲームをあそぶ / 退出する)
const ITEMS := [
	{"id": "resume", "label": "続ける",           "sub": "ゲームに戻る"},
	{"id": "home",   "label": "別のゲームをあそぶ", "sub": "ゲームを終了して選択画面に戻る"},
	{"id": "exit",   "label": "退出する",          "sub": "プレイを終了して席を離れる", "danger": true},
]

var _root: Control = null
var _bg: TextureRect = null          # 終了中の背景アート (普段は透明=ライブゲームが透ける、終了中だけフェードイン)
var _veil: ColorRect = null          # 全画面の白 veil (開閉でフェード)
var _rail_panel: Panel = null        # 右の角丸パネル (開閉で右からフェード)
var _icon_panel: Panel = null        # ゲームアイコン (開閉で拡大ポップ)
var _quitting_overlay: LaunchingOverlay = null  # 終了中表示 (カルーセル窓側と同じ launching_overlay を流用)
var _clock_label: Label = null
var _date_label: Label = null    # 時計の下の小さい日付 + 曜日 (例: 2026/05/25 MON)
var _title_label: Label = null
var _icon_tex: TextureRect = null
var _icon_placeholder: Control = null  # (#316) no-image 共通プレースホルダ (NoImagePlaceholder)
var _buttons: Array[Button] = []
var _clock_timer: Timer = null
var _anim_tween: Tween = null        # 開閉アニメ用 (再トリガ時は kill して作り直す)
# フォーカスのグロー枠 (launcher の白グローを濃色アレンジ)。ブラウズ画面と同じ「1 枚の可動グロー枠」方式:
# Godot の per-button focus stylebox (瞬間ジャンプ) は使わず、フォーカス中ボタンの矩形へ毎フレーム lerp
# 追従する Panel を 1 枚重ねる (移動がぬるぬる)。初回出現はズームイン pop (ブラウズ準拠)。
var _focus_glow_sb: StyleBoxFlat = null  # 可動グロー枠の stylebox (枠リング、_process で明滅)
var _focus_off_sb: StyleBoxFlat = null   # ボタンの focus stylebox (常に透明 = グローは可動枠が担当)
var _hover_sb: StyleBoxFlat = null       # マウスホバー時の薄黒塗り
var _press_sb: StyleBoxFlat = null       # マウスクリック時の薄黒塗り (グロー無し)
var _glow_t: float = 0.0
var _using_mouse: bool = false  # マウス操作中はキーフォーカスのグローを出さない (他画面と同じ分離)
# 可動グロー枠 (ブラウズの FocusBorder 準拠)。
var _focus_border: Panel = null
var _focus_target: Control = null        # 追従先 (現在フォーカス中のボタン)
var _focus_target_rect: Rect2 = Rect2()
var _focus_initialized: bool = false     # 初回出現の pop 済みか (移動時は lerp、出現時のみ pop)
var _focus_tweening: bool = false        # pop tween 中は lerp を止める
var _focus_pop_tween: Tween = null       # 出現 pop 用 (再 pop / 非表示時に kill して取り残しを防ぐ)
var _menu_ready: bool = false            # 開アニメが完了し可動枠を出してよいか (開ききる前/閉じ中は枠を出さない)
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
	# tree.paused (ダイアログ等) でも glow アニメ/入力処理を止めない (本窓も同じ tree に属するため防御的に)。
	process_mode = Node.PROCESS_MODE_ALWAYS
	_build_ui()


func _build_ui() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(_root)

	# ── 終了中の背景アート (最背面)。普段は非表示で透明窓=ライブゲームがそのまま透ける。show_quitting で
	# 走行中ゲームの background を playing/カルーセルと同じ拡大率でフェードインし、handoff をシームレスにする。
	var bg := TextureRect.new()
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	bg.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bg.visible = false
	bg.modulate.a = 0.0
	_root.add_child(bg)
	_bg = bg

	# ── 全画面ライト veil: 画面全体を薄い白で覆う (ライトテーマ。透明窓なのでライブゲームが薄く透ける) ──
	var veil := ColorRect.new()
	veil.color = Color(C_SCRIM.r, C_SCRIM.g, C_SCRIM.b, 0.5)
	veil.set_anchors_preset(Control.PRESET_FULL_RECT)
	veil.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(veil)
	_veil = veil

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
	_rail_panel = rail_panel

	# ── 時計 (パネル右下、大きめ) + その下に小さく日付・曜日。パネルの子なので開閉でパネルと一緒に動く ──
	var clock_box := VBoxContainer.new()
	clock_box.anchor_left = 1.0
	clock_box.anchor_right = 1.0
	clock_box.anchor_top = 1.0
	clock_box.anchor_bottom = 1.0
	clock_box.offset_left = -320.0
	clock_box.offset_right = -36.0
	clock_box.offset_top = -150.0
	clock_box.offset_bottom = -24.0
	clock_box.alignment = BoxContainer.ALIGNMENT_END  # 下寄せ (パネル右下に張り付く)
	clock_box.add_theme_constant_override("separation", -6)  # 時計と日付を近づける
	clock_box.mouse_filter = Control.MOUSE_FILTER_IGNORE
	rail_panel.add_child(clock_box)

	_clock_label = Label.new()
	_clock_label.add_theme_font_override("font", FONT_REG)
	_clock_label.add_theme_font_size_override("font_size", 72)
	_clock_label.add_theme_color_override("font_color", C_TEXT)
	_clock_label.text = _now_hhmm()
	_clock_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_clock_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	clock_box.add_child(_clock_label)

	_date_label = Label.new()
	_date_label.add_theme_font_override("font", FONT_REG)
	_date_label.add_theme_font_size_override("font_size", 22)
	_date_label.add_theme_color_override("font_color", C_MUTED)
	_date_label.text = _now_date()
	_date_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_date_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	clock_box.add_child(_date_label)

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
	icon_panel.pivot_offset = Vector2(ICON_SIZE, ICON_SIZE) / 2.0  # 中心基準でポップ
	icon_panel.z_index = 100  # 終了中の launching_overlay veil (z=50) より前面に (カルーセルのサムネと同じ)
	_root.add_child(icon_panel)
	_icon_panel = icon_panel

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

	# (#316) no-image はカルーセルと見た目を揃えた「明るいグレーの箱 + 灰字 NO IMAGE」に統一 (旧: 暗い icon_clip 地 +
	# 灰字ラベル)。暗い loading 表示との混同を避けるため明るい箱にする。_set_thumbnail で表示/非表示を切替。
	_icon_placeholder = NoImagePlaceholder.make(ICON_RADIUS, 28)
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
	kicker.text = "中断メニュー"
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

	# 終了中表示 (別のゲーム/退出 選択時)。カルーセル窓側と同じ launching_overlay を流用して見た目を揃え、
	# handoff (overlay → メイン窓) をシームレスにする。普段は非表示、show_quitting で QUITTING を出す。
	_quitting_overlay = preload("res://scenes/components/launching_overlay.tscn").instantiate()
	_root.add_child(_quitting_overlay)

	# ── 可動グロー枠 (ブラウズ FocusBorder 準拠): フォーカス中ボタンの矩形へ lerp 追従する 1 枚 ──
	# Godot の per-button focus stylebox (瞬間移動) ではなくこの枠を動かすことで、移動が滑らかになる。
	_focus_border = Panel.new()
	_focus_border.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_focus_border.visible = false
	_focus_border.add_theme_stylebox_override("panel", _focus_glow_sb)
	_focus_border.z_index = 60  # ボタン (rail_panel 内 z=0) より前面、終了中 veil (z=50) より前面
	_root.add_child(_focus_border)

	# 時計更新タイマー
	_clock_timer = Timer.new()
	_clock_timer.wait_time = 10.0
	_clock_timer.timeout.connect(func(): _refresh_clock())
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
	btn.add_theme_stylebox_override("focus", _focus_off_sb)  # グローは可動枠 (_focus_border) が担当
	btn.add_theme_stylebox_override("pressed", _press_sb)
	btn.pressed.connect(func(): _activate(item["id"]))
	# フォーカスが乗ったら可動グロー枠の追従先を更新 (移動は _process が lerp で滑らかに)。
	btn.focus_entered.connect(func(): _focus_target = btn)

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
	# ボタンの focus は常に透明 (グローは可動枠 _focus_border が描く)。差は hover/pressed の塗りと可動枠の表示有無。
	for b in _buttons:
		b.add_theme_stylebox_override("focus", _focus_off_sb)
		if enabled:
			b.add_theme_stylebox_override("hover", _focus_off_sb)
			b.add_theme_stylebox_override("pressed", _focus_off_sb)
		else:
			b.add_theme_stylebox_override("hover", _hover_sb)
			b.add_theme_stylebox_override("pressed", _press_sb)
	if not enabled and _focus_border:
		# マウス時は可動グロー枠を隠す (次にキーへ戻った時 _focus_initialized=false から pop し直す)。
		_focus_border.visible = false
		_focus_initialized = false


func _activate(id: String) -> void:
	match id:
		"resume":
			resume_requested.emit()
		"home":
			quit_to_selection_requested.emit()
		"exit":
			exit_to_screensaver_requested.emit()


## 表示: 走行中ゲームのタイトル/サムネを反映し、画面全面を覆って最前面化＋フォーカス取得。
func show_overlay(game_title: String = "", thumb_path: String = "", screen: int = -1) -> void:
	if _title_label:
		_title_label.text = game_title
	_set_thumbnail(thumb_path)
	_refresh_clock()
	if _clock_timer:
		_clock_timer.start()
	# メニュー表示なので終了中表示は隠す (前回の終了中が残っていた場合の保険)。
	if _quitting_overlay:
		_quitting_overlay.visible = false
	# 背景アートも隠す (メニューは透明窓=ライブゲームを透かす。背景は終了中 morph 時のみ出す)。
	if _bg:
		_bg.visible = false
		_bg.modulate.a = 0.0

	# ゲーム窓のいるモニタ (OverlayManager が解決して渡す) に合わせて、この透明窓を全面に広げる。
	# screen 未指定 (-1) はランチャーのいる画面にフォールバック (単体テスト等)。
	var scr := screen if screen >= 0 else get_tree().root.current_screen
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
	_play_open_anim()


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


## 終了中の背景アートを読み込む (playing._load_into と同等)。取得できなければ texture=null のまま。
func _set_background(bg_path: String) -> void:
	if _bg == null:
		return
	var tex: Texture2D = null
	if bg_path != "" and FileAccess.file_exists(bg_path):
		var img := Image.load_from_file(bg_path)
		if img != null and not img.is_empty():
			tex = ImageTexture.create_from_image(img)
	_bg.texture = tex


func hide_overlay() -> void:
	if _clock_timer:
		_clock_timer.stop()
	_play_close_anim()  # アニメ完了後に visible=false


## 退場アニメの長さ (OverlayManager が「アニメ完了までゲームを前面に戻さない」待ち合わせに使う)。
func get_close_anim_duration() -> float:
	return ANIM_CLOSE_DUR


## 別のゲーム/退出 選択時: メニューを退場させつつ「ゲーム終了中…」(カルーセル窓側と同じ launching_overlay)
## をフェードインして morph する。閉じずに前面のまま (フォーカス移動なし=一瞬で切替が起きない、#214)。
func show_quitting(game_title: String, bg_path: String = "") -> void:
	if _anim_tween and _anim_tween.is_valid():
		_anim_tween.kill()
	if _clock_timer:
		_clock_timer.stop()
	_hide_focus_border()
	var center := Vector2(1920, 1080) / 2.0
	# 終了中表示をフェードイン (launching_overlay 側が modulate をフェード)。
	# ブラウズ→カルーセルと同じズーム登場: 終了中の veil+ラベルを QUIT_ZOOM 倍から等倍へ縮めながら出す。
	# アイコン (z=100、メニューから据え置き) はズームさせない (急な拡大ジャンプを避ける anchor)。
	if _quitting_overlay:
		_quitting_overlay.modulate.a = 0.0  # 再 quit でも必ずフェードし直す
		_quitting_overlay.pivot_offset = center
		_quitting_overlay.scale = Vector2(QUIT_ZOOM, QUIT_ZOOM)
		_quitting_overlay.show_for_game(game_title, LaunchingOverlay.State.QUITTING)
	# 走行中ゲームの背景アートを playing/カルーセルと同じ拡大率 (BG_ZOOM) へ着地させつつフェードイン。
	# 登場は同じ視覚ズーム率 (BG_ZOOM × QUIT_ZOOM) から BG_ZOOM へ縮める (veil と同率のブラウズ風ズーム)。
	# texture が取れた時だけ可視化 (background 未設定なら従来どおりライブゲームが透ける)。
	if _bg:
		_set_background(bg_path)
		_bg.pivot_offset = center
		_bg.scale = Vector2(BG_ZOOM * QUIT_ZOOM, BG_ZOOM * QUIT_ZOOM)
		_bg.modulate.a = 0.0
		_bg.visible = _bg.texture != null
	_anim_tween = create_tween()
	_anim_tween.set_parallel(true)
	# ブラウズ→カルーセルのズーム遷移と同じ feel (TRANS_CUBIC / EASE_OUT で 1.05→等倍)。
	if _quitting_overlay:
		_anim_tween.tween_property(_quitting_overlay, "scale", Vector2.ONE, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	if _bg and _bg.visible:
		_anim_tween.tween_property(_bg, "scale", Vector2(BG_ZOOM, BG_ZOOM), ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_bg, "modulate:a", 1.0, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	# レールメニューを退場 (右へ + フェード)。自前 veil は launching_overlay の veil に引き継ぐためフェードアウト。
	if _rail_panel:
		_anim_tween.tween_property(_rail_panel, "offset_left", -RAIL_W + PANEL_SLIDE, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_rail_panel, "offset_right", PANEL_SLIDE, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_rail_panel, "modulate:a", 0.0, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if _veil:
		_anim_tween.tween_property(_veil, "color:a", 0.0, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)


## handoff 用の即時非表示 (game_exited 時。裏のメイン窓が同じ終了中を出しているので閉じアニメ不要)。
func hide_now() -> void:
	if _anim_tween and _anim_tween.is_valid():
		_anim_tween.kill()
	if _clock_timer:
		_clock_timer.stop()
	if _quitting_overlay:
		_quitting_overlay.visible = false
	if _bg:
		_bg.visible = false
		_bg.modulate.a = 0.0
	_hide_focus_border()
	visible = false


## 開く: veil フェードイン + パネル(時計含む)を少し右からフェードイン + アイコンを拡大フェードイン。
## どれも「完全な画面外から」ではなく途中位置からフェードと一緒に出す (バウンス無し)。
func _play_open_anim() -> void:
	if _anim_tween and _anim_tween.is_valid():
		_anim_tween.kill()
	# 開ききるまで可動グロー枠を出さない (スライド中の誤位置 pop を防ぐ)。
	_menu_ready = false
	_hide_focus_border()
	# 開始状態 (少しずれ / 縮小 / 透明) を明示セット (resting のチラ見え防止)。
	if _veil:
		_veil.color.a = 0.0
	if _rail_panel:
		_rail_panel.offset_left = -RAIL_W + PANEL_SLIDE
		_rail_panel.offset_right = PANEL_SLIDE
		_rail_panel.modulate.a = 0.0
	if _icon_panel:
		_icon_panel.scale = Vector2(ICON_POP_SCALE, ICON_POP_SCALE)
		_icon_panel.modulate.a = 0.0
	_anim_tween = create_tween()
	_anim_tween.set_parallel(true)
	if _veil:
		_anim_tween.tween_property(_veil, "color:a", VEIL_ALPHA, ANIM_OPEN_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if _rail_panel:
		_anim_tween.tween_property(_rail_panel, "offset_left", -RAIL_W, ANIM_OPEN_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_rail_panel, "offset_right", 0.0, ANIM_OPEN_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_rail_panel, "modulate:a", 1.0, ANIM_OPEN_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if _icon_panel:
		_anim_tween.tween_property(_icon_panel, "scale", Vector2.ONE, ANIM_OPEN_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_icon_panel, "modulate:a", 1.0, ANIM_OPEN_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	# 開ききったら可動グロー枠を解禁 (定位置に収まった状態で正しい位置に pop させる)。
	_anim_tween.chain().tween_callback(func(): _menu_ready = true)


## 閉じる: 開くの逆再生 (パネル少し右へフェードアウト / アイコン縮小フェード / veil フェードアウト) → 完了後に窓を隠す。
func _play_close_anim() -> void:
	if _anim_tween and _anim_tween.is_valid():
		_anim_tween.kill()
	_hide_focus_border()
	_anim_tween = create_tween()
	_anim_tween.set_parallel(true)
	if _veil:
		_anim_tween.tween_property(_veil, "color:a", 0.0, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if _rail_panel:
		_anim_tween.tween_property(_rail_panel, "offset_left", -RAIL_W + PANEL_SLIDE, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_rail_panel, "offset_right", PANEL_SLIDE, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_rail_panel, "modulate:a", 0.0, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if _icon_panel:
		_anim_tween.tween_property(_icon_panel, "scale", Vector2(ICON_POP_SCALE, ICON_POP_SCALE), ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_anim_tween.tween_property(_icon_panel, "modulate:a", 0.0, ANIM_CLOSE_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	_anim_tween.chain().tween_callback(func(): visible = false)


## この overlay 窓の OS ネイティブハンドル (Windows: HWND)。companion に渡して overlay 窓だけ前面化する。
func get_overlay_hwnd() -> int:
	return DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE, get_window_id())


## 可動グロー枠を隠す (閉じる/終了中 morph/handoff 時。次の表示で pop し直す)。
func _hide_focus_border() -> void:
	# pop tween を kill (kill は finished を発火しないので _focus_tweening は明示クリア)。
	if _focus_pop_tween and _focus_pop_tween.is_valid():
		_focus_pop_tween.kill()
	_focus_tweening = false
	if _focus_border:
		_focus_border.visible = false
	_focus_initialized = false
	_menu_ready = false  # 閉じ/morph 後に _process が枠を再出現させないようロック


func _now_hhmm() -> String:
	var t := Time.get_time_dict_from_system()
	return "%02d:%02d" % [t.hour, t.minute]


## 日付 + 曜日 (例: 2026/05/25 MON)。weekday は 0=日 .. 6=土。
func _now_date() -> String:
	const WD := ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"]
	var d := Time.get_date_dict_from_system()
	return "%04d/%02d/%02d %s" % [d.year, d.month, d.day, WD[d.weekday]]


func _refresh_clock() -> void:
	if _clock_label:
		_clock_label.text = _now_hhmm()
	if _date_label:
		_date_label.text = _now_date()


func _process(delta: float) -> void:
	if not visible:
		return
	# 可動グロー枠をフォーカス中ボタンへ追従させる (ブラウズ準拠: 出現は pop、移動は lerp)。
	_update_focus_border(delta)
	# フォーカスのグロー枠を呼吸させる (launcher GlowAnimator の 0.5+0.3*sin と同等、色は濃色版)。
	# 枠が見えている時だけ明滅 (マウス時など非表示なら無駄に動かさない)。
	if _focus_glow_sb and _focus_border and _focus_border.visible:
		_glow_t += delta
		var a: float = 0.45 + 0.3 * sin(_glow_t * 3.0)
		_focus_glow_sb.border_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, a)
		_focus_glow_sb.shadow_color = Color(C_FOCUS_GLOW.r, C_FOCUS_GLOW.g, C_FOCUS_GLOW.b, a * 0.55)


## 可動グロー枠の追従 (ブラウズ store_browse の _sync_focus_position / _process と同じ要領)。
## 初回 (または再表示) はターゲット矩形へスナップしてズームイン pop、以降は毎フレーム lerp で滑らかに移動。
func _update_focus_border(delta: float) -> void:
	if _focus_border == null:
		return
	# 開ききる前 (_menu_ready=false) / 閉じ中 / マウス時 / ターゲット無効時は隠す。
	# → メニューが定位置に収まってから正しい位置に pop し、閉じる時は再出現しない。
	if not _menu_ready or _using_mouse or _focus_target == null or not is_instance_valid(_focus_target):
		if _focus_border.visible:
			_focus_border.visible = false
		_focus_initialized = false
		return
	_focus_target_rect = (_focus_target as Control).get_global_rect()
	if not _focus_initialized:
		# 初回出現: 位置/サイズをスナップしてから scale 1.15→1.0 + フェードイン (ブラウズと同じ 0.25s CUBIC)。
		_focus_border.global_position = _focus_target_rect.position
		_focus_border.size = _focus_target_rect.size
		_focus_border.visible = true
		_focus_initialized = true
		_focus_tweening = true
		_focus_border.pivot_offset = _focus_target_rect.size / 2.0
		_focus_border.scale = Vector2(1.15, 1.15)
		_focus_border.modulate.a = 0.0
		# 直前の pop tween が生きていれば kill (マウス↔キー高速切替で取り残しが scale/modulate を奪い合うのを防ぐ)。
		if _focus_pop_tween and _focus_pop_tween.is_valid():
			_focus_pop_tween.kill()
		_focus_pop_tween = create_tween()
		_focus_pop_tween.set_parallel(true)
		_focus_pop_tween.tween_property(_focus_border, "scale", Vector2.ONE, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_focus_pop_tween.tween_property(_focus_border, "modulate:a", 1.0, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_focus_pop_tween.finished.connect(func(): _focus_tweening = false)
		return
	# pop 中は lerp しない (pop の動きと干渉させない)。
	if _focus_tweening:
		return
	# フォーカス移動を lerp で補間 (ブラウズと同じ速度感 delta*25)。
	var speed: float = delta * 25.0
	_focus_border.global_position = _focus_border.global_position.lerp(_focus_target_rect.position, speed)
	_focus_border.size = _focus_border.size.lerp(_focus_target_rect.size, speed)


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
