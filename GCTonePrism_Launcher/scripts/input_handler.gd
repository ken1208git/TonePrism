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
var desc_scroll: ScrollContainer = null
var desc_label: Label = null
var _desc_scroll_target: float = 0.0
var _desc_focus_guard: bool = false  # フォーカス直後のスクロール防止
const DESC_SCROLL_PX_PER_SEC := 200.0  # 長押し時のスクロール速度 (px/sec)
const DESC_WHEEL_STEP := 60.0  # ホイール1ノッチ分のスクロール量
const DESC_LERP_SPEED := 15.0  # ホイールスクロールのlerp補間速度
var _input_hold_timer: float = 0.0
var _last_input_dir: int = 0

## _input イベントを処理する
func handle_input(event: InputEvent, viewport: Viewport,
		play_button: Button, exit_button: Button,
		is_running: bool, games_empty: bool) -> void:
	# 何らかの操作があったらアイドルタイマーをリセット
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion or event is InputEventMouseButton:
		idle_reset_requested.emit()

	# 入力デバイス判定（微小なマウス移動は無視 ― スクショ時等の誤検知防止）
	if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
		if using_mouse:
			using_mouse = false
	elif event is InputEventMouseButton:
		if not using_mouse:
			using_mouse = true
	elif event is InputEventMouseMotion and event.relative.length() > 1.0:
		if not using_mouse:
			using_mouse = true

	# マウス操作時はフォーカスを外す
	if event is InputEventMouseMotion and event.relative.length() > 1.0:
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

	# 説明文スクロールにフォーカスがある場合
	if desc_scroll and desc_scroll.has_focus():
		if event.is_action_pressed("ui_up"):
			if _desc_scroll_target <= 0 and desc_scroll.scroll_vertical <= 0:
				play_button.grab_focus()
		elif event.is_action_pressed("ui_left"):
			focus_to_card_requested.emit()
		# 上下左右すべて消費（カルーセルに流さない）
		if event is InputEventKey or event is InputEventJoypadButton or event is InputEventJoypadMotion:
			viewport.set_input_as_handled()
		return

	# プレイボタンにフォーカスがある場合
	if play_button and play_button.has_focus():
		if event.is_action_pressed("ui_left"):
			focus_to_card_requested.emit()
			viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_up"):
			if exit_button:
				exit_button.grab_focus()
				viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_down"):
			if desc_scroll and desc_scroll.get_child_count() > 0 \
					and desc_scroll.get_child(0).size.y > desc_scroll.size.y:
				_desc_focus_guard = true
				desc_scroll.grab_focus()
			viewport.set_input_as_handled()
		elif event.is_action_pressed("ui_right"):
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
			# 説明文エリア上ではカルーセルスクロールしない
			if desc_scroll and desc_scroll.get_global_rect().has_point(event.global_position):
				return
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
	   (desc_scroll and desc_scroll.has_focus()) or \
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

## 説明文スクロール更新（毎フレーム呼ぶ）
func update_desc_scroll(delta: float) -> void:
	if not desc_scroll:
		return

	# キーボードフォーカス中: 長押しでターゲット更新
	if desc_scroll.has_focus():
		if _desc_focus_guard:
			if not Input.is_action_pressed("ui_down"):
				_desc_focus_guard = false
			return
		var max_scroll := _get_desc_max_scroll()
		if Input.is_action_pressed("ui_down"):
			_desc_scroll_target = minf(_desc_scroll_target + DESC_SCROLL_PX_PER_SEC * delta, max_scroll)
		elif Input.is_action_pressed("ui_up"):
			_desc_scroll_target = maxf(_desc_scroll_target - DESC_SCROLL_PX_PER_SEC * delta, 0.0)

	# ターゲットに向かってlerp補間（キーボード・ホイール共通）
	var current := float(desc_scroll.scroll_vertical)
	if absf(current - _desc_scroll_target) > 0.5:
		desc_scroll.scroll_vertical = int(lerpf(current, _desc_scroll_target, DESC_LERP_SPEED * delta))
	elif int(current) != int(_desc_scroll_target):
		desc_scroll.scroll_vertical = int(_desc_scroll_target)

## 説明文スクロール位置をリセット
func reset_desc_scroll() -> void:
	_desc_scroll_target = 0.0
	if desc_scroll:
		desc_scroll.scroll_vertical = 0

## 説明文の最大スクロール量を取得
func _get_desc_max_scroll() -> float:
	if not desc_scroll:
		return 0.0
	var vbar := desc_scroll.get_v_scroll_bar()
	return maxf(vbar.max_value - vbar.page, 0.0)

## 説明文エリアのマウスホイールスクロール（_inputから呼ぶ）
func handle_desc_wheel(event: InputEventMouseButton, viewport: Viewport) -> void:
	var max_scroll := _get_desc_max_scroll()
	if max_scroll <= 0:
		return
	if event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
		_desc_scroll_target = minf(_desc_scroll_target + DESC_WHEEL_STEP, max_scroll)
	elif event.button_index == MOUSE_BUTTON_WHEEL_UP:
		_desc_scroll_target = maxf(_desc_scroll_target - DESC_WHEEL_STEP, 0.0)
	viewport.set_input_as_handled()

