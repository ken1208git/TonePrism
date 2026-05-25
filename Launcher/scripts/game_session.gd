extends Node
## Autoload: ゲームセッションを一元管理する (#30 / #214)。
## 起動・監視・PLAYING確定・前面化異常(#216)・resume/quit・プロセス死活を、シーンをまたいで保持する。
## これにより「プレイ中は game_selection (重いカルーセル) を破棄して軽量シーンに切替」してもゲームの
## 監視が途切れない (#214 メモリ削減の前提)。
##
## 本 autoload は **ノードに依存しない**。launching_overlay の状態表示や起動/復帰演出は、本 autoload の
## signal を購読する現シーン (game_selection / playing) が各自反映する。
## (旧: game_selection 所有の GameLauncher。RefCounted からの移管。)

signal game_started()          ## プロセス起動直後 (LAUNCHING 相当)
signal playing_confirmed()     ## 可視ウィンドウ検出 or タイムアウトで PLAYING 確定
signal game_quitting()         ## 中断メニューからの終了開始 (taskkill 直前)。現シーンが「終了中」表示に使う
signal game_quit_aborted()     ## 終了開始に失敗 (taskkill 起動不可)。game_quitting で出した「終了中」表示を戻す
signal game_exited()           ## プロセス終了 (自然終了 / quit)

# 可視ウィンドウ未検出でも「起動中」で固まらないよう強制 PLAYING にするまでの時間 (初回スキャン対策で長め)。
const PLAYING_FALLBACK_TIMEOUT_MS: int = 60000
const ANOMALY_DEBOUNCE_MS: int = 2000  # 前面化異常を「異常」と判定するまでの継続時間 (一瞬の Alt-Tab 除外)
# #219: 異常確定後、スタッフ警告の前にゲーム窓の強制前面化で自己修復を試みる二段構え。
const ANOMALY_RECOVERY_MAX_ATTEMPTS: int = 3  # 強制前面化のリトライ上限。尽きても background なら警告へ
const ANOMALY_RECOVERY_INTERVAL_MS: int = 500  # リトライ間隔 (focus 反映 + 次 probe 更新を待つ)
# Companion ハンドシェイク待ちの上限 (起動直後にゲームを起動した race 対策)。超過したら probe 無しで継続。
const PROBE_HANDSHAKE_TIMEOUT_MS: int = 5000
# quit() で taskkill 発行後、これだけ経ってもプロセスが消えない場合は終了失敗とみなして固着を解く
# (taskkill は起動できたが権限/昇格/ハングで kill できずゲームが生き残る稀ケース。固着すると #216 抑止が続く)。
const QUIT_FALLBACK_TIMEOUT_MS: int = 10000

var running_pid: int = -1
var current_game: GameInfo = null

var _is_launching: bool = false
var _probe_available: bool = false      # Companion が使えるか
var _probe_pending: bool = false        # exe 起動済だがハンドシェイク未完了で watch 待ち (起動直後 race)
var _playing_confirmed: bool = false
var _anomaly_armed: bool = false
var _anomaly_since_ms: int = 0
var _anomaly_active: bool = false
var _anomaly_logged: bool = false
var _recovery_attempts: int = 0    # #219: 今回の異常エピソードでの強制前面化リトライ回数
var _last_recovery_ms: int = 0     # 直近の強制前面化時刻 (リトライ間隔の判定用)
var _launch_time_ms: int = 0
var _probe_failure_logged: bool = false
# 終了後の遷移先: true=スクリーンセーバー / false=ゲーム選択画面。退出メニュー選択で true。
# 実際の change_scene は終了時に現シーン (playing / game_selection) が本フラグを見て行う。
var _exit_to_screensaver: bool = false
# quit/exit 実行中 (taskkill 発行〜プロセス消失まで)。この過渡状態はランチャーが前面でゲームが
# まだ生きており #216 前面化異常を誤検知するため、検知を抑止する。
var _quitting: bool = false
var _quit_started_ms: int = 0  # taskkill 発行時刻 (quit watchdog 用)。プロセスが消えない固着の検知に使う


func _ready() -> void:
	# ダイアログ表示等で tree.paused になっても監視 (プロセス死活 / PLAYING 確定 / 前面化異常) を止めない。
	process_mode = Node.PROCESS_MODE_ALWAYS


func is_running() -> bool:
	return running_pid != -1 or _is_launching


func is_playing() -> bool:
	return running_pid != -1 and _playing_confirmed


## 起動シーケンス開始を宣言する (実プロセスはまだ起動しない)。起動演出の「前」に呼ぶことで、
## 演出中 (await) も is_running()=true となり、カルーセル更新が modulate を戻して演出を打ち消すのを防ぐ。
func begin_launch(game: GameInfo) -> bool:
	if running_pid != -1 or _is_launching:
		return false
	_is_launching = true
	current_game = game
	_exit_to_screensaver = false
	_quitting = false
	return true


## begin_launch 済み前提で実プロセスを起動する (exe 探索・引数・cmd 経由起動・companion watch)。
## 起動演出 (await) の後に呼ぶ。戻り値=成功可否。
func start_process() -> bool:
	if not _is_launching or current_game == null:
		return false
	var game := current_game

	var exe_path := GamePathResolver.find_executable(game)
	if exe_path.is_empty():
		print("❌ Executable not found: ", game.executable_path)
		ErrorManager.show_error(ErrorCode.GAME_EXECUTABLE_NOT_FOUND)
		_is_launching = false
		current_game = null
		return false

	var args := GamePathResolver.parse_arguments(game.arguments)
	var working_dir := exe_path.get_base_dir()
	var cmd_command := 'cd /d "%s" && "%s"' % [working_dir, exe_path]
	if not args.is_empty():
		var escaped: Array = []
		for a in args:
			escaped.append('"%s"' % a.replace('"', '\\"'))
		cmd_command += " " + " ".join(escaped)
	print("[GameSession] CMD Command: ", cmd_command)

	var pid := OS.create_process("cmd.exe", ["/C", cmd_command])
	if pid == -1:
		print("❌ Failed to create process.")
		ErrorManager.show_error(ErrorCode.GAME_EXECUTION_FAILED)
		_is_launching = false
		current_game = null
		return false

	print("[GameSession] Process started. PID: %d" % pid)
	running_pid = pid
	_is_launching = false
	_reset_probe_state()
	if LauncherAgent.is_available():
		_probe_available = true
		LauncherAgent.watch(pid, _launcher_monitor_rect())
		print("[GameSession] LauncherAgent 監視を開始 (PLAYING 遷移はウィンドウ出現で確定)")
	elif LauncherAgent.is_expected():
		# exe は起動済みだがハンドシェイク未完了 (起動直後にゲームを起動した race)。完了を _process で待ってから
		# watch する。それまで PLAYING は未確定のまま (確定 → watch 前に anomaly 監視が始まり誤検知し得るため)。
		_probe_pending = true
		print("[GameSession] LauncherAgent ハンドシェイク待ち (完了後に監視を開始)")
	else:
		# Companion 不在 (エディタ実行 / exe 未同梱): ハンドシェイクは来ないので従来どおり即 PLAYING。
		_playing_confirmed = true
		print("[GameSession] LauncherAgent 不在のため即 PLAYING (従来挙動)")
		playing_confirmed.emit()
	game_started.emit()
	return true


## 毎フレーム監視。プロセス死活 / Companion 窓状態 → PLAYING確定・前面化異常(#216) を判定。
func _process(_delta: float) -> void:
	if running_pid == -1:
		return

	# プロセス終了判定 (最優先)
	if not OS.is_process_running(running_pid):
		_on_exited()
		return

	# quit watchdog: taskkill は起動できたが実際に kill できず (権限/昇格/ハング) ゲームが生き残ると、
	# game_exited が永久に来ず _quitting のまま固着 → #216 検知も無期限抑止される。一定時間で諦めて
	# 固着を解き (anomaly 再有効化)、game_quit_aborted で overlay/playing の「終了中」表示も巻き戻す。
	if _quitting and Time.get_ticks_msec() - _quit_started_ms >= QUIT_FALLBACK_TIMEOUT_MS:
		_quitting = false
		_exit_to_screensaver = false  # 退出失敗扱い (次の遷移判定で誤ってスクリーンセーバーへ行かない)
		push_error("[GameSession] taskkill 後 %dms 経ってもプロセスが終了せず (PID %d)、終了処理を中断" % [QUIT_FALLBACK_TIMEOUT_MS, running_pid])
		game_quit_aborted.emit()

	# Companion ハンドシェイク待ち (起動直後にゲームを起動した race 対策): 完了で watch 開始、
	# タイムアウトで probe 無しの即 PLAYING にフォールバック。解決まで以降の probe 処理はスキップ。
	if _probe_pending:
		_resolve_pending_probe()
		return

	if not _probe_available:
		return

	var res := LauncherAgent.get_window_state()
	var game_visible := (res == LauncherAgent.WindowState.VISIBLE_BACKGROUND
		or res == LauncherAgent.WindowState.VISIBLE_FOREGROUND)

	if res == LauncherAgent.WindowState.UNAVAILABLE:
		if not _probe_failure_logged:
			_probe_failure_logged = true
			push_warning("[GameSession] LauncherAgent が UNAVAILABLE を返した (window event 未達 or Companion 異常)")
	elif _probe_failure_logged:
		_probe_failure_logged = false

	# 起動中 → プレイ中確定: 可視ウィンドウ検出 or タイムアウト。
	if not _playing_confirmed:
		if game_visible:
			_confirm_playing("Companion が可視ウィンドウを検出")
		elif Time.get_ticks_msec() - _launch_time_ms >= PLAYING_FALLBACK_TIMEOUT_MS:
			_confirm_playing("フォールバックタイムアウト (%dms)" % PLAYING_FALLBACK_TIMEOUT_MS)

	# 前面化異常監視の arm は「実際にゲーム窓を観測した」時のみ (スキャン中の誤発報防止)。
	if not _anomaly_armed and game_visible:
		_anomaly_armed = true
		print("[GameSession] ゲーム窓を確認、前面化異常監視を有効化")

	if _anomaly_armed:
		# 中断オーバーレイ表示中はランチャーが意図的に前面化するため異常カウントしない (#30/#216 whitelist)。
		if OverlayManager.is_open() or _quitting:
			_anomaly_since_ms = 0
			_anomaly_logged = false  # episode をリセット (オーバーレイ閉じ後に再発したら再ログできるように)
		else:
			_update_anomaly_detection(_launcher_is_foreground(), res)


## オーバーレイ「ゲームを再開」: ゲーム窓を前面に戻す。
func resume() -> void:
	if running_pid != -1 and _probe_available:
		LauncherAgent.focus(running_pid)


## 退出メニュー: 終了後スクリーンセーバーへ。フラグを立てて quit (game_exited 時に現シーンが判定)。
## 戻り値=終了処理を開始できたか (taskkill 起動失敗時は false)。失敗時はフラグを戻して誤遷移を防ぐ。
func request_exit_to_screensaver() -> bool:
	_exit_to_screensaver = true
	if quit():
		return true
	_exit_to_screensaver = false  # 失敗時は戻す (次の quit で誤ってスクリーンセーバーへ行かないように)
	return false


## 終了後にスクリーンセーバーへ遷移すべきか (現シーンが game_exited で参照)。
func should_exit_to_screensaver() -> bool:
	return _exit_to_screensaver


## ゲームプロセスツリーを終了する。kill 後、_process がプロセス消失を検出 → game_exited 発火。
## 戻り値=taskkill を起動できたか。false の場合ゲームは生存し game_exited は来ないため、呼び出し側
## (OverlayManager) は終了中 morph を巻き戻して操作可能状態へ復帰させること (overlay 固着防止)。
func quit() -> bool:
	if running_pid == -1:
		return false
	_quitting = true  # taskkill 完了 (プロセス消失) までは #216 異常検知を止める (意図的終了の過渡状態)
	# 現シーン (playing) に「終了中」表示を要求 (handoff の下地。前面化はしない — 中断メニュー窓が前面で
	# 終了中を出し、game_exited で overlay を隠してメイン窓へシームレスに引き継ぐ #214)。
	game_quitting.emit()
	print("[GameSession] ゲーム終了 (PID %d ツリーを taskkill)" % running_pid)
	# cmd.exe 経由起動のため running_pid は cmd。/T でツリー (game.exe 含む) ごと終了。
	var tk_pid := OS.create_process("taskkill", ["/PID", str(running_pid), "/T", "/F"])
	if tk_pid == -1:
		# taskkill 自体を起動できない (PATH/権限等)。このままだとゲームが死なず _quitting のまま固まり
		# #216 前面化異常検知も抑止され続ける。抑止を解除し (スタッフに異常を気づかせる) false を返して
		# 呼び出し側に morph 巻き戻しを委ねる (game_quitting は発火済だが、復帰時に再表示で上書きされる)。
		_quitting = false
		push_error("[GameSession] taskkill 起動失敗、ゲームを終了できませんでした (PID %d)" % running_pid)
		# game_quitting で現シーン (playing) が「終了中」表示にしたので、それを戻すよう通知する。
		game_quit_aborted.emit()
		return false
	_quit_started_ms = Time.get_ticks_msec()  # quit watchdog 起点 (これ以降プロセス消失を待つ)
	return true


func _on_exited() -> void:
	print("[GameSession] Game process %d finished." % running_pid)
	if _probe_available:
		LauncherAgent.unwatch()
	running_pid = -1
	current_game = null
	_quitting = false
	if _anomaly_active:
		_anomaly_active = false
		ErrorManager.hide_error(ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY)
	game_exited.emit()


func _confirm_playing(reason: String) -> void:
	_playing_confirmed = true
	_anomaly_since_ms = 0
	print("[GameSession] PLAYING 確定: %s" % reason)
	playing_confirmed.emit()


## ハンドシェイク待ちの解決: 完了したら watch 開始 (probe 有効化)、タイムアウトしたら probe 無しで即 PLAYING。
func _resolve_pending_probe() -> void:
	if LauncherAgent.is_available():
		_probe_pending = false
		_probe_available = true
		LauncherAgent.watch(running_pid, _launcher_monitor_rect())
		print("[GameSession] LauncherAgent ハンドシェイク完了、監視を開始")
	elif Time.get_ticks_msec() - _launch_time_ms >= PROBE_HANDSHAKE_TIMEOUT_MS:
		_probe_pending = false
		_probe_available = false
		_playing_confirmed = true
		push_warning("[GameSession] LauncherAgent ハンドシェイク未完了 (%dms 経過)、probe 無しで PLAYING 継続" % PROBE_HANDSHAKE_TIMEOUT_MS)
		playing_confirmed.emit()


func _reset_probe_state() -> void:
	_probe_pending = false
	_playing_confirmed = false
	_anomaly_armed = false
	_anomaly_since_ms = 0
	_anomaly_active = false
	_anomaly_logged = false
	_recovery_attempts = 0
	_last_recovery_ms = 0
	_probe_failure_logged = false
	_launch_time_ms = Time.get_ticks_msec()


## ランチャー本体 (メイン窓) が表示されているモニタの矩形 (OS 仮想デスクトップ座標)。
## B案 (#30 マルチモニタ): ゲーム窓をこのモニタへ寄せて 2 枚構成を同一モニタに揃える。
func _launcher_monitor_rect() -> Rect2i:
	var scr: int = get_tree().root.current_screen
	return Rect2i(DisplayServer.screen_get_position(scr), DisplayServer.screen_get_size(scr))


## ランチャー (メインウィンドウ) が前面か。前面でない (=ゲームが前面) が正常。
func _launcher_is_foreground() -> bool:
	var w := get_window()
	return w != null and w.has_focus()


## プレイ中の前面化異常を検知し、確定後はまず強制前面化で自己修復を試み、リトライを尽くしても復旧
## しなければスタッフ警告へエスカレーションする (#216 検知 + #219 自己修復)。
## 異常 = ランチャー前面 かつ ゲームが前面でない、がデバウンス継続。
func _update_anomaly_detection(launcher_foreground: bool, res: int) -> void:
	var game_not_front := (res == LauncherAgent.WindowState.NOT_VISIBLE
		or res == LauncherAgent.WindowState.VISIBLE_BACKGROUND)
	var anomaly := launcher_foreground and game_not_front

	if anomaly:
		var now := Time.get_ticks_msec()
		if _anomaly_since_ms == 0:
			_anomaly_since_ms = now
			_recovery_attempts = 0
			_last_recovery_ms = 0
		elif not _anomaly_active and now - _anomaly_since_ms >= ANOMALY_DEBOUNCE_MS:
			# 異常確定。#219: まずゲーム窓の強制前面化で自己修復を試み、リトライを尽くしても
			# background のままなら従来どおりスタッフ警告を出す (Windows の foreground-lock で
			# focus が効かないケースがあるため「トライ → 失敗時のみ警告」の二段構え)。
			if _recovery_attempts < ANOMALY_RECOVERY_MAX_ATTEMPTS:
				if _last_recovery_ms == 0 or now - _last_recovery_ms >= ANOMALY_RECOVERY_INTERVAL_MS:
					_recovery_attempts += 1
					_last_recovery_ms = now
					# 呼び出し元 (_process) が _probe_available 真を保証済み。
					LauncherAgent.focus(running_pid)
					# print 使用は意図的: autoload `Logger` は Godot 組み込み `Logger` クラスと名前衝突し
					# GDScript から `Logger.info()` を呼べない (#85 で明示 API 移行予定)。print は log tail で INFO 分類。
					print("[GameSession] 前面化異常 → ゲーム窓を強制前面化で自己修復を試行 (%d/%d)" % [_recovery_attempts, ANOMALY_RECOVERY_MAX_ATTEMPTS])
				# まだ警告は出さない (次 probe で復旧したか確認。復旧すれば else 分岐でリセット)。
			elif now - _last_recovery_ms >= ANOMALY_RECOVERY_INTERVAL_MS:
				# 全リトライ後も INTERVAL 経過して復旧せず (最終試行にも検証猶予を与えてから警告)。
				if not _anomaly_logged:
					_anomaly_logged = true
					push_warning("[GameSession] ランチャー前面化異常を検出、自己修復に失敗 (PID %d 生存中、要スタッフ対応)" % running_pid)
				_anomaly_active = ErrorManager.show_error(ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY)
	else:
		_anomaly_since_ms = 0
		_anomaly_logged = false
		_recovery_attempts = 0
		_last_recovery_ms = 0
		if _anomaly_active:
			_anomaly_active = false
			print("[GameSession] ランチャー前面化異常が解消、エラーをクリア")
			ErrorManager.hide_error(ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY)
