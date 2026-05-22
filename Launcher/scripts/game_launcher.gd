class_name GameLauncher
extends RefCounted
## ゲーム起動/復帰の「画面演出」ヘルパー (game_selection が所有)。
##
## 起動・監視・PLAYING確定・前面化異常(#216)・resume/quit・プロセス死活などのセッションロジックは
## autoload **GameSession** に移管済み。本クラスは game_selection 固有のノード (カルーセルカード /
## InfoPanel / TopBar / BottomBar / フォーカス枠 / 背景テクスチャ) のフェード・背景ズーム演出のみを担う。

const LAUNCH_TRANSITION_DURATION: float = 0.55
const LAUNCH_BG_ZOOM_SCALE: float = 1.05

var _is_returning: bool = false


## 復帰演出中か (この間はカルーセル入力を止める)。
func is_returning() -> bool:
	return _is_returning


## 背景レイヤー (BackgroundTexture / BackgroundTextureOld など同 parent の TextureRect) を全部返す。
## カルーセルのクロスフェードで「どちらが可視か」が入れ替わるため、ズームは可視・不可視問わず
## 全層に掛ける (片方だけだと modulate.a=0 の層をズームして「拡大されない」ことがある)。
func _bg_layers(bg_node: Control) -> Array:
	var layers: Array = []
	var parent := bg_node.get_parent()
	if parent:
		for child in parent.get_children():
			if child is TextureRect:
				layers.append(child)
	if layers.is_empty():
		layers.append(bg_node)
	return layers


## 起動中表示に切り替え（UIをフェードアウト + 背景ズームイン）
func switch_to_running_view(card_nodes: Array[Panel],
		selected_index: int, info_panel: Panel, top_bar: Control,
		static_focus_border: Panel, tree: SceneTree,
		carousel_container: Control = null, bottom_bar: Control = null,
		background_texture: TextureRect = null) -> void:
	print("[GameLauncher] Switching to Running View (Fade Out + BG Zoom)")

	var tween = tree.create_tween()
	tween.set_parallel(true)

	for i in range(card_nodes.size()):
		if i != selected_index:
			tween.tween_property(card_nodes[i], "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
				.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		# 選択中のカード（i == selected_index）は何もしない（残す）

	if info_panel:
		tween.tween_property(info_panel, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if top_bar:
		tween.tween_property(top_bar, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if bottom_bar:
		tween.tween_property(bottom_bar, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if static_focus_border:
		tween.tween_property(static_focus_border, "modulate:a", 0.0, 0.3)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	if carousel_container:
		var up_btn = carousel_container.get_node_or_null("ScrollUpButton")
		var down_btn = carousel_container.get_node_or_null("ScrollDownButton")
		if up_btn:
			tween.tween_property(up_btn, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		if down_btn:
			tween.tween_property(down_btn, "modulate:a", 0.0, LAUNCH_TRANSITION_DURATION).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	# 背景画像を中心からほんのちょっとズームイン（クロスフェードの2層とも掛ける）。
	# pivot は中心。初回 size 未確定 (0) は viewport サイズで代替。前回の戻し残りを避けるため必ず等倍開始。
	if background_texture:
		for bg in _bg_layers(background_texture):
			var bg_size: Vector2 = bg.size
			if bg_size.x <= 0.0 or bg_size.y <= 0.0:
				bg_size = bg.get_viewport_rect().size
			bg.pivot_offset = bg_size / 2.0
			bg.scale = Vector2.ONE
			tween.tween_property(bg, "scale", Vector2(LAUNCH_BG_ZOOM_SCALE, LAUNCH_BG_ZOOM_SCALE), LAUNCH_TRANSITION_DURATION)\
				.from(Vector2.ONE).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)


## 通常表示に戻す（UIをフェードイン + 背景ズームアウト）
func switch_to_normal_view(card_nodes: Array[Panel],
		info_panel: Panel, top_bar: Control, static_focus_border: Panel, tree: SceneTree,
		carousel_container: Control = null, bottom_bar: Control = null,
		background_texture: TextureRect = null) -> void:
	print("[GameLauncher] Switching to Normal View (Fade In + BG Zoom Out)")

	_is_returning = true

	if not tree:
		# ツリーがない場合のフォールバック（強制即時表示）
		for card in card_nodes:
			card.visible = true
			if card.modulate.a < 1.0:
				card.modulate.a = CarouselController.OPACITY_INACTIVE
		if info_panel:
			info_panel.visible = true
			info_panel.modulate.a = 1.0
		if top_bar:
			top_bar.visible = true
			top_bar.modulate.a = 1.0
		if bottom_bar:
			bottom_bar.visible = true
			bottom_bar.modulate.a = 1.0
		if static_focus_border:
			static_focus_border.visible = true
			static_focus_border.modulate.a = 1.0
		if carousel_container:
			var up_btn = carousel_container.get_node_or_null("ScrollUpButton")
			var down_btn = carousel_container.get_node_or_null("ScrollDownButton")
			if up_btn:
				up_btn.visible = true
				up_btn.modulate.a = 1.0
			if down_btn:
				down_btn.visible = true
				down_btn.modulate.a = 1.0
		if background_texture:
			background_texture.scale = Vector2.ONE
		_is_returning = false
		return

	var tween = tree.create_tween()
	tween.set_parallel(true)

	for card in card_nodes:
		card.visible = true
		if card.modulate.a < 1.0: # 選択カードは1.0のままなのでスキップされる
			tween.tween_property(card, "modulate:a", CarouselController.OPACITY_INACTIVE, LAUNCH_TRANSITION_DURATION)\
				.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	if info_panel:
		info_panel.visible = true
		tween.tween_property(info_panel, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if top_bar:
		top_bar.visible = true
		tween.tween_property(top_bar, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if bottom_bar:
		bottom_bar.visible = true
		tween.tween_property(bottom_bar, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	if static_focus_border:
		static_focus_border.visible = true
		tween.tween_property(static_focus_border, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION)\
			.from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	if carousel_container:
		var up_btn = carousel_container.get_node_or_null("ScrollUpButton")
		var down_btn = carousel_container.get_node_or_null("ScrollDownButton")
		if up_btn:
			up_btn.visible = true
			tween.tween_property(up_btn, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION).from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		if down_btn:
			down_btn.visible = true
			tween.tween_property(down_btn, "modulate:a", 1.0, LAUNCH_TRANSITION_DURATION).from(0.0).set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	# 背景画像をズームアウトして元のスケールに戻す (2層とも)
	if background_texture:
		for bg in _bg_layers(background_texture):
			var bg_size: Vector2 = bg.size
			if bg_size.x <= 0.0 or bg_size.y <= 0.0:
				bg_size = bg.get_viewport_rect().size
			bg.pivot_offset = bg_size / 2.0
			tween.tween_property(bg, "scale", Vector2.ONE, LAUNCH_TRANSITION_DURATION)\
				.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

	# フェードインアニメーション完了後に状態を戻す
	tween.finished.connect(func():
		_is_returning = false
	)
