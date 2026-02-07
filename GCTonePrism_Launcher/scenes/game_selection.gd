extends Control

## ゲーム選択画面（カルーセルUI実装）
## マイルストーン5: ゲーム表示選択機能

# --- 定数設定 ---
const CARD_SIZE := Vector2(200, 200)     # 正方形
const GAP_NARROW := 20.0                 # 非選択カード間の隙間（20pxで少し空ける）
const GAP_WIDE := 120.0                  # 選択カードとの隙間
const SCROLL_SPEED := 10.0
const SCALE_ACTIVE := 1.8                # 選択中のカードの拡大率
const SCALE_INACTIVE := 1.0              # 非選択カードの拡大率（等倍）
const OPACITY_INACTIVE := 0.6

# --- ノード参照 ---
# --- ノード参照 ---
@onready var _background_texture: TextureRect = $BackgroundLayer/BackgroundTexture
@onready var _carousel_container: Control = $CarouselContainer
@onready var _card_template: Panel = $CarouselContainer/CardTemplate
# _info_panelは直接操作しないため削除（警告対応）
@onready var _title_label: Label = $InfoPanel/MarginContainer/VBoxContainer/TitleLabel
@onready var _desc_label: Label = $InfoPanel/MarginContainer/VBoxContainer/DescLabel
@onready var _running_overlay: ColorRect = $RunningOverlay
@onready var _status_label: Label = $RunningOverlay/StatusLabel

var _card_nodes: Array[Panel] = []

# --- 状態管理 ---
var _db_manager: DatabaseManager
var _games: Array[GameInfo] = []
var _selected_index: int = 0
var _current_scroll_index: float = 0.0 # インデックス単位の現在位置
var _running_pid: int = -1 # 実行中のゲームPID (-1: 実行なし)
var _game_window_activated: bool = false # ゲームウィンドウが一度でもアクティブになったか

# --- リソース ---
var _placeholder_texture: Texture2D

func _ready():
	_load_resources()
	# UI構築はtscnで行われるため不要
	# 既に_load_games_from_db内でエラー処理済みだが、
	# ゲームリストが空の場合はUI更新を行わない（エラーダイアログが表示されているはず）
	if not _load_games_from_db() or _games.is_empty():
		return
	
	# _create_dummy_data呼び出しは削除済み
	# if _games.is_empty():
	# 	_create_dummy_data()
	
	_create_carousel_cards()
	# 初期位置セット
	_current_scroll_index = float(_selected_index)
	_update_selection(true)

	set_process(true)
	set_process_input(true)

func _load_resources():
	_placeholder_texture = load("res://icon.svg")

# --- データ読み込み ---
func _load_games_from_db() -> bool:
	_db_manager = DatabaseManager.new()
	# データベースを開く
	if not _db_manager.open():
		ErrorManager.show_error(ErrorCode.DATABASE_NOT_FOUND)
		set_process_input(false) # 入力を無効化
		return false
	
	# ゲームリストを読み込む
	_games = _db_manager.get_all_games()
	_db_manager.close() # DBは読み込み後すぐに閉じる
	
	# ゲームが見つからない場合の処理
	if _games.is_empty():
		# 以前のダミーデータ生成ロジックを廃止し、エラーダイアログを表示
		# TODO: テーブルはあるがデータがない場合は別のコード(DATABASE_TABLE_MISSING等)にするなども検討可能
		# ここでは「表示するゲームがない」という意味で、一旦DB_NOT_FOUNDまたは専用コードを使う
		# 今回はシンプルにDATABASE_TABLE_MISSING（データなし）として扱うか、
		# あるいは「ゲームが登録されていません」というメッセージを出す
		
		# 専用のエラーコードがないため、一旦DATABASE_NOT_FOUNDに近い扱いにするか、
		# メッセージを上書きして表示する
		ErrorManager.show_error(ErrorCode.DATABASE_TABLE_MISSING)
		set_process_input(false)
		return true # エラーダイアログを出したが、アプリとしては「初期化自体は完了（エラー画面ボーン）」とする
		
	print("[GameSelection] DB Load: %d games found" % _games.size())
	return true

# --- カルーセル生成 ---
func _create_carousel_cards():
	if not _card_template:
		push_error("CardTemplate not found!")
		return

	for game in _games:
		# テンプレートを複製
		var card = _card_template.duplicate()
		card.name = "Card_%s" % game.game_id
		card.visible = true # テンプレートは非表示なので表示する
		# サイズはテンプレートに従うが、念のため設定
		card.custom_minimum_size = CARD_SIZE
		card.pivot_offset = CARD_SIZE / 2
		
		# アイコン用TextureRectを取得
		var icon_rect = card.get_node("Icon") as TextureRect
		
		# サムネイル読み込み
		var thumb_path = _resolve_path(game.thumbnail_path, game.game_id)
		# 成功ログは出さず、失敗時のみ警告を出す（ログが流れるのを防ぐため）
		
		var tex_to_set = _placeholder_texture
		if not thumb_path.is_empty() and FileAccess.file_exists(thumb_path):
			var img = Image.load_from_file(thumb_path)
			var tex = ImageTexture.create_from_image(img)
			tex_to_set = tex
		else:
			# パスが設定されているのに見つからない場合は警告
			if not game.thumbnail_path.is_empty():
				push_warning("[GameSelection] ⚠️ Thumbnail NOT found for '%s' (ID: %s)\n  - DB Path: '%s'\n  - Check: '%s'" % 
					[game.title, game.game_id, game.thumbnail_path, thumb_path])
		
		if icon_rect:
			icon_rect.texture = tex_to_set
		
		_carousel_container.add_child(card)
		_card_nodes.append(card)

# --- パス解決ヘルパー ---
func _resolve_path(path: String, game_id: String) -> String:
	if path.is_empty():
		return ""
	
	# すでに絶対パス、またはres:// user://ならそのまま返す
	if path.is_absolute_path() or path.begins_with("res://") or path.begins_with("user://"):
		return path
	
	# 1. ゲームフォルダ内を探す (games/game_id/path)
	var game_folder_path = PathManager.get_game_folder(game_id).path_join(path)
	if FileAccess.file_exists(game_folder_path):
		return game_folder_path
		
	# 2. プロジェクトルート直下を探す (path) - 互換性のため
	var root_path = PathManager.get_base_directory().path_join(path)
	if FileAccess.file_exists(root_path):
		return root_path
	
	# 見つからない場合は、とりあえずゲームフォルダ内のパスを返しておく（デバッグ用）
	return game_folder_path

# --- 入力処理 ---
func _input(event):
	if _games.is_empty():
		if event.is_action_pressed("ui_cancel"):
			_transition_to_screensaver()
		return

	# ゲーム実行中は操作を受け付けない
	if _running_pid != -1:
		return

	if event.is_action_pressed("ui_up"):
		_move_selection(-1)
	elif event.is_action_pressed("ui_down"):
		_move_selection(1)
	elif event.is_action_pressed("ui_accept"):
		_launch_game()
	elif event.is_action_pressed("ui_cancel"):
		_transition_to_screensaver()

func _move_selection(dir: int):
	var prev = _selected_index
	_selected_index = clampi(_selected_index + dir, 0, _games.size() - 1)
	
	if prev != _selected_index:
		_update_selection()

func _update_selection(_instant: bool = false):
	var game = _games[_selected_index]
	
	# 情報表示更新
	_title_label.text = game.title
	_desc_label.text = game.description
	
	# 背景画像読み込み
	var bg_path = _resolve_path(game.background_path, game.game_id)
	if not bg_path.is_empty() and FileAccess.file_exists(bg_path):
		var img = Image.load_from_file(bg_path)
		var tex = ImageTexture.create_from_image(img)
		_background_texture.texture = tex
	else:
		# 背景は見つからなくても警告までは出さない（必須ではない場合も多いため）
		# が、設定されているのに見つからない場合は一応デバッグログだけ出す
		if not game.background_path.is_empty():
			print_verbose("[GameSelection] Background image not found: %s" % bg_path)
			
		_background_texture.texture = null # またはデフォルト背景

func _launch_game():
	var game = _games[_selected_index]
	print("[GameSelection] Launching Game: %s" % game.title)
	
	# 実行ファイルのパス解決
	var exec_path = _resolve_path(game.executable_path, game.game_id)
	
	if exec_path.is_empty():
		push_warning("[GameSelection] ⚠️ No executable path set for '%s' (ID: %s)" % [game.title, game.game_id])
		return

	if not FileAccess.file_exists(exec_path):
		push_warning("[GameSelection] ⚠️ Executable NOT found for '%s' (ID: %s)\n  - Path: '%s'" % 
			[game.title, game.game_id, exec_path])
		return

	print("[GameSelection] Executing: %s" % exec_path)
	
	# 外部プロセスとして実行
	# 第2引数は引数リスト（必要ならDBに追加検討だが現状は空で）
	var pid = OS.create_process(exec_path, [])
	
	if pid != -1:
		print("[GameSelection] Process started with PID: %d" % pid)
		_running_pid = pid
		_game_window_activated = false
		_running_overlay.visible = true
		_status_label.text = "起動中..."
		# 起動成功時はここでreturn（復帰は_processで行う）
		return

	# 以下、起動失敗時の処理
	push_error("[GameSelection] Failed to create process for: %s" % exec_path)
	print("[GameSelection] Launch Failed. Path: %s" % exec_path)
	ErrorManager.show_error(ErrorCode.GAME_EXECUTION_FAILED)
	# オーバーレイを隠す（失敗時）
	_running_overlay.visible = false
	_running_pid = -1

func _transition_to_screensaver():
	get_tree().change_scene_to_file("res://scenes/screensaver.tscn")

# --- アニメーション処理 ---
func _process(delta):
	# ゲーム実行中の監視ロジック
	if _running_pid != -1:
		if OS.is_process_running(_running_pid):
			# ゲームが実行中
			if not get_window().has_focus():
				# ランチャーがフォーカスを失った＝ゲームが前面に来た
				_game_window_activated = true
				_status_label.text = "ゲーム実行中..."
			else:
				# ランチャーがフォーカスを持っている
				if _game_window_activated:
					# 一度アクティブになったのに戻ってきた（Alt-Tab等で裏に行った）
					_status_label.text = "ゲーム実行中\n(ウィンドウが裏に隠れています)"
				else:
					# まだ一度もアクティブになっていない（起動ロード中）
					_status_label.text = "起動中..."
		else:
			# ゲームが終了した
			print("[GameSelection] Process %d finished. Returning to launcher." % _running_pid)
			_running_pid = -1
			_running_overlay.visible = false
			# 必要であればここでランチャーを全面に出す処理（grab_focusなど）を入れる
			get_window().grab_focus()
			
		# ゲーム実行中はカルーセルのアニメーション等はスキップ
		return

	if _games.is_empty(): return
	
	# インデックス単位でスムーズに移動
	_current_scroll_index = lerpf(_current_scroll_index, float(_selected_index), SCROLL_SPEED * delta)
	
	var viewport_center_y = get_viewport_rect().size.y / 2
	var container_center_x = _carousel_container.size.x / 2
	
	# 各カードの座標とスケールを更新
	for i in range(_card_nodes.size()):
		var card = _card_nodes[i]
		
		# 中心（現在選択されているインデックス）からの距離（インデックス単位）
		# 例: selected=2.5, i=2 なら diff=-0.5 (少し上)
		var diff = float(i) - _current_scroll_index
		
		# Y座標計算
		# 1. 基本的な等間隔配置 (狭い間隔)
		var base_y_offset = diff * (CARD_SIZE.y + GAP_NARROW)
		
		# 2. 中心付近だけ間隔を広げる「押し出し」処理
		# diffが 0 から ±1 になるまでの間に、GAP_WIDE 分だけ余分に移動させる
		var push_amount = remap(abs(diff), 0.0, 1.0, 0.0, GAP_WIDE)
		push_amount = clamp(push_amount, 0.0, GAP_WIDE)
		var push_offset = sign(diff) * push_amount
		
		var screen_y = viewport_center_y + base_y_offset + push_offset
		
		# 画面中央からの絶対距離（スケーリング用）
		var dist_from_center_px = abs(screen_y - viewport_center_y)
		
		# スケーリング計算
		# 150px以上離れたら最小サイズ
		var scale_factor = remap(dist_from_center_px, 0, 150, SCALE_ACTIVE, SCALE_INACTIVE)
		scale_factor = clamp(scale_factor, SCALE_INACTIVE, SCALE_ACTIVE)
		
		# 透明度
		var opacity = remap(dist_from_center_px, 0, 300, 1.0, OPACITY_INACTIVE)
		opacity = clamp(opacity, 0.0, 1.0)
		
		# 反映
		card.position = Vector2(container_center_x - (CARD_SIZE.x / 2), screen_y - (CARD_SIZE.y / 2))
		card.scale = Vector2(scale_factor, scale_factor)
		card.modulate.a = opacity
		
		# Z-Index制御
		card.z_index = 100 - int(dist_from_center_px / 10)
