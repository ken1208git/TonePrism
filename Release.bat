@echo off
REM ============================================================================
REM Release.bat - Release.ps1 wrapper for double-click / shortcut use.
REM
REM Usage:
REM   .\Release.bat                  : full release using CHANGELOG.md head Bundle entry
REM   .\Release.bat -SkipUpload      : build zip only, skip GitHub upload
REM   .\Release.bat -DryRun          : build verification only (auto-implies -SkipUpload)
REM   .\Release.bat -Force           : allow uncommitted changes
REM   .\Release.bat -NoPause         : skip pause on exit (for CI / automation)
REM
REM Arguments other than `-NoPause` are forwarded verbatim to Release.ps1.
REM The Bundle version is auto-detected from CHANGELOG.md (-Version optional).
REM See `Get-Help .\Release.ps1 -Detailed` for Release.ps1 docstring.
REM
REM File format: UTF-8 (no BOM) + CRLF. Comments / echo above `chcp 65001` MUST
REM stay ASCII (chcp switch below defines the ASCII / UTF-8 boundary).
REM
REM Detailed cmd.exe compatibility notes (BOM, chcp 3-way branch, delayed
REM expansion `!` side effect, top-level goto for multi-byte echo, etc.):
REM   See SPECIFICATION.md section 3.7.9 (Release.bat cmd.exe compatibility notes)
REM ============================================================================

REM enabledelayedexpansion is required for `!FORWARDED_ARGS!` concatenation in
REM the parseargs loop below. `!` in arg values is consumed as delayed-expansion
REM token (current pass-through args contain none, future args see SPEC 3.7.9.3).
setlocal enabledelayedexpansion

REM ---- UTF-8 codepage switch + restore-on-exit (3-way branch, SPEC 3.7.9.2) ----
REM   captured + numeric  -> chcp 65001, restore on exit
REM   captured + invalid  -> SKIP (corrupted format)
REM   not captured        -> SKIP (redirect filter / findstr unavailable / etc.)
REM Skip = safe default (codepage 65001 leak only happens if switch succeeded).
for /f "tokens=2 delims=:" %%a in ('chcp') do set ORIGINAL_CODEPAGE=%%a
if defined ORIGINAL_CODEPAGE set ORIGINAL_CODEPAGE=%ORIGINAL_CODEPAGE: =%
if defined ORIGINAL_CODEPAGE (
    echo %ORIGINAL_CODEPAGE%| findstr /R "^[0-9][0-9]*$" >nul || set ORIGINAL_CODEPAGE=
)
if defined ORIGINAL_CODEPAGE (
    chcp 65001 >nul
) else (
    echo [WARN] Failed to capture codepage from 'chcp' output.
    echo [WARN] Skipping UTF-8 codepage switch ^(Japanese output may be garbled^).
)

REM ==== ASCII boundary (only when chcp 65001 succeeded above) ====
REM   chcp 65001 succeeded -> UTF-8 console below (Japanese safe)
REM   skip path            -> codepage unchanged, see SPEC 3.7.9.2

set SCRIPT_DIR=%~dp0

REM Parse args: strip -NoPause, forward the rest to PowerShell verbatim
set NO_PAUSE=0
set FORWARDED_ARGS=
:parseargs
if "%~1"=="" goto :runps
if /i "%~1"=="-NoPause" (
    set NO_PAUSE=1
    shift
    goto :parseargs
)
REM Quote args to protect those containing spaces (paths etc.)
REM Skip leading space on first append, hence the `if defined` branch
if defined FORWARDED_ARGS (
    set FORWARDED_ARGS=!FORWARDED_ARGS! "%~1"
) else (
    set FORWARDED_ARGS="%~1"
)
shift
goto :parseargs

:runps
REM Re-emit ASCII warning if chcp switch was skipped (Japanese echo below may garble).
if not defined ORIGINAL_CODEPAGE (
    echo [WARN] Codepage switch was skipped; the following Japanese output may be garbled.
)
echo Release.bat: 引数 = %FORWARDED_ARGS%
echo Release.bat: NoPause = %NO_PAUSE%
echo.

powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Release.ps1" %FORWARDED_ARGS%

set EXIT_CODE=%ERRORLEVEL%
echo.
REM Exit code dispatch via top-level goto labels (multi-byte mis-tokenize avoidance, SPEC 3.7.9.4)
REM Codes (matches Release.ps1): 0 = success, 1 = failure, 2 = tag conflict skip, 3 = N answer skip
if %EXIT_CODE% EQU 0 goto :ec_success
if %EXIT_CODE% EQU 2 goto :ec_skip_conflict
if %EXIT_CODE% EQU 3 goto :ec_skip_n
goto :ec_fail
:ec_success
echo Release.bat: 正常終了
goto :ec_done
:ec_skip_conflict
echo Release.bat: publish skip [exit 2: tag conflict + -Force なし、zip は生成済み]
goto :ec_done
:ec_skip_n
echo Release.bat: publish skip [exit 3: Y/N の N 回答、zip は生成済み]
goto :ec_done
:ec_fail
echo [FAIL] Release.ps1 が exit code %EXIT_CODE% で終了しました
:ec_done

if %NO_PAUSE% EQU 0 (
    echo.
    pause
)
REM Restore caller cmd codepage (after pause so preceding echo stays during pause, SPEC 3.7.9.6)
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
exit /b %EXIT_CODE%
