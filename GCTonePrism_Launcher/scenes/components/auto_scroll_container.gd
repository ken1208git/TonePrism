extends Control

@export var scroll_speed: float = 30.0
@export var pause_time: float = 1.0

var _label: Label
var _scroll_pos: float = 0.0
var _min_scroll: float = 0.0
var _max_scroll: float = 0.0
var _direction: int = 1 # 1 for forward (left), -1 for backward (right)
var _wait_timer: float = 0.0
var _is_waiting: bool = false # Initial wait

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
	_label.size.x = 0 # triggers auto-resize based on text
	
	var label_width = _label.get_minimum_size().x
	var container_width = size.x
	
	# Reset if fits
	if label_width <= container_width:
		_label.position.x = 0
		_label.size.x = container_width # Expand to fit container if aligned
		return

	# Calculate scroll limits
	# Usually start at 0 (left aligned)
	# Max scroll is diff
	_min_scroll = 0.0
	_max_scroll = label_width - container_width
	
	if _is_waiting:
		_wait_timer -= delta
		if _wait_timer <= 0:
			_is_waiting = false
		return

	# Scroll logic
	_scroll_pos += scroll_speed * delta * _direction
	
	# Check boundaries
	if _scroll_pos >= _max_scroll:
		_scroll_pos = _max_scroll
		_direction = -1
		_is_waiting = true
		_wait_timer = pause_time
	elif _scroll_pos <= _min_scroll:
		_scroll_pos = _min_scroll
		_direction = 1
		_is_waiting = true
		_wait_timer = pause_time
		
	_label.position.x = -_scroll_pos

func reset():
	_scroll_pos = 0.0
	_direction = 1
	_is_waiting = true
	_wait_timer = pause_time
	if _label:
		_label.position.x = 0.0
