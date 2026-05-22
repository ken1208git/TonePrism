extends Control
## プレイ中シーン (軽量, #214)。
## ゲーム実行中は重い game_selection (全ゲーム分のカルーセル/サムネ) を破棄し、本シーンに切り替える。
## 表示は「背景画像 + 選択ゲームのサムネ + 『プレイ中』」だけ (game_selection の running-view と同配置)。
## セッションは autoload GameSession が保持・監視するので、本シーンは表示と「終了で選択画面へ復帰」のみ。

const ICON_CENTER_X := 250.0   # carousel コンテナ幅 500 / 2 (= 選択カードの中心 x)
const ICON_SIZE := 360.0       # CARD_SIZE 200 × SCALE_ACTIVE 1.8
const ICON_RADIUS := 36        # CORNER_RADIUS 20 × 1.8

var _game: GameInfo = null
var _launching_overlay: LaunchingOverlay = null


func _ready() -> void:
	_game = GameSession.current_game
	_build_ui()
	GameSession.game_exited.connect(_on_game_exited)


func _build_ui() -> void:
	# 背景色 (bg 画像が無い場合の下地、game_selection の BackgroundColor 相当)
	var bg_color := ColorRect.new()
	bg_color.color = Color(0.1, 0.1, 0.1)
	bg_color.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg_color.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg_color)

	# 背景画像 (走行中ゲームの background)
	var bg := TextureRect.new()
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	bg.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg)

	# サムネ (carousel 選択カードと同位置: 左中央・360×360・角丸36)
	var thumb_panel := Panel.new()
	thumb_panel.clip_children = CanvasItem.CLIP_CHILDREN_ONLY
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0.1, 0.1, 0.1)
	sb.set_corner_radius_all(ICON_RADIUS)
	thumb_panel.add_theme_stylebox_override("panel", sb)
	thumb_panel.set_anchors_preset(Control.PRESET_CENTER_LEFT)
	thumb_panel.anchor_top = 0.5
	thumb_panel.anchor_bottom = 0.5
	thumb_panel.offset_left = ICON_CENTER_X - ICON_SIZE / 2.0
	thumb_panel.offset_right = ICON_CENTER_X + ICON_SIZE / 2.0
	thumb_panel.offset_top = -ICON_SIZE / 2.0
	thumb_panel.offset_bottom = ICON_SIZE / 2.0
	thumb_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(thumb_panel)

	var thumb := TextureRect.new()
	thumb.set_anchors_preset(Control.PRESET_FULL_RECT)
	thumb.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	thumb.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	thumb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	thumb_panel.add_child(thumb)

	# 「プレイ中」表示 (既存 launching_overlay コンポーネントを流用: 白 veil + ラベル)
	_launching_overlay = preload("res://scenes/components/launching_overlay.tscn").instantiate()
	add_child(_launching_overlay)

	if _game != null:
		_load_into(bg, GamePathResolver.resolve_path(_game.background_path, _game.game_id))
		_load_into(thumb, GamePathResolver.resolve_path(_game.thumbnail_path, _game.game_id))
		_launching_overlay.show_for_game(_game.title, LaunchingOverlay.State.PLAYING)
	else:
		# 単体起動 / セッション無し: 空表示
		_launching_overlay.show_for_game("", LaunchingOverlay.State.PLAYING)


func _load_into(rect: TextureRect, path: String) -> void:
	if path != "" and FileAccess.file_exists(path):
		var img := Image.load_from_file(path)
		if img != null and not img.is_empty():
			rect.texture = ImageTexture.create_from_image(img)


## ゲーム終了 (GameSession.game_exited) → 選択画面へ復帰。
## 復帰時に直前ゲームへフォーカスを戻すため initial_game_id を設定 (filtered_games は AppState に残存)。
func _on_game_exited() -> void:
	# 退出メニュー選択時はスクリーンセーバーへ。それ以外 (続行終了/ホーム/自然終了) は選択画面へ。
	if GameSession.should_exit_to_screensaver():
		IdleManager.transition_to_screensaver(get_tree())
		return
	if _game != null:
		AppState.initial_game_id = _game.game_id
	TransitionManager.change_scene("res://scenes/game_selection.tscn")
