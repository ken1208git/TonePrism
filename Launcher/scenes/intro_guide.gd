extends Control

## 初回説明画面 (#253)
## screensaver で「PRESS ANY KEY」後、ストアブラウズへ入る前に挟む説明スライド。
## 各来場者にとって毎回が「初回」になるため、頻度は毎回表示（自動再生なし・手動ナビゲーション）。
##
## データは intro_slides テーブル（DB v22, Manager 側で編集）。画像実体は guide/ フォルダ。
## #244 方針: DB 由来の純動的コンテンツは builder スクリプトで構築する（.tscn を強制しない）。
## 本シーンは単一用途（再利用しない1画面）のため、シーン script 内で直接 UI を組み立てる。

const STORE_BROWSE_SCENE := "res://scenes/store_browse.tscn"

# --- 状態 ---
var _db_manager: DatabaseManager
var _slides: Array[IntroSlideInfo] = []
var _current: int = 0
var _transitioning: bool = false
var _texture_cache: Dictionary = {}  # slide index -> ImageTexture（再ナビ時の再読込回避）

# --- コードで構築するノード参照 ---
var _image_rect: TextureRect
var _body_label: Label
var _page_label: Label

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
	_show_slide(0)

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
	# 背景
	var bg := ColorRect.new()
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bg.color = Color(0.08, 0.08, 0.1, 1)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg)

	# 中央コンテンツ（画像 + 本文）
	var center := CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(center)

	var vbox := VBoxContainer.new()
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_theme_constant_override("separation", 40)
	vbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.add_child(vbox)

	# 画像（アスペクト維持・中央寄せ）。画像なしスライドでは visible=false にして本文のみ表示。
	_image_rect = TextureRect.new()
	_image_rect.custom_minimum_size = Vector2(960, 540)
	_image_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_image_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_image_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(_image_rect)

	# 本文
	_body_label = Label.new()
	_body_label.add_theme_font_override("font", load("res://fonts/NotoSansJP-Regular.ttf"))
	_body_label.add_theme_font_size_override("font_size", 32)
	_body_label.add_theme_color_override("font_color", Color(1, 1, 1, 0.95))
	_body_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_body_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_body_label.custom_minimum_size = Vector2(1100, 0)
	_body_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(_body_label)

	_build_page_indicator()
	_build_hint_bar()

## ページ表示（上部中央 "1 / 3"）
func _build_page_indicator() -> void:
	var top_margin := MarginContainer.new()
	top_margin.set_anchors_and_offsets_preset(Control.PRESET_TOP_WIDE)
	top_margin.add_theme_constant_override("margin_top", 40)
	top_margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(top_margin)

	_page_label = Label.new()
	_page_label.add_theme_font_override("font", load("res://fonts/NotoSansJP-Regular.ttf"))
	_page_label.add_theme_font_size_override("font_size", 24)
	_page_label.add_theme_color_override("font_color", Color(1, 1, 1, 0.6))
	_page_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_page_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	top_margin.add_child(_page_label)

## 操作ヒント（下部中央）。← 戻る / → Enter 次へ / Esc スキップ。
func _build_hint_bar() -> void:
	var margin := MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
	margin.add_theme_constant_override("margin_bottom", 40)
	margin.add_theme_constant_override("margin_top", 20)
	margin.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(margin)

	var hbox := HBoxContainer.new()
	hbox.alignment = BoxContainer.ALIGNMENT_CENTER
	hbox.add_theme_constant_override("separation", 40)
	hbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	margin.add_child(hbox)

	hbox.add_child(KeyHintBuilder.create_hint("←", "戻る"))
	hbox.add_child(KeyHintBuilder.create_hint("→ / Enter", "次へ"))
	hbox.add_child(KeyHintBuilder.create_hint("Esc", "スキップ"))

# --- スライド表示 ---

func _show_slide(index: int) -> void:
	if index < 0 or index >= _slides.size():
		return
	_current = index
	var slide := _slides[index]

	_body_label.text = slide.body_text
	_page_label.text = "%d / %d" % [index + 1, _slides.size()]
	_update_image(index, slide)

func _update_image(index: int, slide: IntroSlideInfo) -> void:
	# キャッシュ済みならそのまま適用
	if _texture_cache.has(index):
		_apply_texture(_texture_cache[index])
		return

	var resolved := _resolve_image_path(slide.image_path)
	if resolved.is_empty() or not FileAccess.file_exists(resolved):
		# 画像なし / 見つからない → 本文のみ表示
		_image_rect.texture = null
		_image_rect.visible = false
		return

	var image := Image.load_from_file(resolved)
	if image == null:
		_image_rect.texture = null
		_image_rect.visible = false
		return

	var tex := ImageTexture.create_from_image(image)
	_texture_cache[index] = tex
	_apply_texture(tex)

func _apply_texture(tex: Texture2D) -> void:
	_image_rect.visible = true
	_image_rect.texture = tex
	_image_rect.modulate = Color(1, 1, 1, 0)
	var tween := create_tween()
	tween.tween_property(_image_rect, "modulate:a", 1.0, 0.2)

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

# --- ナビゲーション ---

func _next_slide() -> void:
	if _current >= _slides.size() - 1:
		# 最終スライドで「次へ」→ 説明終了、ストアへ
		_go_to_store()
		return
	_show_slide(_current + 1)

func _prev_slide() -> void:
	if _current <= 0:
		return
	_show_slide(_current - 1)

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
	if not event.is_pressed():
		return

	var viewport := get_viewport()
	if not viewport:
		return

	if event.is_action_pressed("ui_cancel"):
		_go_to_store()
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_left"):
		_prev_slide()
		viewport.set_input_as_handled()
	elif event.is_action_pressed("ui_right") or event.is_action_pressed("ui_accept"):
		_next_slide()
		viewport.set_input_as_handled()
