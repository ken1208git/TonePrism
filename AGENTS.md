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

## Launcher Implementation
- ユーザーが調整しやすいよう、UIやレイアウトはなるべく `.tscn`（シーンファイル）で実装すること。
- 変数はなるべく `@export` で定義し、エディタから調整可能にすること。

## Launcher Verification
- Launcher の動作確認は MCP を用いて行うこと。
- Godot エディタのパスは以下を使用すること。
  - `C:\Users\busin\Documents\Godot Engine\Godot_v4.5.1-stable_win64.exe`
