## ゲーム関連のパス解決を担当
## 実行ファイルパス、リソースパス、引数パースを提供

extends RefCounted
class_name GamePathResolver

## リソースパスを解決する（サムネイル、背景画像等）
static func resolve_path(path: String, game_id: String) -> String:
	if path.is_empty():
		return ""

	if path.is_absolute_path() or path.begins_with("res://") or path.begins_with("user://"):
		return path

	var game_folder_path = PathManager.get_game_folder(game_id).path_join(path)
	if FileAccess.file_exists(game_folder_path):
		return game_folder_path

	var root_path = PathManager.get_base_directory().path_join(path)
	if FileAccess.file_exists(root_path):
		return root_path

	return game_folder_path

## 実行ファイルのパスを解決する
static func find_executable(game: GameInfo) -> String:
	if game.executable_path.is_absolute_path() and FileAccess.file_exists(game.executable_path):
		return game.executable_path

	var game_folder = PathManager.get_game_folder(game.game_id)
	var candidate1 = game_folder.path_join(game.executable_path)
	if FileAccess.file_exists(candidate1):
		return candidate1

	var candidate2 = PathManager.get_base_directory().path_join(game.executable_path)
	if FileAccess.file_exists(candidate2):
		return candidate2

	return ""

## 引数文字列をパースする
static func parse_arguments(arguments: String) -> Array[String]:
	var args: Array[String] = []
	if not arguments.is_empty() and arguments != "<null>":
		var raw_args = arguments.split(" ", false)
		for arg in raw_args:
			if arg != "<null>" and arg != "null":
				args.append(arg)
	return args
