extends Control

# ダイアログのUI要素への参照
@onready var title_label = $Panel/VBoxContainer/TitleLabel
@onready var message_label = $Panel/VBoxContainer/MessageLabel
@onready var code_label = $Panel/VBoxContainer/CodeLabel
@onready var exit_button = $Panel/VBoxContainer/ButtonBox/ExitButton
@onready var retry_button = $Panel/VBoxContainer/ButtonBox/RetryButton

var _error_code: int = 0

# フォーカスモーフ用
var _focus_border: Panel = null
var _focus_initialized: bool = false
var _glow_timer: float = 0.0


func _ready():
	exit_button.pressed.connect(_on_exit_pressed)
	retry_button.pressed.connect(_on_retry_pressed)

	_setup_focus_border()
	_make_focus_transparent(exit_button)
	_make_focus_transparent(retry_button)

	# フォーカスを終了ボタンに当てる
	exit_button.grab_focus()

	# 初期化時に保留されていた内容があれば反映
	if _error_code != 0:
		_update_ui()

func _process(delta):
	if not _focus_border:
		return

	var focus_owner = get_viewport().gui_get_focus_owner()
	if focus_owner is Button and (focus_owner == exit_button or focus_owner == retry_button):
		_focus_border.visible = true
		var target_rect = focus_owner.get_global_rect()

		if not _focus_initialized:
			_focus_border.global_position = target_rect.position
			_focus_border.size = target_rect.size
			_focus_initialized = true
		else:
			var speed = delta * 25.0
			_focus_border.global_position = _focus_border.global_position.lerp(
				target_rect.position, speed)
			_focus_border.size = _focus_border.size.lerp(
				target_rect.size, speed)
	else:
		_focus_border.visible = false

	# グローアニメーション
	_glow_timer += delta
	var glow_alpha = 0.5 + 0.3 * sin(_glow_timer * 3.0)
	var style = _focus_border.get_theme_stylebox("panel") as StyleBoxFlat
	if style:
		style.shadow_color = Color(1, 1, 1, glow_alpha)
		style.border_color = Color(1, 1, 1, glow_alpha)

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

func _make_focus_transparent(btn: Button) -> void:
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_color = Color.TRANSPARENT
	style_focus.shadow_color = Color.TRANSPARENT
	style_focus.shadow_size = 0
	btn.add_theme_stylebox_override("focus", style_focus)

func setup(code: int):
	_error_code = code

	# ノード準備完了していれば即反映
	# 未完了なら_readyで呼ばれるのを待つ
	if is_node_ready():
		_update_ui()

func _update_ui():
	# メッセージはtscnファイルで静的に設定済み
	code_label.text = "Error Code: E-%04d" % _error_code

	print("[ErrorDialog] Showing error E-%04d" % _error_code)

func _on_exit_pressed():
	print("[ErrorDialog] Exit button pressed. Quitting application.")
	get_tree().quit()

func _on_retry_pressed():
	# リトライ機能は実装次第（現状は非表示）
	print("[ErrorDialog] Retry not implemented.")
