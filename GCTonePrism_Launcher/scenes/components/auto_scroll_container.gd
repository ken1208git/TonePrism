extends Control

@export var scroll_speed: float = 30.0
@export var pause_time: float = 1.0
@export var loop_gap: float = 80.0  # 1周目と2周目の間隔

var _label: Label
var _label_copy: Label  # シームレスループ用の複製
var _scroll_pos: float = 0.0
var _max_scroll: float = 0.0
var _wait_timer: float = 0.0
var _is_waiting: bool = false
var _needs_scroll: bool = false

func _ready():
	clip_contents = true
	_wait_timer = pause_time
	_is_waiting = true

	# Find direct child label
	for child in get_children():
		if child is Label:
			_label = child
			break

func _process(delta):
	if not _label:
		return

	# Force label to resize to text
	_label.size.x = 0

	var label_width = _label.get_minimum_size().x
	var container_width = size.x

	# Reset if fits
	if label_width <= container_width:
		_label.position.x = 0
		_label.size.x = container_width
		_needs_scroll = false
		_remove_copy()
		return

	_needs_scroll = true
	# 一周の全長 = テキスト幅 + 間隔（2周目の先頭が元位置に来るまで）
	_max_scroll = label_width + loop_gap

	# 複製ラベルを作成/更新
	_ensure_copy()

	if _is_waiting:
		_wait_timer -= delta
		if _wait_timer <= 0:
			_is_waiting = false
		return

	# 常に左方向にスクロール
	_scroll_pos += scroll_speed * delta

	# 2周目の先頭が元位置に到達 → リセットして待機
	if _scroll_pos >= _max_scroll:
		_scroll_pos = 0.0
		_is_waiting = true
		_wait_timer = pause_time

	_label.position.x = -_scroll_pos
	_label_copy.position.x = -_scroll_pos + label_width + loop_gap

func _ensure_copy():
	if _label_copy and is_instance_valid(_label_copy):
		if _label_copy.text != _label.text:
			_label_copy.text = _label.text
		return
	_label_copy = Label.new()
	_label_copy.text = _label.text
	_label_copy.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# フォント設定をコピー
	if _label.has_theme_font_override("font"):
		_label_copy.add_theme_font_override("font", _label.get_theme_font("font"))
	if _label.has_theme_font_size_override("font_size"):
		_label_copy.add_theme_font_size_override("font_size", _label.get_theme_font_size("font_size"))
	if _label.has_theme_color_override("font_color"):
		_label_copy.add_theme_color_override("font_color", _label.get_theme_color("font_color"))
	add_child(_label_copy)

func _remove_copy():
	if _label_copy and is_instance_valid(_label_copy):
		_label_copy.queue_free()
		_label_copy = null

func reset():
	_scroll_pos = 0.0
	_is_waiting = true
	_wait_timer = pause_time
	if _label:
		_label.position.x = 0.0
	if _label_copy and is_instance_valid(_label_copy):
		_label_copy.position.x = 99999  # 画面外に退避
