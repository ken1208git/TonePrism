extends Node

## シーンマネージャー
## 画面遷移を管理するシングルトン

## 画面の種類
enum SceneType {
	SCREENSAVER,      # スクリーンセーバー画面
	GAME_SELECTION,   # ゲーム選択画面（将来実装）
	OPTIONS,          # オプション画面（将来実装）
}

var current_scene: Node = null

func _ready():
	# シングルトンとして登録
	pass

## シーンを遷移
## @param scene_type: 遷移先のシーンタイプ
func transition_to(scene_type: SceneType):
	match scene_type:
		SceneType.SCREENSAVER:
			_load_scene("res://scenes/screensaver.tscn")
		SceneType.GAME_SELECTION:
			# 将来実装
			print("ゲーム選択画面への遷移（将来実装）")
		SceneType.OPTIONS:
			# 将来実装
			print("オプション画面への遷移（将来実装）")

## シーンを読み込む
func _load_scene(scene_path: String):
	var scene = load(scene_path)
	if scene:
		# 現在のシーンを削除
		if current_scene:
			current_scene.queue_free()
		
		# 新しいシーンをインスタンス化
		current_scene = scene.instantiate()
		get_tree().root.add_child(current_scene)
		
		# メインシーンを設定
		get_tree().current_scene = current_scene
	else:
		print("エラー: シーンの読み込みに失敗しました: ", scene_path)


