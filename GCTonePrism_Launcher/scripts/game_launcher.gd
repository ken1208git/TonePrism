class_name GameLauncher
extends RefCounted
## ゲーム起動・終了監視ロジック
## パス解決・引数パースは GamePathResolver を参照

signal game_started()
signal game_ended()

var running_pid: int = -1
var _has_lost_focus_since_launch: bool = false
var _is_launching: bool = false
var _is_returning: bool = false

func is_running() -> bool:
	return running_pid != -1 or _is_launching or _is_returning

## ゲーム起動
## 起動・終了時の背景ズーム演出のために background_texture を受け取る
const LAUNCH_TRANSITION_DURATION: float = 0.55
const LAUNCH_BG_ZOOM_SCALE: float = 1.05

func launch_game(game: GameInfo, status_label: Label, launching_overlay: LaunchingOverlay,
		carousel_container: Control, info_panel: Panel, top_bar: Control,
	static_focus_border: Panel, card_nodes: Array[Panel], selected_index: int,
		tree: SceneTree, bottom_bar: Control = null,
		background_texture: TextureRect = null) -> void:
	if running_pid != -1 or _is_launching:
		return

	_is_launching = true
	print("[GameLauncher] Launching game: ", game.title, " (ID: ", game.game_id, ")")

	var exe_path = GamePathResolver.find_executable(game)

	if exe_path.is_empty():
		print("❌ Executable not found: ", game.executable_path)
		ErrorManager.show_error(ErrorCode.GAME_EXECUTABLE_NOT_FOUND)
		_is_launching = false
		return

	var args = GamePathResolver.parse_arguments(game.arguments)

	# 起動中オーバーレイを表示（LAUNCHING 状態）
	if launching_overlay:
		launching_overlay.show_for_game(game.title, LaunchingOverlay.State.LAUNCHING)

	_switch_to_running_view(card_nodes, selected_index, info_panel, top_bar, static_focus_border, tree, carousel_container, bottom_bar, background_texture)

	# UIの描画更新とアニメーションを待つ
	await tree.create_timer(1.0).timeout

	print("  Path: ", exe_path)
	print("  Args: ", args)

	var working_dir = exe_path.get_base_dir()
	print("[GameLauncher] Working Directory: %s" % working_dir)

	# cmd経由で作業ディレクトリを設定して起動
	var cmd_command = 'cd /d "%s" && "%s"' % [working_dir, exe_path]
	if not args.is_empty():
		var escaped_args = []
		for arg in args:
			escaped_args.append('"%s"' % arg.replace('"', '\\"'))
		cmd_command += " " + " ".join(escaped_args)

	print("[GameLauncher] CMD Command: ", cmd_command)

	_has_lost_focus_since_launch = false
	var pid = OS.create_process("cmd.exe", ["/C", cmd_command])

	if pid == -1:
		print("❌ Failed to create process.")
		ErrorManager.show_error(ErrorCode.GAME_EXECUTION_FAILED)
		if launching_overlay:
			launching_overlay.hide_overlay()
		_switch_to_normal_view(card_nodes, info_panel, top_bar, static_focus_border, tree, carousel_container, bottom_bar, background_texture) # 失敗時は戻す
		_is_launching = false
		return
	else:
		print("✅ Process started. PID: %d" % pid)
		running_pid = pid
		_is_launching = false
		# プロセスが立ち上がったので PLAYING 状態に切り替え
		if launching_overlay:
			launching_overlay.set_state(LaunchingOverlay.State.PLAYING)
		game_started.emit()

## 毎フレーム呼ばれる。ゲーム終了を監視する
func monitor_process(window: Window, status_label: Label, game: GameInfo,
		launching_overlay: LaunchingOverlay, card_nodes: Array[Panel],
		info_panel: Panel, top_bar: Control, static_focus_border: Panel,
		carousel_container: Control = null, bottom_bar: Control = null,
		background_texture: TextureRect = null) -> void:
	if running_pid == -1:
		return

	# フォーカス状態の監視
	if not window.has_focus():
		_has_lost_focus_since_launch = true

	# プロセスの終了判定
	if not OS.is_process_running(running_pid):
		print("[GameLauncher] Game process %d finished." % running_pid)
		running_pid = -1

		if launching_overlay:
			launching_overlay.hide_overlay()
		_switch_to_normal_view(card_nodes, info_panel, top_bar, static_focus_border, window.get_tree(), carousel_container, bottom_bar, background_texture)
		game_ended.emit()

## 起動中表示に切り替え（UIをフェードアウト + 背景ズームイン）
func _switch_to_running_view(card_nodes: Array[Panel],
		selected_index: int, info_panel: Panel, top_bar: Control,
		static_focus_border: Panel, tree: SceneTree,
		carousel_container: Control = null, bottom_bar: Control = null,
		background_texture: TextureRect = null) -> void:
	print("[GameLauncher] Switching to Running View (Fade Out + BG Zoom)")

	var tween = tree.create_tween()
	tween.set_parallel(true)

	for i in range(card_nodes.size()):
		if i != selected_index:
			tween.tween_property(card_nodes[i], "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
				.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		# 選択中のカード（i == selected_index）は何もしない（残す）

	if info_panel:
		tween.tween_property(info_panel, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if top_bar:
		tween.tween_property(top_bar, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if bottom_bar:
		tween.tween_property(bottom_bar, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if static_focus_border:
		tween.tween_property(static_focus_border, "modulate:a", 0.0, 0.3)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	if carousel_container:
		var up_btn = carousel_container.get_node_or_null("ScrollUpButton")
		var down_btn = carousel_container.get_node_or_null("ScrollDownButton")
		if up_btn:
			tween.tween_property(up_btn, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		if down_btn:
			tween.tween_property(down_btn, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	# 背景画像を中心からほんのちょっとズームイン（同じ easing でオーバーレイ演出と同期）
	if background_texture:
		background_texture.pivot_offset = background_texture.size / 2.0
		tween.tween_property(background_texture, "scale", Vector2(LAUNCH_BG_ZOOM_SCALE, LAUNCH_BG_ZOOM_SCALE), LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

## 通常表示に戻す（UIをフェードイン + 背景ズームアウト）
func _switch_to_normal_view(card_nodes: Array[Panel],
		info_panel: Panel, top_bar: Control, static_focus_border: Panel, tree: SceneTree,
		carousel_container: Control = null, bottom_bar: Control = null,
		background_texture: TextureRect = null) -> void:
	print("[GameLauncher] Switching to Normal View (Fade In + BG Zoom Out)")

	_is_returning = true

	if not tree:
		# ツリーがない場合のフォールバック（強制即時表示）
		for card in card_nodes:
			card.visible = true
			if card.modulate.a < 1.0:
				card.modulate.a = CarouselController.OPACITY_INACTIVE
		if info_panel:
			info_panel.visible = true
			info_panel.modulate.a = 1.0
		if top_bar:
			top_bar.visible = true
			top_bar.modulate.a = 1.0
		if bottom_bar:
			bottom_bar.visible = true
			bottom_bar.modulate.a = 1.0
		if static_focus_border:
			static_focus_border.visible = true
			static_focus_border.modulate.a = 1.0
		if carousel_container:
			var up_btn = carousel_container.get_node_or_null("ScrollUpButton")
			var down_btn = carousel_container.get_node_or_null("ScrollDownButton")
			if up_btn:
				up_btn.visible = true
				up_btn.modulate.a = 1.0
			if down_btn:
				down_btn.visible = true
				down_btn.modulate.a = 1.0
		if background_texture:
			background_texture.scale = Vector2.ONE
		_is_returning = false
		return

	var tween = tree.create_tween()
	tween.set_parallel(true)

	for card in card_nodes:
		card.visible = true
		if card.modulate.a < 1.0: # 選択カードは1.0のままなのでスキップされる
			tween.tween_property(card, "modulate:a", CarouselController.OPACITY_INACTIVE, LAUNCH_TRANSITION_DURATION)\
				.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	if info_panel:
		info_panel.visible = true
		tween.tween_property(info_panel, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if top_bar:
		top_bar.visible = true
		tween.tween_property(top_bar, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if bottom_bar:
		bottom_bar.visible = true
		tween.tween_property(bottom_bar, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if static_focus_border:
		static_focus_border.visible = true
		tween.tween_property(static_focus_border, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	if carousel_container:
		var up_btn = carousel_container.get_node_or_null("ScrollUpButton")
		var down_btn = carousel_container.get_node_or_null("ScrollDownButton")
		if up_btn:
			up_btn.visible = true
			tween.tween_property(up_btn, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION).from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		if down_btn:
			down_btn.visible = true
			tween.tween_property(down_btn, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION).from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	# 背景画像をズームアウトして元のスケールに戻す
	if background_texture:
		background_texture.pivot_offset = background_texture.size / 2.0
		tween.tween_property(background_texture, "scale", Vector2.ONE, LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	# フェードインアニメーション完了後に状態を戻す
	tween.finished.connect(func():
		_is_returning = false
	)
