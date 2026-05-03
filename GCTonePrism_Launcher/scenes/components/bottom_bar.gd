## 共通ボトムバー（操作説明ラベル）
## CanvasLayer ベースでトランジション（modulate）の影響を受けない
extends CanvasLayer

@onready var _panel: Control = $Panel
@onready var _hint_container: HBoxContainer = $Panel/LabelMargin/HintContainer

func _ready():
	set_hints([["Enter", "決定"]])

## キーヒントを設定する
## hints: [["キー名", "アクション名"], ...] 形式の配列
func set_hints(hints: Array) -> void:
	for child in _hint_container.get_children():
		child.queue_free()
	for hint in hints:
		_hint_container.add_child(KeyHintBuilder.create_hint(hint[0], hint[1]))

func get_panel() -> Control:
	return _panel
