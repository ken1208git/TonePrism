class_name GameInfoDisplay
extends RefCounted
## ゲーム情報パネルの表示 (背景は game_selection が連続クロスフェードするため本クラスは扱わない)。
## フォーマットヘルパーは GameInfoFormatter を参照

## ゲーム情報パネルを更新する
func update_display(game: GameInfo,
		title_label: Label, creator_tags_container: Container, desc_label: Label,
		players_label: Label, difficulty_val_label: Label,
		playtime_val_label: Label,
		controller_label: Label, online_label: Label) -> void:

	# --- タイトル ---
	var title_text = game.title
	if title_text == null or str(title_text) == "null" or str(title_text) == "<null>":
		title_text = ""

	# --- 説明文 ---
	var desc_text = game.description
	var is_desc_placeholder := false
	if desc_text == null or str(desc_text) == "null" or str(desc_text) == "<null>" or desc_text.strip_edges().is_empty():
		desc_text = "このゲームには説明文がありません。"
		is_desc_placeholder = true  # (#293) 説明なしのプレースホルダは半透明で「本物の説明」と区別

	# --- タグ（作者名、年、ジャンル） ---
	if creator_tags_container:
		for child in creator_tags_container.get_children():
			creator_tags_container.remove_child(child)
			child.queue_free()
		
		var font_bold = load("res://fonts/NotoSansJP-Bold.ttf")
		
		# 作者バッジ (白背景、黒文字、アイコン有)
		var seen_devs = []
		for dev in game.developers:
			var dev_name = dev.get_full_name()  # (#313) 姓/名どちらか空欄でも余分なスペースを出さない
			# (#313) grade 空欄 = 不明 → 期生/教員を一切表示しない。空文字や NULL 由来の "<null>"・非数値文字列を
			# int() で 0 に化けさせ「(教員)」と誤表示しないよう、整数として妥当なときだけ表示する（"0"=教員 / N=N期生）。
			var grade_raw = str(dev.grade).strip_edges() if dev.grade != null else ""
			var grade_str = GameInfoFormatter.get_grade_string(int(grade_raw)) if grade_raw.is_valid_int() else ""
			var unique_key = dev_name + grade_str
			if unique_key in seen_devs:
				continue
			seen_devs.append(unique_key)
			var creator_text = ("%s %s" % [dev_name, grade_str]) if grade_str != "" else dev_name
			
			var bg_panel = PanelContainer.new()
			var p_style = StyleBoxFlat.new()
			p_style.bg_color = Color(1.0, 1.0, 1.0, 1.0) # 白背景
			p_style.corner_radius_top_left = 16
			p_style.corner_radius_top_right = 16
			p_style.corner_radius_bottom_left = 16
			p_style.corner_radius_bottom_right = 16
			p_style.content_margin_left = 12
			p_style.content_margin_right = 14
			p_style.content_margin_top = 4
			p_style.content_margin_bottom = 4
			bg_panel.add_theme_stylebox_override("panel", p_style)
			
			var badge_inner = HBoxContainer.new()
			badge_inner.add_theme_constant_override("separation", 6)
			
			var icon = TextureRect.new()
			icon.texture = load("res://images/person.png")
			if icon.texture:
				icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
				icon.custom_minimum_size = Vector2(20, 20)
				icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
				icon.modulate = Color(0, 0, 0, 1) # 黒色で表示
				badge_inner.add_child(icon)
			
			var lbl = Label.new()
			lbl.text = creator_text
			lbl.add_theme_color_override("font_color", Color(0, 0, 0, 1)) # 黒文字
			lbl.add_theme_font_size_override("font_size", 18)
			if font_bold: lbl.add_theme_font_override("font", font_bold)
			badge_inner.add_child(lbl)
			
			bg_panel.add_child(badge_inner)
			creator_tags_container.add_child(bg_panel)

		# 情報タグを収集
		var temp_tags = []
		if game.release_year > 0:
			temp_tags.append(str(game.release_year))
		for g in game.genre:
			if not g.strip_edges().is_empty():
				temp_tags.append(g)
				
		# 情報バッジUI(半透明白背景)を生成
		for tag_text in temp_tags:
			var lbl = Label.new()
			lbl.text = tag_text
			lbl.add_theme_font_size_override("font_size", 18)
			if font_bold: lbl.add_theme_font_override("font", font_bold)
			
			var style = StyleBoxFlat.new()
			style.bg_color = Color(1.0, 1.0, 1.0, 0.15)
			style.corner_radius_top_left = 16
			style.corner_radius_top_right = 16
			style.corner_radius_bottom_left = 16
			style.corner_radius_bottom_right = 16
			style.content_margin_left = 14
			style.content_margin_right = 14
			style.content_margin_top = 4
			style.content_margin_bottom = 4
			
			lbl.add_theme_stylebox_override("normal", style)
			creator_tags_container.add_child(lbl)

	# --- スペック情報 ---
	# アイコンを白化（invert_color シェーダー適用）
	var _invert_shader = preload("res://resources/shaders/invert_color.gdshader")
	for spec_label in [players_label, difficulty_val_label, playtime_val_label, controller_label, online_label]:
		var icon_node = spec_label.get_parent().get_node_or_null("IconGroup/Icon")
		if not icon_node:
			icon_node = spec_label.get_parent().get_node_or_null("IconGroup/IconClip/Icon")
		if icon_node and icon_node is TextureRect and not icon_node.material:
			var mat = ShaderMaterial.new()
			mat.shader = _invert_shader
			icon_node.material = mat

	# プレイ人数
	var min_p = game.min_players
	var max_p = game.max_players
	var players_text = ""
	if min_p > 0 and max_p > 0:
		if min_p == max_p:
			players_text = "%d人" % min_p
		else:
			players_text = "%d～%d人" % [min_p, max_p]
	elif max_p > 0:
		players_text = "%d人" % max_p
	else:
		players_text = "不明"
	players_label.text = players_text

	# 難易度
	var diff_val = game.difficulty
	if diff_val > 0:
		difficulty_val_label.text = GameInfoFormatter.get_difficulty_text(diff_val)
		_apply_badge_color(difficulty_val_label, diff_val)
		difficulty_val_label.get_parent().show()
	else:
		difficulty_val_label.get_parent().hide()

	# プレイ時間
	var time_val = game.play_time
	if time_val > 0:
		playtime_val_label.text = GameInfoFormatter.get_play_time_text(time_val)
		_apply_badge_color(playtime_val_label, time_val)
		playtime_val_label.get_parent().show()
	else:
		playtime_val_label.get_parent().hide()

	# コントローラー
	controller_label.text = "対応" if game.controller_support else "非対応"
	_apply_controller_badge(controller_label, game.controller_support)

	# 通信対戦
	match game.supported_connection:
		0:
			online_label.text = "非対応"
		1:
			online_label.text = "ローカル"
		2:
			online_label.text = "オンライン"
	_apply_connection_badge(online_label, game.supported_connection)

	# --- ラベル・コンテナ状態リセット ---
	title_label.text = title_text
	if title_label.get_parent().has_method("reset"):
		title_label.get_parent().reset()

	if creator_tags_container and creator_tags_container.get_parent().has_method("reset"):
		creator_tags_container.get_parent().reset()

	desc_label.text = desc_text
	# (#293) 説明文が無いときのプレースホルダは半透明にして、実際の説明文と見分けられるようにする。
	desc_label.modulate.a = 0.45 if is_desc_placeholder else 1.0

## 値に応じたバッジ背景色をラベルに適用（1=緑, 2=黄, 3=赤）
func _apply_badge_color(label: Label, value: int) -> void:
	var color: Color
	if value <= 1:
		color = Color(0.2, 0.7, 0.3, 0.5)   # 緑
	elif value == 2:
		color = Color(0.8, 0.65, 0.1, 0.5)   # 黄
	else:
		color = Color(0.8, 0.25, 0.2, 0.5)   # 赤

	_apply_badge_style(label, color)

## コントローラー対応バッジ（対応=青, 非対応=グレー）
func _apply_controller_badge(label: Label, supported: bool) -> void:
	if supported:
		_apply_badge_style(label, Color(0.2, 0.45, 0.85, 0.5))   # 青
	else:
		_apply_badge_style(label, Color(0.4, 0.4, 0.4, 0.5))     # グレー

## 通信対戦バッジ（非対応=グレー, ローカル=青, オンライン=紫）
func _apply_connection_badge(label: Label, connection: int) -> void:
	match connection:
		1:
			_apply_badge_style(label, Color(0.2, 0.45, 0.85, 0.5))   # 青
		2:
			_apply_badge_style(label, Color(0.6, 0.3, 0.75, 0.5))    # 紫
		_:
			_apply_badge_style(label, Color(0.4, 0.4, 0.4, 0.5))     # グレー

## バッジスタイルを適用する共通関数
func _apply_badge_style(label: Label, color: Color) -> void:
	var style = StyleBoxFlat.new()
	style.bg_color = color
	style.corner_radius_top_left = 6
	style.corner_radius_top_right = 6
	style.corner_radius_bottom_left = 6
	style.corner_radius_bottom_right = 6
	style.content_margin_left = 10
	style.content_margin_right = 10
	style.content_margin_top = 2
	style.content_margin_bottom = 2
	label.add_theme_stylebox_override("normal", style)
