## アプリケーションのファイルパスを管理するクラス
## プロジェクトルート、データベースパス、ゲームフォルダパスを提供

extends RefCounted
class_name PathManager

## プロジェクトルートディレクトリのパス
static var base_directory: String = ""

## プロジェクトルートを取得（初回呼び出し時に検出）
static func get_base_directory() -> String:
	if base_directory.is_empty():
		base_directory = _find_base_directory()
	return base_directory

## gamesフォルダのパス
static func get_games_folder() -> String:
	return get_base_directory().path_join("games")

## データベースファイルのパス
static func get_database_path() -> String:
	return get_base_directory().path_join("prism.db")

## 指定したゲームのフォルダパス
static func get_game_folder(game_id: String) -> String:
	return get_games_folder().path_join(game_id)

## プロジェクトルートを自動検出
## 開発時（エディタ実行）・本番時（エクスポート実行）どちらも対応
static func _find_base_directory() -> String:
	# 方法1: res://パスを使用（エディタ実行時）
	# ProjectSettings.globalize_path("res://")でプロジェクトルートの絶対パスを取得
	var project_root = ProjectSettings.globalize_path("res://")
	
	if not project_root.is_empty():
		# res://から取得したパスを正規化
		project_root = project_root.replace("\\", "/").rstrip("/")
		
		# プロジェクトルートにprism.dbがあるか確認
		var db_path = project_root.path_join("prism.db")
		if FileAccess.file_exists(db_path):
			print("[PathManager] res://からプロジェクトルートを検出: ", project_root)
			return project_root
		else:
			# res://から取得したパスにprism.dbがない場合、親ディレクトリを確認
			# プロジェクトルートはLauncherフォルダの親ディレクトリ
			var parent_path = project_root.get_base_dir()
			var parent_db_path = parent_path.path_join("prism.db")
			if FileAccess.file_exists(parent_db_path):
				print("[PathManager] res://の親ディレクトリからプロジェクトルートを検出: ", parent_path)
				return parent_path
			else:
				# DBが見つからない場合でも、もしここが "Launcher" なら
				# その親をプロジェクトルートとみなして返す（開発中のフォルダ構造を信じる）
				# これは、DBファイルがまだ存在しない初期状態などでのパス解決を防ぐため
				if project_root.ends_with("Launcher") or parent_path.ends_with("GCTonePrism"):
					print("[PathManager] DB未検出だがフォルダ構造からルートを推測: ", parent_path)
					return parent_path
				
				print("[PathManager] res://から取得したパスにprism.dbが見つかりません。")
	
	# res://が使えない場合（エクスポート時など）、実行ファイルのパスから検出
	return _find_base_directory_from_executable()

## 実行ファイルのパスからプロジェクトルートを検出（エクスポート実行時）
static func _find_base_directory_from_executable() -> String:
	var exe_path = OS.get_executable_path().get_base_dir()
	var current_path = exe_path
	
	# 最大10階層まで遡る（無限ループ防止）
	var max_levels = 10
	var current_level = 0
	var dir = DirAccess.open(current_path)
	var detected_base_directory: String = ""
	
	while dir != null and current_level < max_levels:
		var current_dir_path = dir.get_current_dir()
		
		# 優先順位1: prism.db（データベースファイル）
		var db_path = current_dir_path.path_join("prism.db")
		if FileAccess.file_exists(db_path):
			print("[PathManager] prism.db を検出: ", current_dir_path)
			detected_base_directory = current_dir_path
			break
		
		# 優先順位2: .git（Gitリポジトリのルート）
		var git_path = current_dir_path.path_join(".git")
		if dir.dir_exists(git_path):
			print("[PathManager] .git フォルダを検出: ", current_dir_path)
			detected_base_directory = current_dir_path
			break
		
		# 優先順位3: Launcherフォルダが存在する場合（実行ファイルがその中にある場合）
		# 実行ファイルがLauncherフォルダ内にある場合、親ディレクトリをプロジェクトルートとする
		# NOTE: begins_with 比較は末尾区切り ("/") を付与した文字列で行うこと。区切り無しだと
		#       "Launcher" prefix が "LauncherStudio" 等の兄弟 dir 名にも誤マッチする
		#       (PR #150 round 2 M2)。Godot の path_join / get_base_dir は "/" separated を返す。
		var launcher_folder_check = current_dir_path.path_join("Launcher")
		var launcher_folder_check_with_sep = launcher_folder_check + "/"
		if dir.dir_exists(launcher_folder_check):
			# 実行ファイルがLauncherフォルダ内にあるか確認
			if exe_path.begins_with(launcher_folder_check_with_sep):
				print("[PathManager] Launcherフォルダを検出: ", current_dir_path)
				detected_base_directory = current_dir_path
				break
		
		# 親ディレクトリに移動
		var parent_path = current_dir_path.get_base_dir()
		if parent_path == current_dir_path:
			# ルートディレクトリに到達
			break
		
		dir = DirAccess.open(parent_path)
		current_level += 1
	
	# プロジェクトルートが見つからない場合
	if detected_base_directory.is_empty():
		var error_message = "エラー: プロジェクトルートが見つかりません。\n\n" + \
						   "実行ファイルのパス: " + exe_path + "\n\n" + \
		                   "このアプリケーションは、Launcherフォルダ内から実行してください。"
		print("[PathManager] ", error_message)
		push_error(error_message)
		return exe_path
	
	# Launcherフォルダが存在し、実行ファイルがその中にあるか確認
	var launcher_folder_path = detected_base_directory.path_join("Launcher")
	if not DirAccess.dir_exists_absolute(launcher_folder_path):
		var error_message = "エラー: Launcherフォルダが見つかりません。\n\n" + \
						   "プロジェクトルート: " + detected_base_directory + "\n" + \
						   "実行ファイルのパス: " + exe_path + "\n\n" + \
		                   "このアプリケーションは、Launcherフォルダ内から実行してください。"
		print("[PathManager] ", error_message)
		push_error(error_message)
		return detected_base_directory
	
	# Separator 付きで begins_with 比較 (兄弟 dir 名との prefix collision 防止、round 2 M2)
	var launcher_folder_path_with_sep = launcher_folder_path + "/"
	if not exe_path.begins_with(launcher_folder_path_with_sep):
		var error_message = "エラー: 実行ファイルがLauncherフォルダ内にありません。\n\n" + \
						   "プロジェクトルート: " + detected_base_directory + "\n" + \
						   "Launcherフォルダ: " + launcher_folder_path + "\n" + \
						   "実行ファイルのパス: " + exe_path + "\n\n" + \
		                   "このアプリケーションは、Launcherフォルダ内から実行してください。"
		print("[PathManager] ", error_message)
		push_error(error_message)
		return detected_base_directory
	
	return detected_base_directory

## パスの確認（デバッグ用）
static func verify_paths() -> void:
	print("=== PathManager - パス確認 ===")
	print("実行ファイル: ", OS.get_executable_path().get_base_dir())
	print("プロジェクトルート: ", get_base_directory())
	print("Gamesフォルダ: ", get_games_folder())
	print("データベース: ", get_database_path())
	print("")
	print("Gamesフォルダ存在: ", DirAccess.dir_exists_absolute(get_games_folder()))
	print("データベース存在: ", FileAccess.file_exists(get_database_path()))
	print("============================")

## 必要なフォルダを作成
static func ensure_directories_exist() -> void:
	var games_folder = get_games_folder()
	if not DirAccess.dir_exists_absolute(games_folder):
		DirAccess.make_dir_recursive_absolute(games_folder)
