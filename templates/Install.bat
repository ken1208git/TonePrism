@echo off
REM ============================================================================
REM Install.bat - GCTonePrism 初回インストーラ (#108 Phase 2)
REM
REM File format: UTF-8 (no BOM) + CRLF (.gitattributes の *.bat eol=crlf で強制、
REM Release.ps1 の Copy-Templates も staging 時に強制 CRLF 化)
REM
REM ASCII boundary rule (Release.bat と同じ規約):
REM   - chcp 65001 より上の echo は ASCII 必須 (codepage 未切替のため日本語は
REM     cp932 mojibake)。REM は @echo off 環境で表示されないため日本語 OK
REM   - chcp 65001 以降は echo も日本語可
REM   - BOM は NG (一部 cmd.exe build で `@echo off` が壊れる、PR #140 経緯)
REM
REM 重要な構造規約: 日本語 echo は (...) ブロック内に置かない (top-level goto
REM パターンで実装)。cmd は `if cond (echo 日本語... goto :fail)` のような
REM ブロックを parse-time に展開する際、ブロック内の日本語 byte 列を mis-tokenize
REM して fragment を command 扱いする ('em.Windows.Forms' is not recognized 等の
REM 連発エラー) 動作が観測された。回避: `if not cond goto :label / [...日本語 echo...] /
REM goto :fail / :label / [...continue...]` の linear flow に統一。
REM
REM Usage (zip ルートから):
REM   Install.bat                ダブルクリック実行 (来場スタッフ / 部員向け)
REM
REM Flow:
REM   1. FolderBrowserDialog で <親> パス選択
REM   2. 入れ子検知: <親> 末尾が GCTonePrism なら警告 + abort
REM   3. 既存検知: <親>\GCTonePrism\ 存在?
REM        Yes -> Y/N 警告 (上書き or abort、Y なら保護データ温存 + Manager close 待機)
REM        No  -> 新規作成 + files/* 全コピー
REM   4. Manager 起動 Y/N -> Y なら Manager.bat 起動
REM
REM Exit codes:
REM   0  正常終了 or ユーザーキャンセル (folder dialog cancel / 上書き N 等)
REM   1  失敗 (files/ 不在 / 入れ子検知 / mkdir 失敗 / robocopy 失敗 / PS 起動失敗)
REM ============================================================================

REM 重要: delayed expansion は **明示的に無効** (Codex P2 対応)。
REM 理由: FolderBrowserDialog の選択パスに `!` が含まれていた場合
REM (例: D:\Games\Backup!\、`!` を含むユーザー名等)、delayed expansion が
REM 有効だと `set INSTALL_PARENT=%%P` の段階で `!` が削除/解釈される。
REM 本ファイル内に `!VAR!` 参照は存在しないので無効化して問題なし。
setlocal disabledelayedexpansion

REM ---- codepage 切替 (Release.bat と同じ pattern、PR #140 で確立) ----
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

REM ==== ASCII boundary (above: ASCII only, below: Japanese OK) ====

set SCRIPT_DIR=%~dp0
set FILES_DIR=%SCRIPT_DIR%files
REM exit code sentinel
set EXIT_CODE=0

REM ---- files/ 不在チェック (top-level、ブロック内に日本語 echo 置かない) ----
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
echo  (選んだフォルダの下に GCTonePrism\ が作成されます)
echo.
echo 例: D:\Games を選ぶ → D:\Games\GCTonePrism\ にインストール
echo.

REM ---- PowerShell で FolderBrowserDialog を起動 → temp file 経由で受け取り ----
REM PS 終了コードを 3 値で意味付け:
REM   0 = OK (path 書き出し済)
REM   2 = ユーザー Cancel (明示的に [Environment]::Exit(2)、PS 内部 error の 1 と区別)
REM   その他 = PS 実行失敗 (Execution Policy block / PS 未 install 等)
REM
REM `[Console]::Out.Write` (newline なし) で書き出して set /p の CR trap を回避。
set "PS_DIALOG_CMD=Add-Type -AssemblyName System.Windows.Forms ^| Out-Null; $d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = 'GCTonePrism のインストール先の親フォルダを選択してください'; $d.ShowNewFolderButton = $true; if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.SelectedPath); [Environment]::Exit(0) } else { [Environment]::Exit(2) }"
set "TEMP_DIALOG_OUT=%TEMP%\gctone_install_dialog_%RANDOM%.tmp"
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -Command "%PS_DIALOG_CMD%" > "%TEMP_DIALOG_OUT%"
set DIALOG_EXIT=%ERRORLEVEL%

if %DIALOG_EXIT% EQU 0 goto :dialog_ok
if %DIALOG_EXIT% EQU 2 goto :dialog_cancel
goto :dialog_fail

:dialog_ok
set /p INSTALL_PARENT=<"%TEMP_DIALOG_OUT%"
del "%TEMP_DIALOG_OUT%" 2>nul
if defined INSTALL_PARENT goto :dialog_done
echo.
echo [FAIL] PowerShell が exit 0 を返しましたが選択パスが取得できませんでした (想定外)。
echo        本シナリオが再現する場合は GitHub issue で報告してください。
goto :fail

:dialog_cancel
del "%TEMP_DIALOG_OUT%" 2>nul
echo.
echo [INFO] キャンセルされました。インストールを中止します。
goto :end

:dialog_fail
echo.
echo [FAIL] PowerShell の起動 / 実行に失敗しました (exit %DIALOG_EXIT%)。
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

REM ---- 入れ子検知 (top-level、ブロック内に日本語 echo 置かない) ----
REM 親パスの末尾が GCTonePrism / GCTonePrism\ の場合、二重入れ子 (<親>\GCTonePrism\GCTonePrism\)
REM になるので警告 + abort。末尾 \ の有無に対応するため両方チェック
set "INSTALL_PARENT_NO_TRAIL=%INSTALL_PARENT%"
if "%INSTALL_PARENT_NO_TRAIL:~-1%"=="\" set "INSTALL_PARENT_NO_TRAIL=%INSTALL_PARENT_NO_TRAIL:~0,-1%"
for %%F in ("%INSTALL_PARENT_NO_TRAIL%") do set "INSTALL_PARENT_LEAF=%%~nxF"

if /i not "%INSTALL_PARENT_LEAF%"=="GCTonePrism" goto :not_nested
echo.
echo [FAIL] 選択されたフォルダ自体が GCTonePrism です:
echo        %INSTALL_PARENT%
echo.
echo        「親フォルダ」を選んでください (その下に GCTonePrism\ が作成されます)。
echo        例: D:\Games\GCTonePrism\ にインストールしたい場合は D:\Games を選択。
goto :fail
:not_nested

set "INSTALL_TARGET=%INSTALL_PARENT_NO_TRAIL%\GCTonePrism"
echo.
echo インストール先: %INSTALL_TARGET%
echo.

REM ---- 既存検知 → top-level label に分岐 ----
if exist "%INSTALL_TARGET%\" goto :existing_install
goto :new_install

:existing_install
echo ============================================================================
echo  [警告] 既存インストールを検出しました
echo ============================================================================
echo.
echo  通常、アップデートは Manager UI から行うのを推奨します
echo  (Phase 4 実装後、現在は未実装)。
echo.
echo  Manager が壊れて起動できない / クリーンインストールしたい場合のみ Y を押してください。
echo  Y を押した場合でも以下のゲームデータは維持されます:
echo    - prism.db (ゲーム情報 DB)
echo    - games\        (ゲーム実体)
echo    - backups\      (バックアップ)
echo    - responses\    (アンケート回答)
echo    - logs\         (ログ)
echo.
REM set /p は空 Enter で変数を更新しない仕様、事前初期化で「前回値保持」事故を防ぐ
set "OVERWRITE_CONFIRM="
set /p OVERWRITE_CONFIRM=上書きしますか？ (Y/N):
if /i "%OVERWRITE_CONFIRM%"=="Y" goto :checkprocess
echo.
echo [INFO] インストールを中止しました。
goto :end

REM ---- Manager / Launcher 稼働中検出 + 手動 close 待機 ----
REM findstr exit 0 = 該当プロセス発見 (= 稼働中)、exit 1 = 未発見 (= 停止中)
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
REM robocopy: /E = 空ディレクトリ含む全コピー、/XF = 除外ファイル、/XD = 除外ディレクトリ
REM /NFL /NDL /NJH /NJS /NC /NS /NP = 出力簡素化、/R:1 /W:1 = retry 控えめ
REM 注: /XF / /XD は「名前マッチ」でツリー全体を走査するため、将来 component 内に
REM 同名 dir (例: Companions/logs/) を作る場合は意図せず除外される副作用に注意。
REM 現状の files/ には prism.db / games / backups / responses / logs が含まれないので
REM 実害なし、防御的に明示している
robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /XF prism.db /XD games backups responses logs /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
REM robocopy の終了コードは bit field、< 8 が成功 (1=コピーあり, 0=変更なし, 等)
if not errorlevel 8 goto :install_done
echo.
echo [FAIL] ファイルコピーに失敗しました (robocopy exit %ERRORLEVEL%)。
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
echo [FAIL] ファイルコピーに失敗しました (robocopy exit %ERRORLEVEL%)。
goto :fail

:install_done

echo.
echo ============================================================================
echo  インストール完了: %INSTALL_TARGET%
echo ============================================================================
echo.
echo  日常使い:
echo    %INSTALL_TARGET%\Launcher.bat   (来場者用 Launcher 起動)
echo    %INSTALL_TARGET%\Manager.bat    (運営用 Manager 起動)
echo.

REM ---- Manager 起動 Y/N (top-level、ブロック内に日本語 echo 置かない) ----
REM 事前初期化で「前回値保持」事故防止 (set /p 空 Enter 罠)
set "START_MANAGER="
set /p START_MANAGER=Manager を起動しますか？ (Y/N):
if /i not "%START_MANAGER%"=="Y" goto :end
echo.
echo [INFO] Manager を起動します...
start "" "%INSTALL_TARGET%\Manager.bat"
REM Manager 起動 path では Install.bat window の pause を抑止 (Manager UI と
REM Install.bat の "何かキーを押してください" prompt が同時表示される UX 退行回避)
set MANAGER_STARTED=1
goto :end

:fail
set EXIT_CODE=1
goto :end

:end
echo.
REM Manager 起動 path 以外では pause で結果メッセージを残す
if not defined MANAGER_STARTED pause
REM 呼出元 cmd の codepage を復元 (Release.bat と同じ pattern)
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
endlocal & exit /b %EXIT_CODE%
