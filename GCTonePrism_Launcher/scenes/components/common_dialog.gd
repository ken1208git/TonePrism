extends Control

class_name CommonDialog

signal button_pressed(button_index: int)

@onready var _title_label: Label = $Panel/MarginContainer/VBoxContainer/TitleLabel
@onready var _message_label: Label = $Panel/MarginContainer/VBoxContainer/MessageLabel
@onready var _button_container: HBoxContainer = $Panel/MarginContainer/VBoxContainer/ButtonContainer

var _buttons: Array[Button] = []

var _glow_timer: float = 0.0

# フォーカスモーフ用
var _focus_border: Panel = null
var _focus_target_rect: Rect2 = Rect2()
var _focus_target_radius: float = 16.0
var _focus_current_radius: float = 16.0
var _focus_initialized: bool = false
var _focus_prev_target: Control = null
var _focus_prev_target_pos: Vector2 = Vector2.ZERO

func _process(delta):
	if not _focus_border:
		return

	# フォーカスオーナーを取得してモーフ
	var focus_owner = get_viewport().gui_get_focus_owner()
	if focus_owner is Button and focus_owner in _buttons:
		_focus_border.visible = true
		_focus_target_rect = focus_owner.get_global_rect()

		if not _focus_initialized:
			_focus_border.global_position = _focus_target_rect.position
			_focus_border.size = _focus_target_rect.size
			_focus_current_radius = _focus_target_radius
			_focus_prev_target = focus_owner
			_focus_prev_target_pos = _focus_target_rect.position
			_focus_initialized = true
		else:
			if focus_owner == _focus_prev_target:
				var target_delta = _focus_target_rect.position - _focus_prev_target_pos
				_focus_border.global_position += target_delta
			var speed = delta * 25.0
			_focus_border.global_position = _focus_border.global_position.lerp(
				_focus_target_rect.position, speed)
			_focus_border.size = _focus_border.size.lerp(
				_focus_target_rect.size, speed)
			_focus_current_radius = lerpf(_focus_current_radius, _focus_target_radius, speed)

		_focus_prev_target = focus_owner
		_focus_prev_target_pos = _focus_target_rect.position
	else:
		_focus_border.visible = false

	# グローアニメーション（ブリージング）
	_glow_timer += delta
	var glow_alpha = 0.5 + 0.3 * sin(_glow_timer * 3.0)
	var style = _focus_border.get_theme_stylebox("panel") as StyleBoxFlat
	if style:
		style.set_corner_radius_all(int(_focus_current_radius))
		style.shadow_color = Color(1, 1, 1, glow_alpha)
		style.border_color = Color(1, 1, 1, glow_alpha)

@onready var _button_template: Button = $Panel/MarginContainer/VBoxContainer/ButtonContainer/ButtonTemplate

func _ready():
	_title_label.text = ""
	_message_label.text = ""
	_setup_focus_border()

func _setup_focus_border() -> void:
	_focus_border = Panel.new()
	_focus_border.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_focus_border.z_index = 100
	_focus_border.visible = false
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.6, 0.6, 0.6, 0)
	style.draw_center = false
	style.border_color = Color(1, 1, 1, 1)
	style.set_corner_radius_all(16)
	style.set_expand_margin_all(6)
	style.shadow_color = Color(1, 1, 1, 1)
	style.shadow_size = 12
	_focus_border.add_theme_stylebox_override("panel", style)
	add_child(_focus_border)

func setup(title: String, message: String):
	if not is_node_ready():
		await ready
	
	_title_label.text = title
	_message_label.text = message
	
	# ボタンコンテナをクリア（テンプレート以外）
	for child in _button_container.get_children():
		if child != _button_template:
			child.queue_free()
	_buttons.clear()
	_focus_initialized = false

func set_message(message: String):
	if _message_label:
		_message_label.text = message

func add_button(text: String, callback: Callable = Callable(), should_grab_focus: bool = false, color_override: Color = Color.TRANSPARENT):
	if not is_node_ready():
		await ready

	if not _button_template:
		push_error("ButtonTemplate not found!")
		return

	var btn = _button_template.duplicate()
	btn.visible = true
	btn.text = text
	
	# 色上書きがある場合のみスタイルを複製して変更
	if color_override != Color.TRANSPARENT:
		# Normal
		var style_normal = btn.get_theme_stylebox("normal").duplicate()
		if style_normal is StyleBoxFlat:
			style_normal.bg_color = color_override
			btn.add_theme_stylebox_override("normal", style_normal)
		
		# Hover
		var style_hover = btn.get_theme_stylebox("hover").duplicate()
		if style_hover is StyleBoxFlat:
			style_hover.bg_color = color_override.lightened(0.1)
			btn.add_theme_stylebox_override("hover", style_hover)
			
		# Pressed
		var style_pressed = btn.get_theme_stylebox("pressed").duplicate()
		if style_pressed is StyleBoxFlat:
			style_pressed.bg_color = color_override.darkened(0.2)
			btn.add_theme_stylebox_override("pressed", style_pressed)
			
	# Focusスタイルを透明化（共有フォーカス枠で管理）
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_color = Color.TRANSPARENT
	style_focus.shadow_color = Color.TRANSPARENT
	style_focus.shadow_size = 0
	btn.add_theme_stylebox_override("focus", style_focus)
	
	var index = _buttons.size()
	btn.pressed.connect(func():
		if callback.is_valid():
			callback.call()
		button_pressed.emit(index)
	)
	
	_button_container.add_child(btn)
	_buttons.append(btn)
	
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
