extends Control

## スクリーンセーバー画面
## マイルストーン3: 基本画面・画面遷移

## 画面状態
enum ScreenState {
	LOGO_DISPLAY,  # ロゴ表示画面（初期状態）
	SLIDESHOW      # ゲームプレビュースライドショー（将来実装）
}

var current_state: ScreenState = ScreenState.LOGO_DISPLAY
var idle_timer: float = 0.0
var idle_timeout: float = 30.0  # 30秒でスライドショーに遷移

# フォントリソース
var noto_sans_regular: FontFile = null
var noto_sans_bold: FontFile = null

func _ready():
	# フォントを読み込む
	noto_sans_regular = load("res://fonts/NotoSansJP-Regular.ttf") as FontFile
	noto_sans_bold = load("res://fonts/NotoSansJP-Bold.ttf") as FontFile
	# フルスクリーン表示
	get_viewport().size_changed.connect(_on_viewport_size_changed)
	_update_layout()
	
	# キー入力とコントローラー入力を有効化
	set_process_input(true)
	set_process(true)
	
	# ラベルのテキストをコードから強制的に変更
	var message_label = $LogoContainer/StartMessage
	if message_label:
		message_label.text = "PRESS ANY KEY"

func _process(delta):
	# アイドルタイマーの更新
	if current_state == ScreenState.LOGO_DISPLAY:
		idle_timer += delta
		
		# PRESS ANY KEY のブリージングアニメーション
		# 周期2秒程度 (3.0 rad/sec)
		# alpha: 0.2 ~ 1.0 (少し広めに)
		var scroll_alpha = 0.6 + 0.4 * sin(idle_timer * 3.0)
		var message_label = $LogoContainer/StartMessage
		if message_label:
			message_label.modulate.a = scroll_alpha

		if idle_timer >= idle_timeout:
			# スライドショーに遷移（将来実装）
			# _transition_to_slideshow()
			pass

func _input(event):
	var viewport = get_viewport()
	if not viewport:
		return
	
	# キー入力、マウスボタン、コントローラーボタンでゲーム選択画面に遷移
	# マウス移動（Motion）は除外する（誤作動防止）
	if event is InputEventKey or event is InputEventMouseButton or event is InputEventJoypadButton:
		if event.is_pressed():
			_transition_to_game_selection()
			viewport.set_input_as_handled()
			return
	
	# 任意のキー/ボタンでロゴ表示画面に戻る（スライドショー表示時）
	if current_state == ScreenState.SLIDESHOW:
		if event is InputEventKey or event is InputEventJoypadButton:
			_transition_to_logo_display()
			viewport.set_input_as_handled()
			return

## ゲーム選択画面に遷移
func _transition_to_game_selection():
	get_tree().change_scene_to_file("res://scenes/game_selection.tscn")

## ロゴ表示画面に遷移
func _transition_to_logo_display():
	current_state = ScreenState.LOGO_DISPLAY
	idle_timer = 0.0
	# 将来実装: ロゴ表示のUIを表示

## スライドショーに遷移（将来実装）
func _transition_to_slideshow():
	current_state = ScreenState.SLIDESHOW
	# 将来実装: スライドショーのUIを表示

## ビューポートサイズ変更時の処理
func _on_viewport_size_changed():
	_update_layout()

## レイアウトの更新
func _update_layout():
	# フルスクリーン表示を確保
	var viewport_size = get_viewport().get_visible_rect().size
	# size = viewport_size  <-- アンカー設定と競合するため削除
	# position = Vector2.ZERO <-- アンカー設定と競合するため削除
	
	# ロゴコンテナのサイズを画面サイズに応じて調整
	var logo_container = $LogoContainer
	if logo_container:
		# 画面サイズの30-40%程度の幅に設定（最小300px、最大600px）
		var base_width = viewport_size.x * 0.35
		var container_width = clamp(base_width, 300.0, 600.0)
		var container_height = container_width * 0.5  # アスペクト比を維持
		
		logo_container.offset_left = -container_width / 2
		logo_container.offset_top = -container_height / 2
		logo_container.offset_right = container_width / 2
		logo_container.offset_bottom = container_height / 2
		
		# ロゴ画像のサイズを画面サイズに応じて調整
		var logo_texture = logo_container.get_node("Logo")
		var message_label = logo_container.get_node("StartMessage")
		
		if logo_texture:
			# ロゴのサイズ: 画面幅の30-40%程度（最小200px、最大500px）
			var logo_width = clamp(viewport_size.x * 0.35, 200.0, 500.0)
			# アスペクト比を維持するため、高さは画像のアスペクト比に基づいて計算
			# 画像の実際のアスペクト比を取得
			var texture = logo_texture.texture
			if texture:
				var image = texture.get_image()
				if image:
					var aspect_ratio = float(image.get_width()) / float(image.get_height())
					var logo_height = logo_width / aspect_ratio
					logo_texture.custom_minimum_size = Vector2(logo_width, logo_height)
		
		if message_label:
			# メッセージのフォントサイズ: ロゴの約1/3（最小16px、最大32px）
			var message_font_size = clamp(viewport_size.x * 0.02, 16.0, 32.0)
			message_label.add_theme_font_size_override("font_size", message_font_size)
			# フォントを設定（Bold）
			if noto_sans_bold:
				message_label.add_theme_font_override("font", noto_sans_bold)
