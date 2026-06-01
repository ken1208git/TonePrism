extends RefCounted
class_name Version

## ランチャー版数のアクセサ (#281)。
##
## 版数の SoT は `Launcher/project.godot` の `[application] config/version="X.Y.Z"` **1 箇所**。
## 本ファイルは数字を持たず、実行時に ProjectSettings からその値を読むだけの薄いラッパ。
## (以前は version.gd の `const MAJOR/MINOR/PATCH` と project.godot config/version の二重持ちで、
##  開発バンプのたびに手で両方を合わせる必要があった。SoT を project.godot に一本化して解消した。)
##
## - **Launcher 実行時**: `ProjectSettings.get_setting("application/config/version")` で取得。
##   config/version は Godot export 時に PCK へ焼き込まれるため、配布版でも参照できる。
## - **Manager (ファイル parse 側)**: Godot を実行できないので `project.godot` の
##   `config/version="X.Y.Z"` 行を直接 regex parse する (`VersionInventory.ReadLauncherVersion`)。
##   → version.gd はもう Manager の parse 対象ではない (= 旧「DO NOT CHANGE FORMAT」制約は撤廃)。
##
## consumer (`debug_overlay` / `service_mode_overlay` / `session_heartbeat`) は本 API
## (`get_version_string` / `get_version_number`) を呼ぶだけなので無改修。

const _VERSION_SETTING := "application/config/version"
const _FALLBACK := "0.0.0"  # config/version 未設定/空のときの sentinel (UI に "v0.0.0" と出れば異常を疑える)

# project.godot config/version を読む。未設定/空なら _FALLBACK。
static func _read_config_version() -> String:
	var raw = ProjectSettings.get_setting(_VERSION_SETTING, "")
	var s := str(raw).strip_edges()
	return s if s != "" else _FALLBACK

static func get_version_string() -> String:
	return "v" + _read_config_version()

static func get_version_number() -> String:
	return _read_config_version()
