extends Control

class_name CommonDialog

signal button_pressed(button_index: int)

@onready var _title_label: Label = $Panel/MarginContainer/VBoxContainer/TitleLabel
@onready var _message_label: Label = $Panel/MarginContainer/VBoxContainer/MessageLabel
@onready var _button_container: HBoxContainer = $Panel/MarginContainer/VBoxContainer/ButtonContainer

var _buttons: Array[Button] = []

func _ready():
	# 初期化待ちはsetupで制御するためここでは特になし
	pass

func setup(title: String, message: String):
	if not is_node_ready():
		# まだreadyでない場合、後で設定されるようにするか、awaitする
		# 通常はadd_child後に呼ばれるためready済みのはず
		await ready
	
	_title_label.text = title
	_message_label.text = message
	
	# ボタンコンテナをクリア
	for child in _button_container.get_children():
		child.queue_free()
	_buttons.clear()

func set_message(message: String):
	if _message_label:
		_message_label.text = message

func add_button(text: String, callback: Callable = Callable(), should_grab_focus: bool = false):
	if not is_node_ready():
		await ready

	var btn = Button.new()
	btn.text = text
	btn.add_theme_font_override("font", load("res://fonts/NotoSansJP-Regular.ttf"))
	btn.add_theme_font_size_override("font_size", 24)
	btn.custom_minimum_size = Vector2(160, 50)
	
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
	
	if should_grab_focus:
		btn.grab_focus()
