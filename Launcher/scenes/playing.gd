extends Control
## プレイ中シーン (軽量, #214)。
## ゲーム実行中は重い game_selection (全ゲーム分のカルーセル/サムネ) を破棄し、本シーンに切り替える。
## 表示は「背景画像 + 選択ゲームのサムネ + 『プレイ中』」だけ (game_selection の running-view と同配置)。
## セッションは autoload GameSession が保持・監視するので、本シーンは表示と「終了で選択画面へ復帰」のみ。

const ICON_CENTER_X := 250.0   # carousel コンテナ幅 500 / 2 (= 選択カードの中心 x)
const ICON_SIZE := 360.0       # CARD_SIZE 200 × SCALE_ACTIVE 1.8
const ICON_RADIUS := 29        # カルーセルのカードbg角丸 16 × 1.8 ≈ 29 (影と角丸をカード厳密一致)
const BG_ZOOM := 1.05          # 起動中 (game_launcher.LAUNCH_BG_ZOOM_SCALE) と一致させる
const BG_ZOOM_DURATION := 0.55 # game_launcher.LAUNCH_TRANSITION_DURATION と一致

var _game: GameInfo = null
var _launching_overlay: LaunchingOverlay = null
var _bg: TextureRect = null
var _thumb_panel: Panel = null  # 中断メニュー表示中は隠す (メニュー窓が同位置に自前アイコンを出すため)


func _ready() -> void:
	_game = GameSession.current_game
	_build_ui()
	# 背景を起動中画面と同じく少し拡大した状態で開始 (瞬時切替なので継続して見える)。
	_bg.pivot_offset = _bg_pivot()
	_bg.scale = Vector2(BG_ZOOM, BG_ZOOM)
	GameSession.game_exited.connect(_on_game_exited)
	# 中断メニューからの終了開始 → 「ゲーム終了中…」表示に切替 (メイン窓は GameSession が前面化する)。
	GameSession.game_quitting.connect(_on_game_quitting)
	# 終了開始に失敗 (taskkill 起動不可) → 「終了中」表示を「プレイ中」へ戻す (誤った復帰演出を防ぐ)。
	GameSession.game_quit_aborted.connect(_on_game_quit_aborted)
	# 2 枚構成 (#214): 本シーンは常に不透明な背景のまま据え置く。中断メニューは別ウィンドウ
	# (透明・最前面) として上に重なり、本シーンはその背面でゲーム窓の隙間 (ウィンドウゲーム) を
	# 埋める背景になる。よって透過トグルは行わない (旧 4b のメイン窓透明化は撤去)。
	# 中断メニュー表示中は、メニュー窓が同位置に自前アイコンを出すので、重複する自身のサムネを隠す。
	OverlayManager.opened.connect(func(): if _thumb_panel: _thumb_panel.visible = false)
	OverlayManager.closed.connect(func(): if _thumb_panel: _thumb_panel.visible = true)


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

	# サムネ (carousel 選択カードと同位置: 左中央・360×360・角丸36)。
	# カルーセルの Card→Clipper→Icon と同じ2層構造: 外=影付き角丸カード(clipしない→影が角丸に追従)、
	# 内=角丸クリップ用 (サムネを角丸に切り抜く)。1枚に clip と影を同居させると clip がパネル矩形で
	# 影を四角く切ってしまい角丸に追従しないため分ける。
	var thumb_panel := Panel.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0.1, 0.1, 0.1)
	sb.set_corner_radius_all(ICON_RADIUS)
	# カルーセルのカードと同じ影 (card: shadow 0.25 / size4 ×1.8scale ≒ 7px。等倍 360px なので size 7)。
	sb.shadow_color = Color(0, 0, 0, 0.25)
	sb.shadow_size = 7
	thumb_panel.add_theme_stylebox_override("panel", sb)
	thumb_panel.set_anchors_preset(Control.PRESET_CENTER_LEFT)
	thumb_panel.anchor_top = 0.5
	thumb_panel.anchor_bottom = 0.5
	thumb_panel.offset_left = ICON_CENTER_X - ICON_SIZE / 2.0
	thumb_panel.offset_right = ICON_CENTER_X + ICON_SIZE / 2.0
	thumb_panel.offset_top = -ICON_SIZE / 2.0
	thumb_panel.offset_bottom = ICON_SIZE / 2.0
	thumb_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# launching_overlay の白 veil (z_index=50) より前面に出す (carousel 選択カードが z_index=100 で
	# 白 veil の上に出るのと同じ。これが無いとサムネが「プレイ中」の白オーバーレイの後ろに沈む)。
	thumb_panel.z_index = 100
	add_child(thumb_panel)
	_thumb_panel = thumb_panel

	var thumb_clip := Panel.new()
	thumb_clip.clip_children = CanvasItem.CLIP_CHILDREN_ONLY
	var clip_sb := StyleBoxFlat.new()
	clip_sb.bg_color = Color(0.1, 0.1, 0.1)
	clip_sb.set_corner_radius_all(ICON_RADIUS)
	thumb_clip.add_theme_stylebox_override("panel", clip_sb)
	thumb_clip.set_anchors_preset(Control.PRESET_FULL_RECT)
	thumb_clip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	thumb_panel.add_child(thumb_clip)

	var thumb := TextureRect.new()
	thumb.set_anchors_preset(Control.PRESET_FULL_RECT)
	thumb.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	thumb.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	thumb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	thumb_clip.add_child(thumb)

	# 「プレイ中」表示 (既存 launching_overlay コンポーネントを流用: 白 veil + ラベル)
	_launching_overlay = preload("res://scenes/components/launching_overlay.tscn").instantiate()
	add_child(_launching_overlay)

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


## 中断メニューからの終了開始 (GameSession.game_quitting): 「プレイ中」→「ゲーム終了中…」表示へ。
## メイン窓の前面化は GameSession 側が行うので、ここは表示切替のみ。サムネはメニューを閉じた際に
## 再表示済み (closed シグナル)。
var _quit_shown: bool = false  # 中断メニューからの終了で「終了中」を表示したか (復帰再現の状態判別用)


func _on_game_quitting() -> void:
	_quit_shown = true
	if _launching_overlay:
		_launching_overlay.set_state(LaunchingOverlay.State.QUITTING)


## 終了開始に失敗 (taskkill 起動不可) → 「終了中」を「プレイ中」へ戻し、復帰再現フラグも取り消す。
## これがないと後でゲームが自然終了した際に returning_from_quit=true となり、カルーセル復帰が誤って
## 「終了中」morph で再生される。
func _on_game_quit_aborted() -> void:
	_quit_shown = false
	if _launching_overlay:
		_launching_overlay.set_state(LaunchingOverlay.State.PLAYING)


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
	AppState.returning_from_quit = _quit_shown  # 終了中を見せた場合は復帰再現も「終了中」で連続させる
	get_tree().change_scene_to_file("res://scenes/game_selection.tscn")
