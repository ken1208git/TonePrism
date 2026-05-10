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
- **`ExpectedSchema` 辞書 ↔ SPEC §7.3 の同期**: `SchemaManager.cs` 末尾の `ExpectedSchema` 辞書を更新したら、`SPECIFICATION.md` §7.3 のテーブル定義表（およびできれば §7.2 階層図 / §7.4 ER 図）も同時に更新すること。VerifySchema は ExpectedSchema との比較のみで SPEC は読まないため、SPEC 側の drift は人間が見つける必要がある。
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

## Cross-component Standards
新規クライアントコンポーネント（Launcher / Manager / Monitor / 将来 Tools 等）を追加・改修する際は、
以下のベースラインを必ず満たすこと。これは「ファイルログが無いと運用上の事故調査が不可能」(#116) という
過去の教訓を踏まえた **横断的な共通要件** である。

- **ファイルログ**: 共有プロジェクトルート（prism.db のあるディレクトリ）の `logs/{component}/` 配下に出力
  - 例: `<project_root>/logs/manager/manager_<PCname>_<YYYY-MM-DD_HHmmss>.log`
  - 例: `<project_root>/logs/launcher/launcher_<PCname>_<YYYY-MM-DD_HHmmss>.log`
- **1 起動セッション = 1 ファイル**（日跨ぎでもローテートしない）
  - 複数 PC が同じファイルに書き込まないので、書き込み競合・行間 interleaving が発生しない
  - PC 名と起動時刻でファイル名がユニークになる（同秒衝突は連番サフィックスで回避）
- **レベル**: 最低 INFO / WARN / ERROR の 3 段階（将来 #85 で DEBUG 追加予定）
- **フォーマット**: `[YYYY-MM-DD HH:mm:ss] [LEVEL] [Module] message` で統一
- **保持期間**: 30 日。古いファイルは起動時に自動削除（mtime 基準、現セッションのアクティブファイルは保護）
- **既存出力との統合**: 既存の `Console.WriteLine` (.NET) / `print()` (GDScript) 等は、
  Console.SetOut / OS.add_logger の自動キャプチャで **コード変更ゼロ** でファイルにも流すこと。
  新規コードでは `Logger.Info/Warn/Error` 等で明示的にレベル指定
- **起動・終了イベント**: 必ず INFO で記録（事故調査で「いつ落ちたか」が分かるように）
- **Logger 自体の障害は握り潰す**: ログ機構の例外でアプリ起動を止めない・無限ループを起こさない
  （Logger 内部の例外は **ログにも書かない** こと。書くと再帰してハング）

参照実装: `GCTonePrism_Manager/Services/Logger.cs`, `GCTonePrism_Launcher/scripts/logger.gd`
（クライアント追加時は SPECIFICATION.md §3.6 の仕様も合わせて参照）
