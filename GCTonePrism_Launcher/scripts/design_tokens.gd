## Material Design 3 デザイントークン定数
## docs/design_reference.md の値を GDScript 定数として定義
## 使用例: DesignTokens.COLOR_PRIMARY, DesignTokens.SHAPE_MEDIUM
class_name DesignTokens


# =============================================================================
# Typography — フォントサイズ (px)
# =============================================================================
# 注: 日本語テキストは +1px 推奨（必要に応じて調整）

# Display
const FONT_SIZE_DISPLAY_LARGE := 57
const FONT_SIZE_DISPLAY_MEDIUM := 45
const FONT_SIZE_DISPLAY_SMALL := 36

# Headline
const FONT_SIZE_HEADLINE_LARGE := 32
const FONT_SIZE_HEADLINE_MEDIUM := 28
const FONT_SIZE_HEADLINE_SMALL := 24

# Title
const FONT_SIZE_TITLE_LARGE := 22
const FONT_SIZE_TITLE_MEDIUM := 16
const FONT_SIZE_TITLE_SMALL := 14

# Body
const FONT_SIZE_BODY_LARGE := 16
const FONT_SIZE_BODY_MEDIUM := 14
const FONT_SIZE_BODY_SMALL := 12

# Label
const FONT_SIZE_LABEL_LARGE := 14
const FONT_SIZE_LABEL_MEDIUM := 12
const FONT_SIZE_LABEL_SMALL := 11

# Line Height
const LINE_HEIGHT_DISPLAY_LARGE := 64
const LINE_HEIGHT_DISPLAY_MEDIUM := 52
const LINE_HEIGHT_DISPLAY_SMALL := 44
const LINE_HEIGHT_HEADLINE_LARGE := 40
const LINE_HEIGHT_HEADLINE_MEDIUM := 36
const LINE_HEIGHT_HEADLINE_SMALL := 32
const LINE_HEIGHT_TITLE_LARGE := 28
const LINE_HEIGHT_TITLE_MEDIUM := 24
const LINE_HEIGHT_TITLE_SMALL := 20
const LINE_HEIGHT_BODY_LARGE := 24
const LINE_HEIGHT_BODY_MEDIUM := 20
const LINE_HEIGHT_BODY_SMALL := 16
const LINE_HEIGHT_LABEL_LARGE := 20
const LINE_HEIGHT_LABEL_MEDIUM := 16
const LINE_HEIGHT_LABEL_SMALL := 16


# =============================================================================
# Shape — Corner Radius (dp/px)
# =============================================================================

const SHAPE_NONE := 0
const SHAPE_EXTRA_SMALL := 4
const SHAPE_SMALL := 8
const SHAPE_MEDIUM := 12
const SHAPE_LARGE := 16
const SHAPE_EXTRA_LARGE := 28
const SHAPE_FULL := 9999  # Pill shape


# =============================================================================
# Color — ダークテーマ ベースライン（MD3 デフォルト紫テーマ）
# =============================================================================

# --- Primary ---
const COLOR_PRIMARY := Color(0.816, 0.737, 1.0)           # #D0BCFF
const COLOR_ON_PRIMARY := Color(0.22, 0.118, 0.447)       # #381E72
const COLOR_PRIMARY_CONTAINER := Color(0.31, 0.216, 0.545) # #4F378B
const COLOR_ON_PRIMARY_CONTAINER := Color(0.918, 0.867, 1.0) # #EADDFF

# --- Secondary ---
const COLOR_SECONDARY := Color(0.8, 0.761, 0.863)         # #CCC2DC
const COLOR_ON_SECONDARY := Color(0.2, 0.176, 0.255)      # #332D41
const COLOR_SECONDARY_CONTAINER := Color(0.29, 0.267, 0.345) # #4A4458
const COLOR_ON_SECONDARY_CONTAINER := Color(0.91, 0.871, 0.973) # #E8DEF8

# --- Tertiary ---
const COLOR_TERTIARY := Color(0.937, 0.722, 0.784)        # #EFB8C8
const COLOR_ON_TERTIARY := Color(0.286, 0.145, 0.196)     # #492532
const COLOR_TERTIARY_CONTAINER := Color(0.388, 0.231, 0.282) # #633B48
const COLOR_ON_TERTIARY_CONTAINER := Color(1.0, 0.847, 0.894) # #FFD8E4

# --- Error ---
const COLOR_ERROR := Color(0.949, 0.722, 0.71)            # #F2B8B5
const COLOR_ON_ERROR := Color(0.376, 0.078, 0.063)        # #601410
const COLOR_ERROR_CONTAINER := Color(0.549, 0.114, 0.094) # #8C1D18
const COLOR_ON_ERROR_CONTAINER := Color(0.976, 0.871, 0.863) # #F9DEDC

# --- Surface ---
const COLOR_SURFACE := Color(0.078, 0.071, 0.094)         # #141218
const COLOR_ON_SURFACE := Color(0.902, 0.878, 0.914)      # #E6E0E9
const COLOR_SURFACE_VARIANT := Color(0.286, 0.271, 0.31)  # #49454F
const COLOR_ON_SURFACE_VARIANT := Color(0.792, 0.769, 0.816) # #CAC4D0

# --- Surface Containers ---
const COLOR_SURFACE_CONTAINER_LOWEST := Color(0.059, 0.051, 0.075) # #0F0D13
const COLOR_SURFACE_CONTAINER_LOW := Color(0.114, 0.106, 0.125)    # #1D1B20
const COLOR_SURFACE_CONTAINER := Color(0.129, 0.122, 0.149)        # #211F26
const COLOR_SURFACE_CONTAINER_HIGH := Color(0.169, 0.161, 0.188)   # #2B2930
const COLOR_SURFACE_CONTAINER_HIGHEST := Color(0.212, 0.204, 0.231) # #36343B
const COLOR_SURFACE_DIM := Color(0.078, 0.071, 0.094)              # #141218
const COLOR_SURFACE_BRIGHT := Color(0.231, 0.22, 0.243)            # #3B383E

# --- Outline ---
const COLOR_OUTLINE := Color(0.576, 0.561, 0.6)           # #938F99
const COLOR_OUTLINE_VARIANT := Color(0.286, 0.271, 0.31)  # #49454F

# --- Inverse ---
const COLOR_INVERSE_SURFACE := Color(0.902, 0.878, 0.914)     # #E6E0E9
const COLOR_INVERSE_ON_SURFACE := Color(0.196, 0.184, 0.208)  # #322F35
const COLOR_INVERSE_PRIMARY := Color(0.404, 0.314, 0.643)     # #6750A4

# --- Scrim / Shadow ---
const COLOR_SCRIM := Color(0.0, 0.0, 0.0)                 # #000000
const COLOR_SHADOW := Color(0.0, 0.0, 0.0)                # #000000

# --- Fixed (Expressive) ---
const COLOR_PRIMARY_FIXED := Color(0.918, 0.867, 1.0)         # #EADDFF
const COLOR_PRIMARY_FIXED_DIM := Color(0.816, 0.737, 1.0)     # #D0BCFF
const COLOR_ON_PRIMARY_FIXED := Color(0.129, 0.0, 0.365)      # #21005D
const COLOR_ON_PRIMARY_FIXED_VARIANT := Color(0.31, 0.216, 0.545) # #4F378B
const COLOR_SECONDARY_FIXED := Color(0.91, 0.871, 0.973)      # #E8DEF8
const COLOR_SECONDARY_FIXED_DIM := Color(0.8, 0.761, 0.863)   # #CCC2DC
const COLOR_ON_SECONDARY_FIXED := Color(0.114, 0.098, 0.169)  # #1D192B
const COLOR_ON_SECONDARY_FIXED_VARIANT := Color(0.29, 0.267, 0.345) # #4A4458
const COLOR_TERTIARY_FIXED := Color(1.0, 0.847, 0.894)        # #FFD8E4
const COLOR_TERTIARY_FIXED_DIM := Color(0.937, 0.722, 0.784)  # #EFB8C8
const COLOR_ON_TERTIARY_FIXED := Color(0.192, 0.067, 0.114)   # #31111D
const COLOR_ON_TERTIARY_FIXED_VARIANT := Color(0.388, 0.231, 0.282) # #633B48


# =============================================================================
# Elevation — Tonal Surface カラー（ダークテーマ推奨）
# =============================================================================

const ELEVATION_LEVEL_0 := COLOR_SURFACE                    # 最下層
const ELEVATION_LEVEL_1 := COLOR_SURFACE_CONTAINER_LOW      # Nav Bar/Rail
const ELEVATION_LEVEL_2 := COLOR_SURFACE_CONTAINER          # Elevated Card
const ELEVATION_LEVEL_3 := COLOR_SURFACE_CONTAINER_HIGH     # FAB, Dialog
const ELEVATION_LEVEL_4 := COLOR_SURFACE_CONTAINER_HIGH     # Hover lift
const ELEVATION_LEVEL_5 := COLOR_SURFACE_CONTAINER_HIGHEST  # 最高

# Shadow パラメータ（StyleBoxFlat.shadow_* 用）
# [offset_y, blur, alpha]
const SHADOW_LEVEL_0 := Vector3(0, 0, 0.0)
const SHADOW_LEVEL_1 := Vector3(1, 3, 0.15)
const SHADOW_LEVEL_2 := Vector3(2, 6, 0.15)
const SHADOW_LEVEL_3 := Vector3(4, 8, 0.15)
const SHADOW_LEVEL_4 := Vector3(6, 10, 0.15)
const SHADOW_LEVEL_5 := Vector3(8, 12, 0.15)


# =============================================================================
# Motion — Duration (seconds)
# =============================================================================

const DURATION_SHORT_1 := 0.05   # 50ms  マイクロインタラクション
const DURATION_SHORT_2 := 0.1    # 100ms ステート変化
const DURATION_SHORT_3 := 0.15   # 150ms 小さなフェード
const DURATION_SHORT_4 := 0.2    # 200ms デスクトップ推奨

const DURATION_MEDIUM_1 := 0.25  # 250ms 小パネル開閉
const DURATION_MEDIUM_2 := 0.3   # 300ms ダイアログ出現
const DURATION_MEDIUM_3 := 0.35  # 350ms 中規模トランジション
const DURATION_MEDIUM_4 := 0.4   # 400ms 大パネル開閉

const DURATION_LONG_1 := 0.45    # 450ms 画面遷移（小）
const DURATION_LONG_2 := 0.5     # 500ms 画面遷移（中）
const DURATION_LONG_3 := 0.55    # 550ms 画面遷移（大）
const DURATION_LONG_4 := 0.6     # 600ms 複雑なトランジション

const DURATION_EXTRA_LONG_1 := 0.7  # 700ms 全画面
const DURATION_EXTRA_LONG_2 := 0.8  # 800ms
const DURATION_EXTRA_LONG_3 := 0.9  # 900ms
const DURATION_EXTRA_LONG_4 := 1.0  # 1000ms 最長

# Easing — Tween.TransitionType / EaseType の組み合わせ
# 厳密な MD3 カーブは Curve リソース + set_custom_interpolator() で再現
# 以下は Godot 標準 Tween での近似値

# Standard: 画面内の要素移動
const EASING_STANDARD_TRANS := Tween.TRANS_CUBIC
const EASING_STANDARD_EASE := Tween.EASE_IN_OUT

# Standard Decelerate: 画面に入る要素
const EASING_DECELERATE_TRANS := Tween.TRANS_QUART
const EASING_DECELERATE_EASE := Tween.EASE_OUT

# Standard Accelerate: 画面から出る要素
const EASING_ACCELERATE_TRANS := Tween.TRANS_QUART
const EASING_ACCELERATE_EASE := Tween.EASE_IN

# Spring (Expressive): バウンス効果の近似
const EASING_SPRING_TRANS := Tween.TRANS_BACK
const EASING_SPRING_EASE := Tween.EASE_OUT


# =============================================================================
# State Layer — オパシティ
# =============================================================================

const STATE_HOVER := 0.08       # 8%
const STATE_FOCUS := 0.12       # 12%
const STATE_PRESSED := 0.12     # 12%
const STATE_DRAGGED := 0.16     # 16%
const STATE_DISABLED_CONTAINER := 0.12  # 12%
const STATE_DISABLED_CONTENT := 0.38    # 38%


# =============================================================================
# Spacing — 4px ベースライングリッド
# =============================================================================

const SPACING_XS := 4     # アイコンとテキストの間
const SPACING_SM := 8     # 密接な要素間
const SPACING_MD := 12    # 関連要素間
const SPACING_BASE := 16  # 標準パディング
const SPACING_LG := 20    # セクション間
const SPACING_XL := 24    # カード内パディング
const SPACING_2XL := 32   # 大きなセクション間
const SPACING_3XL := 48   # 画面レベルのマージン


# =============================================================================
# Component — よく使うコンポーネントパラメータ
# =============================================================================

# Button
const BUTTON_HEIGHT := 40
const BUTTON_PADDING_H := 24
const BUTTON_ICON_SIZE := 18
const BUTTON_ICON_GAP := 8

# FAB
const FAB_SIZE := 56
const FAB_SIZE_SMALL := 40
const FAB_SIZE_LARGE := 96

# Dialog
const DIALOG_MIN_WIDTH := 280
const DIALOG_MAX_WIDTH := 560
const DIALOG_PADDING := 24
const DIALOG_CORNER_RADIUS := SHAPE_EXTRA_LARGE  # 28dp
const DIALOG_SCRIM_ALPHA := 0.32

# Card
const CARD_CORNER_RADIUS := SHAPE_MEDIUM  # 12dp
const CARD_PADDING := 16

# Chip
const CHIP_HEIGHT := 32
const CHIP_PADDING_H := 16
const CHIP_CORNER_RADIUS := SHAPE_SMALL  # 8dp

# Touch Target
const TOUCH_TARGET_MIN := 48

# Icon Size
const ICON_SMALL := 20
const ICON_MEDIUM := 24
const ICON_LARGE := 40
const ICON_EXTRA_LARGE := 48

# Progress Indicator
const PROGRESS_LINEAR_HEIGHT := 4
const PROGRESS_CIRCULAR_SIZE := 48
const PROGRESS_CIRCULAR_SMALL := 24
const PROGRESS_STROKE_WIDTH := 4
