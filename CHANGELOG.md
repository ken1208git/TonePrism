# 変更履歴

このプロジェクト（Prismランチャーシステム）の重要な変更点を全て記録します。

このファイルの形式は [Keep a Changelog](https://keepachangelog.com/ja/1.0.0/) に従い、
このプロジェクトは [Semantic Versioning](https://semver.org/lang/ja/) に準拠しています。

**注意**: このCHANGELOGはソフトウェア本体のバージョンを追跡します。仕様書の変更履歴については、[SPECIFICATION.md](SPECIFICATION.md)の「変更履歴」セクションを参照してください。

---

## Bundle（リリース全体 / `RELEASE_VERSION`）

リリース zip 全体に付与する独立バージョン。GitHub Releases の本文として `Release.ps1` がこのセクションを抜き出して使う。エンドユーザー（来場スタッフ / 顧問の先生 / 部員）向けの **summary** を書く。技術詳細は `## Launcher` / `## Manager` / `## Release Tooling` 等の別セクションを参照。詳細仕様は [SPECIFICATION.md §3.7.7](SPECIFICATION.md) を参照。

### [Bundle v0.1.0] - 2026-05-11

初回 Bundle リリース。`Release.bat` 1 発でビルド + zip + GitHub Releases アップロードまで自動化する配布インフラを導入 (#108 Phase 1)。

- Launcher: 変更なし (v0.5.16 同梱)
- Manager: 変更なし (v0.8.9 同梱)
- Release Tooling: `Release.ps1` / `Release.bat` を新規追加 → 詳細は `## Release Tooling` セクションを参照

**Notes**: 本リリースは `Install.bat` 未同梱のため本番運用不可、Release.ps1 の動作確認用テストリリース扱い。Phase 2 以降で `Install.bat` / `Updater` / Manager UI アップデートタブ / Launcher 通知バナーを順次実装予定。

---

## Release Tooling（配布インフラ）

`Release.ps1` / `Release.bat` / `Install.bat` (Phase 2 以降) / `Updater` (Phase 3 以降) 等の配布インフラの変更履歴。エンドユーザー向けではなく、開発者が「リリーススクリプトのこの挙動はいつから？」を辿るために残す。

### [Release Tooling v0.1.9] - 2026-05-12

#### Fixed (PR #149 シニアレビュー round 4)

- **[M2] robocopy コメントが「ユーザーデータ保護の主機構」を誤って伝える bug**: 旧コメントは `/XF` `/XD` を保護の主軸として説明していたが、実際に user data (prism.db / games / backups / responses / logs) を保護しているのは **robocopy の default 非ミラー挙動** (dest 側にあって source にないファイル / ディレクトリは touch しない) であり、`/XF /XD` は「source 側に勘違いで同名ファイル / ディレクトリが混入した場合の defense-in-depth」に過ぎない。現状 source (`files/`) には保護対象名は入らないので `/XF /XD` は 実質 no-op。誤解の害: 将来「コピー効率化のため `/MIR` 追加」を検討した時に「`/XF /XD` があるから user data は守られる」と誤判断 → `/MIR` の "source にないものを削除" 挙動で user data が即削除される silent path。修正: コメントを「PRIMARY protection = default 非ミラー挙動 / `/XF /XD` = defense-in-depth / **`/MIR` 追加禁止**、必要になった場合は `/XO` + pre-copy snapshot + SPEC §3.7.3 と同期」の警告文に書き直し
- **[M3] `Read-Host` が non-interactive 環境で silent exit 3 になる bug**: Y/N upload prompt は対話前提だが、`Read-Host` は stdin redirect / 端末なし環境で空文字列を返し、`'^(y|yes)$'` regex に一致しないため exit 3 (= N 回答 skip) で終了する。CI で `-SkipUpload` 付け忘れた場合、エラーメッセージなしで Warn 行のみ出して silent skip。exit code 体系 (2 = env state, 3 = user 判断) の区別も崩れる。修正: Read-Host 前に `[Environment]::UserInteractive` + `[Console]::IsInputRedirected` で non-interactive を検出し、検出時は明示的に `Fail "non-interactive environment: -SkipUpload / -DryRun 未指定で Read-Host を呼ぼうとしました"` (exit 1) で abort、`-SkipUpload` または `-DryRun` を案内する Write-Info 付き
- **[M1] Launcher.bat の `exit` 採用理由コメントが Launcher.bat の文脈で literally false**: 旧コメントは「Install.bat が Manager を起動する経路で residual cmd window 問題が出た」と書かれており、Manager.bat の文脈では正しいが Launcher.bat 自体は Install.bat の auto-start path から呼ばれない (Install.bat に Launcher 起動経路はない) ため事実関係がそのまま当てはまらない。copy-paste 痕跡で未来の保守者が「Launcher.bat も Install.bat に呼ばれているのか」と誤解する path。修正: 「leaf shortcut (他 bat の building block ではない) として Manager.bat と同じ `exit` discipline を予防的に適用」「観測された residual cmd 問題は Manager 経路のみ、Launcher.bat 自体は forward-looking consistency choice」と書き直し、参照は Manager.bat docstring に集約

#### Changed (PR #149 シニアレビュー round 4)

- **[L1] SPEC §3.7.5 「階層変更」表現を修正**: 旧記述「Phase 2 で `GCTonePrism/Launcher.bat` から `<親>/Launcher.bat` に階層変更」は、リリース歴を辿る読者に「以前は `GCTonePrism/Launcher.bat` として配布されていた」と誤解させる。実際には `GCTonePrism/Launcher.bat` 配置は PR #149 内の中間コミット時点の設計で published version 履歴ではない。「Phase 2 で `<親>/Launcher.bat` 直下配置として新規導入、初期案では `GCTonePrism/Launcher.bat` も検討されたが published 前に確定」と整理
- **[L2] Install.bat の `set /p` プロンプト内の `(Y/N)` を `[Y/N]` に統一**: line 19 docstring rule の「echo arguments MUST NOT contain literal `(` or `)`」を echo に限定して書いていたが、`set /p` プロンプトでも `(Y/N)` を露出させていた整合性欠如。`set /p` parser も内部的に block 解釈と独立している保証は薄く、将来の cmd 挙動変化への安全マージンとして `(` `)` 禁止ルールを `echo / set /p prompt` 両方に拡張。既存の 2 箇所 (`OVERWRITE_CONFIRM` / `START_MANAGER` プロンプト) も `[Y/N]` に置換
- **[L4] exit 3 N 回答メッセージを tag conflict path と表現統一**: 旧文言「同 zip を再生成」が「ファイルが消える → 再作成」と誤読されやすかった。「ビルドキャッシュは温存され、Godot export / msbuild は再実行されない (zip のみ再生成で retry は速い)」と tag conflict graceful exit path の説明と同じ表現に揃え、技術的に正確な記述に統一
- **[L5] PR description の "Known untested" を UNC share root と sub-path で区別整理**: 旧表記は `\\学校サーバー\PCクラブ` を UNC root の例として挙げていたが、`PCクラブ` が share name か sub-path か曖昧。本当に未テストなのは share root 直接選択 (`\\server\share` 形式) のケース。`\\server\share\sub` は通常 path 扱いで edge case ではない、と区別。**ドライブルート (`C:\` 等)** も追加で Known untested に明記、leaf extraction が空文字列を返して INSTALL_TARGET= drive root + GCTonePrism で続行し、mkdir が権限不足で graceful fail する path を docstring 化

#### Added (PR #149 シニアレビュー round 4)

- **Install.bat docstring の leaf extraction edge case 注記 (L3)**: `for %%F in (...) do %%~nxF` がドライブルート (`D:\`) と UNC root (`\\server\share`) で divergent な値を返す挙動を明文化、各ケースの後段挙動 (drive root mkdir 権限失敗 / UNC share-as-folder 続行) を `:nest_check` 周辺コメントに記述
- **Install.bat docstring の structural rule (2) を `set /p` 含む形に拡張**: 「echo / set /p prompt arguments MUST NOT contain literal `(` or `)`」「even top-level placement isn't formally guaranteed by docs against future cmd version changes」と一貫性を明記

#### Fixed (PR #149 シニアレビュー round 3)

- **[M2] `:dialog_fail` 経路で PS stderr (実際の失敗理由) が捕捉されていなかった bug**: 旧実装は `> "%TEMP_DIALOG_OUT%"` で stdout のみリダイレクト、stderr は親 cmd console に直流していた。インタラクティブ実行では人間が画面で見られるが、Phase 3 Updater が `cmd /c install.bat > log.txt 2>&1` で呼ぶ運用に入ると stderr 行は log に残らない (= 自動化呼出しで診断情報が失われる) silent path。`show_folder_dialog.ps1:catch` の `[Console]::Error.WriteLine` 出力も同じ理由で消える。修正: `> stdout.tmp 2> stderr.tmp` に分離キャプチャ、`:dialog_fail` で `PS stderr の内容:` + `PS stdout の内容:` を順に表示。`:dialog_ok` / `:dialog_cancel` でも err tmp ファイルを cleanup
- **[M3] Manager 自動起動経路が SPEC §3.7.5 の「日常起動は Manager.bat 経由」規約を bypass していた**: round 2 で「Manager.bat 経由だと中間 cmd 残骸が出る」問題を「Manager.exe 直叩き」で回避していたが、SPEC で規約として明文化した「日常起動は Manager.bat 経由」を Install.bat の auto-start path だけ破る silent inconsistency。将来「Manager.bat 内で working dir 設定 / 環境変数 export / ログ転送」等を追加した時に auto-start 経路だけ漏れる path を防ぐため、規約準拠側に揃え直す。修正 2 段:
  - **Manager.bat / Launcher.bat 末尾に `exit` 追加**: 中間 cmd 残骸の根本原因 (Manager.bat の cmd プロセスが `start "" Manager.exe` 後も "次の行" を待ってアイドル) を Manager.bat 側で解消。`start` で Manager.exe を detach した後は cmd が do nothing なので、`exit` で cmd を強制終了 → 親 (Install.bat) から見ても子 cmd は即時終了 → window 残骸なし
  - **Install.bat auto-start を Manager.bat 経由に復元**: `start "" "%INSTALL_TARGET%\GCTonePrism_Manager\GCTonePrism_Manager.exe"` → `start "" "%INSTALL_PARENT_NO_TRAIL%\Manager.bat"`。SPEC §3.7.5 と整合し、Manager.bat lifecycle 変更時の漏れ path を解消
  - trade-off: `call Manager.bat` で呼ぶと caller bat も `exit` で巻き添えで終了する。Manager.bat / Launcher.bat は leaf shortcut (他 bat の building block ではない) という前提で許容、docstring に明記
- **[M4] `Resolve-TagConflict` 既存タグ検出 / Y/N の N で `exit 0` が「成功」と「skip」を区別不能だった bug**: caller (CI / 上位 script) から見ると `Release.bat` の `exit 0` が「publish 成功」「tag 衝突で skip」「N で skip」のいずれかを区別できない。CI で `.\Release.bat -SkipUpload:$false` 回しても、tag 衝突で publish せず exit 0 を返すと CI は緑になる silent path。同 PR 内で「誤 publish 防止優先」(Y/N 完全一致マッチ) と書きながら「期待通り publish できたか」を caller が exit code で検出できない設計矛盾。修正: exit code 体系を細分化:
  - `0` = success (publish 成功 / `-SkipUpload` / `-DryRun`)
  - `1` = failure (script の本来失敗: build / publish / env)
  - `2` = skip (tag conflict + `-Force` なしによる publish skip、env 起因)
  - `3` = skip (Y/N の N 回答による intentional skip)
  - Release.bat 側も 4 状態を区別表示 (`正常終了` / `publish skip [exit 2: ...]` / `publish skip [exit 3: ...]` / `[FAIL]`)、exit code はそのまま透過
- **[L1] `Release.ps1:940` のコメントで関数名が `Assert-NoTagConflict` (実体 `Resolve-TagConflict`)**: 同 PR 内で命名 divergent、Resolve-TagConflict に修正 + コメント内容も最新の "zip 完成後 / Y/N upload prompt の前に呼ぶ" 配置説明に更新

#### Changed (PR #149 シニアレビュー round 3)

- **[M1] CHANGELOG `### [Release Tooling v0.1.9]` の `#### Added` を最終形に書き換え**: 旧記述は round 1 時点の事実を残しつつ round 2 までの修正を `#### Fixed` に追記する累積型だったため、同一 version エントリ内で「`<親>\GCTonePrism\Launcher.bat` 配置」(Added) vs 「1 階層上に変更」(Fixed)、「`chcp 65001` save/restore」(Added) vs 「cp932 staging で chcp 撤去」(Fixed)、「ExpectedFiles 12 件」(Changed) vs 「13 件」(別 bullet) が並存して読みづらかった。AGENTS.md "1 PR 1 bump、レビュー対応コミットでは既存エントリの description を加筆・修正" 規約に従い、`#### Added` 側を最終形 (`<親>/` shortcut + cp932 staging + ExpectedFiles 13 件 + `show_folder_dialog.ps1` 同梱 + `exit` 終端 + Phase 3 起動規約) に書き換え、journey 自体は `#### Fixed` 各 bullet に残す形に整理
- **[L3] FolderBrowserDialog SelectedPath の trailing whitespace 防御コメント**: `:dialog_ok` の `set /p INSTALL_PARENT=<...` 直前に「.NET 4.x の SelectedPath は trailing space を付けない実装で確認済、問題化したら whitespace trim を入れること」を明記、将来の OS / .NET 変更に対する fail-safe ガイドラインを残す
- **[L4] `%TEMP_DIALOG_OUT%` / `%TEMP_DIALOG_ERR%` の衝突確率を 1/32K → 1/1G に低減**: 旧実装は `%RANDOM%` 単独 (15-bit、1/32768)、新実装は `%RANDOM%%RANDOM%` (30-bit、1/1G 弱)。インタラクティブ double-click 想定のため並列実行はまずないが、「ユーザー誤って 2 回 double click → 前回 instance がまだ dialog 待ち」のケースで tmp ファイル shared → `set /p` が前回 path を inherit する silent bug を構造的に低減

#### Added (PR #149 シニアレビュー round 3)

- **SPEC §3.7.8 チェックリスト「Updater」項目に Phase 3 Install.bat 起動規約を追加 (L5)**: 「`Process.Start("cmd", "/c install.bat ...")` 形式必須、`call` 形式 / 直接起動禁止、理由は §3.7.4」を着手前チェック項目として明示。AGENTS.md から本節へのリンク済みなので、Phase 3 着手時に自然と参照される導線が確保される

#### Fixed (PR #149 シニアレビュー round 2 + Codex P1/P2 round 2)

- **[Codex bot P1] `show_folder_dialog.ps1` setup エラーが Cancel と区別不能 → real failures が exit 0 に丸まる bug**: `Add-Type` / `New-Object` / property 代入の非終了エラー後も `if ($d.ShowDialog() -eq OK)` 行に到達し、`$d` が null だと else 分岐に流れて `exit 2` (user cancel と同じ) を返してしまう問題。Install.bat 側は exit 2 を Cancel として処理して `goto :end` (成功扱い、exit 0) するため、自動化呼出し / Phase 3 Updater から見ると「ユーザがキャンセルした」のか「PS が壊れた」のか区別不能だった。`$ErrorActionPreference = 'Stop'` で非終了エラーを終了エラーに昇格 + try/catch で Add-Type 〜 ShowDialog を囲み、catch 時は stderr にメッセージ + `[Environment]::Exit(1)`。Install.bat 側の 3-way dispatch (0/2/else) で exit 1 は自動的に `:dialog_fail` に流れるので bat 側修正不要。exit code の意味 (0=OK / 1=catch / 2=Cancel / その他=PS host 起動失敗) を冒頭コメントに追記
- **Install.bat の残骸 cmd window 問題 (Manager 自動起動 Y 経路のみ)**: 「だいたい問題なくなったけど、最後に残骸だけ残る」報告対応。原因 2 段。
  - **段 1: `exit /b %EXIT_CODE%` → `exit %EXIT_CODE%`**: ダブルクリック起動 (`cmd /c install.bat`) では `exit /b` で caller cmd が追従終了するはずだが、Windows Terminal 等一部の terminal host で空 prompt が残るケースがあった。`exit` (without `/b`) で cmd プロセスを直接終了させ、window を統一的に閉じる。トレードオフ: 将来 `call install.bat` を書くと caller bat も巻き添えで終了するため、SPEC §3.7.4 Updater 仕様に「`Process.Start("cmd","/c install.bat ...")` 経由で呼ぶこと」を明記 (シニアレビュー L3)
  - **段 2: `start "" "Manager.bat"` → `start "" "Manager.exe"` 直叩き**: 段 1 でも Y 経路のみ window 残存が継続。Manager.bat は単に `start "" "...Manager.exe"` する 2 行 wrapper のため、Install.bat から `start "" Manager.bat` すると中間 cmd プロセスが一瞬発生 → Windows Terminal がそれを子プロセスとみなして Install.bat の window を「終了待ち」状態に保持していた。直接 Manager.exe を `start ""` することで中間 cmd を排除し、Install.bat 終了後に親 window がクリーンに閉じる。Manager.bat 自体は `<親>/` 直下の日常起動用として残り、機能影響なし
- **INSTALL_README.txt 冒頭文言の組織名誤読対策**: 旧「ゲームセンターTONE 開発の Prism ランチャーシステム」は「ゲームセンターTONE という会社/組織が開発した」と読めてしまっていた。ゲームセンターTONE は文化祭企画の名前なので、「文化祭企画『ゲームセンターTONE』の Prism ランチャーシステム」に修正

#### Changed (PR #149 シニアレビュー round 2)

- **[M1] 1 PR 1 bump 規約に整合**: SPEC 変更履歴の v1.10.9 (ショートカット bat 1 階層上 relocation) を v1.10.8 (Phase 2 元仕様) に統合。両方とも同じ「Phase 2 / #108」スコープで breaking change でも別関心事でもないため、AGENTS.md "CHANGELOG Section Roles" の規約 (1 PR 1 bump、レビュー対応コミットでは既存エントリの description を加筆) に揃える
- **[M2] Y/N 判定の寛容度を Release.ps1 と統一**: Install.bat の `OVERWRITE_CONFIRM` / `START_MANAGER` は `if /i "%X%"=="Y"` で **Y/y のみ受理** だったため、Release.ps1 の Y/N upload prompt (`y/yes` 両受理) を触っているユーザーが「yes」と打って意図せず abort する path があった。Install.bat の 2 箇所も `Y` / `YES` 両方を受理する形に拡張、INSTALL_README にも「Y / y / yes」明示
- **[M3] `Resolve-TagConflict` の network 失敗 path にも zip path 案内を追加**: 既存タグ + `-Force` なし時は graceful exit + zip path 案内だったが、gh release view の auth/network 失敗時は `Fail` で exit 1 のみで zip path を案内せず「ビルド完了 → ネットワーク一時障害 → Fail → ユーザー: zip が捨てられたと誤解」する path があった。Fail 直前に `Write-Info "zip は $ZipPath に残っています"` を追加、graceful exit path と同じ "publish 失敗とは独立して zip は流用可" メッセージで統一
- **[M4] Install.bat 最終 pause を日本語化**: 周囲が全て日本語 UX なのに cmd default の "Press any key to continue . . ." だけ英語だった一貫性破れ。`pause >nul` + `echo  何かキーを押して終了します...` の 2 行に置換、文言を制御
- **[L1] set quoted/unquoted の規約コメント追記**: line 44-46 で「path-derived は quoted set 必須」と宣言しつつ numeric sentinels (`MANAGER_FOUND` / `LAUNCHER_FOUND` / `MANAGER_STARTED` / `EXIT_CODE`) は unquoted という不整合が将来 maintainer を混乱させる懸念。「quote rule applies to path / user-input values, not to numeric flags」とコメントで明文化、混在の合理化を行った
- **[L2] PR #149 description の検証ログを最新化**: ExpectedFiles 12 → **13** (`show_folder_dialog.ps1` 追加で +1)、staging 構造の記述も最新の `<親>/` ショートカット bat 配置に同期、検証 Stage 3 の "deferred" を実機 E2E 完了済みにマーク
- **[L3] SPEC §3.7.4 Updater に Install.bat 起動方式を明記**: `Install.bat` 終端の `exit` (not `exit /b`) は `call install.bat` で呼ぶと caller bat も巻き添えで終了する silent danger があるため、Phase 3 Updater は `Process.Start("cmd", "/c install.bat ...")` 形式で呼ぶこと、を SPEC 側にも明記。bat 末尾の REM コメントだけだと埋もれる懸念があり、SPEC の Updater 責務節に格上げ
- **[L4] `:wait_close` の unbounded loop を docstring に明記**: Manager.exe / Launcher.exe を user が close できない場合 (hung process / 別 session / 権限不足) Ctrl+C しか退出手段がない設計を **意図** として docstring 化。Phase 2 の対象 (人がダブルクリック) では「インストーラが固まっている、なぜ?」と気付かせる方が auto-fail で原因を隠すより良いという根拠と、Phase 3 Updater が forced termination の責務を持つ役割分担を明記。将来 unattended re-install フローで問題化したら max-iterations 追加の hint も併記
- **[L5] INSTALL_README に shortcut bat 上書き注意を追加**: 再 install (上書き Y) で `copy /Y` により `<親>/Launcher.bat` / `<親>/Manager.bat` が無条件上書きされる挙動を README の「緊急アップデート」項目に明記。ゲームデータ保護リストと並べて wrapper bat も列挙し、カスタマイズ消失の path をユーザに事前認知させる

#### Fixed (PR #149 シニアレビュー round 1 + Codex P2)

- **[Codex P2] FolderBrowserDialog 選択パス内の `!` 文字が delayed expansion で破壊される bug**: `Install.bat` 全体で `setlocal enabledelayedexpansion` を有効化していたため、選択パスに `!` が含まれる場合 (例: `D:\Backup!\` 等のユーザー命名パス) に `for /f ... do set INSTALL_PARENT=%%P` の段階で `!` が delayed expansion token として解釈され削除される。本ファイル refactor 後は `!VAR!` 参照が実質ゼロのため、`setlocal disabledelayedexpansion` に変更して構造的に解消
- **PR #149 Codex bot review 対応 (P2 #4 #5 #6)**:
  - **P2 #4**: `INSTALL_PARENT` が caller env から inherit して `set /p` が input なしの場合に stale 値で続行する path を解消。`set "INSTALL_PARENT="` を dialog 起動 *前* に追加して initialize
  - **P2 #5**: `set SCRIPT_DIR=%~dp0` / `set FILES_DIR=...` の unquoted 形式は zip 展開 path に `&` 等の cmd metachar (例: `D:\R&D\`) が含まれると line split で abort する path だった → `set "VAR=value"` quoted 形式に統一
  - **P2 #6**: `echo %INSTALL_TARGET%` 等 user-controlled path を出力する 7 箇所の echo を `echo "%VAR%"` 形式に変更。`%VAR%` 展開後の path に `&` 含むと cmd の multi-command split で異常出力 / fragment 実行になる path を解消。引用符付きの出力 (`インストール先: "D:\Games\GCTonePrism"`) になるが、edge case 防御として allowed
  - **過去 review で既に解消**: P1 #1 (overwrite-process delayed expansion) は top-level goto refactor 済、P2 #2 (path 内 `!`) は disabledelayedexpansion 済、P2 #3 (PS 失敗 vs Cancel) は 3-way dispatch 済
- **Install.bat の cmd parse cascade 問題を根本解決: staging encoding を cp932 (Shift-JIS) に変換**: ユーザー実機テストで何度試しても解消しなかった「`'�に'` `'em.Windows.Forms'` `'��データは維持されます:'` 等の 'is not recognized' 連鎖エラー + Manager 起動後の黒画面残存」の根本原因が判明: **`chcp 65001` は console output codepage のみ切り替え、cmd の bat ファイル parser は **システム codepage** (JP Windows = cp932) で読み続ける** 仕様。UTF-8 で書かれた長文 Japanese echo (`Manager が壊れて起動できない / クリーンインストールしたい場合のみ Y を押してください。` 等 30+ chars) の byte 境界を parser が mis-tokenize → cascade。修正:
  - **Release.ps1 Copy-Templates が .bat staging 時に UTF-8 → cp932 変換**: cmd の system codepage と一致するので parser が natively 読める。長文 Japanese 行も安全に処理。デメリットの非 JP Windows での mojibake は JP 校内向け配布なので OK
  - **Install.bat から `chcp 65001` ロジック撤去**: cp932 file ↔ cp932 system で一致するため不要。同時に「ASCII boundary rule」も削除 (codepage 切替がないので boundary 自体存在しない)
  - **Install.bat docstring の構造規約 (1)(2)(3) は維持**: cmd parser の `(...)` block / literal `(` / inline 日本語の各 quirk は cp932 でも残るので、引き続き必要
  - 副次効果: Manager 自動起動後の黒画面残存も解消 (parser cascade で `start` の後始末が狂っていたのが root cause だった)
- **ショートカット bat (`Launcher.bat` / `Manager.bat`) の配置を 1 階層上に変更** (ユーザー UX 要望):
  - 旧: `<親>/GCTonePrism/Launcher.bat` (部員が `GCTonePrism/` サブフォルダに入る必要があった)
  - 新: `<親>/Launcher.bat` (`<親>` を開けば即ダブルクリック可能)
  - インストール後の見た目: `<親>/Launcher.bat` + `<親>/Manager.bat` + `<親>/GCTonePrism/` が並ぶ
  - zip 内構造: `files/Launcher.bat` → zip ルートの `Launcher.bat` に移動 (`files/` は component 本体専用に)
  - Install.bat: `:copy_shortcuts` label を追加し、files/ → `<親>/GCTonePrism/` の robocopy 後に zip ルートの `Launcher.bat` / `Manager.bat` を `copy /Y` で `<親>/` に配置
  - Launcher.bat / Manager.bat 内 path: `%~dp0GCTonePrism_Launcher\...` → `%~dp0GCTonePrism\GCTonePrism_Launcher\...` (1 階層分の親パス追加)
  - Manager 起動 Y/N の `start` path: `%INSTALL_TARGET%\Manager.bat` → `%INSTALL_PARENT_NO_TRAIL%\Manager.bat` (`<親>/Manager.bat`)
  - Release.ps1 Copy-Templates: `filesTemplates` を空配列に、`rootTemplates` に Launcher.bat / Manager.bat を追加
  - Assert-ExpectedFiles: `files\Launcher.bat` / `files\Manager.bat` → zip ルートの `Launcher.bat` / `Manager.bat`
  - INSTALL_README.txt: 日常使い path 例 (`D:\Games\Launcher.bat`) に更新
  - SPEC §3.7.1 (zip 構造 + インストール後構造) + §3.7.5 (Launcher 日常起動 path) を更新、変更履歴は v1.10.8 entry に統合 (1 PR 1 bump 規約、シニアレビュー M1)
- **Install.bat の cmd parser 問題を構造的に解消 (`.ps1` 切り出し + `[...]` 使用)**: 数回の試行錯誤を経て、根本原因は (a) 長い `set "PS_DIALOG_CMD=...日本語..."` の Japanese byte 列が cmd の line tokenize を壊し PS に malformed command を渡す、(b) `set "..."` 内の `^|` が literal で残り PS に届く (cmd quoted set では `^` 非 escape)、(c) `echo (text)` の `(` が cmd で block 開始と解釈される、と判明。これらを構造的に回避:
  - **PS dialog code を `templates/show_folder_dialog.ps1` に切り出し**: cmd parsing を経由せず PS native の UTF-8 処理に任せる。Japanese description (`'GCTonePrism のインストール先の親フォルダを選択してください'`) を維持可能。Install.bat 側は `powershell.exe -File "%~dp0show_folder_dialog.ps1"` で起動するだけ
  - **echo の `(` `)` を `[` `]` に置換 (14 箇所)**: `[` `]` は cmd で escape 不要、`^(` `^)` の top-level 不安定挙動を回避
  - **Release.ps1 Copy-Templates**: .ps1 を **UTF-8 BOM + CRLF** で staging に書き出し (PS 5.1 default の ASCII 読み込みで Japanese mojibake を防ぐため BOM 必須)
  - **Assert-ExpectedFiles**: 12 → 13 件 (`show_folder_dialog.ps1` 追加)
  - **Install.bat docstring の構造規約 (3) 新規追記**: 「PS 起動の引数で日本語を含む長い文字列を inline しない、別 .ps1 に切り出して `-File` 経由で起動する」を明文化、将来 maintainer が誤って inline に戻すのを防ぐ
- **Install.bat 実行時に「'em.Windows.Forms' is not recognized」等 cmd parse error が連発する bug**: ユーザー実機の testing で、Install.bat が `[FAIL] PowerShell が exit 0 を返しましたが選択パスが取得できませんでした` と並んで `'��よう' is not recognized` `'em.Windows.Forms' is not recognized` `'場合は' is not recognized` 等の cmd error が散発した。原因: cmd は `if cond (echo 日本語... goto :fail)` のような `(...)` ブロックを parse-time に展開する際、UTF-8 で書かれた日本語の byte 列を mis-tokenize して fragment を command として実行する動作。chcp 65001 が success していても、cmd の bat parser はブロック展開時に system codepage (cp932 on JP Windows) で byte を解釈する余地があり、Japanese 含む `(...)` block が壊れる。welcome 等 top-level の echo は正常動作するが、`if (...) echo ... goto :fail` パターンは全滅。
  - 修正: 全 `(...)` ブロック内の Japanese echo を **top-level goto pattern** に refactor。`if cond (echo X / goto Y)` → `if not cond goto :ok / [...echo X...] / goto :fail / :ok / [...continue...]` の linear flow に統一
  - 既存 refactor (Codex P1 で existing_install branch のみ実施) を全 block (files/ 不在 / dialog_ok 防御 / dialog_fail / 入れ子検知 / overwrite cancel / robocopy fail × 2 / mkdir fail / Manager 起動) に展開
  - docstring に「日本語 echo は (...) ブロック内に置かない」規約を構造規約として明文化
  - 副次効果: 一部 echo の `^(` `^)` エスケープが不要になった (ブロック外なので) → 可読性向上
- **Install.bat ダブルクリックで一瞬だけ開いて即閉じる bug (LF/CRLF 不一致)**: cmd.exe は LF-only bat を正しく parse できず double-click で即終了する (PR #140 の Release.bat 同種事故、本 Phase 2 で Install.bat に再発)。原因: `.gitattributes` の `*.bat eol=crlf` は git checkout 時に working tree を CRLF 化するが、Write tool 経由の編集で working tree に LF が残っており、Release.ps1 の `Copy-Item` がそのまま staging へ LF コピーしていた。zip の Install.bat は LF だったため double-click で開いた瞬間に cmd.exe parse 失敗 → 即 close。修正: `Copy-Templates` 内で `$src -match '\.bat$'` の場合に `ReadAllText` → `\r\n` 正規化 → `WriteAllText` で強制 CRLF 化。working tree の改行状態に依存せず確実に CRLF zip を出力できるように
- **[Codex P2 round 2] PowerShell 起動失敗とユーザー Cancel が exit code で区別不能 → real installer failures が exit 0 に丸まる bug**: 旧 `for /f ... do set INSTALL_PARENT=%%P` + `if not defined INSTALL_PARENT` チェックは「PS 起動失敗 (PS 未 install / AppLocker block / PS_DIALOG_CMD syntax error 等)」と「ユーザー Cancel」の両方で `INSTALL_PARENT` undef → cancel 扱い (exit 0) になる。Phase 3 で Updater が Install.bat を invoke する場合、real failures を成功扱いしてしまう実害ある silent failure path。修正:
  - PS 終了コードを 3 値で意味付け: `0` = OK + path 出力 / `2` = ユーザー Cancel (旧 `1` → `2` に変更、PS 内部 error の exit 1 と区別) / その他 = PS 実行失敗
  - 出力を `%TEMP%\gctone_install_dialog_<RANDOM>.tmp` 経由で受け取り、bat 側で `ERRORLEVEL` を確実に捕捉 (`for /f` の弱い exit code 伝播を回避)
  - 3-way dispatch: `:dialog_ok` (続行) / `:dialog_cancel` (`goto :end`、exit 0) / `:dialog_fail` (`goto :fail`、exit 1 + Execution Policy 確認手順案内 + PS stdout 内容表示)
  - PS one-liner も `[Console]::Out.WriteLine` → `[Console]::Out.Write` に変更 (末尾 CRLF を抑止、`set /p` の cmd.exe 版差での CR 残留 trap を回避)
- **[M2] Install.bat の失敗 path も `exit /b 0` で caller から成否区別不能**: 4 つの失敗経路 (files/ 不在 / 入れ子検知 / robocopy 失敗 / mkdir 失敗) がすべて `:end` に合流して `exit /b 0`。Phase 3 で Updater から Install.bat を呼び出す場合に exit code でエラー判定できない。`:fail` label を新設 + `EXIT_CODE` sentinel を導入、失敗 path は `goto :fail` で 1、成功 / ユーザーキャンセル (folder dialog cancel / 上書き N) は 0 を返す形に
- **[M3] docstring の ASCII 規約と実装の乖離**: 「chcp 65001 より上の REM / echo は ASCII 必須」と書きながら、自身の docstring REM 行が日本語だった。`@echo off` 下で REM は表示されないので mojibake は echo にしか発生しないのが実態。docstring を「echo は ASCII 必須、REM は日本語 OK」に修正
- **[M4] PS one-liner の多行出力に対する防御**: `Write-Output $d.SelectedPath` だけだと将来 Add-Type warning が stdout に出るケース等で for /f の最終行が pollute される可能性。`[Console]::Out.WriteLine + [Environment]::Exit(0)` で明示的に 1 行のみ書き出し + 後続出力を封じる形に変更

#### Changed (PR #149 シニアレビュー round 1)

- **[M1] `:wait_close` 文言を `pause` の挙動に整合**: 「Enter キーを押してください」→「何かキーを押してください」(`pause` は ReadKey ベースで任意キーで進むため、文言通り Enter を期待するユーザーには直感に反していた)
- **[L1] `robocopy /XD` 名前マッチの副作用を明記**: `/XF /XD` はツリー全体で同名 file/dir を除外するため、将来 component 内に `Companions/logs/` 等の同名 dir が登場すると意図せず除外される。現状 files/ には保護対象名と衝突する物がないので実害なし、コメントで明記
- **[L2] Manager 起動 path で Install.bat の `pause` を抑止**: Manager.bat 起動 → Manager UI 表示中に Install.bat の「何かキーを押してください」が同時表示される UX 退行を回避、`MANAGER_STARTED=1` sentinel で `:end` の pause を skip
- **[L4] `set /p` 空 Enter の「前回値保持」事故を防ぐ事前初期化**: `set "OVERWRITE_CONFIRM="` / `set "START_MANAGER="` を `set /p` 直前に追加。現状の variable chain では発火経路なしだが、将来上流で同名変数が定義される変更が入った時の silent break を防ぐ
- **[L5] `Release.ps1` の Y/N 判定を厳格マッチ化**: 旧 `-inotmatch '^y'` は先頭 y 一致のみで `yikes` / `yo` / 「YES (確認)」末尾括弧等の typo でも公開が走る。`-imatch '^(y|yes)$'` の完全一致に変更、prompt 文言も「y/yes/n/no で回答」に明示。誤判定で abort → 再実行する方が誤 publish より低コスト (GitHub Releases publish は巻き戻し不可)

#### Acknowledged (PR #149 L3 scope creep)

- 本 PR は (a) Install.bat 等 Phase 2 本体 (b) Release.ps1 Y/N upload prompt 追加 (c) Release Tooling v1.0.x → v0.1.x rename の 3 つの関心事が混在している scope creep の指摘 (PR review L3)。次回類似シナリオでは分割を検討、本 PR ではすでに review が進行しているため merge 完了させる方針

#### Fixed (PR #149 Codex P1)

- **`Install.bat` の parenthesized block 内で `%VAR%` parse-time 展開 bug**: 既存検出 branch の `MANAGER_RUNNING` / `LAUNCHER_RUNNING` は `if exist (...)` ブロック内で `set` していたが、続く `if %VAR% EQU 0` の比較が parse-time に展開される (cmd.exe 仕様)。これにより stale/empty 値で `EQU was unexpected` の parse error が発生し、**GCTonePrism が既存の場合の overwrite フロー全体が壊れる** P1 bug だった。さらにラベル `:checkprocess` / `:wait_close` / `:do_overwrite` が `()` ブロック内に置かれており `goto` の動作も不安定。両 issue を構造 refactor で解消:
  - `if exist () (...) else (...)` を **`goto :existing_install` / `goto :new_install` の top-level 分岐** に書き換え、すべての label を `()` 外に移動
  - `tasklist | findstr` の結果取得を top-level に置き、`%ERRORLEVEL%` が run-time に正しく評価される構造に
  - 変数名も `MANAGER_RUNNING` → `MANAGER_FOUND` に変更 (findstr exit 0 = 発見 = 稼働中、の意味を明示)

#### Changed (upload prompt + タグ衝突チェック位置変更)

- **`Release.bat` / `Release.ps1`: zip 化後の `Y/N` 公開確認 prompt 追加**: user 提案、Install.bat 等の動作確認ワークフローを安全に作るため。新フロー: ビルド → zip 化 → 「GitHub Releases に v$Version を公開しますか？ (Y/N)」prompt → Y で `gh release create`、N で「zip だけ残して終了」。これにより:
  - **本番 release publish を毎回明示的に同意する形に変更**、誤 publish のリスク削減
  - zip だけ作って別環境に持ち出して Install.bat 動作確認、という運用が default で可能 (`-SkipUpload` を毎回付け忘れる必要なし)
  - `-SkipUpload` は引き続き有効 (prompt 自体を skip、CI / non-interactive 運用向け)
  - prompt の Read-Host 入力が `y` / `yes` 完全一致 (大小文字不問) の場合のみ公開、それ以外は abort (round 1 L5 で typo 寛容を厳格化)
- docstring の `gh release create でアップロード（-SkipUpload で抑止）` → `zip 完成後、Y/N 確認プロンプト → Y なら gh release create、N なら zip だけ残して終了` に更新
- **タグ衝突チェックを `Assert-Preflight` → `Resolve-TagConflict` (zip 後 / Y/N 前) に移動**: 段階的に 3 つの設計を経た最終形。
  - 旧 (v0.1.7 以前): preflight でタグ衝突を確認 → 既存なら build 前に即 fail。「Install.bat 検証用に zip だけ欲しい」シナリオで毎回 `-SkipUpload` を付け忘れる問題があった
  - 中間 (v0.1.9 途中): build → zip → Y/N prompt → Y 確定後にタグ衝突チェック → 既存なら Fail。「publish 不可なのに Y を聞いて、Y 押させた後に fail」というミスリードな順序
  - 最終 (v0.1.9): build → zip → **タグ衝突チェック** → Y/N prompt。publish 可能な状態 (衝突なし or `-Force`) に絞ってから Y/N を聞く。
  - 既存 + `-Force` なし時の挙動: 旧 `Fail` (exit 1 + 赤字 FAIL) ではなく `Write-Warn` + 復旧手順案内 + `exit 0` で graceful exit。「zip 生成は成功している」「publish 不可は env 状態であって script の失敗ではない」「caller (CI 等) も exit 0 を正常扱いで判定可能」のため。
  - preflight は env 系 (gh auth / CHANGELOG / working tree) のみに役割を絞り、「事前 fail-fast」と「build 投資後の状態確認」の責務を分離。

#### Changed (versioning scheme)

- **Release Tooling の version numbering を v1.0.x → v0.1.x に renumber**: 初版 (Phase 1) 時に v1.0.0 開始としたが、他コンポーネント (Launcher v0.5.x / Manager v0.8.x / Bundle v0.1.0) が全て pre-1.0 dev mode の中で Release Tooling だけ v1.x になっていた。さらに毎 PR で構造変更を繰り返している現実 (本 entry までに 9 patch、結構な breaking 含む) と SemVer v1.0 = stable の意味が乖離していた。v1.0.x には git tag / GitHub Release が一切存在せず外部参照ゼロのため、CHANGELOG / Release.ps1 inline コメント / AGENTS.md の歴史言及をすべて v0.1.x に retroactive rename。今後の patch も v0.1.10, v0.1.11 ... と続く。Bundle が 1.0 に到達した時点で各 component と合わせて Release Tooling も v1.0 を検討する想定

#### Added

注: 本セクションは PR #149 最終状態の事実のみを記述する。レビュー round 1/2/3 で複数回書き換わった項目 (cp932 staging / `<親>/` shortcut 配置 / ExpectedFiles 件数 / `show_folder_dialog.ps1` 切り出し 等) は、その変遷理由を `#### Fixed` 各 bullet で記録している (シニアレビュー round 3 M1)。

- **`templates/Install.bat`** — 初回インストーラ実装 (#108 Phase 2)
  - PowerShell `FolderBrowserDialog` で親フォルダを GUI 選択 (`set /p` 経由のパス入力 typo を回避、dialog は `show_folder_dialog.ps1` に切り出し `-File` 起動)
  - 入れ子検知: 親パス末尾が `GCTonePrism` なら警告 + abort (二重入れ子 `<親>\GCTonePrism\GCTonePrism\` 防止)
  - 既存検知: `<親>\GCTonePrism\` ディレクトリ存在で判定 (metadata file 不要、単純化)
    - Y/N 警告メッセージで「アップデートは Manager UI から推奨 / Manager 壊れた / クリーンインストール時のみ Y / ゲームデータ (prism.db / games / backups / responses / logs) は維持」を明示
    - Y → Manager.exe / Launcher.exe 稼働中チェック (`tasklist`) → 稼働中なら「閉じてから Enter」表示で手動 close 待機 (自動 kill しない、§3.7.4 Updater の責務に残す) → robocopy で保護データ除外 (`/XF prism.db /XD games backups responses logs`) しつつ上書き
    - N → abort
  - 新規時: ディレクトリ作成 + robocopy で `files/*` 全コピー、加えて `<親>/Launcher.bat` / `<親>/Manager.bat` (parent-level shortcut) を `copy /Y` で配置
  - 完了後 Manager 起動 Y/N プロンプト → Y なら **`<親>/Manager.bat` 経由で Manager.exe 起動** (旧設計の自動起動は廃止、SPEC §3.7.5 の Manager.bat 経由規約と整合)
  - file format: 編集ソースは UTF-8 (no BOM) + CRLF、staging 時 Release.ps1 Copy-Templates が **cp932 (Shift-JIS) + CRLF** に変換 (cmd の bat parser が system codepage で読む仕様に合わせる、JP Windows 校内向け配布スコープのため非 JP locale で mojibake になる trade-off は許容、詳細は `templates/Install.bat` 冒頭 docstring 参照)
  - 終端 `exit %EXIT_CODE%` (not `exit /b`) で cmd プロセスを直接終了させ、ダブルクリック起動時の window が確実に閉じるように。Phase 3 Updater 呼出し時は `Process.Start("cmd", "/c install.bat ...")` 形式必須 (SPEC §3.7.4)
  - exit code: `0` = success / cancel、`1` = failure (files/ 不在 / 入れ子検知 / robocopy 失敗 / mkdir 失敗 / PS 失敗) — Phase 3 Updater integration 用
- **`templates/show_folder_dialog.ps1`** — FolderBrowserDialog launcher (Install.bat の `-File` 経由で起動)
  - Install.bat 内 `-Command "..."` インラインだと cmd parser が長い `set "PS_DIALOG_CMD=...日本語..."` を破壊するため別ファイル化、Japanese description を維持可能
  - `$ErrorActionPreference = 'Stop'` + try/catch で setup / launch 失敗を `exit 1` で区別
  - exit code 3-way dispatch: `0` = OK + stdout に選択 path / `1` = catch (stderr に詳細) / `2` = user Cancel / その他 = PS host 起動失敗
  - file format: UTF-8 BOM + CRLF (PS 5.1 default ASCII 読み込みでの Japanese mojibake 防止)
- **`templates/INSTALL_README.txt`** — 配布 zip 同梱の部員向け手順書
  - インストール手順 (zip 展開 → Install.bat → folder dialog → Y/N → Manager 起動 Y/N)
  - 日常起動方法 (`<親>/Launcher.bat` / `<親>/Manager.bat` を `<親>` 直下から直接ダブルクリック)
  - アップデート方法 (通常: Manager UI、緊急: Install.bat 上書き、ゲームデータ保護リスト明記)
  - トラブルシューティング 4 項目
- **`templates/Launcher.bat` / `templates/Manager.bat`** — 部員日常起動用 **parent-level shortcut** (`<親>/` 直下配置)
  - 内容: `start "" "%~dp0GCTonePrism\GCTonePrism_<comp>\GCTonePrism_<comp>.exe"` (1 階層下の component exe を起動)
  - インストール後の最終構造: `<親>/Launcher.bat`、`<親>/Manager.bat`、`<親>/GCTonePrism/` が `<親>` 直下に並ぶ。部員は `<親>` を開けば即ダブルクリックで起動可能、`GCTonePrism/GCTonePrism_<comp>/` まで辿る煩雑さを排除 (SPEC §3.7.1 / §3.7.5)

#### Changed

- **`Release.ps1` Copy-Templates 拡張**: 旧仕様の Install.bat / INSTALL_README.txt の 2 件 WARN を廃止、**5 ファイル** (zip root × 5: Install.bat / INSTALL_README.txt / show_folder_dialog.ps1 / Launcher.bat / Manager.bat) の正規 staging 配置に。`.bat` は cp932 + CRLF、`.ps1` は UTF-8 BOM + CRLF に staging 時変換 (それぞれ cmd parser / PS 5.1 のデフォルト読み込み挙動に合わせる)。テンプレート不在時は `Fail` (旧 WARN だったが Phase 2 完成後はテンプレート存在が必須前提のため厳密化)
- **`Release.ps1` Assert-ExpectedFiles 拡張**: 期待ファイル一覧を 8 → **13 件** に (zip ルート 5 + files/ 配下 8)。SPEC §3.7.1 正規 zip 構造との対応を明示
- **`SPECIFICATION.md` §3.7.1 / §3.7.2 / §3.7.4 / §3.7.5 更新** (変更履歴 v1.10.8):
  - §3.7.1 正規 zip 構造に zip-root レベルの `Launcher.bat` / `Manager.bat` (parent-level shortcut) + `show_folder_dialog.ps1` を追加、ルートショートカット規約の理由 (部員日常使いの煩雑さ解消、`<親>` 直下から直接起動) を明文化
  - §3.7.2 を Approach C 仕様に再定義 (FolderBrowserDialog / 既存検知 Y/N / Manager UI 推奨 / 保護データ温存 / Manager 起動 Y/N)、旧仕様の自動起動 + `GCTonePrism_Manager` 配下チェックを廃止
  - §3.7.4 Updater 仕様に「Install.bat は `Process.Start("cmd", "/c install.bat ...")` 形式で呼ぶこと、`call` 形式 / 直接起動は禁止」を明記 (Install.bat 終端 `exit` の caller 巻き込み trade-off に対する制約)
  - §3.7.5 Launcher 側の役割に「日常起動は `<親>/Launcher.bat` から」の規約を明記

### [Release Tooling v0.1.8] - 2026-05-12

#### Fixed

- **msbuild 後の日本語 doubled rendering (Wave 2)**: 症状例 `完了` → `完完了了`、ASCII は影響なし、複数 Write-Host が 1 行に collapse。PS 5.1 + chcp 65001 環境で子プロセス (msbuild) がコンソールハンドル継承後にコンソール encoding を OEM 系に戻して去ることが原因の既知バグ。修正:
  - script 冒頭で `[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)` + `$OutputEncoding` を明示ピン留め
  - `Invoke-ExternalProcess` の finally ブロックで再ピン留め (二重防御、msbuild 等の毎呼び出し後)
  - 同じ try/finally で Process オブジェクトの Dispose() も追加 (handle leak 防止)
- **upload progress が表示されない問題 (Wave 2)**: `gh release create` は TTY 検出で進捗描画を OFF にするため、PS から呼ぶと upload 中ずっと無音 → ハング懸念。修正:
  - `Invoke-NativeWithCapture` に `-ShowProgress` / `-ProgressMessage` パラメータを追加 (500ms 間隔で経過秒数を `\r` 上書き表示)
  - `Invoke-GhRelease` (gh release delete / create) を PASS_THROUGH → helper + ShowProgress に移行、zip サイズも表示 (`アップロード中 (zip 59 MB)... 12s 経過`)
  - 完了後は progress 行を消して、gh が stdout に出す release URL を `Write-Info` で再表示
- **PR #148 round 17 シニアレビュー反映**:
  - **[R17 #2] UTF8Encoding 一意ソース統一**: `Invoke-GhRelease` の `WriteAllText` (`tmpNotes` 書き込み) と `Write-FileUtf8NoBom` helper が `[System.Text.UTF8Encoding]::new($false)` を新規生成しており、本 PR の「`$script:Utf8NoBomEncoding` を一意ソースとして共有」設計に違反していた。両箇所を `$script:Utf8NoBomEncoding` 参照に置換、UTF-8 encoding instance を script 内で完全に一意化
  - **[R17 #3] `$OutputEncoding` 再ピン留め不要の理由を明文化**: `Invoke-ExternalProcess` finally で `[Console]::OutputEncoding` だけ再ピン留めし `$OutputEncoding` (PS pipeline 側 encoding) は触らない理由 (PS 変数なので子プロセスから変更不可) をコメント追記、保守者が「漏れか?」と判断して不要な再ピン留めを追加する誤修正を防止
  - **[R17 #4] `Invoke-NativeWithCapture` docstring 注 1 を vswhere `-utf8` 対応反映**: 旧記述「vswhere は ASCII 中心」は同 PR 内で追加した `-utf8` 化と乖離。「vswhere はデフォルト system codepage 出力なので、本 helper 経由の call site では `-utf8` を必ず付与」に更新、日本語 VS install path も正しく decode できる旨を明示
  - **[R17 #5] ShowProgress を `IsOutputRedirected` で抑止**: 非 TTY (CI / log file redirect) では `\r` が行リセットとして機能せず、progress 更新ごとに改行付きで log に展開されてノイズになる。`[Console]::IsOutputRedirected` (取得失敗時は redirected 扱いで safe-side) で検出して live progress を skip、redirected 環境では従来の gh TTY-off モードと同じく完了まで無音 → URL 表示の動作に degrade
  - **[R17 #1 (false positive)]** PR body Commits 数: reviewer は "(7)" と認識していたが、round 16 push 後の `gh pr edit` で既に "(8)" に更新済み、stale view を見ていた可能性。本 round では body 再更新 (commit 8 を含む) のみ
- **PR #148 round 16 シニアレビュー反映**:
  - **[R16 #1] vswhere 失敗時の `$vsInstall` クリア**: round 15 で `Write-Info "PATH fallback に切替"` と宣言したのに、続く `$vsInstall = $vswhereResult.StdOut.Trim()` が無条件実行で「失敗時 stdout の部分出力」も後段 `Test-Path` 経路に流れる構造矛盾。`if/else` に分けて失敗 branch で `$vsInstall = ''` を明示、最終的に `Test-Path` で実害は止まるが、宣言と実装の意図不一致を解消
  - **[R16 #2] `WaitForExit()` の両 path 役割を明示**: 旧コメント「.NET 慣習との表面的対称性のみ」は非 ShowProgress 経路で誤誘導 (実際にはここがプロセス完了を明示待機する唯一のポイント)。「ShowProgress 経路 = no-op / 非 ShowProgress 経路 = 必須」と path 毎の役割を明記、削除不可の根拠を両 path で示す
  - **[R16 #3] `clearLine` 全角 cell 幅の注意書きを正確化**: 旧コメント「Japanese 文字 cell 幅によらず消し残しが出ない」は実装より強い保証に読める。実態は「現 ProgressMessage 長では WindowWidth-1 幅で覆える前提に依存」「全角 2 cell 幅を構造的に補正していない」を明文化、長文 ProgressMessage 追加時の修正 hint も併記
  - **[R16 #4] CHANGELOG R13 ナンバリング順を整理**: R13 L1 (保留) を L4 の後から L2 の前 (= L1/L2/L3/L4 の自然順) に移動、後追い時のスキャンが直感的に
- **PR #148 round 15 シニアレビュー反映**:
  - **[R15 M1] 死参照訂正**: `Invoke-NativeWithCapture` のセクションヘッダコメント `(冒頭の \`2>&1\` trap セクション参照)` が v0.1.8 のセクション再構成で stale に。「冒頭の『Native command 呼び出しの方針』セクション参照」に修正
  - **[R15 M2] WindowWidth fallback コメントと実装の divergence 解消**: コメント「0 以下を返す」と実装 `-lt 30` の食い違いを明文化。「30 未満」は WindowWidth=0 の非 console host と極端に狭い実コンソールの両方を catch する threshold である旨を明示
  - **[R15 M3] vswhere の silent pass 解消**: コメントが「実行環境破損で stderr 出力する可能性」を挙げて Invoke-NativeWithCapture に移行していたにも関わらず ExitCode / StdErr を完全に無視していた silent pass を修正。vswhere 失敗時は `Write-Warn` で診断情報を出して PATH fallback に切替 (release は続行可能なため Fail にはしない)
  - **[R15 L1] Combined 4 case コメント精度向上**: 第 4 case「stderr 空 → stdout のみ」は stdout 末尾改行なしのとき不正確 (実は `\n` が付く)。stdout 末尾改行の有無を主軸にした 2 分岐構造に書き換え、各分岐で stderr の各 case を入れ子で記述
  - **[R15 L2] gh release delete dead code を future-proofing と明示**: 現行 gh では成功時 stdout 空なので `if ($delOut -match '\S')` ブランチは常に false。コメントを「現行は発火しない、gh 将来 version への future-proofing として残置」に書き換え、reader が「`Write-Ok` では不十分な case があるのか?」と誤解する path を防止
  - **[R15 L3] CHANGELOG R13 L2/L3 フォーマット統一**: 他エントリの `**[R## L#] 見出し**: 説明文` 形式に揃える ( `↔` や文の ぶつ切りを解消)
- **PR #148 round 14 シニアレビュー + Codex bot P2 反映**:
  - **[Codex P2] vswhere に `-utf8` フラグ追加**: `Invoke-NativeWithCapture` は stdout/stderr を UTF-8 として decode するが、vswhere は default で system codepage 出力。非 UTF-8 locale で日本語 VS install path 等 non-ASCII path が mojibake → 後段 `Test-Path` 失敗 → MSBuild 検出失敗の silent path に落ちる。`-utf8` を Arguments に追加して vswhere 出力を UTF-8 強制
  - **[Codex P2 ×2] `[Console]::OutputEncoding` を try/catch でガード**: non-console host (CI / headless / redirected execution) では console API setter が `IOException` を投げ、`$ErrorActionPreference='Stop'` 下で release 開始前に script abort する path。script 冒頭の pin + `Invoke-ExternalProcess` finally の再 pin 両方を try/catch でラップ、script-level `$script:Utf8NoBomEncoding` を一意ソースとして共有
  - **[R14 H1] `WaitForExit()` コメントの自己矛盾を訂正**: 旧コメント「パイプバッファ完全フラッシュを保証」は誤り (WaitForExit no-arg のフラッシュ保証は `BeginOutputReadLine` 系の event-based async 読み取りに対するもので、本実装の `ReadToEndAsync` Task-based には適用されない)。実際のフラッシュ保証は `$outTask.Result` / `$errTask.Result` が EOF までブロックすることで提供。コメントを「`.Result` を削除すると出力切り捨てバグが発生するため touch しないこと」に書き換え
  - **[R14 M1] CHANGELOG label 重複解消**: 同 v0.1.8 entry 内に「Wave 2 シニアレビュー反映 (M1-M3)」と「PR #148 シニアレビュー反映 (M1-M3)」が共存し M1-M3 が 2 セット存在、後追い不能だった。本 entry のラベルを「R14」「W2」「Codex P2」等の round 識別子付きに変更
  - **[R14 L1] zip size 表示の小ファイル対応**: `[int]($bytes / 1MB)` は 1MB 未満で 0 表示になり破損誤解を招く。`if ge 1MB → MB / ge 1KB → KB / else B` の自動切替に変更、Phase 2 以降の Updater 単体 zip 等で発火する想定
  - **[R14 L2] ShowProgress 中の Task リーク (Known follow-up)**: `ReadToEndAsync()` 開始後に ShowProgress ループ内で例外発生 → finally で Dispose() → Task が `ObjectDisposedException` で faulted のまま GC まで放置。実害ほぼなし (現状実行 path で発火経路なし) だが、post v0.1.8 で `-TimeoutSeconds` + CancellationToken 対応する際に合わせて整理
- **PR #148 round 13 シニアレビュー反映**:
  - **[R13 M1] `gh release delete` UX 退行**: 成功時 stdout/stderr 無音のため `if ($delOut -match '\S')` が false で ShowProgress 行消去後に「無の状態」。`Write-Ok "既存タグ v$Version を削除完了"` を無条件追加して旧 PASS_THROUGH UX を回復
  - **[R13 M2] PASS_THROUGH catalog のメカニズム乖離**: catalog の例示は `& native-cmd` 直書きだが現実装は `Invoke-ExternalProcess` (Process 直叩き)。「(a) 直 & 演算子版」「(b) helper 経由 (推奨)」の 2 実装に分割記述
  - **[R13 M3] `ShowProgress` パスで `WaitForExit()` 未呼び出し**: HasExited ループ後にも `WaitForExit()` 明示呼びを追加、両 path 合流点で対称化 (上記 R14 H1 でコメント内容を訂正)
  - **[R13 L1 (保留)] CHANGELOG 日付ポリシー**: JST 統一 / UTC 統一 / PR merge 日のいずれかに統一する議論は CHANGELOG 全体に関わるため別途
  - **[R13 L2] WindowWidth fallback 条件の記載追加**: R13 M3 説明文に「`[Console]::WindowWidth` が 0 以下を返す non-console host も同様に fallback 120」を追記、コード側との整合を明示 (round 15 M2 で更に「30 未満も含む」に精度向上)
  - **[R13 L3] 引数 quoting backslash 制約のドキュメント化**: trailing backslash を持つパス引数 (`"C:\path\"`) は CommandLineToArgvW 規則上 `\"` が閉じ引用符として機能せず引数 parse 破損する既知制約を helper の inline コメントに明文化、現 call site は該当なし
  - **[R13 L4] `Invoke-ExternalProcess` の Process.Start 失敗 rethrow**: `Invoke-NativeWithCapture` と同じ context 付き例外メッセージ粒度で対称化
- **PR #148 round 12 (Wave 2) シニアレビュー反映**:
  - **[W2 M1]** catalog の PS 7.3+ 移行注意から vswhere を削除 (Wave 1 で helper 化済みのため stale)、残る `&` イディオムは `Assert-WorkingTreeClean` の `git status --porcelain` のみと正確化
  - **[W2 M2]** `Invoke-NativeWithCapture` docstring に「`$LASTEXITCODE` は更新されない、`.ExitCode` を使え」を明記
  - **[W2 M3]** ShowProgress の clear-line 幅を 70 ハードコード → `[Console]::WindowWidth - 1` で動的算出 (fallback 120)
  - **[W2 L1]** gh release create 成功時の URL 表示が gh の stdout フォーマット依存である旨をコメントに明記
  - **[W2 L2]** encoding pin コメントの「特に msbuild」断定を「Godot CLI / msbuild / nuget 等」に緩和
  - **[W2 L3]** `.Trim() / .TrimEnd()` 二重呼び解消 (`-match '\S'` + 一時変数化)
  - **[W2 L4]** `Combined` separator 判定コメントを 4 case 網羅に書き換え

#### Changed

- **Native command 呼び出しを `Invoke-NativeWithCapture` helper に一本化 (Wave 1: trap pattern 整理)**: `gh release view` (v0.1.7) / `gh auth status` (Round 11 Critical #1) / `vswhere` (Round 11 Low) を Process 直叩きベースの helper に移行。catalog から「失敗 path で stderr を出すコマンドでは罠を踏む」パターン (`SUPPRESS_BOTH` / `CAPTURE_DIAGNOSTIC` / `TRY_CATCH_NATIVE`) を anti-pattern に格下げ
  - **`Invoke-NativeWithCapture` helper 新設**: `System.Diagnostics.Process` で stdout/stderr/exit code を直接捕捉。PS の error stream を経由しないため NativeCommandError trap を構造的に回避。両 stream を async 読みでデッドロックも回避。返り値は `[PSCustomObject]` で `.StdOut` / `.StdErr` / `.ExitCode` / `.Combined` を提供 (Round 12 #1/3 のリネーム指摘もこの構造変更で自動解消)
  - **catalog 大幅整理**: 6 個あった pattern を「Invoke-NativeWithCapture 推奨 + 3 個の安全な `&` pattern + anti-patterns」に再構成。Round 11 Low の命名軸統一 + Round 12 全般の意図明確化を満たす形に
  - **`gh release view` の structured parse 表現訂正 (Round 11 High + Round 12 #2)**: `--json id` の効果は「存在時の stdout を最小化」のみで、parse はしていない。stderr 文字列マッチでの「不在 vs 別種失敗」判定は gh の英語メッセージ依存である旨をコメントに明記、本来の structured parse (`ConvertFrom-Json` + HTTP 404 判定 etc.) は将来課題として記録
  - **Round 11/12 の Medium / Low 一括対処**: `$_.Exception.Message` 単一行依存 (helper で StdErr 直接取得に変更で解消)、`$releaseViewOutput` mixed content (helper で StdOut/StdErr 分離で解消)、`Out-String` 末尾改行 `.TrimEnd()` 化、elseif → ネスト if/else (exit code を一次軸、stderr を二次軸の構造に)、catalog ↔ call site 重複圧縮 (helper 化で per-site コメントが固有理由 1-2 行に)
  - **`gh auth status` の同 trap も同時解消 (Round 11 Critical #1)**: v0.1.7 で CHANGELOG note として deferred していたものを Wave 1 で本対応
- **PASS_THROUGH の「例外節」呼称を廃止**: 旧 catalog では「実 release 操作 (gh release create/delete) は例外」と書いていたが、PASS_THROUGH 自体を catalog の一級 pattern として再定義したため例外節は不要に。`Invoke-GhRelease` 内の 2 件のコメントから「例外節 -」を削除

#### Known follow-up (post-v0.1.8 で対処予定)

- **`Invoke-NativeWithCapture` 内 TODO**: (a) 引数 quoting helper 切り出し (`Invoke-ExternalProcess` と duplicate)、(b) `-TimeoutSeconds` 引数で network hang ガード — いずれも post v0.1.8 で対処
- **既存 issue #142 / #143 / #144 / #146**: catalog 重複は本 v0.1.8 で大幅解消、`Release.bat` docstring 分離 / `Read-*` 命名 sweep / `$Context → $PostSync` は別途
- **#145 (CAPTURE_DIAGNOSTIC ラベル拡張) を本 v0.1.8 で close**: パターン自体を anti-pattern に格下げしたため拡張議論不要に

### [Release Tooling v0.1.7] - 2026-05-12

#### Fixed

- **本番 release で `gh release view` が NativeCommandError trap を踏んで preflight が失敗する問題 (#141 のインライン解決)**: v0.1.6 までの `& gh release view "v$Version" 2>$null | Out-Null` は DRY-RUN で `-SkipUpload` 自動 promote により実行されず、本番 release で v0.1.0 を publish しようとして初めて発火した
  - **根本原因**: PS 5.1 + `$ErrorActionPreference='Stop'` 環境では、native command が **exit 非ゼロ + stderr 出力** という組合せを返した場合に `2>$null` redirect が trap を防げない。これは PS が native command 用に別途生成する NativeCommandError ErrorRecord が `2>$null` の redirect 処理よりも先 (exit code 確定時点) に作られるため。v0.1.6 までの集約コメントは「`Out-String` を経由すれば Stop の判定対象から外れる」と書いていたが、これは exit 0 限定の挙動で、`gh release view "v0.1.0"` 不在時の `exit 1 + stderr "release not found"` には適用できなかった
  - **修正**: `try/catch` で受ける `TRY_CATCH_NATIVE` パターンを新設 + 該当 call site に適用。`--json id` で stdout を最小化 (#141 で提案していた structured parse)、`"release not found"` 文字列マッチで "確実に存在しない" を確証、別種の失敗 (auth / network / API rate limit / gh install 破損 等) は preflight で早期 Fail させる形に。これで H2 silent false-negative (gh の auth/network 障害を「タグ衝突なし」と誤判定して zip ビルド完了後に fail する path) も同時解消
  - **catalog 全体の精度向上**: 集約コメントの機構説明に「重要な制約」セクションを追加、`SUPPRESS_BOTH` / `CAPTURE_STDOUT` / `CAPTURE_DIAGNOSTIC` の各パターンが「success/failure path のどちらで stderr を出すか」によって安全性が変わる旨を明記。失敗 path で stderr を出すコマンドには `TRY_CATCH_NATIVE` 一択
  - 影響範囲は preflight の 1 call site のみ。zip / upload ロジックには変更なし
- **#141 (元シニアレビュー round 8 H2) を本 fix で close**: 当時「別 issue で `--json id` への移行」と切り出した内容を、本番踏み抜きを機にインラインで本格対応

#### Known follow-up (本 PR スコープ外、別ブランチで対処予定)

- **`gh auth status` の call site は本 PR の catalog 制約に未追従**: `Release.ps1` の `Assert-Preflight` 内 `$authStatusOutput = & gh auth status 2>&1 | Out-String` (CAPTURE_DIAGNOSTIC) は、本 PR で新設した制約 (「失敗 path で stderr を出すコマンドには TRY_CATCH_NATIVE 必須」) に該当する。`gh auth status` は未認証時 exit 1 + stderr 出力なので、`gh auth login` 未実行の dev clone / token 期限切れ / `GH_TOKEN` 消失 等のシナリオで NativeCommandError 例外で abort し、本来表示するはずの `Fail "gh 認証に失敗しました ..."` メッセージが届かない。本 PR のスコープは `gh release view` の 1 call site に絞ったため未対応、改善ブランチでまとめて TRY_CATCH_NATIVE に移行予定。シニアレビュー round 11 で同 PR 内対応 or CHANGELOG note のどちらかが許容ラインとされたため、後者を選択

### [Release Tooling v0.1.6] - 2026-05-11

#### Fixed

- **`Release.bat` ダブルクリック / ターミナル実行で実質動作しない問題**: cmd.exe が UTF-8 BOM (`EF BB BF`) を CP932 として解釈、1 行目の `@echo off` が機能せず、全 REM 行を echo on で splay + cp932-mojibake で表示する状態だった。UTF-8 (no BOM) + ASCII プレフィックス (`chcp 65001` 以前は ASCII 限定) に変更して解消
- **`gh release view` が release 不在時に script を terminating error で止める問題**: preflight タグ衝突チェックの `2>&1` が PS 5.1 + `$ErrorActionPreference='Stop'` 下で NativeCommandError 化、本来「衝突なし続行」のはずの正常系で fail していた問題。`2>$null | Out-Null` パターンに変更
- **同 anti-pattern が `gh auth status` と `git status --porcelain` にも残っていた問題**: シニアレビューで指摘され全 3 箇所を統一
  - `gh auth status`: `& cmd 2>&1 | Out-String` パターンで診断情報を文字列として保持。失敗時に `gh auth login` 以外の原因 (network / proxy / token expiry / install 破損) も Fail メッセージに出して切り分け可能に
  - `git status --porcelain`: redirect 削除 + 明示的 `$LASTEXITCODE` チェック + Fail 追加。redirect 単純削除では git 失敗時に "clean" 誤報告の **silent pass regression** を踏むため exit code check 必須

#### Changed

- **v0.1.5 で導入した `[WARN]` メッセージの日本語併記行を削除**: chcp 取得失敗パスは codepage 未切替 + ファイル no-BOM のため日本語 echo の文字化けが確定的、ASCII-only ルールに整合させる形で意図的に撤回
- **`2>&1` trap 関連の per-site コメント 3 箇所を集約 + 参照形式に整理**: Release.ps1 冒頭の `Set-StrictMode` 直後に集約コメントを設置、各 call site は集約参照 + 1-2 行の理由のみ。集約ラベルは self-documenting な名前 (`SUPPRESS_BOTH` / `CAPTURE_DIAGNOSTIC` / `CAPTURE_STDOUT` / `CAPTURE_STDOUT_PASS_STDERR` / `PASS_THROUGH` / `STOP_TRAP`) で索引参照型コメントの欠点 (表まで戻る往復) を解消。集約コメント内に「機構」セクションを追加して PS 7.x 等への移植時の調査起点を明示
  - **シニアレビュー round 7 反映**: `Assert-WorkingTreeClean` の git status call site は stdout を変数 capture / stderr を console 直書きする hybrid なので、PASS_THROUGH (console 直書き、変数 capture なし) では実態と乖離していた。`CAPTURE_STDOUT_PASS_STDERR` パターンを新設して移行。call site のパターンマーカーと catalog 定義の整合性を保つことが「self-documenting パターン名」アプローチの前提
  - **`STREAM_TO_VAR` の独立記載を廃止**: `STOP_TRAP` と本質的に同じ罠 (`2>&1` 自体が原因) で、変数 capture 有無は表面の違い。「PS バージョンで挙動が変わる」表現が誤読を招くため統合し、両形式を `STOP_TRAP` 配下にまとめて回避策を 2 つ並記
  - **`Invoke-GhRelease` の gh release create / delete に pattern マーカー追加**: 集約 catalog の「例外」節に書いてあるが call site にマーカーがなく「対象外」と読まれる余地があった。`# pattern: PASS_THROUGH (例外節)` を 2 箇所に追加
- **`Test-Preflight` → `Assert-Preflight` リネーム**: PowerShell 規約で `Test-*` は `[bool]` 返却用 (`Test-Path` / `Test-Connection` 等)。preflight は違反時 `Fail` (= `exit 1`) する性質なので `Assert-*` 系に揃え、同ファイル内の `Assert-WorkingTreeClean` と命名一貫性を取る
- **`Release.bat` の防御性向上**:
  - `ORIGINAL_CODEPAGE` の数値 validation (`findstr /R "^[0-9][0-9]*$"`) を追加。chcp 出力が想定外フォーマット (unicode digit / 想定外 locale 等) の場合に exit 時の `chcp %ORIGINAL_CODEPAGE%` が garbage を渡して cmd 窓が壊れるレアケースを防御
  - `FORWARDED_ARGS` 連結ロジックを `if defined` 分岐で書き換え、初回 append 時の leading space を解消 (echo 出力の見た目改善)
- **Release.bat の docstring 整理**: BOM 失敗症状の記述を「最初の 1 行目で `@echo off` ITSELF が fail する」に厳密化 (旧記述 "subsequent commands fail" は誤り)、chcp 65001 境界 banner を 4 行 → 1 行 marker に簡素化 (詳細は docstring と一元化)
- **Release.bat に codepage 切替の Flow ダイアグラム追加**: `for /f` + 3 if 文の状態遷移 (`captured + numeric` / `captured + invalid` / `not captured`) を docstring 冒頭の Flow セクションで明示。3 つの独立 if を逐次読まなくても判断できる
- **`Get-BundleReleaseNotes` の正規表現に `\Z` 追加**: 旧 lookahead `(?=^### |^---|^## )` は必ず後続セクションマーカーを要求し、Bundle entry が CHANGELOG 末尾 (後続なし) だと空文字列を返す regression。現状の CHANGELOG 構造 (Bundle 配下に Launcher / Manager / Release Tooling が続く) では発火しないが、将来セクション順を入れ替えた場合や最小 CHANGELOG での誤動作を防ぐ保険
- **`gh auth status` の success 時 warning を抽出して `Write-Warn`**: `gh` は exit 0 でも stderr に token scope 不足 / token expiry 近接の warning を出すことがある。失敗ではないが早期の気付きをリリース担当に届けるため、出力中の特定パターンを検出して警告表示 (release 自体は継続)
  - **シニアレビュー round 8 (M1) 反映**: 初版の `warning|expir` regex は `expiration` / `experimental` / 通常 success 時の `Token expires:` 等にも hit する false positive 多数。特異性高めの `(token has expired\|token expir(es\|ed\|ing) in \d+\s*(day\|hour)\|missing.*scope\|insufficient.*scope\|^warning:)` に厳密化
- **シニアレビュー round 10 反映**:
  - **L4**: catalog `PASS_THROUGH` の「exit code チェック必須」理由を cross-reference から自己完結型に修正。旧コメントは「CAPTURE_STDOUT_PASS_STDERR と同じ理由」と参照していたが、後者の理由は「出力空でも `$LASTEXITCODE` 非ゼロ誤判定防止」で変数 capture が前提。`PASS_THROUGH` は変数 capture なしで構造的に該当しない。`PASS_THROUGH` の真の理由「成否判定の信号が exit code のみのため」に書き換え
  - **L5**: `Get-BundleReleaseNotes` の `-SilentMissing` switch を `-AllowMissing` にリネーム。旧名は「CHANGELOG.md が見つからない時」のみを silent にする読みだが、実体は「Bundle セクションが見つからなかった時」も含めて empty 返却する semantics。`AllowMissing` の方が実態に近い
- **別 issue 化**:
  - **M1 (#144)**: `Read-LauncherVersion` / `Read-ManagerVersion` / `Read-ComponentVersions` の命名規約 sweep — `Read-*` は PowerShell では対話的入力 (`Read-Host` 等) を意味、ファイル読み出しは `Get-*` 系が原則。さらに `Fail` する性質を踏まえると round 6/9 の `Test-*` → `Assert-*` sweep の対象範囲外として漏れていた
  - **M2 (#145)**: catalog `CAPTURE_DIAGNOSTIC` ラベル説明 (「失敗時に表示するため」) が実態より狭い。実装では success 時の `^warning:` 検出にも再利用しているため、「両 stream 文字列化キャプチャ」相当に拡張すべき
  - **M3 (#146)**: `Assert-WorkingTreeClean` の `$Context -like '*sync 後*'` 文字列マッチを `[switch]$PostSync` パラメータに構造化 — call site の文字列を変えると特例メッセージが silent に失われる脆さを解消
- **シニアレビュー round 9 反映**:
  - **H1**: `Test-ExpectedFiles` → `Assert-ExpectedFiles` リネーム。round 6 で `Test-Preflight` → `Assert-Preflight` した時と完全に同じ条件 (`Fail` (exit 1) する関数で PowerShell の `Test-*` = `[bool]` 規約に違反) に該当していた漏れを解消、命名規約の対象範囲を完結させる
  - **M2**: `gh auth status` warning 検出 regex を `^warning:` (gh 公式の warning prefix) のみに簡素化。round 8 の特殊形式 (`token expir(es|ed|ing) in \d+...` 等) は false negative リスク (「Token will expire soon」「tomorrow」等の日数表記なし形式の取りこぼし) があり、かつ gh 出力フォーマット変更に脆弱。本格的な structured 検出は別 issue (#141 系) で JSON parse に寄せる方針なので、ここでは過信を生む特殊形式を持たない方向に倒した
  - **L2**: `Get-BundleReleaseNotes` の `\Z` コメントを 6 行 → 1 行に圧縮。発火確率の低さに対しコメント長が不釣り合いだった
  - **L4**: `Assert-Preflight` 関数内のリネーム言い訳コメント (2 行) を削除。リネーム判断の justification は CHANGELOG / PR 説明に書く内容で、関数定義に残すと半年後の reader が「なぜここに?」となる
  - **L5**: `Release.bat` の `enabledelayedexpansion` 副作用例から架空引数 `-Tag` を削除、現存 pass-through 引数の列挙 + 「新規 pass-through 引数追加時の注意」framing に変更
- **別 issue 化**:
  - **M1 (#142)**: catalog vs per-site コメントの方針統一 — ラベル参照 + 詳細説明の二重化がメンテナンスコストを生む構造に逆戻りしていた問題。本 PR スコープ外で trackable 化
  - **M3 (#143)**: `Release.bat` の docstring 経緯を `SPECIFICATION.md` に分離 — 100/150 行が REM の状態を整理する separate PR 候補
- **シニアレビュー round 8 反映**:
  - **H1**: `Release.bat` の `findstr` validation コメントに「`findstr` 自体の起動失敗 (minimal WinPE / PATH 破損) も同じ skip path に落ちる」旨を追記。区別不可なので同一扱いは意図的だが、コメントが反映していなかった
  - **M2**: catalog ⇔ call site 整合性の最終 sweep。`Resolve-MsBuild` の vswhere call site (CAPTURE_STDOUT 該当) に label が欠落していたため追加。これで全 6 件の native `&` call site にパターンマーカー揃った
  - **M3**: PS 7.3+ の `$PSNativeCommandUseErrorActionPreference` (default `$true`) が `& cmd` の非ゼロ exit code を terminating error 化する仕様を集約コメントに追記。本スクリプトの「2>&1 を外して `$LASTEXITCODE` で判定」イディオムが PS 7 で silent regression する可能性 + 回避策 (`$false` 固定 or try/catch) を記録
  - **L1**: `Release.bat` の `setlocal enabledelayedexpansion` で引数中の `!` が消費される副作用を docstring に明記
  - **L2**: anti-pattern の「変数 capture なし版」が現実に書かれる経緯 (副作用 only call で stderr も console に出したい意図の typo) を 1 行追加
  - **L4**: chcp 65001 skip path で日本語 echo が文字化けする旨を ASCII 1 行で改めて警告 (line 89-90 の `[WARN]` だけでは見落とされる可能性に対する保険)
  - **L5**: `Get-BundleReleaseNotes` の `\Z` コメントを精度向上。現実の発火条件は「初回 Bundle release + 後続セクション未追加の極初期状態」のみで、汎用的な「セクション順変更時」は誤誘導
  - **H2 → 別 issue 化 (#141)**: `gh release view` の exit code 単独依存による silent false-negative。zip ビルド完了後に fail する path が残るが、本 PR スコープ外で `--json id` への移行を別 issue として trackable 化

### [Release Tooling v0.1.5] - 2026-05-11

#### Fixed

- **`Release.bat` ダブルクリック / ターミナル実行で何も出力されない問題を修正**: リポジトリの `.gitattributes` に `* text=auto eol=lf` がデフォルト設定されており、checkout 時に `Release.bat` も LF 改行に強制されていた。**cmd.exe は LF only の bat を正しくパースできない**ため、ダブルクリックしても無音で fail していた (process は起動するが何も実行されない)
  - `.gitattributes` に `*.bat text eol=crlf` / `*.cmd text eol=crlf` 例外を追加し、`.bat` / `.cmd` ファイルは checkout 時に CRLF 改行に強制
  - `Release.bat` 自体も UTF-8 BOM + CRLF に書き換え (旧 SJIS 形式は VS Code 等のモダンエディタで開くと文字化けして編集体験が悪い、新形式は cmd.exe / エディタ両方で正しく扱える)
  - `Release.bat` 冒頭で `chcp 65001 >nul` を発行して UTF-8 codepage を強制 (念のための保険、cmd.exe の UTF-8 BOM 認識挙動が環境依存のため)。exit 時には元の codepage を復元
  - codepage 取得失敗時 (chcp 出力フォーマットが想定外のロケール等のレアケース) は `chcp 65001` への切替自体を skip して呼び出し元 cmd 窓への副作用を回避、警告メッセージを表示
  - 警告メッセージは **英語先 + 日本語後** の併記 (失敗経路では UTF-8 codepage 未切替で日本語が文字化けする可能性が高いため、ASCII で確実に届く英文を先頭に配置)
  - `setlocal` の endlocal が `chcp` を復元しない (chcp は console-wide な状態) ため明示復元が必要、というハマりやすい挙動の経緯をコード上に明記
  - **BOM 認識不能環境での脱出経路** (Win 10 1809 以前の古い cmd.exe build 等) を docstring に記録: 「BOM 外して UTF-8 (no BOM) + CRLF に戻し、chcp 65001 より前の REM / echo は ASCII 化」の 3 ステップ
  - `Release.bat` の docstring に CRLF 必須の経緯を追記

### [Release Tooling v0.1.4] - 2026-05-11

#### Fixed

- **`-DryRun` モードでも GitHub preflight が走る問題を修正 (Codex P2 #137)**: 旧実装は `-DryRun` 時も `-SkipUpload` がなければ `gh auth status` / `gh release view` を呼び出していた。`-DryRun` は zip 化と upload を skip するモードのため、preflight だけ network 必須にする意味は無い。gh 認証なし環境やオフライン環境で `-DryRun` 単体実行が fail していた問題を解消。v0.1.3 で導入した `-Offline → -SkipUpload promote` ロジックを拡張し、`if (($Offline -or $DryRun) -and -not $SkipUpload) { $SkipUpload = $true }` の形に統合。docstring も `-DryRun` の説明を更新

### [Release Tooling v0.1.3] - 2026-05-11

#### Fixed

- **manifest sync 後の working tree 再検証を追加 (Codex P1 #137)**: `Test-Preflight` で working tree clean を要求した後、`Set-ManifestVersions` が `project.godot` / `export_presets.cfg` を書き換えると tree が dirty になり、その状態で packaging + tag 付けが進むと **source ↔ artifact traceability が崩れる** 問題を解消。新規 `Assert-WorkingTreeClean` 共通関数を切り出し、preflight と sync 後の 2 タイミングで呼ぶように変更。sync 後の dirty 検出時は「`Set-ManifestVersions` が書き換えた差分をコミットしてから再実行」という具体的なメッセージで fail (`Set-ManifestVersions` は idempotent なので 2 回目以降は no-op)
- **`-Offline` モードで GitHub preflight が走る問題を修正 (Codex P2 #137)**: 旧実装は `-Offline` 時も `-SkipUpload` がなければ `gh auth status` / `gh release view` を呼び出していた。オフライン環境ではこれらが必ず fail するため `-Offline` フラグが advertise 通り機能していなかった。`param` block 直後で `if ($Offline -and -not $SkipUpload) { $SkipUpload = $true }` の自動 promote を追加し、`-Offline` 指定時は upload 関連の preflight + 実 upload を全て skip する形に統一。docstring も `-Offline` の説明を更新

### [Release Tooling v0.1.2] - 2026-05-11

#### Fixed

- **`export_presets.cfg` が gitignore で除外されてクリーン clone で Release.ps1 が即 fail する問題を修正 (Codex P1 #137)**: Godot のデフォルト `.gitignore` テンプレが `export_presets.cfg` を除外する慣習で、本プロジェクトの初期 `.gitignore` もそれを踏襲していた。しかし Release.ps1 の `Set-ManifestVersions` が `application/file_version` を書き換えるため必須ファイルになっており、別開発者の clean clone では `ReadAllText` の段階で fail していた。`.gitignore` から `export_presets.cfg` を除外し tracked 化、初期の Godot エディタ生成版を repo に含める形に変更
- **`bin\Release\` 再帰コピーで前回ビルド時の runtime ゴミが zip に混入し得る問題を修正 (Codex P1 #137)**: 開発者が Manager を直接 `bin\Release\` から起動した場合、log / db / backups 等の runtime ファイルが `bin\Release\` に発生する。`Get-ChildItem -Recurse -File | Where Extension -ne '.pdb'` で拾ってると、これら不要ファイルが release zip に紛れ込む可能性があった。`Build-Manager` の msbuild 直前に `bin\Release\` を `Remove-Item -Recurse -Force` で完全削除し、毎回クリーンビルドする形に変更。これで bin\Release/ には msbuild が生成した正規の output のみが残り、コピー対象の予期せぬ追加が起きない

### [Release Tooling v0.1.1] - 2026-05-11

#### Changed

- **`RELEASE_VERSION` ファイル廃止、`CHANGELOG.md` を Bundle version の SoT に統合 (#108 Phase 1)**: 旧設計では `RELEASE_VERSION` ファイル (リポジトリルート) と `CHANGELOG.md` の `### [Bundle vX.Y.Z]` エントリの 2 箇所にバージョン情報を持つ二重管理だったが、整合性チェックが必要になり煩雑だったため、CHANGELOG 1 本に集約
  - `Release.ps1` の `-Version` 引数を optional に変更、省略時は `CHANGELOG.md` の最上段 (最新) の `### [Bundle vX.Y.Z]` エントリから自動取得
  - `-Version` を明示指定した場合は CHANGELOG の最新版数と一致するか検証し、不一致なら fail
  - `Release.bat` から `RELEASE_VERSION` ファイル読み取り処理を削除、引数を Release.ps1 にそのまま forward する形に簡素化
  - SPEC §3.7.7 / AGENTS.md "Release and Versioning" / §3.7.8 チェックリストも同方式に対応する形に更新

### [Release Tooling v0.1.0] - 2026-05-11

#### Added

- **`Release.ps1` を repo root に新設 (#108 Phase 1)**: Launcher (Godot CLI export) + Manager (msbuild Release ビルド) を一括ビルドし、`release/v<version>/files/` に staging、`release/GCTonePrism_v<version>.zip` を生成、`gh release create` でアップロードする PowerShell スクリプト
  - **Bundle version 制度**: `CHANGELOG.md` の最新 `### [Bundle vX.Y.Z]` エントリで Bundle 版数を管理。zip タグは `v<X.Y.Z>` 形式で既存の `Launcher_v*` / `Manager_v*` tag との命名衝突を回避
  - **Godot エディタ + export templates の自動 DL**: `project.godot` の `config/features` から major.minor を読み取り、`$GodotPatchTable` で patch をピン留めして `tools/godot/<patch>/` と `%APPDATA%/Godot/export_templates/<patch>.stable/` にキャッシュ。SHA256 検証 + 3 回 retry + キャッシュ命中時 skip + 古い version は最大 2 件まで自動削除 (AppData 側は `.gctone_managed` マーカー方式で外部管理 templates を保護)
  - **DL 進捗表示**: PS 5.1 標準の `Invoke-WebRequest` はプログレスバー描画バグで DL が極端に遅くなるため、`System.Net.Http.HttpClient` でチャンク読み出し + 50MB / 5 秒ごとに `MB / MB (MB/s)` を表示
  - **NuGet 自動 DL**: `tools/nuget-<version>.exe` にバージョンピン留めでキャッシュ。`$NugetPinnedVersion = '6.10.0'`
  - **MSBuild 自動検出**: `vswhere` → PATH の順で VS / Build Tools を検出。Manager コードは C# 7+ (ValueTuple / string interpolation 等) を使うため Roslyn を含む MSBuild 14+ が必須で、Windows 同梱 .NET Framework MSBuild は不使用。MSBuild 未検出時は VS Build Tools のインストール手順 (https://aka.ms/vs/17/release/vs_BuildTools.exe、~1-2GB) を案内する詳細エラーで fail
  - **`nuget restore` の x64/x86 漏れ修正**: `Stub.System.Data.SQLite.Core.NetFramework` の packages.config 形式 restore で `build/net46/x64/SQLite.Interop.dll` と `x86/` が展開されない既知問題を、nupkg を直接 `System.IO.Compression.ZipFile` で開いて欠損ファイルだけ抽出する形で防御
  - **Process.Start ベースの外部プロセス呼び出し**: PowerShell 標準 `&` (call operator) は大量の stdout で非同期 return することがあり、`Start-Process -ArgumentList` は配列要素にスペースを含むと argument 分割するため、`ProcessStartInfo.Arguments` を自前で quoting する形を採用 (`Invoke-ExternalProcess` ヘルパー)
  - **`-DryRun` / `-SkipUpload` / `-Force` / `-Offline` / `-GodotExe` / `-GodotPatch` / `-MsBuildExe` / `-NugetExe` 引数**: 開発・運用シナリオに合わせて部分実行可能
  - **export_presets.cfg / project.godot の version 自動同期**: `version.gd` の MAJOR/MINOR/PATCH を SoT として `application/file_version`、`application/product_version`、`config/version` を Release.ps1 が機械的に書き換え (`Write-FileUtf8NoBom` で BOM なし UTF-8 で書き出し、Godot ConfigFile パーサと互換)
  - **release_notes は CHANGELOG.md から自動抽出**: 該当 Bundle セクション (`### [Bundle v<X.Y.Z>]`) をパースして `gh release create --notes` に渡す。`release_notes/` ディレクトリは持たない（CHANGELOG が SoT、重複記述なし）
- **`Release.bat` を repo root に新設**: `Release.ps1` のラッパーバッチ。`RELEASE_VERSION` ファイルを読んで `-Version` を自動補完するため、本番運用は `.\Release.bat` 1 発で完結（ダブルクリック実行も可能）。引数を Release.ps1 にそのまま forward する仕組みのため `.\Release.bat -DryRun -SkipUpload` 等の組み合わせも可能。`-NoPause` 引数で CI / 自動化用の pause 抑止にも対応 (Shift-JIS + CRLF で保存、cmd.exe の日本語環境互換)
- **`.gitignore` 拡張**: `release/` (Release.ps1 生成物) / `tools/godot/` / `tools/nuget-*.exe` (auto-DL) を git 追跡対象から除外

---

## Launcher（ランチャー本体）

### [Launcher v0.5.16] - 2026-05-11

#### Added

- **ファイルログ基盤 (#116, #85 の土台先行)**: 新規 autoload `scripts/logger.gd` を追加し、`<project_root>/logs/launcher/launcher_<PCname>_<YYYY-MM-DD_HHmmss>.log` に **1 起動セッション = 1 ファイル** でログ出力。INFO / WARN / ERROR の 3 段階、`[YYYY-MM-DD HH:mm:ss] [LEVEL] [Module] msg` 形式（Manager と統一）
  - **既存 print 系を Godot 標準ログのテールで自動キャプチャ**: 既存 41 件 / 6 ファイルの `print()` および `printerr()` / `push_warning` / `push_error` がすべて自動的にファイルにも流れる仕組み（コード変更ゼロ）。実装は Godot 標準ファイルログ (`user://logs/godot.log`) を 0.5 秒間隔でテール → 新規追加分を本セッションファイルに `[Godot]` プレフィックス付きで転送
    - 行頭の `WARNING:` / `USER WARNING:` → WARN、`ERROR:` / `SCRIPT ERROR:` / `USER ERROR:` → ERROR、それ以外 (print 等) → INFO で自動振り分け
    - **設計の経緯**: 当初は `OS.add_logger` (Godot 4.5+) で `Logger` クラスを継承したカスタムロガーを登録する方針だったが、Godot 4.6 の GDScript パーサーが script 継承の `Logger` 型変換を蹴る (inner class / class_name / preload + as Logger キャストすべて NG) ため、Godot 標準ログのテール方式に切替。0.5 秒の polling 遅延が出るが、Godot 内部エラーまで含めて全部キャプチャできるメリットあり
  - **明示 API**: 新規コードからは `Logger.info(msg)` / `Logger.warn(msg)` / `Logger.error(msg)` でレベル指定可能
  - **保存先**: prism.db と同じ共有先 `<project_root>/logs/launcher/` に集約。Manager 側 `<project_root>/logs/manager/` と並列配置で、将来 Manager のログビューア UI から複数 PC の Launcher ログも 1 箇所で閲覧可能に
  - **1 セッション 1 ファイル設計**: PC 名 + 起動時刻でファイル名がユニークになるため、複数展示 PC の Launcher が同時に同じ共有先へ書き込んでも書き込み競合・行間 interleaving が一切起きない。同秒衝突は連番サフィックスで回避
  - **30 日 retention**: 起動時に `logs/launcher/launcher_*.log` をスキャンし、`get_modified_time` が 30 日より古いものを削除。現セッションのアクティブファイルは保護
  - **prism.db 自動検出 + フォールバック**: PathManager と同じく exe ベースで上に prism.db を 10 階層まで探す軽量ロジックを Logger 内部に持つ（PathManager の起動 print も Logger でキャプチャしたいため、依存を持たせず重複実装）。見つからなければ exe 隣（エディタ実行時は `res://`）にフォールバック
  - **起動・終了イベント記録**: `_init()` で「Launcher 起動 (PC=...)」、`NOTIFICATION_WM_CLOSE_REQUEST` / `NOTIFICATION_PREDELETE` で「Launcher 終了」を必ず INFO 出力
  - **`Logger` autoload を最先頭に登録**: `project.godot` の `[autoload]` 先頭に追加して、他の autoload (`ErrorManager` / `AppManager` 等) の `_init/_ready` の出力も確実にキャプチャ
  - **既存 `user://logs/` (Godot 標準) との関係**: %APPDATA% 配下の Godot デフォルトログは引き続き残るが、本基盤の `<project_root>/logs/launcher/` が事故調査時の正規場所。本番展示 PC のリカバリで揮発しない
  - **横断要件として SPEC §3.6 / AGENTS.md "Cross-component Standards" に格上げ**: 同じベースラインを Monitor 等の将来コンポーネントでも適用する仕組みを文書化

#### Changed

- **`config/version` を `0.5.15` → `0.5.16` に更新、`version.gd` の `PATCH` も `14` → `16` に同期**
- **`scripts/database_manager.gd` の `CURRENT_DB_VERSION` を 8 → 12 に追従**: Manager 側 DB が v9 → v12 まで進んでいたが Launcher が 8 のまま放置されており、起動毎に「DBバージョン(12)が Launcher の対応バージョン(8)より新しい」警告が出ていた問題を本 PR の動作確認で発見。v9 / v10 / v12 は backup_log 関連で Launcher は触らない、v11 の surveys / play_records 新スキーマには Launcher のクエリが既に対応済 (`pr.start_time` 等を参照) のため、定数追従のみで安全に解消

#### Changed

- **`prism.db` のジャーナルモードを WAL → DELETE へ移行 + `busy_timeout=10000` (#103)**: 学校 SMB ファイルサーバー上で `prism.db` を共有する運用形態において、SQLite 公式が WAL モードの動作を保証外と明言しているため、Manager v0.8.2 と歩調を合わせて `PRAGMA journal_mode=DELETE` に変更
  - 続けて `PRAGMA busy_timeout=10000` を発行し、書き込み競合時に即時 SQLITE_BUSY を返さず最大 10 秒待機する挙動に
  - Launcher は実質 Read-Only（アンケート/プレイ記録は drop-folder 経由）のため運用上の体感影響は無い見込み
- **`config/version` を `0.5.8` → `0.5.15` に同期**: `project.godot` の `config/version` が v0.5.9〜v0.5.14 のリリース時に更新されていなかった drift を本リリースで解消

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

### [Manager v0.8.9] - 2026-05-11

#### Added

- **ログビューア UI を追加 (#129)**: 新規「ログ」タブで `<project_root>/logs/manager/` と `<project_root>/logs/launcher/` 両方のログファイルを Manager から閲覧可能に。Manager v0.8.8 で導入したファイルログ基盤の実用化として、エクスプローラから直接ファイルを開かなくても部員が GUI で過去ログを追える
  - 新規 `Controls/LogSectionPanel.cs` + `.Designer.cs`（既存 BackupSectionPanel と同じ UserControl パターン）
  - 上部 2 行ツールバー (76px): 1 行目=更新ボタン + INFO/WARN/ERROR レベルフィルタ + Manager/Launcher コンポーネントフィルタ + (右) ファイル件数表示。2 行目=検索窓（横一杯）
  - 上下分割（横割り SplitContainer）: 上ペイン=ファイル一覧 (DataGridView)、下ペイン=本文 (RichTextBox)
  - ファイル一覧の列: アプリ / PC / 開始日時 / 最終更新 / サイズ / ファイルパス（残り幅 Fill）。新しい順ソート、選択時に下ペインへ内容ロード
  - 本文表示: Consolas 9pt + WordWrap 有効（横スクロール無し、長い行は改行）
  - **ハイライトモード（フィルタしないで強調）**: フィルタや検索で他の行が消えるのではなく、マッチ行は通常表示+レベル別背景色、非マッチ行は薄い灰色文字でディム。検索ヒット substring には黄色ハイライトを追加。文脈ごと見られるので「エラー前後の INFO ログ」も自然に追える
  - レベル分類: INFO=白 / WARN=淡黄 / ERROR=淡赤 背景色。スタックトレース等のフォーマット外行は直前行と同レベルとして扱う
  - コンポーネントフィルタ: Manager / Launcher のチェックを外すとそのファイルが灰色化（一覧から消えるのではなく見えるけど目立たない状態）
  - ファイルは `FileShare.ReadWrite` で開くため、Logger が書き込み中のセッションファイルもロックなしで閲覧可能
  - フィルタ変更時はファイル再読み込みなしで描画のみ更新（パフォーマンス）

### [Manager v0.8.8] - 2026-05-11

#### Added

- **ファイルログ基盤 (#116)**: 新規 `Services/Logger.cs` を追加し、`<project_root>/logs/manager/manager_<PCname>_<YYYY-MM-DD_HHmmss>.log` に **1 起動セッション = 1 ファイル** でログを出力。INFO / WARN / ERROR の 3 段階、`[YYYY-MM-DD HH:mm:ss] [LEVEL] [Module] msg` 形式。これまで Manager はエラーが出ても後追いで原因調査ができなかった (#115 の WAL 起因エラー診断時に判明) 問題を根本解決
  - **既存 `Console.WriteLine` を `Console.SetOut` で自動キャプチャ**: 既存 109 件 / 11 ファイルの `Console.WriteLine($"[Module] msg")` 呼び出しが **コード変更ゼロ** で自動的にログファイルにも書かれるよう、カスタム `TextWriter` (内部 `ConsoleHookWriter`) を `Program.cs` の `Logger.Initialize()` で `Console.SetOut(...)` 経由で差し替え。INFO 扱いで記録
  - **明示 API**: 新規コードからは `Logger.Info / Warn / Error / Error(msg, ex)` でレベル指定可能。`Error(msg, ex)` は `Exception.ToString()` を改行付きで追記
  - **保存先の判断**: 当初 Issue #116 提案の `%APPDATA%\GCTonePrism_Manager\logs\` ではなく **`<project_root>/logs/manager/`**（prism.db と同じ共有先）に変更。理由は (1) Manager の将来的なログビューア UI で複数 PC のログを 1 箇所で閲覧可能、(2) 本番展示 PC のリカバリ・OS 再構築で揮発しない、(3) クラブメンバーがエクスプローラから直感的に発見できる、(4) Launcher と対称な設計（Launcher も同じ `<project_root>/logs/launcher/` に出力）
  - **1 セッション 1 ファイル設計**: PC 名 + 起動時刻でファイル名がユニークになるため、複数 PC が同時に同じ共有先へ書き込んでも書き込み競合・行間 interleaving が一切起きない。同秒衝突は連番サフィックスで回避
  - **30 日 retention**: 起動時に `logs/manager/manager_*.log` をスキャンし、`LastWriteTime` が 30 日より古いものを削除。現セッションのアクティブファイルは `_currentLogPath` 一致チェックで明示的に保護
  - **障害耐性**: `Logger.Initialize()` 失敗時は MessageBox 警告のみで Manager 起動は継続。`Write` の例外は握り潰し、Logger 自身の例外は **絶対にログに書かない**（無限ループ防止）
  - **プロジェクトルート検出は Logger 内部で重複実装**: PathManager の起動 `Console.WriteLine` も Logger でキャプチャしたいため、Logger は PathManager に依存せず自前で exe → 上に prism.db を 10 階層まで探す軽量ロジックを持つ。見つからなければ exe 隣にフォールバック
  - **起動・終了イベント記録**: `Program.cs` を `try-finally` で囲み、`finally` で `Logger.Shutdown()` を呼んで終了ログを必ず出す。事故調査で「いつ落ちたか」を追跡可能に
  - **横断要件として SPEC §3.6 / AGENTS.md "Cross-component Standards" に格上げ**: 同じベースラインを Monitor 等の将来コンポーネントにも適用する仕組みを明文化

### [Manager v0.8.7] - 2026-05-10

#### Added

- **バックアップ履歴の相対パス保存 (#126)**: `backup_log` テーブルに `relative_path` カラムを追加 (DB v11 → v12 マイグレーション)。バックアップ作成時に `prism.db` のあるディレクトリからの相対パスを記録し、表示・復元時に動的にパスを再構築することで **プロジェクト場所の移動に追従** できるように
  - 新規 `Services/BackupPathResolver.cs`: `relative_path` を優先し、無ければ `file_path` にフォールバックする共通ヘルパー
  - 既存レコード (relative_path NULL) はそのまま絶対パスで動作（後方互換性）
  - dbDir 配下にない destinationDir (ユーザーが絶対パスで設定) は relative_path NULL のまま
- **failed 履歴の自動掃除 (#126)**: Manager 起動時 (`RefreshDisplay`) のリコンサイル処理で、`status='failed'` のレコードを **物理ファイル + DB レコード両方** から自動削除。失敗履歴は復元には使えないため、ユーザーに表示する価値が無い + 古いプロジェクトパスのゴミが残り続ける主因にもなっていたため
- **個別削除 UI (#126)**: 「バックアップ」タブの操作セクションに「選択した履歴を削除...」ボタンを追加。確認ダイアログを経て、選択行のバックアップファイル + DB レコード両方を削除
- **不在ファイル非表示 (#126)**: 履歴表示時にパス解決後の `File.Exists` チェックを行い、ファイルが見つからないレコードは履歴一覧に表示しない (DB レコードは保護のため残す)
- **`DatabaseManager.DatabasePath` プロパティ**: `_conn.DbPath` を公開する読み取り専用プロパティ。BackupPathResolver 等から参照

#### Database

- DB バージョン: 11 → 12
- `backup_log` テーブルに `relative_path TEXT NULL` カラムを追加
- `MigrateV11ToV12` を新設（既に列がある場合はスキップ、ALTER TABLE で追加）
- `ExpectedSchema` の `backup_log` 列リストにも `relative_path` を追加

### [Manager v0.8.6] - 2026-05-10

#### Changed

- **`EditGameForm` / `AddGameForm` / `VersionUpForm` を 2 列レイアウトに刷新 (#123)**: 縦長 (480 × 888〜1000px) だった入力フォームを 2 列構成 (950 × 570〜645px) に再設計。ノート PC でも縦スクロールなしで全項目が見渡せるようになった
  - **左列**: 基本情報 (ゲームID/タイトル/説明文/ジャンル/プレイヤー数/難易度/プレイ時間/フラグ/通信プレイ対応 など)
  - **右列**: アセット & 設定 (サムネイル/背景画像 + プレビュー/実行ファイル/テスト起動/起動オプション/製作者情報 + EditGameForm はバージョン管理、VersionUpForm は更新内容)
  - 入力フィールドの幅を広げ、ジャンル選択 (CheckedListBox) や説明文入力欄も従来より広く

#### Fixed

- **ノート PC で `EditGameForm` / `AddGameForm` が縦に見切れる問題を解消 (#123)**: 上記 2 列化で根本解消。念のため Form に `AutoScroll = true` も設定し、それでも入りきらない環境では縦スクロールバーが出るように
- **`DataGridView` の行高をユーザーが変更できる問題を解消 (#123)**: ゲームタブ・ストアタブ等で行と行の境界をドラッグすると行高が変わってレイアウトが崩れる事故を防止。全 `DataGridView` に `AllowUserToResizeRows = false` を設定
  - 対象: `dgvGames` (GameSectionPanel), `dgvSections` (StoreSectionPanel / StoreSectionListForm), `dgvDevelopers` × 3 (Add/Edit/VersionUp)
  - `gridHistory` (BackupSectionPanel) は既に設定済みで対応不要
- **`DataGridView` の行ヘッダーを非表示化 (#123)**: 行ヘッダー自体を消すことで境界ドラッグの誤操作余地を物理的に排除。新規追加対象は `dgvSections` (StoreSectionListForm), `dgvDevelopers` (Add/Edit) の 3 件 (他は既に設定済み)。製作者情報の `dgvDevelopers` も「追加」「編集」「削除」ボタン経由の操作なので新行マーク不要

### [Manager v0.8.5] - 2026-05-10

#### Added

- **フォルダ削除失敗時の再試行 UI を追加 (#122)**: ゲーム削除や DB リセットでフォルダ削除に失敗した場合、従来は「警告 MessageBox 表示 → ユーザーが手動でなんとかする」だったが、専用ダイアログ `FolderDeletionFailureDialog` で「再試行」「諦める」を選べるように改善
  - 主因は **Launcher が起動中ゲームの実行ファイルを掴んでいる** ケース。ユーザーが Launcher を閉じてから「再試行」を押すだけで解消する
  - ダイアログには対象フォルダパス、エラー詳細 (Exception.Message)、対処手順 (Launcher を閉じてから再試行) を表示
  - `AcceptButton = btnRetry` で Enter で再試行 (再試行は何度押しても安全)、`CancelButton = btnGiveUp` で ESC で諦める
- **新規 `Services/FolderDeletionService.cs`**: フォルダ削除のリトライ機構を共通化 (5 回 × 200ms、`RestoreService.DeleteWithRetry` と同じパターン)。`IOException` / `UnauthorizedAccessException` のみリトライ対象、それ以外は throw
- **新規 `FolderDeletionFailureDialog.cs` + `.Designer.cs` + `.resx`**: 上記再試行 UI のカスタム Form

#### Changed

- **`SchemaManager.ResetDatabase()` の戻り値を `string` → `FolderDeletionService.Result` に変更**: 退避フォルダ削除の Path / Exception を呼び出し側に渡せるようにし、再試行時に同じ path を使えるように構造化 (#122)
- **`DatabaseManager.ResetDatabase()` の wrapper signature 更新**: 同様に `Result` を返す形に
- **`SettingsSectionPanel.btnResetDatabase_Click`**: 退避フォルダ削除失敗時に `FolderDeletionFailureDialog` を表示する再試行ループに変更。「諦める」を選んだ場合のみ警告 MessageBox を表示 (#122)
- **`GameSectionPanel.btnDeleteGame_Click`**: フォルダ削除を `ProcessingDialog` の外に出して再試行ループ化。`folderWarning` 文字列方式を廃止し、失敗時は `FolderDeletionFailureDialog` で対応 (#122)
- **ゲーム削除を rename rollback パターンに刷新**: リセットと同じ 3 フェーズ「(1) `games/{gameId}/` を `.pending-delete-{guid}/` に rename で退避 → (2) DB 削除 → (3) 退避フォルダ物理削除」に統一 (Codex P1 #122 への対応)。これにより:
  - フォルダ物理削除前に DB 削除が走るので、DB 削除失敗時 (SQLiteException 等) はフォルダを rename で戻して**何も変わらない状態にロールバック**でき、永続的データロストを排除
  - (1) 失敗時 (Launcher ロック中など) は再試行 UI、諦めたら全体中止で DB 無事
  - (3) 失敗時は DB 削除済みなのでゴミ退避フォルダだけ残るパターン (リセットと同じ)
- 旧実装の「フォルダ物理削除 → DB 削除」順は、DB 失敗時にフォルダだけ消えて戻せない問題があった (PR #117 以来の負債)。本変更で完全解消

#### Scope out (将来 Issue 候補)

- **ロックしてるプロセス特定 UI** (Restart Manager API 利用): 大規模で P/Invoke 必要、別 Issue として検討
- **ファイルログ機構**: Issue [#116](https://github.com/ken1208git/GCTonePrism/issues/116) に依存
- **「フォルダをエクスプローラで開く」ボタン**: ロック中はエクスプローラから消そうとしても同じく失敗するため、本質的解決にならず削除

### [Manager v0.8.4] - 2026-05-10

#### Fixed

- **データベースリセット時に `games/` フォルダも削除するよう実装を修正 (#119)**: `ResetDatabaseConfirmForm` の確認画面で「・gamesフォルダ内のすべてのファイルとフォルダ」を削除すると告知していたが、実装 (`SchemaManager.ResetDatabase()`) は `prism.db` 1 ファイルしか削除しておらず、確認画面と挙動が一致していなかった
  - `SchemaManager.ResetDatabase()` を **rename rollback 方式** で実装:
    1. `games/` を `games.pending-delete-{guid}/` に rename して退避（同一ボリューム rename は事実上 atomic）
    2. `prism.db` を削除
    3. 退避フォルダを物理削除
    4. `games/` を再作成 + DB 再構築
  - **rename rollback の意図** (Codex P1 指摘 #121 への対応): 物理削除順 (games → DB) でも、DB 削除でロック等で失敗した場合に「games 消えたまま + DB 古いレコード」という broken partial-reset 状態になる。rename ならフォルダ実体は退避先に残るので、DB 削除失敗時は rename を戻して「何も変わってない」状態にロールバックできる
  - `backups/` 等の隣接フォルダは触らない（復元用に残す）
  - 順序は **「(1) games rename → (2) DB 削除 → (3) games/ 再作成 + DB 再初期化 → (4) 退避フォルダ物理削除」** の 4 ステップ
  - `IOException` / `UnauthorizedAccessException` を捕捉してユーザー向けメッセージに変換。**失敗パターン別の状態**:
    - (1) games rename 失敗 → 何も変わらず throw（DB / games 共に無事）
    - (2) DB 削除失敗 → games を rename で復元してから throw（DB / games 共に無事）
    - (3) games/ 再作成 or DB 再初期化失敗 → 部分作成された games/ と prism.db を削除 + 退避を rename で games/ に戻してから throw（DB だけ消えた状態に着地、ユーザーは backup #96 から復元可能）。Codex P1 #121 への 4 度目の対応
    - (4) 退避フォルダの物理削除失敗 → 戻り値で警告メッセージを返す（DB / games 共に再構築済み + ゴミ退避フォルダだけ残る → ユーザーに「手動削除を」と通知）。rename はファイルロックを解除しないため Launcher が起動中ゲームの実行ファイルを掴んでいるとレアケースで起き得る
  - **`ResetDatabase()` の戻り値を `void` → `string` に変更** (Codex P2 #121): 上記 (4) 失敗時の警告を例外で投げると `ProcessingDialog` が `DialogResult.Abort` を返し、`SettingsSectionPanel.btnResetDatabase_Click` が non-OK で early return → `UpdateVersionInfo()` / `DatabaseReset?.Invoke()` がスキップされて UI が古いまま「失敗」と誤報告されていた。完全成功は null、警告ありは文字列を戻り値で返すよう変更し、呼び出し側は warning の有無に関わらず UI リフレッシュを実行 + warning があれば情報 MessageBox を出す形に
- **`ResetDatabaseConfirmForm` に「すべての展示PCの Launcher を終了してから実行」警告を追加**: DB ファイル + games フォルダ全部に対するロック競合を予防
- **`DeleteGameConfirmForm` に「該当ゲームを起動中の Launcher があれば閉じて」ヒントを追加**: フォルダ削除失敗の主原因を予防
- **`ResetDatabaseConfirmForm` の文言を部員向けに刷新 (#119)**: 「データベース内のすべての情報」「gamesフォルダ内のすべてのファイルとフォルダ」という技術用語ベースの表現を、PR #118 の `DeleteGameConfirmForm` と同じトーンで「すべてのゲーム情報・プレイ記録・アンケート回答」「Manager に登録されている全ゲームのファイル（games フォルダ全体）」に書き換え + 「部員の開発フォルダには影響しません。リセット前にバックアップ機能でスナップショット取得を推奨。」の補足を追加。**「games フォルダ全体」の括弧を残しているのは、未登録の手動配置ファイル（部員の実験フォルダ等）も削除対象であることを明示するため (Codex P2 #121)**

#### Added

- **`AddGameForm` に gameId 重複検出警告を追加 (#120)**: 何らかの原因で `games/{gameId}/` フォルダが残っている状態で同 gameId を新規追加しようとすると、最悪 Launcher が古い実行ファイルを起動する silent failure になり得たため、`btnOK_Click` のバリデーション直後に存在チェックを追加
  - 残骸を検出すると確認 MessageBox を表示し、「失いたくないデータがある場合は手動退避してから削除を」とユーザーに案内
  - 「OK」を押せばそのまま続行（古いフォルダはそのまま残る）、「キャンセル」を押すと追加処理自体を中止
  - 自動削除はしない（データ保護優先）

### [Manager v0.8.3] - 2026-05-10

#### Changed

- **ゲーム削除時に DB レコードに加えて `games/{game_id}/` フォルダも常に削除するよう変更 (#111)**: 従来は DB 削除のみだったため `games/{game_id}/` フォルダがディスクに残り続けていた。SPEC §2.2 にあった「DB のみ / DB + フォルダ」のオプション分岐は廃止し、削除実行 = DB + フォルダのセット削除に統一
  - 新規 `DeleteGameConfirmForm` を追加。何が消えるかを明確化するため、フォルダパスをダイアログ内に表示（`Consolas` モノスペース、ReadOnly）し、警告色で「ディスクから物理的に削除されます」と明示
  - フォルダが存在しない場合（手動削除済み等）は表示を「フォルダが見つかりません。DB のみ削除します」に切り替えて DB 削除のみ実行（無害）
  - `Enter` キーで誤って削除が走らないよう `AcceptButton` をキャンセルに割り当て
  - 削除処理は既存の `ProcessingDialog` (Marquee モード) 内で DB 削除に続けて `Directory.Delete(folder, true)` を実行
  - `IOException` (Launcher など他プロセスがロック中)・`UnauthorizedAccessException` を個別捕捉し、DB 削除は成功した上で「フォルダ削除に失敗、手動削除してください」の警告に切り替える非破壊運用
  - 関連実装: `Controls/GameSectionPanel.cs` の `btnDeleteGame_Click`、`PathManager.GetGameFolder(gameId)` を再利用

### [Manager v0.8.2] - 2026-05-10

#### Changed

- **`prism.db` のジャーナルモードを WAL → DELETE へ移行 (#103)**: 学校 SMB ファイルサーバー上で `prism.db` を共有する運用形態において、SQLite 公式が WAL モードの動作を保証外と明言している（`prism.db-shm` のメモリマップトファイルが SMB で整合性を保証されない）リスクを回避するため、`PRAGMA journal_mode=DELETE` に切り替え
  - `DatabaseConnection.OpenConnectionWithWalMode` を `OpenConnectionWithJournalMode` にリネームし、呼び出し側 7 ファイル（`SchemaManager.cs` + 6 リポジトリ）を更新
  - 接続文字列に `Busy Timeout=10000` を追加（System.Data.SQLite ライブラリ側のフォールバック）
  - 接続オープン時に `PRAGMA busy_timeout=10000` を実行し、書き込み競合時は SQLite 内部で最大 10 秒待機する「書き込み待機列」として動作させる

#### Operations

- **既存環境の移行手順（一度きり）**:
  1. 全 PC で Launcher / Manager を停止
  2. CLI で `tools\sqlite3\sqlite3.exe <prism.db のパス> "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;"`
  3. `prism.db-wal` / `prism.db-shm` が削除されたことを確認
  4. v0.8.2 の Manager と v0.5.15 以降の Launcher を全 PC に配布

### [Manager v0.8.1] - 2026-05-10

#### Fixed

- **surveys / play_records スキーマ drift を修正 (`MigrateV10ToV11`)**: SPEC v1.5.1 (2026-03-28) で `surveys`（JSON 形式 → ★評価+コメント）、`play_records`（累計方式 → イベントログ方式）に変更されたが、対応するマイグレーションが書かれていなかったため、`CREATE TABLE IF NOT EXISTS` の仕様により旧スキーマのテーブルが温存されていた。本マイグレーションで修正
  - 旧スキーマ判定: `surveys` は `submitted_at` 列、`play_records` は `play_count` 列の存在で検出
  - 行数 0 の場合のみ DROP & CREATE で新スキーマへ置換（データ保護）
  - 行数あり時は警告ログを出して bool false を返し、`MigrateV10ToV11` が呼び出し側に伝播。`CheckAndMigrateDatabase` は成功した migration のみ `currentVersion` を bump し、`SetDbVersion` には実際に達成した `currentVersion` を書き込むため `user_version` は 10 のまま保持される。これにより次回起動でも migration が再試行されつつ、Manager 自体は正常起動する（Codex P1 指摘 "Avoid marking DB v11 when drift migration is skipped" + "Avoid hard-failing startup on non-empty drift tables" の両方に対応）
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
