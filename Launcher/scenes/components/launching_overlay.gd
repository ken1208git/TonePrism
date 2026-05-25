## ゲーム起動中・プレイ中オーバーレイ
## 起動中（ゲームウィンドウが立ち上がるまで）と、プレイ中（背面にいる時に alt+tab で見える）の表示を担当。
## 状態: LAUNCHING（"ゲーム起動中..." + シマー）/ PLAYING（"プレイ中" + 静かな点滅）
##
## ルートを Control にして z_index=50 で描画順を制御している。
## 選択中のカルーセルカードは z_index=100 なので白オーバーレイの上に出て、
## "うっすら白" の影響を受けない。
class_name LaunchingOverlay
extends Control

enum State {
	LAUNCHING,
	PLAYING,
	QUITTING,
}

@export var fade_duration: float = 0.55

@onready var _white_overlay: ColorRect = $WhiteOverlay
@onready var _status_label: Label = $RightContent/StatusLabel
@onready var _title_label: Label = $RightContent/TitleScrollWrapper/TitleLabel

var _current_state: State = State.LAUNCHING

func _ready() -> void:
	visible = false
	modulate.a = 0.0
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	# ステータステキストにシマーシェーダーを適用（明るい背景に暗い字用、暗→中間→暗の波）
	# 状態切替時に shimmer_speed だけ変える
	if _status_label:
		ShimmerHelper.apply_with_params(_status_label, 0.15, 0.55, 0.25, 1.5)

func show_for_game(game_title: String, state: State = State.LAUNCHING, instant: bool = false) -> void:
	if _title_label:
		_title_label.text = game_title
	set_state(state)
	visible = true

	# instant: フェード無しで即可視 (プレイ中シーンからの復帰で、直後に hide_overlay へ繋ぐ用)。
	if instant:
		modulate.a = 1.0
		return

	# フェードイン（QUINT easing で映画的に）
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 1.0, fade_duration)\
		.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)

func set_state(state: State) -> void:
	_current_state = state
	if not _status_label:
		return
	match state:
		State.LAUNCHING:
			_status_label.text = "ゲーム起動中..."
			_set_shimmer_speed(0.7)  # プレイ中と同じ速度に統一
		State.PLAYING:
			_status_label.text = "プレイ中"
			_set_shimmer_speed(0.7)
		State.QUITTING:
			_status_label.text = "ゲーム終了中..."
			_set_shimmer_speed(0.7)

func _set_shimmer_speed(speed: float) -> void:
	if _status_label and _status_label.material is ShaderMaterial:
		(_status_label.material as ShaderMaterial).set_shader_parameter("shimmer_speed", speed)

func hide_overlay() -> void:
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, fade_duration)\
		.set_trans(Tween.TRANS_QUINT).set_ease(Tween.EASE_OUT)
	tween.tween_callback(func():
		visible = false
	)
