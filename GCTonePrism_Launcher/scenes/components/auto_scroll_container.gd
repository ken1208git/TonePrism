extends Control

@export var scroll_speed: float = 30.0
@export var pause_time: float = 1.0
@export var loop_gap: float = 80.0  # 1周目と2周目の間隔

var _content: Control
var _content_copy: Control  # シームレスループ用の複製
var _scroll_pos: float = 0.0
var _max_scroll: float = 0.0
var _wait_timer: float = 0.0
var _is_waiting: bool = false
var _needs_scroll: bool = false

func _ready():
	clip_contents = true
	_wait_timer = pause_time
	_is_waiting = true

	# Find direct child control
	for child in get_children():
		if child is Control and not child.name.ends_with("_Copy"):
			_content = child
			break

func _process(delta):
	if not _content:
		return

	# Force resize to text or contents length
	_content.size.x = 0

	var content_width = _content.get_minimum_size().x
	var container_width = size.x

	# Reset if fits
	if content_width <= container_width:
		_content.position.x = 0
		_content.size.x = container_width
		_needs_scroll = false
		_remove_copy()
		return

	_needs_scroll = true
	# 一周の全長 = テキスト/中身幅 + 間隔（2周目の先頭が元位置に来るまで）
	_max_scroll = content_width + loop_gap

	# 複製要素を作成/更新
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

	_content.position.x = -_scroll_pos
	if _content_copy and is_instance_valid(_content_copy):
		_content_copy.position.x = -_scroll_pos + content_width + loop_gap

func _ensure_copy():
	if _content_copy and is_instance_valid(_content_copy):
		return
	_content_copy = _content.duplicate()
	_content_copy.name = _content.name + "_Copy"
	if _content_copy is Label:
		_content_copy.text = _content.get("text")
		# duplicate だけではテーマオーバーライドがコピーされない場合があるため（Godot4の一部挙動）
		if _content.has_theme_font_override("font"):
			_content_copy.add_theme_font_override("font", _content.get_theme_font("font"))
		if _content.has_theme_font_size_override("font_size"):
			_content_copy.add_theme_font_size_override("font_size", _content.get_theme_font_size("font_size"))
		if _content.has_theme_color_override("font_color"):
			_content_copy.add_theme_color_override("font_color", _content.get_theme_color("font_color"))
	add_child(_content_copy)

func _remove_copy():
	if _content_copy and is_instance_valid(_content_copy):
		_content_copy.queue_free()
		_content_copy = null

func reset():
	_scroll_pos = 0.0
	_is_waiting = true
	_wait_timer = pause_time
	if _content:
		_content.position.x = 0.0
	_remove_copy()  # 次回 _process で新しい中身の複製を作り直す
