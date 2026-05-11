@echo off
REM ============================================================================
REM Release.bat - Release.ps1 wrapper for double-click / shortcut use.
REM
REM File format: UTF-8 (no BOM) + CRLF line endings.
REM   - cmd.exe parses LF-only bat files incorrectly (silent fail). CRLF is
REM     required; .gitattributes enforces this via `*.bat eol=crlf`.
REM   - UTF-8 BOM is NOT used: some older cmd.exe builds (Win 10 < 1809) fail
REM     to recognize the BOM and dump mojibake before `chcp 65001` can fire.
REM     Keeping the top ASCII-only avoids this trap entirely.
REM   - Comments / echo above the `chcp 65001` line MUST stay ASCII because
REM     they are parsed under the console codepage (cp932 on JP locale).
REM   - Below `chcp 65001 >nul`, Japanese is safe (UTF-8 console).
REM
REM Usage:
REM   .\Release.bat                  : full release using CHANGELOG.md head Bundle entry
REM   .\Release.bat -SkipUpload      : build zip only, skip GitHub upload
REM   .\Release.bat -DryRun -SkipUpload : build verification only
REM   .\Release.bat -Force           : allow uncommitted changes
REM   .\Release.bat -NoPause         : skip pause on exit (for CI / automation)
REM
REM Arguments other than `-NoPause` are forwarded verbatim to Release.ps1.
REM The Bundle version is auto-detected from CHANGELOG.md (-Version optional).
REM See Release.ps1 docstring for details (Get-Help .\Release.ps1 -Detailed).
REM ============================================================================

REM enabledelayedexpansion is required for `!FORWARDED_ARGS!` concatenation in
REM the parseargs loop below. Removing it breaks argument forwarding.
setlocal enabledelayedexpansion

REM ---- UTF-8 codepage switch + restore-on-exit setup ----
REM
REM `chcp 65001` is a defense-in-depth for cmd.exe echo / pause output and
REM bat file character parsing. The file is no-BOM UTF-8, so the top of this
REM script (above this point) is intentionally ASCII-only.
REM
REM Note: `chcp` is console-wide state and is NOT scoped by setlocal/endlocal,
REM       so an explicit `chcp %ORIGINAL_CODEPAGE%` at the end is required.
REM       (Otherwise the caller's cmd window keeps codepage 65001 after exit.)
REM
REM Expected chcp output formats:
REM   English:  "Active code page: 932"
REM   Japanese: "Current code page: 932" (translated, same colon layout)
REM   Other locales also follow "label: number" pattern, so delims=: works.
for /f "tokens=2 delims=:" %%a in ('chcp') do set ORIGINAL_CODEPAGE=%%a
REM `%VAR: =%` strips ALL spaces in VAR (chcp output is " 932" with leading space).
if defined ORIGINAL_CODEPAGE set ORIGINAL_CODEPAGE=%ORIGINAL_CODEPAGE: =%

REM If ORIGINAL_CODEPAGE failed to parse (unknown locale format, redirection
REM filtering, etc.), skip the chcp 65001 switch entirely to avoid leaking
REM codepage 65001 to the caller's cmd window.
if defined ORIGINAL_CODEPAGE (
    chcp 65001 >nul
) else (
    echo [WARN] Failed to capture codepage from 'chcp' output.
    echo [WARN] Skipping UTF-8 codepage switch ^(Japanese output may be garbled^).
)

REM ---- 以下、chcp 65001 後は日本語 OK ----

set SCRIPT_DIR=%~dp0

REM 引数を解析: -NoPause は剥がす、それ以外はそのまま PS に渡す
set NO_PAUSE=0
set FORWARDED_ARGS=
:parseargs
if "%~1"=="" goto :runps
if /i "%~1"=="-NoPause" (
    set NO_PAUSE=1
    shift
    goto :parseargs
)
REM 引数にスペースが含まれる場合 (パス等) のためダブルクォートで保護
set FORWARDED_ARGS=!FORWARDED_ARGS! "%~1"
shift
goto :parseargs

:runps
echo Release.bat: 引数 = %FORWARDED_ARGS%
echo Release.bat: NoPause = %NO_PAUSE%
echo.

powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Release.ps1" %FORWARDED_ARGS%

set EXIT_CODE=%ERRORLEVEL%
echo.
if %EXIT_CODE% NEQ 0 (
    echo [FAIL] Release.ps1 が exit code %EXIT_CODE% で終了しました
) else (
    echo Release.bat: 正常終了
)

if %NO_PAUSE% EQU 0 (
    echo.
    pause
)
REM codepage を元に戻す (chcp は console-wide で setlocal の endlocal 対象外、
REM 明示復元が必須。ここを削ると呼び出し元 cmd 窓の codepage に副作用が残る)
REM pause より後にしてあるのは、直前の echo メッセージ ([FAIL] / 正常終了 等) を
REM ユーザーが pause 中も正しく表示し続けるため (chcp 復元すると過去出力の見え方が崩れうる)
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
exit /b %EXIT_CODE%
