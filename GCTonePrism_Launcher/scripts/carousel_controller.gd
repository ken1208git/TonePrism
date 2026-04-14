class_name CarouselController
extends RefCounted
## カルーセルUIの座標計算・カード生成・アニメーション

# --- 定数設定 ---
const CARD_SIZE := Vector2(200, 200)
const GAP_NARROW := 20.0
const GAP_WIDE := 120.0
const SCROLL_SPEED := 10.0
const SCALE_ACTIVE := 1.8
const SCALE_INACTIVE := 1.0
const OPACITY_INACTIVE := 0.6
const CORNER_RADIUS := 20.0
const FOCUS_MARGIN := 8.0

var card_nodes: Array[Panel] = []
var current_scroll_index: float = 0.0

## カルーセルのカードを生成する
func create_cards(games: Array[GameInfo], card_template: Panel,
		carousel_container: Control) -> void:
	if not card_template:
		push_error("CardTemplate not found!")
		return

	for game in games:
		var card = card_template.duplicate()
		card.name = "Card_%s" % game.game_id
		card.visible = true
		card.custom_minimum_size = CARD_SIZE
		card.pivot_offset = CARD_SIZE / 2
		card.focus_mode = Control.FOCUS_ALL

		carousel_container.add_child(card)

		# フォーカストラップ設定
		card.focus_neighbor_top = card.get_path()
		card.focus_neighbor_bottom = card.get_path()
		card.focus_neighbor_left = card.get_path()
		card.focus_neighbor_right = card.get_path()

		card_nodes.append(card)

		# サムネイル読み込み
		_setup_card_thumbnail(card, game)

## カードのサムネイルを準備する（画像は後からバックグラウンドで読み込む）
func _setup_card_thumbnail(card: Panel, game: GameInfo) -> void:
	var icon_rect = card.get_node_or_null("Clipper/Icon")
	var no_image_label = card.get_node_or_null("Clipper/NoImageLabel")

	var thumb_path = GamePathResolver.resolve_path(game.thumbnail_path, game.game_id)

	if not thumb_path.is_empty() and FileAccess.file_exists(thumb_path):
		# パスを保持し、後でバックグラウンドスレッドから読み込む
		card.set_meta("thumb_path", thumb_path)
		if icon_rect:
			icon_rect.visible = false
		if no_image_label:
			no_image_label.text = "LOADING"
			no_image_label.visible = true
	else:
		if not game.thumbnail_path.is_empty():
			push_warning("[CarouselController] ⚠️ Thumbnail NOT found for '%s' (ID: %s)\n  - DB Path: '%s'\n  - Check: '%s'" %
				[game.title, game.game_id, game.thumbnail_path, thumb_path])
		if icon_rect:
			icon_rect.visible = false
		if no_image_label:
			no_image_label.visible = true

## バックグラウンド読み込み用のキューを返す
func get_image_load_queue() -> Array:
	var queue: Array = []
	for card in card_nodes:
		if card.has_meta("thumb_path"):
			queue.append({
				"node_id": card.get_instance_id(),
				"path": card.get_meta("thumb_path"),
				"card": card
			})
	return queue

## 毎フレーム呼ばれる。カードの位置・スケール・透明度を更新する
func update_cards(delta: float, selected_index: int, viewport_size: Vector2,
		container_center_x: float, using_mouse: bool,
		static_focus_border: Panel, is_running: bool = false) -> int:
	var viewport_center_y = viewport_size.y / 2

	# スムーズスクロール
	var target = float(selected_index)
	current_scroll_index = lerpf(current_scroll_index, target, SCROLL_SPEED * delta)

	# アクティブインデックスの計算
	var new_active = int(round(current_scroll_index))
	new_active = clampi(new_active, 0, card_nodes.size() - 1)

	# 各カードの位置更新
	for i in range(card_nodes.size()):
		var card = card_nodes[i]
		var diff = float(i) - current_scroll_index

		# Y座標計算
		var base_y_offset = diff * (CARD_SIZE.y + GAP_NARROW)
		var push_amount = remap(abs(diff), 0.0, 1.0, 0.0, GAP_WIDE)
		push_amount = clamp(push_amount, 0.0, GAP_WIDE)
		var push_offset = sign(diff) * push_amount
		var screen_y = viewport_center_y + base_y_offset + push_offset

		# スケーリング
		var dist_from_center_px = abs(screen_y - viewport_center_y)
		var scale_factor = remap(dist_from_center_px, 0, 150, SCALE_ACTIVE, SCALE_INACTIVE)
		scale_factor = clamp(scale_factor, SCALE_INACTIVE, SCALE_ACTIVE)

		# 透明度（起動中(フェード中)は上書きしない）
		if not is_running:
			var opacity = 1.0 if i == selected_index else OPACITY_INACTIVE
			card.modulate.a = opacity

		# 反映
		card.position = Vector2(container_center_x - (CARD_SIZE.x / 2), screen_y - (CARD_SIZE.y / 2))
		card.scale = Vector2(scale_factor, scale_factor)
		card.z_index = 100 - int(dist_from_center_px / 10)

	return new_active
