extends CanvasLayer

# ErrorManager (AutoLoad)
# アプリケーション全体のエラー表示を管理します

func _ready():
	# 最前面に表示されるようにレイヤーを設定
	layer = 128
	# プロセスモードを常に実行（ポーズ中も表示可能）に設定
	process_mode = Node.PROCESS_MODE_ALWAYS

var _dialog_scene = preload("res://scenes/components/error_dialog.tscn")
var _current_dialog: Control = null
var _current_code: int = 0  # 表示中ダイアログのエラーコード（hide_error の照合用）

func is_error_showing() -> bool:
	return _current_dialog != null

## エラーを表示する。実際に表示できたら true、既に別ダイアログ表示中で
## 抑止された場合は false を返す（呼び出し側が再試行可否を判断できるように）。
func show_error(code: int) -> bool:
	# 既に表示されている場合は何もしない
	if _current_dialog != null:
		return false

	# コードが0（初期値やOK）の場合は、不明なエラーとして扱う
	if code == 0:
		code = ErrorCode.SYSTEM_UNKNOWN_ERROR

	var dialog = _dialog_scene.instantiate()

	add_child(dialog)

	dialog.setup(code)

	_current_dialog = dialog
	_current_code = code

	DialogAnimator.animate_in(dialog, self, "Panel", "ColorRect")
	return true

## 表示中のエラーをフェードアウトして閉じる。
## 自己回復するエラー（例: ランチャー前面化異常 #216 が解消したとき）に使う。
## expected_code を渡すと、表示中ダイアログがそのコードのときだけ閉じる
## （別経路で差し替わった無関係なエラーを誤って閉じないため）。-1 で任意。
func hide_error(expected_code: int = -1) -> void:
	if _current_dialog == null:
		return
	if expected_code != -1 and _current_code != expected_code:
		return
	var dialog := _current_dialog
	_current_dialog = null
	_current_code = 0
	DialogAnimator.animate_out(dialog, self, Callable(), "Panel", "ColorRect")
