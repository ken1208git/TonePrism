class_name GameLauncher
extends RefCounted
## ゲーム起動・終了監視ロジック
## パス解決・引数パースは GamePathResolver を参照

signal game_started()
signal game_ended()

var running_pid: int = -1
var _has_lost_focus_since_launch: bool = false

func is_running() -> bool:
	return running_pid != -1

## ゲーム起動
func launch_game(game: GameInfo, status_label: Label, running_overlay: Control,
		carousel_container: Control, info_panel: Panel, top_bar: Control,
		static_focus_border: Panel, card_nodes: Array[Panel], selected_index: int,
		tree: SceneTree) -> void:
	if running_pid != -1:
		return

	print("[GameLauncher] Launching game: ", game.title, " (ID: ", game.game_id, ")")

	var exe_path = GamePathResolver.find_executable(game)

	if exe_path.is_empty():
		print("❌ Executable not found: ", game.executable_path)
		ErrorManager.show_error(ErrorCode.GAME_EXECUTABLE_NOT_FOUND)
		return

	var args = GamePathResolver.parse_arguments(game.arguments)

	# 起動中表示
	if running_overlay:
		running_overlay.visible = true
	_switch_to_running_view(running_overlay, card_nodes, selected_index, info_panel, top_bar, static_focus_border)

	if status_label:
		status_label.text = "ゲーム起動中: %s\nお楽しみください！" % game.title

	# UIの描画更新を待つ
	await tree.process_frame
	await tree.process_frame
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
		return
	else:
		print("✅ Process started. PID: %d" % pid)
		running_pid = pid
		game_started.emit()

## 毎フレーム呼ばれる。ゲーム終了を監視する
func monitor_process(window: Window, status_label: Label, game: GameInfo,
		running_overlay: Control, card_nodes: Array[Panel],
		info_panel: Panel, top_bar: Control, static_focus_border: Panel) -> void:
	if running_pid == -1:
		return

	# フォーカス状態の監視
	if not window.has_focus():
		_has_lost_focus_since_launch = true
		if status_label and "起動中" in status_label.text:
			status_label.text = "ゲーム実行中: %s\nお楽しみください！" % game.title

	# プロセスの終了判定
	if not OS.is_process_running(running_pid):
		print("[GameLauncher] Game process %d finished." % running_pid)
		running_pid = -1

		if running_overlay:
			running_overlay.visible = false
		for card in card_nodes:
			card.visible = true
		if info_panel:
			info_panel.visible = true
		if top_bar:
			top_bar.visible = true
		if static_focus_border:
			static_focus_border.visible = true

		game_ended.emit()

## 起動中表示に切り替え
func _switch_to_running_view(running_overlay: Control, card_nodes: Array[Panel],
		selected_index: int, info_panel: Panel, top_bar: Control,
		static_focus_border: Panel) -> void:
	print("[GameLauncher] Switching to Running View")
	if running_overlay:
		running_overlay.visible = true
		for i in range(card_nodes.size()):
			if i != selected_index:
				card_nodes[i].visible = false
		if info_panel:
			info_panel.visible = false
		if top_bar:
			top_bar.visible = false
		if static_focus_border:
			static_focus_border.visible = false

		var running_icon_container = running_overlay.get_node_or_null("RunningIconContainer")
		if running_icon_container:
			running_icon_container.visible = false
		var running_icon = running_overlay.get_node_or_null("RunningIconContainer/Icon")
		if running_icon:
			running_icon.visible = false
