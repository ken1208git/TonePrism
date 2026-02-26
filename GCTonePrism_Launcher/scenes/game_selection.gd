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
const CORNER_RADIUS := 20.0              # カードの角丸半径
const FOCUS_MARGIN := 8.0                # フォーカス枠のマージン

# キーリピート（長押し）設定
const KEY_REPEAT_DELAY := 0.4            # 初回リピートまでの待機時間
const KEY_REPEAT_RATE := 0.06            # リピート間隔（秒） "ドウルルル"用

const IDLE_WARNING_TIME := 60.0          # 警告を表示するまでの無操作時間（秒）
const IDLE_RESET_TIME := 90.0            # リセットするまでの無操作時間（秒） = 警告(60) + ダイアログ(30)
# フォントリソース
var _font_regular: FontFile = preload("res://fonts/NotoSansJP-Regular.ttf")
var _font_bold: FontFile = preload("res://fonts/NotoSansJP-Bold.ttf")



# --- ノード参照 ---
# --- ノード参照 ---
@onready var _carousel_container: Control = $CarouselContainer
@onready var _card_template: Panel = $CarouselContainer/CardTemplate
@onready var _static_focus_border: Panel = $CarouselContainer/StaticFocusBorder
@onready var _info_panel: Panel = $InfoPanel

# _static_focus_border removed (now @onready)

@onready var _title_label: Label = $InfoPanel/MarginContainer/VBoxContainer/TitleContainer/TitleScrollWrapper/TitleLabel
@onready var _creator_label: Label = $InfoPanel/MarginContainer/VBoxContainer/CreatorScrollWrapper/CreatorLabel

# Specs UI Components
@onready var _players_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayersContainer/PlayersValueLabel
@onready var _difficulty_bar: ProgressBar = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/DifficultyContainer/DifficultyBar
@onready var _difficulty_val_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/DifficultyContainer/DifficultyValueLabel
@onready var _playtime_bar: ProgressBar = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayTimeContainer/PlayTimeBar
@onready var _playtime_val_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/PlayTimeContainer/PlayTimeValueLabel
@onready var _controller_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/ControllerContainer/ControllerValueLabel
@onready var _online_label: Label = $InfoPanel/MarginContainer/VBoxContainer/SpecsContainer/OnlineContainer/OnlineValueLabel

@onready var _desc_label: Label = $InfoPanel/MarginContainer/VBoxContainer/DescriptionScroll/DescLabel
@onready var _running_overlay: Control = $RunningOverlay
@onready var _running_icon_container: Panel = $RunningOverlay/RunningIconContainer
@onready var _running_icon: TextureRect = $RunningOverlay/RunningIconContainer/Icon
@onready var _status_label: Label = $RunningOverlay/StatusContainer/StatusLabel

# New UI components from TSCN
@onready var _play_button: Button = $InfoPanel/MarginContainer/VBoxContainer/TitleContainer/PlayButton
@onready var _top_bar: Control = $TopBar
@onready var _bottom_bar: Control = $BottomBar
@onready var _clock_label: Label = $TopBar/MarginContainer/HBoxContainer/ClockLabel
@onready var _exit_button: Button = $TopBar/MarginContainer/HBoxContainer/ExitButton
@onready var _bottom_label: Label = $BottomBar/LabelMargin/GuideLabel
@onready var _background_texture: TextureRect = $BackgroundLayer/BackgroundTexture
@onready var _background_old: TextureRect = $BackgroundLayer/BackgroundTextureOld

var _bg_tween: Tween = null

var _card_nodes: Array[Panel] = []
# _play_button removed (now @onready)
var _target_scroll_index: float = 0.0
var _db_manager: DatabaseManager
var _games: Array[GameInfo] = []
var _selected_index: int = 0
var _active_index: int = 0   # 現在画面中央にあり、フォーカスを持つべきインデックス
var _has_lost_focus_since_launch: bool = false # 起動後にフォーカスを失ったかどうか（終了検知用）
var _glow_styles: Array[StyleBoxFlat] = [] # ブリージング（明滅）させるスタイルリスト
var _glow_timer: float = 0.0
var _current_scroll_index: float = 0.0 # インデックス単位の現在位置

# 入力制御用 (Drum Roll)
var _input_hold_timer: float = 0.0
var _last_input_dir: int = 0
var _using_mouse: bool = false # マウス操作中かどうか

# ... (rest of variables)

# ...

# --- Helper logic changes ---

func _move_selection(dir: int):
	# dir: +1 (下へスクロール -> 視覚的にはゲームリストが上へ移動し、選択枠が下へ)
	# dir: -1 (上へスクロール -> 視覚的にはゲームリストが下へ移動し、選択枠が上へ)
	var new_index = clampi(_selected_index + dir, 0, _games.size() - 1)
	
	if new_index != _selected_index:
		_selected_index = new_index
		
		# どのアニメーションを流すかを決める
		# 下のゲーム(インデックス大)に移動 -> リストは上へスライド、新しい背景は下から上へスライド(transition_up)
		# 上のゲーム(インデックス小)に移動 -> リストは下へスライド、新しい背景は上から下へスライド(transition_down)
		var dir_y = -1 if dir > 0 else 1 # dir > 0 は「下へ(index増)」なので背景は「下から上へ(Yはマイナス方向へ移動)」
		
		# 背景と情報は即時更新
		_update_info_display(_selected_index, dir_y)

func _update_focus_to_current_card():
	# _active_index に基づいてフォーカス
	if _card_nodes.size() > _active_index:
		var card = _card_nodes[_active_index]
		if not card.has_focus():
			card.grab_focus()

func _update_info_display(index: int, slide_dir_y: int = 0):
	# 指定インデックス（基本は_selected_index）の情報で画面を更新
	var game = _games[index]
	
	# 情報表示更新
	
	var title_text = game.title
	if title_text == null or str(title_text) == "null" or str(title_text) == "<null>":
		title_text = ""
		
	var desc_text = game.description
	if desc_text == null or str(desc_text) == "null" or str(desc_text) == "<null>" or desc_text.strip_edges().is_empty():
		desc_text = "このゲームには説明文がありません。"

	# --- 詳細情報作成 ---
	# 1. 製作者情報
	var creator_text = ""
	var seen_devs = [] # 重複チェック用
	for dev in game.developers:
		var name = "%s %s" % [dev.last_name, dev.first_name]
		# gradeはStringで来る可能性があるためint変換
		var grade_val = int(dev.grade) if dev.grade != null else -1
		var grade_str = _get_grade_string(grade_val)
		
		# 重複チェック（名前と学年が同じならスキップ）
		var unique_key = name + grade_str
		if unique_key in seen_devs:
			continue
		seen_devs.append(unique_key)
		
		creator_text += "%s %s　" % [name, grade_str]
	
	if game.release_year > 0:
		creator_text += "%d年　" % game.release_year
		
	if not game.genre.is_empty():
		creator_text += "ジャンル: " + ", ".join(game.genre)
		
	# 2. スペック情報
	# プレイ人数
	var min_p = game.min_players
	var max_p = game.max_players
	var players_text = ""
	if min_p > 0 and max_p > 0:
		if min_p == max_p:
			players_text = "%d人" % min_p
		else:
			players_text = "%d～%d人" % [min_p, max_p]
	elif max_p > 0:
		players_text = "%d人" % max_p
	else:
		players_text = "不明"
	_players_label.text = players_text

	# 難易度
	var diff_val = game.difficulty
	if diff_val > 0:
		_difficulty_bar.value = diff_val
		_difficulty_val_label.text = _get_difficulty_text_only(diff_val)
		_update_bar_style(_difficulty_bar, diff_val)
		_difficulty_bar.get_parent().show()
	else:
		_difficulty_bar.get_parent().hide()
		
	# プレイ時間
	var time_val = game.play_time
	if time_val > 0:
		_playtime_bar.value = time_val
		_playtime_val_label.text = _get_play_time_text_only(time_val)
		_update_bar_style(_playtime_bar, time_val)
		_playtime_bar.get_parent().show()
	else:
		_playtime_bar.get_parent().hide()
		
	# コントローラー
	var controller_text = "対応" if game.controller_support else "非対応"
	_controller_label.text = controller_text
	
	# 通信対戦
	# supported_connection: 0=なし, 1=ローカル, 2=オンライン
	var online_text = "対応" if game.supported_connection > 0 else "非対応"
	_online_label.text = online_text

	_title_label.text = title_text
	if _title_label.get_parent().has_method("reset"):
		_title_label.get_parent().reset()
		
	if _creator_label: 
		_creator_label.text = creator_text
		if _creator_label.get_parent().has_method("reset"):
			_creator_label.get_parent().reset()

	_desc_label.text = desc_text
	
	# 背景画像更新
	
	# もし前のTweenが実行中ならキルする
	if _bg_tween and _bg_tween.is_valid():
		_bg_tween.kill()

	# 現在の背景を古いテクスチャに移す（ただし、すでにフェードアウト中のものはそのままにしたいが、TextureRectが2つしかないので上書きする）
	# 見た目をスムーズにするため、古いテクスチャの現在の不透明度や位置を維持しつつ、画像だけ差し替える
	if _background_texture.texture != null:
		_background_old.texture = _background_texture.texture
		# 現在のアニメーション途中の状態を引き継ぐ
		_background_old.modulate = _background_texture.modulate
		_background_old.position = _background_texture.position
	else:
		_background_old.texture = null
		_background_old.modulate = Color(1, 1, 1, 0)
		
	var bg_path = _resolve_path(game.background_path, game.game_id)
	if not bg_path.is_empty() and FileAccess.file_exists(bg_path):
		var img = Image.load_from_file(bg_path)
		var tex = ImageTexture.create_from_image(img)
		_background_texture.texture = tex
	else:
		# 背景が見つからない場合はデフォルト（または無し）
		_background_texture.texture = null
		if not game.background_path.is_empty():
			push_warning("[GameSelection] Background not found: %s" % bg_path)

	# アニメーション再生（Tweenによる動的アニメーション）
	_bg_tween = create_tween()
	_bg_tween.set_parallel(true)
	
	if slide_dir_y == 0:
		# 初回表示や方向指定なしの場合は単なるフェードイン
		_background_texture.position = Vector2.ZERO
		_background_texture.modulate = Color(1, 1, 1, 0)
		_bg_tween.tween_property(_background_texture, "modulate", Color(1, 1, 1, 1), 0.3)
	else:
		# 移動方向の決定 (slide_dir_y: -1なら新しい絵は下から上へ, 1なら上から下へ)
		# 画面の縦幅より少し小さいくらいのオフセット50px
		var offset_y = 50.0 * -slide_dir_y 
		
		# 新しいテクスチャの初期状態設定（古い状態から引き継がない、常に一定のオフセットから出現させる）
		_background_texture.position = Vector2(0, offset_y)
		_background_texture.modulate = Color(1, 1, 1, 0)
		
		# トゥイーンアニメーション
		# 新しい背景：フェードインしつつ定位置(0,0)へ
		_bg_tween.tween_property(_background_texture, "position", Vector2.ZERO, 0.3).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
		_bg_tween.tween_property(_background_texture, "modulate", Color(1, 1, 1, 1), 0.3)
		
		# 古い背景：現在の位置からさらに同じ方向へ押し出されつつフェードアウト
		# slide_dir_y は新しい絵の進行方向（例：-1 は上へ向かう）。古い絵も同じ方向へ向かうので -1 * 50 = -50
		var old_target_y = _background_old.position.y + (50 * slide_dir_y)
		_bg_tween.tween_property(_background_old, "position", Vector2(0, old_target_y), 0.3).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
		_bg_tween.tween_property(_background_old, "modulate", Color(1, 1, 1, 0), 0.3)

# --- フォーマットヘルパー ---
func _get_current_fiscal_year() -> int:
	var date = Time.get_date_dict_from_system()
	# 4月始まり
	if date.month >= 4:
		return date.year
	else:
		return date.year - 1

func _get_grade_string(grade: int) -> String:
	if grade == 0:
		return "(教員)"
	
	var fy = _get_current_fiscal_year()
	# 基準: 1975年 (ユーザー指定の計算式: (CurrentFY - 1975) - Grade = 学年)
	# 例: 2025年度 - 1975 = 50. 49期生 -> 50 - 49 = 1年生
	var base_year = 1975
	var school_year = (fy - base_year) - grade
	
	if school_year >= 1 and school_year <= 3:
		return "(%d年生)" % school_year
	elif school_year > 3:
		return "(卒業生: %d期生)" % grade
	else:
		# まだ入学していない、あるいは計算外
		return "(%d期生)" % grade

func _get_difficulty_text_only(level: int) -> String:
	match level:
		1: return "簡単"
		2: return "普通"
		3: return "難しい"
		_: return "---"

func _get_play_time_text_only(level: int) -> String:
	match level:
		1: return "～5分"
		2: return "5分～15分"
		3: return "15分～"
		_: return "---"

func _update_bar_style(bar: ProgressBar, value: int):
	# スタイルボックスを動的に生成して色を変更
	var style = StyleBoxFlat.new()
	style.set_corner_radius_all(4)
	
	if value <= 1:
		style.bg_color = Color(0.2, 0.8, 0.2, 1.0) # 緑
	elif value == 2:
		style.bg_color = Color(0.9, 0.9, 0.2, 1.0) # 黄色
	else:
		style.bg_color = Color(0.9, 0.2, 0.2, 1.0) # 赤
		
	bar.add_theme_stylebox_override("fill", style)
	
	# 背景（空の部分）も少し暗くする
	var bg = StyleBoxFlat.new()
	bg.set_corner_radius_all(4)
	bg.bg_color = Color(0.2, 0.2, 0.2, 1.0)
	bar.add_theme_stylebox_override("background", bg)

func _get_connection_string(type: int) -> String:
	match type:
		1: return "教室内"
		2: return "オンライン"
		_: return "非対応"

func _update_focus_state():
	# _active_index に基づいてフォーカス移動を行う
	# フォーカス移動
	var focus_owner = get_viewport().gui_get_focus_owner()
	if focus_owner and (focus_owner is Panel and focus_owner in _card_nodes):
		_update_focus_to_current_card()

func _launch_game():
	if _games.is_empty(): return
	# 既に起動中の場合は無視
	if _running_pid != -1: return

	var game = _games[_active_index]
	print("[GameSelection] Launching game: ", game.title, " (ID: ", game.game_id, ")")
	
	var exe_path = ""
	
	# パスの解決
	# 1. games/{game_id}/{executable_path}
	var game_folder = PathManager.get_game_folder(game.game_id)
	var candidate1 = game_folder.path_join(game.executable_path)
	
	if FileAccess.file_exists(candidate1):
		exe_path = candidate1
	else:
		# 2. プロジェクトルート直下 (互換性/デバッグ)
		var candidate2 = PathManager.get_base_directory().path_join(game.executable_path)
		if FileAccess.file_exists(candidate2):
			exe_path = candidate2
	
	if exe_path.is_empty():
		print("❌ Executable not found: ", game.executable_path)
		DialogManager.show_message("起動エラー", "実行ファイルが見つかりませんでした。\n%s" % game.executable_path)
		return

	# 引数の処理
	var args = []
	if not game.arguments.is_empty() and game.arguments != "<null>":
		# 簡易的なスペース区切り（引用符などは考慮していないため注意）
		# "<null>" という文字列が含まれている場合は除外
		var raw_args = game.arguments.split(" ", false)
		for arg in raw_args:
			if arg != "<null>" and arg != "null":
				args.append(arg)
	
	# 起動中表示（プロセス生成前）
	# 即座にUIを隠す
	if _running_overlay:
		_running_overlay.visible = true
	
	_switch_to_running_view()

	if _status_label:
		_status_label.text = "ゲーム起動中: %s\nお楽しみください！" % game.title
		
	# UIの描画更新を待つ
	await get_tree().process_frame
	await get_tree().process_frame
	
	# 起動待ち（1秒）
	await get_tree().create_timer(1.0).timeout

	print("  Path: ", exe_path)
	print("  Args: ", args)
	
	var working_dir = exe_path.get_base_dir()
	print("[GameSelection] Working Directory: %s" % working_dir)
	
	# プロセス起動 (cmd経由で作業ディレクトリを設定)
	# cmd /c "cd /d WORK_DIR && EXE_PATH ARGS"
	var cmd_command = 'cd /d "%s" && "%s"' % [working_dir, exe_path]
	if not args.is_empty():
		cmd_command += " " + " ".join(args)
	
	print("[GameSelection] CMD Command: ", cmd_command)
	
	# _has_lost_focus_since_launch をリセット
	_has_lost_focus_since_launch = false
	
	var pid = OS.create_process("cmd.exe", ["/C", cmd_command])
	
	if pid == -1:
		print("❌ Failed to create process.")
		DialogManager.show_message("起動エラー", "ゲームの起動に失敗しました。")
		# エラー時は表示を消す
		if _running_overlay: _running_overlay.visible = false
		# UIを戻す
		if _carousel_container: _carousel_container.visible = true
		if _info_panel: _info_panel.visible = true
		if _top_bar: _top_bar.visible = true
		if _static_focus_border: _static_focus_border.visible = true
		for card in _card_nodes: card.visible = true
	else:
		print("✅ Process started. PID: %d" % pid)
		_running_pid = pid
		
		# 文言更新は _process でフォーカスが外れたときに行う

# ...

func _process(delta):
	# --- 時計更新 ---
	_update_clock()
	
	# --- グローアニメーション（ブリージング） ---
	# 周期2秒程度 (0.003 * msec ≒ rate) -> 単純に delta で計算
	_glow_timer += delta
	# sin波: 0.2 ~ 0.8 の範囲で変動させる
	# sin(-1~1) -> +1(0~2) -> /2(0~1) -> *0.6(0~0.6) -> +0.2(0.2~0.8)
	var glow_alpha = 0.5 + 0.3 * sin(_glow_timer * 3.0) # 3.0 rad/sec ≒ 2秒周期
	
	for style in _glow_styles:
		if style:
			var c = Color(1, 1, 1, glow_alpha)
			style.shadow_color = c
			style.border_color = c

	# グローアニメーション（ブリージング）
	# ... (略)

	# ゲーム実行中の監視ロジック
	# ゲーム実行中はここが先に処理され、終了したら _running_pid = -1 になる
	if _running_pid != -1:
		
		# フォーカス状態の監視
		if not get_window().has_focus():
			# 一度でもフォーカスを失ったらフラグを立てる
			_has_lost_focus_since_launch = true
			
			if _status_label and "起動中" in _status_label.text:
				var game = _games[_selected_index]
				_status_label.text = "ゲーム実行中: %s\nお楽しみください！" % game.title
		
		# 終了判定:
		# 1. プロセスが死んだ (cmdが終了した)
		# 2. 起動後にフォーカスを失い、かつフォーカスが戻ってきた (ゲーム終了 or Alt-Tab)
		var process_dead = not OS.is_process_running(_running_pid)
		var focus_returned = _has_lost_focus_since_launch and get_window().has_focus()
		
		if process_dead or focus_returned:
			if focus_returned:
				print("[GameSelection] Focus returned. Assuming game finished.")
			else:
				print("[GameSelection] Game process %d finished." % _running_pid)
			_running_pid = -1
			# ゲーム終了時の処理（必要ならウィンドウをアクティブにするなど）
			# Grab focus back to the selection
			_update_focus_to_current_card()
			
			# 起動中表示を消す
			if _running_overlay:
				_running_overlay.visible = false
			
			# UI再表示
			# if _carousel_container: _carousel_container.visible = true
			
			# 全てのカードを表示状態に戻す
			for card in _card_nodes:
				card.visible = true
				
			if _info_panel: _info_panel.visible = true
			if _top_bar: _top_bar.visible = true
			if _static_focus_border: _static_focus_border.visible = true
	
	# --- アイドルタイマー更新 ---
	# ゲーム実行中はカウントしない
	if _running_pid > 0:
		_idle_timer = 0.0
		return

	# ダイアログ表示中（ポーズ中）はカウントしない
	# ただし、タイムアウト警告ダイアログ表示中はカウントを継続する（リセットへ移行するため）
	if not get_tree().paused or _timeout_dialog != null:
		_idle_timer += delta
	
	if _idle_timer >= IDLE_RESET_TIME:
		print("[GameSelection] Idle timeout. Transitioning to screensaver.")
		_transition_to_screensaver()
		
	elif _idle_timer >= IDLE_WARNING_TIME:
		var remaining = int(ceil(IDLE_RESET_TIME - _idle_timer))
		var msg = "長時間操作がなかったため、タイトル画面に戻ります。\n\nあと %d 秒\n\n続ける場合は、何かボタンを押してください。" % remaining
		
		if _timeout_dialog == null:
			# ダイアログをまだ出していない場合
			# キャンセルボタンのみ表示（放置で戻る、操作でキャンセル）
			_timeout_dialog = DialogManager.show_message("確認", msg, 
				["キャンセル"], 
				func(_idx): 
					# キャンセルボタンが押された場合（インデックス0）
					_reset_idle_timer()
			)
		else:
			# ダイアログ表示中はメッセージを更新（カウントダウン）
			if _timeout_dialog.has_method("set_message"):
				_timeout_dialog.set_message(msg)

	# ポーズ中（ダイアログ表示中など）はここから下のアニメーションを行わない
	# ただし、DialogManagerでpauseがかかるとここも止まってしまうため、
	# DialogManager側で process_mode = ALWAYS になっているか、
	# もしくはこのスクリプト自体が pause_mode = process になっている必要がある。
	# 現在、DialogManagerは ALWAYS だが、GameSelection自体は PAUSABLE (デフォルト) なので、
	# show_message で pause = true にされると _process が止まってしまう！
	# -> DialogManagerを使っている間はここが動かない。
	
	# これを解決するには、DialogManagerが表示している間は、DialogManager側でカウントダウンするか、
	# GameSelectionを PROCESS_MODE_ALWAYS にして、ここでのアニメーション更新を自前で pause チェックする必要がある。
	# 先ほど process_mode = ALWAYS を消してしまったが、アイドル監視のためには必要だった可能性がある。
	# しかし、ALWAYSにするとポーズ中のアニメーション停止を手動でやる必要がある。
	
	# とりあえず、DialogManagerの仕様上 pause されるので、
	# 「DialogManagerが表示されている間も _process を動かす」ために、
	# process_mode を再度 ALWAYS に設定し、アニメーション部分だけ if get_tree().paused: return でガードする形にする。
	# シーン遷移中などで get_tree() が無効な場合のガード
	if not is_inside_tree() or get_tree() == null:
		return
		
	if get_tree().paused:
		# ダイアログ表示中など
		return

	# --- Drum Roll Input (高速スクロール) ---
	# --- Drum Roll Input (高速スクロール) ---
	var input_allowed = true
	# プレイボタン、または終了ボタンにフォーカスがある、または実行中は操作不可
	if (_play_button and _play_button.has_focus()) or \
	   (_exit_button and _exit_button.has_focus()) or \
	   _running_pid != -1:
		input_allowed = false
	
	if input_allowed:
		# 上下入力の取得 (Up=-1, Down=1)
		var dir = 0
		if Input.is_action_pressed("ui_down"): dir += 1
		if Input.is_action_pressed("ui_up"):   dir -= 1
		
		# マウスホイール等は _input で処理されるのでここではキー/ボタンのみ
		
		if dir != 0:
			if _last_input_dir != dir:
				# 初回押し
				_move_selection(dir)
				_update_focus_to_current_card()
				_input_hold_timer = KEY_REPEAT_DELAY
				_last_input_dir = dir
			else:
				# 押しっぱなし
				_input_hold_timer -= delta
				if _input_hold_timer <= 0:
					_move_selection(dir)
					# 高速移動中なのでフォーカス移動は_process後半の自動追従に任せてもいいが、
					# 音を鳴らすタイミング等も考慮してここでは明示的に呼ばない（_active_indexが変われば勝手に処理される）
					# ただし _move_selection はインデックスを変えるだけ。
					
					_input_hold_timer = KEY_REPEAT_RATE
		else:
			_last_input_dir = 0
			_input_hold_timer = 0.0


			
	if _games.is_empty(): return
	
	# インデックス単位でスムーズに移動
	var target = float(_selected_index)
	_current_scroll_index = lerpf(_current_scroll_index, target, SCROLL_SPEED * delta)
	
	# Leashロジック削除: 高速スクロール時に「動きがリセットされる（スナップする）」違和感を防ぐため、
	# 目標位置が遠くても lerp で自然に追従させる。
	# var leash_diff = _current_scroll_index - target
	# if abs(leash_diff) > 1.0:
	# 	_current_scroll_index = target + sign(leash_diff) * 1.0
	
	# --- Active Index Update Integration ---
	var new_active = int(round(_current_scroll_index))
	new_active = clampi(new_active, 0, _games.size() - 1)
	
	if new_active != _active_index:
		_active_index = new_active
		_update_focus_state() # フォーカス移動のみ行う
		
		# 音を鳴らすならここ
	
	var viewport_center_y = get_viewport_rect().size.y / 2
	var container_center_x = _carousel_container.size.x / 2
	
	# --- StaticFocusBorderの制御 ---
	var static_border = _carousel_container.get_node_or_null("StaticFocusBorder")
	if static_border:
		# プレイボタンにフォーカスがある時は隠す
		# また、マウス操作中も隠す
		var focus_owner = get_viewport().gui_get_focus_owner()
		var processing_focus = (focus_owner == null) or (focus_owner is Panel and focus_owner in _card_nodes)
		
		# フォーカスが有効かつ、マウス操作中でない場合に表示
		static_border.visible = processing_focus and not _using_mouse
		
		# 位置合わせ: 画面中央（diff=0）のカード位置に合わせる
		# Active時のサイズ
		var active_size = CARD_SIZE * SCALE_ACTIVE
		# カードの基準位置（中央）
		# Y座標: center + 0 (diff=0なのでoffsetなし)
		var center_y = viewport_center_y
		
		# StaticBorderはTopLeft基準なので補正
		# container_center_x はコンテナローカルのX中心
		static_border.position = Vector2(
			container_center_x - (active_size.x / 2),
			center_y - (active_size.y / 2)
		)
		static_border.size = active_size # 念のためサイズも維持
	
	# 各カードの座標とスケールを更新
	for i in range(_card_nodes.size()):
		var card = _card_nodes[i]
		
		# FocusBorder更新処理は削除（StaticFocusBorderに変更したため）
		
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
		# 選択ターゲット（_selected_index）と一致する場合のみ不透明、それ以外は半透明
		# これにより入力した瞬間に切り替わる（ラグなし）
		var opacity = OPACITY_INACTIVE
		if i == _selected_index:
			opacity = 1.0
		
		# 反映
		card.position = Vector2(container_center_x - (CARD_SIZE.x / 2), screen_y - (CARD_SIZE.y / 2))
		card.scale = Vector2(scale_factor, scale_factor)
		card.modulate.a = opacity
		
		# Z-Index制御
		card.z_index = 100 - int(dist_from_center_px / 10)
var _running_pid: int = -1 # 実行中のゲームPID (-1: 実行なし)
var _idle_timer: float = 0.0 # 無操作経過時間
var _timeout_dialog: CommonDialog = null # 表示中のタイムアウト警告ダイアログ



func _ready():
	# UI構築はtscnで行われるため不要
	# 既に_load_games_from_db内でエラー処理済みだが、
	# ゲームリストが空の場合はUI更新を行わない（エラーダイアログが表示されているはず）
	if not _load_games_from_db() or _games.is_empty():
		return
		
	# ダイアログ表示中（Pause中）もアイドルタイマーを動かすため常にプロセスを実行
	process_mode = Node.PROCESS_MODE_ALWAYS
	
	# _create_dummy_data呼び出しは削除済み
	# if _games.is_empty():
	# 	_create_dummy_data()
	
	_create_carousel_cards()

	
	# StaticFocusBorderのスタイルを取得してGlowリストに追加
	if _static_focus_border:
		var style = _static_focus_border.get_theme_stylebox("panel")
		if style is StyleBoxFlat:
			_glow_styles.append(style)
	
	# _create_play_button() # Now in TSCN
	# _create_top_bar() # Now in TSCN
	# _create_bottom_bar() # Now in TSCN
	
	# Initial Setup for Static Focus Border (glow animation handling)
	if _static_focus_border:
		var style = _static_focus_border.get_theme_stylebox("panel")
		if style:
			_glow_styles.append(style)

	# Initial Setup for Exit Button (Icon & Styles)
	if _exit_button:
		var icon_tex = load("res://images/exit.jpg")
		if icon_tex:
			_exit_button.icon = icon_tex
			
		# Apply Exit Button Styles (Complex styles kept in code or move to Theme resource ideally)
		_setup_exit_button_styles()
		
		# Signals
		_exit_button.pressed.connect(_on_exit_button_pressed)
		_exit_button.mouse_entered.connect(func(): _exit_button.grab_focus())

	# Initial Setup for Play Button (Styles)
	if _play_button:
		_setup_play_button_styles()
		_play_button.pressed.connect(func(): _launch_game())

	# InfoPanelの背景を角丸にする
	var info_style = StyleBoxFlat.new()
	info_style.bg_color = Color(0, 0, 0, 0.7) # 半透明黒
	info_style.set_corner_radius_all(20)
	
	var info_panel = $InfoPanel
	if info_panel:
		info_panel.add_theme_stylebox_override("panel", info_style)
		# 操作説明ラベル（GuideLabel）を非表示にする（ボトムバーに移動したため）
		var guide_label = info_panel.get_node_or_null("MarginContainer/VBoxContainer/GuideLabel")
		if guide_label:
			guide_label.visible = false
	
	# 初期位置セット
	_current_scroll_index = float(_selected_index)
	_update_info_display(_selected_index) # 初期状態ではアニメーション不要
	# フォーカス状態の初期化はフレーム確定後に行う（トラップ設定のため）
	call_deferred("_update_focus_state") # Changed to deferred to ensure nodes are ready
	
	# 相互参照の更新 (PlayButton <-> ExitButton)
	if _play_button and _exit_button:
		_exit_button.focus_neighbor_bottom = _play_button.get_path()
		_play_button.focus_neighbor_top = _exit_button.get_path()
		# PlayButton navigation
		_play_button.focus_neighbor_left = _play_button.get_path() # Loop/Trap
		_play_button.focus_neighbor_right = _play_button.get_path()

	set_process(true)
	set_process_input(true)

# --- Style Setup Helpers ---
func _setup_exit_button_styles():
	if not _exit_button: return
	
	# 白ボタンに変更
	var style_normal = StyleBoxFlat.new()
	style_normal.bg_color = Color.WHITE # 白
	style_normal.set_corner_radius_all(12) # 角丸
	# 影をつける（全方向均等）
	style_normal.shadow_color = Color(0, 0, 0, 0.3)
	style_normal.shadow_size = 4
	style_normal.shadow_offset = Vector2(0, 0)
	# アイコンを大きくするためにマージンを減らす
	style_normal.content_margin_left = 4
	style_normal.content_margin_right = 4
	style_normal.content_margin_top = 4
	style_normal.content_margin_bottom = 4
	_exit_button.add_theme_stylebox_override("normal", style_normal)
	
	# ホバー時は背景を少し暗くするだけ
	var style_hover = style_normal.duplicate()
	style_hover.bg_color = Color(0.9, 0.9, 0.9, 1.0)
	_exit_button.add_theme_stylebox_override("hover", style_hover)

	# 押下時はさらに暗く
	var style_pressed = style_normal.duplicate()
	style_pressed.bg_color = Color(0.7, 0.7, 0.7, 1.0)
	_exit_button.add_theme_stylebox_override("pressed", style_pressed)

	# フォーカス時は「白い輪っか」を表示
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_width_left = 0
	style_focus.border_width_top = 0
	style_focus.border_width_right = 0
	style_focus.border_width_bottom = 0
	style_focus.border_color = Color.WHITE
	# 枠を外側に広げる
	style_focus.expand_margin_left = 6
	style_focus.expand_margin_right = 6
	style_focus.expand_margin_top = 6
	style_focus.expand_margin_bottom = 6
	style_focus.set_corner_radius_all(18)
	
	# 光彩（グロー）効果を追加
	style_focus.shadow_color = Color(1, 1, 1, 0.5)
	style_focus.shadow_size = 12
	style_focus.shadow_offset = Vector2(0, 0)
	
	_glow_styles.append(style_focus) # アニメーション対象に追加
	
	_exit_button.add_theme_stylebox_override("focus", style_focus) 

	# フォーカス制御 (Self-loop)
	_exit_button.focus_neighbor_left = _exit_button.get_path()
	_exit_button.focus_neighbor_right = _exit_button.get_path()
	_exit_button.focus_neighbor_top = _exit_button.get_path()

func _setup_play_button_styles():
	if not _play_button: return
	
	# 緑色スタイル（不透明）
	var style_normal = StyleBoxFlat.new()
	style_normal.bg_color = Color(0.0, 0.6, 0.0, 1.0) # 緑
	style_normal.set_corner_radius_all(10)
	style_normal.content_margin_left = 20
	style_normal.content_margin_right = 20
	
	_play_button.add_theme_stylebox_override("normal", style_normal)
	
	var style_hover = style_normal.duplicate()
	style_hover.bg_color = Color(0.2, 0.8, 0.2, 1.0) # 明るい緑
	_play_button.add_theme_stylebox_override("hover", style_hover)
	
	# Focusは別スタイル（白枠）
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_width_left = 0
	style_focus.border_width_top = 0
	style_focus.border_width_right = 0
	style_focus.border_width_bottom = 0
	style_focus.border_color = Color.WHITE
	# 枠を外側に広げる
	var focus_margin = 8
	style_focus.expand_margin_left = focus_margin
	style_focus.expand_margin_right = focus_margin
	style_focus.expand_margin_top = focus_margin
	style_focus.expand_margin_bottom = focus_margin
	style_focus.set_corner_radius_all(10 + focus_margin)
	
	# 光彩（グロー）
	style_focus.shadow_color = Color(1, 1, 1, 0.5)
	style_focus.shadow_size = 8
	style_focus.shadow_offset = Vector2(0, 0)
	
	_glow_styles.append(style_focus)
	
	_play_button.add_theme_stylebox_override("focus", style_focus)

	var style_pressed = style_normal.duplicate()
	style_pressed.bg_color = Color(0.0, 0.4, 0.0, 1.0) # 暗い緑
	_play_button.add_theme_stylebox_override("pressed", style_pressed)

func _update_bottom_bar_visibility():
	if _bottom_bar:
		# マウス操作中は非表示、キー/パッド操作中は表示
		_bottom_bar.visible = not _using_mouse

func _on_exit_button_pressed():
	var callback = func(idx):
		if idx == 1: # 「退出する」
			_transition_to_screensaver()

	DialogManager.show_message("確認", "退出しますか？\nタイトル画面に戻ります。",
		["キャンセル", "退出する"],
		callback,
		[Color(0.3, 0.3, 0.3), Color(0.8, 0.2, 0.2)] # ボタン色: キャンセル=グレー, 退出する=赤
	)

func _update_clock():
	if _clock_label:
		var time = Time.get_time_dict_from_system()
		_clock_label.text = "%02d:%02d" % [time.hour, time.minute]

func _exit_tree():
	# シーンが破棄される際にダイアログも閉じる
	# リソース解放などはここで行うが、シーン遷移は行わない
	if DialogManager and DialogManager.has_method("close_current_dialog"):
		DialogManager.close_current_dialog()

# --- データ読み込み ---
func _load_games_from_db() -> bool:
	_db_manager = DatabaseManager.new()
	var db_path = PathManager.get_database_path()
	print("[GameSelection] データベース読み込み開始. ターゲットパス: ", db_path)
	
	# データベースを開く
	if not _db_manager.open():
		print("[GameSelection] ❌ データベースのオープンに失敗しました (E-1001)")
		ErrorManager.show_error(ErrorCode.DATABASE_NOT_FOUND)
		set_process_input(false) # 入力を無効化
		return false
	
	# ゲームリストを読み込む
	_games = _db_manager.get_all_games()
	_db_manager.close() # DBは読み込み後すぐに閉じる
	
	# ゲームが見つからない場合の処理
	if _games.is_empty():
		print("[GameSelection] ⚠️ データベースは開けましたが、表示対象のゲームが0件です (E-1006)")
		ErrorManager.show_error(ErrorCode.DATABASE_NO_GAMES_REGISTERED)
		set_process_input(false)
		return true 
		
	print("[GameSelection] ✅ DB読み込み完了: %d 件のゲームが見つかりました" % _games.size())
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
		card.focus_mode = Control.FOCUS_ALL # フォーカス可能にする
		
		_carousel_container.add_child(card)
		
		# トラップ設定: ツリーに追加してからパスを取得して設定
		card.focus_neighbor_top = card.get_path()
		card.focus_neighbor_bottom = card.get_path()
		card.focus_neighbor_left = card.get_path()
		card.focus_neighbor_right = card.get_path()
		
		_card_nodes.append(card)

		# --- 内部コンテンツの設定 ---
		var icon_rect = card.get_node("Clipper/Icon")
		var no_image_label = card.get_node("Clipper/NoImageLabel")

		# サムネイル読み込み
		var thumb_path = _resolve_path(game.thumbnail_path, game.game_id)
		
		var tex_to_set = null
		if not thumb_path.is_empty() and FileAccess.file_exists(thumb_path):
			var img = Image.load_from_file(thumb_path)
			var tex = ImageTexture.create_from_image(img)
			tex_to_set = tex
		else:
			# パスが設定されているのに見つからない場合は警告
			if not game.thumbnail_path.is_empty():
				push_warning("[GameSelection] ⚠️ Thumbnail NOT found for '%s' (ID: %s)\n  - DB Path: '%s'\n  - Check: '%s'" % 
					[game.title, game.game_id, game.thumbnail_path, thumb_path])
		
		if tex_to_set:
			if icon_rect:
				icon_rect.texture = tex_to_set
				icon_rect.visible = true
			if no_image_label:
				no_image_label.visible = false
		else:
			# 画像がない場合は "NO IMAGE" ラベルを表示
			if icon_rect:
				icon_rect.visible = false
			if no_image_label:
				no_image_label.visible = true


			
	# 初期情報の表示
	# ...

# ...

# ... (In _process)



func _create_play_button():
	_play_button = Button.new()
	_play_button.text = "プレイ"
	_play_button.custom_minimum_size = Vector2(120, 50)
	_play_button.add_theme_font_size_override("font_size", 24)
	_play_button.add_theme_font_override("font", _font_bold) # ボールドフォント適用
	
	# 緑色スタイル（不透明）
	var style_normal = StyleBoxFlat.new()
	style_normal.bg_color = Color(0.0, 0.6, 0.0, 1.0) # 緑
	style_normal.set_corner_radius_all(10)
	style_normal.content_margin_left = 20
	style_normal.content_margin_right = 20
	
	_play_button.add_theme_stylebox_override("normal", style_normal)
	
	var style_hover = style_normal.duplicate()
	style_hover.bg_color = Color(0.2, 0.8, 0.2, 1.0) # 明るい緑
	_play_button.add_theme_stylebox_override("hover", style_hover)
	# Focusは別スタイル（白枠）
	
	var style_focus = StyleBoxFlat.new()
	style_focus.bg_color = Color.TRANSPARENT
	style_focus.draw_center = false
	style_focus.border_width_left = 0
	style_focus.border_width_top = 0
	style_focus.border_width_right = 0
	style_focus.border_width_bottom = 0
	style_focus.border_color = Color.WHITE
	
	# 光彩（グロー）効果を追加
	style_focus.shadow_color = Color(1, 1, 1, 0.5)
	style_focus.shadow_size = 12
	style_focus.shadow_offset = Vector2(0, 0)
	
	_glow_styles.append(style_focus) # アニメーション対象に追加
	
	# 少し外側に広げる
	var focus_margin = 8
	style_focus.expand_margin_left = focus_margin
	style_focus.expand_margin_right = focus_margin
	style_focus.expand_margin_top = focus_margin
	style_focus.expand_margin_bottom = focus_margin
	style_focus.set_corner_radius_all(10 + focus_margin)
	
	_play_button.add_theme_stylebox_override("focus", style_focus)
	
	var style_pressed = style_normal.duplicate()
	style_pressed.bg_color = Color(0.0, 0.4, 0.0, 1.0) # 暗い緑
	_play_button.add_theme_stylebox_override("pressed", style_pressed)
	
	# InfoPanel内のVBoxContainerにあるTitleLabelの横に配置するため、HBoxContainerを作成して差し替える
	var parent_vbox = _title_label.get_parent()
	if parent_vbox:
		var hbox = HBoxContainer.new()
		hbox.name = "TitleButtonContainer"
		hbox.add_theme_constant_override("separation", 20) # タイトルとボタンの間隔
		
		# 位置を保持して入れ替え
		var idx = _title_label.get_index()
		parent_vbox.remove_child(_title_label)
		parent_vbox.add_child(hbox)
		parent_vbox.move_child(hbox, idx)
		
		# TitleLabelをHBoxに追加
		hbox.add_child(_title_label)
		# タイトルが余白を埋めるように設定（ボタンを右に寄せる場合）
		# _title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		# 単に横に並べるだけならデフォルトでOKだが、長いタイトルでボタンが潰れないように注意
		_title_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		
		# ボタンを追加
		hbox.add_child(_play_button)
	
	_play_button.pressed.connect(_launch_game)
	
	# フォーカス制御用シグナル
	# ボタンから左入力でカルーセルに戻る
	# 上下右も含めて、すべて自分自身を設定することでエンジンの自動移動を封じる
	_play_button.focus_neighbor_left = _play_button.get_path()
	# _play_button.focus_neighbor_top = _play_button.get_path() # 上は「遊び終わる」ボタンへ行くため制限解除（または明示的に設定）
	_play_button.focus_neighbor_bottom = _play_button.get_path()
	_play_button.focus_neighbor_right = _play_button.get_path()
	
	# マウスでフォーカスされたときの処理
	_play_button.mouse_entered.connect(func(): _play_button.grab_focus())
	
	# process_mode = Node.PROCESS_MODE_ALWAYS # _readyで設定済み

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
	# ポーズ中（ダイアログ表示中など）は入力を無視
	if get_tree().paused:
		return

	# 何らかの操作があったらアイドルタイマーをリセット
	# MouseMotionはリセット対象から外す（微細な動きでタイマーが進まないのを防ぐため）
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion or event is InputEventMouseButton:
		_reset_idle_timer()

	# 入力デバイス判定
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
		if _using_mouse:
			_using_mouse = false
			_update_bottom_bar_visibility()
	elif event is InputEventMouseButton or event is InputEventMouseMotion:
		if not _using_mouse:
			_using_mouse = true
			_update_bottom_bar_visibility()

	# マウス操作時はフォーカスを外す（マウスカーソルでの操作を優先）
	if event is InputEventMouseMotion:
		# ある程度大きく動いた場合のみフォーカス外しを行うなどの調整も可能だが、
		# 現状は動いたら即外しとする（ただしタイマーはリセットしない）
		var focus_owner = get_viewport().gui_get_focus_owner()
		if focus_owner:
			focus_owner.release_focus()
	
	if _games.is_empty():
		if event.is_action_pressed("ui_cancel"):
			_transition_to_screensaver()
		return

	# ゲーム実行中は操作を受け付けない
	if _running_pid != -1:
		return

	# キー入力/パッド入力検知
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
		# 何かキーを押したら、フォーカスがない場合は現在選択中のカードにフォーカスを当てる
		if not get_viewport().gui_get_focus_owner():
			_update_focus_to_current_card()

	# プレイボタンにフォーカスがある場合の例外処理
	if _play_button and _play_button.has_focus():
		if event.is_action_pressed("ui_left"):
			_update_focus_to_current_card() # カルーセルに戻る
			get_viewport().set_input_as_handled()
		elif event.is_action_pressed("ui_up"):
			# 上入力で「遊び終わる」ボタンへフォーカス移動
			if _exit_button:
				_exit_button.grab_focus()
				get_viewport().set_input_as_handled()
		elif event.is_action_pressed("ui_down") or event.is_action_pressed("ui_right"):
			# 下右は無効化（ボタンから抜けない）
			get_viewport().set_input_as_handled()
		return

	# 遊び終わるボタンにフォーカスがある場合の例外処理
	if _exit_button and _exit_button.has_focus():
		if event.is_action_pressed("ui_down"):
			# 下入力で「プレイ」ボタンへフォーカス移動
			if _play_button:
				_play_button.grab_focus()
				get_viewport().set_input_as_handled()
		elif event.is_action_pressed("ui_up") or event.is_action_pressed("ui_right") or event.is_action_pressed("ui_left"):
			# 他の方向は無効化（ボタンから抜けない）
			get_viewport().set_input_as_handled()
		return

	# カルーセル操作（プレイボタンにフォーカスがない時）
	# 上下は _process で処理されるため、ここではイベントを消費するだけ（デフォルトのフォーカス移動を防ぐ）
	# カルーセル操作（プレイボタンにフォーカスがない時）
	# 上下は _process で処理されるため、ここではイベントを消費するだけ（デフォルトのフォーカス移動を防ぐ）
	if event.is_action_pressed("ui_up") or event.is_action_pressed("ui_down"):
		get_viewport().set_input_as_handled()
	
	if event.is_action_pressed("ui_right") or event.is_action_pressed("ui_accept"):
		# 上下移動中（_processでのドラムロール中）は誤爆防止のため入力を無視
		if _last_input_dir != 0:
			get_viewport().set_input_as_handled()
			return

		# 右入力 または 決定キー でプレイボタンにフォーカス
		if _play_button:
			_play_button.grab_focus()
			get_viewport().set_input_as_handled()
	elif event.is_action_pressed("ui_cancel"):
		if get_viewport():
			get_viewport().set_input_as_handled()
		_transition_to_screensaver()

# ... (Previous helper functions) ...
func _transition_to_screensaver():
	# 遷移前にダイアログを閉じる
	DialogManager.close_current_dialog()
	get_tree().change_scene_to_file("res://scenes/screensaver.tscn")

func _reset_idle_timer():
	_idle_timer = 0.0
	if _timeout_dialog != null:
		# DialogManager経由で作ったダイアログならDialogManagerで閉じるのが筋だが
		# 個別に保持している参照を消す
		_timeout_dialog.queue_free()
		_timeout_dialog = null
		# DialogManager側の参照もクリアしておく（念のため）
	DialogManager.close_current_dialog()

func _switch_to_running_view():
	print("[GameSelection] Focus lost, switching to Running View")
	if _running_overlay:
		_running_overlay.visible = true
		
		# 他のカードを非表示にする
		for i in range(_card_nodes.size()):
			if i != _selected_index:
				_card_nodes[i].visible = false
		
		# その他のUIコンポーネントを非表示
		if _info_panel: _info_panel.visible = false
		if _top_bar: _top_bar.visible = false
		if _static_focus_border: _static_focus_border.visible = false 
		
		if _running_icon_container:
			_running_icon_container.visible = false
	if _running_icon:
		_running_icon.visible = false

func _unhandled_input(event):
	if get_tree().paused: return
	
	# マウスホイール操作（どこにフォーカスがあっても効くように、ただしScrollContainer上ではScrollContainerが優先されるように_unhandled_inputで）
	if event is InputEventMouseButton:
		if event.is_pressed():
			if event.button_index == MOUSE_BUTTON_WHEEL_UP:
				_move_selection(-1)
				get_viewport().set_input_as_handled()
			elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
				_move_selection(1)
				get_viewport().set_input_as_handled()
