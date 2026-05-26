extends Node
## スタッフ向けサービスモードの開閉を管理する。
## Ctrl+Alt+F12 で全画面の診断・管理画面を開閉する。60 秒間操作がないと自動的に閉じて通常画面へ戻る
## (開けっ放しで離れてしまう事故を防ぐため)。
##
## ランチャーが手前に表示されている状態での使用を想定している (ゲームが手前のときはキー入力がゲームに渡るので
## 反応しない。ゲームを終了してから使う)。このスクリプトは開閉の管理だけを行い、画面そのものは service_mode_overlay が持つ。

const IDLE_TIMEOUT_SEC := 60.0
const ServiceModeOverlay := preload("res://scripts/service_mode_overlay.gd")

var _overlay: CanvasLayer = null
var _open: bool = false
var _idle_sec: float = 0.0
var _prev_paused: bool = false  # 開く前の pause 状態 (閉じる時に復元)
var _prev_mouse_mode: int = Input.MOUSE_MODE_VISIBLE  # 開く前のカーソル表示状態 (閉じる時に復元)


func _ready() -> void:
	# ダイアログ等で tree.paused でも開閉・自動復帰を効かせる。
	process_mode = Node.PROCESS_MODE_ALWAYS


func _input(event: InputEvent) -> void:
	# Ctrl+Alt+F12 で開閉トグル (スタッフのみ知る隠しコンボ)。
	if event is InputEventKey and event.pressed and not event.echo \
			and event.keycode == KEY_F12 and event.ctrl_pressed and event.alt_pressed:
		get_viewport().set_input_as_handled()
		toggle()
		return
	# 開いている間は何か操作があればアイドルタイマーをリセット (無操作 60 秒で自動復帰)。
	if _open:
		_idle_sec = 0.0


func _process(delta: float) -> void:
	if not _open:
		return
	_idle_sec += delta
	if _idle_sec >= IDLE_TIMEOUT_SEC:
		print("[ServiceMode] 60 秒無操作のため自動的に閉じる")
		close()


func is_open() -> bool:
	return _open


## アイドルタイマーをリセットする。overlay 側が入力を消費 (set_input_as_handled) すると、
## _input フェーズで子の overlay が親の本 autoload より先に処理され本 _input がスキップされ得る
## (Esc/←/→ 等)。その経路でも overlay からこれを呼んでもらい無操作判定を確実にリセットする。
func notify_activity() -> void:
	_idle_sec = 0.0


func toggle() -> void:
	if _open:
		close()
	else:
		open()


func open() -> void:
	if _open:
		return
	if _overlay == null:
		_overlay = ServiceModeOverlay.new()
		add_child(_overlay)
	_open = true
	_idle_sec = 0.0
	# 裏のシーン (ブラウズ/カルーセル等) を凍結して入力競合・背面の動作を止める。
	# 本 autoload と overlay は PROCESS_MODE_ALWAYS なので paused でも動き続ける。
	_prev_paused = get_tree().paused
	get_tree().paused = true
	# Ctrl+Alt+F12 (キーボード) で開くのでカーソルは隠して開始。以後 overlay 側がマウス移動で表示・
	# キー/パッドで非表示に切替 (ランチャー他画面と同じ挙動)。閉じる時に元の状態へ戻す。
	_prev_mouse_mode = Input.mouse_mode
	Input.mouse_mode = Input.MOUSE_MODE_HIDDEN
	_overlay.open_overlay()
	print("[ServiceMode] サービスモードを開いた")


func close() -> void:
	if not _open:
		return
	_open = false
	if _overlay:
		_overlay.close_overlay()
	get_tree().paused = _prev_paused  # pause 状態を復元 (開く前が paused でなければ解除)
	Input.mouse_mode = _prev_mouse_mode  # カーソル表示状態を復元
	print("[ServiceMode] サービスモードを閉じた")
