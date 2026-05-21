class_name GameLauncher
extends RefCounted
## ゲーム起動・終了監視ロジック
## パス解決・引数パースは GamePathResolver を参照

signal game_started()
signal game_ended()

var running_pid: int = -1
var _is_launching: bool = false
var _is_returning: bool = false

# --- WindowProbe 連携 (#101 起動中→プレイ中の遷移同期 / #216 前面化異常検知) ---
# probe は OS.execute がブロッキングなので専用スレッドで回し、結果を Mutex 保護の共有変数に置く。
# 遷移判定・異常判定はメインスレッド (monitor_process) で共有結果を読んで行う。
const PROBE_POLL_INTERVAL_LAUNCHING_MS: int = 150  # 起動中: ウィンドウ出現を素早く検出
const PROBE_POLL_INTERVAL_PLAYING_MS: int = 1000   # プレイ中: 異常検知は粗くてよい (i3 負荷軽減 #214)
# 可視ウィンドウ未検出でも「起動中」で固まらないよう強制 PLAYING にするまでの時間。
# 初回起動の Defender / SmartScreen スキャンで窓出現が遅れるため長めに取る (1 分)。
# 注意: タイムアウトで PLAYING ラベルにしても前面化異常監視は arm しない (窓を実際に観測した時のみ arm)。
const PLAYING_FALLBACK_TIMEOUT_MS: int = 60000
const ANOMALY_DEBOUNCE_MS: int = 2000              # 前面化異常を発報するまでの継続時間 (一瞬の Alt-Tab 除外)
const PROBE_STOP_CHECK_CHUNK_MS: int = 50          # 待機を小刻みにして停止要求へ素早く応答 (ゲーム終了時の join 短縮)

var _probe_available: bool = false
var _probe_thread: Thread = null
var _probe_mutex: Mutex = Mutex.new()
var _probe_pid: int = -1
var _probe_stop: bool = false
var _probe_result: int = WindowProbeClient.Result.UNAVAILABLE
var _playing_confirmed: bool = false   # 可視検出 or タイムアウトで PLAYING 確定済み
var _anomaly_armed: bool = false       # PLAYING 確定後に異常監視を有効化
var _anomaly_since_ms: int = 0         # 異常状態が継続し始めた時刻 (0 = 非異常)
var _anomaly_active: bool = false      # 異常エラーを表示中
var _launch_time_ms: int = 0

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
		print("[GameLauncher] Process started. PID: %d" % pid)
		running_pid = pid
		_is_launching = false

		# WindowProbe が使えるなら、可視ウィンドウ出現まで「起動中」を保ち、
		# 出現を検出した時点で PLAYING へ遷移する (#101)。
		# 使えない場合 (エディタ実行 / exe 未同梱) は従来どおり即 PLAYING にフォールバック。
		_reset_probe_state()
		_probe_available = WindowProbeClient.is_available()
		if _probe_available:
			_start_probe_thread(pid)
			print("[GameLauncher] WindowProbe 監視を開始 (PLAYING 遷移はウィンドウ出現で確定)")
		else:
			if launching_overlay:
				launching_overlay.set_state(LaunchingOverlay.State.PLAYING)
			_playing_confirmed = true
			print("[GameLauncher] WindowProbe 不在のため即 PLAYING (従来挙動)")

		game_started.emit()

## 毎フレーム呼ばれる。ゲーム終了を監視する
func monitor_process(window: Window, status_label: Label, game: GameInfo,
		launching_overlay: LaunchingOverlay, card_nodes: Array[Panel],
		info_panel: Panel, top_bar: Control, static_focus_border: Panel,
		carousel_container: Control = null, bottom_bar: Control = null,
		background_texture: TextureRect = null) -> void:
	if running_pid == -1:
		return

	# プロセスの終了判定（最優先）
	if not OS.is_process_running(running_pid):
		_on_game_exited(window, launching_overlay, card_nodes, info_panel, top_bar,
			static_focus_border, carousel_container, bottom_bar, background_texture)
		return

	# ランチャーが前面かどうか（前面でない＝ゲームが前面、が正常）
	var launcher_foreground := window.has_focus()

	# WindowProbe 有効時のみ: 起動中→プレイ中の遷移と前面化異常を判定する
	if _probe_available:
		var res := _get_probe_result()
		var game_visible := (res == WindowProbeClient.Result.VISIBLE_BACKGROUND
			or res == WindowProbeClient.Result.VISIBLE_FOREGROUND)

		# 起動中 → プレイ中ラベル切替: 可視ウィンドウ検出で確定。
		# 検出できないままタイムアウトしたら「起動中」で固まるのを防ぐため強制的にプレイ中へ。
		if not _playing_confirmed:
			if game_visible:
				_confirm_playing(launching_overlay, "WindowProbe が可視ウィンドウを検出")
			elif Time.get_ticks_msec() - _launch_time_ms >= PLAYING_FALLBACK_TIMEOUT_MS:
				_confirm_playing(launching_overlay, "フォールバックタイムアウト (%dms)" % PLAYING_FALLBACK_TIMEOUT_MS)

		# 前面化異常の監視を arm するのは「実際にゲーム窓を一度でも観測した」時のみ。
		# 初回起動の Defender / SmartScreen スキャン等で窓が出ないうちは arm せず、
		# 「窓が無い＝異常」と誤判定してスタッフ呼び出しを出すのを防ぐ。
		# タイムアウトで PLAYING ラベルにしただけでは arm しない (window 観測が条件)。
		if not _anomaly_armed and game_visible:
			_anomaly_armed = true
			print("[GameLauncher] ゲーム窓を確認、前面化異常監視を有効化")

		if _anomaly_armed:
			_update_anomaly_detection(launcher_foreground)

## ゲーム終了時の後始末（probe スレッド停止 + 異常エラークリア + 通常表示復帰）
func _on_game_exited(window: Window, launching_overlay: LaunchingOverlay,
		card_nodes: Array[Panel], info_panel: Panel, top_bar: Control,
		static_focus_border: Panel, carousel_container: Control,
		bottom_bar: Control, background_texture: TextureRect) -> void:
	print("[GameLauncher] Game process %d finished." % running_pid)
	_stop_probe_thread()
	running_pid = -1

	# 前面化異常エラーを表示中なら閉じる（ゲーム終了で意味を失うため）
	if _anomaly_active:
		_anomaly_active = false
		ErrorManager.hide_error(ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY)

	if launching_overlay:
		launching_overlay.hide_overlay()
	_switch_to_normal_view(card_nodes, info_panel, top_bar, static_focus_border, window.get_tree(), carousel_container, bottom_bar, background_texture)
	game_ended.emit()

# ============================================================================
# WindowProbe 連携 (#101 / #216)
# ============================================================================

func _reset_probe_state() -> void:
	_probe_mutex.lock()
	_probe_result = WindowProbeClient.Result.UNAVAILABLE
	_probe_stop = false
	_probe_mutex.unlock()
	_playing_confirmed = false
	_anomaly_armed = false
	_anomaly_since_ms = 0
	_anomaly_active = false
	_launch_time_ms = Time.get_ticks_msec()

func _start_probe_thread(pid: int) -> void:
	_probe_pid = pid
	_probe_thread = Thread.new()
	_probe_thread.start(_probe_loop)

## probe 専用スレッド本体。結果を共有変数に書くだけ（UI/Window には触らない）。
func _probe_loop() -> void:
	while true:
		_probe_mutex.lock()
		var stop := _probe_stop
		var playing := _playing_confirmed
		_probe_mutex.unlock()
		if stop:
			break

		var res := WindowProbeClient.probe(_probe_pid)
		_probe_mutex.lock()
		_probe_result = res
		_probe_mutex.unlock()

		# 待機は小刻みにして毎回 stop を確認する。これでゲーム終了時の
		# wait_to_finish が最大 ~1 chunk + probe 1 回分しかブロックしない
		# (旧実装は OS.delay_msec(1000) を待ち切るためメインスレッドが最大 ~1s 固まった)。
		var interval := PROBE_POLL_INTERVAL_PLAYING_MS if playing else PROBE_POLL_INTERVAL_LAUNCHING_MS
		var waited := 0
		while waited < interval:
			_probe_mutex.lock()
			var stop2 := _probe_stop
			_probe_mutex.unlock()
			if stop2:
				return
			var chunk := mini(PROBE_STOP_CHECK_CHUNK_MS, interval - waited)
			OS.delay_msec(chunk)
			waited += chunk

func _stop_probe_thread() -> void:
	if _probe_thread == null:
		return
	_probe_mutex.lock()
	_probe_stop = true
	_probe_mutex.unlock()
	_probe_thread.wait_to_finish()
	_probe_thread = null

## ランチャー終了時などに外部から呼ぶ後始末（probe スレッドの join）。
func shutdown() -> void:
	_stop_probe_thread()

func _get_probe_result() -> int:
	_probe_mutex.lock()
	var res := _probe_result
	_probe_mutex.unlock()
	return res

func _confirm_playing(launching_overlay: LaunchingOverlay, reason: String) -> void:
	# _playing_confirmed を立てると _probe_loop が次周回で poll 間隔を粗く切り替える。
	# probe スレッドも mutex 下で読むため、書き込みも mutex で囲って対称にする。
	# 注意: ここでは異常監視を arm しない。arm は「実際にゲーム窓を観測した」時のみ
	# (monitor_process 側)。タイムアウト確定では arm せず、スキャン中の誤発報を防ぐ。
	_probe_mutex.lock()
	_playing_confirmed = true
	_probe_mutex.unlock()
	_anomaly_since_ms = 0
	if launching_overlay:
		launching_overlay.set_state(LaunchingOverlay.State.PLAYING)
	print("[GameLauncher] PLAYING 確定: %s" % reason)

## プレイ中の前面化異常を検知する。
## 異常 = ランチャーが前面 かつ ゲームが前面でない、がデバウンス期間継続。
func _update_anomaly_detection(launcher_foreground: bool) -> void:
	var res := _get_probe_result()
	# 「ゲームが前面でない」と positive に確証できる結果のみで異常カウントする。
	# NOT_FOUND / UNAVAILABLE は probe の一時失敗 (スナップショット失敗等) の可能性があるため
	# 判定不能扱いとし、「窓が無い＝異常」と誤カウントしない (= デバウンスをリセット)。
	var game_not_front := (res == WindowProbeClient.Result.NOT_VISIBLE
		or res == WindowProbeClient.Result.VISIBLE_BACKGROUND)
	var anomaly := launcher_foreground and game_not_front

	if anomaly:
		if _anomaly_since_ms == 0:
			_anomaly_since_ms = Time.get_ticks_msec()
		elif not _anomaly_active and Time.get_ticks_msec() - _anomaly_since_ms >= ANOMALY_DEBOUNCE_MS:
			_anomaly_active = true
			# logger.gd は "USER WARNING:" 行を WARN レベルとして拾うため push_warning を使う
			push_warning("[GameLauncher] ランチャー前面化異常を検出 (PID %d 生存中、要スタッフ対応)" % running_pid)
			ErrorManager.show_error(ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY)
	else:
		_anomaly_since_ms = 0
		if _anomaly_active:
			_anomaly_active = false
			print("[GameLauncher] ランチャー前面化異常が解消、エラーをクリア")
			ErrorManager.hide_error(ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY)

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
