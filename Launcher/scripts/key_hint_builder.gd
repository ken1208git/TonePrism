## キーヒントUI生成ヘルパー
## BottomBar の操作説明にキーキャップ風アイコンを生成する
class_name KeyHintBuilder

static var _font_bold: FontFile
static var _font_regular: FontFile

static func _get_font_bold() -> FontFile:
	if not _font_bold:
		_font_bold = load("res://fonts/NotoSansJP-Bold.ttf")
	return _font_bold

static func _get_font_regular() -> FontFile:
	if not _font_regular:
		_font_regular = load("res://fonts/NotoSansJP-Regular.ttf")
	return _font_regular

## キーヒント1組（キーキャップ + アクション名）を生成
static func create_hint(key_name: String, action: String) -> HBoxContainer:
	var container = HBoxContainer.new()
	container.add_theme_constant_override("separation", 8)
	container.mouse_filter = Control.MOUSE_FILTER_IGNORE

	# キーキャップ
	container.add_child(_create_key_cap(key_name))

	# アクション名ラベル
	var action_label = Label.new()
	action_label.text = action
	action_label.add_theme_font_override("font", _get_font_regular())
	action_label.add_theme_font_size_override("font_size", 18)
	action_label.add_theme_color_override("font_color", Color(1, 1, 1, 0.7))
	action_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	container.add_child(action_label)

	return container

## キーキャップの PanelContainer を生成
static func _create_key_cap(key_name: String) -> PanelContainer:
	var panel = PanelContainer.new()
	panel.mouse_filter = Control.MOUSE_FILTER_IGNORE

	# スタイル
	var style = StyleBoxFlat.new()
	style.bg_color = Color(1, 1, 1, 0.15)
	style.border_color = Color(1, 1, 1, 0.3)
	style.border_width_left = 1
	style.border_width_top = 1
	style.border_width_right = 1
	style.border_width_bottom = 1
	style.corner_radius_top_left = 6
	style.corner_radius_top_right = 6
	style.corner_radius_bottom_right = 6
	style.corner_radius_bottom_left = 6
	style.content_margin_left = 8
	style.content_margin_right = 8
	style.content_margin_top = 2
	style.content_margin_bottom = 2
	panel.add_theme_stylebox_override("panel", style)

	# キー名ラベル
	var label = Label.new()
	label.text = key_name
	label.add_theme_font_override("font", _get_font_bold())
	label.add_theme_font_size_override("font_size", 16)
	label.add_theme_color_override("font_color", Color(1, 1, 1, 0.9))
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	panel.add_child(label)

	return panel
