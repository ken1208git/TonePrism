@echo off
REM ============================================================================
REM Release.bat - Release.ps1 wrapper for double-click / shortcut use.
REM
REM File format: UTF-8 (no BOM) + CRLF line endings.
REM   - cmd.exe parses LF-only bat files incorrectly (silent fail). CRLF is
REM     required; .gitattributes enforces this via `*.bat eol=crlf`.
REM   - UTF-8 BOM is NOT used: on cmd.exe builds that do not recognize the
REM     BOM (observed on some Windows installations), the BOM bytes
REM     (EF BB BF) are interpreted as CP932 text and concatenated with the
REM     first command. The very first line becomes something like
REM     "'_@echo' is not recognized as ... command", which means `@echo off`
REM     ITSELF fails. From that point on the script runs with echo enabled,
REM     splaying every REM line + cp932-mojibake to the console.
REM     Do NOT re-introduce a BOM here even if a modern editor suggests it.
REM   - Comments / echo above the `chcp 65001` line MUST stay ASCII because
REM     they are parsed under the console codepage (cp932 on JP locale).
REM   - Below `chcp 65001 >nul`, Japanese is safe (UTF-8 console).
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
REM See Release.ps1 docstring for details (Get-Help .\Release.ps1 -Detailed).
REM ============================================================================

REM enabledelayedexpansion is required for `!FORWARDED_ARGS!` concatenation in
REM the parseargs loop below. Removing it breaks argument forwarding.
REM
REM Side effect: with delayed expansion enabled, `!` in argument values is
REM consumed as the delayed-expansion token. The current pass-through args
REM (-DryRun / -Force / -NoPause / -Version / -SkipUpload / -Offline /
REM -GodotExe / -GodotPatch / -MsBuildExe / -NugetExe) never contain `!`,
REM so this is a non-issue today. Documented here in case a future
REM pass-through arg accepts arbitrary text containing `!`.
setlocal enabledelayedexpansion

REM ---- UTF-8 codepage switch + restore-on-exit setup ----
REM
REM `chcp 65001` is a defense-in-depth for cmd.exe echo / pause output and
REM bat file character parsing. The file is no-BOM UTF-8, so the top of this
REM script (above this point) is intentionally ASCII-only.
REM
REM Note: `chcp` is console-wide state and is NOT scoped by setlocal/endlocal,
REM       so an explicit `chcp %ORIGINAL_CODEPAGE%` at the end is required
REM       (only if we successfully captured the original codepage; see below).
REM       Otherwise the caller's cmd window keeps codepage 65001 after exit.
REM
REM Flow (this `for /f` + 3 if blocks below implement the following 3-way branch):
REM   chcp output captured + numeric  -> chcp 65001, restore original on exit
REM   chcp output captured + invalid  -> SKIP chcp 65001 (corrupted format)
REM   chcp output not captured        -> SKIP chcp 65001 (redirect filter, etc.)
REM Skipping the switch is the safe default: leaks codepage 65001 to the
REM caller's cmd window can only happen if we entered the switch successfully.
REM
REM Expected chcp output structure (this REM block must stay ASCII because it
REM runs before `chcp 65001`; the JP localized label is intentionally NOT
REM pasted here, see CHANGELOG [Release Tooling v1.0.5] for the literal):
REM   English locale (cp437/cp1252): "Active code page: 932"
REM   Japanese locale (cp932):       label is the localized version (non-ASCII),
REM                                  same "label: number" layout with colon
REM   Other locales also follow "label: number" pattern, so `delims=:` works.
for /f "tokens=2 delims=:" %%a in ('chcp') do set ORIGINAL_CODEPAGE=%%a
REM `%VAR: =%` strips ALL spaces in VAR (chcp output is " 932" with leading space).
if defined ORIGINAL_CODEPAGE set ORIGINAL_CODEPAGE=%ORIGINAL_CODEPAGE: =%
REM Validate that the captured value is a pure decimal number. If parsing
REM produced anything else (unknown locale format, unicode digit, etc.),
REM OR if `findstr` itself fails to launch (minimal WinPE, broken PATH),
REM the `||` branch fires and we treat as "capture failed" by clearing
REM the variable. We can't distinguish "non-numeric" from "findstr missing"
REM here, so both fall into the same skip path below. That's intentional:
REM if we can't verify the captured value, refuse the codepage switch and
REM leak garbage to the caller's cmd window on exit.
if defined ORIGINAL_CODEPAGE (
    echo %ORIGINAL_CODEPAGE%| findstr /R "^[0-9][0-9]*$" >nul || set ORIGINAL_CODEPAGE=
)

REM If ORIGINAL_CODEPAGE failed to parse (unknown locale format, redirection
REM filtering, etc.), skip the chcp 65001 switch entirely to avoid leaking
REM codepage 65001 to the caller's cmd window.
if defined ORIGINAL_CODEPAGE (
    chcp 65001 >nul
) else (
    echo [WARN] Failed to capture codepage from 'chcp' output.
    echo [WARN] Skipping UTF-8 codepage switch ^(Japanese output may be garbled^).
)

REM ==== ASCII boundary (chcp 65001 above, UTF-8 console below) ====

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
REM 初回 append 時は leading space を付けないため if defined で分岐
if defined FORWARDED_ARGS (
    set FORWARDED_ARGS=!FORWARDED_ARGS! "%~1"
) else (
    set FORWARDED_ARGS="%~1"
)
shift
goto :parseargs

:runps
REM Below uses Japanese echo. If the chcp 65001 skip path was taken above
REM (no `[WARN]` line, codepage still the original cp932/cp437 etc.), the
REM Japanese here will be garbled. We emit one more ASCII line to make that
REM context explicit instead of silently relying on the line 89-90 warning.
if not defined ORIGINAL_CODEPAGE (
    echo [WARN] Codepage switch was skipped; the following Japanese output may be garbled.
)
echo Release.bat: 引数 = %FORWARDED_ARGS%
echo Release.bat: NoPause = %NO_PAUSE%
echo.

powershell.exe -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Release.ps1" %FORWARDED_ARGS%

set EXIT_CODE=%ERRORLEVEL%
echo.
REM Exit code 体系 (Release.ps1 と一致、シニアレビュー round 3 M4):
REM   0 = success: publish 成功 / -SkipUpload / -DryRun
REM   1 = failure: script の本来失敗 (build / publish / env)
REM   2 = skip:    tag conflict + -Force なしによる publish skip (env 起因)
REM   3 = skip:    Y/N の N 回答による intentional skip
REM
REM Caller (CI 等) はこの bat の %ERRORLEVEL% で 4 状態を区別できる。
REM Release.bat 自体は表示のみ責任を持ち、exit code はそのまま透過する。
if %EXIT_CODE% EQU 0 (
    echo Release.bat: 正常終了
) else if %EXIT_CODE% EQU 2 (
    echo Release.bat: publish skip [exit 2: tag conflict + -Force なし、zip は生成済み]
) else if %EXIT_CODE% EQU 3 (
    echo Release.bat: publish skip [exit 3: Y/N の N 回答、zip は生成済み]
) else (
    echo [FAIL] Release.ps1 が exit code %EXIT_CODE% で終了しました
)

if %NO_PAUSE% EQU 0 (
    echo.
    pause
)
REM 呼出元 cmd の codepage を復元 (詳細経緯は冒頭 docstring 参照)。
REM pause より後にしているのは、直前 echo を pause 中も正しく表示し続けるため。
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
exit /b %EXIT_CODE%
