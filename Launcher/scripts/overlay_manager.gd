extends Node
## Autoload: 中断メニュー (overlay_menu) の表示制御 (#30)。
## LauncherCompanion.trigger_received (HOME / Guide) で開閉トグルする。
## Companion の sensor は watch 中 (=ゲーム実行中) のみ発火するため、トリガはゲーム中限定。
## メニューの選択結果 (再開 / 終了して選択画面へ) を signal で再発火し、game_selection 側が
## game_launcher につなぐ (配線)。

signal resume_requested()
signal quit_to_selection_requested()

var _overlay: Window = null
var _open: bool = false


func _ready() -> void:
	var packed := load("res://scenes/overlay_menu.tscn") as PackedScene
	if packed == null:
		push_warning("[OverlayManager] overlay_menu.tscn の load 失敗、中断メニュー無効")
		return
	_overlay = packed.instantiate()
	add_child(_overlay)
	_overlay.resume_requested.connect(_on_resume)
	_overlay.quit_to_selection_requested.connect(_on_quit)

	# トリガ (HOME / Guide) で開閉トグル。autoload 順で LauncherCompanion が先に居る前提だが防御的に確認。
	var companion := get_node_or_null("/root/LauncherCompanion")
	if companion:
		companion.trigger_received.connect(_on_trigger)
	else:
		push_warning("[OverlayManager] LauncherCompanion 不在、トリガ連携なし")


func _on_trigger(_source: String) -> void:
	toggle()


func is_open() -> bool:
	return _open


func toggle() -> void:
	if _open:
		close()
	else:
		open()


func open() -> void:
	if _open or _overlay == null:
		return
	_open = true
	_overlay.show_overlay()
	print("[OverlayManager] 中断メニューを開いた")


func close() -> void:
	if not _open or _overlay == null:
		return
	_open = false
	_overlay.hide_overlay()
	print("[OverlayManager] 中断メニューを閉じた")


func _on_resume() -> void:
	close()
	resume_requested.emit()


func _on_quit() -> void:
	close()
	quit_to_selection_requested.emit()
