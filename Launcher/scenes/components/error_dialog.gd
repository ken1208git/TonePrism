extends Control

# ダイアログのUI要素への参照
@onready var title_label = $Panel/VBoxContainer/TitleLabel
@onready var message_label = $Panel/VBoxContainer/MessageLabel
@onready var code_label = $Panel/VBoxContainer/CodeLabel

var _error_code: int = 0
var _ui_applied: bool = false  # _update_ui の二重適用 (remedy 二重追記) 防止

# コード別の文言上書き。ここに無いコードは tscn の静的文言をそのまま使う。
# （error_dialog.tscn は汎用文言で設計されているため、スタッフ対応など個別文言が要るコードのみ上書きする）
const _TITLE_OVERRIDES := {
	ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY: "スタッフをお呼びください",
}
const _MESSAGE_OVERRIDES := {
	ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY: "ゲーム画面が正しく表示されていません。\nお近くのスタッフをお呼びください。",
}

# コード別の対処法 (スタッフ向け)。エラー画面に併記し、別途マニュアルを引かなくても対処が分かるようにする
# (ゲーセン筐体のエラー表示と同様。これにより専用の「エラーマニュアル」画面は不要)。
const _REMEDY := {
	ErrorCode.DATABASE_NOT_FOUND: "Manager を起動して DB を初期化してください。共有フォルダの接続も確認。",
	ErrorCode.DATABASE_CONNECTION_FAILED: "DB が他で開かれていないか、共有フォルダの接続を確認してください。",
	ErrorCode.DATABASE_TABLE_MISSING: "Manager を起動して DB を最新スキーマに更新してください。",
	ErrorCode.DATABASE_QUERY_FAILED: "DB 破損の可能性。Manager でバックアップから復元を検討してください。",
	ErrorCode.DATABASE_DATA_INVALID: "Manager の「ゲーム」タブでデータを確認・修正してください。",
	ErrorCode.DATABASE_NO_GAMES_REGISTERED: "Manager の「ゲーム」タブからゲームを追加してください。",
	ErrorCode.GAME_EXECUTION_FAILED: "サービスモード(Ctrl+Alt+F12)の「ゲーム動作テスト」で exe を一括チェックできます。",
	ErrorCode.GAME_EXECUTABLE_NOT_FOUND: "Manager の「ゲーム」編集で実行ファイルのパスを確認してください。共有フォルダ接続も確認。",
	ErrorCode.GAME_PATH_INVALID: "Manager の「ゲーム」編集で実行ファイルのパスを設定し直してください。",
	ErrorCode.GAME_PERMISSION_DENIED: "ゲーム exe の実行権限/ブロック解除と、共有フォルダのアクセス権を確認してください。",
	ErrorCode.GAME_LAUNCHER_FOREGROUND_ANOMALY: "改善しなければサービスモード(Ctrl+Alt+F12)で診断・再起動してください。",
	ErrorCode.RESOURCE_IMAGE_NOT_FOUND: "Manager で該当ゲームのサムネ/背景画像を再設定してください。",
	ErrorCode.RESOURCE_FONT_NOT_FOUND: "インストールが不完全な可能性。再インストールを検討してください。",
	ErrorCode.SYSTEM_CONFIG_ERROR: "設定ファイルを確認、または再インストールを検討してください。",
	ErrorCode.SYSTEM_FILE_ACCESS_ERROR: "共有フォルダ/ディスクのアクセス権・空き容量を確認してください。",
	ErrorCode.SYSTEM_UNKNOWN_ERROR: "サービスモード(Ctrl+Alt+F12)の「簡易ログ確認」で詳細を確認してください。",
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
	if _ui_applied:
		return
	_ui_applied = true
	# 基本のメッセージ / タイトルは tscn ファイルで静的に設定済み。
	# コード別の上書きがあれば適用する（スタッフ対応文言など）。
	code_label.text = "Error Code: E-%04d" % _error_code
	if _TITLE_OVERRIDES.has(_error_code) and title_label:
		title_label.text = _TITLE_OVERRIDES[_error_code]
	if message_label:
		# メッセージ (上書き or tscn 既定) に、コード別の対処法を併記する。
		var msg: String = _MESSAGE_OVERRIDES[_error_code] if _MESSAGE_OVERRIDES.has(_error_code) else message_label.text
		if _REMEDY.has(_error_code):
			msg += "\n\n【スタッフの方へ】" + _REMEDY[_error_code]
		message_label.text = msg

	print("[ErrorDialog] Showing error E-%04d" % _error_code)
