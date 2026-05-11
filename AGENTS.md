# AGENTS Rules

## Session Start
- `README.md`、`SPECIFICATION.md`、`CHANGELOG.md` をくまなく読む。
- オープン中のイシューとマイルストーンの進捗を確認し、現状をユーザーに共有する。

## Branch Strategy
- main で直接実装せず、機能単位でブランチを切る。
- ブランチ名は `feature/〇〇`（新機能）/ `fix/〇〇`（修正）。

## File Structure
- 新機能の追加は、既存ファイルへの追記ではなく新ファイル作成を必ず検討する。
- 既存ファイルへの関数追加時、ファイルの責務と合わなければ別ファイルにする。

## Release and Versioning
- コミット直前に Launcher / Manager / Monitor のバージョン番号を上げるべきかを必ず提案する。
- バージョンを上げる場合は `CHANGELOG.md` に変更内容を記載する。
- タグ/Release を作成した場合は `CHANGELOG.md` 末尾のリンク定義を追記・更新する。
- 新規クライアントコンポーネント追加時の更新チェックリストは **SPECIFICATION.md §3.7.8** を参照。

## Specification Management
- `SPECIFICATION.md` の議論・追記・修正が一段落したら、変更履歴セクションへの追記（日付・バージョン・変更内容・変更者）を必ず提案する。バージョン番号は既存最新からインクリメント。

## Database Schema Management
- スキーマ変更時のワークフロー（マイグレーション関数・`CurrentDbVersion` 増分・`sqlite3.exe` 検証・`ExpectedSchema` ↔ SPEC §7.3 同期）は **SPECIFICATION.md §7.6** を参照のこと。
- 重要： `CreateTables()` を編集したら必ず `MigrateVxToVy` を書く。スキーマ drift の温床。

## Cross-component Standards (ファイルログ)
- 新規クライアントコンポーネント（Launcher / Manager / Monitor 等）を追加・改修する際は、ファイルログ基盤を必ず実装する。仕様詳細は **SPECIFICATION.md §3.6** を参照。
- 参照実装: [`GCTonePrism_Manager/Services/Logger.cs`](GCTonePrism_Manager/Services/Logger.cs), [`GCTonePrism_Launcher/scripts/logger.gd`](GCTonePrism_Launcher/scripts/logger.gd)
- Logger 自体の障害は握り潰す（再帰ハング回避のため、Logger 内部例外はログにも書かない）。

## Launcher Implementation
- UI やレイアウトはなるべく `.tscn`（シーンファイル）で実装する。
- 変数はなるべく `@export` で定義し、エディタから調整可能にする。

## GitHub Integration

### 作業中
- 作業がイシューに関連する場合、コミットメッセージ・PR に `#イシュー番号` を含める。
- 作業中に発見したバグ・課題・TODO は GitHub イシュー登録を提案する。
- ユーザーが「覚えておいて」と言った内容は、メモリ保存に加えてイシュー登録も提案する。

### 作業完了時
- 完了したイシューのクローズを提案する。
- 全イシュー完了時はマイルストーンのクローズも提案する。
- 次に取りかかるべきイシューを提示する。

### 仕様書との同期
- `SPECIFICATION.md` に記載があるがイシューが存在しない機能・課題を発見した場合、イシュー作成を提案する。
- イシューに記載があるが `SPECIFICATION.md` に未反映の仕様変更を発見した場合、仕様書への追記を提案する。
