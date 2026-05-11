@echo off
REM ============================================================================
REM Release.bat - Release.ps1 のラッパー (ダブルクリック / 短縮コマンド用)
REM
REM ファイル形式: UTF-8 BOM + CRLF 改行 (Windows cmd.exe 要件)
REM   - cmd.exe は LF only の bat を正しくパースできず、無音で実行が止まる
REM   - .gitattributes で *.bat eol=crlf を指定して checkout 時に CRLF を強制
REM   - cmd.exe の echo / pause 出力およびファイルパース時の文字解釈の保険として
REM     chcp 65001 (UTF-8 codepage) に切替。BOM 認識挙動が cmd.exe build に依存するため
REM
REM 使い方:
REM   .\Release.bat                  : CHANGELOG.md の最新 Bundle エントリで本番リリース
REM   .\Release.bat -SkipUpload      : zip 生成のみ、アップロードしない
REM   .\Release.bat -DryRun -SkipUpload : ビルド確認のみ
REM   .\Release.bat -Force           : uncommitted change 許容
REM   .\Release.bat -NoPause         : 完了時に pause しない (CI / 自動化用)
REM
REM `-NoPause` 以外の引数はすべて Release.ps1 にそのまま渡す。
REM Bundle version は Release.ps1 が CHANGELOG.md から自動取得 (-Version 省略可)。
REM 詳細は Release.ps1 のヘルプ (Get-Help .\Release.ps1 -Detailed) を参照。
REM ============================================================================

setlocal enabledelayedexpansion

REM ---- UTF-8 codepage 切替 + 終了時の復元準備 ----
REM cmd.exe の echo / pause / ファイルパース時の文字解釈の保険として 65001 へ切替。
REM
REM 注意: chcp は console-wide な状態で setlocal / endlocal の対象外なので、
REM       明示的に元の codepage に復元しないとユーザーの cmd 窓に副作用が残る。
REM       (このため終了時に明示 chcp で復元する処理が下部にある)
REM
REM chcp 出力フォーマット例:
REM   英:  "Active code page: 932"
REM   日:  "現在のコード ページ: 932"
REM   他ロケールも "ラベル: 番号" の形式を踏襲しているため delims=: で動作する想定
for /f "tokens=2 delims=:" %%a in ('chcp') do set ORIGINAL_CODEPAGE=%%a
REM %VAR: =% は VAR 内の全スペースを空文字に置換する構文 (" 932" → "932")
if defined ORIGINAL_CODEPAGE set ORIGINAL_CODEPAGE=%ORIGINAL_CODEPAGE: =%

REM ORIGINAL_CODEPAGE が取れなかった場合は codepage を切り替えない (副作用回避)。
REM 出力フォーマットが未知のロケール / リダイレクトでフィルタされる等のレアケース対策。
if defined ORIGINAL_CODEPAGE (
    chcp 65001 >nul
) else (
    echo [WARN] chcp 出力から codepage を取得できませんでした。
    echo        UTF-8 codepage 切替を skip します ^(日本語が文字化けする可能性あり^)。
)

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
REM pause より後にしてあるのは、ユーザーが pause 中も日本語 echo を見られるように
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
exit /b %EXIT_CODE%
