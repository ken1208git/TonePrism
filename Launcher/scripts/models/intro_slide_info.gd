## 初回説明スライドを表すデータモデル (#253)
## データベースの intro_slides テーブル（DB v22）に対応。
## 画像実体は guide/ フォルダにファイル別管理し、本モデルは相対パス文字列のみ保持する。

extends RefCounted
class_name IntroSlideInfo

## スライドID（一意の識別子）
var slide_id: int = -1

## 表示順序（数値が小さいほど先に表示）
var display_order: int = -1

## 本文テキスト
var body_text: String = ""

## 画像のパス（相対パス：guide/ フォルダからの相対。画像なしは空文字）
var image_path: String = ""

## 表示/非表示
var is_visible: bool = true
