## 製作者情報を表すデータモデル
## データベースのdevelopersテーブルに対応

extends RefCounted
class_name DeveloperInfo

## 製作者ID（オートインクリメント）
var id: int = -1

## ゲームID（games.game_idを参照）
var game_id: String = ""

## 姓
var last_name: String = ""

## 名
var first_name: String = ""

## 期生（0を指定すると「教員」と表記）
var grade: String = ""

## フルネームを取得
func get_full_name() -> String:
	# (#313) 姓のみ／名のみ登録が許可されたため、空欄側の余分なスペースを出さない。
	if last_name.is_empty():
		return first_name
	if first_name.is_empty():
		return last_name
	return last_name + " " + first_name

## 期生表示を取得（0の場合は「教員」と表示）
func get_grade_display() -> String:
	if grade == "0":
		return "教員"
	if grade.is_empty():
		return ""
	return grade + "期生"

