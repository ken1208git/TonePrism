extends Control

# ダイアログのUI要素への参照
@onready var title_label = $Panel/VBoxContainer/TitleLabel
@onready var message_label = $Panel/VBoxContainer/MessageLabel
@onready var code_label = $Panel/VBoxContainer/CodeLabel

var _error_code: int = 0

# コード別の文言上書き。ここに無いコードは tscn の静的文言をそのまま使う。
# （error_dialog.tscn は汎用文言で設計されているため、スタッフ対応など個別文言が要るコードのみ上書きする）
const _TITLE_OVERRIDES := {
	ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY: "スタッフをお呼びください",
}
const _MESSAGE_OVERRIDES := {
	ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY: "ゲーム画面が正しく表示されていません。\nお近くのスタッフをお呼びください。",
}


func _ready():
	# 初期化時に保留されていた内容があれば反映
	if _error_code != 0:
		_update_ui()

func setup(code: int):
	_error_code = code

	# ノード準備完了していれば即反映
	# 未完了なら_readyで呼ばれるのを待つ
	if is_node_ready():
		_update_ui()

func _update_ui():
	# 基本のメッセージ / タイトルは tscn ファイルで静的に設定済み。
	# コード別の上書きがあれば適用する（スタッフ対応文言など）。
	code_label.text = "Error Code: E-%04d" % _error_code
	if _TITLE_OVERRIDES.has(_error_code) and title_label:
		title_label.text = _TITLE_OVERRIDES[_error_code]
	if _MESSAGE_OVERRIDES.has(_error_code) and message_label:
		message_label.text = _MESSAGE_OVERRIDES[_error_code]

	print("[ErrorDialog] Showing error E-%04d" % _error_code)
