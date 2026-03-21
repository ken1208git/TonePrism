class_name GameInfoDisplay
extends RefCounted
## ゲーム情報の表示・背景アニメーション・フォーマットヘルパー

var _bg_tween: Tween = null

## ゲーム情報パネルを更新する
func update_display(game: GameInfo, slide_dir_y: int,
		title_label: Label, creator_label: Label, desc_label: Label,
		players_label: Label, difficulty_bar: ProgressBar, difficulty_val_label: Label,
		playtime_bar: ProgressBar, playtime_val_label: Label,
		controller_label: Label, online_label: Label,
		background_texture: TextureRect, background_old: TextureRect,
		owner_node: Node) -> void:

	# --- タイトル ---
	var title_text = game.title
	if title_text == null or str(title_text) == "null" or str(title_text) == "<null>":
		title_text = ""

	# --- 説明文 ---
	var desc_text = game.description
	if desc_text == null or str(desc_text) == "null" or str(desc_text) == "<null>" or desc_text.strip_edges().is_empty():
		desc_text = "このゲームには説明文がありません。"

	# --- 製作者情報 ---
	var creator_text = ""
	var seen_devs = []
	for dev in game.developers:
		var dev_name = "%s %s" % [dev.last_name, dev.first_name]
		var grade_val = int(dev.grade) if dev.grade != null else -1
		var grade_str = _get_grade_string(grade_val)

		var unique_key = dev_name + grade_str
		if unique_key in seen_devs:
			continue
		seen_devs.append(unique_key)
		creator_text += "%s %s　" % [dev_name, grade_str]

	if game.release_year > 0:
		creator_text += "%d年　" % game.release_year
	if not game.genre.is_empty():
		creator_text += "ジャンル: " + ", ".join(game.genre)

	# --- スペック情報 ---
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
		difficulty_bar.value = diff_val
		difficulty_val_label.text = _get_difficulty_text(diff_val)
		_update_bar_style(difficulty_bar, diff_val)
		difficulty_bar.get_parent().show()
	else:
		difficulty_bar.get_parent().hide()

	# プレイ時間
	var time_val = game.play_time
	if time_val > 0:
		playtime_bar.value = time_val
		playtime_val_label.text = _get_play_time_text(time_val)
		_update_bar_style(playtime_bar, time_val)
		playtime_bar.get_parent().show()
	else:
		playtime_bar.get_parent().hide()

	# コントローラー
	controller_label.text = "対応" if game.controller_support else "非対応"

	# 通信対戦
	online_label.text = "対応" if game.supported_connection > 0 else "非対応"

	# --- ラベル反映 ---
	title_label.text = title_text
	if title_label.get_parent().has_method("reset"):
		title_label.get_parent().reset()

	if creator_label:
		creator_label.text = creator_text
		if creator_label.get_parent().has_method("reset"):
			creator_label.get_parent().reset()

	desc_label.text = desc_text

	# --- 背景画像更新 ---
	_update_background(game, slide_dir_y, background_texture, background_old, owner_node)

## 背景画像のTweenアニメーション
func _update_background(game: GameInfo, slide_dir_y: int,
		background_texture: TextureRect, background_old: TextureRect,
		owner_node: Node) -> void:
	if _bg_tween and _bg_tween.is_valid():
		_bg_tween.kill()

	# 現在の背景を古いテクスチャに移す
	if background_texture.texture != null:
		background_old.texture = background_texture.texture
		background_old.modulate = background_texture.modulate
		background_old.position = background_texture.position
	else:
		background_old.texture = null
		background_old.modulate = Color(1, 1, 1, 0)

	var bg_path = GameLauncher.resolve_path(game.background_path, game.game_id)
	if not bg_path.is_empty() and FileAccess.file_exists(bg_path):
		var img = Image.load_from_file(bg_path)
		background_texture.texture = ImageTexture.create_from_image(img)
	else:
		background_texture.texture = null
		if not game.background_path.is_empty():
			push_warning("[GameInfoDisplay] Background not found: %s" % bg_path)

	# Tweenアニメーション
	_bg_tween = owner_node.create_tween()
	_bg_tween.set_parallel(true)

	if slide_dir_y == 0:
		background_texture.position = Vector2.ZERO
		background_texture.modulate = Color(1, 1, 1, 0)
		_bg_tween.tween_property(background_texture, "modulate", Color(1, 1, 1, 1), 0.3)
	else:
		var offset_y = 50.0 * -slide_dir_y
		background_texture.position = Vector2(0, offset_y)
		background_texture.modulate = Color(1, 1, 1, 0)

		_bg_tween.tween_property(background_texture, "position", Vector2.ZERO, 0.3).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
		_bg_tween.tween_property(background_texture, "modulate", Color(1, 1, 1, 1), 0.3)

		var old_target_y = background_old.position.y + (50 * slide_dir_y)
		_bg_tween.tween_property(background_old, "position", Vector2(0, old_target_y), 0.3).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
		_bg_tween.tween_property(background_old, "modulate", Color(1, 1, 1, 0), 0.3)

## 時計を更新する
static func update_clock(clock_label: Label) -> void:
	if clock_label:
		var time = Time.get_time_dict_from_system()
		clock_label.text = "%02d:%02d" % [time.hour, time.minute]

# --- フォーマットヘルパー ---

func _get_current_fiscal_year() -> int:
	var date = Time.get_date_dict_from_system()
	if date.month >= 4:
		return date.year
	else:
		return date.year - 1

func _get_grade_string(grade: int) -> String:
	if grade == 0:
		return "(教員)"
	var fy = _get_current_fiscal_year()
	var base_year = 1975
	var school_year = (fy - base_year) - grade
	if school_year >= 1 and school_year <= 3:
		return "(%d年生)" % school_year
	elif school_year > 3:
		return "(卒業生: %d期生)" % grade
	else:
		return "(%d期生)" % grade

func _get_difficulty_text(level: int) -> String:
	match level:
		1: return "簡単"
		2: return "普通"
		3: return "難しい"
		_: return "---"

func _get_play_time_text(level: int) -> String:
	match level:
		1: return "～5分"
		2: return "5分～15分"
		3: return "15分～"
		_: return "---"

func _update_bar_style(bar: ProgressBar, value: int) -> void:
	var style = StyleBoxFlat.new()
	style.set_corner_radius_all(4)
	if value <= 1:
		style.bg_color = Color(0.2, 0.8, 0.2, 1.0)
	elif value == 2:
		style.bg_color = Color(0.9, 0.9, 0.2, 1.0)
	else:
		style.bg_color = Color(0.9, 0.2, 0.2, 1.0)
	bar.add_theme_stylebox_override("fill", style)

	var bg = StyleBoxFlat.new()
	bg.set_corner_radius_all(4)
	bg.bg_color = Color(0.2, 0.2, 0.2, 1.0)
	bar.add_theme_stylebox_override("background", bg)
