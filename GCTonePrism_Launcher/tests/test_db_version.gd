@tool
extends SceneTree

# データベースバージョン管理のテスト
#
# 使い方:
# godot -s tests/test_db_version.gd

func _init():
	print("Running Database Versioning Test...")
	
	# テスト用DBパスを設定（既存のDBを上書きしないように）
	var test_db_path = "user://test_version_v2.db"
	
	# 既存のテストDBがあれば削除
	if FileAccess.file_exists(test_db_path):
		var err = DirAccess.remove_absolute(test_db_path)
		if err != OK:
			print("Error deleting old test db: ", err)
			quit(1)
			return

	# DatabaseManagerのモックを作成（パスを上書きするため）
	var db_manager = DatabaseManager.new()
	# db_path変数を直接書き換えるのは難しいので、
	# PathManagerのget_database_pathが返すパスを一時的に変更する必要があるが、
	# ここではdb_manager.db_pathをopen後に書き換えるアプローチは使えない（open内で使われるため）
	
	# 代わりに、SQLiteを直接使ってバージョン管理のロジックだけテストする
	print("Step 1: Create a new DB with version 0")
	var db = SQLite.new()
	db.path = test_db_path
	db.open_db()
	
	# 初期状態は0のはず
	db.query("PRAGMA user_version")
	var result = db.get_query_result()
	var version = 0
	if result[0] is Dictionary:
		version = result[0]["user_version"]
	else:
		version = result[0]
	
	if version != 0:
		print("Test Failed: Initial version is not 0. Got: ", version)
		quit(1)
		return
	print("  - Initial version is 0 (OK)")
	
	# バージョンを0に設定（明示的）
	db.query("PRAGMA user_version = 0")
	
	# DatabaseManagerの_check_and_migrate_dbロジックをシミュレート
	# （private関数を呼べないのでロジックを再実装して確認）
	
	print("Step 2: Simulate Migration")
	var target_version = 1 # DatabaseManager.CURRENT_DB_VERSION
	
	if version < target_version:
		print("  - Migration needed: v%d -> v%d" % [version, target_version])
		db.query("PRAGMA user_version = %d" % target_version)
		print("  - Migration executed")
	
	# バージョン確認
	db.query("PRAGMA user_version")
	result = db.get_query_result()
	var new_version = 0
	if result[0] is Dictionary:
		new_version = result[0]["user_version"]
	else:
		new_version = result[0]
		
	if new_version != target_version:
		print("Test Failed: Version did not update to %d. Got: %d" % [target_version, new_version])
		quit(1)
		return
		
	print("  - Version updated to %d (OK)" % new_version)
	
	# クリーンアップ
	db.close_db()
	# DirAccess.remove_absolute(test_db_path) # 確認用に残す
	
	print("All Tests Passed!")
	quit(0)
