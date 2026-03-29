## ゲーム情報のテキストフォーマットヘルパー
## 学年表示、難易度テキスト、プレイ時間テキスト、時計表示を担当

extends RefCounted
class_name GameInfoFormatter

static func get_grade_string(grade: int) -> String:
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

static func get_difficulty_text(level: int) -> String:
	match level:
		1: return "簡単"
		2: return "普通"
		3: return "難しい"
		_: return "---"

static func get_play_time_text(level: int) -> String:
	match level:
		1: return "～5分"
		2: return "5分～15分"
		3: return "15分～"
		_: return "---"

static func update_clock(clock_label: Label) -> void:
	if clock_label:
		var time = Time.get_time_dict_from_system()
		clock_label.text = "%02d:%02d" % [time.hour, time.minute]

static func _get_current_fiscal_year() -> int:
	var date = Time.get_date_dict_from_system()
	if date.month >= 4:
		return date.year
	else:
		return date.year - 1
