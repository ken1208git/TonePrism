extends Window
## ゲーム実行中の中断メニュー (オーバーレイ) 本体 (#30)。
## 透明・最前面・borderless の実 OS ウィンドウ (project: embed_subwindows=false) として、
## 走っているゲームの上に重ねて表示する。フォーカスを取り、上下＋決定で操作する
## (= 排他入力。ゲームはフォーカス喪失で一時停止しうるがポーズメニューとして許容、設計確定)。
##
## 表示制御・トリガ配線は autoload OverlayManager が行う。本スクリプトは見た目と入力のみ。

signal resume_requested()            ## ▶ ゲームを再開
signal quit_to_selection_requested() ## 🏠 ゲームを終了して選択画面に戻る

## 背景の減光。ゲームは止まるので減光が「停止中」を示し、メニューの可読性も上がる。
@export var dim_alpha: float = 0.5

var _resume_btn: Button = null
var _quit_btn: Button = null


func _ready() -> void:
	# 実 OS ウィンドウとして透明・最前面・枠なしにする (spike #218 で実証済の構成)。
	transparent = true
	borderless = true
	always_on_top = true
	unresizable = true
	visible = false
	_build_ui()


func _build_ui() -> void:
	var root := Control.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(root)

	# 減光レイヤー (全面)
	var dim := ColorRect.new()
	dim.color = Color(0, 0, 0, dim_alpha)
	dim.set_anchors_preset(Control.PRESET_FULL_RECT)
	dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(dim)

	# 中央のメニューパネル
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_child(center)

	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(560, 0)
	center.add_child(panel)

	var margin := MarginContainer.new()
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 36)
	panel.add_child(margin)

	var vb := VBoxContainer.new()
	vb.add_theme_constant_override("separation", 20)
	margin.add_child(vb)

	var title := Label.new()
	title.text = "中断メニュー"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 36)
	vb.add_child(title)

	_resume_btn = _make_button("▶  ゲームを再開")
	_resume_btn.pressed.connect(func(): resume_requested.emit())
	vb.add_child(_resume_btn)

	_quit_btn = _make_button("🏠  ゲームを終了して選択画面に戻る")
	_quit_btn.pressed.connect(func(): quit_to_selection_requested.emit())
	vb.add_child(_quit_btn)


func _make_button(text: String) -> Button:
	var b := Button.new()
	b.text = text
	b.focus_mode = Control.FOCUS_ALL
	b.custom_minimum_size = Vector2(0, 64)
	b.add_theme_font_size_override("font_size", 26)
	return b


## 表示: 走っているゲームのスクリーンを覆い、最前面化＋フォーカス取得。
func show_overlay() -> void:
	var scr := get_tree().root.current_screen
	position = DisplayServer.screen_get_position(scr)
	size = DisplayServer.screen_get_size(scr)
	visible = true
	# spike #218: 初回 show では前面に出ないことがあるため最前面を再アサートする。
	move_to_foreground()
	# 誤決定が安全側になるよう「再開」を初期フォーカスに。
	if _resume_btn:
		_resume_btn.grab_focus()


func hide_overlay() -> void:
	visible = false


## この overlay 窓の OS ネイティブハンドル (Windows: HWND)。companion に渡して
## 「overlay 窓だけ」を前面化するのに使う (メインのランチャー窓を巻き込まないため)。
func get_overlay_hwnd() -> int:
	return DisplayServer.window_get_native_handle(DisplayServer.WINDOW_HANDLE, get_window_id())


func _input(event: InputEvent) -> void:
	if not visible:
		return
	# Esc / コントローラ B (ui_cancel) で再開 (= 閉じる)。
	if event.is_action_pressed("ui_cancel"):
		set_input_as_handled()
		resume_requested.emit()
