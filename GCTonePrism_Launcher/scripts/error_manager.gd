extends CanvasLayer

# ErrorManager (AutoLoad)
# アプリケーション全体のエラー表示を管理します

func _ready():
	# 最前面に表示されるようにレイヤーを設定
	layer = 128
	# プロセスモードを常に実行（ポーズ中も表示可能）に設定
	process_mode = Node.PROCESS_MODE_ALWAYS

var _dialog_scene = preload("res://scenes/components/error_dialog.tscn")
var _current_dialog: Control = null

func is_error_showing() -> bool:
	return _current_dialog != null

func show_error(code: int):
	# 既に表示されている場合は何もしない
	if _current_dialog != null:
		return
	
	# コードが0（初期値やOK）の場合は、不明なエラーとして扱う
	if code == 0:
		code = ErrorCode.SYSTEM_UNKNOWN_ERROR
		
	var dialog = _dialog_scene.instantiate()

	# CanvasLayer（自分自身）の下に追加することで、layer設定が有効になる
	add_child(dialog)

	# セットアップ
	dialog.setup(code)

	_current_dialog = dialog

	# オーバーレイフェードイン
	var overlay = dialog.get_node_or_null("ColorRect")
	if overlay:
		overlay.color = Color(0, 0, 0, 0)

	# ズームフェードインアニメーション
	var panel = dialog.get_node_or_null("Panel")
	if panel:
		panel.pivot_offset = panel.size / 2.0
		panel.scale = Vector2(1.08, 1.08)
		panel.modulate = Color(1, 1, 1, 0)
		var tween = create_tween()
		tween.set_parallel(true)
		tween.tween_property(panel, "scale", Vector2.ONE, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		tween.tween_property(panel, "modulate:a", 1.0, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		if overlay:
			tween.tween_property(overlay, "color:a", 0.784314, 0.25)\
				.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
