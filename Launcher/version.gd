extends RefCounted
class_name Version

## ランチャーのバージョン情報
## マイルストーン5: StoreBrowse画面（UI改善・レスポンス向上）
##
## DO NOT CHANGE FORMAT: Manager parses these 3 const lines (#108 Phase 4 #161 round 5 M-5)
## `Manager/Services/VersionInventory.cs` の MajorRegex / MinorRegex / PatchRegex が
## `^\s*const\s+(MAJOR|MINOR|PATCH)\s*:\s*int\s*=\s*(\d+)` に literal match する。
## 型注釈削除 / rename / line 分割で Manager UI の Launcher 版数表示が「不明」に化けるため、
## 形式変更時は SPEC §3.7.8 チェックリストに従って Manager 側 regex も同期更新すること。

const MAJOR: int = 0
const MINOR: int = 6
const PATCH: int = 0

static func get_version_string() -> String:
	return "v%d.%d.%d" % [MAJOR, MINOR, PATCH]

static func get_version_number() -> String:
	return "%d.%d.%d" % [MAJOR, MINOR, PATCH]
