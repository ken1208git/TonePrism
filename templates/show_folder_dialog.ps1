# ============================================================================
# show_folder_dialog.ps1 - FolderBrowserDialog 起動 helper (Install.bat から呼ばれる)
#
# 配置: zip ルート (Install.bat と同階層)、Install.bat が `%~dp0show_folder_dialog.ps1`
#       経由で起動
#
# File format: UTF-8 BOM + CRLF (Release.ps1 の Copy-Templates が staging 時に
#              ensure する。PS 5.1 default の ASCII 読み込みでは Japanese description
#              が mojibake になるため BOM 必須)
#
# Exit codes (Install.bat 側で 3-way dispatch):
#   0  選択 OK → stdout に選択 path (改行なし)
#   1  setup / launch 失敗 (try/catch で受けた exception)。stderr に詳細
#   2  ユーザー Cancel (Dialog の Cancel ボタン / バツ押下)
#   その他  PS host 自体の起動失敗 (.ps1 missing / Execution Policy block 等。
#          この場合 .ps1 内 catch には到達しない)
#
# 設計経緯: もともと Install.bat 内に PS 一行 (`-Command "Add-Type...; ..."`)
# として inline していたが、cmd の bat parser が長い `set "VAR=...日本語..."` 行を
# 適切に解釈できず PS に malformed command を渡してしまう問題があった (description
# 内 Japanese の byte 列が `System.Windows.Forms` を分割して PS error)。.ps1 を
# 別ファイルにすることで cmd の parsing を経由せず PS native の UTF-8 処理に任せ、
# Japanese description を安全に維持できる。
# ============================================================================

# Stop on non-terminating errors so Add-Type / New-Object / property assignment
# failures surface as catchable exceptions (Codex bot P1, 2026-05-12). Without this
# a failing Add-Type would still let the script reach the ShowDialog branch with a
# null $d, hit the else, and exit 2 (= user cancel) — making real install errors
# indistinguishable from intentional cancellation.
$ErrorActionPreference = 'Stop'

try {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'GCTonePrism のインストール先の親フォルダを選択してください'
    $d.ShowNewFolderButton = $true
    $result = $d.ShowDialog()
} catch {
    [Console]::Error.WriteLine("[ERROR] show_folder_dialog setup/launch failed: $($_.Exception.Message)")
    [Environment]::Exit(1)
}

if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    # Out.Write (newline なし): bat 側の set /p が末尾 CR 残留する trap を回避
    [Console]::Out.Write($d.SelectedPath)
    [Environment]::Exit(0)
} else {
    [Environment]::Exit(2)
}
