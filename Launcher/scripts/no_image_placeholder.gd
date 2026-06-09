## サムネ/バナー画像が未登録のときに表示する共通プレースホルダ (#316)。
##
## カルーセルの no-image 表示 (明るいグレーの箱 + 灰字「NO IMAGE」、ガラケーの画像未登録連絡先風) に
## オーバーレイ / プレイ中 / ストアのサムネ・スライド・パネルを統一する。各所でバラバラに
## 「暗背景 + ラベル」「何も出さない」だった no-image をここ 1 箇所に集約し、見た目を一致させる。
##
## loading (暗ヴェール Color(0.08…) + 白50%「LOADING」, StoreBrowseBuilder._create_loading_label) とは
## 別デザインにして、「読込中 (暗)」と「画像なし (明)」を視覚的に区別する。黒系の no-image だと loading と
## 混同するため、あえて明るいグレーにしている。
class_name NoImagePlaceholder
extends RefCounted

## 箱の背景色 (明るいグレー)。カルーセルは「白パネル × alpha フェード × 暗背景」で結果的に灰に見える
## 副産物だが、ここでは位置に依らず一定の「意図したグレー」を単一定数で持ち全箇所で一致させる。
## 実機で見て濃さを最終調整する場合はこの 1 行だけ変更する。
const BG_COLOR := Color(0.85, 0.85, 0.85, 1.0)
## 「NO IMAGE」文字色 (中間グレー)。カルーセル NoImageLabel と同じ。
const TEXT_COLOR := Color(0.5, 0.5, 0.5, 1.0)

static var _font_bold: Font = null

static func _get_font_bold() -> Font:
	if _font_bold == null:
		_font_bold = load("res://fonts/NotoSansJP-Bold.ttf")
	return _font_bold

## no-image プレースホルダを生成して返す。親 (サムネ枠など) に add_child するだけで全面を覆う。
## corner_radius: 角丸半径 (設置先コンテナに合わせる)。font_size: 「NO IMAGE」文字サイズ (枠の大きさに合わせる)。
static func make(corner_radius: int = 16, font_size: int = 28) -> Control:
	var root := Panel.new()
	root.name = "NoImagePlaceholder"
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.mouse_filter = Control.MOUSE_FILTER_IGNORE  # フォーカス/クリックを奪わない (タイルの focus は親 Panel)
	var sb := StyleBoxFlat.new()
	sb.bg_color = BG_COLOR
	sb.set_corner_radius_all(corner_radius)
	root.add_theme_stylebox_override("panel", sb)

	var lbl := Label.new()
	lbl.text = "NO IMAGE"
	lbl.add_theme_font_size_override("font_size", font_size)
	lbl.add_theme_color_override("font_color", TEXT_COLOR)
	var f := _get_font_bold()
	if f:
		lbl.add_theme_font_override("font", f)
	lbl.set_anchors_preset(Control.PRESET_FULL_RECT)
	lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	root.add_child(lbl)
	return root
