## ダイアログのズームフェードアニメーションを提供
## DialogManager と ErrorManager で共有する

extends RefCounted
class_name DialogAnimator

## ズームフェードインアニメーション
## panel_node_name: "Panel" 等のパネルノード名
## overlay_node_name: "Overlay" や "ColorRect" 等のオーバーレイノード名
static func animate_in(dialog: Control, owner: Node,
		panel_node_name: String = "Panel",
		overlay_node_name: String = "Overlay") -> void:
	var overlay = dialog.get_node_or_null(overlay_node_name)
	if overlay:
		overlay.color = Color(0, 0, 0, 0)
	var panel = dialog.get_node_or_null(panel_node_name)
	if not panel:
		return
	panel.pivot_offset = panel.size / 2.0
	panel.scale = Vector2(1.08, 1.08)
	panel.modulate = Color(1, 1, 1, 0)
	var tween = owner.create_tween()
	tween.set_parallel(true)
	tween.tween_property(panel, "scale", Vector2.ONE, 0.25)\
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(panel, "modulate:a", 1.0, 0.25)\
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	if overlay:
		tween.tween_property(overlay, "color:a", 0.784314, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

## ズームフェードアウトアニメーション
## 完了後に dialog を queue_free し、ポーズを解除する
static func animate_out(dialog: Control, owner: Node,
		on_finished: Callable = Callable(),
		panel_node_name: String = "Panel",
		overlay_node_name: String = "Overlay") -> void:
	var overlay = dialog.get_node_or_null(overlay_node_name)
	var panel = dialog.get_node_or_null(panel_node_name)
	if not panel:
		dialog.queue_free()
		if on_finished.is_valid():
			on_finished.call()
		return
	panel.pivot_offset = panel.size / 2.0
	var tween = owner.create_tween()
	tween.set_parallel(true)
	tween.tween_property(panel, "scale", Vector2(0.92, 0.92), 0.2)\
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	tween.tween_property(panel, "modulate:a", 0.0, 0.2)\
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	if overlay:
		tween.tween_property(overlay, "color:a", 0.0, 0.2)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	tween.finished.connect(func():
		if is_instance_valid(dialog):
			dialog.queue_free()
		if on_finished.is_valid():
			on_finished.call()
	)
