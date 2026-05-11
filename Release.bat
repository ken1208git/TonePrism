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
REM BOM 認識不能環境での脱出方法 (Win 10 1809 以前の cmd.exe build 等):
REM   1. BOM を外して UTF-8 (no BOM) + CRLF で保存し直す
REM   2. chcp 65001 より前の REM / echo 行を全部 ASCII (英語) に置換する
REM      (chcp 65001 が効くまでの数行は console codepage のままパースされるため)
REM   3. .gitattributes の *.bat の rule に working-tree-encoding=UTF-8 は付けない
REM      (git の eol=crlf が CRLF 強制してくれれば十分)
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

REM enabledelayedexpansion は parseargs ループ内の `!FORWARDED_ARGS!` 連結のために必要。
REM (それ以外の通常 set 行では遅延展開を使ってないため、削除すると引数 forward だけ壊れる)
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
    REM chcp 65001 未切替の状態で出力するため、ASCII 限定の英語を先に出す
    REM (この時点で UTF-8 BOM bat の日本語 echo は文字化けして読めない可能性が高い)
    echo [WARN] Failed to capture codepage from 'chcp' output.
    echo [WARN] Skipping UTF-8 codepage switch ^(Japanese output may be garbled^).
    echo        chcp 出力から codepage を取得できませんでした ^(UTF-8 切替を skip^)。
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
REM pause より後にしてあるのは、直前の echo メッセージ ([FAIL] / 正常終了 等) を
REM ユーザーが pause 中も正しく表示し続けるため (chcp 復元すると過去出力の見え方が崩れうる)
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
exit /b %EXIT_CODE%
