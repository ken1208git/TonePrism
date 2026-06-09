## シーン間データ受け渡し用シングルトン
## ストアブラウズ → カルーセル間のゲームリスト共有に使用

extends Node

## カルーセルに渡すフィルタ済みゲームリスト（空ならDB全件取得）
var filtered_games: Array[GameInfo] = []

## カルーセルで最初にフォーカスするゲームID
var initial_game_id: String = ""

## カルーセルからの戻り先シーンパス
var return_scene: String = ""

## カルーセル画面に表示するセクション名
var section_title: String = ""

## (#315) カルーセルが「最上位画面」か。空ストア (0 セクション) から StoreEntryRouter で store_browse を
## 挟まず直接カルーセルに来た場合に true。戻る先のストアが無いので、game_selection は (1) 戻るボタンを
## 出さず (2) ESC を「戻る」ではなく「退出ダイアログ」にして、store_browse と同じ最上位の退出挙動に揃える。
var carousel_top_level: bool = false

## プレイ中シーン (playing) からゲーム終了で game_selection へ復帰中か (#214)。
## true の場合 game_selection は起動直後に running-view 静止状態を再現し、
## switch_to_normal_view (起動モーションの逆再生) でカルーセルへ戻る。
var returning_from_game: bool = false

## 上記復帰が「中断メニューからの終了 (= 終了中画面を見せた)」由来か。
## true なら running-view 再現も「ゲーム終了中…」で出して連続させる (false=自然終了は「プレイ中」)。
var returning_from_quit: bool = false

## データをクリアする
func clear() -> void:
	filtered_games = []
	initial_game_id = ""
	return_scene = ""
	section_title = ""
	carousel_top_level = false
	returning_from_game = false
	returning_from_quit = false
