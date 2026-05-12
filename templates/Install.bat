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
REM   (2) echo arguments MUST NOT contain literal `(` or `)`. Use `[` `]` instead.
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

REM Quoted set form: path-derived values may contain cmd metachars (`&`, `^` 等)
REM e.g. if zip is extracted under `D:\R&D\`. Unquoted assignment would split the
REM line into multiple commands and abort. Quoted form stores the value literally.
set "SCRIPT_DIR=%~dp0"
set "FILES_DIR=%SCRIPT_DIR%files"
REM Exit code sentinel: failure paths goto :fail to set 1.
set "EXIT_CODE=0"

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
set "TEMP_DIALOG_OUT=%TEMP%\gctone_install_dialog_%RANDOM%.tmp"
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%SCRIPT_DIR%show_folder_dialog.ps1" > "%TEMP_DIALOG_OUT%"
set "DIALOG_EXIT=%ERRORLEVEL%"

if "%DIALOG_EXIT%"=="0" goto :dialog_ok
if "%DIALOG_EXIT%"=="2" goto :dialog_cancel
goto :dialog_fail

:dialog_ok
set /p INSTALL_PARENT=<"%TEMP_DIALOG_OUT%"
del "%TEMP_DIALOG_OUT%" 2>nul
if defined INSTALL_PARENT goto :dialog_done
echo.
echo [FAIL] PowerShell が exit 0 を返しましたが選択パスが取得できませんでした [想定外]。
echo        本シナリオが再現する場合は GitHub issue で報告してください。
goto :fail

:dialog_cancel
del "%TEMP_DIALOG_OUT%" 2>nul
echo.
echo [INFO] キャンセルされました。インストールを中止します。
goto :end

:dialog_fail
echo.
echo [FAIL] PowerShell の起動 / 実行に失敗しました [exit %DIALOG_EXIT%]。
echo        PowerShell が利用可能か、Execution Policy を確認してください:
echo          powershell.exe -NoProfile -Command "Get-ExecutionPolicy -List"
if not exist "%TEMP_DIALOG_OUT%" goto :dialog_fail_cleanup
echo.
echo PS stdout の内容:
type "%TEMP_DIALOG_OUT%"
:dialog_fail_cleanup
del "%TEMP_DIALOG_OUT%" 2>nul
goto :fail

:dialog_done

REM ---- Nest check ----
REM If the selected parent path ends in GCTonePrism (with or without trailing \),
REM we'd create <parent>\GCTonePrism\GCTonePrism\ which is nested. Warn + abort.
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
set /p OVERWRITE_CONFIRM=上書きしますか？ (Y/N):
if /i "%OVERWRITE_CONFIRM%"=="Y" goto :checkprocess
echo.
echo [INFO] インストールを中止しました。
goto :end

REM ---- Detect running Manager / Launcher; wait for user to close manually ----
REM findstr exit 0 = process found (= running), exit 1 = not found.
REM We do NOT auto-kill: user might have unsaved work. Phase 3 Updater will handle
REM controlled termination if needed.
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
REM robocopy: /E recursive incl. empty dirs, /XF excludes files, /XD excludes dirs,
REM /NFL /NDL /NJH /NJS /NC /NS /NP minimal output, /R:1 /W:1 retry conservative.
REM /XF and /XD match by *name* across the entire tree, so future components named
REM games/backups/etc. would be silently excluded. Currently safe because files/
REM never contains these names. Documented for future maintainers.
robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /XF prism.db /XD games backups responses logs /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
REM robocopy exit code is a bitfield, < 8 = success (0 no change, 1 files copied, etc.).
if errorlevel 8 goto :copy_failed
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
robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
if errorlevel 8 goto :copy_failed
goto :copy_shortcuts

:copy_failed
echo.
echo [FAIL] ファイルコピーに失敗しました [robocopy exit %ERRORLEVEL%]。
goto :fail

:copy_shortcuts
REM Copy parent-level shortcuts (Launcher.bat / Manager.bat) from zip root to
REM <parent>/ (= one level above GCTonePrism/). This places daily-use shortcuts
REM directly under the user's selected parent folder for easier discovery.
REM See SPEC §3.7.1.
copy /Y "%SCRIPT_DIR%Launcher.bat" "%INSTALL_PARENT_NO_TRAIL%\Launcher.bat" >nul
if errorlevel 1 goto :shortcut_failed
copy /Y "%SCRIPT_DIR%Manager.bat" "%INSTALL_PARENT_NO_TRAIL%\Manager.bat" >nul
if errorlevel 1 goto :shortcut_failed
goto :install_done

:shortcut_failed
echo.
echo [FAIL] ショートカット bat のコピーに失敗しました ["%INSTALL_PARENT_NO_TRAIL%"]。
echo        書き込み権限を確認してください。
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
set /p START_MANAGER=Manager を起動しますか？ (Y/N):
if /i not "%START_MANAGER%"=="Y" goto :end
echo.
echo [INFO] Manager を起動します...
REM Use parent-level Manager.bat (one above GCTonePrism/), placed by :copy_shortcuts above.
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
if not defined MANAGER_STARTED pause
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
