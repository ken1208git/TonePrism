@echo off
REM ============================================================================
REM Install.bat - GCTonePrism initial installer (#108 Phase 2)
REM
REM File format: cp932 (Shift-JIS) + CRLF at *staging time*.
REM   - templates/Install.bat (this source) is UTF-8 (no BOM) + CRLF for repo
REM     readability / cross-platform editing.
REM   - Release.ps1 Copy-Templates re-encodes to cp932 + CRLF for the zip.
REM   - Rationale: cmd.exe parses bat files in the SYSTEM codepage (cp932 on JP
REM     Windows). Bat-internal `chcp 65001` only switches the *console output*
REM     codepage, NOT the file-reading codepage. Long Japanese lines under
REM     UTF-8 source caused cmd parser to mistokenize byte sequences and emit
REM     cascading "is not recognized" errors. Staging as cp932 lets cmd parse
REM     natively. Cost: non-JP Windows (cp437/1252) would mojibake; out of scope
REM     for the school deployment target.
REM
REM Structural rules (still apply since they protect against cmd parser quirks):
REM   (1) Japanese echo MUST live at top-level only (not inside `if ... (...)` blocks).
REM   (2) echo / set /p prompt arguments MUST NOT contain literal `(` or `)`.
REM       Use `[` `]` instead. (set /p prompts go through the same parser as echo;
REM       even top-level placement isn't formally guaranteed by docs against
REM       future cmd version changes — keep the rule uniform for safety.)
REM   (3) PowerShell invocations MUST NOT inline long commands with Japanese in
REM       `set "PS_CMD=...日本語..."` form. Use external .ps1 + `-File`.
REM
REM Usage:
REM   Install.bat   -- double-click to run (target user: 部員 / 来場スタッフ)
REM
REM Flow:
REM   1. FolderBrowserDialog (via show_folder_dialog.ps1) selects parent folder.
REM   2. Nest check: abort if parent path ends in GCTonePrism.
REM   3. Existing check: <parent>\GCTonePrism\ exists?
REM        Yes -> Y/N warning. Y = overwrite preserving user data + manager-close wait.
REM        No  -> mkdir + copy files/.
REM   4. Manager-start Y/N prompt.
REM
REM Exit codes (for Phase 3 Updater integration):
REM   0  success or user cancel (dialog cancel / overwrite N / manager-start N).
REM   1  failure (files/ missing / nested path / mkdir fail / robocopy fail / PS launch fail).
REM ============================================================================

REM setlocal disabledelayedexpansion: delayed expansion is intentionally off so
REM FolderBrowserDialog paths containing `!` (e.g. D:\Backup!\) are preserved
REM as-is. No `!VAR!` references exist in this file.
setlocal disabledelayedexpansion

REM Quoted set form (`set "VAR=value"`) is required for *path-derived* values that
REM may contain cmd metachars (`&`, `^` 等) e.g. if zip is extracted under `D:\R&D\`.
REM Unquoted assignment would split the line into multiple commands and abort.
REM Quoted form stores the value literally.
REM
REM Note: numeric sentinels (MANAGER_FOUND / LAUNCHER_FOUND / MANAGER_STARTED /
REM EXIT_CODE 等) are intentionally NOT quoted below — they only hold ERRORLEVEL
REM (0/1) or `1` literals, so quoting adds noise without value. The quote rule
REM applies to path / user-input values, not to numeric flags. (シニアレビュー L1)
set "SCRIPT_DIR=%~dp0"
set "FILES_DIR=%SCRIPT_DIR%files"
REM Exit code sentinel: failure paths goto :fail to set 1.
REM (numeric per L1 rule — unquoted, matches :fail's `set EXIT_CODE=1`)
set EXIT_CODE=0

REM ---- files/ existence check (top-level, no Japanese echo inside block) ----
if exist "%FILES_DIR%" goto :files_ok
echo [FAIL] files/ ディレクトリが見つかりません: "%FILES_DIR%"
echo        zip を正しく展開してから Install.bat を実行してください。
goto :fail
:files_ok

echo ============================================================================
echo  GCTonePrism インストーラ
echo ============================================================================
echo.
echo インストール先の「親フォルダ」を選択してください。
echo  [選んだフォルダの下に GCTonePrism\ が作成されます]
echo.
echo 例: D:\Games を選ぶ → D:\Games\GCTonePrism\ にインストール
echo.

REM ---- Launch FolderBrowserDialog via show_folder_dialog.ps1 ----
REM PS exit code dispatch:
REM   0  = OK (selected path written to stdout)
REM   2  = user cancel (explicit [Environment]::Exit(2), distinct from PS error exit 1)
REM   other = PS launch failure (policy block / PS missing / .ps1 missing / etc.)
REM
REM Why a separate .ps1 (vs inline -Command "..."):
REM   cmd's bat parser cannot reliably tokenize a long `set "PS_CMD=...日本語..."`
REM   string. The Japanese byte sequence splits adjacent ASCII tokens like
REM   System.Windows.Forms, and PS receives malformed input.
REM 重要: INSTALL_PARENT は caller env から inherit する可能性があるため、dialog
REM 起動 *前* に明示的にクリア。`set /p` は input が空 (file が空) なら変数を
REM 更新しないため、初期化しないと前回値 / 環境値が leak して別パスへ install
REM するリスクあり (bot review P2 #4)。
set "INSTALL_PARENT="
REM tmp filename uses %RANDOM%%RANDOM% (30 bit, 1/1G collision) instead of bare
REM %RANDOM% (15 bit, 1/32K). Double-clicked installer is not designed for parallel
REM runs, but defends against the "user double-clicked twice, prior instance still
REM waiting on dialog" case where same tmp filename would let set /p pick up stale
REM path (シニアレビュー round 3 L4).
set "TEMP_DIALOG_OUT=%TEMP%\gctone_install_dialog_%RANDOM%%RANDOM%.tmp"
set "TEMP_DIALOG_ERR=%TEMP%\gctone_install_dialog_%RANDOM%%RANDOM%.err.tmp"
REM stdout / stderr を分離キャプチャ (シニアレビュー round 3 M2)。
REM 旧実装は stdout のみリダイレクト、stderr は親 cmd console に直流していた。
REM インタラクティブ実行では人間が画面で見られるが、Phase 3 Updater が
REM `cmd /c install.bat > log.txt 2>&1` で呼ぶ運用に入ったとき、stderr に書かれた
REM 実際の失敗理由 (show_folder_dialog.ps1 catch メッセージ等) は log に残る一方、
REM bat 内の `:dialog_fail` 表示と分離されて読みづらい。本実装では分離キャプチャで
REM `:dialog_fail` の表示に "PS stderr の内容" として明示的に含める。
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%SCRIPT_DIR%show_folder_dialog.ps1" > "%TEMP_DIALOG_OUT%" 2> "%TEMP_DIALOG_ERR%"
set "DIALOG_EXIT=%ERRORLEVEL%"

if "%DIALOG_EXIT%"=="0" goto :dialog_ok
if "%DIALOG_EXIT%"=="2" goto :dialog_cancel
goto :dialog_fail

:dialog_ok
REM NOTE on trailing whitespace (シニアレビュー round 3 L3):
REM   show_folder_dialog.ps1 の [Console]::Out.Write は trailing newline を付けないが、
REM   FolderBrowserDialog の SelectedPath プロパティ自体が末尾空白を含む path を返す
REM   ケース (theoretical) に対しては防御薄。実害は OS / .NET の SelectedPath
REM   normalize 仕様に依存するが、現状 .NET 4.x の SelectedPath は trailing space を
REM   付けない実装で確認済。問題化したら set /p 後に whitespace trim を入れること。
set /p INSTALL_PARENT=<"%TEMP_DIALOG_OUT%"
del "%TEMP_DIALOG_OUT%" 2>nul
del "%TEMP_DIALOG_ERR%" 2>nul
if defined INSTALL_PARENT goto :dialog_done
echo.
echo [FAIL] PowerShell が exit 0 を返しましたが選択パスが取得できませんでした [想定外]。
echo        本シナリオが再現する場合は GitHub issue で報告してください。
goto :fail

:dialog_cancel
del "%TEMP_DIALOG_OUT%" 2>nul
del "%TEMP_DIALOG_ERR%" 2>nul
echo.
echo [INFO] キャンセルされました。インストールを中止します。
goto :end

:dialog_fail
echo.
echo [FAIL] PowerShell の起動 / 実行に失敗しました [exit %DIALOG_EXIT%]。
echo        PowerShell が利用可能か、Execution Policy を確認してください:
echo          powershell.exe -NoProfile -Command "Get-ExecutionPolicy -List"
if not exist "%TEMP_DIALOG_ERR%" goto :dialog_fail_stdout
REM stderr (catch 経路の [Console]::Error.WriteLine 出力 / PS host 起動エラー等)
echo.
echo PS stderr の内容:
type "%TEMP_DIALOG_ERR%"
:dialog_fail_stdout
if not exist "%TEMP_DIALOG_OUT%" goto :dialog_fail_cleanup
echo.
echo PS stdout の内容:
type "%TEMP_DIALOG_OUT%"
:dialog_fail_cleanup
del "%TEMP_DIALOG_OUT%" 2>nul
del "%TEMP_DIALOG_ERR%" 2>nul
goto :fail

:dialog_done

REM ---- Nest check ----
REM If the selected parent path ends in GCTonePrism (with or without trailing \),
REM we'd create <parent>\GCTonePrism\GCTonePrism\ which is nested. Warn + abort.
REM
REM Edge cases for the leaf extraction:
REM   - Drive root `D:\`        -> strip trailing \ leaves `D:` ->
REM                                `%%~nxF` returns empty string -> leaf = "" ->
REM                                nest check passes (empty != "GCTonePrism") ->
REM                                INSTALL_TARGET = `D:\GCTonePrism` -> mkdir
REM                                may fail with permission (drive root often
REM                                requires admin), caught by :new_install
REM                                mkdir-failed branch. Acceptable degradation.
REM   - UNC root `\\server\share`-> leaf = "share" -> nest passes (unless share
REM                                is literally named GCTonePrism) ->
REM                                INSTALL_TARGET = `\\server\share\GCTonePrism`.
REM                                Untested in E2E, see PR Known untested list.
set "INSTALL_PARENT_NO_TRAIL=%INSTALL_PARENT%"
if "%INSTALL_PARENT_NO_TRAIL:~-1%"=="\" set "INSTALL_PARENT_NO_TRAIL=%INSTALL_PARENT_NO_TRAIL:~0,-1%"
for %%F in ("%INSTALL_PARENT_NO_TRAIL%") do set "INSTALL_PARENT_LEAF=%%~nxF"

if /i not "%INSTALL_PARENT_LEAF%"=="GCTonePrism" goto :not_nested
echo.
echo [FAIL] 選択されたフォルダ自体が GCTonePrism です:
echo        "%INSTALL_PARENT%"
echo.
echo        「親フォルダ」を選んでください [その下に GCTonePrism\ が作成されます]。
echo        例: D:\Games\GCTonePrism\ にインストールしたい場合は D:\Games を選択。
goto :fail
:not_nested

set "INSTALL_TARGET=%INSTALL_PARENT_NO_TRAIL%\GCTonePrism"
echo.
echo インストール先: "%INSTALL_TARGET%"
echo.

REM ---- Existing install check: dispatch to top-level labels ----
if exist "%INSTALL_TARGET%\" goto :existing_install
goto :new_install

:existing_install
echo ============================================================================
echo  [警告] 既存インストールを検出しました
echo ============================================================================
echo.
echo  通常、アップデートは Manager UI から行うのを推奨します
echo  [Phase 4 実装後、現在は未実装]。
echo.
echo  Manager が壊れて起動できない / クリーンインストールしたい場合のみ Y を押してください。
echo  Y を押した場合でも以下のゲームデータは維持されます:
echo    - prism.db [ゲーム情報 DB]
echo    - games\        [ゲーム実体]
echo    - backups\      [バックアップ]
echo    - responses\    [アンケート回答]
echo    - logs\         [ログ]
echo.
REM set /p preserves the previous value when user just hits Enter. Initialize to
REM empty so we always evaluate the latest input (defends against future change
REM that might set OVERWRITE_CONFIRM upstream).
set "OVERWRITE_CONFIRM="
set /p OVERWRITE_CONFIRM=上書きしますか？ [Y/N]:
REM Accept y / Y / yes / Yes / YES (Release.ps1 の upload prompt と寛容度を揃える)
if /i "%OVERWRITE_CONFIRM%"=="Y"   goto :checkprocess
if /i "%OVERWRITE_CONFIRM%"=="YES" goto :checkprocess
echo.
echo [INFO] インストールを中止しました。
goto :end

REM ---- Detect running Manager / Launcher; wait for user to close manually ----
REM findstr exit 0 = process found (= running), exit 1 = not found.
REM We do NOT auto-kill: user might have unsaved work. Phase 3 Updater will handle
REM controlled termination if needed.
REM
REM NOTE on wait loop termination (シニアレビュー L4):
REM   :wait_close is an UNBOUNDED loop with no timeout — if the user cannot close
REM   Manager / Launcher (hung process, locked by another session, permission issue),
REM   the only exit path is Ctrl+C. Ctrl+C aborts the bat without running :end, so
REM   %EXIT_CODE% is not set and the cmd exit code is undefined from the caller's
REM   perspective. This is INTENTIONAL for the current Phase 2 scope:
REM     - Phase 2 is "human double-clicks Install.bat", and a stuck process is
REM       almost always operator error that's better surfaced as "the installer
REM       seems stuck, what's going on?" than silently aborted.
REM     - Bounded loop with auto-fail would mask the real cause (e.g. operator
REM       forgot to close a tab) behind a generic "installer failed" message.
REM     - Phase 3 Updater is the proper place for forced termination (it owns the
REM       Manager process lifecycle and can decide to kill safely).
REM   If the loop becomes a problem in practice (e.g. unattended re-install flows
REM   where Ctrl+C isn't available), add a max-iterations counter here and goto :fail.
:checkprocess
tasklist /FI "IMAGENAME eq GCTonePrism_Manager.exe" 2>nul | findstr /I "GCTonePrism_Manager.exe" >nul
set MANAGER_FOUND=%ERRORLEVEL%
tasklist /FI "IMAGENAME eq GCTonePrism_Launcher.exe" 2>nul | findstr /I "GCTonePrism_Launcher.exe" >nul
set LAUNCHER_FOUND=%ERRORLEVEL%
if "%MANAGER_FOUND%"=="0" goto :wait_close
if "%LAUNCHER_FOUND%"=="0" goto :wait_close
goto :do_overwrite

:wait_close
echo.
if "%MANAGER_FOUND%"=="0" echo [警告] GCTonePrism_Manager.exe が稼働中です。
if "%LAUNCHER_FOUND%"=="0" echo [警告] GCTonePrism_Launcher.exe が稼働中です。
echo.
echo  Manager / Launcher を全て閉じてから何かキーを押してください...
pause >nul
goto :checkprocess

:do_overwrite
echo.
echo [INFO] 上書きインストールを開始します...
goto :migrate_legacy_manager

REM ---- v0.2.0 → v0.3.0+ 旧構造 migration (シニアレビュー / ディレクトリ rename 整理対応) ----
REM v0.2.0 までは `<install>/GCTonePrism_Manager/` / `<install>/GCTonePrism_Launcher/` 配置。
REM v0.3.0 から `<install>/Manager/` / `<install>/Launcher/` + `<install>/Companions/Updater/` に変更。
REM 旧構造を検出したらリネームしてからロボコピーする (`move` で dir 名のみ変更 = 中身そのまま carry-over)。
REM 一度移行すれば以降はリネーム不要 (旧 dir 不在で skip)。
REM
REM Destination-exists guard:
REM   Windows の `move srcdir dstdir` は dst が既存ディレクトリの場合、エラーで失敗するのではなく
REM   src を dst の中にネスト移動 (`dst\src\` を作る) する挙動を取るバージョン / シェル組合せがある。
REM   errorlevel 0 で済んでしまうと `:migrate_failed` には飛ばず、`<install>/Manager/GCTonePrism_Manager/`
REM   という壊れたネスト構造ができたまま `:do_robocopy` が走り、ユーザーに見えにくいゴミが堆積する
REM   silent failure path だった。事前 `if exist <new>\` チェックで両 dir 並存を検出し、
REM   `:migrate_conflict` で user に手動判断を促す形に変更。並存ケースは theoretical だが、
REM   v0.3.0 install + 過去 zip バックアップ復元 / partial install の再試行で発生し得る。
REM
REM top-level goto pattern: 日本語 echo を if-block 内に置くと cmd parser cascade するため
REM (構造規約 1)、各 step を独立ラベルにしている。
:migrate_legacy_manager
if not exist "%INSTALL_TARGET%\GCTonePrism_Manager\" goto :migrate_legacy_launcher
if exist "%INSTALL_TARGET%\Manager\" goto :migrate_conflict_manager
echo [INFO] 旧構造 (v0.2.0) 検出: GCTonePrism_Manager\ → Manager\ に移行
move "%INSTALL_TARGET%\GCTonePrism_Manager" "%INSTALL_TARGET%\Manager" >nul
if errorlevel 1 goto :migrate_failed_manager
REM Sentinel: Manager 移行成功時にセット。:migrate_failed_launcher が「Manager 移行済」を
REM 案内する条件として使う (Manager 不在で skip された場合は未 set なので誤情報を避ける)。
set MANAGER_MIGRATED=1
:migrate_legacy_launcher
if not exist "%INSTALL_TARGET%\GCTonePrism_Launcher\" goto :overwrite_set_mode
if exist "%INSTALL_TARGET%\Launcher\" goto :migrate_conflict_launcher
echo [INFO] 旧構造 (v0.2.0) 検出: GCTonePrism_Launcher\ → Launcher\ に移行
move "%INSTALL_TARGET%\GCTonePrism_Launcher" "%INSTALL_TARGET%\Launcher" >nul
if errorlevel 1 goto :migrate_failed_launcher
REM Sentinel: Launcher 移行成功時にセット。:shortcut_failed が「Launcher 移行済」を
REM 案内する条件として使う (:migrate_failed_launcher / :shortcut_failed 共通の状態案内)。
set LAUNCHER_MIGRATED=1
goto :overwrite_set_mode

:migrate_conflict_manager
echo.
echo [FAIL] 旧 dir と新 dir が両方存在しています:
echo          "%INSTALL_TARGET%\GCTonePrism_Manager"  [旧、v0.2.0 配置]
echo          "%INSTALL_TARGET%\Manager"              [新、v0.3.0+ 配置]
echo.
echo        Install.bat は自動でどちらを残すか判断できません。手動でどちらか一方を削除してから再実行:
echo          - 推奨: 新側を残す → 旧 "%INSTALL_TARGET%\GCTonePrism_Manager" を削除
echo            [通常はこちら。Install.bat 再実行で v0.3.0+ binary が上書きインストールされます]
echo          - 旧側を残す → 新 "%INSTALL_TARGET%\Manager" を削除 [v0.2.0 にダウングレードしたい場合のみ]
echo            [Install.bat 再実行で旧 dir が "Manager" にリネーム → 同じ v0.3.0+ binary で robocopy 上書き、
echo             結局 v0.3.0+ binary が入るので、本当に v0.2.0 のまま運用したい場合は zip も v0.2.0 を使うこと]
echo.
echo        どちらを選んでもユーザーデータ [prism.db / games / backups / responses / logs] は失われません。
echo        これらは "%INSTALL_TARGET%\" 直下にあり Manager dir の外なので、Manager dir の削除と無関係に維持されます。
goto :fail

:migrate_conflict_launcher
echo.
echo [FAIL] 旧 dir と新 dir が両方存在しています:
echo          "%INSTALL_TARGET%\GCTonePrism_Launcher"  [旧、v0.2.0 配置]
echo          "%INSTALL_TARGET%\Launcher"              [新、v0.3.0+ 配置]
echo.
echo        Install.bat は自動でどちらを残すか判断できません。手動でどちらか一方を削除してから再実行:
echo          - 推奨: 新側を残す → 旧 "%INSTALL_TARGET%\GCTonePrism_Launcher" を削除
echo            [通常はこちら。Install.bat 再実行で v0.3.0+ binary が上書きインストールされます]
echo          - 旧側を残す → 新 "%INSTALL_TARGET%\Launcher" を削除 [v0.2.0 にダウングレードしたい場合のみ]
echo            [Install.bat 再実行で旧 dir が "Launcher" にリネーム → 同じ v0.3.0+ binary で robocopy 上書き、
echo             結局 v0.3.0+ binary が入るので、本当に v0.2.0 のまま運用したい場合は zip も v0.2.0 を使うこと]
echo.
echo        どちらを選んでもユーザーデータ [prism.db / games / backups / responses / logs] は失われません。
echo        これらは "%INSTALL_TARGET%\" 直下にあり Launcher dir の外なので、Launcher dir の削除と無関係に維持されます。
goto :fail

REM :migrate_failed_manager と :migrate_failed_launcher は独立ラベル。
REM 旧 `:migrate_failed` 共通ラベルだと、Manager 移行成功 + Launcher 移行失敗のケースで
REM 「Manager の rename が必要」と案内してしまい (実際は完了済)、user が「移動元が無い」と
REM 混乱する path があった。失敗側だけの手動 rename 手順を案内する形に分離。
:migrate_failed_manager
echo.
echo [FAIL] 旧構造 (v0.2.0) → 新構造への Manager フォルダ移行に失敗しました。
echo        フォルダロック / 書き込み権限を確認してください。
echo        手動で以下のリネームを行えば回避可能:
echo          "%INSTALL_TARGET%\GCTonePrism_Manager"  → "%INSTALL_TARGET%\Manager"
echo        Launcher 側は本実行ではまだ移行を試みていないので、現状維持で OK。
echo        Install.bat 再実行で Launcher 側の移行が走ります。
goto :fail

:migrate_failed_launcher
echo.
echo [FAIL] 旧構造 (v0.2.0) → 新構造への Launcher フォルダ移行に失敗しました。
REM Manager 移行は skip / 成功 / fail (= 別経路) の 3 通り。fail はここに来る前に止まるので、
REM 残るは skip (Manager 不在) と 成功。sentinel で「成功時のみ案内」する形に絞る。
if defined MANAGER_MIGRATED echo        Manager 側はすでに移行済 ["%INSTALL_TARGET%\Manager" が新版]、Launcher 側のみ rename 失敗。
echo        フォルダロック / 書き込み権限を確認してください。
echo        手動で以下のリネームを行えば回避可能:
echo          "%INSTALL_TARGET%\GCTonePrism_Launcher" → "%INSTALL_TARGET%\Launcher"
goto :fail

:overwrite_set_mode
REM Overwrite 経路の終端: INSTALL_MODE sentinel を立てて :copy_shortcuts に合流。
REM Both overwrite and new_install paths go through :copy_shortcuts first so that
REM shortcut bat updates happen BEFORE robocopy starts.
REM   旧 flow: migration / mkdir → robocopy → copy_shortcuts → install_done
REM   新 flow: migration / mkdir → copy_shortcuts → robocopy → install_done
REM 旧 flow では robocopy の partial failure (Ctrl+C / OS reboot / 権限エラー等で中断)
REM 時に shortcut bat が旧 path (`%~dp0GCTonePrism\GCTonePrism_Launcher\...`) のままで
REM 実体は新 dir `<install>/Launcher/` に既に移動済 → user が Launcher.bat ダブルクリック
REM で「ファイルが見つかりません」になる窓があった。shortcut copy を先に実施することで
REM 「中断時も shortcut bat は新 path を指す」状態を保証し、Install.bat 再実行で robocopy
REM リトライ → 復旧、の経路を整合的にする。
set "INSTALL_MODE=overwrite"
goto :copy_shortcuts

:new_install
echo [INFO] 新規インストールを開始します...
mkdir "%INSTALL_TARGET%" 2>nul
if exist "%INSTALL_TARGET%\" goto :new_mkdir_ok
echo.
echo [FAIL] インストール先フォルダを作成できませんでした: "%INSTALL_TARGET%"
echo        書き込み権限を確認してください。
goto :fail
:new_mkdir_ok
set "INSTALL_MODE=new"
goto :copy_shortcuts

:copy_shortcuts
REM Copy parent-level shortcuts (Launcher.bat / Manager.bat) from zip root to
REM <parent>/ (= one level above GCTonePrism/). This places daily-use shortcuts
REM directly under the user's selected parent folder for easier discovery.
REM See SPEC §3.7.1.
REM
REM Order: shortcut copy is performed BEFORE robocopy in both overwrite and
REM new_install paths, so that shortcut bat always points to the current install
REM structure even if robocopy is interrupted partway through.
copy /Y "%SCRIPT_DIR%Launcher.bat" "%INSTALL_PARENT_NO_TRAIL%\Launcher.bat" >nul
if errorlevel 1 goto :shortcut_failed
copy /Y "%SCRIPT_DIR%Manager.bat" "%INSTALL_PARENT_NO_TRAIL%\Manager.bat" >nul
if errorlevel 1 goto :shortcut_failed
goto :do_robocopy_dispatch

:do_robocopy_dispatch
REM ============================================================================
REM USER DATA PROTECTION — read this carefully before modifying robocopy flags.
REM ============================================================================
REM The PRIMARY protection mechanism is robocopy's DEFAULT non-mirror behavior:
REM   robocopy without /MIR does NOT delete destination files / directories
REM   that are absent from source. So user-generated data in INSTALL_TARGET
REM   (prism.db / games/ / backups/ / responses/ / logs/) is left untouched
REM   simply because those names don't exist in the source FILES_DIR.
REM
REM `/XF prism.db /XD games backups responses logs` is DEFENSE-IN-DEPTH only:
REM   it guards against a hypothetical future regression where someone
REM   accidentally adds a `prism.db` placeholder or a `games/` template into
REM   `files/` (which would then overwrite real user data on upgrade install).
REM   If today's FILES_DIR has no such names, /XF and /XD are effectively no-ops
REM   — verified by Assert-ExpectedFiles in Release.ps1.
REM
REM ⚠ DO NOT add /MIR. /MIR enables "delete dest files not in source" which
REM   would IMMEDIATELY destroy all user data (prism.db, games/, etc.). Neither
REM   /XF nor /XD prevents /MIR's purge. If you ever need a mirror-style sync,
REM   the protection list must be reimplemented as `/XO` + manual pre-copy
REM   snapshot of user data, AND /XF /XD must be kept in strict sync with the
REM   SPEC §3.7.3 user-data list. Today: just don't add /MIR.
REM
REM Other flags: /E recursive incl. empty dirs, /NFL /NDL /NJH /NJS /NC /NS /NP
REM minimal output, /R:1 /W:1 retry conservative.
REM
REM Note on /XF /XD name-matching: they match by *name* across the entire tree,
REM so future components containing a `games/` or `backups/` subfolder would be
REM silently excluded. Safe today (no such names in files/) but worth flagging
REM if a future component adds one.
REM ============================================================================
if "%INSTALL_MODE%"=="overwrite" goto :do_robocopy_overwrite
goto :do_robocopy_new

:do_robocopy_overwrite
robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /XF prism.db /XD games backups responses logs /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
REM robocopy exit code is a bitfield, < 8 = success (0 no change, 1 files copied, etc.).
if errorlevel 8 goto :copy_failed
goto :install_done

:do_robocopy_new
robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
if errorlevel 8 goto :copy_failed
goto :install_done

:copy_failed
echo.
echo [FAIL] ファイルコピーに失敗しました [robocopy exit %ERRORLEVEL%]。
echo        Install.bat 再実行で復旧可能です [shortcut bat はすでに新版で配置済み]。
goto :fail

:shortcut_failed
echo.
echo [FAIL] ショートカット bat のコピーに失敗しました ["%INSTALL_PARENT_NO_TRAIL%"]。
echo        書き込み権限を確認してください。
REM Migration が走った場合は user に「dir rename は完了済」を案内 (:migrate_failed_launcher と
REM 同パターン、:copy_failed の「shortcut bat はすでに新版」hint との対称性)。user が
REM 「shortcut が壊れたから手動で旧 dir 名に戻そう」と誤対処して migration を巻き戻す path
REM を防ぐ。両 sentinel (Manager/Launcher) が立つのは「両方の rename 成功時」、片方のみは
REM 「片方 skip (= 元から不在) + もう片方 rename 成功」のケース。
if defined MANAGER_MIGRATED goto :shortcut_failed_with_migration_note
if defined LAUNCHER_MIGRATED goto :shortcut_failed_with_migration_note
goto :fail

:shortcut_failed_with_migration_note
echo.
echo  [注意] 旧構造 (v0.2.0) → 新構造への dir rename はすでに完了しています:
REM 空白は echo の前に 1 個固定 (round 6 L5: VAR 名長さに依存した手動 align は将来 sentinel 追加時に崩れる)
if defined MANAGER_MIGRATED echo          "%INSTALL_TARGET%\Manager"   [旧 GCTonePrism_Manager から rename 済]
if defined LAUNCHER_MIGRATED echo          "%INSTALL_TARGET%\Launcher"  [旧 GCTonePrism_Launcher から rename 済]
echo         旧 dir 名 ["%INSTALL_TARGET%\GCTonePrism_*"] に戻さないでください。
echo         書き込み権限を解消してから Install.bat を再実行すれば、shortcut bat だけ
echo         再書き込みされて続行可能です [migration は冪等、再走しても影響なし]。
echo.
echo  [警告] Install.bat 再実行までは、"%INSTALL_PARENT_NO_TRAIL%\Launcher.bat" /
echo         "%INSTALL_PARENT_NO_TRAIL%\Manager.bat" をダブルクリックしないでください。
echo         2 段階 copy で 1 段目成功 + 2 段目失敗の場合、片方が新 path / もう片方が
echo         旧 path (壊れた状態) の不揃いになっている可能性があります。再実行で
echo         両方とも新版に上書きされます。
goto :fail

:install_done

echo.
echo ============================================================================
echo  インストール完了: "%INSTALL_TARGET%"
echo ============================================================================
echo.
echo  日常使い ["%INSTALL_PARENT_NO_TRAIL%"\ 直下のショートカット]:
echo    Launcher.bat   [来場者用 Launcher 起動]
echo    Manager.bat    [運営用 Manager 起動]
echo.

REM ---- Manager start Y/N (top-level, no Japanese echo inside block) ----
REM set /p empty-Enter retains previous value. Initialize to defend.
set "START_MANAGER="
set /p START_MANAGER=Manager を起動しますか？ [Y/N]:
REM Accept y / Y / yes / Yes / YES (Release.ps1 の upload prompt と寛容度を揃える)
if /i "%START_MANAGER%"=="Y"   goto :do_start_manager
if /i "%START_MANAGER%"=="YES" goto :do_start_manager
goto :end
:do_start_manager
echo.
echo [INFO] Manager を起動します...
REM Launch via parent-level Manager.bat (placed by :copy_shortcuts above), to keep
REM the SPEC §3.7.5 "日常起動は Manager.bat 経由" convention consistent across all
REM startup paths (シニアレビュー round 3 M3). Earlier intermediate-cmd residual
REM problem is now handled by Manager.bat's own terminal `exit` (see templates/
REM Manager.bat docstring), so going through the wrapper no longer leaves a stale
REM cmd window. This keeps the future ability to add working-dir setup / env
REM injection / log redirection inside Manager.bat without missing this code path.
start "" "%INSTALL_PARENT_NO_TRAIL%\Manager.bat"
REM Suppress final pause when Manager started (avoid stacking Install.bat 'press
REM any key' prompt on top of Manager UI).
set MANAGER_STARTED=1
goto :end

:fail
set EXIT_CODE=1
goto :end

:end
echo.
REM Final pause (only when Manager not auto-started). cmd default の英語メッセージ
REM "Press any key to continue . . ." を避けて日本語に統一 (シニアレビュー M4)。
if not defined MANAGER_STARTED echo  何かキーを押して終了します...
if not defined MANAGER_STARTED pause >nul
REM `exit` (not `exit /b`) forces cmd process termination so the double-clicked
REM Install.bat window doesn't leave a residual empty prompt. With `exit /b` the
REM bat returns to caller; for `cmd /c install.bat` (the typical Explorer
REM double-click invocation) cmd exits afterwards, but some terminals / cmd-host
REM combinations keep an interactive prompt instead. `exit %CODE%` exits cmd
REM directly with the given code so the window closes uniformly.
REM
REM Trade-off: if a future caller uses `call install.bat` from another bat,
REM `exit` would also terminate the caller. For Phase 3 Updater integration,
REM invoke via `Process.Start("cmd", "/c install.bat ...")` instead of `call`.
endlocal & exit %EXIT_CODE%
