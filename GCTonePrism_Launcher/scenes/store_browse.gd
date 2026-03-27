extends Control

## ストアブラウズ画面（メインオーケストレーター）
## Steam風の縦スクロールブラウズ画面
## セクションごとにスライドショー/タイルグリッド/通常行を表示

# --- コンポーネント ---
var _idle_mgr: IdleManager
var _db_manager: DatabaseManager
var _style_mgr: ButtonStyleManager
var _sections: Array[StoreSectionInfo] = []

# --- ナビゲーション状態 ---
var _current_section: int = 0
var _current_tile: int = 0
var _on_view_all: bool = false   # type=0で「すべて見る」にフォーカス中か
var _on_exit_button: bool = false  # 退出ボタンにフォーカス中か
var _on_all_games: bool = false    # 「すべてのゲーム」ボタンにフォーカス中か
var _using_mouse: bool = false

# --- スライドショー ---
var _slideshow_timers: Dictionary = {}  # section_index -> float
const SLIDESHOW_INTERVAL := 4.0
var _slideshow_indices: Dictionary = {}  # section_index -> int (現在表示中のスライド)

# --- グローアニメーション ---
var _glow_time: float = 0.0

# --- セクションUIデータ ---
# 各要素: {section: StoreSectionInfo, container: Control, tiles: Array[Control], type: int}
var _section_ui: Array = []

# --- ノード参照 ---
@onready var _scroll_container: ScrollContainer = $ScrollArea
@onready var _content_container: VBoxContainer = $ScrollArea/MarginContainer/ContentContainer
@onready var _focus_border: Panel = $FocusBorder
@onready var _clock_label: Label = $TopBar/MarginContainer/HBoxContainer/ClockLabel
@onready var _exit_button: Button = $TopBar/MarginContainer/HBoxContainer/ExitButton
@onready var _bottom_bar: Control = $BottomBar

# --- 「すべてのゲーム」ボタン ---
var _all_games_button: Button = null

# --- フォーカス対象 ---
var _focus_target: Control = null

# --- フォーカスモーフ用 ---
var _focus_target_rect: Rect2 = Rect2()
var _focus_target_radius: float = 24.0
var _focus_current_radius: float = 24.0
var _focus_initialized: bool = false
var _focus_prev_target: Control = null
var _focus_prev_target_pos: Vector2 = Vector2.ZERO

# --- スクロールTween ---
var _scroll_tween: Tween = null

func _ready():
	_idle_mgr = IdleManager.new()
	_db_manager = DatabaseManager.new()
	_style_mgr = ButtonStyleManager.new()

	process_mode = Node.PROCESS_MODE_ALWAYS

	if not _db_manager.open():
		print("[StoreBrowse] データベースのオープンに失敗しました")
		ErrorManager.show_error(ErrorCode.DATABASE_NOT_FOUND)
		return

	_sections = _db_manager.get_store_sections()

	# セクション0件 → フォールバック: 全ゲームでカルーセル直接表示
	if _sections.is_empty():
		print("[StoreBrowse] セクション0件 → カルーセルにフォールバック")
		var all_games = _db_manager.get_all_games()
		_db_manager.close()
		if all_games.is_empty():
			ErrorManager.show_error(ErrorCode.DATABASE_NO_GAMES_REGISTERED)
			return
		AppState.filtered_games = all_games
		AppState.initial_game_id = all_games[0].game_id
		AppState.return_scene = "res://scenes/screensaver.tscn"
		AppState.section_title = ""
		TransitionManager.change_scene("res://scenes/game_selection.tscn")
		return

	print("[StoreBrowse] %d 件のセクションを読み込みました" % _sections.size())

	# セクションUIを動的生成
	var viewport_width = get_viewport_rect().size.x
	for i in range(_sections.size()):
		var section = _sections[i]
		var container: Control
		match section.section_type:
			1:  # スライドショー
				container = StoreBrowseBuilder.build_slideshow_section(section, viewport_width)
				_slideshow_timers[i] = 0.0
				_slideshow_indices[i] = 0
			2:  # タイルグリッド
				container = StoreBrowseBuilder.build_tile_grid_section(section, viewport_width)
			_:  # 通常セクション行
				container = StoreBrowseBuilder.build_normal_section(section, viewport_width)

		_content_container.add_child(container)

		# タイル一覧を収集
		var tiles: Array[Control] = []
		_collect_focusable_tiles(container, section.section_type, tiles)

		_section_ui.append({
			"section": section,
			"container": container,
			"tiles": tiles,
			"type": section.section_type
		})

		# マウスクリック/ホバーのシグナル接続
		_connect_tile_signals(i, tiles)

		# スライドショーの左右ボタンにシグナル接続
		if section.section_type == 1:
			var prev_btn = container.get_node_or_null("SlideshowPrev")
			var next_btn = container.get_node_or_null("SlideshowNext")
			var sec_idx_ss = i
			if prev_btn:
				prev_btn.pressed.connect(func():
					_switch_slide(sec_idx_ss, -1)
					_slideshow_timers[sec_idx_ss] = 0.0
				)
			if next_btn:
				next_btn.pressed.connect(func():
					_switch_slide(sec_idx_ss, 1)
					_slideshow_timers[sec_idx_ss] = 0.0
				)

		# type=0の「すべて見る」ボタンにシグナル接続
		if section.section_type == 0:
			var view_all = container.get_node_or_null("ViewAllButton")
			if view_all == null:
				# HBoxContainerの中にある可能性
				for child in container.get_children():
					if child is HBoxContainer:
						view_all = child.get_node_or_null("ViewAllButton")
						break
			if view_all:
				var sec_idx = i
				view_all.pressed.connect(func(): _on_view_all_pressed(sec_idx))
				view_all.mouse_entered.connect(func():
					_using_mouse = true
					_current_section = sec_idx
					_on_view_all = true
					_update_focus_visual()
				)

	# 「すべてのゲーム」ボタンをセクション末尾に追加
	_all_games_button = StoreBrowseBuilder.build_all_games_button(viewport_width)
	_all_games_button.pressed.connect(_on_all_games_pressed)
	_all_games_button.mouse_entered.connect(func():
		_using_mouse = true
		_on_all_games = true
		_on_view_all = false
		_on_exit_button = false
		_update_focus_visual()
	)
	_content_container.add_child(_all_games_button)

	# ExitButton設定（カルーセルと同じスタイル）
	if _exit_button:
		_exit_button.pressed.connect(_on_exit_button_pressed)
		_style_mgr.setup_exit_button(_exit_button)

	# フォーカスボーダーのスタイル設定
	_setup_focus_border()

	# 初期フォーカス
	_current_section = 0
	_current_tile = 0
	_on_view_all = false
	call_deferred("_update_focus_visual")

	set_process(true)
	set_process_input(true)

func _process(delta):
	# 時計更新
	GameInfoDisplay.update_clock(_clock_label)

	# アイドルタイマー
	if _idle_mgr.update(delta, get_tree().paused):
		IdleManager.transition_to_screensaver(get_tree())
		return

	# シーン遷移中のガード
	if not is_inside_tree() or get_tree() == null:
		return
	if get_tree().paused:
		return

	# スライドショー自動切替 + ゲージバー更新
	for sec_idx in _slideshow_timers.keys():
		_slideshow_timers[sec_idx] += delta
		if _slideshow_timers[sec_idx] >= SLIDESHOW_INTERVAL:
			_slideshow_timers[sec_idx] = 0.0
			_switch_slide(sec_idx, 1)
		else:
			_update_slideshow_bar(sec_idx)

	# グローアニメーション
	_glow_time += delta
	_update_glow()

	# フォーカスボーダーをlerp追従（スクロール分は即座反映）
	_sync_focus_position()
	if _focus_border and _focus_border.visible and _focus_initialized:
		# 同じターゲットの位置変化（=スクロール）は即座に反映
		if _focus_target == _focus_prev_target:
			var target_delta = _focus_target_rect.position - _focus_prev_target_pos
			_focus_border.global_position += target_delta
		# フォーカス移動分はlerpで補間
		var speed = delta * 25.0
		_focus_border.global_position = _focus_border.global_position.lerp(
			_focus_target_rect.position, speed)
		_focus_border.size = _focus_border.size.lerp(
			_focus_target_rect.size, speed)
		_focus_current_radius = lerpf(_focus_current_radius, _focus_target_radius, speed)
		var morph_style = _focus_border.get_theme_stylebox("panel") as StyleBoxFlat
		if morph_style:
			morph_style.set_corner_radius_all(int(_focus_current_radius))
	_focus_prev_target = _focus_target
	_focus_prev_target_pos = _focus_target_rect.position

	# BottomBar表示切替
	if _bottom_bar:
		_bottom_bar.visible = not _using_mouse

func _input(event):
	if get_tree().paused:
		return

	# アイドルリセット
	_idle_mgr.reset()

	# マウス移動検知
	if event is InputEventMouseMotion:
		_using_mouse = true
		return

	if event is InputEventMouseButton:
		_using_mouse = true
		# マウスホイールはScrollContainerに任せる
		return

	# キーボード/ゲームパッド入力
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
		_using_mouse = false

	if not event.is_pressed():
		return

	var viewport = get_viewport()
	if not viewport:
		return

	if event.is_action_pressed("ui_up"):
		_move_section(-1)
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_down"):
		_move_section(1)
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_left"):
		_move_tile(-1)
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_right"):
		_move_tile(1)
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_accept"):
		_on_select()
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_cancel"):
		IdleManager.transition_to_screensaver(get_tree())
		viewport.set_input_as_handled()

func _exit_tree():
	if _db_manager:
		_db_manager.close()
	if DialogManager and DialogManager.has_method("close_current_dialog"):
		DialogManager.close_current_dialog()

# --- ナビゲーション ---

func _move_section(dir: int) -> void:
	if _section_ui.is_empty():
		return

	if _on_exit_button:
		# 退出ボタンから下でセクション0へ
		if dir > 0:
			var prev_x := _get_current_focus_center_x()
			_on_exit_button = false
			_on_all_games = false
			_current_section = 0
			_on_view_all = false
			var sec_data = _section_ui[_current_section]
			var sec_tiles = sec_data["tiles"] as Array
			_current_tile = _find_nearest_tile(sec_tiles, prev_x)
			_scroll_to_section(_current_section)
			_update_focus_visual()
		return

	if _on_all_games:
		# 「すべてのゲーム」ボタンから上で最後のセクションへ
		if dir < 0:
			var prev_x := _get_current_focus_center_x()
			_on_all_games = false
			_current_section = _section_ui.size() - 1
			_on_view_all = false
			var sec_data = _section_ui[_current_section]
			var sec_tiles = sec_data["tiles"] as Array
			_current_tile = _find_nearest_tile(sec_tiles, prev_x)
			_scroll_to_section(_current_section)
			_update_focus_visual()
		return

	if dir < 0 and _current_section == 0:
		# セクション0で上 → 退出ボタンへ
		_on_exit_button = true
		_on_view_all = false
		_on_all_games = false
		_scroll_container.scroll_vertical = 0
		_update_focus_visual()
		return

	var new_section = clampi(_current_section + dir, 0, _section_ui.size() - 1)
	if new_section == _current_section and dir > 0:
		# 最後のセクションで下 → 「すべてのゲーム」ボタンへ
		_on_all_games = true
		_on_view_all = false
		_scroll_to_all_games_button()
		_update_focus_visual()
		return
	if new_section == _current_section:
		return
	# 移動前のフォーカスX座標を記憶
	var prev_x := _get_current_focus_center_x()

	_current_section = new_section
	_on_view_all = false

	# 位置ベースで最も近いタイルを選択
	var data = _section_ui[_current_section]
	var new_tiles = data["tiles"] as Array
	_current_tile = _find_nearest_tile(new_tiles, prev_x)

	_scroll_to_section(_current_section)
	_update_focus_visual()

func _move_tile(dir: int) -> void:
	if _section_ui.is_empty():
		return
	var data = _section_ui[_current_section]
	var section_type = data["type"]
	var tile_list = data["tiles"] as Array

	match section_type:
		0:  # 通常セクション行
			if _on_view_all:
				if dir < 0:
					# 「すべて見る」から左に戻る
					_on_view_all = false
					_current_tile = tile_list.size() - 1
					_update_focus_visual()
				return

			var new_tile = _current_tile + dir
			if new_tile < 0:
				return  # 左端で止まる
			elif new_tile >= tile_list.size():
				# 右端を超えたら「すべて見る」にフォーカス
				_on_view_all = true
				_update_focus_visual()
				return
			_current_tile = new_tile
			_update_focus_visual()

		1:  # スライドショー
			_switch_slide(_current_section, dir)
			_slideshow_timers[_current_section] = 0.0  # タイマーリセット

		2:  # タイルグリッド
			var new_tile = clampi(_current_tile + dir, 0, tile_list.size() - 1)
			if new_tile != _current_tile:
				_current_tile = new_tile
				_update_focus_visual()

## 現在のフォーカス対象のX中心座標を取得
func _get_current_focus_center_x() -> float:
	if _focus_target and is_instance_valid(_focus_target):
		var rect = _focus_target.get_global_rect()
		return rect.position.x + rect.size.x / 2.0
	return 0.0

## 指定X座標に最も近いタイルのインデックスを返す
func _find_nearest_tile(tiles: Array, target_x: float) -> int:
	if tiles.is_empty():
		return 0
	var best_idx := 0
	var best_dist := INF
	for i in range(tiles.size()):
		var tile = tiles[i] as Control
		if tile and is_instance_valid(tile):
			var rect = tile.get_global_rect()
			var center_x = rect.position.x + rect.size.x / 2.0
			var dist = absf(center_x - target_x)
			if dist < best_dist:
				best_dist = dist
				best_idx = i
	return best_idx

func _on_select() -> void:
	if _on_all_games:
		_on_all_games_pressed()
		return
	if _on_exit_button:
		_on_exit_button_pressed()
		return
	if _section_ui.is_empty():
		return
	var data = _section_ui[_current_section]
	var section: StoreSectionInfo = data["section"]

	# 全ゲーム取得（LIMIT なし）
	var all_games = _db_manager.get_all_games_for_section(section)
	if all_games.is_empty():
		return

	# フォーカスするゲームIDを決定
	var focus_game_id: String = ""
	match data["type"]:
		0:  # 通常行
			if _on_view_all:
				focus_game_id = all_games[0].game_id
			else:
				var tiles = data["tiles"] as Array
				if _current_tile < section.games.size():
					focus_game_id = section.games[_current_tile].game_id
				else:
					focus_game_id = all_games[0].game_id
		1:  # スライドショー
			var slide_idx = _slideshow_indices.get(_current_section, 0)
			if slide_idx < section.games.size():
				focus_game_id = section.games[slide_idx].game_id
			else:
				focus_game_id = all_games[0].game_id
		2:  # タイルグリッド
			if _current_tile < section.games.size():
				focus_game_id = section.games[_current_tile].game_id
			else:
				focus_game_id = all_games[0].game_id

	# AppState設定
	AppState.filtered_games = all_games
	AppState.initial_game_id = focus_game_id
	AppState.return_scene = "res://scenes/store_browse.tscn"
	AppState.section_title = section.title

	TransitionManager.change_scene("res://scenes/game_selection.tscn")

# --- スライドショー ---

## スライドショーのアニメーション中かどうか
var _slide_animating: bool = false

func _switch_slide(section_index: int, dir: int) -> void:
	if section_index >= _section_ui.size():
		return
	var data = _section_ui[section_index]
	if data["type"] != 1:
		return
	var section: StoreSectionInfo = data["section"]
	var container: Control = data["container"]
	if section.games.is_empty():
		return
	if _slide_animating:
		return

	var current_idx = _slideshow_indices.get(section_index, 0)
	var clip = container.get_node_or_null("BannerClip")
	if not clip:
		return
	var old_banner = clip.get_node_or_null("Banner_%d" % current_idx)

	# 新しいインデックス（ラップアラウンド）
	var game_count = section.games.size()
	var new_idx = (current_idx + dir) % game_count
	if new_idx < 0:
		new_idx += game_count
	_slideshow_indices[section_index] = new_idx

	var new_banner = clip.get_node_or_null("Banner_%d" % new_idx)
	var banner_width = container.custom_minimum_size.x

	# スライドアニメーション
	_slide_animating = true
	var slide_tween = create_tween()
	slide_tween.set_parallel(true)

	if new_banner:
		new_banner.visible = true
		# 新バナーを進行方向から登場
		new_banner.position.x = banner_width * dir
		slide_tween.tween_property(new_banner, "position:x", 0.0, 0.4)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

	if old_banner:
		# 旧バナーを反対方向にスライドアウト
		slide_tween.tween_property(old_banner, "position:x", -banner_width * dir, 0.4)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

	slide_tween.set_parallel(false)
	slide_tween.tween_callback(func():
		if old_banner:
			old_banner.visible = false
			old_banner.position.x = 0
		_slide_animating = false
	)

	# ゲージバー: 満タンバーをフェードアウト、新バーを0からスタート
	var bars = container.get_node_or_null("BarIndicator")
	if bars:
		var bar_tween = create_tween()
		bar_tween.set_parallel(true)
		for i in range(bars.get_child_count()):
			var bar = bars.get_child(i) as Panel
			if bar:
				var fill = bar.get_node_or_null("Fill") as ColorRect
				if fill:
					if i == current_idx:
						# 満タンバーをフェードアウト
						bar_tween.tween_property(fill, "color:a", 0.0, 0.3)\
							.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
						bar_tween.tween_callback(func():
							fill.size.x = 0
							fill.color.a = 1.0
						).set_delay(0.3)
					else:
						fill.size.x = 0
						fill.color.a = 1.0

	# タイトルをバナーと連動してスライドアウト→スライドイン（BannerClip内）
	var title_scroll = clip.get_node_or_null("SlideshowTitleScroll")
	if title_scroll and new_idx < section.games.size():
		var title_label = title_scroll.get_node_or_null("SlideshowTitle")
		var game = section.games[new_idx]
		var new_text: String
		if section.game_display_texts.has(game.game_id) and not section.game_display_texts[game.game_id].is_empty():
			new_text = section.game_display_texts[game.game_id]
		else:
			new_text = game.title
		var scroll_origin_x = title_scroll.position.x
		var title_tween = create_tween()
		# 旧テキストをスライド方向にフェードアウト（コンテナごと移動）
		title_tween.set_parallel(true)
		title_tween.tween_property(title_scroll, "position:x", scroll_origin_x - 80.0 * dir, 0.15)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
		title_tween.tween_property(title_scroll, "modulate:a", 0.0, 0.15)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
		# 新テキスト: テキスト差し替え→スライドイン
		title_tween.set_parallel(false)
		title_tween.tween_callback(func():
			if title_label:
				title_label.text = new_text
			title_scroll.reset()
		)
		title_tween.set_parallel(true)
		title_tween.tween_property(title_scroll, "position:x", scroll_origin_x, 0.25)\
			.from(scroll_origin_x + 80.0 * dir)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		title_tween.tween_property(title_scroll, "modulate:a", 1.0, 0.25)\
			.from(0.0)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

	# 現在セクションの場合、フォーカスも更新
	if section_index == _current_section:
		_update_focus_visual()

## ゲージバーのフィル幅を更新（毎フレーム呼び出し）
func _update_slideshow_bar(section_index: int) -> void:
	if section_index >= _section_ui.size():
		return
	var data = _section_ui[section_index]
	if data["type"] != 1:
		return
	var container: Control = data["container"]
	var bars = container.get_node_or_null("BarIndicator")
	if not bars:
		return
	var current_idx = _slideshow_indices.get(section_index, 0)
	var bar = bars.get_node_or_null("Bar_%d" % current_idx)
	if bar:
		var fill = bar.get_node_or_null("Fill")
		if fill:
			var progress = _slideshow_timers[section_index] / SLIDESHOW_INTERVAL
			fill.size.x = progress * StoreBrowseBuilder.BAR_WIDTH

# --- フォーカス表示 ---

func _setup_focus_border() -> void:
	if not _focus_border:
		return
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.6, 0.6, 0.6, 0)
	style.draw_center = false
	style.border_color = Color(1, 1, 1, 1)
	style.set_corner_radius_all(16)
	style.set_expand_margin_all(8)
	style.shadow_color = Color(1, 1, 1, 1)
	style.shadow_size = 12
	_focus_border.add_theme_stylebox_override("panel", style)
	_focus_border.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_focus_border.z_index = 100

func _update_focus_visual() -> void:
	if not _focus_border or _section_ui.is_empty():
		return

	# マウス操作中はフォーカスボーダー非表示
	if _using_mouse:
		_focus_border.visible = false
		_focus_initialized = false
		return

	# 退出ボタンにフォーカス中
	if _on_exit_button:
		if _exit_button:
			_focus_border.visible = true
			_focus_target = _exit_button
			_sync_focus_position()
		else:
			_focus_border.visible = false
			_focus_target = null
		return

	# 「すべてのゲーム」ボタンにフォーカス中
	if _on_all_games:
		if _all_games_button:
			_focus_border.visible = true
			_focus_target = _all_games_button
			_focus_target_radius = 20
			_sync_focus_position()
		else:
			_focus_border.visible = false
			_focus_target = null
		return

	var data = _section_ui[_current_section]
	var target: Control = null

	if data["type"] == 0 and _on_view_all:
		# 「すべて見る」ボタンにフォーカス
		var container = data["container"] as Control
		for child in container.get_children():
			if child is HBoxContainer:
				var btn = child.get_node_or_null("ViewAllButton")
				if btn:
					target = btn
					break
	elif data["type"] == 1:
		# スライドショー: クリップ領域（窓）にフォーカス固定
		var container = data["container"] as Control
		target = container.get_node_or_null("BannerClip")
	else:
		# タイルにフォーカス
		var tiles = data["tiles"] as Array
		if _current_tile < tiles.size():
			target = tiles[_current_tile]

	if target == null:
		_focus_border.visible = false
		_focus_target = null
		return

	_focus_border.visible = true
	_focus_target = target
	_focus_target_radius = 24
	_sync_focus_position()

func _sync_focus_position() -> void:
	if not _focus_border or not _focus_border.visible or _focus_target == null:
		return
	if not is_instance_valid(_focus_target):
		_focus_border.visible = false
		_focus_target = null
		return
	# 目標位置・サイズを更新（実際の移動はlerpで行う）
	_focus_target_rect = _focus_target.get_global_rect()
	# 初回は即座配置（(0,0)からモーフ防止）
	if not _focus_initialized:
		_focus_border.global_position = _focus_target_rect.position
		_focus_border.size = _focus_target_rect.size
		_focus_current_radius = _focus_target_radius
		_focus_prev_target = _focus_target
		_focus_prev_target_pos = _focus_target_rect.position
		_focus_initialized = true

func _update_glow() -> void:
	if not _focus_border or not _focus_border.visible:
		return
	var style = _focus_border.get_theme_stylebox("panel") as StyleBoxFlat
	if style:
		# sin波でグロー強度を変化 (8〜16)
		var glow_intensity = 12.0 + 4.0 * sin(_glow_time * 3.0)
		style.shadow_size = int(glow_intensity)
		# 透明度もわずかに変化
		var alpha = 0.8 + 0.2 * sin(_glow_time * 3.0)
		style.shadow_color = Color(1, 1, 1, alpha)

# --- スクロール ---

func _scroll_to_section(index: int) -> void:
	if index >= _section_ui.size():
		return
	var container = _section_ui[index]["container"] as Control

	var viewport_height = get_viewport_rect().size.y
	var margin := 80.0  # 画面端からの余裕
	var top_bar_height := 120.0  # TopBarの高さ

	# セクションの画面上でのY位置（スクロール考慮済み）
	var section_top = container.global_position.y
	var section_bottom = section_top + container.size.y

	var current_scroll = _scroll_container.scroll_vertical
	var scroll_target = current_scroll

	if section_top < top_bar_height + margin:
		# 上にはみ出しそう → セクション上端が見えるまでスクロール
		var offset = (top_bar_height + margin) - section_top
		scroll_target = maxi(int(current_scroll - offset), 0)
	elif section_bottom > viewport_height - margin:
		# 下にはみ出しそう → セクション下端が見えるまでスクロール
		var offset = section_bottom - (viewport_height - margin)
		scroll_target = int(current_scroll + offset)

	if scroll_target != current_scroll:
		# Tweenでスムーズスクロール
		if _scroll_tween and _scroll_tween.is_running():
			_scroll_tween.kill()
		_scroll_tween = create_tween()
		_scroll_tween.tween_property(_scroll_container, "scroll_vertical", scroll_target, 0.3)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

# --- タイル収集・シグナル接続 ---

func _collect_focusable_tiles(container: Control, section_type: int, tiles: Array[Control]) -> void:
	match section_type:
		0:  # 通常行: ThumbnailRow 内の Tile_* ラッパー内の TilePanel
			var thumb_row = container.get_node_or_null("ThumbnailRow")
			if thumb_row:
				for child in thumb_row.get_children():
					if child.name.begins_with("Tile_"):
						var tile_panel = child.get_node_or_null("TilePanel")
						if tile_panel:
							tiles.append(tile_panel)
						else:
							tiles.append(child)
		1:  # スライドショー: BannerClip（窓）が1つのフォーカス対象
			var clip = container.get_node_or_null("BannerClip")
			if clip:
				tiles.append(clip)
		2:  # タイルグリッド: GridTile_* パネル
			for child in container.get_children():
				if child is Panel and child.name.begins_with("GridTile_"):
					tiles.append(child)

func _connect_tile_signals(section_index: int, tiles: Array[Control]) -> void:
	for tile_idx in range(tiles.size()):
		var tile = tiles[tile_idx]
		var sec_idx = section_index
		var t_idx = tile_idx
		tile.gui_input.connect(func(event: InputEvent):
			if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
				_current_section = sec_idx
				_current_tile = t_idx
				_on_view_all = false
				_update_focus_visual()
				_on_select()
		)
		tile.mouse_entered.connect(func():
			_using_mouse = true
			_current_section = sec_idx
			_current_tile = t_idx
			_on_view_all = false
			_update_focus_visual()
		)

# --- ボタンハンドラ ---

func _on_view_all_pressed(section_index: int) -> void:
	_current_section = section_index
	_on_view_all = true
	_on_select()

func _on_all_games_pressed() -> void:
	var all_games = _db_manager.get_all_games()
	if all_games.is_empty():
		return
	AppState.filtered_games = all_games
	AppState.initial_game_id = all_games[0].game_id
	AppState.return_scene = "res://scenes/store_browse.tscn"
	AppState.section_title = "すべてのゲーム"
	TransitionManager.change_scene("res://scenes/game_selection.tscn")

func _scroll_to_all_games_button() -> void:
	if not _all_games_button:
		return
	var viewport_height = get_viewport_rect().size.y
	var margin := 80.0
	var btn_bottom = _all_games_button.global_position.y + _all_games_button.size.y
	var current_scroll = _scroll_container.scroll_vertical
	if btn_bottom > viewport_height - margin:
		var offset = btn_bottom - (viewport_height - margin)
		var scroll_target = int(current_scroll + offset)
		if _scroll_tween and _scroll_tween.is_running():
			_scroll_tween.kill()
		_scroll_tween = create_tween()
		_scroll_tween.tween_property(_scroll_container, "scroll_vertical", scroll_target, 0.3)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

func _on_exit_button_pressed() -> void:
	var callback = func(idx):
		if idx == 1:
			IdleManager.transition_to_screensaver(get_tree())
	DialogManager.show_message("確認", "退出しますか？\nタイトル画面に戻ります。",
		["キャンセル", "退出する"], callback,
		[Color(0.3, 0.3, 0.3), Color(0.8, 0.2, 0.2)])
