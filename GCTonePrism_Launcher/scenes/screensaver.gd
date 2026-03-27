extends Control

## スクリーンセーバー画面
## マイルストーン3: 基本画面・画面遷移

## 画面状態
enum ScreenState {
	LOGO_DISPLAY,  # ロゴ表示画面（初期状態）
	SLIDESHOW      # ゲームプレビュースライドショー（将来実装）
}

var current_state: ScreenState = ScreenState.LOGO_DISPLAY
var idle_timer: float = 0.0
var idle_timeout: float = 30.0  # 30秒でスライドショーに遷移

@onready var _start_message: Label = $CenterContainer/VBoxContainer/StartMessage

func _ready():
	# キー入力とコントローラー入力を有効化
	set_process_input(true)
	set_process(true)
	
	if _start_message:
		_start_message.text = "PRESS ANY KEY"

func _process(delta):
	# アイドルタイマーの更新
	if current_state == ScreenState.LOGO_DISPLAY:
		idle_timer += delta
		
		# PRESS ANY KEY のブリージングアニメーション
		# 周期2秒程度 (3.0 rad/sec)
		# alpha: 0.2 ~ 1.0 (少し広めに)
		var scroll_alpha = 0.6 + 0.4 * sin(idle_timer * 3.0)
		
		if _start_message:
			_start_message.modulate.a = scroll_alpha

		if idle_timer >= idle_timeout:
			# スライドショーに遷移（将来実装）
			pass

func _input(event):
	var viewport = get_viewport()
	if not viewport:
		return
	
	# キー入力、マウスボタン、コントローラーボタンでゲーム選択画面に遷移
	# マウス移動（Motion）は除外する（誤作動防止）
	if event is InputEventKey or event is InputEventMouseButton or event is InputEventJoypadButton:
		if event.is_pressed():
			_transition_to_game_selection()
			viewport.set_input_as_handled()
			return
	
	# 任意のキー/ボタンでロゴ表示画面に戻る（スライドショー表示時）
	if current_state == ScreenState.SLIDESHOW:
		if event is InputEventKey or event is InputEventJoypadButton:
			_transition_to_logo_display()
			viewport.set_input_as_handled()
			return

## ゲーム選択画面に遷移
func _transition_to_game_selection():
	TransitionManager.change_scene("res://scenes/store_browse.tscn")

## ロゴ表示画面に遷移
func _transition_to_logo_display():
	current_state = ScreenState.LOGO_DISPLAY
	idle_timer = 0.0

## スライドショーに遷移（将来実装）
func _transition_to_slideshow():
	current_state = ScreenState.SLIDESHOW
