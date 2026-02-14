extends Control

class_name CommonDialog

signal button_pressed(button_index: int)

@onready var _title_label: Label = $Panel/MarginContainer/VBoxContainer/TitleLabel
@onready var _message_label: Label = $Panel/MarginContainer/VBoxContainer/MessageLabel
@onready var _button_container: HBoxContainer = $Panel/MarginContainer/VBoxContainer/ButtonContainer

var _buttons: Array[Button] = []

var _glow_styles: Array[StyleBoxFlat] = []
var _glow_timer: float = 0.0

func _process(delta):
	# グローアニメーション（ブリージング）
	_glow_timer += delta
	var glow_alpha = 0.5 + 0.3 * sin(_glow_timer * 3.0)
	
	for style in _glow_styles:
		if style:
			var c = Color(1, 1, 1, glow_alpha)
			style.shadow_color = c
			style.border_color = c

func _ready():
	_title_label.text = ""
	_message_label.text = ""
	
	# ダイアログの背景スタイル設定（不透明・角丸）
	# PanelContainerのStyleBoxを上書き
	var panel = $Panel
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.15, 0.15, 0.15, 1.0) # 不透明なダークグレー
	style.set_corner_radius_all(20)
	style.border_width_bottom = 2
	style.border_width_left = 2
	style.border_width_right = 2
	style.border_width_top = 2
	style.border_color = Color(0.4, 0.4, 0.4)
	
	panel.add_theme_stylebox_override("panel", style)

func setup(title: String, message: String):
	if not is_node_ready():
		await ready
	
	_title_label.text = title
	_message_label.text = message
	
	if _message_label:
		_message_label.text = message
	
	# ボタンコンテナをクリア
	for child in _button_container.get_children():
		child.queue_free()
	_buttons.clear()
	_glow_styles.clear()

func set_message(message: String):
	if _message_label:
		_message_label.text = message

func add_button(text: String, callback: Callable = Callable(), should_grab_focus: bool = false, color_override: Color = Color.TRANSPARENT):
	if not is_node_ready():
		await ready

	var btn = Button.new()
	btn.text = text
	btn.add_theme_font_override("font", load("res://fonts/NotoSansJP-Regular.ttf"))
	btn.add_theme_font_size_override("font_size", 24)
	btn.custom_minimum_size = Vector2(160, 50)
	
	if color_override != Color.TRANSPARENT:
		# スタイルボックスをオーバーライド
		var style_normal = StyleBoxFlat.new()
		style_normal.bg_color = color_override
		style_normal.set_corner_radius_all(10)
		btn.add_theme_stylebox_override("normal", style_normal)
		
		# ホバー時は少し明るく
		var style_hover = style_normal.duplicate()
		style_hover.bg_color = color_override.lightened(0.1)
		btn.add_theme_stylebox_override("hover", style_hover)
		
		# Focusは別スタイル（白枠）
		var style_focus = StyleBoxFlat.new()
		style_focus.bg_color = Color.TRANSPARENT
		style_focus.draw_center = false
		style_focus.border_width_left = 0
		style_focus.border_width_top = 0
		style_focus.border_width_right = 0
		style_focus.border_width_bottom = 0
		style_focus.border_color = Color.WHITE
		
		# 光彩（グロー）効果を追加
		style_focus.shadow_color = Color(1, 1, 1, 0.5)
		style_focus.shadow_size = 12
		style_focus.shadow_offset = Vector2(0, 0)
		
		_glow_styles.append(style_focus) # アニメーション対象に追加
		
		# 少し外側に広げる
		# マージンや角丸はボタンのサイズや元の角丸(10)に合わせて調整
		var focus_margin = 6
		style_focus.expand_margin_left = focus_margin
		style_focus.expand_margin_right = focus_margin
		style_focus.expand_margin_top = focus_margin
		style_focus.expand_margin_bottom = focus_margin
		style_focus.set_corner_radius_all(10 + focus_margin)
		
		btn.add_theme_stylebox_override("focus", style_focus)
		
		# 押下時は暗く
		var style_pressed = style_normal.duplicate()
		style_pressed.bg_color = color_override.darkened(0.2)
		btn.add_theme_stylebox_override("pressed", style_pressed)
	
	var index = _buttons.size()
	btn.pressed.connect(func():
		if callback.is_valid():
			callback.call()
		button_pressed.emit(index)
		# 基本的にボタンを押したら閉じるかどうかは呼び出し元が決めるが
		# DialogManager経由の場合は閉じる処理が含まれることが多い
	)
	
	_button_container.add_child(btn)
	_buttons.append(btn)
	
	# フォーカストラップ（ボタン間の循環）を設定
	_update_focus_neighbors()
	
	if should_grab_focus:
		btn.grab_focus()

func _update_focus_neighbors():
	if _buttons.is_empty(): return
	
	var count = _buttons.size()
	for i in range(count):
		var btn = _buttons[i]
		var prev_btn = _buttons[(i - 1 + count) % count]
		var next_btn = _buttons[(i + 1) % count]
		
		# 左右で循環させる
		btn.focus_neighbor_left = prev_btn.get_path()
		btn.focus_neighbor_right = next_btn.get_path()
		
		# 上下は自分自身にして外に出さない（あるいは左右と同じ挙動にする）
		# ここでは外に出さないように自分自身を指定
		btn.focus_neighbor_top = btn.get_path()
		btn.focus_neighbor_bottom = btn.get_path()
