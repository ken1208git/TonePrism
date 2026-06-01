extends Control

## 初回説明画面 (#253)
## screensaver で「PRESS ANY KEY」後、ストアブラウズへ入る前に挟む説明スライド。
## 各来場者にとって毎回が「初回」になるため、頻度は毎回表示（自動再生なし・手動ナビゲーション）。
##
## データは intro_slides テーブル（DB v22, Manager 側で編集）。画像実体は guide/ フォルダ。
## #244 方針: DB 由来の純動的コンテンツは builder スクリプトで構築する（.tscn を強制しない）。
## 本シーンは単一用途（再利用しない1画面）のため、シーン script 内で直接 UI を組み立てる。
##
## 送り/戻しは「中断メニュー (overlay_menu) 登場」と同じ流儀の横スライド + フェード
## (TRANS_QUINT / EASE_OUT)。次へ=新スライドが右から入り旧が左へ抜ける、戻る=その逆。

const STORE_BROWSE_SCENE := "res://scenes/store_browse.tscn"

# スライド送りアニメ (overlay_menu の ANIM_OPEN_DUR=0.55 / QUINT・EASE_OUT を踏襲、やや速め)
const SLIDE_DX := 320.0   # 横スライド量(px)。旧は -dir*SLIDE_DX へ、新は +dir*SLIDE_DX から 0 へ
const NAV_DUR := 0.4

# --- 状態 ---
var _db_manager: DatabaseManager
var _slides: Array[IntroSlideInfo] = []
var _current: int = 0
var _transitioning: bool = false
var _texture_cache: Dictionary = {}      # image_path(String) -> ImageTexture（再ナビ時の再読込回避）
var _font_regular_cache: FontFile = null

# --- コードで構築するノード参照 ---
var _stage: Control                       # スライド本体（image+body）が横スライドする領域
var _page_label: Label
var _current_content: Control = null       # 現在表示中のスライドコンテンツ
var _outgoing_content: Control = null      # 退場アニメ中の旧スライドコンテンツ
var _nav_tween: Tween = null
var _btn_back: Button                      # 戻る（先頭スライドで無効化）
var _btn_skip: Button                      # スキップ（→ストアへ）
var _btn_next: Button                      # 進む（最終スライドでは「ストアへ」表記）

func _ready() -> void:
	set_process_input(true)
	process_mode = Node.PROCESS_MODE_ALWAYS

	_load_slides()

	# 表示対象スライド0件 → 説明をスキップしてストアへ。
	# 通常は screensaver 側の has_visible_slides() で事前に分岐し本シーンには来ないが、
	# screensaver のチェックと本シーンの読込の間にスライドが削除された race のための保険。
	if _slides.is_empty():
		_go_to_store_when_free()
		return

	_build_ui()
	_set_initial_slide(0)

## DBから表示対象スライドを読み込む
func _load_slides() -> void:
	_db_manager = DatabaseManager.new()
	if not _db_manager.open():
		# DBを開けない場合は説明をスキップ（ストア側のエラー表示に委ねる）
		return
	var repo := IntroSlideRepository.new(_db_manager)
	_slides = repo.get_visible_slides()
	_db_manager.close()

# --- UI構築 ---

func _build_ui() -> void:
	# 背景（静止。スライドしない）
	var bg := ColorRect.new()
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bg.color = Color(0.08, 0.08, 0.1, 1)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg)

	# スライド領域（この中のコンテンツが左右にスライドする）。画面端からはみ出す分はクリップ。
	_stage = Control.new()
	_stage.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_stage.clip_contents = true
	_stage.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_stage)

	# 以降（ページ表示・ヒントバー）はスライド領域より前面に固定表示
	_build_page_indicator()
	_build_nav_buttons()

## ページ表示（上部中央 "1 / 3"）
func _build_page_indicator() -> void:
	var top_margin := MarginContainer.new()
	top_margin.set_anchors_and_offsets_preset(Control.PRESET_TOP_WIDE)
	top_margin.add_theme_constant_override("margin_top", 40)
	top_margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(top_margin)

	_page_label = Label.new()
	_page_label.add_theme_font_override("font", _font_regular())
	_page_label.add_theme_font_size_override("font_size", 24)
	_page_label.add_theme_color_override("font_color", Color(1, 1, 1, 0.6))
	_page_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_page_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	top_margin.add_child(_page_label)

## 操作ボタン（下部中央）。戻る / スキップ / 進む。クリック操作用（キーボード ←/→/Esc は _input で併用）。
func _build_nav_buttons() -> void:
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	margin.add_theme_constant_override("margin_bottom", 48)
	margin.add_theme_constant_override("margin_top", 20)
	margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(margin)

	var hbox := HBoxContainer.new()
	hbox.alignment = BoxContainer.ALIGNMENT_CENTER
	hbox.add_theme_constant_override("separation", 24)
	hbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	margin.add_child(hbox)

	_btn_back = _make_nav_button("←  戻る")
	_btn_back.pressed.connect(func() -> void: _navigate(-1))
	hbox.add_child(_btn_back)

	_btn_skip = _make_nav_button("スキップ")
	_btn_skip.pressed.connect(func() -> void: _go_to_store())
	hbox.add_child(_btn_skip)

	_btn_next = _make_nav_button("進む  →")
	_btn_next.pressed.connect(func() -> void: _navigate(1))
	hbox.add_child(_btn_next)

## ナビボタン1つを生成。キーボード/パッドは _input で処理するため focus は取らせない (Enter 二重発火防止)。
func _make_nav_button(text: String) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.focus_mode = Control.FOCUS_NONE
	btn.custom_minimum_size = Vector2(180, 56)
	btn.add_theme_font_override("font", _font_regular())
	btn.add_theme_font_size_override("font_size", 22)
	btn.add_theme_color_override("font_color", Color(1, 1, 1, 0.9))
	btn.add_theme_color_override("font_hover_color", Color(1, 1, 1, 1))
	btn.add_theme_color_override("font_pressed_color", Color(1, 1, 1, 1))
	btn.add_theme_color_override("font_disabled_color", Color(1, 1, 1, 0.25))
	btn.add_theme_stylebox_override("normal", _button_style(Color(1, 1, 1, 0.12)))
	btn.add_theme_stylebox_override("hover", _button_style(Color(1, 1, 1, 0.22)))
	btn.add_theme_stylebox_override("pressed", _button_style(Color(1, 1, 1, 0.30)))
	btn.add_theme_stylebox_override("disabled", _button_style(Color(1, 1, 1, 0.05)))
	return btn

## ナビボタンのスタイル（キーキャップ風の半透明パネル + 角丸）
func _button_style(bg: Color) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = Color(1, 1, 1, 0.3)
	s.set_border_width_all(1)
	s.set_corner_radius_all(10)
	s.content_margin_left = 24
	s.content_margin_right = 24
	s.content_margin_top = 10
	s.content_margin_bottom = 10
	return s

## ボタンの状態更新: 先頭で「戻る」無効、最終で「進む」を「ストアへ」表記に。
func _update_nav_buttons() -> void:
	if _btn_back:
		_btn_back.disabled = (_current <= 0)
	if _btn_next:
		_btn_next.text = "ストアへ  →" if _current >= _slides.size() - 1 else "進む  →"

# --- スライドコンテンツ生成 ---

## 1スライド分のコンテンツ（画像 + 本文）を full-rect の Control として生成。
## 横スライドさせるため position を直接いじれる素の Control を root にし、中身は CenterContainer で中央寄せ。
func _make_slide_content(slide: IntroSlideInfo) -> Control:
	var root := Control.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(center)

	var vbox := VBoxContainer.new()
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_theme_constant_override("separation", 40)
	vbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.add_child(vbox)

	# 画像（アスペクト維持・中央寄せ）。画像なしスライドでは非表示にして本文のみ。
	var image_rect := TextureRect.new()
	image_rect.custom_minimum_size = Vector2(960, 540)
	image_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	image_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	image_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var tex := _get_texture_for(slide)
	if tex != null:
		image_rect.texture = tex
	else:
		image_rect.visible = false
	vbox.add_child(image_rect)

	# 本文
	var body := Label.new()
	body.text = slide.body_text
	body.add_theme_font_override("font", _font_regular())
	body.add_theme_font_size_override("font_size", 32)
	body.add_theme_color_override("font_color", Color(1, 1, 1, 0.95))
	body.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	body.custom_minimum_size = Vector2(1100, 0)
	body.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(body)

	return root

## スライドの画像を解決・読込してテクスチャを返す（image_path 単位でキャッシュ）。画像なし/失敗は null。
func _get_texture_for(slide: IntroSlideInfo) -> Texture2D:
	if slide.image_path.is_empty():
		return null
	if _texture_cache.has(slide.image_path):
		return _texture_cache[slide.image_path]

	var resolved := _resolve_image_path(slide.image_path)
	if resolved.is_empty() or not FileAccess.file_exists(resolved):
		return null
	var image := Image.load_from_file(resolved)
	if image == null:
		return null
	var tex := ImageTexture.create_from_image(image)
	_texture_cache[slide.image_path] = tex
	return tex

func _font_regular() -> FontFile:
	if _font_regular_cache == null:
		_font_regular_cache = load("res://fonts/NotoSansJP-Regular.ttf")
	return _font_regular_cache

## guide/ 相対パスを絶対パスに解決（GamePathResolver.resolve_path の guide 版）
func _resolve_image_path(rel: String) -> String:
	if rel.is_empty():
		return ""
	if rel.is_absolute_path() or rel.begins_with("res://") or rel.begins_with("user://"):
		return rel

	var guide_path := PathManager.get_guide_folder().path_join(rel)
	if FileAccess.file_exists(guide_path):
		return guide_path

	# フォールバック: プロジェクトルートからの相対
	var root_path := PathManager.get_base_directory().path_join(rel)
	if FileAccess.file_exists(root_path):
		return root_path

	return guide_path

# --- スライド表示・ナビゲーション ---

## 初回表示（アニメなし。シーン遷移のフェードと二重がけしない）
func _set_initial_slide(index: int) -> void:
	_current = index
	_page_label.text = "%d / %d" % [index + 1, _slides.size()]
	_current_content = _make_slide_content(_slides[index])
	_stage.add_child(_current_content)
	_update_nav_buttons()

## 送り/戻し（キー入力・ボタン共通）。端での挙動: 最終で「進む」→ストア、先頭で「戻る」→何もしない。
func _navigate(direction: int) -> void:
	if _transitioning:
		return
	var target := _current + direction
	if direction > 0 and target >= _slides.size():
		_go_to_store()
		return
	if target < 0 or target >= _slides.size():
		return
	_animate_to(target, direction)

## index のスライドへ横スライド + フェードで切り替える。
## direction = +1(次へ): 新が右(+SLIDE_DX)から中央へ、旧が左(-SLIDE_DX)へ抜ける。-1(戻る)はその逆。
func _animate_to(index: int, direction: int) -> void:
	# 進行中アニメの割り込み: 旧 tween を kill し、宙に浮いた旧コンテンツを片付け、現コンテンツを定位置へ戻す。
	if _nav_tween and _nav_tween.is_valid():
		_nav_tween.kill()
	if is_instance_valid(_outgoing_content):
		_outgoing_content.queue_free()
	_outgoing_content = null
	if is_instance_valid(_current_content):
		_current_content.position = Vector2.ZERO
		_current_content.modulate.a = 1.0

	_current = index
	_page_label.text = "%d / %d" % [index + 1, _slides.size()]
	_update_nav_buttons()

	var new_content := _make_slide_content(_slides[index])
	_stage.add_child(new_content)
	new_content.position = Vector2(direction * SLIDE_DX, 0.0)
	new_content.modulate.a = 0.0

	_outgoing_content = _current_content
	_current_content = new_content

	_nav_tween = create_tween()
	_nav_tween.set_parallel(true)
	# 新スライド: 横から中央へ + フェードイン
	_nav_tween.tween_property(new_content, "position:x", 0.0, NAV_DUR)\
		.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	_nav_tween.tween_property(new_content, "modulate:a", 1.0, NAV_DUR)\
		.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	# 旧スライド: 反対側へ抜けつつフェードアウト → 完了後に破棄
	var outgoing := _outgoing_content
	if is_instance_valid(outgoing):
		_nav_tween.tween_property(outgoing, "position:x", -direction * SLIDE_DX, NAV_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		_nav_tween.tween_property(outgoing, "modulate:a", 0.0, NAV_DUR)\
			.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
		# chain(): 上の parallel が出揃ってから実行。kill 時はコールバックも発火しないため、
		# 自然完了かつ未割り込みのときだけここに来る（その場合 _outgoing_content は依然 outgoing を指す）。
		_nav_tween.chain().tween_callback(func() -> void:
			if is_instance_valid(outgoing):
				outgoing.queue_free()
			if _outgoing_content == outgoing:
				_outgoing_content = null
		)

func _go_to_store() -> void:
	if _transitioning:
		return
	_transitioning = true
	TransitionManager.change_scene(STORE_BROWSE_SCENE)

## 直前の遷移 (screensaver → 本シーン) が完了するのを待ってからストアへ。
## TransitionManager は遷移中の再入を弾くため、_ready 直後の即時遷移では無視される。
## 空スライドは通常 screensaver 側で分岐するため、本経路は稀な race のみ。
func _go_to_store_when_free() -> void:
	if _transitioning:
		return
	_transitioning = true
	await get_tree().create_timer(0.5).timeout
	TransitionManager.change_scene(STORE_BROWSE_SCENE)

# --- 入力 ---

func _input(event: InputEvent) -> void:
	if _transitioning:
		return

	# マウス移動でカーソル表示（ボタンをクリック操作可能に）、キー/パッドで非表示（キオスク既定）。
	# store_browse と同じ分離。マウスクリックは下の action 判定に一致せずボタン側で処理される。
	if event is InputEventMouseMotion:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		return
	if event is InputEventKey or event is InputEventJoypadButton:
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN

	if not event.is_pressed():
		return

	var viewport := get_viewport()
	if not viewport:
		return

	if event.is_action_pressed("ui_cancel"):
		_go_to_store()
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_left"):
		_navigate(-1)
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_right") or event.is_action_pressed("ui_accept"):
		_navigate(1)
		viewport.set_input_as_handled()
