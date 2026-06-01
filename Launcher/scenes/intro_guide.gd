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

# --- フォーカスグロー（ブラウズ/カルーセルと同じ、ぬるぬる追従する発光枠）---
var _focus_border: Panel
var _glow: GlowAnimator
var _focus_initialized: bool = false       # 初出現は zoom-in pop、以降 lerp 追従
var _focus_pop_tween: Tween = null         # 初出現の zoom-in + フェードイン pop (他画面と同じ)
var _focus_tweening: bool = false          # pop 中は追従 lerp を止める（scale 中の位置ドリフト防止）
var _using_mouse: bool = false             # マウス操作中はグローフォーカスを隠す（store_browse と同じ分離）

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
	# 背景（静止。スライドしない）。store_browse の Background ColorRect と同じ単色に揃える。
	var bg := ColorRect.new()
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bg.color = Color(0.08, 0.08, 0.08, 1)
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
	_build_focus_border()

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

## 操作ボタン（下端）。スキップ＝左端の独立ピル、戻る/進む＝中央の「1つの角丸四角を2分割」した
## segmented 風（中央に細いディバイダ）。全ボタン枠なし。クリック操作用（キーボード ←/→/Enter は併用）。
func _build_nav_buttons() -> void:
	# 下端いっぱいの帯 (高さ72px)。MarginContainer+preset は子追加前に min-size が潰れるため
	# アンカー+offset で明示。中の配置は band の子として絶対アンカーで置く (Container 非依存で確実)。
	var band := Control.new()
	band.anchor_left = 0.0
	band.anchor_right = 1.0
	band.anchor_top = 1.0
	band.anchor_bottom = 1.0
	band.offset_left = 0.0
	band.offset_right = 0.0
	band.offset_top = -112.0
	band.offset_bottom = -40.0
	band.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(band)

	# 中央: 戻る/進む の分割ピル。CenterContainer で帯の中央に置く。
	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	band.add_child(center)

	var seg := PanelContainer.new()
	seg.clip_contents = true   # 内側の矩形ボタンを角丸でクリップ → 外周だけ丸い1つの四角に見せる
	seg.add_theme_stylebox_override("panel", _seg_panel_style())
	seg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.add_child(seg)

	var seg_hb := HBoxContainer.new()
	seg_hb.add_theme_constant_override("separation", 0)
	seg_hb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	seg.add_child(seg_hb)

	_btn_back = _make_seg_button("←  戻る")
	_btn_back.pressed.connect(func() -> void: _navigate(-1))
	seg_hb.add_child(_btn_back)

	seg_hb.add_child(_make_divider())

	_btn_next = _make_seg_button("進む  →")
	# 進むは青のアクセント（主要操作を目立たせる）。segmented の右半分が青く塗られる。
	_btn_next.add_theme_stylebox_override("normal", _flat_style(Color(0.20, 0.50, 0.95, 0.9), 0))
	_btn_next.add_theme_stylebox_override("hover", _flat_style(Color(0.30, 0.58, 1.0, 1.0), 0))
	_btn_next.add_theme_stylebox_override("pressed", _flat_style(Color(0.15, 0.42, 0.85, 1.0), 0))
	_btn_next.pressed.connect(func() -> void: _navigate(1))
	seg_hb.add_child(_btn_next)

	# 右端: スキップ (独立した枠なしピル)。帯の右に絶対アンカーで配置・縦中央。
	_btn_skip = _make_pill_button("スキップ")
	_btn_skip.pressed.connect(func() -> void: _go_to_store())
	_btn_skip.anchor_left = 1.0
	_btn_skip.anchor_right = 1.0
	_btn_skip.anchor_top = 0.5
	_btn_skip.anchor_bottom = 0.5
	_btn_skip.offset_left = -(40.0 + 160.0)
	_btn_skip.offset_right = -40.0
	_btn_skip.offset_top = -28.0
	_btn_skip.offset_bottom = 28.0
	band.add_child(_btn_skip)

## ボタン共通設定（フォント・フォーカス可・サイズ・標準フォーカス装飾の抑止）。
## フォーカス表示はグロー枠 (_focus_border) が担うので focus stylebox は空にする。
func _make_button_base(text: String) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.focus_mode = Control.FOCUS_ALL
	btn.custom_minimum_size = Vector2(160, 56)
	btn.add_theme_font_override("font", _font_regular())
	btn.add_theme_font_size_override("font_size", 22)
	btn.add_theme_color_override("font_color", Color(1, 1, 1, 0.9))
	btn.add_theme_color_override("font_hover_color", Color(1, 1, 1, 1))
	btn.add_theme_color_override("font_focus_color", Color(1, 1, 1, 1))
	btn.add_theme_color_override("font_pressed_color", Color(1, 1, 1, 1))
	btn.add_theme_color_override("font_disabled_color", Color(1, 1, 1, 0.25))
	btn.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	return btn

## 独立ピル（スキップ用）。枠なし・角丸14。
func _make_pill_button(text: String) -> Button:
	var btn := _make_button_base(text)
	btn.add_theme_stylebox_override("normal", _flat_style(Color(1, 1, 1, 0.10), 14))
	btn.add_theme_stylebox_override("hover", _flat_style(Color(1, 1, 1, 0.18), 14))
	btn.add_theme_stylebox_override("pressed", _flat_style(Color(1, 1, 1, 0.26), 14))
	btn.add_theme_stylebox_override("disabled", _flat_style(Color(1, 1, 1, 0.04), 14))
	return btn

## segmented の片側（戻る/進む）。通常は透明（パネル地が透ける）・hover/pressed のみ薄く光る・角丸なし
## （外周の角丸は親 PanelContainer の clip が担う）。枠なし。
func _make_seg_button(text: String) -> Button:
	var btn := _make_button_base(text)
	btn.add_theme_stylebox_override("normal", _flat_style(Color(1, 1, 1, 0.0), 0))
	btn.add_theme_stylebox_override("hover", _flat_style(Color(1, 1, 1, 0.14), 0))
	btn.add_theme_stylebox_override("pressed", _flat_style(Color(1, 1, 1, 0.24), 0))
	btn.add_theme_stylebox_override("disabled", _flat_style(Color(1, 1, 1, 0.0), 0))
	return btn

## segmented 全体の地（1つの角丸四角）。枠なし・角丸14・内側 padding 0（子が端まで埋まる）。
func _seg_panel_style() -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = Color(1, 1, 1, 0.10)
	s.set_corner_radius_all(14)
	s.content_margin_left = 0
	s.content_margin_right = 0
	s.content_margin_top = 0
	s.content_margin_bottom = 0
	return s

## 戻る|進む の境目の細いディバイダ（縦1px・高さ36px・中央寄せ）。
func _make_divider() -> ColorRect:
	var d := ColorRect.new()
	d.color = Color(1, 1, 1, 0.18)
	d.custom_minimum_size = Vector2(1, 36)
	d.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	d.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return d

## ボタン地のスタイル（枠なし＝border幅0）。bg と角丸半径を指定。
func _flat_style(bg: Color, corner: int) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.set_corner_radius_all(corner)
	s.content_margin_left = 24
	s.content_margin_right = 24
	s.content_margin_top = 10
	s.content_margin_bottom = 10
	return s

## ボタンの状態更新: 先頭で「戻る」無効、最終で「進む」を「ストアへ」表記に。
func _update_nav_buttons() -> void:
	if _btn_back:
		_btn_back.disabled = (_current <= 0)
		# 無効化した「戻る」がフォーカスを持っていたら「進む」へ逃がす (無効ボタンは決定できないため)。
		if _btn_back.disabled and _btn_back.has_focus() and _btn_next:
			_btn_next.grab_focus()
	if _btn_next:
		_btn_next.text = "ストアへ  →" if _current >= _slides.size() - 1 else "進む  →"

# --- フォーカスグロー（ブラウズ/カルーセルと同じ発光枠を流用）---

## 追従するグロー枠を生成（store_browse の FocusBorder と同じ StyleBox: draw_center=false +
## shadow でやわらかく発光、GlowAnimator で明滅）。ボタン群より前面に出すため最後に add_child。
func _build_focus_border() -> void:
	_focus_border = Panel.new()
	_focus_border.visible = false
	_focus_border.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_focus_border.add_theme_stylebox_override("panel", _make_focus_border_style())
	add_child(_focus_border)

	_glow = GlowAnimator.new()
	_glow.register_focus_border(_focus_border)

func _make_focus_border_style() -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = Color(0.6, 0.6, 0.6, 0.0)
	s.draw_center = false
	s.border_color = Color(1, 1, 1, 1)
	s.set_corner_radius_all(16)
	s.expand_margin_left = 8.0
	s.expand_margin_top = 8.0
	s.expand_margin_right = 8.0
	s.expand_margin_bottom = 8.0
	s.shadow_color = Color(1, 1, 1, 1)
	s.shadow_size = 12
	return s

func _process(delta: float) -> void:
	if _glow:
		_glow.update(delta)        # グロー明滅（ブリージング）
	_update_focus_border(delta)

## フォーカス中のボタンへグロー枠を lerp 追従させる。
## フォーカスが自分のボタン以外（ダイアログ等）へ移ったら枠を隠す＝追従も奪い返しもしない。
func _update_focus_border(delta: float) -> void:
	if not _focus_border:
		return
	# シーン遷移中は枠を隠す（遷移完了後に再表示・スナップ）
	if _transitioning or TransitionManager._transitioning:
		_focus_border.visible = false
		_focus_initialized = false
		return

	# マウス操作中はグローフォーカスを隠す（キー/パッドに戻ったら再度 pop で出る）。
	if _using_mouse:
		_focus_border.visible = false
		_focus_initialized = false
		return

	var owner := get_viewport().gui_get_focus_owner()
	# 自分の 3 ボタン以外がフォーカスを持っている（=ダイアログ等が出た）ら枠を隠す。
	if owner == null or not (owner == _btn_back or owner == _btn_skip or owner == _btn_next):
		_focus_border.visible = false
		_focus_initialized = false
		return

	var target := (owner as Control).get_global_rect()
	if target.size.x <= 0.0:
		return  # レイアウト未確定（サイズ0）の間はスナップを待つ

	if not _focus_initialized:
		# 初出現は他画面（store_browse）と同じ zoom-in + フェードイン pop で現れる。
		# 位置/サイズは対象にスナップしたうえで、中心基準に scale 1.15→1.0・α 0→1 を 0.25s。
		_focus_border.global_position = target.position
		_focus_border.size = target.size
		_focus_border.pivot_offset = target.size / 2.0
		_focus_border.scale = Vector2(1.15, 1.15)
		_focus_border.modulate.a = 0.0
		_focus_border.visible = true
		_focus_initialized = true
		_focus_tweening = true   # pop 中は下の lerp を止める（後述）
		if _focus_pop_tween and _focus_pop_tween.is_valid():
			_focus_pop_tween.kill()
		_focus_pop_tween = create_tween()
		_focus_pop_tween.set_parallel(true)
		_focus_pop_tween.tween_property(_focus_border, "scale", Vector2.ONE, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_focus_pop_tween.tween_property(_focus_border, "modulate:a", 1.0, 0.25)\
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_focus_pop_tween.finished.connect(func() -> void: _focus_tweening = false)
		return

	# pop（zoom-in）中は位置/サイズの lerp を行わない。scale 中に global_position を読み書きすると
	# 位置がドリフトして「拡大しながら動く」見え方になるため（store_browse の _focus_tweening と同じガード）。
	if _focus_tweening:
		return

	# 以降は lerp で滑らかに追従（store_browse と同じ speed=delta*25）
	var speed := delta * 25.0
	_focus_border.global_position = _focus_border.global_position.lerp(target.position, speed)
	_focus_border.size = _focus_border.size.lerp(target.size, speed)

# --- スライドコンテンツ生成 ---

## 1スライド分のコンテンツを full-rect の Control として生成。
## 横スライドさせるため position を直接いじれる素の Control を root にし、中身は CenterContainer で中央寄せ。
## レイアウトは中身に応じて分岐:
##   - 画像＋本文の両方 → 左に画像・右に本文の横並び (HBox)
##   - 画像のみ / 本文のみ → それを中央に1つ
func _make_slide_content(slide: IntroSlideInfo) -> Control:
	var root := Control.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE

	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(center)

	var tex := _get_texture_for(slide)
	var has_image := tex != null
	var has_text := not slide.body_text.is_empty()

	if has_image and has_text:
		# 両方 → 左:画像 / 右:本文 の横並び（互いに縦中央そろえ）
		var hbox := HBoxContainer.new()
		hbox.alignment = BoxContainer.ALIGNMENT_CENTER
		hbox.add_theme_constant_override("separation", 60)
		hbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
		center.add_child(hbox)

		var img := _make_image_rect(tex, Vector2(820, 500))
		img.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		hbox.add_child(img)

		# 右側の本文は左揃え（列として読みやすい）。
		var body := _make_body_label(slide.body_text, 640, HORIZONTAL_ALIGNMENT_LEFT)
		body.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		hbox.add_child(body)
	elif has_image:
		center.add_child(_make_image_rect(tex, Vector2(960, 540)))
	else:
		center.add_child(_make_body_label(slide.body_text, 1100, HORIZONTAL_ALIGNMENT_CENTER))

	return root

## 画像 TextureRect を生成（アスペクト維持・中央寄せ）。
func _make_image_rect(tex: Texture2D, min_size: Vector2) -> TextureRect:
	var image_rect := TextureRect.new()
	image_rect.custom_minimum_size = min_size
	image_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	image_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	image_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	image_rect.texture = tex
	return image_rect

## 本文 Label を生成（幅・水平揃えを指定、折り返しあり）。
func _make_body_label(text: String, width: float, halign: int) -> Label:
	var body := Label.new()
	body.text = text
	body.add_theme_font_override("font", _font_regular())
	body.add_theme_font_size_override("font_size", 32)
	body.add_theme_color_override("font_color", Color(1, 1, 1, 0.95))
	body.horizontal_alignment = halign
	body.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	body.custom_minimum_size = Vector2(width, 0)
	body.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return body

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
	# 初期フォーカスは「進む」（連続で決定すれば読み進められる）。先頭で「戻る」は無効。
	_btn_next.grab_focus()

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
		_using_mouse = true
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		return
	if event is InputEventKey or event is InputEventJoypadButton:
		_using_mouse = false
		Input.mouse_mode = Input.MOUSE_MODE_HIDDEN

	if not event.is_pressed():
		return

	var viewport := get_viewport()
	if not viewport:
		return

	# ←/→ でのボタン間フォーカス移動・Enter/決定でのボタン押下は Godot の標準フォーカス系に委ねる
	# (ここで consume しない)。Esc(ui_cancel) だけは素早いスキップ用ショートカットとして直接処理する。
	if event.is_action_pressed("ui_cancel"):
		_go_to_store()
		viewport.set_input_as_handled()
