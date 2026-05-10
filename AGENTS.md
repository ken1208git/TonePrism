# AGENTS Rules

## Release and Versioning
- コミットする直前に、Launcher と Manager と Monitor のバージョン番号を上げる必要があるかを必ず提案すること。
- バージョン番号を上げる場合は、必ずそのバージョンの変更内容を `CHANGELOG.md` に記載すること。
- リリース（タグ/Release）を作成した場合は、`CHANGELOG.md` 末尾のリンク定義に該当バージョンのURLを必ず追記・更新すること。

## Specification Management
- SPECIFICATION.md の議論・追記・修正が一段落したら、以下を必ずユーザーに提案すること：
  1. 変更履歴セクションへの追記（日付・バージョン・変更内容・変更者）
  2. 変更者の記載をどうするか（ユーザー名 or 共同作業の場合の記載方法）
- バージョン番号は既存の変更履歴の最新バージョンをインクリメントすること。

## Session Start
- セッション開始時に `README.md`、`SPECIFICATION.md`、`CHANGELOG.md` をくまなく読むこと。

## File Structure
- 新機能を追加する前に、既存ファイルに足すのではなく新しいファイルを作るべきか必ず検討すること。
- 既存ファイルに関数を追加する際、そのファイルの既存の責務と一致しない場合は別ファイルにすること。

## Database Schema Management
- **スキーマ変更には必ずマイグレーションを書く**: `GCTonePrism_Manager/SchemaManager.cs` の `CreateTables()` 内のスキーマ定義を変更したら、対応する `MigrateVxToVy` 関数を追加し、`CurrentDbVersion` をインクリメントすること。
- **`CREATE TABLE IF NOT EXISTS` は新規 DB 用**: 既存テーブルが存在する場合は何もしないため、スキーマ変更には ALTER TABLE / DROP+CREATE+データ移行 を伴う明示的なマイグレーションが必要。
- **マイグレーション実装後は `tools/sqlite3/sqlite3.exe` で検証**: 変更前後で `PRAGMA table_info(<tableName>);` を実行し、想定通りのスキーマになっているか確認すること。Manager 起動時の `VerifySchema()` でも自動チェックされるが、手動検証も併用する。
- **過去事例**: SPEC v1.5.1 (2026-03-28) で `surveys` / `play_records` のスキーマを変更したが、マイグレーション未実装のため drift が温存されていた（Manager v0.8.1 の `MigrateV10ToV11` で修正）。

## Branch Strategy
- 原則 main ブランチのまま実装せず、大まかな機能単位でブランチを分けること。
- ブランチ名は `feature/〇〇`（新機能）、`fix/〇〇`（修正）の形式を使うこと。

## GitHub Integration

### セッション開始時
- オープン中のイシューとマイルストーンの進捗を確認し、現状をユーザーに共有すること。

### 作業中
- 作業がイシューに関連する場合、コミットメッセージやPRに `#イシュー番号` を含めること。
- 作業中に発見したバグ・課題・TODOは GitHub イシューとして登録を提案すること。
- ユーザーが「覚えておいて」と言った内容は、メモリ保存に加えてイシュー登録も提案すること。

### 作業完了時
- 完了したイシューのクローズを提案すること。
- マイルストーンの進捗を確認し、全イシュー完了時はマイルストーンのクローズも提案すること。
- 次に取りかかるべきイシューを提示すること。

### 仕様書との同期
- SPECIFICATION.md に記載があるがイシューが存在しない機能・課題を発見した場合、イシュー作成を提案すること。
- イシューに記載があるが SPECIFICATION.md に反映されていない仕様変更を発見した場合、仕様書への追記を提案すること。

## Launcher Implementation
- ユーザーが調整しやすいよう、UIやレイアウトはなるべく `.tscn`（シーンファイル）で実装すること。
- 変数はなるべく `@export` で定義し、エディタから調整可能にすること。
