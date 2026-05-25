extends Node
## Autoload: スタッフ向けサービスモード (#74, SPEC §機能23)。
## Ctrl+Alt+F12 で全画面の診断・管理オーバーレイを開閉する。60 秒無操作で自動的に閉じて通常画面へ復帰する
## (サービスモードを開けっ放しで離れる事故防止)。
##
## ランチャーが前面の状況での使用を想定 (ゲーム前面中は Ctrl+Alt+F12 がゲームに奪われ届かないため対象外。
## スタッフはゲーム終了後に使う)。本 autoload は入力検知とライフサイクルのみを担い、UI は service_mode_overlay が持つ。

const IDLE_TIMEOUT_SEC := 60.0
const ServiceModeOverlay := preload("res://scripts/service_mode_overlay.gd")

var _overlay: CanvasLayer = null
var _open: bool = false
var _idle_sec: float = 0.0


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
	_overlay.open_overlay()
	print("[ServiceMode] サービスモードを開いた")


func close() -> void:
	if not _open:
		return
	_open = false
	if _overlay:
		_overlay.close_overlay()
	print("[ServiceMode] サービスモードを閉じた")
