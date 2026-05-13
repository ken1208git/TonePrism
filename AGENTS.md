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

## Naming Conventions

リポジトリ内の各層で命名規約を分ける。理由は層ごとに「衝突回避の必要性」が違うため。

- **トップレベル dir 名 (リポジトリ構造) = 短縮**: `Launcher/` / `Manager/` / `Monitor/` / `Companions/`。リポジトリ全体が GCTonePrism なので prefix は冗長。`Companions/` 配下のサブツール dir も `Companions/Updater/` / `Companions/WindowProbe/` と短縮。
- **csproj / アセンブリ名 / exe ファイル名 = `GCTonePrism_<Name>`**: `GCTonePrism_Manager.exe` / `GCTonePrism_Updater.exe` / 将来 `GCTonePrism_WindowProbe.exe`。**理由: 実機 OS との接点 (tasklist / `Process.GetProcessesByName` / Windows のショートカット / プロセス管理 UI) で `Manager.exe` / `Updater.exe` のような汎用名は他アプリと衝突する。特に Chrome / Edge / 各種 Updater など多くのアプリが `Updater.exe` を使う**。prefix 維持で uniqueness を担保。
  - **例外**: `Common` / `Core` / `Shared` 等の **汎用すぎる名前** は assembly 衝突 (= GCTonePrism 内の他コンポーネントが同名 `Common` を持つ可能性、または他アプリの `Common.dll` との namespace 衝突) を避けるため `GCTonePrism_<Parent><Name>` の形式で disambiguation を許容。例: `Companions/Common/` (dir 短縮) → `GCTonePrism_CompanionsCommon.csproj` (Parent=Companions を含めて衝突回避)。SPEC §2.4 参照。
- **C# namespace = `GCTonePrism.<Name>`**: `GCTonePrism.Manager` / `GCTonePrism.Updater`。namespace 衝突は実害が小さいが、コード読みやすさのため exe 名と一貫させる。

主要 vs サポートの分類:
- **主要アプリ** (Launcher / Manager / Monitor): リポジトリルート直下に置く独立 dir。
- **サポート exe** (それ以外、Updater / WindowProbe / PauseOverlay 等): `Companions/` 配下に集約。dev-time / runtime 共通の配置で、Launcher 補助か Manager 補助かを問わず統一する (Launcher 補助 Companion は Launcher が相対 path で呼び出して使う)。

詳細は **SPECIFICATION.md §2.4** (Companions) / **§3.7.4** (Updater) を参照。

## Release and Versioning
- コミット直前に Launcher / Manager / Monitor / 各 Companion (Updater / WindowProbe 等) の各バージョン番号を上げるべきかを必ず提案する。
- **Bundle version の bump はリリース実行時のみ**。`Release.bat` を押す直前に `CHANGELOG.md` の `## Bundle` セクションに新エントリを追加（最上段、`### [Bundle vX.Y.Z]` 形式）。これが Bundle version と release_notes 両方の SoT。開発中の component bump とは別タイミング。
- Bundle bump ルール:
  - **Major**: いずれかの component に breaking change（DB schema 変更含む）
  - **Minor**: いずれかの component で機能追加
  - **Patch**: bugfix のみ
- バージョンを上げる場合は `CHANGELOG.md` に変更内容を記載する。
- Bundle version の詳細仕様は **SPECIFICATION.md §3.7.7** を参照。
- 新規クライアントコンポーネント追加時の更新チェックリストは **SPECIFICATION.md §3.7.8** を参照。

## CHANGELOG Section Roles
- `## Bundle` セクション: リリース単位の **summary**。Release.ps1 がここを抜き出して GitHub Releases の本文に流すため、エンドユーザー (来場スタッフ / 顧問の先生 / 部員) が読んで意味が分かる粒度で書く（1-3 行 + 影響を受けるコンポーネント版数の言及）
- `## Launcher` / `## Manager` / 将来の `## Monitor` 等のコンポーネント別セクション: 開発者向けの **詳細履歴**。技術判断、PR / issue 番号、設計の経緯等を書く
- `## Release Tooling` セクション: `Release.ps1` / `Release.bat` / `Install.bat` (Phase 2 以降) / `Updater` (Phase 3 以降) 等の配布インフラの変更履歴。リリース当日のエンドユーザーは見ないが、開発者が「リリーススクリプトのこの挙動はいつから？」を辿るために残す
- 1 件の変更は **どれか 1 セクション** にのみ詳細を書き、他セクションからは「Manager v0.8.10 を参照」のような形で参照する。重複記述は避ける
- **1 PR 内の version bump は原則 1 回のみ**。PR 初コミットで version エントリを確定させ、レビュー対応コミットでは既存エントリの description を加筆・修正する形にする。新規 version エントリを毎回作ると CHANGELOG が「途中段階の version」を抱える ("v0.1.4 と v0.1.5 の差分はレビュー対応のみ" のような無意味な差分が残る) ため避ける。バージョン番号自体を変える必要が出た場合 (breaking change が見つかった、想定範囲を超える機能追加が混入した等) は例外として変更可。これにより PR レビュー進行中にユーザーが merge ボタン押しても CHANGELOG が常に正しい状態になる

## Specification Management
- `SPECIFICATION.md` の議論・追記・修正が一段落したら、変更履歴セクションへの追記（日付・バージョン・変更内容・変更者）を必ず提案する。バージョン番号は既存最新からインクリメント。

## Database Schema Management
- スキーマ変更時のワークフロー（マイグレーション関数・`CurrentDbVersion` 増分・`sqlite3.exe` 検証・`ExpectedSchema` ↔ SPEC §7.3 同期）は **SPECIFICATION.md §7.6** を参照のこと。
- 重要： `CreateTables()` を編集したら必ず `MigrateVxToVy` を書く。スキーマ drift の温床。

## Cross-component Standards (ファイルログ)
- 新規クライアントコンポーネント（Launcher / Manager / Monitor 等）を追加・改修する際は、ファイルログ基盤を必ず実装する。仕様詳細は **SPECIFICATION.md §3.6** を参照。
- 参照実装: [`Manager/Services/Logger.cs`](Manager/Services/Logger.cs), [`Launcher/scripts/logger.gd`](Launcher/scripts/logger.gd)
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
