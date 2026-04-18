extends Control

## ゲーム選択画面（オーケストレーター）
## 各コンポーネントの連携を担当する

# --- コンポーネント ---
var _carousel: CarouselController
var _game_launcher: GameLauncher
var _idle_mgr: IdleManager
var _glow_animator: GlowAnimator
var _info_display: GameInfoDisplay
var _input_handler: InputHandler

# --- ノード参照 ---
@onready var _carousel_container: Control = $CarouselContainer
@onready var _card_template: Panel = $CarouselContainer/CardTemplate
@onready var _static_focus_border: Panel = $CarouselContainer/FocusLayer/StaticFocusBorder
@onready var _info_panel: Panel = $InfoPanel

@onready var _title_label: Label = $InfoPanel/MarginContainer/VBoxContainer/TitleContainer/TitleScrollWrapper/TitleLabel
@onready var _creator_tags_container: HBoxContainer = $InfoPanel/MarginContainer/VBoxContainer/CreatorScrollWrapper/CreatorTagsContainer

@onready var _players_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayersContainer/PlayersValueLabel
@onready var _difficulty_val_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/DifficultyContainer/DifficultyValueLabel
@onready var _playtime_val_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayTimeContainer/PlayTimeValueLabel
@onready var _controller_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/ControllerContainer/ControllerValueLabel
@onready var _online_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/OnlineContainer/OnlineValueLabel

@onready var _desc_scroll: ScrollContainer = $InfoPanel/MarginContainer/VBoxContainer/DescriptionScroll
@onready var _desc_label: Label = $InfoPanel/MarginContainer/VBoxContainer/DescriptionScroll/DescLabel

@onready var _play_button: Button = $InfoPanel/MarginContainer/VBoxContainer/TitleContainer/PlayButton
@onready var _top_bar: CanvasLayer = $TopBar
@onready var _bottom_bar: CanvasLayer = $BottomBar
@onready var _background_texture: TextureRect = $BackgroundLayer/BackgroundTexture
@onready var _background_old: TextureRect = $BackgroundLayer/BackgroundTextureOld

# --- 状態 ---
var _db_manager: DatabaseManager
var _game_repo: GameRepository
var _games: Array[GameInfo] = []
var _selected_index: int = 0
var _active_index: int = 0

# --- フォーカスモーフ用 ---
var _focus_target_rect: Rect2 = Rect2()
var _focus_target_radius: float = 24.0
var _focus_current_radius: float = 24.0
var _focus_initialized: bool = false
var _focus_tweening: bool = false
var _focus_prev_target: Control = null
var _focus_prev_target_pos: Vector2 = Vector2.ZERO
var _exit_button: Button
var _desc_hint_active: bool = false

# --- サムネイル非同期読み込み ---
var _thumb_load_thread: Thread = null
var _thumb_load_mutex: Mutex = Mutex.new()
var _thumb_load_queue: Array = []
var _thumb_loaded_images: Array = []
var _thumb_cancel_requested: bool = false
var _thumb_node_registry: Dictionary = {}  # node_id -> Panel(card)

func _ready():
	# コンポーネント初期化（_process()で参照されるため、早期returnより先に生成）
	_carousel = CarouselController.new()
	_game_launcher = GameLauncher.new()
	_idle_mgr = IdleManager.new()
	_glow_animator = GlowAnimator.new()
	_info_display = GameInfoDisplay.new()
	_input_handler = InputHandler.new()

	if not _load_games_from_db() or _games.is_empty():
		return

	# ダイアログ表示中もアイドルタイマーを動かすため
	process_mode = Node.PROCESS_MODE_ALWAYS

	# カルーセル生成
	_carousel.create_cards(_games, _card_template, _carousel_container)

	# サムネイルのバックグラウンド読み込みを開始
	_start_thumbnail_loading()

	# フォーカス枠は FocusLayer(CanvasLayer layer=11) で TopBar より前面に表示

	# ボタンスタイル設定
	_glow_animator.register_focus_border(_static_focus_border)
	_exit_button = _top_bar.get_exit_button()
	_top_bar.exit_pressed.connect(_on_exit_button_pressed)

	# 戻るボタン（ブラウズから来たときのみ表示）
	if not AppState.return_scene.is_empty() and _exit_button:
		var back_button = preload("res://scenes/components/back_button.tscn").instantiate()
		_top_bar.get_panel().add_child(back_button)
		back_button.position = Vector2(40, 80)
		# 黒アイコンを白に反転
		var shader = load("res://resources/shaders/invert_color.gdshader")
		var mat = ShaderMaterial.new()
		mat.shader = shader
		back_button.material = mat
		back_button.pressed.connect(func(): _go_back())
		back_button.mouse_entered.connect(func(): back_button.grab_focus())
		_input_handler.back_button = back_button
		# セクション名ラベル（戻るボタンの横）
		if not AppState.section_title.is_empty():
			var section_label = Label.new()
			section_label.text = AppState.section_title
			section_label.add_theme_font_override("font", preload("res://fonts/NotoSansJP-Bold.ttf"))
			section_label.add_theme_font_size_override("font_size", 56)
			section_label.add_theme_color_override("font_color", Color(1, 1, 1, 1))
			section_label.custom_minimum_size = Vector2(0, 90)
			section_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
			_top_bar.get_panel().add_child(section_label)
			section_label.position = Vector2(160, 80)

	# ボタンシグナル
	if _exit_button:
		_exit_button.mouse_entered.connect(func(): _exit_button.grab_focus())
	if _play_button:
		_play_button.pressed.connect(func(): _launch_game())

	# InfoPanelの背景スタイルは .tscn で適用済み
	if _info_panel:
		var guide_label = _info_panel.get_node_or_null("MarginContainer/VBoxContainer/GuideLabel")
		if guide_label:
			guide_label.visible = false

	# 説明文スクロール参照
	_input_handler.desc_scroll = _desc_scroll
	_input_handler.desc_label = _desc_label

	# 入力ハンドラのシグナル接続
	_input_handler.selection_moved.connect(_on_selection_moved)
	_input_handler.play_requested.connect(func(): if _play_button: _play_button.grab_focus())
	_input_handler.exit_requested.connect(func(): _go_back())
	_input_handler.focus_to_card_requested.connect(_update_focus_to_current_card)
	_input_handler.idle_reset_requested.connect(func(): _idle_mgr.reset())

	# ゲームランチャーのシグナル接続
	_game_launcher.game_ended.connect(func():
		_update_focus_to_current_card()
		_update_arrow_visibility()
	)

	# カルーセルの上下矢印ボタンを追加
	_add_carousel_arrow_buttons()
	_update_arrow_visibility()

	# 初期表示
	_carousel.current_scroll_index = float(_selected_index)
	_update_info_display(_selected_index)

	# フォーカスの初期化（ノードが確定してから）
	call_deferred("_update_focus_state")

	# フォーカスナビゲーション設定
	if _play_button and _exit_button:
		_exit_button.focus_neighbor_bottom = _play_button.get_path()
		_play_button.focus_neighbor_top = _exit_button.get_path()
		_play_button.focus_neighbor_left = _play_button.get_path()
		_play_button.focus_neighbor_right = _play_button.get_path()

	# カルーセル画面の操作ヒント
	_bottom_bar.set_hints([["Esc", "戻る"], ["Enter", "決定"]])

	set_process(true)
	set_process_input(true)

func _process(delta):
	# サムネイル非同期読み込み適用
	_apply_thumb_loaded_images()

	# グローアニメーション
	_glow_animator.update(delta)

	# ゲーム実行中の監視
	_game_launcher.monitor_process(get_window(), null,
		_games[_selected_index] if not _games.is_empty() else null,
		null, _carousel.card_nodes,
		_info_panel, _top_bar.get_panel(), _static_focus_border,
		_carousel_container, _bottom_bar.get_panel())

	# アイドルタイマー
	if _game_launcher.is_running():
		_idle_mgr.reset()
	elif _idle_mgr.update(delta, get_tree().paused):
		IdleManager.transition_to_screensaver(get_tree())
		return

	# シーン遷移中のガード
	if not is_inside_tree() or get_tree() == null:
		return
	if get_tree().paused:
		return

	# ドラムロール入力
	_input_handler.process_drum_roll(delta, _play_button, _exit_button, _game_launcher.is_running())
	_input_handler.update_desc_scroll(delta)

	if _games.is_empty():
		return

	# カルーセル更新
	var container_center_x = _carousel_container.size.x / 2
	var new_active = _carousel.update_cards(delta, _selected_index,
		get_viewport_rect().size, container_center_x,
		_input_handler.using_mouse, _static_focus_border, _game_launcher.is_running())

	if new_active != _active_index:
		_active_index = new_active
		_update_focus_state()

	# フォーカスモーフ更新
	_update_focus_morph(delta)

	# ボトムバーの表示切替・ヒント更新
	if _bottom_bar:
		_bottom_bar.get_panel().visible = not _input_handler.using_mouse
		var desc_focused := _desc_scroll and _desc_scroll.has_focus()
		if desc_focused and not _desc_hint_active:
			_bottom_bar.set_hints([["Esc", "戻る"]])
			_desc_hint_active = true
		elif not desc_focused and _desc_hint_active:
			_bottom_bar.set_hints([["Esc", "戻る"], ["Enter", "決定"]])
			_desc_hint_active = false

func _input(event):
	if get_tree().paused:
		return
	# 説明文エリア上のホイールを横取り → lerp補間で滑らかスクロール
	if event is InputEventMouseButton and event.is_pressed():
		if _desc_scroll and _desc_scroll.get_global_rect().has_point(event.global_position):
			if event.button_index == MOUSE_BUTTON_WHEEL_UP or event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
				_input_handler.handle_desc_wheel(event, get_viewport())
				return
	_input_handler.handle_input(event, get_viewport(),
		_play_button, _exit_button,
		_game_launcher.is_running(), _games.is_empty())

func _unhandled_input(event):
	if get_tree().paused:
		return
	_input_handler.handle_unhandled_input(event, get_viewport())

func _exit_tree():
	# サムネイル読み込みスレッドをキャンセル
	if _thumb_load_thread and _thumb_load_thread.is_started():
		_thumb_load_mutex.lock()
		_thumb_cancel_requested = true
		_thumb_load_queue.clear()
		_thumb_load_mutex.unlock()
		_thumb_load_thread.wait_to_finish()
	if DialogManager and DialogManager.has_method("close_current_dialog"):
		DialogManager.close_current_dialog()

# --- サムネイル非同期読み込み ---

func _start_thumbnail_loading() -> void:
	var queue = _carousel.get_image_load_queue()
	if queue.is_empty():
		return
	for item in queue:
		_thumb_node_registry[item["node_id"]] = item["card"]
		_thumb_load_queue.append({"node_id": item["node_id"], "path": item["path"]})
	# LOADINGラベルにシマーエフェクト + 暗い背景（Clipper直下に配置）
	for card in _carousel.card_nodes:
		var clipper = card.get_node_or_null("Clipper")
		if not clipper:
			continue
		var label = clipper.get_node_or_null("NoImageLabel")
		if label and label.visible and label.text == "LOADING":
			# 既存の DimBackground があれば重複追加しない
			if not clipper.get_node_or_null("DimBackground"):
				var dim = ColorRect.new()
				dim.name = "DimBackground"
				dim.color = Color(0.08, 0.08, 0.08, 1.0)
				dim.set_anchors_preset(Control.PRESET_FULL_RECT)
				dim.mouse_filter = Control.MOUSE_FILTER_IGNORE
				clipper.add_child(dim)
				# NoImageLabelの前に配置（テキストが前面に来るよう）
				clipper.move_child(dim, label.get_index())
			ShimmerHelper.apply_to_label(label)
	_thumb_load_thread = Thread.new()
	_thumb_load_thread.start(_thumb_load_images_in_thread)

func _thumb_load_images_in_thread() -> void:
	while true:
		_thumb_load_mutex.lock()
		if _thumb_cancel_requested or _thumb_load_queue.is_empty():
			_thumb_load_mutex.unlock()
			break
		var item = _thumb_load_queue.pop_front()
		_thumb_load_mutex.unlock()

		var image = Image.load_from_file(item["path"])
		if image:
			_thumb_load_mutex.lock()
			if _thumb_cancel_requested:
				_thumb_load_mutex.unlock()
				break
			_thumb_loaded_images.append({
				"image": image,
				"node_id": item["node_id"]
			})
			_thumb_load_mutex.unlock()

func _apply_thumb_loaded_images() -> void:
	_thumb_load_mutex.lock()
	if _thumb_loaded_images.is_empty():
		_thumb_load_mutex.unlock()
		return

	var apply_count = mini(2, _thumb_loaded_images.size())
	var to_apply: Array = []
	for i in range(apply_count):
		to_apply.append(_thumb_loaded_images.pop_front())
	_thumb_load_mutex.unlock()

	for item in to_apply:
		var card = _thumb_node_registry.get(item["node_id"]) as Panel
		if not card or not is_instance_valid(card):
			continue
		var tex = ImageTexture.create_from_image(item["image"])
		var icon_rect = card.get_node_or_null("Clipper/Icon") as TextureRect
		if icon_rect:
			icon_rect.texture = tex
			icon_rect.visible = true
			icon_rect.modulate = Color(1, 1, 1, 0)
			var tween = card.create_tween()
			tween.tween_property(icon_rect, "modulate:a", 1.0, 0.2)
		var no_image_label = card.get_node_or_null("Clipper/NoImageLabel")
		if no_image_label:
			no_image_label.queue_free()
		var dim_bg = card.get_node_or_null("Clipper/DimBackground")
		if dim_bg:
			dim_bg.queue_free()

# --- 内部メソッド ---

func _on_selection_moved(dir: int) -> void:
	var new_index = clampi(_selected_index + dir, 0, _games.size() - 1)
	if new_index != _selected_index:
		_selected_index = new_index
		var dir_y = -1 if dir > 0 else 1
		_update_info_display(_selected_index, dir_y)
		_update_arrow_visibility()
		_input_handler.reset_desc_scroll()
	elif dir < 0 and _selected_index == 0 and _input_handler.back_button:
		# カルーセル最上端で上入力 → 戻るボタンにフォーカス
		_input_handler.back_button.grab_focus()

func _update_info_display(index: int, slide_dir_y: int = 0) -> void:
	_info_display.update_display(_games[index], slide_dir_y,
		_title_label, _creator_tags_container, _desc_label,
		_players_label, _difficulty_val_label,
		_playtime_val_label,
		_controller_label, _online_label,
		_background_texture, _background_old, self)

func _update_focus_to_current_card() -> void:
	if _carousel.card_nodes.size() > _active_index:
		var card = _carousel.card_nodes[_active_index]
		if not card.has_focus():
			card.grab_focus()

func _update_focus_state() -> void:
	var focus_owner = get_viewport().gui_get_focus_owner()
	if focus_owner and (focus_owner is Panel and focus_owner in _carousel.card_nodes):
		_update_focus_to_current_card()

func _launch_game() -> void:
	if _games.is_empty() or _game_launcher.is_running():
		return
	_game_launcher.launch_game(_games[_selected_index], null, null,
		_carousel_container, _info_panel, _top_bar.get_panel(),
		_static_focus_border, _carousel.card_nodes, _selected_index,
		get_tree(), _bottom_bar.get_panel())

func _go_back() -> void:
	if TransitionManager._transitioning:
		return
	if not AppState.return_scene.is_empty():
		var scene = AppState.return_scene
		TransitionManager.change_scene(scene)
		AppState.clear()
	else:
		IdleManager.transition_to_screensaver(get_tree())

func _on_exit_button_pressed() -> void:
	var callback = func(idx):
		if idx == 1:
			AppState.clear()
			IdleManager.transition_to_screensaver(get_tree())
	DialogManager.show_message("確認", "退出しますか？\nタイトル画面に戻ります。",
		["キャンセル", "退出する"], callback,
		[Color(0.3, 0.3, 0.3), Color(0.8, 0.2, 0.2)])

func _load_games_from_db() -> bool:
	# AppStateにフィルタ済みゲームがあればそちらを使用
	if not AppState.filtered_games.is_empty():
		_games = AppState.filtered_games
		# initial_game_id に合わせて _selected_index を設定
		if not AppState.initial_game_id.is_empty():
			for i in range(_games.size()):
				if _games[i].game_id == AppState.initial_game_id:
					_selected_index = i
					break
		print("[GameSelection] ✅ AppStateからゲーム読み込み: %d 件 (セクション: %s)" % [_games.size(), AppState.section_title])
		return true

	# 従来のDB読み込み（フォールバック）
	_db_manager = DatabaseManager.new()
	var db_path = PathManager.get_database_path()
	print("[GameSelection] データベース読み込み開始. ターゲットパス: ", db_path)

	if not _db_manager.open():
		print("[GameSelection] ❌ データベースのオープンに失敗しました (E-1001)")
		ErrorManager.show_error(ErrorCode.DATABASE_NOT_FOUND)
		set_process_input(false)
		return false

	_game_repo = GameRepository.new(_db_manager)
	_games = _game_repo.get_all_games()
	_db_manager.close()

	if _games.is_empty():
		print("[GameSelection] ⚠️ データベースは開けましたが、表示対象のゲームが0件です (E-1006)")
		ErrorManager.show_error(ErrorCode.DATABASE_NO_GAMES_REGISTERED)
		set_process_input(false)
		return true

	print("[GameSelection] ✅ DB読み込み完了: %d 件のゲームが見つかりました" % _games.size())
	return true

# --- フォーカスモーフ ---

func _update_focus_morph(delta: float) -> void:
	if not _static_focus_border:
		return

	# シーン遷移中は非表示（遷移完了後に正確な位置へ即座配置するためリセット）
	if TransitionManager._transitioning:
		_static_focus_border.visible = false
		_focus_initialized = false
		return

	# マウス操作中は非表示
	if _input_handler.using_mouse:
		_static_focus_border.visible = false
		_focus_initialized = false
		return

	# フォーカスオーナーを取得して目標を決定
	var focus_owner = get_viewport().gui_get_focus_owner()
	if focus_owner == null:
		_static_focus_border.visible = false
		return

	var target: Control = null
	var target_radius: float = 24.0

	if focus_owner is Panel and focus_owner in _carousel.card_nodes:
		# カード → アクティブサイズで画面中央に固定（scaleアニメの影響を受けない）
		target = focus_owner
		target_radius = CarouselController.CORNER_RADIUS * CarouselController.SCALE_ACTIVE + CarouselController.FOCUS_MARGIN
		var active_size = CarouselController.CARD_SIZE * CarouselController.SCALE_ACTIVE
		var viewport_center = get_viewport_rect().size / 2
		var container_center_x = _carousel_container.size.x / 2
		_static_focus_border.visible = true
		_focus_target_rect = Rect2(
			Vector2(container_center_x - active_size.x / 2, viewport_center.y - active_size.y / 2),
			active_size)
		_focus_target_radius = target_radius
	elif focus_owner is Button:
		# ボタン（退出/戻る/プレイ）
		target = focus_owner
		target_radius = 18
		_static_focus_border.visible = true
		_focus_target_rect = target.get_global_rect()
		_focus_target_radius = target_radius
	elif focus_owner == _desc_scroll:
		# 説明文スクロール
		target = focus_owner
		target_radius = 8
		_static_focus_border.visible = true
		_focus_target_rect = target.get_global_rect()
		_focus_target_radius = target_radius
	else:
		_static_focus_border.visible = false
		return

	# 初回はズームイン登場
	if not _focus_initialized:
		_static_focus_border.global_position = _focus_target_rect.position
		_static_focus_border.size = _focus_target_rect.size
		_focus_current_radius = _focus_target_radius
		_focus_prev_target = target
		_focus_prev_target_pos = _focus_target_rect.position
		_focus_initialized = true
		_focus_tweening = true
		var style = _static_focus_border.get_theme_stylebox("panel") as StyleBoxFlat
		if style:
			style.set_corner_radius_all(int(_focus_current_radius))
		# ズームイン + フェードイン（中心からスケール）
		_static_focus_border.pivot_offset = _focus_target_rect.size / 2.0
		_static_focus_border.scale = Vector2(1.15, 1.15)
		_static_focus_border.modulate.a = 0.0
		var tween = create_tween()
		tween.set_parallel(true)
		tween.tween_property(_static_focus_border, "scale", Vector2.ONE, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		tween.tween_property(_static_focus_border, "modulate:a", 1.0, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		tween.finished.connect(func(): _focus_tweening = false)
		return

	# Tween中は位置/サイズの更新をスキップ（干渉防止）
	if _focus_tweening:
		_focus_prev_target = target
		_focus_prev_target_pos = _focus_target_rect.position
		return

	# 同じターゲットの位置変化（カードアニメーション等）は即座反映
	if target == _focus_prev_target:
		var target_delta = _focus_target_rect.position - _focus_prev_target_pos
		_static_focus_border.global_position += target_delta

	# lerpで補間
	var speed = delta * 25.0
	_static_focus_border.global_position = _static_focus_border.global_position.lerp(
		_focus_target_rect.position, speed)
	_static_focus_border.size = _static_focus_border.size.lerp(
		_focus_target_rect.size, speed)
	_focus_current_radius = lerpf(_focus_current_radius, _focus_target_radius, speed)
	var style = _static_focus_border.get_theme_stylebox("panel") as StyleBoxFlat
	if style:
		style.set_corner_radius_all(int(_focus_current_radius))

	_focus_prev_target = target
	_focus_prev_target_pos = _focus_target_rect.position

func _add_carousel_arrow_buttons() -> void:
	var arrow_size := Vector2(80, 80)
	var pad = 12.0

	for i in range(2):
		var is_up = (i == 0)
		var btn = Button.new()
		btn.name = "ScrollUpButton" if is_up else "ScrollDownButton"
		btn.flat = true
		btn.custom_minimum_size = arrow_size
		btn.size = arrow_size
		btn.focus_mode = Control.FOCUS_NONE
		btn.mouse_filter = Control.MOUSE_FILTER_STOP
		
		# 画面中央(選択中の大きなアイコン位置)を基準に配置
		btn.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
		btn.offset_left = -arrow_size.x / 2.0
		btn.offset_right = arrow_size.x / 2.0
		
		# さっき（240）と今（200）の中間で220pxに設定
		var y_offset = 220.0
		if is_up:
			btn.offset_top = -y_offset - arrow_size.y / 2.0
			btn.offset_bottom = -y_offset + arrow_size.y / 2.0
		else:
			btn.offset_top = y_offset - arrow_size.y / 2.0
			btn.offset_bottom = y_offset + arrow_size.y / 2.0

		# アイコン
		var icon = TextureRect.new()
		icon.texture = preload("res://resources/icons/arrow.png")
		icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		
		# 白化シェーダー
		var mat = ShaderMaterial.new()
		mat.shader = preload("res://resources/shaders/invert_color.gdshader")
		icon.material = mat
		
		# 向きの回転 (元が左向き◀前提)
		icon.pivot_offset = (arrow_size - Vector2(pad * 2, pad * 2)) / 2.0
		icon.rotation_degrees = 90 if is_up else -90
		
		icon.position = Vector2(pad, pad)
		icon.size = arrow_size - Vector2(pad * 2, pad * 2)
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		icon.modulate.a = 0.5
		btn.add_child(icon)
		
		# ホバー/フォーカス時の見た目変化
		btn.mouse_entered.connect(func(): icon.modulate.a = 1.0)
		btn.mouse_exited.connect(func(): icon.modulate.a = 0.5)
		btn.button_down.connect(func(): icon.modulate.a = 0.3)
		btn.button_up.connect(func(): if btn.is_hovered(): icon.modulate.a = 1.0 else: icon.modulate.a = 0.5)
		
		# 押した時の処理
		if is_up:
			btn.pressed.connect(func(): _on_selection_moved(-1))
		else:
			btn.pressed.connect(func(): _on_selection_moved(1))
			
		for state_data in [
			{"state": "normal", "alpha": 0.2},
			{"state": "hover", "alpha": 0.5},
			{"state": "pressed", "alpha": 0.7}
		]:
			var s = StyleBoxFlat.new()
			s.bg_color = Color(0, 0, 0, state_data["alpha"])
			s.set_corner_radius_all(40) # 丸型
			btn.add_theme_stylebox_override(state_data["state"], s)
			
		btn.z_index = 100
		_carousel_container.add_child(btn)

func _update_arrow_visibility() -> void:
	if not is_instance_valid(_carousel_container): return
	var up_btn = _carousel_container.get_node_or_null("ScrollUpButton")
	var down_btn = _carousel_container.get_node_or_null("ScrollDownButton")
	
	if up_btn:
		up_btn.visible = (_selected_index > 0)
	if down_btn:
		down_btn.visible = (_selected_index < _games.size() - 1)
