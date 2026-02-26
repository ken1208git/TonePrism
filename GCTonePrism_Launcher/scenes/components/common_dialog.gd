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

@onready var _button_template: Button = $Panel/MarginContainer/VBoxContainer/ButtonContainer/ButtonTemplate

func _ready():
	_title_label.text = ""
	_message_label.text = ""
	
	# パネルスタイルはtscnで設定済みのため、コードによる上書きは削除
	# var panel = $Panel
	# ...

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
	_glow_styles.clear()
	
	# テンプレートからFocusスタイルを取得してGlow対象に追加（テンプレート自体は非表示だがアニメーション用）
	# 実際のボタン追加時に個別に登録する

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
			
	# FocusスタイルをGlowリストに追加
	var style_focus = btn.get_theme_stylebox("focus")
	if style_focus and style_focus is StyleBoxFlat:
		# 複製しなくても良いが、個別にアニメーションさせるなら複製も可
		# ここではTscnのリソースを共有している可能性があるため、複製して割り当てる
		# (Tscnのリソースは共有されるため、全てのボタンが同期して明滅するのはOK)
		# ただし、add_theme_stylebox_overrideしていない場合はテーマから取得されるので
		# 明示的に複製してoverrideする
		var new_focus = style_focus.duplicate()
		btn.add_theme_stylebox_override("focus", new_focus)
		_glow_styles.append(new_focus)
	
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
