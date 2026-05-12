@echo off
REM ============================================================================
REM Install.bat - GCTonePrism 初回インストーラ (#108 Phase 2)
REM
REM File format: UTF-8 (no BOM) + CRLF (.gitattributes の *.bat eol=crlf で強制)
REM
REM ASCII boundary rule (Release.bat と同じ規約):
REM   - chcp 65001 より上の REM / echo は ASCII 必須 (codepage 未切替のため
REM     日本語は cp932 mojibake)
REM   - chcp 65001 以降は日本語可
REM   - BOM は NG (一部 cmd.exe build で `@echo off` が壊れる、PR #140 経緯)
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
REM ============================================================================

setlocal enabledelayedexpansion

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

if not exist "%FILES_DIR%" (
    echo [FAIL] files/ ディレクトリが見つかりません: %FILES_DIR%
    echo        zip を正しく展開してから Install.bat を実行してください。
    goto :end
)

echo ============================================================================
echo  GCTonePrism インストーラ
echo ============================================================================
echo.
echo インストール先の「親フォルダ」を選択してください。
echo (選んだフォルダの下に GCTonePrism\ が作成されます)
echo.
echo 例: D:\Games を選ぶ → D:\Games\GCTonePrism\ にインストール
echo.

REM ---- PowerShell で FolderBrowserDialog を起動して選択パスを取得 ----
REM PowerShell の Add-Type で WinForms を読み込み、ダイアログを出して結果を stdout で返す
set "PS_DIALOG_CMD=Add-Type -AssemblyName System.Windows.Forms ^| Out-Null; $d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = 'GCTonePrism のインストール先の親フォルダを選択してください'; $d.ShowNewFolderButton = $true; if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { Write-Output $d.SelectedPath } else { exit 1 }"
for /f "usebackq tokens=* delims=" %%P in (`powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -Command "%PS_DIALOG_CMD%"`) do set INSTALL_PARENT=%%P

if not defined INSTALL_PARENT (
    echo.
    echo [INFO] キャンセルされました。インストールを中止します。
    goto :end
)

REM ---- 入れ子検知 ----
REM 親パスの末尾が GCTonePrism / GCTonePrism\ の場合、二重入れ子 (<親>\GCTonePrism\GCTonePrism\)
REM になるので警告 + abort。末尾 \ の有無に対応するため両方チェック
set "INSTALL_PARENT_NO_TRAIL=%INSTALL_PARENT%"
if "%INSTALL_PARENT_NO_TRAIL:~-1%"=="\" set "INSTALL_PARENT_NO_TRAIL=%INSTALL_PARENT_NO_TRAIL:~0,-1%"
for %%F in ("%INSTALL_PARENT_NO_TRAIL%") do set "INSTALL_PARENT_LEAF=%%~nxF"

if /i "%INSTALL_PARENT_LEAF%"=="GCTonePrism" (
    echo.
    echo [FAIL] 選択されたフォルダ自体が GCTonePrism です:
    echo        %INSTALL_PARENT%
    echo.
    echo        「親フォルダ」を選んでください ^(その下に GCTonePrism\ が作成されます^)。
    echo        例: D:\Games\GCTonePrism\ にインストールしたい場合は D:\Games を選択。
    goto :end
)

set "INSTALL_TARGET=%INSTALL_PARENT_NO_TRAIL%\GCTonePrism"
echo.
echo インストール先: %INSTALL_TARGET%
echo.

REM ---- 既存検知 ----
if exist "%INSTALL_TARGET%\" (
    echo ============================================================================
    echo  [警告] 既存インストールを検出しました
    echo ============================================================================
    echo.
    echo  通常、アップデートは Manager UI から行うのを推奨します
    echo  ^(Phase 4 実装後、現在は未実装^)。
    echo.
    echo  Manager が壊れて起動できない / クリーンインストールしたい場合のみ Y を押してください。
    echo  Y を押した場合でも以下のゲームデータは維持されます:
    echo    - prism.db ^(ゲーム情報 DB^)
    echo    - games\        ^(ゲーム実体^)
    echo    - backups\      ^(バックアップ^)
    echo    - responses\    ^(アンケート回答^)
    echo    - logs\         ^(ログ^)
    echo.
    set /p OVERWRITE_CONFIRM=上書きしますか？ (Y/N):
    if /i not "!OVERWRITE_CONFIRM!"=="Y" (
        echo.
        echo [INFO] インストールを中止しました。
        goto :end
    )

    REM ---- Manager / Launcher 稼働中検出 + 手動 close 待機 ----
    :checkprocess
    tasklist /FI "IMAGENAME eq GCTonePrism_Manager.exe" 2>nul | findstr /I "GCTonePrism_Manager.exe" >nul
    set MANAGER_RUNNING=%ERRORLEVEL%
    tasklist /FI "IMAGENAME eq GCTonePrism_Launcher.exe" 2>nul | findstr /I "GCTonePrism_Launcher.exe" >nul
    set LAUNCHER_RUNNING=%ERRORLEVEL%
    if %MANAGER_RUNNING% EQU 0 (
        echo.
        echo [警告] GCTonePrism_Manager.exe が稼働中です。
    )
    if %LAUNCHER_RUNNING% EQU 0 (
        echo [警告] GCTonePrism_Launcher.exe が稼働中です。
    )
    if %MANAGER_RUNNING% EQU 0 goto :wait_close
    if %LAUNCHER_RUNNING% EQU 0 goto :wait_close
    goto :do_overwrite
    :wait_close
    echo.
    echo  Manager / Launcher を全て閉じてから Enter キーを押してください...
    pause >nul
    goto :checkprocess

    :do_overwrite
    echo.
    echo [INFO] 上書きインストールを開始します...
    REM robocopy: /E = 空ディレクトリ含む全コピー、/XF = 除外ファイル、/XD = 除外ディレクトリ
    REM /NFL /NDL /NJH /NJS /NC /NS /NP = 出力簡素化、/R:1 /W:1 = retry 控えめ
    REM robocopy 除外は GCTonePrism_Manager\ サブディレクトリ配下のものを名前指定で除外
    REM (フルパス指定すると環境依存になるためファイル名 / ディレクトリ名のみで指定)
    robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /XF prism.db /XD games backups responses logs /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
    REM robocopy の終了コードは bit field、< 8 が成功 (1=コピーあり, 0=変更なし, 等)
    if errorlevel 8 (
        echo.
        echo [FAIL] ファイルコピーに失敗しました ^(robocopy exit %ERRORLEVEL%^)。
        goto :end
    )
) else (
    echo [INFO] 新規インストールを開始します...
    mkdir "%INSTALL_TARGET%" 2>nul
    if not exist "%INSTALL_TARGET%\" (
        echo.
        echo [FAIL] インストール先フォルダを作成できませんでした: %INSTALL_TARGET%
        echo        書き込み権限を確認してください。
        goto :end
    )
    robocopy "%FILES_DIR%" "%INSTALL_TARGET%" /E /NFL /NDL /NJH /NJS /NC /NS /NP /R:1 /W:1
    if errorlevel 8 (
        echo.
        echo [FAIL] ファイルコピーに失敗しました ^(robocopy exit %ERRORLEVEL%^)。
        goto :end
    )
)

echo.
echo ============================================================================
echo  インストール完了: %INSTALL_TARGET%
echo ============================================================================
echo.
echo  日常使い:
echo    %INSTALL_TARGET%\Launcher.bat   ^(来場者用 Launcher 起動^)
echo    %INSTALL_TARGET%\Manager.bat    ^(運営用 Manager 起動^)
echo.

REM ---- Manager 起動 Y/N ----
set /p START_MANAGER=Manager を起動しますか？ (Y/N):
if /i "%START_MANAGER%"=="Y" (
    echo.
    echo [INFO] Manager を起動します...
    start "" "%INSTALL_TARGET%\Manager.bat"
)

:end
echo.
pause
REM 呼出元 cmd の codepage を復元 (Release.bat と同じ pattern)
if defined ORIGINAL_CODEPAGE chcp %ORIGINAL_CODEPAGE% >nul
endlocal
exit /b 0
