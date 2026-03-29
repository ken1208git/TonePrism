# Material Design 3 + Expressive デザインリファレンス

本ドキュメントは、GCTonePrism Launcher の UI 実装時に参照する Material Design 3（MD3）および MD3 Expressive のデザイントークンとコンポーネント仕様をまとめたものです。

> **参照元**: [m3.material.io](https://m3.material.io/)、[Material Components Android](https://github.com/material-components/material-components-android)
>
> **プロジェクト方針**: ダークテーマベース、日本語フォント（Noto Sans JP）、アクセントカラー設定可能

---

## 1. Typography（タイポグラフィ）

### フォント

| 用途 | フォントファミリー | ファイル |
|------|-------------------|---------|
| 全般 | Noto Sans JP | `fonts/NotoSansJP-*.ttf` |

**利用可能ウェイト:**

| ウェイト | ファイル | MD3での用途 |
|---------|---------|-------------|
| Regular (400) | NotoSansJP-Regular.ttf | Body, Title Large |
| Medium (500) | NotoSansJP-Medium.ttf | Title Medium/Small, Label |
| SemiBold (600) | NotoSansJP-SemiBold.ttf | Display, Headline |
| Bold (700) | NotoSansJP-Bold.ttf | 強調テキスト |

> **注意**: MD3 の Display/Headline は本来ウェイト 475 だが、Noto Sans JP には存在しないため SemiBold (600) で代替。日本語テキストは英語の指定サイズより **+1px** が推奨。

### Type Scale

| Role | Size (px) | Line Height (px) | Weight | Letter Spacing (px) |
|------|-----------|-------------------|--------|---------------------|
| Display Large | 57 | 64 | 600 (SemiBold) | -0.25 |
| Display Medium | 45 | 52 | 600 (SemiBold) | 0 |
| Display Small | 36 | 44 | 600 (SemiBold) | 0 |
| Headline Large | 32 | 40 | 600 (SemiBold) | 0 |
| Headline Medium | 28 | 36 | 600 (SemiBold) | 0 |
| Headline Small | 24 | 32 | 600 (SemiBold) | 0 |
| Title Large | 22 | 28 | 400 (Regular) | 0 |
| Title Medium | 16 | 24 | 500 (Medium) | 0.15 |
| Title Small | 14 | 20 | 500 (Medium) | 0.1 |
| Body Large | 16 | 24 | 400 (Regular) | 0.5 |
| Body Medium | 14 | 20 | 400 (Regular) | 0.25 |
| Body Small | 12 | 16 | 400 (Regular) | 0.4 |
| Label Large | 14 | 20 | 500 (Medium) | 0.1 |
| Label Medium | 12 | 16 | 500 (Medium) | 0.5 |
| Label Small | 11 | 16 | 500 (Medium) | 0.5 |

### Expressive Typography

MD3 Expressive では、重要な要素のタイポグラフィをより強調する:
- Display/Headline のウェイトを上げる（SemiBold → Bold）
- サイズのコントラストを拡大（大きい見出し + 小さい本文）
- アニメーション中のテキストサイズ変化（フォーカス時にスケールアップ等）

---

## 2. Shape（シェイプ）

### Corner Radius Scale

| Shape | Corner Radius | 主な用途 |
|-------|--------------|---------|
| None | 0dp | 全画面コンテナ、背景 |
| Extra Small | 4dp | Chip, Small Button |
| Small | 8dp | Chip（選択時）、Snackbar |
| Medium | 12dp | Card, Dialog, FAB (Small) |
| Large | 16dp | FAB, Extended FAB, Navigation Drawer |
| Extra Large | 28dp | Large FAB, Bottom Sheet |
| Full | 9999dp | Button (Filled/Outlined/Text), Pill Shape, Slider Thumb |

### コンポーネント別 Shape マッピング

| コンポーネント | Shape | Corner Radius |
|--------------|-------|---------------|
| Filled Button | Full | 9999dp (pill) |
| Outlined Button | Full | 9999dp (pill) |
| Icon Button | Full | 9999dp (circle) |
| FAB (通常) | Large | 16dp |
| FAB (Small) | Medium | 12dp |
| FAB (Large) | Extra Large | 28dp |
| Card (Filled/Elevated/Outlined) | Medium | 12dp |
| Chip (Assist/Filter/Input/Suggestion) | Small | 8dp |
| Dialog | Extra Large | 28dp |
| Snackbar | Small | 8dp |
| Navigation Bar | None | 0dp |
| Navigation Drawer | Large (end corner) | 16dp |
| Bottom Sheet | Extra Large (top) | 28dp |
| Text Field | Extra Small (top) | 4dp |
| Menu | Extra Small | 4dp |
| Tooltip | Extra Small | 4dp |

### Expressive Shape

MD3 Expressive では Shape の役割が拡張される:
- **Shape Morphing**: ステート変化に合わせて角丸がアニメーションで変化（例: ボタンプレス時に pill → squircle）
- **35種の新シェイプ**: 標準の角丸以外に、角丸の非対称パターンや有機的な形状が追加
- **ブランド表現**: Shape をブランドアイデンティティの一部として活用

**Godot での実装**: `StyleBoxFlat.corner_radius_*` プロパティを Tween でアニメーションさせることで Shape Morphing を再現可能。

---

## 3. Color（カラー）

### カラーロール一覧（ダークテーマ ベースライン）

Material Theme Builder のデフォルト紫テーマをベースラインとして使用。将来的にアクセントカラー設定機能で動的に変更可能にする。

#### Primary グループ

| Role | Hex | RGB | 用途 |
|------|-----|-----|------|
| Primary | #D0BCFF | (208, 188, 255) | 主要ボタン、アクティブ状態 |
| On Primary | #381E72 | (56, 30, 114) | Primary 上のテキスト/アイコン |
| Primary Container | #4F378B | (79, 55, 139) | 主要コンテナ背景 |
| On Primary Container | #EADDFF | (234, 221, 255) | Primary Container 上のテキスト |

#### Secondary グループ

| Role | Hex | RGB | 用途 |
|------|-----|-----|------|
| Secondary | #CCC2DC | (204, 194, 220) | 補助的なアクション |
| On Secondary | #332D41 | (51, 45, 65) | Secondary 上のテキスト |
| Secondary Container | #4A4458 | (74, 68, 88) | フィルターチップ、選択状態 |
| On Secondary Container | #E8DEF8 | (232, 222, 248) | Secondary Container 上のテキスト |

#### Tertiary グループ

| Role | Hex | RGB | 用途 |
|------|-----|-----|------|
| Tertiary | #EFB8C8 | (239, 184, 200) | アクセント、バランス色 |
| On Tertiary | #492532 | (73, 37, 50) | Tertiary 上のテキスト |
| Tertiary Container | #633B48 | (99, 59, 72) | Tertiary コンテナ背景 |
| On Tertiary Container | #FFD8E4 | (255, 216, 228) | Tertiary Container 上のテキスト |

#### Error グループ

| Role | Hex | RGB | 用途 |
|------|-----|-----|------|
| Error | #F2B8B5 | (242, 184, 181) | エラー状態 |
| On Error | #601410 | (96, 20, 16) | Error 上のテキスト |
| Error Container | #8C1D18 | (140, 29, 24) | エラーコンテナ背景 |
| On Error Container | #F9DEDC | (249, 222, 220) | Error Container 上のテキスト |

#### Surface / Neutral グループ

| Role | Hex | RGB | 用途 |
|------|-----|-----|------|
| Surface | #141218 | (20, 18, 24) | 最下層の背景 |
| On Surface | #E6E0E9 | (230, 224, 233) | Surface 上のテキスト |
| Surface Variant | #49454F | (73, 69, 79) | バリエーション背景 |
| On Surface Variant | #CAC4D0 | (202, 196, 208) | Surface Variant 上のテキスト |
| Surface Container Lowest | #0F0D13 | (15, 13, 19) | 最も暗いコンテナ |
| Surface Container Low | #1D1B20 | (29, 27, 32) | 暗いコンテナ |
| Surface Container | #211F26 | (33, 31, 38) | 標準コンテナ |
| Surface Container High | #2B2930 | (43, 41, 48) | 明るいコンテナ |
| Surface Container Highest | #36343B | (54, 52, 59) | 最も明るいコンテナ |
| Surface Dim | #141218 | (20, 18, 24) | 暗いサーフェス |
| Surface Bright | #3B383E | (59, 56, 62) | 明るいサーフェス |

#### Outline / その他

| Role | Hex | RGB | 用途 |
|------|-----|-----|------|
| Outline | #938F99 | (147, 143, 153) | ボーダー、区切り線 |
| Outline Variant | #49454F | (73, 69, 79) | 薄いボーダー |
| Inverse Surface | #E6E0E9 | (230, 224, 233) | 反転サーフェス（Snackbar等） |
| Inverse On Surface | #322F35 | (50, 47, 53) | 反転サーフェス上のテキスト |
| Inverse Primary | #6750A4 | (103, 80, 164) | 反転プライマリ |
| Scrim | #000000 | (0, 0, 0) | オーバーレイ背景 |
| Shadow | #000000 | (0, 0, 0) | シャドウ色 |

### Expressive Fixed Color Roles

| Role | Hex | RGB | 用途 |
|------|-----|-----|------|
| Primary Fixed | #EADDFF | (234, 221, 255) | テーマに依存しない Primary |
| Primary Fixed Dim | #D0BCFF | (208, 188, 255) | Primary Fixed の暗め |
| On Primary Fixed | #21005D | (33, 0, 93) | Primary Fixed 上のテキスト |
| On Primary Fixed Variant | #4F378B | (79, 55, 139) | Primary Fixed 上のバリアント |
| Secondary Fixed | #E8DEF8 | (232, 222, 248) | テーマに依存しない Secondary |
| Secondary Fixed Dim | #CCC2DC | (204, 194, 220) | Secondary Fixed の暗め |
| On Secondary Fixed | #1D192B | (29, 25, 43) | Secondary Fixed 上のテキスト |
| On Secondary Fixed Variant | #4A4458 | (74, 68, 88) | Secondary Fixed 上のバリアント |
| Tertiary Fixed | #FFD8E4 | (255, 216, 228) | テーマに依存しない Tertiary |
| Tertiary Fixed Dim | #EFB8C8 | (239, 184, 200) | Tertiary Fixed の暗め |
| On Tertiary Fixed | #31111D | (49, 17, 29) | Tertiary Fixed 上のテキスト |
| On Tertiary Fixed Variant | #633B48 | (99, 59, 72) | Tertiary Fixed 上のバリアント |

---

## 4. Elevation（エレベーション）

### Tonal Elevation（推奨: ダークテーマ）

ダークテーマではシャドウの代わりに、Surface 色の明度でエレベーションを表現する（Tonal Elevation）。

| Level | Surface 色 | 用途 |
|-------|-----------|------|
| Level 0 | Surface (`#141218`) | 最下層背景 |
| Level 1 | Surface Container Low (`#1D1B20`) | Navigation Bar/Rail |
| Level 2 | Surface Container (`#211F26`) | Card (Elevated) |
| Level 3 | Surface Container High (`#2B2930`) | FAB, Dialog, Snackbar |
| Level 4 | Surface Container High (`#2B2930`) | ホバー時のリフト |
| Level 5 | Surface Container Highest (`#36343B`) | 最高レベル |

### Shadow Elevation（補助的に使用）

シャドウが必要な場合の値:

| Level | Key Shadow | Ambient Shadow |
|-------|-----------|---------------|
| Level 0 | なし | なし |
| Level 1 | offset_y: 1px, blur: 2px, alpha: 0.30 | offset_y: 1px, blur: 3px, alpha: 0.15 |
| Level 2 | offset_y: 1px, blur: 2px, alpha: 0.30 | offset_y: 2px, blur: 6px, alpha: 0.15 |
| Level 3 | offset_y: 1px, blur: 3px, alpha: 0.30 | offset_y: 4px, blur: 8px, alpha: 0.15 |
| Level 4 | offset_y: 2px, blur: 3px, alpha: 0.30 | offset_y: 6px, blur: 10px, alpha: 0.15 |
| Level 5 | offset_y: 4px, blur: 4px, alpha: 0.30 | offset_y: 8px, blur: 12px, alpha: 0.15 |

**Godot での実装**: `StyleBoxFlat.shadow_*` プロパティで表現。ダークテーマでは Tonal Elevation（コンテナ色の使い分け）を優先。

---

## 5. Motion（モーション）

### Easing Curves

| 名前 | cubic-bezier | 用途 | Godot Tween |
|------|-------------|------|-------------|
| Standard | (0.2, 0.0, 0.0, 1.0) | 画面内の要素移動 | `set_trans(TRANS_CUBIC).set_ease(EASE_IN_OUT)` ※近似 |
| Standard Decelerate | (0.0, 0.0, 0.0, 1.0) | 画面に入る要素 | `set_trans(TRANS_QUART).set_ease(EASE_OUT)` ※近似 |
| Standard Accelerate | (0.3, 0.0, 1.0, 1.0) | 画面から出る要素 | `set_trans(TRANS_QUART).set_ease(EASE_IN)` ※近似 |
| Emphasized Decelerate | (0.05, 0.7, 0.1, 1.0) | MD3 出現アニメーション | カスタムカーブ推奨 |
| Emphasized Accelerate | (0.3, 0.0, 0.8, 0.15) | MD3 退出アニメーション | カスタムカーブ推奨 |
| Linear | (0.0, 0.0, 1.0, 1.0) | 色・透明度の変化 | `set_trans(TRANS_LINEAR)` |

> **Godot Tips**: Emphasized 系は `Curve` リソースを作成し、`tween.set_custom_interpolator()` で適用する。

### Duration Tokens

| Token | Duration | 用途 |
|-------|----------|------|
| Short 1 | 50ms | マイクロインタラクション |
| Short 2 | 100ms | ボタンのステート変化 |
| Short 3 | 150ms | 小さなフェード |
| Short 4 | 200ms | デスクトップアニメーション推奨 |
| Medium 1 | 250ms | 小さなパネル開閉 |
| Medium 2 | 300ms | ダイアログ出現 |
| Medium 3 | 350ms | 中規模トランジション |
| Medium 4 | 400ms | 大きなパネル開閉 |
| Long 1 | 450ms | 画面遷移（小） |
| Long 2 | 500ms | 画面遷移（中） |
| Long 3 | 550ms | 画面遷移（大） |
| Long 4 | 600ms | 複雑なトランジション |
| Extra Long 1 | 700ms | 全画面トランジション |
| Extra Long 2 | 800ms | 複雑な全画面 |
| Extra Long 3 | 900ms | 非常に複雑 |
| Extra Long 4 | 1000ms | 最長トランジション |

> **デスクトップ向け**: 150ms〜300ms を基本とする。モバイル向けの Long/Extra Long は短縮して使用。

### Expressive Spring Motion

MD3 Expressive ではイージングカーブの代わりにスプリング物理を使用:

- **Spatial Spring**: 位置・サイズの変化に使用。物理的な弾みを表現
- **Effects Spring**: 色・透明度の変化に使用。滑らかなトランジション
- 3つのスピードバリアント: **Fast** / **Default** / **Slow**

**Godot での実装**:
```gdscript
# スプリングモーションの近似実装
# damping: 0.0（弾みまくり）〜 1.0（弾みなし）
func spring_tween(node: Node, property: String, target: Variant,
                  duration: float = 0.4, damping: float = 0.6) -> Tween:
    var tween = create_tween()
    # Emphasized Decelerate でスプリングを近似
    tween.tween_property(node, property, target, duration) \
         .set_trans(Tween.TRANS_BACK) \
         .set_ease(Tween.EASE_OUT)
    return tween
```

> **注意**: 完全なスプリング物理の再現には `_process()` ベースのカスタム実装が必要。上記は Tween での近似。

---

## 6. State Layer（ステートレイヤー）

インタラクティブ要素のステートを半透明オーバーレイで表現する。

### オパシティ値

| State | Opacity | 説明 |
|-------|---------|------|
| Enabled | 0% | デフォルト状態 |
| Hover | 8% | マウスホバー時 |
| Focus | 12% | キーボードフォーカス時 |
| Pressed | 12% | クリック/タップ時 |
| Dragged | 16% | ドラッグ中 |
| Disabled (container) | 12% | 無効状態のコンテナ |
| Disabled (content) | 38% | 無効状態のテキスト/アイコン |

### 適用方法

State Layer のカラーは親コンポーネントの `on-*` カラーを使用:
- Primary ボタン上の State Layer → On Primary カラー
- Surface 上の State Layer → On Surface カラー

**Godot での実装**: `ColorRect` をオーバーレイとして配置し、`color.a` を State に応じて変更。

```gdscript
# State Layer の適用例
func apply_state_layer(overlay: ColorRect, state: String, base_color: Color):
    match state:
        "hover":   overlay.color = Color(base_color, 0.08)
        "focus":   overlay.color = Color(base_color, 0.12)
        "pressed": overlay.color = Color(base_color, 0.12)
        "dragged": overlay.color = Color(base_color, 0.16)
```

---

## 7. Spacing（スペーシング）

### ベースライングリッド: 4px

すべてのスペーシングは **4px の倍数** を使用する。

| Token | Value | 主な用途 |
|-------|-------|---------|
| spacing-xs | 4px | アイコンとテキストの間 |
| spacing-sm | 8px | 密接な要素間 |
| spacing-md | 12px | 関連要素間 |
| spacing-base | 16px | 標準パディング |
| spacing-lg | 20px | セクション間 |
| spacing-xl | 24px | カード内パディング |
| spacing-2xl | 32px | 大きなセクション間 |
| spacing-3xl | 48px | 画面レベルのマージン |

### パディング規則

| コンポーネント | 水平パディング | 垂直パディング |
|--------------|-------------|-------------|
| Button | 24px | 10px (高さ40dp に内包) |
| Card | 16px | 16px |
| Dialog | 24px | 24px |
| Chip | 16px | 6px (高さ32dp に内包) |
| Text Field | 16px | 16px |
| List Item | 16px | 8px |

---

## 8. 汎用コンポーネント原則

全 UI コンポーネントに共通する MD3 ルール。個別コンポーネントの仕様がなくても、以下に従うことで MD3 準拠を維持できる。

### Shape 適用ルール

コンポーネントのサイズに応じた Shape の選択:

| コンポーネントサイズ | 推奨 Shape | Corner Radius |
|------------------|-----------|---------------|
| 極小（アイコン、Tooltip） | Extra Small | 4dp |
| 小（Chip、TextField） | Small | 8dp |
| 中（Card、Menu） | Medium | 12dp |
| 大（FAB、Drawer） | Large | 16dp |
| 特大（Sheet、Dialog） | Extra Large | 28dp |
| ボタン・ピル型 | Full | 9999dp |

### State Layer 適用

- **全てのインタラクティブ要素**に State Layer を適用する
- タップ/クリック可能な要素には必ず Hover / Focus / Pressed ステートを実装
- State Layer のカラーは常に `on-*` カラーを使用

### Elevation 階層

| 要素タイプ | Elevation Level |
|----------|----------------|
| 背景・最下層 | Level 0 |
| Card (Filled) | Level 0 |
| Card (Elevated) | Level 1 |
| Navigation Bar / Rail | Level 2 |
| FAB | Level 3 |
| Dialog | Level 3 |
| Dropdown Menu | Level 2 |

### タッチターゲット

- 最小タッチターゲットサイズ: **48 x 48 dp**
- 視覚的にはこれより小さくても、タッチ領域は 48dp を確保する
- ゲームコントローラー操作時はフォーカス表示を大きくする

### アイコンサイズ

| サイズ名 | Value | 用途 |
|---------|-------|------|
| Small | 20dp | Dense な UI、Chip 内 |
| Medium | 24dp | 標準（最も使用頻度高い） |
| Large | 40dp | FAB、強調表示 |
| Extra Large | 48dp | 空状態イラスト、大きなアクション |

---

## 9. 主要コンポーネント実装ガイド

### Button

**MD3 仕様:**
- Shape: Full (pill = 9999dp corner radius)
- 高さ: 40dp（通常）、56dp（FAB）
- 水平パディング: 24dp
- テキスト: Label Large (14px, Medium 500)
- アイコン付き: アイコン 18dp、アイコンとテキスト間 8dp

**バリアント:**

| バリアント | Surface | Text Color | Outline |
|-----------|---------|------------|---------|
| Filled | Primary | On Primary | なし |
| Outlined | 透明 | Primary | Outline 色 |
| Text | 透明 | Primary | なし |
| Elevated | Surface Container Low | Primary | なし (Shadow Level 1) |
| Tonal | Secondary Container | On Secondary Container | なし |

**Expressive Motion:**
- プレス時: スプリングバウンスでわずかに縮小 (scale: 0.95 → 1.0)
- Shape Morphing: ホバー時に corner radius を微調整（任意）

**Godot 実装:**
```gdscript
# ボタンプレスのスプリングアニメーション
func _on_button_pressed(button: Control):
    var tween = create_tween()
    tween.tween_property(button, "scale", Vector2(0.95, 0.95), 0.05)
    tween.tween_property(button, "scale", Vector2(1.0, 1.0), 0.2) \
         .set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
```

### Card

**MD3 仕様:**
- Shape: Medium (12dp corner radius)
- 3つのバリアント: Filled / Elevated / Outlined

| バリアント | Surface | Elevation | Outline |
|-----------|---------|-----------|---------|
| Filled | Surface Container Highest | Level 0 | なし |
| Elevated | Surface Container Low | Level 1 (shadow) | なし |
| Outlined | Surface | Level 0 | Outline Variant |

**インタラクション:**
- Hover: Elevation +1 (Tonal) + State Layer 8%
- Pressed: State Layer 12%、Elevation 変化なし

**Godot 実装:**
```gdscript
# Card ホバーエフェクト
func _on_card_mouse_entered(card: PanelContainer):
    var style = card.get_theme_stylebox("panel").duplicate()
    var tween = create_tween()
    # Tonal Elevation の変化
    tween.tween_property(style, "bg_color",
        DesignTokens.COLOR_SURFACE_CONTAINER_HIGH, 0.2)
    card.add_theme_stylebox_override("panel", style)
```

### Dialog

**MD3 仕様:**
- Shape: Extra Large (28dp corner radius)
- Surface: Surface Container High
- Scrim: #000000 alpha 32% (0.32)
- 最小幅: 280dp、最大幅: 560dp
- パディング: 24dp
- タイトル: Headline Small (24px)
- 本文: Body Medium (14px)

**モーション:**
- 出現: Emphasized Decelerate、300ms、scale 0.9→1.0 + fade in
- 退出: Emphasized Accelerate、200ms、fade out

**Godot 実装:**
```gdscript
# Dialog 出現アニメーション
func show_dialog(dialog: Control, overlay: ColorRect):
    dialog.scale = Vector2(0.9, 0.9)
    dialog.modulate.a = 0.0
    overlay.color.a = 0.0

    var tween = create_tween().set_parallel(true)
    tween.tween_property(overlay, "color:a", 0.32, 0.25)
    tween.tween_property(dialog, "scale", Vector2.ONE, 0.3) \
         .set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
    tween.tween_property(dialog, "modulate:a", 1.0, 0.25)
```

### Progress Indicator

**Linear Progress:**
- 高さ: 4dp
- Shape: Full (2dp corner radius)
- Active: Primary カラー
- Track: Surface Container Highest
- Indeterminate: 2本のバーが左右に移動

**Circular Progress:**
- サイズ: 48dp（通常）、24dp（小）
- ストローク幅: 4dp
- Active: Primary カラー
- Track: Surface Container Highest

**Expressive Motion:**
- スプリングベースの加減速（弾むようなリズム）
- Indeterminate のバー移動にバウンス効果

**Godot 実装:**
```gdscript
# Circular Progress Indicator（簡易版）
# TextureProgressBar または _draw() で円弧を描画
func _draw():
    var center = size / 2
    var radius = min(size.x, size.y) / 2 - 2
    # Track
    draw_arc(center, radius, 0, TAU, 64,
        DesignTokens.COLOR_SURFACE_CONTAINER_HIGHEST, 4.0, true)
    # Progress
    draw_arc(center, radius, -PI/2, -PI/2 + progress * TAU, 64,
        DesignTokens.COLOR_PRIMARY, 4.0, true)
```

---

## 参考リンク

- [Material Design 3 公式](https://m3.material.io/)
- [MD3 Typography](https://m3.material.io/styles/typography/type-scale-tokens)
- [MD3 Color Roles](https://m3.material.io/styles/color/roles)
- [MD3 Shape](https://m3.material.io/styles/shape/corner-radius-scale)
- [MD3 Elevation](https://m3.material.io/styles/elevation/tokens)
- [MD3 Motion](https://m3.material.io/styles/motion/easing-and-duration/tokens-specs)
- [MD3 Components](https://m3.material.io/components)
- [Material Theme Builder](https://material-foundation.github.io/material-theme-builder/)
- [Material Components Android - Shape](https://github.com/material-components/material-components-android/blob/master/docs/theming/Shape.md)
