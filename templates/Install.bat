@echo off
REM ============================================================================
REM Install.bat - GCTonePrism initial installer (#108 Phase 2)
REM
REM File format: UTF-8 (no BOM) + CRLF. Enforced by:
REM   - .gitattributes (*.bat eol=crlf) at git checkout
REM   - Release.ps1 Copy-Templates normalization at staging
REM
REM Encoding rule:
REM   - Above chcp 65001: ASCII only (codepage not switched yet, Japanese mojibakes)
REM   - Below chcp 65001: Japanese OK in echo arguments
REM   - REM comments: keep ASCII English even below chcp boundary. cmd's parser
REM     can misinterpret Japanese byte sequences combined with backticks, parens,
REM     ellipses etc. in REM lines and emit cascading "is not recognized" errors
REM     for subsequent lines. Keeping REM ASCII is the safest convention.
REM   - BOM is NG (some cmd.exe builds break @echo off on BOM, see PR #140).
REM
REM Structural rules:
REM   (1) Japanese echo MUST live at top-level only (not inside `if ... (...)` blocks).
REM       cmd parses block bodies at parse-time and mistokenizes Japanese byte
REM       sequences. Use linear `if not cond goto :label / [...echo...] / goto :fail`
REM       pattern instead.
REM   (2) echo arguments MUST NOT contain literal `(` or `)`. Use `[` `]` or
REM       Japanese brackets `「」` instead. `(` is interpreted as block-open by
REM       cmd even when escaped with `^(`.
REM   (3) PowerShell invocations MUST NOT inline long commands with Japanese in
REM       `set "PS_CMD=...日本語..."` form. cmd's line tokenizer cannot handle
REM       this and PS receives malformed command. Move PS code to a separate
REM       .ps1 file and invoke with `powershell.exe -File ...ps1`.
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

REM ---- Codepage switch (same pattern as Release.bat, established in PR #140) ----
for /f "tokens=2 delims=:" %%a in ('chcp') do set ORIGINAL_CODEPAGE=%%a
if defined ORIGINAL_CODEPAGE set ORIGINAL_CODEPAGE=%ORIGINAL_CODEPAGE: =%
if defined ORIGINAL_CODEPAGE (
    echo %ORIGINAL_CODEPAGE%| findstr /R "^[0-9][0-9]*$" >nul || set ORIGINAL_CODEPAGE=
)
if defined ORIGINAL_CODEPAGE (
    chcp 65001 >nul
) else (
    echo [WARN] Failed to capture codepage, Japanese output may be garbled.
)

REM ==== ASCII boundary (above: ASCII only, below: Japanese OK in echo) ====

set SCRIPT_DIR=%~dp0
set FILES_DIR=%SCRIPT_DIR%files
REM Exit code sentinel: failure paths goto :fail to set 1.
set EXIT_CODE=0

REM ---- files/ existence check (top-level, no Japanese echo inside block) ----
if exist "%FILES_DIR%" goto :files_ok
echo [FAIL] files/ ディレクトリが見つかりません: %FILES_DIR%
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
set "TEMP_DIALOG_OUT=%TEMP%\gctone_install_dialog_%RANDOM%.tmp"
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0show_folder_dialog.ps1" > "%TEMP_DIALOG_OUT%"
set DIALOG_EXIT=%ERRORLEVEL%

if %DIALOG_EXIT% EQU 0 goto :dialog_ok
if %DIALOG_EXIT% EQU 2 goto :dialog_cancel
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
echo        %INSTALL_PARENT%
echo.
echo        「親フォルダ」を選んでください [その下に GCTonePrism\ が作成されます]。
echo        例: D:\Games\GCTonePrism\ にインストールしたい場合は D:\Games を選択。
goto :fail
:not_nested

set "INSTALL_TARGET=%INSTALL_PARENT_NO_TRAIL%\GCTonePrism"
echo.
echo インストール先: %INSTALL_TARGET%
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
if not errorlevel 8 goto :install_done
echo.
echo [FAIL] ファイルコピーに失敗しました [robocopy exit %ERRORLEVEL%]。
goto :fail

:new_install
echo [INFO] 新規インストールを開始します...
mkdir "%INSTALL_TARGET%" 2>nul
if exist "%INSTALL_TARGET%\" goto :new_mkdir_ok
echo.
echo [FAIL] インストール先フォルダを作成できませんでした: %INSTALL_TARGET%
echo        書き込み権限を確認してください。
goto :fail
:new_mkdir_ok
robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
if not errorlevel 8 goto :install_done
echo.
echo [FAIL] ファイルコピーに失敗しました [robocopy exit %ERRORLEVEL%]。
goto :fail

:install_done

echo.
echo ============================================================================
echo  インストール完了: %INSTALL_TARGET%
echo ============================================================================
echo.
echo  日常使い:
echo    %INSTALL_TARGET%\Launcher.bat   [来場者用 Launcher 起動]
echo    %INSTALL_TARGET%\Manager.bat    [運営用 Manager 起動]
echo.

REM ---- Manager start Y/N (top-level, no Japanese echo inside block) ----
REM set /p empty-Enter retains previous value. Initialize to defend.
set "START_MANAGER="
set /p START_MANAGER=Manager を起動しますか？ (Y/N):
if /i not "%START_MANAGER%"=="Y" goto :end
echo.
echo [INFO] Manager を起動します...
start "" "%INSTALL_TARGET%\Manager.bat"
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
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
endlocal & exit /b %EXIT_CODE%
