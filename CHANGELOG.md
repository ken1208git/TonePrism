# 変更履歴

このプロジェクト（Prismランチャーシステム）の重要な変更点を全て記録します。

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) に従い、
このプロジェクトは [Semantic Versioning](https://semver.org/lang/ja/) に準拠しています。

**注意**: このCHANGELOGはソフトウェア本体のバージョンを追跡します。仕様書の変更履歴については、[SPECIFICATION.md](SPECIFICATION.md)の「変更履歴」セクションを参照してください。

---

## Launcher（ランチャー本体）

### [Launcher Unreleased]

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
- **v0.3.0** (マイルストーン4): データベース連携
- **v0.4.0** (マイルストーン5): ゲーム表示・選択機能
- **v0.5.0** (マイルストーン6): ゲーム起動機能
- **v0.6.0** (マイルストーン7): UI完成・ゲーム情報詳細表示

#### Launcher 正式リリース版（v1.0.0以降）

- **v1.0.0** (マイルストーン8): MVP完成
- **v1.1.0** (マイルストーン9): 基本機能完成
- **v1.2.0** (マイルストーン10): データ管理機能完成
- **v2.0.0** (マイルストーン12): 完全版リリース

---

## Manager（管理ソフト）

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
- **マイルストーン9**: 基本機能完成
- **マイルストーン10**: データ管理機能完成
- **マイルストーン11**: 管理ソフト完全版完成
- **マイルストーン12**: 完全版リリース

---

[Launcher Unreleased]: https://github.com/ken1208git/GCTonePrism/compare/launcher-v1.0.0...HEAD
[Manager v0.1.1]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.1.1
[Manager v0.1.0]: https://github.com/ken1208git/GCTonePrism/releases/tag/manager-v0.1.0
