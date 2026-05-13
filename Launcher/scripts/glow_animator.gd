## グローアニメーション（ブリージングエフェクト）を管理
## フォーカス枠やスタイルの発光を制御する

extends RefCounted
class_name GlowAnimator

var _glow_styles: Array[StyleBoxFlat] = []
var _glow_timer: float = 0.0

## グローアニメーションを更新する（毎フレーム呼び出し）
func update(delta: float) -> void:
	_glow_timer += delta
	var glow_alpha = 0.5 + 0.3 * sin(_glow_timer * 3.0)

	for style in _glow_styles:
		if style:
			var c = Color(1, 1, 1, glow_alpha)
			style.shadow_color = c
			style.border_color = c

## グローアニメーション対象にスタイルを追加する
func add_style(style: StyleBoxFlat) -> void:
	_glow_styles.append(style)

## StaticFocusBorderのスタイルをグローリストに登録する
func register_focus_border(static_focus_border: Panel) -> void:
	if not static_focus_border:
		return
	var style = static_focus_border.get_theme_stylebox("panel")
	if style is StyleBoxFlat:
		add_style(style)
