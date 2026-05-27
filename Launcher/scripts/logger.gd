## Launcher のファイルログ機構 (#116, #85 の土台)
##
## - 1 起動セッション = 1 ファイル (`launcher_<PCname>_<YYYY-MM-DD_HHmmss>.log`)
## - 保存先: default `<project_root>/logs/launcher/`、
##   Manager 側で `logs_root_path` setting が設定されていれば `<custom_root>/launcher/` を使用
##   (= #201, v0.15.0 unified logs root)
##   Manager → Launcher への path 伝搬は `<project_root>/responses/launcher_logs_root.json`
##   atomic write file 経由。autoload 最先頭 init 時に DB 接続前で file read のみで完結
##   (= SPEC §6.5 「Launcher は SQLite write しない」原則維持)
##   → 共有上の toneprism.db と同じ場所に集約することで、複数 PC の Launcher / Manager ログを 1 箇所で見られる
##   → セッション単位でファイルが分かれるので書き込み競合・行間 interleaving が発生しない
## - INFO / WARN / ERROR の 3 段階
## - 既存の print / printerr / push_warning / push_error / Godot エンジン内部エラーを **すべて** キャプチャ:
##   Godot 標準ファイルログ (user://logs/godot.log) を 0.5 秒間隔でテールして新規分を本ログに転送
##   (`OS.add_logger` で Logger 継承する案は GDScript パーサーが built-in Logger 型変換を蹴るため断念)
## - 起動時に 30 日より古いログファイルを削除 (mtime 基準)
## - スレッドセーフ (Mutex)
## - 自身の障害で Launcher 起動を止めない (失敗時はファイルログを諦めて Launcher は継続)
##
## autoload なので [autoload] の **先頭** に登録すること
## (Godot 標準ログのテール開始タイミングを早めて、他 autoload の出力をできるだけ早く捕捉するため)
##
## 関連: 将来 #85 (Launcher 統一ログ基盤フル仕様) で DEBUG 追加 / [エラーコード] フィールド /
## リングバッファ / サービスモード (#74) 連携が拡張される。本スクリプトはその土台。

extends Node

const RETENTION_DAYS: int = 30
const FILE_PREFIX: String = "launcher_"
const FILE_SUFFIX: String = ".log"
const LOG_SUBDIRECTORY: String = "launcher"
const GODOT_LOG_POLL_INTERVAL: float = 0.5  # Godot 標準ログのテール間隔 (秒)

var _file: FileAccess = null
var _current_log_path: String = ""
var _log_directory: String = ""
var _mutex: Mutex = Mutex.new()
var _initialized: bool = false
var _init_failed: bool = false

# サービスモード「簡易ログ確認」用に直近ログ行をメモリ保持するリングバッファ (現セッションのみ・ファイル非依存)。
const RECENT_MAX: int = 400
var _recent: Array[String] = []

# Godot 標準ログ (user://logs/godot.log) テール用
var _godot_log_path: String = ""
var _godot_log_pos: int = 0
var _sync_timer: Timer = null

# (#201, v0.15.0, R5 review Low-1) bridge file 読込時の warn message を一時保持。
# `_read_logs_root_from_responses` は `_open_session_file` より前 + `_init_godot_log_tail` の baseline
# 記録前に呼ばれるため、push_warning だけだと godot.log baseline に飲まれて session log に転送されない。
# warn を本 field に貯め、`_open_session_file` 完了後に `_write_safely("WARN", ...)` で session log に直書きする。
var _logs_root_warn_msg: String = ""


func _init() -> void:
	# autoload の _init は engine 起動の極めて早い段階で呼ばれる
	# ここでファイルを開いておくことで、他の autoload の _ready ログも遅滞なくキャプチャ準備が整う
	_initialize_logger()


func _ready() -> void:
	# Godot 標準ログのテール開始 (Timer は add_child が必要なので _ready で)
	if _initialized:
		_init_godot_log_tail()


func _initialize_logger() -> void:
	if _initialized or _init_failed:
		return

	if not _open_log_directory_and_file():
		_init_failed = true
		push_warning("[Logger] ログ機構の初期化に失敗。Launcher は動作しますがファイルログは記録されません。")
		return

	_initialized = true
	_write_safely("INFO", "[Logger] Launcher 起動 (PC=" + _get_pc_name() + ")")
	# (#201, R5 review Low-1) bridge file 読込で warn が出ていれば session log に直書き
	# (= push_warning は godot.log baseline に飲まれるため、session log には別途転送)。
	if not _logs_root_warn_msg.is_empty():
		_write_safely("WARN", _logs_root_warn_msg)
		_logs_root_warn_msg = ""
	_cleanup_old_logs()


func _open_log_directory_and_file() -> bool:
	var project_root = _find_project_root_for_logs()
	if project_root.is_empty():
		return false

	# (#201, v0.15.0 unified logs root) Manager 側 setting `logs_root_path` を
	# `<project_root>/responses/launcher_logs_root.json` 経由で受取。
	# parse 成功 + path 非空 → `<custom_root>/launcher/`、それ以外は default `<project_root>/logs/launcher/`。
	# JSON 不在 / parse 失敗 / path 空 はすべて default fallback、Launcher 起動阻害しない。
	# 戻り値は [path: String, warn_msg: String]。warn は session log open 後に _initialize_logger が flush。
	var read_result = _read_logs_root_from_responses(project_root)
	var logs_root = read_result[0] as String
	_logs_root_warn_msg = read_result[1] as String
	if logs_root.is_empty():
		_log_directory = project_root.path_join("logs").path_join(LOG_SUBDIRECTORY)
	else:
		_log_directory = logs_root.path_join(LOG_SUBDIRECTORY)

	if not DirAccess.dir_exists_absolute(_log_directory):
		var err = DirAccess.make_dir_recursive_absolute(_log_directory)
		if err != OK:
			return false

	return _open_session_file()


# (#201, v0.15.0) Manager → Launcher 設定伝搬 file を読込んで logs root path を返す。
# 失敗時 (file 不在 / parse 失敗 / path 空) はすべて空 path を返し、caller が default fallback する。
# DB 接続前の autoload 最先頭 init で呼ばれる前提、file read のみで完結。
#
# 戻り値: `[path: String, warn_msg: String]` (= R5 review Low-1)。
#   - path: 解決した logs root (空 = default 使用)
#   - warn_msg: 異常検出時の WARN message (空 = 正常)。caller (`_open_log_directory_and_file`) が
#     field に保持し、`_initialize_logger` が session log open 後に `_write_safely` で flush する
#     (= push_warning だけだと godot.log baseline に飲まれて session log に転送されないため)。
#   file 不在 (= 通常 path、Manager 未起動 / setting default) は warn なしの正常 fallback。
func _read_logs_root_from_responses(project_root: String) -> Array:
	var responses_dir = project_root.path_join("responses")
	var bridge_path = responses_dir.path_join("launcher_logs_root.json")
	if not FileAccess.file_exists(bridge_path):
		return ["", ""]  # file 不在 = 正常 (default 使用)、warn なし

	var f = FileAccess.open(bridge_path, FileAccess.READ)
	if f == null:
		var msg = "[Logger] launcher_logs_root.json open 失敗、default 使用"
		push_warning(msg)
		return ["", msg]

	var content = f.get_as_text()
	f.close()

	var json = JSON.new()
	var parse_result = json.parse(content)
	if parse_result != OK:
		var msg = "[Logger] launcher_logs_root.json parse 失敗 (line=%d msg=%s)、default 使用" % [json.get_error_line(), json.get_error_message()]
		push_warning(msg)
		return ["", msg]

	var data = json.data
	if typeof(data) != TYPE_DICTIONARY:
		var msg = "[Logger] launcher_logs_root.json 内容が dictionary でない、default 使用"
		push_warning(msg)
		return ["", msg]

	var value = data.get("logs_root_path", "")
	if typeof(value) != TYPE_STRING or (value as String).is_empty():
		return ["", ""]  # path 空 = default 使用 (Manager が default 設定中)、warn なし

	# (#201, R6 review Low-2) 絶対 path invariant の defense-in-depth。Manager 側 (ApplyLogSection
	# + migration) で絶対 path を enforce 済だが、手動 file 編集 / 旧 install の相対値残置に備えて Launcher
	# 側でも検証。相対 path を path_join(LOG_SUBDIRECTORY) に渡すと CWD 相対解決で起動毎に場所が変わる
	# silent hazard になるため、絶対 path でなければ warn + default fallback。
	var path_str = value as String
	if not path_str.is_absolute_path():
		var msg = "[Logger] launcher_logs_root.json の logs_root_path が絶対 path でない (\"%s\")、default 使用" % path_str
		push_warning(msg)
		return ["", msg]
	return [path_str, ""]


# 1 セッション 1 ファイル: launcher_<PCname>_<YYYY-MM-DD_HHmmss>.log
# 万一同秒で衝突した場合は連番サフィックスでリトライ
func _open_session_file() -> bool:
	var pc_name = _sanitize_filename(_get_pc_name())
	var dt = Time.get_datetime_dict_from_system()
	var start_ts = "%04d-%02d-%02d_%02d%02d%02d" % [dt.year, dt.month, dt.day, dt.hour, dt.minute, dt.second]
	var base_name = "%s%s_%s" % [FILE_PREFIX, pc_name, start_ts]
	var path = _log_directory.path_join(base_name + FILE_SUFFIX)

	var counter = 2
	while FileAccess.file_exists(path) and counter < 100:
		path = _log_directory.path_join("%s_%d%s" % [base_name, counter, FILE_SUFFIX])
		counter += 1

	# 新規ファイルとして作成
	_file = FileAccess.open(path, FileAccess.WRITE)
	if _file == null:
		return false

	_current_log_path = path
	return true


# Godot 標準ログをテールする準備:
# 既存サイズを記録して過去内容のリプレイを防ぎつつ、Timer で新規分の polling を開始
func _init_godot_log_tail() -> void:
	var raw_path = ProjectSettings.get_setting("debug/file_logging/log_path", "user://logs/godot.log")
	_godot_log_path = ProjectSettings.globalize_path(raw_path)

	# 既存内容のサイズを記録 (このセッション開始前に書かれたものはリプレイしない)
	if FileAccess.file_exists(_godot_log_path):
		var f = FileAccess.open(_godot_log_path, FileAccess.READ)
		if f != null:
			_godot_log_pos = f.get_length()
			f.close()
	else:
		_godot_log_pos = 0  # まだ無い場合は Godot が作ってから 0 から読む

	_sync_timer = Timer.new()
	_sync_timer.wait_time = GODOT_LOG_POLL_INTERVAL
	_sync_timer.autostart = true
	_sync_timer.one_shot = false
	_sync_timer.timeout.connect(_sync_godot_log)
	add_child(_sync_timer)


# Godot 標準ログを定期的にテール: 新規追加分を本セッションファイルに転送
func _sync_godot_log() -> void:
	if not _initialized or _godot_log_path.is_empty():
		return
	if not FileAccess.file_exists(_godot_log_path):
		return

	var f = FileAccess.open(_godot_log_path, FileAccess.READ)
	if f == null:
		return
	var size = f.get_length()

	# Godot 側がローテートして新ファイルになった場合 (size が縮んだ) は位置をリセット
	if size < _godot_log_pos:
		_godot_log_pos = 0

	if size <= _godot_log_pos:
		f.close()
		return

	f.seek(_godot_log_pos)
	var bytes_to_read = size - _godot_log_pos
	var data = f.get_buffer(bytes_to_read).get_string_from_utf8()
	_godot_log_pos = size
	f.close()

	# 各行をレベル判定して本ログに書く
	for line in data.split("\n"):
		var trimmed = line.strip_edges()
		if trimmed.is_empty():
			continue
		var level = _classify_godot_line(trimmed)
		_write_safely(level, "[Godot] " + trimmed)


# Godot 標準ログ行のレベル判定:
# WARNING:, USER WARNING: → WARN
# ERROR:, SCRIPT ERROR:, USER ERROR: → ERROR
# その他 (print の出力など) → INFO
func _classify_godot_line(line: String) -> String:
	if line.begins_with("WARNING:") or line.begins_with("USER WARNING:"):
		return "WARN"
	if line.begins_with("ERROR:") or line.begins_with("SCRIPT ERROR:") or line.begins_with("USER ERROR:"):
		return "ERROR"
	return "INFO"


# PathManager と同じく exe ベースで上に toneprism.db を探すが、
# Logger は他の autoload より先に動くため軽量実装で重複させる
# (print 出力なし、エラーなし、見つからなければ exe 隣にフォールバックして Logger 自体は動かす)
func _find_project_root_for_logs() -> String:
	var base_dir: String
	if OS.has_feature("editor"):
		base_dir = ProjectSettings.globalize_path("res://")
	else:
		base_dir = OS.get_executable_path().get_base_dir()

	if base_dir.is_empty():
		return ""

	base_dir = base_dir.replace("\\", "/").rstrip("/")

	var current = base_dir
	for i in 10:
		if FileAccess.file_exists(current.path_join("toneprism.db")):
			return current
		var parent = current.get_base_dir()
		if parent == current or parent.is_empty():
			break
		current = parent

	# 見つからなければ exe 隣にフォールバック (Logger 自体は動く)
	return base_dir


func _get_pc_name() -> String:
	var name = OS.get_environment("COMPUTERNAME")
	if name.is_empty():
		name = OS.get_environment("HOSTNAME")
	if name.is_empty():
		name = "unknown"
	return name


func _sanitize_filename(name: String) -> String:
	# Windows で禁止される文字を _ に置換
	var invalid = ["/", "\\", ":", "*", "?", "\"", "<", ">", "|"]
	var result = name
	for c in invalid:
		result = result.replace(c, "_")
	return result


# 公開 API: 新規コードからレベル明示で呼ぶ
func info(message: String) -> void:
	_write_safely("INFO", message)


func warn(message: String) -> void:
	_write_safely("WARN", message)


func error(message: String) -> void:
	_write_safely("ERROR", message)


# 内部書き込み: ロック取得 → ファイル書き込み (ローテートなし、1 セッション 1 ファイル)
# Logger 自身の障害で連鎖しないよう、書き込み失敗は静かに無視
func _write_safely(level: String, message: String) -> void:
	if _init_failed:
		return
	var ts = Time.get_datetime_string_from_system(false, true)
	var line := "[%s] [%s] %s" % [ts, level, message]
	_mutex.lock()
	if _file != null:
		_file.store_line(line)
		_file.flush()
	# サービスモード「簡易ログ確認」用にメモリ保持 (上限超過は古い行から捨てる)。
	_recent.append(line)
	if _recent.size() > RECENT_MAX:
		_recent = _recent.slice(_recent.size() - RECENT_MAX)
	_mutex.unlock()


## サービスモード「簡易ログ確認」用: 現セッションの直近ログ行 (古い順) のコピーを返す。
func get_recent_logs() -> Array[String]:
	_mutex.lock()
	var copy: Array[String] = _recent.duplicate()
	_mutex.unlock()
	return copy


# 起動時に 30 日より古いログファイルを削除
# 失敗時 (権限・ロック等) は静かにスキップ
func _cleanup_old_logs() -> void:
	var dir = DirAccess.open(_log_directory)
	if dir == null:
		return

	var cutoff_unix: int = int(Time.get_unix_time_from_system()) - (RETENTION_DAYS * 86400)

	dir.list_dir_begin()
	var name = dir.get_next()
	while name != "":
		if not dir.current_is_dir() and name.begins_with(FILE_PREFIX) and name.ends_with(FILE_SUFFIX):
			var full_path: String = _log_directory.path_join(name)
			# 念のため: 今セッションのアクティブファイルは絶対に消さない
			if full_path != _current_log_path:
				var mtime: int = int(FileAccess.get_modified_time(full_path))
				if mtime > 0 and mtime < cutoff_unix:
					DirAccess.remove_absolute(full_path)  # 失敗は無視
		name = dir.get_next()
	dir.list_dir_end()


func _notification(what: int) -> void:
	# Codex P1 #128: ファイル close は本物のプロセス終了 (PREDELETE) でのみ実施。
	# WM_CLOSE_REQUEST は AppManager.set_auto_accept_quit(false) でインターセプトされて
	# キャンセルされうるため、ここで _file を閉じてしまうと「キャンセルされた Alt+F4」
	# 1 回でセッション残り全部のログが消失する。WM_CLOSE_REQUEST 時点では何もしない。
	#
	# また project.godot の application/run/flush_stdout_on_print=true により
	# Godot は print() 毎に stdout を即 flush する → PREDELETE 時点で godot.log に
	# 全 print 内容が揃っているので _sync_godot_log() で取りこぼし無し。
	if what == NOTIFICATION_PREDELETE:
		if _initialized:
			# 終了直前に最後の Godot ログ分も同期 (バッファ済みの print も拾う)
			_sync_godot_log()
			_write_safely("INFO", "[Logger] Launcher 終了")
			if _file != null:
				_file.close()
				_file = null
