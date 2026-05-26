extends Node
## Autoload: 常駐 Companion (TonePrism_LauncherAgent.exe) を起動・管理し、双方向 localhost UDP で
## - 窓状態 (起動中→プレイ中 #101 / 前面化異常 #216 の駆動材料) を受信
## - HOME / Guide トリガ (オーバーレイ開閉 #30) を受信し signal で通知
## - watch / unwatch / focus コマンドを送信
## する。旧 WindowProbe (単発 OS.execute) を置換。
##
## Godot 4 は子プロセスの stdout を逐次読めないため UDP を使う。Companion → Launcher の通知は本 autoload が
## 毎フレーム drain する。ハンドシェイク: 起動時 Launcher が event 受信ポート L を bind して --event-port L で
## Companion を起動 → Companion が cmd 受信ポート C を自動 bind し hello イベントで C を通知 → 以後 Launcher は
## 127.0.0.1:C へ cmd を送る。
##
## ログ転送: Companion の WARN/ERROR/主要イベントは log イベントで届くので、print/push_warning/push_error で
## 出す → logger.gd の godot.log テールが拾って launcher ログに [LauncherAgent] 付きで記録 →
## Manager のログ閲覧「Launcher タブ」に出る (Manager 改修不要)。

signal trigger_received(source: String)  ## "home" | "guide"
signal speedtest_result(kind: String, ok: bool, text: String)  ## "internet" | "server" の速度計測結果

enum WindowState { UNAVAILABLE, NOT_FOUND, NOT_VISIBLE, VISIBLE_BACKGROUND, VISIBLE_FOREGROUND }

const _EXE_CANDIDATES: Array[String] = [
	"Companions/LauncherAgent/TonePrism_LauncherAgent.exe",
	"Companions/LauncherAgent/bin/Release/TonePrism_LauncherAgent.exe",
	"Companions/LauncherAgent/bin/Debug/TonePrism_LauncherAgent.exe",
]

const LIVENESS_CHECK_MS := 1000  # companion 死活確認の間隔 (毎フレーム syscall を避ける)

var _proc_pid: int = -1
var _last_liveness_ms: int = 0
var _event_peer: PacketPeerUDP = null
var _cmd_peer: PacketPeerUDP = null
var _cmd_port: int = 0
var _handshaked: bool = false
var _exe_ok: bool = false
var _window_state: int = WindowState.UNAVAILABLE
var _game_window_rect: Rect2i = Rect2i()  # 代表ゲーム窓の画面矩形 (OS 仮想デスクトップ座標)。overlay のモニタ決定用。
var _last_trigger_seq: int = 0


func _ready() -> void:
	# tree.paused (ダイアログ / サービスモード等) でも Companion からの window/trigger イベントを
	# 受信し続ける。これがないと paused 中に窓状態が更新されず、サービスモードの起動テストで
	# 「窓は出ているのに検出できず NG」になる (GameSession も監視継続のため ALWAYS)。
	process_mode = Node.PROCESS_MODE_ALWAYS
	_start_companion()


func _start_companion() -> void:
	var exe := _get_exe_path()
	if not FileAccess.file_exists(exe):
		print("[LauncherAgent] exe 不在のため probe/overlay 無効 (従来挙動): ", exe)
		return
	_exe_ok = true

	_event_peer = PacketPeerUDP.new()
	var err := _event_peer.bind(0, "127.0.0.1")
	if err != OK:
		push_warning("[LauncherAgent] event UDP bind 失敗 (probe/overlay 無効): err=%d" % err)
		_event_peer = null
		_exe_ok = false
		return
	var event_port := _event_peer.get_local_port()

	var logs_root := PathManager.get_base_directory().path_join("logs")
	var args := PackedStringArray([
		"--event-port", str(event_port),
		"--parent-pid", str(OS.get_process_id()),
		"--logs-root", logs_root,
	])
	# open_console=false (既定) で常駐コンソール窓を出さない (WindowProbe の OS.execute 同様)。
	_proc_pid = OS.create_process(exe, args)
	if _proc_pid == -1:
		push_warning("[LauncherAgent] Companion 起動失敗 (probe/overlay 無効)")
		_exe_ok = false
		return
	print("[LauncherAgent] 常駐起動 pid=%d event-port=%d" % [_proc_pid, event_port])


func _process(_delta: float) -> void:
	if _event_peer == null:
		return
	while _event_peer.get_available_packet_count() > 0:
		var txt := _event_peer.get_packet().get_string_from_utf8()
		_handle_event(txt)
	_check_companion_liveness()


## Companion プロセスの死活を定期確認し、消失していたら probe/overlay を無効化する。
## これがないと crash/kill 後も is_available() が true を返し続け、GameSession が死んだ peer に watch を送って
## window イベントを待ち続ける (HOME/Guide と前面化異常監視がそのセッション無効になる)。
func _check_companion_liveness() -> void:
	if not _exe_ok or _proc_pid == -1:
		return
	var now := Time.get_ticks_msec()
	if now - _last_liveness_ms < LIVENESS_CHECK_MS:
		return
	_last_liveness_ms = now
	if not OS.is_process_running(_proc_pid):
		push_warning("[LauncherAgent] Companion プロセス消失を検知、probe/overlay を無効化 (is_available=false)")
		_exe_ok = false
		_handshaked = false
		_cmd_peer = null
		_window_state = WindowState.UNAVAILABLE


func _handle_event(txt: String) -> void:
	var data = JSON.parse_string(txt)
	if typeof(data) != TYPE_DICTIONARY:
		return
	match data.get("type", ""):
		"hello":
			# hello は取りこぼし対策で連送されるので、初回のみ処理する。
			if not _handshaked:
				_cmd_port = int(data.get("cmd_port", 0))
				if _cmd_port > 0:
					_cmd_peer = PacketPeerUDP.new()
					_cmd_peer.set_dest_address("127.0.0.1", _cmd_port)
					_handshaked = true
					print("[LauncherAgent] handshake 完了 cmd-port=%d" % _cmd_port)
		"window":
			_window_state = _map_state(String(data.get("state", "")))
			var w := int(data.get("w", 0))
			var h := int(data.get("h", 0))
			if w > 0 and h > 0:
				_game_window_rect = Rect2i(int(data.get("x", 0)), int(data.get("y", 0)), w, h)
			else:
				_game_window_rect = Rect2i()  # 窓消失 (not_visible/not_found) 時は stale 矩形を残さない
		"trigger":
			var seq := int(data.get("seq", 0))
			if seq > _last_trigger_seq:  # 連送/取りこぼしを seq で吸収
				_last_trigger_seq = seq
				trigger_received.emit(String(data.get("event", "")))
		"log":
			_forward_log(String(data.get("level", "info")), String(data.get("msg", "")))
		"speedtest":
			speedtest_result.emit(String(data.get("kind", "")), bool(data.get("ok", false)), String(data.get("text", "")))


func _forward_log(level: String, msg: String) -> void:
	# logger.gd の godot.log テールがレベル分類して launcher ログに転送する。
	var line := "[LauncherAgent] " + msg
	match level:
		"error": push_error(line)
		"warn": push_warning(line)
		_: print(line)


func _map_state(s: String) -> int:
	match s:
		"visible_foreground": return WindowState.VISIBLE_FOREGROUND
		"visible_background": return WindowState.VISIBLE_BACKGROUND
		"not_visible": return WindowState.NOT_VISIBLE
		"not_found": return WindowState.NOT_FOUND
		_: return WindowState.UNAVAILABLE


# ---- 公開 API (game_launcher / OverlayManager から) ----

## probe / overlay が使えるか (exe 起動済 + ハンドシェイク完了)。
func is_available() -> bool:
	return _exe_ok and _handshaked


## exe を起動済みで、ハンドシェイク完了が今後見込めるか (= is_available() がいずれ true になり得る)。
## エディタ実行 / exe 未同梱 (_exe_ok=false) ではハンドシェイクは来ないため false。
## 起動直後にゲームを起動した場合 (handshake 未完了) の待ち判定に使う。
func is_expected() -> bool:
	return _exe_ok


## 最新の窓状態 (WindowState)。
func get_window_state() -> int:
	return _window_state


## 最新の代表ゲーム窓の画面矩形 (OS 仮想デスクトップ座標)。未取得なら size 0 の Rect2i。
## マルチモニタで中断オーバーレイをゲーム窓のいるモニタに出すために使う。
func get_game_window_rect() -> Rect2i:
	return _game_window_rect


## 指定ゲーム PID の窓状態監視 + HOME/Guide 検知を開始。
## place_rect 指定時は、最初の可視窓をそのモニタ中央へ 1 回だけ寄せる (#30 B案 マルチモニタ: ゲームが
## 別モニタに開いてもランチャーと同じモニタに揃え、2 枚構成の隙間漏れを防ぐ)。本番=単一モニタでは無害。
func watch(pid: int, place_rect: Rect2i = Rect2i()) -> void:
	_window_state = WindowState.UNAVAILABLE  # 次の window イベントまで未確定
	_game_window_rect = Rect2i()  # 前セッションの矩形を持ち越さない (古いモニタに overlay が出るのを防ぐ)
	if place_rect.size.x > 0 and place_rect.size.y > 0:
		_send("watch %d %d %d %d %d" % [pid, place_rect.position.x, place_rect.position.y, place_rect.size.x, place_rect.size.y])
	else:
		_send("watch %d" % pid)


## 監視・検知を停止 (常駐は維持)。
func unwatch() -> void:
	_window_state = WindowState.UNAVAILABLE
	_send("unwatch")


## 指定 PID ツリーの窓を強制前面化。
func focus(pid: int) -> void:
	_send("focus %d" % pid)


## 指定 HWND の窓だけを強制前面化 (overlay 窓用、メインのランチャー窓を巻き込まない)。
func focus_hwnd(hwnd: int) -> void:
	_send("focus_hwnd %d" % hwnd)


## 速度計測を要求する (結果は speedtest_result シグナルで返る)。share_path=共有上の計測対象ファイル。
func request_speedtest(share_path: String) -> void:
	_send("speedtest " + share_path)


func _send(cmd: String) -> void:
	if _cmd_peer == null:
		return
	_cmd_peer.put_packet(cmd.to_utf8_buffer())


func _get_exe_path() -> String:
	var base := PathManager.get_base_directory()
	for rel in _EXE_CANDIDATES:
		var p := base.path_join(rel)
		if FileAccess.file_exists(p):
			return p
	return base.path_join(_EXE_CANDIDATES[0])


func _notification(what: int) -> void:
	if what == NOTIFICATION_PREDELETE:
		# 後始末は graceful な quit コマンド送信のみ。OS.kill での強制終了はしない:
		# _proc_pid は起動時に捕捉した値で、Companion が先に死んで OS に PID が再利用されると
		# is_process_running が無関係なプロセスを true と判定し、それを kill してしまうため。
		# Companion は --parent-pid で Launcher 消失を 1 秒以内に検知し self-exit するため、
		# quit が取りこぼされても確実に終了する (二重の後始末経路)。
		_send("quit")
