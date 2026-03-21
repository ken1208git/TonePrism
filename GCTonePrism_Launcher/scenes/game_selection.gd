extends Control

## ゲーム選択画面（オーケストレーター）
## 各コンポーネントの連携を担当する

# --- コンポーネント ---
var _carousel: CarouselController
var _game_launcher: GameLauncher
var _idle_mgr: IdleManager
var _style_mgr: ButtonStyleManager
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
@onready var _running_overlay: Control = $RunningOverlay
@onready var _status_label: Label = $RunningOverlay/StatusContainer/StatusLabel

@onready var _play_button: Button = $InfoPanel/MarginContainer/VBoxContainer/TitleContainer/PlayButton
@onready var _top_bar: Control = $TopBar
@onready var _bottom_bar: Control = $BottomBar
@onready var _clock_label: Label = $TopBar/MarginContainer/HBoxContainer/ClockLabel
@onready var _exit_button: Button = $TopBar/MarginContainer/HBoxContainer/ExitButton
@onready var _background_texture: TextureRect = $BackgroundLayer/BackgroundTexture
@onready var _background_old: TextureRect = $BackgroundLayer/BackgroundTextureOld

# --- 状態 ---
var _db_manager: DatabaseManager
var _games: Array[GameInfo] = []
var _selected_index: int = 0
var _active_index: int = 0

func _ready():
	if not _load_games_from_db() or _games.is_empty():
		return

	# ダイアログ表示中もアイドルタイマーを動かすため
	process_mode = Node.PROCESS_MODE_ALWAYS

	# コンポーネント初期化
	_carousel = CarouselController.new()
	_game_launcher = GameLauncher.new()
	_idle_mgr = IdleManager.new()
	_style_mgr = ButtonStyleManager.new()
	_info_display = GameInfoDisplay.new()
	_input_handler = InputHandler.new()

	# カルーセル生成
	_carousel.create_cards(_games, _card_template, _carousel_container)

	# ボタンスタイル設定
	_style_mgr.register_focus_border(_static_focus_border)
	_style_mgr.setup_exit_button(_exit_button)
	_style_mgr.setup_play_button(_play_button)

	# ボタンシグナル
	if _exit_button:
		_exit_button.pressed.connect(_on_exit_button_pressed)
		_exit_button.mouse_entered.connect(func(): _exit_button.grab_focus())
	if _play_button:
		_play_button.pressed.connect(func(): _launch_game())

	# InfoPanelの背景スタイル
	var info_style = StyleBoxFlat.new()
	info_style.bg_color = Color(0, 0, 0, 0.7)
	info_style.set_corner_radius_all(20)
	if _info_panel:
		_info_panel.add_theme_stylebox_override("panel", info_style)
		var guide_label = _info_panel.get_node_or_null("MarginContainer/VBoxContainer/GuideLabel")
		if guide_label:
			guide_label.visible = false

	# 入力ハンドラのシグナル接続
	_input_handler.selection_moved.connect(_on_selection_moved)
	_input_handler.play_requested.connect(func(): if _play_button: _play_button.grab_focus())
	_input_handler.exit_requested.connect(func(): IdleManager.transition_to_screensaver(get_tree()))
	_input_handler.focus_to_card_requested.connect(_update_focus_to_current_card)
	_input_handler.idle_reset_requested.connect(func(): _idle_mgr.reset())

	# ゲームランチャーのシグナル接続
	_game_launcher.game_ended.connect(_update_focus_to_current_card)

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
	GameInfoDisplay.update_clock(_clock_label)

	# グローアニメーション
	_style_mgr.update_glow(delta)

	# ゲーム実行中の監視
	_game_launcher.monitor_process(get_window(), _status_label,
		_games[_selected_index] if not _games.is_empty() else null,
		_running_overlay, _carousel.card_nodes,
		_info_panel, _top_bar, _static_focus_border)

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
		_input_handler.using_mouse, _static_focus_border)

	if new_active != _active_index:
		_active_index = new_active
		_update_focus_state()

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
	_game_launcher.launch_game(_games[_selected_index], _status_label, _running_overlay,
		_carousel_container, _info_panel, _top_bar,
		_static_focus_border, _carousel.card_nodes, _selected_index,
		get_tree())

func _on_exit_button_pressed() -> void:
	var callback = func(idx):
		if idx == 1:
			IdleManager.transition_to_screensaver(get_tree())
	DialogManager.show_message("確認", "退出しますか？\nタイトル画面に戻ります。",
		["キャンセル", "退出する"], callback,
		[Color(0.3, 0.3, 0.3), Color(0.8, 0.2, 0.2)])

func _load_games_from_db() -> bool:
	_db_manager = DatabaseManager.new()
	var db_path = PathManager.get_database_path()
	print("[GameSelection] データベース読み込み開始. ターゲットパス: ", db_path)

	if not _db_manager.open():
		print("[GameSelection] ❌ データベースのオープンに失敗しました (E-1001)")
		ErrorManager.show_error(ErrorCode.DATABASE_NOT_FOUND)
		set_process_input(false)
		return false

	_games = _db_manager.get_all_games()
	_db_manager.close()

	if _games.is_empty():
		print("[GameSelection] ⚠️ データベースは開けましたが、表示対象のゲームが0件です (E-1006)")
		ErrorManager.show_error(ErrorCode.DATABASE_NO_GAMES_REGISTERED)
		set_process_input(false)
		return true

	print("[GameSelection] ✅ DB読み込み完了: %d 件のゲームが見つかりました" % _games.size())
	return true
