class_name InputHandler
extends RefCounted
## 入力処理（キーボード・ゲームパッド・マウス）

# キーリピート（長押し）設定
const KEY_REPEAT_DELAY := 0.4
const KEY_REPEAT_RATE := 0.06

signal selection_moved(dir: int)
signal play_requested()
signal exit_requested()
signal focus_to_card_requested()
signal idle_reset_requested()

var using_mouse: bool = false
var back_button: Button = null
var _input_hold_timer: float = 0.0
var _last_input_dir: int = 0

## _input イベントを処理する
func handle_input(event: InputEvent, viewport: Viewport,
		play_button: Button, exit_button: Button,
		is_running: bool, games_empty: bool) -> void:
	# 何らかの操作があったらアイドルタイマーをリセット
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion or event is InputEventMouseButton:
		idle_reset_requested.emit()

	# 入力デバイス判定
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
		if using_mouse:
			using_mouse = false
	elif event is InputEventMouseButton or event is InputEventMouseMotion:
		if not using_mouse:
			using_mouse = true

	# マウス操作時はフォーカスを外す
	if event is InputEventMouseMotion:
		var focus_owner = viewport.gui_get_focus_owner()
		if focus_owner:
			focus_owner.release_focus()

	if games_empty:
		if event.is_action_pressed("ui_cancel"):
			exit_requested.emit()
		return

	# ゲーム実行中は操作を受け付けない
	if is_running:
		return

	# キー入力検知 → フォーカスがなければカードにフォーカス
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
		if not viewport.gui_get_focus_owner():
			focus_to_card_requested.emit()

	# プレイボタンにフォーカスがある場合
	if play_button and play_button.has_focus():
		if event.is_action_pressed("ui_left"):
			focus_to_card_requested.emit()
			viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_up"):
			if exit_button:
				exit_button.grab_focus()
				viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_down") or event.is_action_pressed("ui_right"):
			viewport.set_input_as_handled()
		return

	# 戻るボタンにフォーカスがある場合
	if back_button and back_button.has_focus():
		if event.is_action_pressed("ui_down"):
			_last_input_dir = 1  # process_drum_rollの同フレーム発火を防ぐ
			focus_to_card_requested.emit()
			viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_right"):
			if exit_button:
				exit_button.grab_focus()
				viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_up") or event.is_action_pressed("ui_left"):
			viewport.set_input_as_handled()
		return

	# 終了ボタンにフォーカスがある場合
	if exit_button and exit_button.has_focus():
		if event.is_action_pressed("ui_down"):
			if play_button:
				play_button.grab_focus()
				viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_left"):
			if back_button:
				back_button.grab_focus()
				viewport.set_input_as_handled()
			else:
				viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_up") or event.is_action_pressed("ui_right"):
			viewport.set_input_as_handled()
		return

	# カルーセル操作
	if event.is_action_pressed("ui_up") or event.is_action_pressed("ui_down"):
		viewport.set_input_as_handled()

	if event.is_action_pressed("ui_right") or event.is_action_pressed("ui_accept"):
		if _last_input_dir != 0:
			viewport.set_input_as_handled()
			return
		play_requested.emit()
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_cancel"):
		if viewport:
			viewport.set_input_as_handled()
		exit_requested.emit()

## _unhandled_input イベントを処理する（マウスホイール）
func handle_unhandled_input(event: InputEvent, viewport: Viewport) -> void:
	if event is InputEventMouseButton:
		if event.is_pressed():
			if event.button_index == MOUSE_BUTTON_WHEEL_UP:
				selection_moved.emit(-1)
				viewport.set_input_as_handled()
			elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
				selection_moved.emit(1)
				viewport.set_input_as_handled()

## ドラムロール入力（長押しによる高速スクロール）を処理する
func process_drum_roll(delta: float, play_button: Button, exit_button: Button,
		is_running: bool) -> void:
	var input_allowed = true
	if (play_button and play_button.has_focus()) or \
	   (exit_button and exit_button.has_focus()) or \
	   (back_button and back_button.has_focus()) or \
	   is_running:
		input_allowed = false

	if not input_allowed:
		return

	var dir = 0
	if Input.is_action_pressed("ui_down"):
		dir += 1
	if Input.is_action_pressed("ui_up"):
		dir -= 1

	if dir != 0:
		if _last_input_dir != dir:
			selection_moved.emit(dir)
			if not (back_button and back_button.has_focus()):
				focus_to_card_requested.emit()
			_input_hold_timer = KEY_REPEAT_DELAY
			_last_input_dir = dir
		else:
			_input_hold_timer -= delta
			if _input_hold_timer <= 0:
				selection_moved.emit(dir)
				_input_hold_timer = KEY_REPEAT_RATE
	else:
		_last_input_dir = 0
		_input_hold_timer = 0.0
