## ゲーム情報を表すデータモデル
## データベースのgamesテーブルに対応

extends RefCounted
class_name GameInfo

## ゲームID（一意の識別子）
var game_id: String = ""

## ゲームタイトル
var title: String = ""

## 説明文
var description: String = ""

## リリース年
var release_year: int = -1

## ジャンルのリスト（データベースではJSON形式またはカンマ区切りで保存）
var genre: Array[String] = []

## 最小プレイヤー数
var min_players: int = -1

## 最大プレイヤー数
var max_players: int = -1

## 難易度（1-3の3段階）
## 1: 易しい, 2: 普通, 3: 難しい
var difficulty: int = -1

## プレイ時間の分類
## 1: ～5分, 2: 5分～15分, 3: 15分以上
var play_time: int = -1

## コントローラーサポート
var controller_support: bool = false

## サムネイル画像のパス（相対パス：games/{game_id}/フォルダからの相対パス）
var thumbnail_path: String = ""

## 背景画像のパス（相対パス：games/{game_id}/フォルダからの相対パス）
var background_path: String = ""

## 実行ファイルのパス（相対パス：games/{game_id}/フォルダからの相対パス）
var executable_path: String = ""

## 表示順序（数値が小さいほど先に表示）
var display_order: int = -1

## 表示/非表示
var is_visible: bool = true

## 操作説明（JSON形式で保存）
var controls: String = ""

## キーマッピング設定（JSON形式で保存）
var key_mapping: String = ""

## 製作者リスト（データベースではdevelopersテーブルとして分離）
var developers: Array[DeveloperInfo] = []

## コンストラクタ
func _init():
	genre = []
	developers = []
	controller_support = false
	is_visible = true

