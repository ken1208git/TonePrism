## シーン遷移エフェクトマネージャー
## 拡大→等倍にズームしながらフェードインするトランジション

extends CanvasLayer

var _transitioning: bool = false
# (#315) 遷移アニメ中に要求された次の遷移を保持する (last-wins)。遷移中の change_scene を
# 「飲んで無視」すると、遷移アニメ中に _ready から自動で別シーンへ移る経路 (store_browse の
# 空セクション→カルーセル fallback 等) が固まり画面が詰む (#253 と同クラスのバグ)。飲まずに
# 保持し、現在の遷移完了後に続けて実行する。入力起因の遷移は呼び出し側が _transitioning を見て
# 自前ガードしているため (例: store_browse の入力処理)、本キューは主に「遷移中に走る自動遷移」を救う。
var _pending_scene: String = ""

func _ready():
	layer = 100
	process_mode = Node.PROCESS_MODE_ALWAYS

## ノードツリー内の全 CanvasLayer の子 Control を収集（再帰）
func _collect_canvas_panels(node: Node) -> Array[Control]:
	var panels: Array[Control] = []
	for child in node.get_children():
		if child is CanvasLayer:
			for canvas_child in child.get_children():
				if canvas_child is Control:
					panels.append(canvas_child)
		panels.append_array(_collect_canvas_panels(child))
	return panels

func change_scene(scene_path: String) -> void:
	if _transitioning:
		# (#315) 遷移中の要求は飲まず保持。現在の遷移完了後に実行する (下の _pending_scene 処理)。
		_pending_scene = scene_path
		return
	_transitioning = true

	# 新シーンをロード・インスタンス化
	var packed = load(scene_path) as PackedScene
	if not packed:
		_transitioning = false
		return
	var new_scene = packed.instantiate()

	# 旧シーンの参照を保持
	var old_scene = get_tree().current_scene

	# 新シーンを透明状態にする
	if new_scene is Control:
		new_scene.modulate = Color(1, 1, 1, 0)
	# CanvasLayer 内の Panel も事前に透明化（add_child 時の一瞬表示を防止）
	var new_canvas_panels = _collect_canvas_panels(new_scene)
	for panel in new_canvas_panels:
		panel.modulate = Color(1, 1, 1, 0)

	get_tree().root.add_child(new_scene)

	# シーンの初期化完了を待つ（DB読み込み・UI構築・画像ロードなど）
	await get_tree().create_timer(0.15).timeout

	# ズームフェードイン + 旧シーンフェードアウト（同時）
	if new_scene is Control:
		new_scene.pivot_offset = new_scene.size / 2.0
		new_scene.scale = Vector2(1.05, 1.05)

		var tween = create_tween()
		tween.set_parallel(true)
		tween.tween_property(new_scene, "scale", Vector2.ONE, 0.3)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		tween.tween_property(new_scene, "modulate:a", 1.0, 0.3)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		# 新シーンの CanvasLayer 内 Panel もフェードイン
		for panel in new_canvas_panels:
			panel.modulate = Color(1, 1, 1, 0)
			tween.tween_property(panel, "modulate:a", 1.0, 0.3)\
				.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		if old_scene and is_instance_valid(old_scene):
			tween.tween_property(old_scene, "modulate:a", 0.0, 0.3)\
				.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
			# 旧シーンの CanvasLayer 内 Panel も同時にフェードアウト
			for panel in _collect_canvas_panels(old_scene):
				tween.tween_property(panel, "modulate:a", 0.0, 0.3)\
					.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		await tween.finished

	# 旧シーンを削除、新シーンを現在のシーンに設定
	if old_scene and is_instance_valid(old_scene):
		old_scene.queue_free()
	get_tree().current_scene = new_scene

	_transitioning = false

	# (#315) 遷移中に積まれた次の遷移を実行 (last-wins)。これにより store_browse の空セクション→
	# カルーセル fallback など「遷移アニメ中に呼ばれた change_scene」が飲まれず確実に実行される。
	if _pending_scene != "":
		var next_scene := _pending_scene
		_pending_scene = ""
		change_scene(next_scene)
