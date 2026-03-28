# 変更履歴

このプロジェクト（Prismランチャーシステム）の重要な変更点を全て記録します。

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) に従い、
このプロジェクトは [Semantic Versioning](https://semver.org/lang/ja/) に準拠しています。

**注意**: このCHANGELOGはソフトウェア本体のバージョンを追跡します。仕様書の変更履歴については、[SPECIFICATION.md](SPECIFICATION.md)の「変更履歴」セクションを参照してください。

---

## Launcher（ランチャー本体）

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

[Launcher Unreleased]: https://github.com/ken1208git/GCTonePrism/compare/launcher-v1.0.0...HEAD
[Launcher v0.4.5]: https://github.com/ken1208git/GCTonePrism/releases/tag/launcher-v0.4.5
[Launcher v0.4.3]: https://github.com/ken1208git/GCTonePrism/releases/tag/launcher-v0.4.3
[Manager v0.6.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.6.0
[Manager v0.5.1]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.5.1
[Manager v0.5.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.5.0
[Manager v0.4.1]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.4.1
[Manager v0.4.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.4.0
[Manager v0.3.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.3.0
[Manager v0.1.1]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.1.1
[Manager v0.1.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.1.0
