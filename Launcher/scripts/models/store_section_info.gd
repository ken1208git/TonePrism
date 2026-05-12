## ストアセクション情報のデータモデル
## store_sectionsテーブルに対応

extends RefCounted
class_name StoreSectionInfo

var section_id: int = -1
var title: String = ""

## セクションタイプ: 0=通常カテゴリ行, 1=スライドショー, 2=タイルグリッド
var section_type: int = 0

## セクションソース: 'manual','popular','recent','recently_played','genre:X' 等
var section_source: String = "manual"

var display_order: int = 0
var max_display_count: int = 5
var is_visible: bool = true

## このセクションに含まれるゲーム一覧
var games: Array[GameInfo] = []

## ゲームごとの表示テキスト（game_id → display_text）
## 空の場合はゲームタイトルを表示
var game_display_texts: Dictionary = {}
