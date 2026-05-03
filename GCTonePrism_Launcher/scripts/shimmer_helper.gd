## シマーシェーダーをコントロールに適用するヘルパー
## プログレスバーのフィルと、読み込み中ラベル（LOADING）で共有利用する

class_name ShimmerHelper
extends RefCounted

const SHIMMER_SHADER_PATH := "res://resources/shaders/progress_shimmer.gdshader"

## プログレスバー用（暗めのベース → 白ピークで流れるフィル）
static func apply(control: Control) -> void:
	_apply(control, 0.3)

## ラベル用（明るめのベース：半透明カード越しでも背景より明るく保つ）
static func apply_to_label(control: Control) -> void:
	_apply(control, 0.5)

## 任意パラメータでシマーマテリアルを適用する（明るい背景に暗い字、等の用途向け）
static func apply_with_params(control: Control, base: float, peak: float = 1.0,
		width: float = 0.18, speed: float = 1.2) -> void:
	if not control:
		return
	var mat := ShaderMaterial.new()
	mat.shader = load(SHIMMER_SHADER_PATH)
	mat.set_shader_parameter("base_brightness", base)
	mat.set_shader_parameter("shimmer_peak", peak)
	mat.set_shader_parameter("shimmer_width", width)
	mat.set_shader_parameter("shimmer_speed", speed)
	control.material = mat
	_update_size_uniform(control)
	if not control.resized.is_connected(_on_control_resized):
		control.resized.connect(_on_control_resized.bind(control))

static func _apply(control: Control, base_brightness: float) -> void:
	if not control:
		return
	var mat := ShaderMaterial.new()
	mat.shader = load(SHIMMER_SHADER_PATH)
	mat.set_shader_parameter("base_brightness", base_brightness)
	control.material = mat
	_update_size_uniform(control)
	# サイズ変更時に uniform を更新
	if not control.resized.is_connected(_on_control_resized):
		control.resized.connect(_on_control_resized.bind(control))

static func _on_control_resized(control: Control) -> void:
	_update_size_uniform(control)

static func _update_size_uniform(control: Control) -> void:
	if not control or not control.material is ShaderMaterial:
		return
	var size = control.size
	if size.x <= 0.0 or size.y <= 0.0:
		# サイズ未確定の場合は遅延
		control.call_deferred("queue_redraw")
		return
	(control.material as ShaderMaterial).set_shader_parameter("node_size", size)
