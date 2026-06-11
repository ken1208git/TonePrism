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
	# Panel 高 = bar_height。Background は anchors=full rect で Panel を埋めるので gradient 高 = bar_height。
	# (旧実装は _background.offset_bottom にも bar_height を加えており、anchor_bottom=1.0 と二重に効いて
	#  gradient が実質 2×bar_height に伸びていた = ストアでセクション見出しが沈む原因。Panel 側だけ設定して解消。)
	# 各画面は必要な gradient 高を bar_height で直接指定する
	# (store_browse=セクション見出し(y=100)直前まで 100 / game_selection=セクション名ラベルを覆う 500)。
	_panel.offset_bottom = bar_height

	var style_mgr = ButtonStyleManager.new()
	style_mgr.setup_exit_button(_exit_button)
	_exit_button.pressed.connect(func(): exit_pressed.emit())

func _process(_delta):
	GameInfoFormatter.update_clock(_clock_label)

func get_exit_button() -> Button:
	return _exit_button

func get_panel() -> Control:
	return _panel
