extends RefCounted
class_name Version

## ランチャーのバージョン情報
## マイルストーン4: データベース連携

const MAJOR: int = 0
const MINOR: int = 4
const PATCH: int = 6

static func get_version_string() -> String:
	return "v%d.%d.%d" % [MAJOR, MINOR, PATCH]

static func get_version_number() -> String:
	return "%d.%d.%d" % [MAJOR, MINOR, PATCH]
