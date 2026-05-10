# 変更履歴

このプロジェクト（Prismランチャーシステム）の重要な変更点を全て記録します。

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) に従い、
このプロジェクトは [Semantic Versioning](https://semver.org/lang/ja/) に準拠しています。

**注意**: このCHANGELOGはソフトウェア本体のバージョンを追跡します。仕様書の変更履歴については、[SPECIFICATION.md](SPECIFICATION.md)の「変更履歴」セクションを参照してください。

---

## Launcher（ランチャー本体）

### [Launcher v0.5.14] - 2026-05-03

#### Fixed

- **ゲーム情報パネルの製作者欄が空になる問題を解消**: v0.5.8 で製作者クエリを `developers JOIN game_versions` の INNER JOIN 形式に変えたが、Manager の通常追加/更新フローでは `developers.version_id` を NULL のまま INSERT するため、全ゲームで製作者が表示されなくなっていた。`get_developers_by_game_id` を 2 段階クエリに変更し、現行バージョン (`games.version` ↔ `game_versions.version`) に紐付いた製作者があればそれを返し、無ければ `version_id IS NULL` の行にフォールバックする挙動に修正

### [Launcher v0.5.13] - 2026-05-01

#### Added

- **ゲーム起動中・プレイ中オーバーレイ (`launching_overlay.tscn` / `.gd`)**: ゲーム起動から終了までの間、画面右側に状態表示を出すオーバーレイ
  - 全画面うっすら白オーバーレイ（α=0.5）で背景を透けさせつつ "別モード" 感を演出
  - 右側に「ゲーム起動中...」/「プレイ中」のステータス文字（呼吸アニメ付き）
  - 細い区切り線の下にゲームタイトル（太字・大）。タイトルが幅を超えたら `auto_scroll_container` で自動スクロール
- **状態切り替え API**: `LaunchingOverlay.set_state(LAUNCHING / PLAYING)` で文言とアニメ速度が切り替わる。ゲームウィンドウ起動前後で自動的に LAUNCHING → PLAYING へ遷移
- **ゲーム起動・終了時の背景ズーム演出**: `_switch_to_running_view` / `_switch_to_normal_view` で背景画像を中心からほんのちょっと拡大（×1.05）/縮小して、フェードと同じ TRANS_QUINT で同期させた映画的な遷移に

#### Changed

- **GameLauncher のシグネチャを刷新**: 未使用だった `running_overlay: Control` パラメータを廃止し、新規 `launching_overlay: LaunchingOverlay` に置き換え。`_switch_to_running_view` / `_switch_to_normal_view` も同様に整理。背景ズーム用に `background_texture: TextureRect` パラメータも追加
- **遷移イージングを TRANS_CUBIC → TRANS_QUINT に変更**: 起動中画面とのフェードを映画的なカーブに。所要時間も 0.4-0.5s → 0.55s に統一
- **遷移は全要素 EASE_OUT で統一**: 起動時 / 終了時とも `TRANS_QUINT EASE_OUT` で「決定的に始まり、ゆっくり収まる」スナップ感のあるカーブに統一
- **カルーセルカードの透明度を中心からの距離で連続補間**: 従来は「選択中=1.0/それ以外=0.6」の二値で、中央到達時にパッと変化していたのを、`diff` の絶対値で線形補間する形に。トラックパッド free scroll で半分だけ移動した時も両カードが半分の濃さになる
- **背景画像をカルーセルの scroll_index に応じて連続クロスフェード + 上下スライド**: `BackgroundTexture` (前面) と `BackgroundTextureOld` (背面) に floor/ceil(scroll_index) のゲーム背景をそれぞれロードし、fract で前面の alpha を 1 から 0 に補間。さらに lower bg は上に、upper bg は下から上に、最大 ±50px スライドする視差効果を追加。スクロール途中で次のゲームの背景がだんだん表れ、中央到達時には完全に切り替わってる挙動に。`_update_background` の従来 tween 方式は廃止
- **背景なしゲームとの切り替えで bg が突然現れる/消える問題を解消**: 各 bg の `texture != null` を見て alpha を計算。背景なし → ありの遷移では bg_b を `fract` で徐々にフェードイン、あり → なしでは bg_a を `1-fract` でフェードアウト
- **キーボード/コントローラー操作中はマウスカーソルを自動的に隠す**: 入力イベント毎に `Input.mouse_mode` を `HIDDEN` / `VISIBLE` で同期切替。マウスを動かすと即座に再表示。`game_selection` / `store_browse` / `common_dialog` 各画面で対応。シーン遷移時には状態を持ち越し（`_ready` での強制リセットはしない）。pause 中のダイアログでも `process_mode = ALWAYS` の dialog 側 `_input` でカーソル状態を更新
- **起動中/プレイ中ステータステキストにシマーシェーダー適用**: LOADING 表示と同じ `progress_shimmer.gdshader` を「ゲーム起動中...」「プレイ中」に流用し、暗いベース → 中間色 → 暗いベースの波が文字を流れる効果に。LAUNCHING は速め (speed=1.8)、PLAYING はゆっくり (speed=0.7) に切替。`ShimmerHelper.apply_with_params` を新設して任意のパラメータ指定を可能に

#### Fixed

- **起動中/プレイ中のカルーセル入力をブロック**: `game_selection._unhandled_input` で `_game_launcher.is_running()` 時に早期 return、`_process` 内で `_trackpad_offset = 0` 強制リセット。トラックパッド/ホイール経由でカルーセルが動いてしまう問題を解消

### [Launcher v0.5.12] - 2026-04-28

#### Added

- **トラックパッド向けフリースクロール対応**: `InputEventMouseButton.factor` を見て、マウスホイール（factor>=0.5）はディスクリート1ゲーム移動、トラックパッド（factor<0.5）はフリースクロール + 離したら最寄りゲームにスナップ、と入力デバイスに応じて挙動を分離。`InputHandler.free_scroll(delta_amount: float)` シグナル追加
- **CarouselController に視覚オフセット引数を追加**: `update_cards(... free_offset: float ...)` で `current_scroll_index` の補間先に上乗せできるように。フリースクロール中は `new_active` を `selected_index` で固定してチラつきを防止

#### Changed

- **InfoPanel の縁ぼかしを除去**: 12px の透明ボーダー + `border_blend` + `expand_margin` を削除し、すりガラスのエッジをくっきりさせた。角丸 32px は維持

#### Fixed

- **トラックパッド2本指でカルーセルが爆速で流れる問題を解消**: 従来は1イベント=1移動だったため、Windows precision touchpad の高頻度イベントで暴走していた。factor 蓄積方式 + スナップ遅延でなめらかな操作感に

### [Launcher v0.5.11] - 2026-04-27

#### Added

- **すりガラスシェーダー (`frosted_glass.gdshader`)**: `SCREEN_TEXTURE` を 49-tap でサンプリング（Vogel disk / 黄金角スパイラル分布で擬似一様配置）してぼかし、tint と合成するキャンバスアイテムシェーダー。`blur_radius` / `tint_color` / `tint_strength` を uniform として公開し、インスペクタから調整可能

#### Changed

- **ゲーム選択画面の InfoPanel をすりガラス風に変更**: 背景を単純な半透明黒（α=0.8）から、背景画像をぼかして見せるすりガラス効果へ置き換え。StyleBoxFlat の角丸 32px と縁ぼかし 12px は維持し、形状マスクとして利用
- **スクリーンセーバーのレンガモザイク背景を実ビューポートサイズに追従**: `viewport_w` / `viewport_h` のハードコード（1920×1080）をやめ、`get_viewport_rect().size` で実サイズを取得。16:10 等のアスペクト比でも上下に偏らず画面全体に行き渡るよう修正
- **モザイク行数のデフォルトを 5 → 4 に変更**: タイルがより大きく見える構成に
- **モザイクタイルサイズを画面高さ自動フィットに変更**: 新規 `@export fit_height_to_viewport`（既定 true）で `row_count` 行がビューポート高さを埋めるよう tile_size を自動拡縮。アスペクト比は `tile_size` の x/y で指定（既定 1.8）。固定サイズで運用したい場合は false に

### [Launcher v0.5.10] - 2026-04-19

#### Added

- **シマーシェーダー (`progress_shimmer.gdshader`)**: 白い光の帯が左→右に流れるエフェクトをGPUで実装。プログレスバーと LOADING ラベルで共通利用
- **ShimmerHelper**: シマーエフェクト適用を一元管理するヘルパースクリプト。プログレスバー用とラベル用で明るさのベース値を分けて提供
- **LOADING 状態の暗背景（DimBackground）**: ストアブラウズのタイル/バナー、カルーセルカードで読み込み中に暗い背景を表示し、NO IMAGE 状態と差別化

#### Changed

- **ストアブラウズのプログレスバー刷新**: 標準 `ProgressBar` から背景パネル＋フィルを分離したカスタム構成に変更。フィル側にシマーシェーダーを適用し、角丸を維持
- **LOADING ラベルのアニメーションをシェーダーに統合**: Tween ベースのブリージング（明滅）から、光の帯が横方向に流れるシマーエフェクトへ置き換え
- **LoadingLabel の構造変更**: 単純な Label から「暗背景 + Text」を内包する Control wrapper 構造に変更し、読み込み中の視認性を向上

### [Launcher v0.5.9] - 2026-04-14

#### Changed

- **ストアブラウズのUI構築を段階的に変更**: `_ready`での一括構築をやめ、`_process`で1フレーム1-2セクションずつ構築するローディングフェーズを導入（プログレスバー付き）
- **カルーセルのサムネイル読み込みを非同期化**: 同期的に全カードのサムネイルを読み込んでいた処理をバックグラウンドスレッドに移行し、LOADING→フェードインで表示

#### Fixed

- **画面遷移時のプチフリーズを解消**: スクリーンセーバー/ストアブラウズ/カルーセルのバックグラウンド画像読み込みスレッドにキャンセルフラグを追加し、シーン遷移時の`wait_to_finish()`ブロックを最小化
- **カルーセル画面でフォーカスがボタン等にあるときESCが効かない問題を修正**: 各フォーカスブロックに`ui_cancel`ハンドリングを追加

### [Launcher v0.5.8] - 2026-04-13

#### Added

- **TopBar / BottomBar コンポーネント分離**: TopBar（時計＋退出ボタン）とBottomBar（操作ヒント）を CanvasLayer ベースの独立 tscn に分離し、トランジション演出の影響を受けないよう改善
- **KeyHintBuilder**: BottomBar 用のキーキャップ風操作ヒントUI生成ヘルパーを追加
- **BrickMosaicBackground**: ゲーム背景画像をレンガ状に敷き詰め、行ごとに交互スクロールする背景コンポーネントを追加（バックグラウンドスレッド読み込み対応）
- **説明文スクロール対応**: ゲーム選択画面の説明文エリアをマウスホイール＆キーボードで滑らかにスクロール可能に
- **ゲーム情報パネルにアイコン表示**: プレイ人数・難易度・プレイ時間・コントローラー・オンラインの各スペックにアイコン画像（PNG）を追加

#### Changed

- **AutoScrollContainer の汎用化**: Label 専用だったスクロールコンテナを任意の Control 子ノードに対応
- **制作者表示をタグ形式に変更**: 単一ラベルから HBoxContainer ベースのタグ表示に変更
- **スペック表示のデザイン刷新**: ProgressBar を廃止し、アイコン＋バッジ形式のコンパクトな表示に変更
- **退出ボタン画像を exit.jpg → exit.png に差し替え**
- **FocusLayer導入**: フォーカス枠を CanvasLayer (layer=11) に移動し、TopBar より前面に確実に表示

### [Launcher v0.5.7] - 2026-04-01

#### Added

- **カルーセル上下操作用矢印ナビゲーション**: 選択中のゲームアイコンの上下に操作用の矢印ボタンを動的に配置。画面端での自動表示制御を実装
- **StoreBrowseスライドショー矢印の画像化**: 矢印をテキストから `arrow.png`（反転シェーダー付き）に変更し、デザイン性を向上

#### Changed

- **ゲーム起動時の演出改善**: フェードアウト時、BottomBarやカルーセルの矢印も一緒に同期して消えるように修正し、没入感を向上
- **スライドショーの操作レスポンス改善**: アニメーション中の再入力に対し、即座に連打した地点から次のスライドへ遷移するよう追従性を向上
- **ゲーム情報パネル（InfoPanel）の視覚効果**: エッジが背景に柔らかく溶け込むグラデーションぼかし（12px）と、角丸（32px）を適用

### [Launcher v0.5.6] - 2026-03-29

#### Changed

- **ダイアログ背景フェードアニメーション追加**: CommonDialog・ErrorDialog表示時のオーバーレイ（背景暗転）にフェードイン/フェードアウトアニメーションを追加
- **ゲーム起動エラーをErrorManager経由に変更**: 実行ファイル未検出(E-2002)・起動失敗(E-2001)をErrorDialog（エラーコード付き）で表示するよう変更
- **ErrorDialogから終了ボタンを削除**: エラー表示は情報提示のみとし、終了操作はAlt+F4（AppManager経由）に統一
- **ErrorDialog表示中のAlt+F4を即終了に変更**: エラーダイアログ表示中は確認ダイアログをスキップして即終了

### [Launcher v0.5.5] - 2026-03-29

#### Added

- **フルスクリーンをデフォルト化**: 起動時からフルスクリーンで表示
- **解像度スケーリング対応**: canvas_items stretchモードでFHD/WQHD等どの解像度でも同じUIレイアウトを維持
- **ダイアログのフォーカス枠をマウス操作時に非表示**: マウス/キーボード・コントローラーの入力切替を検知して表示制御

#### Changed

- **Godot Engine 4.5 → 4.6にアップデート**: project.godot、SPECIFICATION.md、README.md、AGENTS.mdのバージョン表記を更新

### [Launcher v0.5.4] - 2026-03-29

#### Fixed

- **戻る操作時のAppStateクリア順序を修正**: 遷移中の戻る操作を無視し、遷移受理後にAppStateをクリアするよう変更

### [Launcher v0.5.3] - 2026-03-29

#### Fixed

- **スライドショー1枚時の画面消失を修正**: ゲーム1枚以下のスライドショーで遷移アニメーションをスキップ

### [Launcher v0.5.2] - 2026-03-29

#### Fixed

- **最近プレイのソート不具合を修正**: `recently_played` クエリを `GROUP BY` + `MAX(start_time)` に変更し、ゲームごとの最新プレイ時刻で正しくソート
- **スライドショーアニメーションのロックをセクション単位に変更**: グローバルフラグからDictionaryに変更し、複数セクション間のブロッキングを解消
- **「すべて見る」ボタン不在時のフォーカス飛びを修正**: ボタンが存在するセクションでのみ `_on_view_all` を有効化

### [Launcher v0.5.1] - 2026-03-29

#### Fixed

- **ダイアログ連続表示時のポーズ解除バグを修正**: ダイアログ閉じアニメーション完了時に新しいダイアログが開かれていればポーズを維持するよう修正
- **StoreBrowseのmax_display_count未適用を修正**: セクションタイプ0（通常行）でManagerで設定した表示上限が反映されるよう修正

### [Launcher v0.5.0] - 2026-03-27

#### Added

- **StoreBrowse画面を新規導入**
  - `store_browse.tscn` / `store_browse.gd` / `store_browse_builder.gd` / `store_section_info.gd` を追加
  - `manual/popular/recent/recently_played/genre/players/difficulty/play_time/online/random/controller` セクションの表示に対応
- **画面遷移制御を追加**
  - `AppState` と `TransitionManager` をAutoLoad登録
  - `screensaver -> store_browse -> game_selection` の遷移フローと戻り導線を実装

#### Changed

- **UI/操作性を改善**
  - `game_selection` / `common_dialog` / `error_dialog` のフォーカス表示と遷移演出を調整
  - コントローラ操作時の視認性を改善
- **DBスキーマをv8へ更新**
  - `CURRENT_DB_VERSION` を8に更新
  - `store_sections` / `store_section_games` 取得APIを追加
- **アプリ名設定を更新**
  - `project.godot` の `config/name` をプロジェクト意図に合わせて変更

### [Launcher v0.4.6] - 2026-03-21

#### Changed

- **game_selection.gd を6つのコンポーネントに分割**
  - 1,257行のメインスクリプトを責務ごとに分離し、保守性を向上
  - `carousel_controller.gd`: カルーセルUIの座標計算・アニメーション
  - `input_handler.gd`: キーボード・コントローラー・マウス入力処理
  - `game_launcher.gd`: ゲーム起動・プロセス監視ロジック
  - `idle_manager.gd`: アイドル検知・スクリーンセーバー遷移
  - `game_info_display.gd`: ゲーム情報パネルの表示・更新
  - `button_style_manager.gd`: ボタンスタイル生成・グローアニメーション
  - メインの `game_selection.gd` はオーケストレーター（各コンポーネントの連携役）として残存

### [Launcher v0.4.5] - 2026-02-27

#### Added

- **ゲーム選択時の背景アニメーション改善**
  - ゲーム選択画面でスクロール操作に合わせて背景が上下にスライドする演出（`transition_up`, `transition_down`）を追加
  - スクロールを連続して行った場合（ドラムロール）でも、アニメーションが途切れずに次の位置から引き継がれてスムーズに繋がるように、内部制御を `AnimationPlayer` から `Tween` に移行

#### Changed

- **UIコンポーネントの tscn 化**
  - `CommonDialog`, `ErrorDialog` 内の UI 定義・スタイル割り当てをコードベースから `.tscn` ファイルの GUI エディタ設定へ統合
  - ダイアログボタンの動的サイズ調整やグローエフェクト、フォーカス時のスタイルを改善
- **スクリーンセーバーの機能改善**
  - `CenterContainer` を用いてロゴの配置とリサイズ制御を簡素化・安定化
  - 画面の「PRESS ENTER OR A BUTTON」の文字を「PRESS ANY KEY」に変更し、フォントサイズを拡大

### [Launcher v0.4.4] - 2026-02-14

#### Added

- **ゲーム選択画面のUI拡張**
  - 画面上部に現在時刻と「遊び終わる」ボタン（トップバー）を追加
  - 画面下部に操作ガイド（ボトムバー）を追加
  - 選択中のゲーム情報パネル内に「プレイ」ボタンを追加
  - サムネイル画像がない場合の「NO IMAGE」プレースホルダー表示を追加
  - マウスホイールによるスクロール操作に対応
- **UIエフェクトの追加**
  - フォーカスされたボタンやカード枠に対し、時間経過で明滅するブリージング（グロー）アニメーションを追加
- **共通ダイアログの機能拡張**
  - ダイアログボタン個別に色（緑のプレイボタンなど）を指定できるオーバーライド機能を追加

#### Changed

- **アイドルタイマーの仕様変更**
  - 警告ダイアログ表示までの時間を変更し、タイムアウト時にカウントダウンメッセージを更新するように修正

### [Launcher v0.4.3] - 2026-02-10

#### Added

- **「ゲーム登録なし」エラー(E-1006)の追加**
  - データベースは存在するがゲームが1件も登録されていない場合に特定のエラーコードを表示するように改善

#### Fixed

- **エラーダイアログの視認性改善**
  - `ErrorManager` と `DialogManager` を `CanvasLayer` に移行
  - 実行環境（ビルド後）において、背景の描画順序によりエラーダイアログが隠れてしまう問題を修正

### [Launcher v0.4.2] - 2026-02-10

#### Fixed

- **データベース読み込みの安定性向上**
  - データベースのカラムが`NULL`の場合にクラッシュする問題を修正（`_safe_int`, `_safe_bool`の導入）

### [Launcher v0.4.1] - 2026-02-09

#### Fixed

- **データベース読み込みの安定性向上**
  - データベースの整数・ブール型カラムにNULLが含まれていた場合にクラッシュする問題を修正
  - すべての必須フィールドに対してNULLチェックとデフォルト値を適用
- **起動オプション（Arguments）の読み込み修正**
  - データベースの `arguments` カラムが読み込まれていなかった問題を修正
  - 管理ソフトで設定した起動引数が正しく反映されるように改善
- **データベースカラム読み込みエラーの修正** (`e-1003`)
  - 新しく追加された `supported_connection` カラムの読み込みに対応
  - マネージャーによるデータベースマイグレーション後に正しく動作するように調整

### [Launcher v0.4.0] - 2026-02-08

#### Added

- **データベースバージョニング機能**
  - `DatabaseManager`クラスに`_check_and_migrate_db`メソッドを追加
  - スキーマバージョン管理用の`CURRENT_DB_VERSION`定数を導入
  - 古いデータベース（v0）から最新版（v1）への自動マイグレーション機能
- **ゲーム選択画面（Game Selection Screen）**
  - カルーセルUIによるゲーム選択機能
  - ゲーム情報の詳細表示（タイトル、サムネイル、説明など）
  - キーボード/コントローラーでの操作サポート
- **共通ダイアログシステム（Common Dialog System）**
  - `DialogManager`による統一されたダイアログ管理
  - 確認ダイアログ、情報ダイアログ、エラーダイアログのサポート
  - 最前面表示とフォーカス管理
- **エラーハンドリングシステム**
  - `ErrorManager`によるエラーコード（E-xxxx）管理
  - ユーザーフレンドリーなエラーメッセージ表示
  - `E-0001`（DB接続エラー）などの定義
- **管理者用終了確認機能（Global Exit Confirmation）**
  - `AppManager`による終了リクエスト（Alt+F4等）のフック
  - 誤操作防止のための確認ダイアログ表示
- **アイドルタイマー機能**
  - 実装: 30秒放置で警告、60秒でスクリーンセーバーへ自動遷移
- バージョン情報を`version.gd`のみで一元管理するように変更

### [Launcher v0.3.1] - 2025-12-27

#### Fixed

- データベースから取得したnull値の処理を修正
  - すべての文字列型プロパティ（game_id, title, description, genre, thumbnail_path, background_path, executable_path, controls, key_mapping）にnullチェックを追加
  - データベースのNULL値が原因で発生していたエラーを解消
- SQLite APIの互換性問題を修正
  - `query_with_args()`が存在しないため、`query()`メソッドを使用するように変更
  - SQLインジェクション対策として`_escape_sql_string()`関数を追加

### [Launcher v0.3.0] - 2025-12-27

マイルストーン4: データベース連携

#### Added

- データベース接続管理クラス（DatabaseManager）の実装
  - SQLiteデータベースへの接続機能
  - ゲーム情報の取得機能（get_all_games, get_game_by_id）
  - 製作者情報の取得機能（get_developers_by_game_id）
- データモデルクラスの実装
  - GameInfoクラス（ゲーム情報を表すデータモデル）
  - DeveloperInfoクラス（製作者情報を表すデータモデル）
- パス管理クラス（PathManager）の実装
  - プロジェクトルートの自動検出
  - データベースパス、ゲームフォルダパスの取得
- ゲーム選択画面でのデータベース連携
  - データベースからゲーム情報を読み込んで表示

#### Technical

- godot-sqliteプラグインを使用したSQLiteデータベース接続
- データアクセス層の実装（CRUD操作の読み取り部分）
- パス管理の統一（管理ソフトと同様のロジック）

### [Launcher v0.2.0] - 2025-12-26

マイルストーン3: 基本画面・画面遷移

#### Added

- スクリーンセーバー画面の実装
  - ロゴ画像（GCToneLogo.png）の表示
  - レスポンシブレイアウト（解像度・アスペクト比に応じた動的サイズ調整）
  - スタートメッセージの表示
- 画面遷移の基本実装
  - スクリーンセーバー → ゲーム選択画面への遷移（EnterキーまたはAボタン）
  - ゲーム選択画面 → スクリーンセーバーへの遷移（ESCキー）
- ゲーム選択画面のテスト実装（将来実装予定のプレースホルダー）
- Noto Sans JPフォントの導入（Regular/Bold）

#### Changed

- 背景色を統一（Color(0.1, 0.1, 0.1, 1)）

#### Technical

- Godot 4.5のシーン管理システムを使用
- 基本的なUIノードの配置（Control, ColorRect, VBoxContainer, Label, TextureRect）
- ビューポートサイズ変更時の動的レイアウト更新

### [Launcher v0.1.0] - 2025-12-26

マイルストーン1: Godotプロジェクトセットアップ完了

#### Added

- Godot 4.5プロジェクトのセットアップ
- SQLiteプラグイン（godot-sqlite）の導入
- プロジェクト構造の確立（scenes/, fonts/, images/フォルダなど）
- データベース接続確認機能

#### Technical

- Godot Engine 4.5のセットアップ
- GDExtension（godot-sqlite）の導入と動作確認
- SQLiteデータベース（prism.db）への接続確認
- データベース接続テストスクリプトの実装

### Launcher 将来のリリース予定

#### Launcher 開発版（v0.x.x）

- **v0.1.0** (マイルストーン1): Godotプロジェクトセットアップ完了
- **v0.2.0** (マイルストーン3): 基本画面・画面遷移
- **v0.4.0** (マイルストーン4, 5, 6): データベース連携・ゲーム表示・起動機能
- **v0.5.0** (マイルストーン7): UI完成・ゲーム情報詳細表示

#### Launcher 正式リリース版（v1.0.0以降）

- **v1.0.0** (マイルストーン8): MVP完成
- **v1.1.0** (マイルストーン9): 基本機能完成
- **v1.2.0** (マイルストーン10): データ管理機能完成
- **v2.0.0** (マイルストーン12): 完全版リリース

---

## Manager（管理ソフト）

### [Manager v0.8.1] - 2026-05-10

#### Fixed

- **surveys / play_records スキーマ drift を修正 (`MigrateV10ToV11`)**: SPEC v1.5.1 (2026-03-28) で `surveys`（JSON 形式 → ★評価+コメント）、`play_records`（累計方式 → イベントログ方式）に変更されたが、対応するマイグレーションが書かれていなかったため、`CREATE TABLE IF NOT EXISTS` の仕様により旧スキーマのテーブルが温存されていた。本マイグレーションで修正
  - 旧スキーマ判定: `surveys` は `submitted_at` 列、`play_records` は `play_count` 列の存在で検出
  - 行数 0 の場合のみ DROP & CREATE で新スキーマへ置換（データ保護）
  - 行数あり時は警告ログのみで自動マイグレーションをスキップ（手動対応に委ねる）
- **`games.version` 列を `CreateTables()` に追加**: 既存 DB では `MigrateGamesTable` の ALTER TABLE で後付けされていたが、`CreateTables()` 側に定義が無く、新規 DB と既存 DB でスキーマ定義が分散していた。`VerifySchema` 初回起動で検出し、本 PR 内で修正（`MigrateGamesTable` の ALTER は古い DB 向けのフォールバックとして残す）

#### Added

- **`VerifySchema()` 起動時スキーマ整合性検証**: `InitializeDatabase` 末尾に組み込み、全 11 テーブルの列名一覧と `ExpectedSchema` 定義を `PRAGMA table_info` 経由で比較。不一致があれば警告ログを出す（drift があってもアプリ動作はそのまま継続）
- **`MigrateV10ToV11` マイグレーション**: 上記 surveys / play_records drift 修正を担当
- **マイグレーション機構の helper メソッド**: `CreateSurveysTable` / `CreatePlayRecordsTable`（`CreateTables` と migration の両方から呼ぶ）、`TableHasColumn`、`GetTableRowCount` を追加
- **`AGENTS.md` に "Database Schema Management" セクションを追加**: スキーマ変更時に必ず対応するマイグレーションを書くこと、`tools/sqlite3/sqlite3.exe` で前後検証することを明文化

#### Technical

- `CurrentDbVersion`: `10` → `11`
- `CreateTables()` の `surveys` / `play_records` 作成を helper メソッド化（マイグレーションでも再利用するため）
- `ExpectedSchema` 静的辞書で全テーブルの期待列リストを定義（スキーマ変更時はここも同時更新する規約）

### [Manager v0.8.0] - 2026-05-07

#### Added

- **データベースバックアップ機能 (#96)**: `prism.db` のスナップショットを Manager から取得・管理できる機能を新設
  - **新規タブ「バックアップ」**: MainForm に4タブ目を追加し、専用UIを設置
  - **手動バックアップ**: 「今すぐバックアップ」ボタンで即時実行。SQLite Online Backup API (`SQLiteConnection.BackupDatabase`) を使用するため、Launcher が `prism.db` を開いている状態でも整合性を保ったコピーが可能
  - **自動バックアップ**: Manager 起動時に「前回バックアップから設定間隔（デフォルト 24h）以上経過していたら走らせる」方式。バックグラウンドタスクで起動をブロックしない
  - **マルチPC重複防止**: `settings.last_backup_at` を `BEGIN IMMEDIATE` (System.Data.SQLite における `IsolationLevel.Serializable`) で更新する lease 方式。複数のManagerが同時起動した場合でも片方のみが自動バックアップを走らせる
  - **バックアップ履歴一覧**: DataGridView で過去 100 件までを表示（開始日時 / 完了日時 / 実行PC / トリガ / 状態 / サイズ / ファイルパス）
  - **世代管理**: 設定値 `backup_retention_count`（デフォルト 30）を超える古いバックアップは自動削除
  - **保存先設定**: デフォルトは `<DBフォルダ>/backups/`、`BackupSettingsForm` から任意のフォルダに変更可能（FolderBrowserDialog 経由）
  - **リストア機能**: 履歴から選択 → 警告（「全Launcher を停止してください」「現DBは退避されます」）+ 4桁確認コード入力（`ResetDatabaseConfirmForm` の安全パターンを踏襲）→ 現DBを `safety_before_restore_HHmmss.db` として Online Backup API で退避 → SQLite 接続プールクリア → `prism.db` / `prism.db-wal` / `prism.db-shm` 削除 → バックアップを `prism.db` としてコピー → DB再初期化
- **新規ファイル**:
  - `Models/BackupLogEntry.cs` — `backup_log` テーブルのモデル（UNIX秒 → `DateTime` 変換ヘルパー付き）
  - `Repositories/BackupLogRepository.cs` — `backup_log` の CRUD（in_progress → success/failed の状態遷移）
  - `Repositories/SettingsRepository.cs` — `settings` テーブルへの汎用アクセサ + バックアップ lease 取得 (`TryAcquireBackupLease`)
  - `Services/BackupService.cs` — バックアップのドメインロジック、Online Backup API 呼び出し、リテンション処理、`BackupResult` 結果モデル
  - `Services/RestoreService.cs` — 退避 + 接続プールクリア + 上書きの安全な復元手順
  - `Controls/BackupSectionPanel.cs` (.Designer.cs) — バックアップタブのUserControl
  - `BackupSettingsForm.cs` (.Designer.cs) — 設定ダイアログ
  - `RestoreConfirmForm.cs` (.Designer.cs) — 復元確認ダイアログ

#### Changed

- **DBスキーマ v8 → v9**: additive migration（既存テーブル変更なし、Launcher 互換性に影響なし）
  - `backup_log` テーブル新設（id, started_at, completed_at, pc_name, file_path, file_size_bytes, status, error_message, trigger_type）
  - `settings` テーブルに 4 キー追加（`last_backup_at`, `backup_destination_path`, `backup_auto_interval_hours`, `backup_retention_count`）
  - 自動マイグレーション（`SchemaManager.MigrateV8ToV9`）が起動時に実行される
- **`settings` テーブルの KVS スキーマ整合化**: SPECIFICATION 1.3.1 (2026-02-08) で「単一行 → KVS方式」に変更されていたが、既存DB向けマイグレーションが実装されていなかったため、それより前に作られたDBは古いスキーマのままだった。Manager v0.8.0 では起動時に `settings` テーブルの `key` カラム有無を検査し、無ければ古いテーブルを `settings_legacy_v8_or_earlier` にリネームしてから KVS 方式の新テーブルを作成する移行処理を追加（`EnsureSettingsTableIsKvsSchema`）。旧データに実コードからの参照は無かったためデータロスは発生しないが、念のため legacy テーブルとして退避
- **`DatabaseManager` ファサードを拡張**: 新規プロパティ `BackupService`, `RestoreService`, `BackupLogRepository`, `SettingsRepository` を追加
- **`ProcessingDialog` を拡張 (#99 関連)**: `MarqueeMode` プロパティ（進捗が定量化できない処理向けの流れるバー）と `AllowCancel` プロパティ（中断不可な処理向けにキャンセルボタン非表示化）を追加
- **進捗バーが付いていなかった重め操作にプログレス表示を追加 (#99)**:
  - **EditGameForm**: ゲームID変更時の `Directory.Move(oldFolder, newFolder)` + `UpdateGameId` をマーキー進捗で表示。失敗時のロールバック（フォルダを元に戻す）処理にも進捗メッセージ。共有フォルダ越し・クロスボリューム時の体感速度を改善
  - **SettingsSectionPanel**: 「データベースリセット」操作（DBファイル削除 + テーブル再作成 + マイグレーション再実行）をマーキー進捗で表示
  - **GameSectionPanel**: 「ゲーム削除」操作（CASCADE で developers / game_versions / game_genres / play_records / surveys / store_section_games の関連レコード削除）をマーキー進捗で表示

#### Fixed

- **`backup_log` の自己参照スナップショット問題**: 「今すぐバックアップ」中の `backup_log` には `in_progress` 行が既に書き込まれているため、SQLite Online Backup API でコピーした .db ファイルにも自分自身の `in_progress` 行が含まれてしまう。後でその .db から復元すると、履歴一覧に「進行中のままのゴースト行」が現れる挙動だった
  - **`InsertInProgress` 時点で予定ファイルパスを記録するよう変更**: 旧実装ではバックアップ完了後にファイルパスを書き込んでいたため、自己参照スナップショットには空の `file_path` が入っていた。最初から書き込むことで「実ファイルが存在するか」での判別が可能になった
  - **`BackupLogRepository.ReconcileInProgressEntries(reasonIfMissing, thresholdSeconds)` を新設**: 各 `in_progress` 行について実ファイルの有無を確認し、存在すれば `success`（実ファイルサイズで `file_size_bytes` 更新）、存在しなければ `failed` に更新する。閾値秒数を null にすると全件対象、指定すると古い行のみ対象（実行中のバックアップに干渉しない）
  - **`MainForm` 起動時**: 閾値 600 秒で呼ぶ（Manager クラッシュ等で取り残された古い行のみリコンサイル）
  - **`MainForm.OnDatabaseRestored`**: 閾値なしで呼ぶ（復元直後はスナップショットの状態なので、参照されているファイルが実在すれば正しく `success` として復元される）
  - これにより「**バックアップ実体ファイルは存在しているのに失敗扱いになる**」という不自然なUXが解消され、復元に使ったまさにそのファイルが「成功」表示で履歴に残る
- **更新ボタンが新しいエントリを反映しないことがある問題の改善**: System.Data.SQLite の接続プールが古いスナップショットを保持して新しいコミットを見せないケースに対応。`BackupSectionPanel.RefreshDisplay()` 内で `SQLiteConnection.ClearAllPools()` を呼んで強制的にプールを掃除してから読み直すように変更
- **更新ボタンに包括リコンサイル機能を追加**: `BackupSectionPanel.RefreshDisplay()` で表示更新前に以下を実行：
  - `ReconcileInProgressEntries` を `recoverFailedWithExistingFile=true` で実行 — `failed` 行のうち `file_path` が指すファイルが実在するものを `success` に救済
  - `RecoverLegacyFailedEntriesByFolderScan` を実行 — `file_path` が空のまま残っている `failed` 行について、バックアップフォルダ内の `prism_yyyyMMdd_HHmmss.db` 形式のファイル名と `started_at` を ±1 秒範囲で照合し、一致すれば `success` として復元（旧バージョン Manager 由来のゴースト救済）
  - 「ファイルが本当に無い `failed` 行」（実際のディスク満杯エラー等）はそのまま保持されるので、誤って成功扱いに戻すことはない

#### Changed (UI)

- **バックアップ履歴の状態表示を視覚的に改善**: 表示は **成功 / 失敗 / 実行中** の3状態に統一しつつ、状態セルに **背景色**（淡緑 / 淡赤 / 淡青）と **ツールチップ** を追加。技術的な詳細（クラッシュ残骸 vs 実行直後の通常ケースなど）はツールチップに押し込んで本体表示はシンプルに：
  - **成功** → 「バックアップは完了済みです。この行から復元できます。」
  - **失敗** → `error_message` の内容（「中断理由: ◯◯」）。空なら「実ファイルが存在しないため復元には使えません。」
  - **実行中（30秒以内）** → 「現在実行中です（経過: X 秒）」
  - **実行中（30秒以上）** → 「前回 Manager 異常終了の残骸の可能性があります（経過: X 分）。更新ボタンを押すと実ファイルの有無で 成功/失敗 が確定します。」
  - 選択中も色味が保たれるよう `SelectionBackColor` も状態に合わせて設定

#### Changed (Safety Backup の取り扱い改善)

- **DBスキーマ v9 → v10**: `backup_log.trigger_type` の CHECK 制約を `('manual', 'auto')` から `('manual', 'auto', 'safety')` に拡張。SQLite では CHECK 制約を ALTER できないため、`backup_log_new` を作成してデータを移し替える方式で実施
- **退避ファイルの保存先を整理**: 旧 `<DBフォルダ>/safety_before_restore_yyyyMMdd_HHmmss.db`（プロジェクトルート直下）から、`<DBフォルダ>/backups/safety/safety_yyyyMMdd_HHmmss.db` に変更
  - `RestoreService.Restore` 内で退避先を構築・作成
  - Manager 起動時に旧形式のファイルを `backups/safety/` へ自動移動（`BackupService.MigrateLegacySafetyFilesToSafetyFolder`、idempotent）
- **退避ファイルの世代管理**: `backups/safety/` 内のファイルを最新10件まで保持し、超過分は古いものから自動削除（`RestoreService.ApplySafetyRetention`）
- **退避ファイルを履歴UIに統合**: `BackupLogRepository.RegisterUnknownSafetyFiles` で `backups/safety/` をスキャンし、`backup_log` に未登録の退避ファイルを `trigger_type='safety'`, `status='success'` で自動登録。`started_at` はファイル名のタイムスタンプから復元
  - Manager 起動時 / バックアップ復元後 / 更新ボタン押下時の各タイミングで実行
  - 履歴グリッドで「退避」とラベル表示（「手動」「自動」と並ぶ第3のトリガ種別）
  - 退避ファイルから直接「選択したバックアップから復元」も可能（誤った復元のロールバック手段）
- **`.gitignore` 修正**: `backups/`（通常バックアップ＋退避フォルダ）と旧形式の `safety_before_restore_*.db` を git 管理から除外。これまで `git add .` でうっかりコミットされる危険があった

### [Manager v0.7.6] - 2026-03-29

#### Changed

- **EditForm外部パス警告にバージョンアップ案内を追加**: ゲームフォルダ外のファイル選択時にバージョンアップ機能の利用を案内するメッセージを追記

### [Manager v0.7.5] - 2026-03-29

#### Fixed

- **ゲームIDリネーム時のロールバック追加**: DB更新失敗時にフォルダを元に戻すよう修正
- **DB初期化拒否時のパネル読み込みをスキップ**: DB未作成のまま起動した場合にパネル初期化を行わないよう修正

### [Manager v0.7.4] - 2026-03-29

#### Fixed

- **パネル初期化をDB確認後に移動**: DB存在チェック前にSettingsSectionPanelがDB接続するのを防止

### [Manager v0.7.3] - 2026-03-29

#### Fixed

- **バージョン切り替え時の編集内容保持**: currentDisplayingVersionの代入漏れを修正
- **ストアセクション編集画面の最大表示数を表示**: 非表示だったmax_display_count入力欄を常時表示に変更

### [Manager v0.7.2] - 2026-03-29

#### Fixed

- **ゲームIDリネーム時のデータ不整合を修正**: フォルダリネームをDB更新より先に実行し、失敗時のDB/ファイルシステム不整合を防止
- **ゲームIDリネーム後のパス変換を修正**: リネーム後にパステキストボックスを新フォルダベースに更新してから相対パス変換するよう修正

### [Manager v0.7.1] - 2026-03-28

#### Added

- **ゲームID編集機能**: EditGameFormからゲームIDを変更可能に（全関連テーブルの一括更新+フォルダリネーム）

#### Changed

- **タブ+セクションパネル構成に分割**: MainFormをGameSectionPanel / StoreSectionPanel / SettingsSectionPanelに分離
- **プロジェクト名をGCTonePrism_Managerに統一**: slnx / csproj / AssemblyName をリネーム

#### Fixed

- **ゲームID変更時のDB制約エラー**: PRAGMA foreign_keysをトランザクション外で制御するよう修正
- **DataGridViewの初期選択ハイライト解除**: ゲーム・ストア一覧で起動時の意図しない行選択を解消
- **行ヘッダー（三角マーク）非表示**: ゲーム・ストア一覧で左端の行ヘッダーを非表示に
- **列ヘッダーの選択ハイライト無効化**: セル選択時に列ヘッダーが青くならないよう修正

### [Manager v0.7.0] - 2026-03-27

#### Added

- **StoreSection管理機能を追加**
  - `StoreSectionInfo` / `StoreSectionRepository` / `StoreSectionForm` / `StoreSectionListForm` を追加
  - セクションの追加・編集・削除・並び替え、`manual` セクションの `display_text` 管理を実装
- **MainFormにストア管理導線を追加**
  - ツールバーからStoreSection一覧を開けるように変更

#### Changed

- **DBスキーマをv8へ更新**
  - `V6 -> V7`: `store_sections` / `store_section_games` テーブルを追加
  - `V7 -> V8`: `store_sections.display_text` 列を追加
  - `DatabaseManager` にStoreSection操作APIを追加
- **ゲーム管理フォーム差分を反映**
  - `AddGameForm` / `EditGameForm` / `VersionUpForm` のフォーム本体・Designer・resx差分を反映

### [Manager v0.6.2] - 2026-03-21

#### Changed

- **Form群から共通ロジックをさらに抽出（Phase 2）**
  - `Services/GameFormHelper.cs`: ComboBox初期化、ジャンル操作、テスト起動、プレースホルダー設定、ファイル自動検出を共通化
  - `Services/PathConversionHelper.cs`: 相対パス⇔絶対パス変換を一元化（ToRelativePath, ToAbsolutePath, ConvertSourceToDestination, ToRelativePathAfterCopy）
  - AddGameForm/EditGameForm/VersionUpFormから約766行削減

#### Removed

- **未使用コード・ファイルの削除**
  - `FileVersioningHelper.cs`: 呼び出し元ゼロのデッドコードを削除（機能はFileOperationService・PathConversionHelperで代替済み）
  - `scene_manager.gd`（Launcher側）: AutoLoad未登録・参照なしの未使用スクリプトを削除
  - `EditGameForm.cs`内の60行の開発時メモコメントを整理

### [Manager v0.6.1] - 2026-03-21

#### Changed

- **DatabaseManager.cs をRepositoryパターンで分割**
  - 1,763行のファイルを責務ごとに分離
  - `DatabaseConnection.cs`: 接続管理、WALモード、リトライロジック
  - `SchemaManager.cs`: テーブル作成・マイグレーション
  - `Repositories/GameRepository.cs`: ゲームのCRUD操作
  - `Repositories/VersionRepository.cs`: バージョン管理のCRUD
  - `Repositories/DeveloperRepository.cs`: 開発者情報のCRUD
  - `DatabaseManager.cs` は既存コードとの互換性を保つファサードとして残存
- **Formファイル群から共通ロジックを抽出**
  - `Services/DeveloperListManager.cs`: 開発者DataGridView管理（AddGameForm, EditGameForm, VersionUpFormで共通化）
  - `Services/FileOperationService.cs`: ファイルコピー・進捗追跡（AddGameForm, MainFormで共通化）
  - `Services/ImagePreviewHelper.cs`: 画像プレビュー処理（全Formで共通化）
  - AddGameForm: 1,109行 → 814行
  - EditGameForm: 1,130行 → 917行
  - MainForm: 917行 → 777行
  - VersionUpForm: 735行 → 573行

### [Manager v0.6.0] - 2026-02-09

#### Added

- **ゲームバージョン管理機能の完全実装**
  - バージョンごとのゲーム情報（タイトル、実行ファイル、設定など）を個別に保存・管理可能に
  - **バージョン追加**: 現在のバージョンをベースに新規バージョンを作成
  - **バージョン切り替え**: プルダウンメニューで編集対象のバージョンを即座に切り替え
  - **アクティブバージョン設定**: ランチャーで起動するバージョンを選択可能に
- **UI/UXの改善**
  - **画像プレビュー機能**: サムネイルと背景画像のプレビューを追加（アスペクト比ヒント付き）
  - **テスト起動ボタン**: 編集画面から直接ゲームをテスト起動できるボタンを追加
  - **起動オプション（Arguments）設定**: バージョンごとに起動引数を設定可能に
  - **ウィンドウサイズ修正**: フォームの初期サイズが正しく設定されない問題を修正

#### Technical

- **データベーススキーマ更新 (v6)**
  - `game_versions` テーブルの拡充: タイトル、ジャンル、説明など全フィールドをバージョン管理対象に
  - `arguments` カラムの追加: `games` および `game_versions` テーブル
  - `games` テーブルへの同期ロジック: 選択されたバージョンの情報をメインテーブルに自動同期

#### Fixed

- **バージョン入力UIの改善**
  - バージョンアップ画面の初期値を `v` に設定（入力の手間を削減）
  - ゲーム追加画面の初期バージョンを `v1.0.0` に統一
  - 入力欄のレイアウト調整（ラベル被りを修正）
- **フォルダ構造の最適化**
  - バージョンフォルダの `v` プレフィックスを必須化し、統一された命名規則を適用

### [Manager v0.5.3] - 2026-02-08

#### Fixed

- **データベースマイグレーション処理の修正**
  - アプリ起動時に `supported_connection` カラムの存在チェックと自動追加を行うように修正
  - データベースバージョンに関わらず、不足しているカラムがある場合は自動的に修正されるように改善（自己修復機能）
- **UI文言の微調整**
  - アプリ起動時にデータベース競合防止のための警告メッセージ（1台での運用推奨）を追加
  - ゲーム追加・編集画面に「通信プレイ対応」の設定項目を追加（なし / ローカル通信 / オンライン通信）
  - データベースリセット画面の警告メッセージをよりユーモラスに変更（「リセット実行ボタンは押そうとすると逃げますので、頑張って押してください」）

### [Manager v0.5.2] - 2026-02-08

#### Added

- データベースバージョニング機能の実装
  - `DatabaseManager`クラスにスキーマバージョン管理機能を追加
  - アプリ起動時にデータベースバージョンをチェックし、自動的に最新版へマイグレーション
  - バージョン情報ダイアログにデータベース構造バージョン（v1）を表示
- `DatabaseManager` APIの改善
  - `GetMinDisplayOrder`メソッドの追加
  - `AddGame`, `UpdateGame`メソッドのシグネチャを`GameInfo`オブジェクトを受け取る形に統一

#### Fixed

- `DatabaseManager`のビルドエラー修正
  - `PathManager.GetDatabasePath()`メソッド呼び出しを`PathManager.DatabasePath`プロパティ参照に修正
  - `DeveloperInfo`モデルのプロパティ使用方法を修正（`GradeDisplay` -> `Grade`）
  - 開発者情報（`developers`）とジャンル（`genre`）のデータ型変換処理を修正

### [Manager v0.5.1] - 2025-12-27

#### Added

- ゲーム追加時の不要なフォルダスキップ機能
  - Unity、Unreal Engine、Godotなどの開発環境の不要なフォルダを自動スキップ
  - スキップ対象: Library, Temp, Intermediate, Saved, .import, .vs, .git, node_modulesなど
- 長いパス名のサポート
  - Windowsのパス長制限（260文字）を超える場合に`\\?\`プレフィックスを使用して対応
- データベース画面に製作者情報カラムを追加
  - 「姓 名 (期生)」形式で表示
  - 複数の製作者はカンマ区切りで表示

#### Changed

- ウィンドウタイトルを「ゲームセンターTONE Prism 管理ソフト」に変更
- データベース画面のカラム順序を調整
  - ゲームID → タイトル → リリース年 → 製作者 → ランチャー表示
- ランチャー表示カラムの幅を調整

#### Fixed

- ゲーム追加時の長いパス名によるエラーを修正
- 一部のファイルがコピーできない場合でも処理を続行するように改善

### [Manager v0.5.0] - 2025-12-27

#### Added

- ジャンルリストの拡張
  - ジャンル選択肢を16種類から22種類に拡張
  - 新規追加ジャンル: アーケード、RPG、カジュアル、ストラテジー、その他、ドライビング/レース、ホラー、ファミリー、シミュレーター、脳トレ、リズムアクション、クイズ、教育、フィットネス
- 期生入力欄に説明を追加
  - 「期生（0で教員）」と表示し、0を入力すると教員として扱われることを明示

#### Changed

- ジャンル名の変更・統合
  - 「ロールプレイング」→「RPG」
  - 「レース」→「ドライビング/レース」
  - 「音楽ゲーム」→「リズムアクション」
  - 「学習・教育」→「教育」
  - 「トレーニング」→「フィットネス」
- 削除されたジャンル: テーブルゲーム、コミュニケーション、ツール

### [Manager v0.4.1] - 2025-12-27

#### Fixed

- developers.last_nameがNULLの場合の処理を修正
  - `DatabaseManager.GetDevelopersByGameId`でlast_nameがNULLの場合にIsDBNullでチェックするように修正
  - `DeveloperInfo.FullName`でLastNameがNULLの場合はFirstNameのみを返すように修正

### [Manager v0.4.0] - 2025-12-27

#### Added

- SQLite同時アクセス対応機能
  - WAL（Write-Ahead Logging）モードの有効化
    - ランチャー起動中でも管理ソフトでデータベース操作が可能に
    - 既存データベースにも自動適用
  - リトライ機構の実装
    - ネットワークドライブ経由での一時的なロックエラーを自動リトライ
    - 最大3回リトライ、指数バックオフ（50ms, 100ms, 200ms）
    - 学校サーバー経由での使用に最適化

#### Changed

- データベース接続方式を改善
  - 接続文字列に`Journal Mode=WAL`と`Busy Timeout=5000`を追加
  - すべてのデータベース接続でWALモードを自動有効化

#### Fixed

- エラーハンドリングの改善
  - SQLiteExceptionを具体的に処理し、分かりやすいエラーメッセージを表示
  - データベースロック時: 「データベースが使用中です」メッセージ
  - データベース破損時: 「データベースが破損しています」メッセージ
  - その他のエラーも適切なメッセージを表示

#### Technical

- `OpenConnectionWithWalMode()`メソッドを追加（接続時にWALモードを確実に有効化）
- `ExecuteWithRetry<T>()`メソッドを追加（リトライ機構）
- `GetUserFriendlyErrorMessage()`メソッドを追加（エラーメッセージの改善）
- 主要な書き込み操作（AddGame, UpdateGame, DeleteGame）にリトライ機構を適用

### [Manager v0.3.0] - 2025-12-27

#### Added

- バージョン情報表示機能
  - 設定メニューに「バージョン情報」メニュー項目を追加
  - アセンブリ情報から製品名、バージョン、会社名、著作権情報を取得して表示
  - AssemblyInfo.csのAssemblyProductとAssemblyTitleをGCTonePrism_Managerに統一

### [Manager v0.2.0] - 2025-12-27

#### Added

- ジャンル選択機能の改善
  - マイニンテンドーストア準拠の16ジャンルを定義（GenreList.cs）
  - ジャンル入力UIをTextBoxからCheckedListBoxに変更（複数選択可能）
  - 既存のジャンルデータも正しく読み込めるように実装

#### Changed

- 製作者情報の登録ルールを変更
  - 姓（LastName）を空欄でも登録可能に変更
  - データベースのdevelopersテーブルのlast_nameカラムのNOT NULL制約を削除
  - 既存データベースのマイグレーション処理を追加
- ファイルパスの保存方式を変更
  - サムネイル・背景・実行ファイルのパスを相対パスで保存（games/{game_id}/フォルダからの相対パス）
  - サブフォルダ内のファイルも正しく相対パスで保存されるように改善
  - パス入力欄を編集可能に変更（ReadOnlyをfalseに）
- 実行ファイルの自動検出を改善
  - UnityCrashHandlerなどのクラッシュハンドラーを除外
  - 除外パターン: .console.exe, UnityCrashHandler64.exe, UnityCrashHandler32.exe など

#### Fixed

- ジャンルUIの表示崩れを修正（CheckedListBoxの高さと下のコントロールの位置を調整）
- IndexOutOfRangeExceptionを修正（CheckedListBoxの操作を安全に）
- サブフォルダ内の実行ファイルの相対パス保存を修正

#### Technical

- データベース確認ログに相対パス/絶対パスの判定を追加
- デバッグログを追加（開発時の確認用）

### [Manager v0.1.1] - 2025-12-26

#### Changed

- プロジェクトフォルダ名を`Manager`から`GCTonePrism_Manager`に変更

#### Fixed

- PathManagerの検出ロジックを簡素化（Launcherフォルダの検出ロジックを削除）
- GCTonePrism_Managerフォルダ内に実行ファイルがない場合のエラーチェックを追加
- 不正な配置での実行を防止（エラーメッセージを表示して起動を停止）

### [Manager v0.1.0] - 2025-12-26

マイルストーン2: 管理ソフト基本機能完成

#### Added

- 管理ソフトプロジェクトの新規作成（Windows Forms C#アプリケーション）
- PathManager: アプリケーションパス、データベースパス、ゲームフォルダパスの管理機能
- DatabaseManager: SQLiteデータベースの作成、初期化、操作機能
- データモデルの実装（GameInfo, DeveloperInfo, PlayRecord, Survey, Settings）
- Microsoft.Data.Sqliteパッケージの導入
- ゲーム追加機能（AddGameForm）
  - ゲームフォルダの選択とコピー
  - 実行ファイルの選択
  - サムネイル画像・背景画像の選択
  - ゲーム情報の入力（タイトル、説明、ジャンル、制作年など）
  - 製作者情報の追加・編集（DataGridViewを使用）
- ゲーム編集機能（EditGameForm）
  - 既存ゲーム情報の編集
  - 製作者情報の編集
- ゲーム削除機能
- ゲーム表示順序の変更機能（表示順序の並び替え）
- データベースリセット機能（確認ダイアログ付き）
- メイン画面（MainForm）の実装
  - ゲーム一覧の表示
  - ゲーム追加・編集・削除ボタン
  - データベースリセットボタン
- 製作者管理画面（DeveloperForm）の実装

#### Changed

- プロジェクトフォルダ名を`GCTonePrism.Manager`から`Manager`に変更
- プロジェクト名を`Manager`に変更（RootNamespaceとAssemblyNameを更新）

#### Fixed

- ゲーム追加時の画像ファイルのバリデーションを追加
- 期生入力フィールドを数値形式に変更

### Manager 将来のリリース予定

#### Manager 開発版（v0.x.x）

- **v0.1.0** (マイルストーン2): 管理ソフト基本機能完成
- **v0.2.0** (マイルストーン8): MVP対応版（ランチャーv1.0.0と連携）

#### Manager 正式リリース版（v1.0.0以降）

- **v1.0.0** (マイルストーン11): 管理ソフト完全版完成
- **v1.1.0** (マイルストーン12): 完全版リリース対応

---

## プロジェクトマイルストーン

開発の進捗管理用のマイルストーン一覧です。詳細は [SPECIFICATION.md](SPECIFICATION.md) を参照してください。

- **マイルストーン1**: Godotプロジェクトセットアップ完了
- **マイルストーン2**: 管理ソフト基本機能完成
- **マイルストーン3**: 基本画面・画面遷移
- **マイルストーン4**: データベース連携
- **マイルストーン5**: ゲーム表示・選択機能
- **マイルストーン6**: ゲーム起動機能
- **マイルストーン7**: UI完成・ゲーム情報詳細表示
- **マイルストーン8**: MVP完成
- **マイルストーン9**: 監視ソフト基本機能完成
- **マイルストーン10**: 基本機能完成
- **マイルストーン11**: データ管理機能完成
- **マイルストーン12**: 管理ソフト完全版完成
- **マイルストーン13**: 完全版リリース

---

[Launcher Unreleased]: https://github.com/ken1208git/GCTonePrism/compare/launcher-v0.1.0...HEAD
[Launcher v0.5.7]: https://github.com/ken1208git/GCTonePrism/releases/tag/Launcher_v0.5.7
[Launcher v0.4.5]: https://github.com/ken1208git/GCTonePrism/releases/tag/Launcher_v0.4.5
[Launcher v0.4.3]: https://github.com/ken1208git/GCTonePrism/releases/tag/Launcher_v0.4.3
[Manager v0.7.6]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.7.6
[Manager v0.6.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.6.0
[Manager v0.5.1]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.5.1
[Manager v0.5.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.5.0
[Manager v0.4.1]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.4.1
[Manager v0.4.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.4.0
[Manager v0.3.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.3.0
[Manager v0.1.1]: https://github.com/ken1208git/GCTonePrism/releases/tag/Manager_v0.1.1
[Manager v0.1.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.1.0
