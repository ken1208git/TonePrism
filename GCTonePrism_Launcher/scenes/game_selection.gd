extends Control

## ゲーム選択画面（オーケストレーター）
## 各コンポーネントの連携を担当する

# --- コンポーネント ---
var _carousel: CarouselController
var _game_launcher: GameLauncher
var _idle_mgr: IdleManager
var _style_mgr: ButtonStyleManager
var _glow_animator: GlowAnimator
var _info_display: GameInfoDisplay
var _input_handler: InputHandler

# --- ノード参照 ---
@onready var _carousel_container: Control = $CarouselContainer
@onready var _card_template: Panel = $CarouselContainer/CardTemplate
@onready var _static_focus_border: Panel = $CarouselContainer/StaticFocusBorder
@onready var _info_panel: Panel = $InfoPanel

@onready var _title_label: Label = $InfoPanel/MarginContainer/VBoxContainer/TitleContainer/TitleScrollWrapper/TitleLabel
@onready var _creator_label: Label = $InfoPanel/MarginContainer/VBoxContainer/CreatorScrollWrapper/CreatorLabel

@onready var _players_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayersContainer/PlayersValueLabel
@onready var _difficulty_bar: ProgressBar = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/DifficultyContainer/DifficultyBar
@onready var _difficulty_val_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/DifficultyContainer/DifficultyValueLabel
@onready var _playtime_bar: ProgressBar = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayTimeContainer/PlayTimeBar
@onready var _playtime_val_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayTimeContainer/PlayTimeValueLabel
@onready var _controller_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/ControllerContainer/ControllerValueLabel
@onready var _online_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/OnlineContainer/OnlineValueLabel

@onready var _desc_label: Label = $InfoPanel/MarginContainer/VBoxContainer/DescriptionScroll/DescLabel

@onready var _play_button: Button = $InfoPanel/MarginContainer/VBoxContainer/TitleContainer/PlayButton
@onready var _top_bar: Control = $TopBar
@onready var _bottom_bar: Control = $BottomBar
@onready var _clock_label: Label = $TopBar/MarginContainer/HBoxContainer/ClockLabel
@onready var _exit_button: Button = $TopBar/MarginContainer/HBoxContainer/ExitButton
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

func _ready():
	# コンポーネント初期化（_process()で参照されるため、早期returnより先に生成）
	_carousel = CarouselController.new()
	_game_launcher = GameLauncher.new()
	_idle_mgr = IdleManager.new()
	_style_mgr = ButtonStyleManager.new()
	_glow_animator = GlowAnimator.new()
	_info_display = GameInfoDisplay.new()
	_input_handler = InputHandler.new()

	if not _load_games_from_db() or _games.is_empty():
		return

	# ダイアログ表示中もアイドルタイマーを動かすため
	process_mode = Node.PROCESS_MODE_ALWAYS

	# カルーセル生成
	_carousel.create_cards(_games, _card_template, _carousel_container)

	# フォーカス枠を最前面に
	_static_focus_border.z_index = 100

	# ボタンスタイル設定（スタイルは .tscn で適用済み）
	_glow_animator.register_focus_border(_static_focus_border)
	_style_mgr.setup_exit_button(_exit_button)

	# 戻るボタン（ブラウズから来たときのみ表示）
	if not AppState.return_scene.is_empty() and _exit_button:
		var back_button = preload("res://scenes/components/back_button.tscn").instantiate()
		_top_bar.add_child(back_button)
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
			_top_bar.add_child(section_label)
			section_label.position = Vector2(160, 80)

	# ボタンシグナル
	if _exit_button:
		_exit_button.pressed.connect(_on_exit_button_pressed)
		_exit_button.mouse_entered.connect(func(): _exit_button.grab_focus())
	if _play_button:
		_play_button.pressed.connect(func(): _launch_game())

	# InfoPanelの背景スタイルは .tscn で適用済み
	if _info_panel:
		var guide_label = _info_panel.get_node_or_null("MarginContainer/VBoxContainer/GuideLabel")
		if guide_label:
			guide_label.visible = false

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

	set_process(true)
	set_process_input(true)

func _process(delta):
	# 時計更新
	GameInfoFormatter.update_clock(_clock_label)

	# グローアニメーション
	_glow_animator.update(delta)

	# ゲーム実行中の監視
	_game_launcher.monitor_process(get_window(), null,
		_games[_selected_index] if not _games.is_empty() else null,
		null, _carousel.card_nodes,
		_info_panel, _top_bar, _static_focus_border,
		_carousel_container, _bottom_bar)

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

	# ボトムバーの表示切替
	if _bottom_bar:
		_bottom_bar.visible = not _input_handler.using_mouse

func _input(event):
	if get_tree().paused:
		return
	_input_handler.handle_input(event, get_viewport(),
		_play_button, _exit_button,
		_game_launcher.is_running(), _games.is_empty())

func _unhandled_input(event):
	if get_tree().paused:
		return
	_input_handler.handle_unhandled_input(event, get_viewport())

func _exit_tree():
	if DialogManager and DialogManager.has_method("close_current_dialog"):
		DialogManager.close_current_dialog()

# --- 内部メソッド ---

func _on_selection_moved(dir: int) -> void:
	var new_index = clampi(_selected_index + dir, 0, _games.size() - 1)
	if new_index != _selected_index:
		_selected_index = new_index
		var dir_y = -1 if dir > 0 else 1
		_update_info_display(_selected_index, dir_y)
		_update_arrow_visibility()
	elif dir < 0 and _selected_index == 0 and _input_handler.back_button:
		# カルーセル最上端で上入力 → 戻るボタンにフォーカス
		_input_handler.back_button.grab_focus()

func _update_info_display(index: int, slide_dir_y: int = 0) -> void:
	_info_display.update_display(_games[index], slide_dir_y,
		_title_label, _creator_label, _desc_label,
		_players_label, _difficulty_bar, _difficulty_val_label,
		_playtime_bar, _playtime_val_label,
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
		_carousel_container, _info_panel, _top_bar,
		_static_focus_border, _carousel.card_nodes, _selected_index,
		get_tree(), _bottom_bar)

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
			
		_carousel_container.add_child(btn)

func _update_arrow_visibility() -> void:
	if not is_instance_valid(_carousel_container): return
	var up_btn = _carousel_container.get_node_or_null("ScrollUpButton")
	var down_btn = _carousel_container.get_node_or_null("ScrollDownButton")
	
	if up_btn:
		up_btn.visible = (_selected_index > 0)
	if down_btn:
		down_btn.visible = (_selected_index < _games.size() - 1)
