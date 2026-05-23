extends Control
## プレイ中シーン (軽量, #214)。
## ゲーム実行中は重い game_selection (全ゲーム分のカルーセル/サムネ) を破棄し、本シーンに切り替える。
## 表示は「背景画像 + 選択ゲームのサムネ + 『プレイ中』」だけ (game_selection の running-view と同配置)。
## セッションは autoload GameSession が保持・監視するので、本シーンは表示と「終了で選択画面へ復帰」のみ。

const ICON_CENTER_X := 250.0   # carousel コンテナ幅 500 / 2 (= 選択カードの中心 x)
const ICON_SIZE := 360.0       # CARD_SIZE 200 × SCALE_ACTIVE 1.8
const ICON_RADIUS := 36        # CORNER_RADIUS 20 × 1.8
const BG_ZOOM := 1.05          # 起動中 (game_launcher.LAUNCH_BG_ZOOM_SCALE) と一致させる
const BG_ZOOM_DURATION := 0.55 # game_launcher.LAUNCH_TRANSITION_DURATION と一致

var _game: GameInfo = null
var _launching_overlay: LaunchingOverlay = null
var _bg: TextureRect = null
# 中断オーバーレイ表示中にライブゲームを透かすため隠す、本シーンの不透明アート群。
var _art_nodes: Array[CanvasItem] = []
var _passthrough: bool = false


func _ready() -> void:
	_game = GameSession.current_game
	_build_ui()
	# 背景を起動中画面と同じく少し拡大した状態で開始 (瞬時切替なので継続して見える)。
	_bg.pivot_offset = _bg_pivot()
	_bg.scale = Vector2(BG_ZOOM, BG_ZOOM)
	GameSession.game_exited.connect(_on_game_exited)
	# 中断オーバーレイ (HOME) の開閉でライブゲーム透過を反映する。
	OverlayManager.opened.connect(_on_overlay_opened)
	OverlayManager.closed.connect(_on_overlay_closed)


func _bg_pivot() -> Vector2:
	var sz: Vector2 = _bg.size
	if sz.x <= 0.0 or sz.y <= 0.0:
		sz = _bg.get_viewport_rect().size
	return sz / 2.0


func _build_ui() -> void:
	# 背景色 (bg 画像が無い場合の下地、game_selection の BackgroundColor 相当)
	var bg_color := ColorRect.new()
	bg_color.color = Color(0.1, 0.1, 0.1)
	bg_color.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg_color.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg_color)

	# 背景画像 (走行中ゲームの background)
	_bg = TextureRect.new()
	_bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	_bg.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_bg.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	_bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_bg)

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

	# 透過時に隠す不透明アート群 (これらを隠すと透明窓越しにライブゲームが見える)。
	_art_nodes = [bg_color, _bg, thumb_panel, _launching_overlay]

	if _game != null:
		_load_into(_bg, GamePathResolver.resolve_path(_game.background_path, _game.game_id))
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


## 中断オーバーレイ HOME: 本シーンの背景アートを隠し、メイン窓を per-pixel 透明化して、
## topmost で前面に居る launcher 越しに背後のライブゲームを透かす。
func _on_overlay_opened() -> void:
	_set_live_passthrough(true)


func _on_overlay_closed() -> void:
	# 即復元するとゲーム窓が前面に戻る前に「プレイ中」が一瞬見えてちらつく。透明+アート非表示のまま
	# (ライブゲームが見える) ゲーム復帰を待ち、ゲームが前面に戻って launcher を覆ってから不可視に復元する。
	await get_tree().create_timer(0.3).timeout
	# 0.3s 内に再度開かれた / シーン離脱した場合は復元しない (離脱時は _exit_tree が即復元する)。
	if is_inside_tree() and not OverlayManager.is_open():
		_set_live_passthrough(false)


## ライブゲーム透過の on/off。背景アート (bg色/背景画像/サムネ/「プレイ中」) の表示と窓透明を切り替える。
func _set_live_passthrough(on: bool) -> void:
	if _passthrough == on:
		return
	_passthrough = on
	for n in _art_nodes:
		if is_instance_valid(n):
			n.visible = not on
	var w := get_window()
	if w:
		w.transparent_bg = on
		w.set_flag(Window.FLAG_TRANSPARENT, on)


func _exit_tree() -> void:
	# シーン離脱時に透明窓のまま次シーンへ持ち越さないよう、必ず不透明へ戻す。
	_set_live_passthrough(false)


## ゲーム終了 (GameSession.game_exited) → 選択画面へ復帰。
## 復帰時に直前ゲームへフォーカスを戻すため initial_game_id を設定 (filtered_games は AppState に残存)。
func _on_game_exited() -> void:
	# 退出メニュー選択時はスクリーンセーバーへ。それ以外 (続行終了/ホーム/自然終了) は選択画面へ。
	if GameSession.should_exit_to_screensaver():
		IdleManager.transition_to_screensaver(get_tree())
		return
	if _game != null:
		AppState.initial_game_id = _game.game_id
	# トランジション無しで瞬時に game_selection へ。game_selection 側が起動中画面と同じ
	# running-view 静止状態 (プレイ中表示・背景 1.05) を再現し、起動モーションの逆再生
	# (switch_to_normal_view: 背景ズームアウト + カルーセルフェードイン) でカルーセルへ戻る。
	AppState.returning_from_game = true
	get_tree().change_scene_to_file("res://scenes/game_selection.tscn")
