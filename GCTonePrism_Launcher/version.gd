extends RefCounted
class_name Version

## ランチャーのバージョン情報
## マイルストーン3: 基本画面・画面遷移

const MAJOR: int = 0
const MINOR: int = 2
const PATCH: int = 0

static func get_version_string() -> String:
	return "v%d.%d.%d" % [MAJOR, MINOR, PATCH]

static func get_version_number() -> String:
	return "%d.%d.%d" % [MAJOR, MINOR, PATCH]
