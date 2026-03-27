class_name IdleManager
extends RefCounted
## アイドル検知・スクリーンセーバー遷移

const IDLE_WARNING_TIME := 60.0
const IDLE_RESET_TIME := 90.0

var _idle_timer: float = 0.0
var _timeout_dialog: CommonDialog = null

## アイドルタイマーを更新する。trueを返したらスクリーンセーバーに遷移
func update(delta: float, is_paused: bool) -> bool:
	if not is_paused or _timeout_dialog != null:
		_idle_timer += delta

	if _idle_timer >= IDLE_RESET_TIME:
		print("[IdleManager] Idle timeout. Transitioning to screensaver.")
		return true

	if _idle_timer >= IDLE_WARNING_TIME:
		var remaining = int(ceil(IDLE_RESET_TIME - _idle_timer))
		var msg = "長時間操作がなかったため、タイトル画面に戻ります。\n\nあと %d 秒\n\n続ける場合は、何かボタンを押してください。" % remaining

		if _timeout_dialog == null:
			_timeout_dialog = DialogManager.show_message("確認", msg,
				["キャンセル"],
				func(_idx):
					reset()
			)
		else:
			if _timeout_dialog.has_method("set_message"):
				_timeout_dialog.set_message(msg)

	return false

## アイドルタイマーをリセットする
func reset() -> void:
	_idle_timer = 0.0
	if _timeout_dialog != null:
		_timeout_dialog.queue_free()
		_timeout_dialog = null
	DialogManager.close_current_dialog()

## スクリーンセーバーに遷移する
static func transition_to_screensaver(_tree: SceneTree = null) -> void:
	DialogManager.close_current_dialog()
	TransitionManager.change_scene("res://scenes/screensaver.tscn")
