## レンガ状スクロール背景
## ゲームの背景画像をレンガパターンに敷き詰め、行ごとに交互にスクロールする
extends Control

@export var tile_size: Vector2 = Vector2(360, 200)
@export var row_count: int = 5
@export var row_gap: float = 4.0
@export var scroll_speed: float = 25.0
@export var tile_opacity: float = 0.15

var _strips: Array[Control] = []
var _strip_width: float = 0.0
var _tiles_per_row: int = 0

# バックグラウンド画像読み込み用
var _load_thread: Thread = null
var _load_mutex: Mutex = Mutex.new()
var _image_load_queue: Array = []   # [{path, row, col}]
var _loaded_images: Array = []      # [{image, row, col}]
var _tile_rects: Array = []         # 2D配列 [row][col] = TextureRect
var _cancel_requested: bool = false

## レイアウトだけ先に構築（テクスチャは後からバックグラウンドで適用）
func setup(bg_paths: Array[String]) -> void:
	if bg_paths.is_empty():
		return

	# 既存の行をクリア
	for child in get_children():
		child.queue_free()
	_strips.clear()
	_tile_rects.clear()

	var viewport_w := 1920.0
	var viewport_h := 1080.0
	_tiles_per_row = ceili(viewport_w / (tile_size.x + row_gap)) + 2
	_strip_width = _tiles_per_row * (tile_size.x + row_gap)

	# 全タイルに偏りなくパスを配る（カード配り方式、固定シードで全PC同期）
	var rng := RandomNumberGenerator.new()
	rng.seed = 42
	var total_tiles := row_count * _tiles_per_row
	var dealt: Array[String] = []
	var deck: Array[String] = []
	for i in range(total_tiles):
		if deck.is_empty():
			deck = bg_paths.duplicate()
			# 固定シードRNGでシャッフル（Fisher-Yates）
			for j in range(deck.size() - 1, 0, -1):
				var k := rng.randi_range(0, j)
				var tmp := deck[j]
				deck[j] = deck[k]
				deck[k] = tmp
		dealt.append(deck.pop_back())

	# 行全体を垂直中央に配置
	var total_h := row_count * tile_size.y + (row_count - 1) * row_gap
	var y_start := (viewport_h - total_h) / 2.0

	for row_i in range(row_count):
		var row_paths := dealt.slice(row_i * _tiles_per_row, (row_i + 1) * _tiles_per_row)
		var row_rects: Array[TextureRect] = []

		# 行コンテナ（クリッピング用）
		var row_clip := Control.new()
		row_clip.clip_contents = true
		row_clip.position = Vector2(0, y_start + row_i * (tile_size.y + row_gap))
		row_clip.size = Vector2(viewport_w, tile_size.y)
		row_clip.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(row_clip)

		# タイルストリップ（2セット分で無限ループ）
		var strip := HBoxContainer.new()
		strip.add_theme_constant_override("separation", int(row_gap))
		strip.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row_clip.add_child(strip)

		for tile_i in range(_tiles_per_row * 2):
			var col_i := tile_i % _tiles_per_row
			var tex_rect := TextureRect.new()
			tex_rect.custom_minimum_size = tile_size
			tex_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			tex_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
			tex_rect.modulate = Color(1, 1, 1, 0)  # 最初は透明
			tex_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
			strip.add_child(tex_rect)

			# 1セット目のみ記録（2セット目は同じ col_i を参照）
			if tile_i < _tiles_per_row:
				row_rects.append(tex_rect)

		_strips.append(strip)
		_tile_rects.append(row_rects)

		# 読み込みキューに追加（1セット分のユニークなパスのみ）
		for col_i in range(row_paths.size()):
			_image_load_queue.append({
				"path": row_paths[col_i],
				"row": row_i,
				"col": col_i
			})

	# バックグラウンドスレッドで画像読み込み開始
	_load_thread = Thread.new()
	_load_thread.start(_load_images_in_thread)

## バックグラウンドスレッド: キューから画像を読み込む
func _load_images_in_thread() -> void:
	while true:
		_load_mutex.lock()
		if _cancel_requested or _image_load_queue.is_empty():
			_load_mutex.unlock()
			break
		var item = _image_load_queue.pop_front()
		_load_mutex.unlock()

		var image := Image.load_from_file(item["path"])
		if not image:
			continue
		image.resize(int(tile_size.x), int(tile_size.y), Image.INTERPOLATE_LANCZOS)
		_load_mutex.lock()
		if _cancel_requested:
			_load_mutex.unlock()
			break
		_loaded_images.append({
			"image": image,
			"row": item["row"],
			"col": item["col"]
		})
		_load_mutex.unlock()

## メインスレッド: 読み込み済み画像をテクスチャ化して適用
func _apply_loaded_images() -> void:
	_load_mutex.lock()
	if _loaded_images.is_empty():
		_load_mutex.unlock()
		return

	var apply_count := mini(3, _loaded_images.size())
	var to_apply: Array = []
	for i in range(apply_count):
		to_apply.append(_loaded_images.pop_front())
	_load_mutex.unlock()

	for item in to_apply:
		var row_i: int = item["row"]
		var col_i: int = item["col"]
		var tex := ImageTexture.create_from_image(item["image"])

		if row_i >= _tile_rects.size() or col_i >= _tile_rects[row_i].size():
			continue

		# 1セット目に適用
		var rect_1st: TextureRect = _tile_rects[row_i][col_i]
		rect_1st.texture = tex

		# 2セット目（ループ用コピー）にも適用
		var strip: HBoxContainer = _strips[row_i]
		var rect_2nd_idx := _tiles_per_row + col_i
		if rect_2nd_idx < strip.get_child_count():
			var rect_2nd: TextureRect = strip.get_child(rect_2nd_idx)
			rect_2nd.texture = tex

		# フェードイン
		var tween := create_tween()
		tween.set_parallel(true)
		tween.tween_property(rect_1st, "modulate", Color(1, 1, 1, tile_opacity), 0.3)
		tween.tween_property(strip.get_child(rect_2nd_idx), "modulate", Color(1, 1, 1, tile_opacity), 0.3)

func _process(delta: float) -> void:
	# 読み込み済み画像の適用
	_apply_loaded_images()

	# システム時刻ベースでスクロール位置を決定（複数PC間で同期）
	var t := Time.get_unix_time_from_system()

	for i in range(_strips.size()):
		# 偶数行→右、奇数行→左
		var raw_offset := fmod(t * scroll_speed, _strip_width)
		# 奇数行は逆方向: strip_width から引いて反転
		var offset := raw_offset if i % 2 == 0 else _strip_width - raw_offset

		# レンガオフセット（奇数行は半タイル分ずらす）
		var brick_offset := -tile_size.x / 2.0 if i % 2 == 1 else 0.0

		# ストリップは2セット分の幅。1セット分左にオフセットして配置し、
		# offset分ずらすことでループする
		_strips[i].position.x = brick_offset - _strip_width + offset

func _exit_tree() -> void:
	if _load_thread and _load_thread.is_started():
		_load_mutex.lock()
		_cancel_requested = true
		_image_load_queue.clear()
		_load_mutex.unlock()
		_load_thread.wait_to_finish()
