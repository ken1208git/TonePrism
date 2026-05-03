## 共通トップバー（時計 + 退出ボタン）
## CanvasLayer ベースでトランジション（modulate）の影響を受けない
extends CanvasLayer

signal exit_pressed

@export var bar_height: float = 120.0

@onready var _panel: Control = $Panel
@onready var _background: TextureRect = $Panel/Background
@onready var _clock_label: Label = $Panel/MarginContainer/HBoxContainer/ClockLabel
@onready var _exit_button: Button = $Panel/MarginContainer/HBoxContainer/ExitButton

func _ready():
	_panel.offset_bottom = bar_height
	_background.offset_bottom = bar_height

	var style_mgr = ButtonStyleManager.new()
	style_mgr.setup_exit_button(_exit_button)
	_exit_button.pressed.connect(func(): exit_pressed.emit())

func _process(_delta):
	GameInfoFormatter.update_clock(_clock_label)

func get_exit_button() -> Button:
	return _exit_button

func get_panel() -> Control:
	return _panel
