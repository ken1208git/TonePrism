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
#   2  ユーザー Cancel (PS 内部 error の exit 1 と区別するため 2 使用)
#   その他  PS 実行失敗 (Add-Type 失敗、UI 起動失敗 等)
#
# 設計経緯: もともと Install.bat 内に PS 一行 (`-Command "Add-Type...; ..."`)
# として inline していたが、cmd の bat parser が長い `set "VAR=...日本語..."` 行を
# 適切に解釈できず PS に malformed command を渡してしまう問題があった (description
# 内 Japanese の byte 列が `System.Windows.Forms` を分割して PS error)。.ps1 を
# 別ファイルにすることで cmd の parsing を経由せず PS native の UTF-8 処理に任せ、
# Japanese description を安全に維持できる。
# ============================================================================

Add-Type -AssemblyName System.Windows.Forms
$d = New-Object System.Windows.Forms.FolderBrowserDialog
$d.Description = 'GCTonePrism のインストール先の親フォルダを選択してください'
$d.ShowNewFolderButton = $true

if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    # Out.Write (newline なし): bat 側の set /p が末尾 CR 残留する trap を回避
    [Console]::Out.Write($d.SelectedPath)
    [Environment]::Exit(0)
} else {
    [Environment]::Exit(2)
}
