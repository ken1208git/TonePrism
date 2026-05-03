extends RefCounted
class_name Version

## ランチャーのバージョン情報
## マイルストーン5: StoreBrowse画面（UI改善・レスポンス向上）

const MAJOR: int = 0
const MINOR: int = 5
const PATCH: int = 14

static func get_version_string() -> String:
	return "v%d.%d.%d" % [MAJOR, MINOR, PATCH]

static func get_version_number() -> String:
	return "%d.%d.%d" % [MAJOR, MINOR, PATCH]
