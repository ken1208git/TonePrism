@echo off
REM ============================================================================
REM Release.bat - Release.ps1 のラッパー (ダブルクリック / 短縮コマンド用)
REM
REM ファイル形式: UTF-8 BOM + CRLF 改行 (Windows cmd.exe 要件)
REM   - cmd.exe は LF only の bat を正しくパースできず、無音で実行が止まる
REM   - .gitattributes で *.bat eol=crlf を指定して checkout 時に CRLF を強制
REM   - 日本語 echo の文字化け防止のため chcp 65001 で UTF-8 codepage に切替
REM
REM 使い方:
REM   .\Release.bat                  : CHANGELOG.md の最新 Bundle エントリで本番リリース
REM   .\Release.bat -SkipUpload      : zip 生成のみ、アップロードしない
REM   .\Release.bat -DryRun -SkipUpload : ビルド確認のみ
REM   .\Release.bat -Force           : uncommitted change 許容
REM   .\Release.bat -NoPause         : 完了時に pause しない（CI / 自動化用）
REM
REM `-NoPause` 以外の引数はすべて Release.ps1 にそのまま渡す。
REM Bundle version は Release.ps1 が CHANGELOG.md から自動取得 (-Version 省略可)。
REM 詳細は Release.ps1 のヘルプ (Get-Help .\Release.ps1 -Detailed) を参照。
REM ============================================================================

setlocal enabledelayedexpansion

REM UTF-8 codepage 強制 (日本語 echo / REM コメントの文字化け防止、念のための保険)
REM chcp 出力: "Active code page: 932" / "現在のコード ページ: 932" → ":" 後の値を取得
REM ":" 後はスペース付き " 932" になるため、変数置換でスペースを除去してから保存
for /f "tokens=2 delims=:" %%a in ('chcp') do set ORIG_CP=%%a
if defined ORIG_CP set ORIG_CP=%ORIG_CP: =%
chcp 65001 >nul

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
REM codepage を元に戻す (pause より後で、ユーザーが pause 中も日本語 echo を見られるように)
if defined ORIG_CP chcp %ORIG_CP% >nul
exit /b %EXIT_CODE%
