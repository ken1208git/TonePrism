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

## データをクリアする
func clear() -> void:
	filtered_games = []
	initial_game_id = ""
	return_scene = ""
	section_title = ""
