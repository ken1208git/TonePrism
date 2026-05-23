extends Node
## Autoload: 常駐 Companion (TonePrism_LauncherCompanion.exe) を起動・管理し、双方向 localhost UDP で
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
## 出す → logger.gd の godot.log テールが拾って launcher ログに [LauncherCompanion] 付きで記録 →
## Manager のログ閲覧「Launcher タブ」に出る (Manager 改修不要)。

signal trigger_received(source: String)  ## "home" | "guide"
signal capture_ready(path: String, ok: bool)  ## capture コマンドの結果 (中断オーバーレイのすりガラス背景用)

enum WindowState { UNAVAILABLE, NOT_FOUND, NOT_VISIBLE, VISIBLE_BACKGROUND, VISIBLE_FOREGROUND }

const _EXE_CANDIDATES: Array[String] = [
	"Companions/LauncherCompanion/TonePrism_LauncherCompanion.exe",
	"Companions/LauncherCompanion/bin/Release/TonePrism_LauncherCompanion.exe",
	"Companions/LauncherCompanion/bin/Debug/TonePrism_LauncherCompanion.exe",
]

var _proc_pid: int = -1
var _event_peer: PacketPeerUDP = null
var _cmd_peer: PacketPeerUDP = null
var _cmd_port: int = 0
var _handshaked: bool = false
var _exe_ok: bool = false
var _window_state: int = WindowState.UNAVAILABLE
var _last_trigger_seq: int = 0


func _ready() -> void:
	_start_companion()


func _start_companion() -> void:
	var exe := _get_exe_path()
	if not FileAccess.file_exists(exe):
		print("[LauncherCompanion] exe 不在のため probe/overlay 無効 (従来挙動): ", exe)
		return
	_exe_ok = true

	_event_peer = PacketPeerUDP.new()
	var err := _event_peer.bind(0, "127.0.0.1")
	if err != OK:
		push_warning("[LauncherCompanion] event UDP bind 失敗 (probe/overlay 無効): err=%d" % err)
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
		push_warning("[LauncherCompanion] Companion 起動失敗 (probe/overlay 無効)")
		_exe_ok = false
		return
	print("[LauncherCompanion] 常駐起動 pid=%d event-port=%d" % [_proc_pid, event_port])


func _process(_delta: float) -> void:
	if _event_peer == null:
		return
	while _event_peer.get_available_packet_count() > 0:
		var txt := _event_peer.get_packet().get_string_from_utf8()
		_handle_event(txt)


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
					print("[LauncherCompanion] handshake 完了 cmd-port=%d" % _cmd_port)
		"window":
			_window_state = _map_state(String(data.get("state", "")))
		"trigger":
			var seq := int(data.get("seq", 0))
			if seq > _last_trigger_seq:  # 連送/取りこぼしを seq で吸収
				_last_trigger_seq = seq
				trigger_received.emit(String(data.get("event", "")))
		"captured":
			capture_ready.emit(String(data.get("path", "")), bool(data.get("ok", false)))
		"log":
			_forward_log(String(data.get("level", "info")), String(data.get("msg", "")))


func _forward_log(level: String, msg: String) -> void:
	# logger.gd の godot.log テールがレベル分類して launcher ログに転送する。
	var line := "[LauncherCompanion] " + msg
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


## 最新の窓状態 (WindowState)。
func get_window_state() -> int:
	return _window_state


## 指定ゲーム PID の窓状態監視 + HOME/Guide 検知を開始。
func watch(pid: int) -> void:
	_window_state = WindowState.UNAVAILABLE  # 次の window イベントまで未確定
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


## 指定 HWND の最前面 (topmost) フラグを on/off。中断オーバーレイ表示で、フルスクリーンの
## メイン窓を即座にゲーム窓の上へ出す用 (SetForegroundWindow の foreground-lock 遅延回避)。
func set_topmost(hwnd: int, on: bool) -> void:
	_send("topmost %d %d" % [hwnd, 1 if on else 0])


## 指定スクリーン rect (ランチャーのある画面の物理座標) を path に PNG キャプチャ要求。結果は capture_ready で返る。
func capture(x: int, y: int, w: int, h: int, path: String) -> void:
	_send("capture %d %d %d %d %s" % [x, y, w, h, path])


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
		_send("quit")
		if _proc_pid != -1 and OS.is_process_running(_proc_pid):
			OS.kill(_proc_pid)
