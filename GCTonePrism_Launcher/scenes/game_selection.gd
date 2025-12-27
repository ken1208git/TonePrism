extends Control

## ゲーム選択画面（テスト用）
## マイルストーン4: データベース連携

# フォントリソース
var noto_sans_regular: FontFile = null
var noto_sans_bold: FontFile = null

# データベース管理
var db_manager: DatabaseManager = null
var games: Array[GameInfo] = []

func _ready():
	# フォントを読み込む
	noto_sans_regular = load("res://fonts/NotoSansJP-Regular.ttf") as FontFile
	noto_sans_bold = load("res://fonts/NotoSansJP-Bold.ttf") as FontFile
	# フルスクリーン表示
	get_viewport().size_changed.connect(_on_viewport_size_changed)
	_update_layout()
	
	# キー入力を有効化
	set_process_input(true)
	
	# データベースからゲーム情報を読み込む
	_load_games()

func _input(event):
	var viewport = get_viewport()
	if not viewport:
		return
	
	# ESCキーでスクリーンセーバーに戻る
	if event.is_action_pressed("ui_cancel"):
		_transition_to_screensaver()
		viewport.set_input_as_handled()
		return

## スクリーンセーバー画面に遷移
func _transition_to_screensaver():
	get_tree().change_scene_to_file("res://scenes/screensaver.tscn")

## ビューポートサイズ変更時の処理
func _on_viewport_size_changed():
	_update_layout()

## レイアウトの更新
func _update_layout():
	var viewport_size = get_viewport().get_visible_rect().size
	size = viewport_size
	position = Vector2.ZERO
	
	# タイトルのフォントサイズを調整
	var title_label = $Title
	if title_label:
		var title_font_size = clamp(viewport_size.x * 0.04, 24.0, 64.0)
		title_label.add_theme_font_size_override("font_size", title_font_size)
		# フォントを設定（Bold）
		if noto_sans_bold:
			title_label.add_theme_font_override("font", noto_sans_bold)
	
	# メッセージのフォントサイズを調整
	var message_label = $Message
	if message_label:
		var message_font_size = clamp(viewport_size.x * 0.02, 16.0, 32.0)
		message_label.add_theme_font_size_override("font_size", message_font_size)
		# フォントを設定（Regular）
		if noto_sans_regular:
			message_label.add_theme_font_override("font", noto_sans_regular)
		
		# ゲーム情報を表示
		_update_message()

## データベースからゲーム情報を読み込む
func _load_games() -> void:
	db_manager = DatabaseManager.new()
	
	if not db_manager.open():
		var message_label = $Message
		if message_label:
			message_label.text = "データベースを開けませんでした\nデータベースファイルを確認してください"
		return
	
	# ゲーム情報を取得
	games = db_manager.get_all_games()
	
	print("[GameSelection] ゲーム情報を読み込みました: ", games.size(), " 件")
	
	# デバッグ: ゲーム情報をコンソールに出力
	for game in games:
		print("  - ", game.title, " (", game.game_id, ")")
	
	# メッセージを更新
	_update_message()
	
	# データベースを閉じる
	db_manager.close()

## メッセージを更新
func _update_message() -> void:
	var message_label = $Message
	if not message_label:
		return
	
	if games.is_empty():
		message_label.text = "ゲームが見つかりませんでした\nESCキーでスクリーンセーバーに戻ります"
	else:
		var game_count = games.size()
		var game_list = ""
		for i in range(min(5, game_count)):
			game_list += "  - " + games[i].title + "\n"
		if game_count > 5:
			game_list += "  ... 他 " + str(game_count - 5) + " 件\n"
		
		message_label.text = "データベースから " + str(game_count) + " 件のゲームを読み込みました:\n\n" + game_list + "\nESCキーでスクリーンセーバーに戻ります"
