<#
.SYNOPSIS
    GCTonePrism のリリース zip を生成し、GitHub Releases にアップロードする。

.DESCRIPTION
    Phase 1 (#108): Launcher + Manager のビルドと zip 化のみ。
        - CHANGELOG.md の最新 `### [Bundle v<X.Y.Z>]` エントリが Bundle version + release_notes 両方の SoT
        - version.gd = Launcher version の SoT、AssemblyInfo.cs = Manager version の SoT
        - Bundle version は zip タグに使う（例: v0.1.0）
        - GitHub Releases の本文は CHANGELOG.md の該当 Bundle セクションから自動抽出
        - Godot エディタとエクスポートテンプレートは tools/godot/ + %APPDATA%/Godot/export_templates/ に自動ダウンロード（バージョンピン留め + SHA256 検証 + キャッシュ + 3 回 retry）
        - Manager のビルドは Visual Studio 同梱の MSBuild (Roslyn) + 自動ダウンロード nuget.exe を使用
          - Manager コードが C# 7+ (ValueTuple / string interpolation 等) を使うため、Roslyn を含む MSBuild 14+ が必須
          - Windows 同梱の .NET Framework MSBuild 4 は csc が古すぎて使えない（VS Build Tools のインストールが必要、~1-2 GB）
        - release/v<version>/files/ に staging
        - Compress-Archive で release/GCTonePrism_v<version>.zip 生成
        - zip 完成後、Y/N 確認プロンプト → Y なら gh release create でアップロード、N なら zip だけ残して終了
          (`-SkipUpload` で confirm prompt 自体を skip。CI 等の non-interactive 運用向け)

    Phase 2 (#108) 完成: templates/Install.bat / INSTALL_README.txt / Launcher.bat / Manager.bat 同梱
    TODO Phase 3 (#108): GCTonePrism_Updater/ の dotnet publish を追加
    TODO #101:           GCTonePrism_Launcher/Companions/ のループを追加
    TODO Monitor:        GCTonePrism_Monitor/ 追加時にビルドステップ追加

    依存ツールのバージョン管理方針:
        - Godot: project.godot の config/features から major.minor を読み取り、$GodotPatchTable で patch をピン留め
        - nuget: $NugetPinnedVersion でピン留め
        - MSBuild: Visual Studio 2019+ または VS Build Tools を要求（C# 7+ サポートのため）
        - 各ツールのバージョン上げは Release.ps1 冒頭定数を手動で書き換え + PR

    必要な環境:
        - Windows 10/11
        - Visual Studio 2019+ または Visual Studio Build Tools (https://aka.ms/vs/17/release/vs_BuildTools.exe、~1-2GB)
          - 「.NET デスクトップビルドツール」ワークロードを選択
        - gh CLI (アップロード時、`gh auth login` 済み)
        - インターネット接続 (初回 Godot + nuget DL のため、~1.2 GB)
        - 開発者は別途 Godot 4.6 をインストールして開発（Release.ps1 は CLI export のみ自動化）

.PARAMETER Version
    リリースバージョン (Bundle version)。省略時は CHANGELOG.md の最新 Bundle エントリから自動取得。
    指定する場合は CHANGELOG.md の最新 Bundle エントリと一致する必要がある。
    SemVer 形式（M.m.p または M.m.p-suffix）。

.PARAMETER GodotExe
    Godot 実行ファイルのパス。指定された場合は自動ダウンロードを skip。

.PARAMETER MsBuildExe
    MSBuild 実行ファイルのパス。空なら自動検出 (vswhere → PATH の順、VS or Build Tools が必要)。

.PARAMETER NugetExe
    NuGet 実行ファイルのパス。指定された場合は自動ダウンロードを skip。

.PARAMETER GodotPatch
    Godot patch version を強制指定 (例: "4.6.2")。
    $GodotPatchTable の自動解決を上書きする。SHA256 検証は skip される。

.PARAMETER SkipUpload
    gh release create を実行しない。zip 生成までで停止。

.PARAMETER DryRun
    ビルドのみ実施。zip 化と upload を skip。Godot/Manager 出力の確認用。
    -SkipUpload を自動で ON にする (upload しないため gh preflight も不要)。

.PARAMETER Force
    既存タグ衝突時に gh release delete してから作り直す。
    uncommitted change があっても続行する。

.PARAMETER Offline
    外部ネットワーク呼び出しを skip。キャッシュにある version で実行。
    Godot DL / nuget DL / GitHub API 呼び出しを行わない。
    -SkipUpload を自動で ON にする (upload は network 必須のため)。

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File .\Release.ps1 -Version 0.1.0 -SkipUpload

.EXAMPLE
    pwsh -ExecutionPolicy Bypass -File .\Release.ps1 -Version 0.1.0 -DryRun -SkipUpload
#>

[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$GodotExe = "",
    [string]$MsBuildExe = "",
    [string]$NugetExe = "",
    [string]$GodotPatch = "",
    [switch]$SkipUpload,
    [switch]$DryRun,
    [switch]$Force,
    [switch]$Offline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ============================================================================
# Console output encoding を UTF-8 (no BOM) にピン留め
# ============================================================================
# PS 5.1 + chcp 65001 環境では、子プロセス (Invoke-ExternalProcess 経由の
# Godot CLI / msbuild / nuget 等、RedirectStandardOutput=$false でコンソール
# ハンドルを継承するもの) が走った後、後続の Write-Host で日本語が doubled
# rendering される既知バグがある (症状例: `完了` → `完完了了`、ASCII は影響なし)。
# 観察例として本番 v0.1.0 release では msbuild 後に発火を確認、ただし原因として
# Godot / nuget も同じ条件を満たすので等価候補。
# `[Console]::OutputEncoding` を UTF-8 (no BOM) で明示ピン留めしておく + 各
# Invoke-ExternalProcess 呼び出し後に再ピン留めすることで発火を防ぐ。
# `$OutputEncoding` は PS pipeline → native command への送信時の encoding。
#
# 非 console host ガード: CI / headless / redirected 実行コンテキストでは
# `[Console]::OutputEncoding` setter が IOException を投げ、$ErrorActionPreference='Stop'
# 環境では release 開始前に script が hard abort する。try/catch で吸収。
# (script-level の $script:Utf8NoBomEncoding を一意ソースとして共有、
#  Invoke-ExternalProcess の finally でも同じインスタンスを再代入)
$script:Utf8NoBomEncoding = [System.Text.UTF8Encoding]::new($false)
try {
    [Console]::OutputEncoding = $script:Utf8NoBomEncoding
} catch {
    # non-console host では console API は使えないが、$OutputEncoding (PS 変数) は
    # 引き続き設定可能、native command pipeline 送信側は UTF-8 のまま維持される
}
$OutputEncoding = $script:Utf8NoBomEncoding

# ============================================================================
# Native command 呼び出しの方針 (v0.1.8 で再整理)
# ============================================================================
# 経緯: PS 5.1 + $ErrorActionPreference='Stop' 下では、`&` 演算子 + native
# command の組み合わせで「exit 非ゼロ + stderr 出力」を返すコマンドが
# NativeCommandError の terminating error を発生させる。
# v0.1.6 までは `2>$null` / `2>&1 | Out-String` 系で回避できると考えていたが、
# v0.1.7 で実証された通り PS の error stream への ErrorRecord 化が redirect
# よりも先に発火するため redirect は防御にならない。v0.1.8 で
# `Invoke-NativeWithCapture` ヘルパー (System.Diagnostics.Process 直叩き) に
# 一本化、`&` 系の patterns は「success exit が確証できる場合のみ」に格下げ。
#
# 採用ガイドライン (call site では「pattern: 名前」で参照):
#
#   1. Invoke-NativeWithCapture (RECOMMENDED for any failable command)
#      $result = Invoke-NativeWithCapture -FilePath 'gh' -Arguments @('release','view','v1.0','--json','id')
#      # → $result.StdOut / $result.StdErr / $result.ExitCode / $result.Combined
#      # System.Diagnostics.Process で PS error stream を経由しないため罠なし。
#      # 失敗 path で stderr を出すコマンドは必ずこれ (gh release view / gh auth status 等)。
#
#   2. pattern: CAPTURE_STDOUT_PASS_STDERR
#      $output = & native-cmd        # stdout は変数 capture、stderr は console 直書き
#      # exit code チェック必須 ($output が空でも非ゼロ exit を見落とさないため)
#      # 例: git status --porcelain (Assert-WorkingTreeClean)
#
#   3. pattern: PASS_THROUGH — 2 実装あり、機能は同等だが内部メカニズムが違う
#
#      (a) 直 & 演算子版 (現状本 script 内で使用なし):
#          & native-cmd               # stdout/stderr 両方 console 直書き、変数 capture なし
#          # exit code チェック必須 (成否を判定する唯一の信号が exit code のみ)
#          # 注: PS 5.1 + chcp 65001 で子プロセスが OutputEncoding を OEM に戻す
#          #     既知バグの再ピン留め保護がない。短時間の単発呼び出し向け
#
#      (b) Invoke-ExternalProcess helper 経由 (推奨、現状の全 PASS_THROUGH 系):
#          $exitCode = Invoke-ExternalProcess -FilePath $cmd -Arguments @(...)
#          # 内部は [System.Diagnostics.Process]::Start + WaitForExit + Dispose
#          # 引数 quoting + finally で [Console]::OutputEncoding を UTF-8 に再ピン留め
#          # 大量 verbose 出力 + 長時間処理向け (Godot CLI export / msbuild / nuget restore)
#
#      注: gh release create/delete は v0.1.7 まで (a) PASS_THROUGH だったが、TTY 検出
#          で進捗 OFF + 完了まで無音になる UX 問題のため v0.1.8 で
#          Invoke-NativeWithCapture -ShowProgress に移行
#
# ANTI-PATTERNS (使用禁止) — いずれも踏み抜き履歴と deprecation 時点を 2 値で記録:
#
#   X. STOP_TRAP — & cmd 2>&1 / $var = & cmd 2>&1
#      stderr が ErrorRecord として success stream に流れ Stop trap 発火。
#      初回 deprecate from v0.1.0 (script 新設時点)。
#      回避: Invoke-NativeWithCapture へ移行。
#
#   X. SUPPRESS_BOTH — & cmd 2>$null | Out-Null
#      踏み抜き: v0.1.6 (本番 release で gh release view が trap 発火)
#      deprecation: v0.1.8 (anti-pattern として正式格下げ)
#      「2>$null で stderr を捨てれば trap 防げる」前提が誤りだった
#
#   X. CAPTURE_DIAGNOSTIC — $out = & cmd 2>&1 | Out-String
#      踏み抜き: v0.1.7 (gh release view の "release not found" stderr で trap)
#      deprecation: v0.1.8 (anti-pattern として正式格下げ)
#      「Out-String を経由すれば Stop の判定対象外」前提が誤りだった
#
#   X. CAPTURE_STDOUT — $out = & cmd 2>$null
#      踏み抜き履歴なし、deprecation: v0.1.8
#      SUPPRESS_BOTH と同じ構造 (2>$null) を持つため同じ trap 形状。
#      本 script では vswhere で使用していたが v0.1.8 で helper に移行、
#      残存 call site ゼロのため anti-pattern 化
#
#   X. TRY_CATCH_NATIVE — try { & cmd 2>&1 | Out-String } catch { ... }
#      導入: v0.1.7 (CAPTURE_DIAGNOSTIC の trap 回避策として)
#      deprecation: v0.1.8 (Invoke-NativeWithCapture が同目的を関数化したため不要)
#
# PS 7.3+ 移行時の注意 (現状未対応):
#   PS 7.3 以降は $PSNativeCommandUseErrorActionPreference (default $true) が
#   追加され、`& native-cmd` の非ゼロ exit code 自体が ErrorActionPreference に
#   従って terminating error 化する。本スクリプトで残る `&` イディオムは
#   Assert-WorkingTreeClean の `git status --porcelain` のみ。これは PS 7.3+ で
#   `if ($LASTEXITCODE -ne 0)` に到達せず abort する。
#   (Invoke-NativeWithCapture / Invoke-ExternalProcess は Process 直叩きのため
#    $PSNativeCommandUseErrorActionPreference の影響を受けず、移行後も安全)
#   PS 7 移行時には以下のいずれかが必須:
#     (a) script 冒頭で $PSNativeCommandUseErrorActionPreference = $false に固定
#     (b) 残った `&` 系 (git status) も Invoke-NativeWithCapture に移行
#   現状は PS 5.1 を前提とした実装。
# ============================================================================

# -Offline / -DryRun は upload を行わないので preflight の gh 関連チェックも
# skip するため -SkipUpload を auto-promote する (Codex P2 #137 ×2)
if (($Offline -or $DryRun) -and -not $SkipUpload) {
    $SkipUpload = $true
}

# ----------------------------------------------------------------------------
# CHANGELOG.md から最新 Bundle エントリの version を取得して $Version に反映
# (RELEASE_VERSION ファイルは持たない、CHANGELOG が SoT)
# ----------------------------------------------------------------------------
$_changelogPathEarly = Join-Path $PSScriptRoot 'CHANGELOG.md'
if (-not (Test-Path $_changelogPathEarly)) {
    Write-Host "[FAIL] CHANGELOG.md が見つかりません: $_changelogPathEarly" -ForegroundColor Red
    exit 1
}
$_changelogContent = [System.IO.File]::ReadAllText($_changelogPathEarly, [System.Text.Encoding]::UTF8)
$_latestBundleMatch = [regex]::Match($_changelogContent, '(?m)^### \[Bundle v(\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?)\]')
if (-not $_latestBundleMatch.Success) {
    Write-Host "[FAIL] CHANGELOG.md に '### [Bundle vX.Y.Z]' エントリが見つかりません" -ForegroundColor Red
    exit 1
}
$_latestBundleVersion = $_latestBundleMatch.Groups[1].Value
if ($Version -eq "") {
    $Version = $_latestBundleVersion
} elseif ($Version -ne $_latestBundleVersion) {
    Write-Host "[FAIL] -Version 引数 ($Version) と CHANGELOG.md の最新 Bundle ($_latestBundleVersion) が一致しません" -ForegroundColor Red
    Write-Host "       CHANGELOG.md の最新 Bundle エントリを先頭に追加するか、-Version を省略してください" -ForegroundColor Red
    exit 1
}

# ============================================================================
# 定数: ツールチェイン pin（バージョン上げる時はこのセクションを書き換える）
# ============================================================================

# Godot patch table: major.minor → 採用する patch + SHA256
# 新しい patch を採用したい時は新しい行を追加 + SPEC §3.7.7 のルールに従って PR
$GodotPatchTable = @{
    '4.6' = @{
        Patch  = '4.6.2'
        # SHA256SUMS.txt は GitHub Releases に同梱されている。初回 DL 時に取得して比較する。
        # 値が空文字なら検証 skip + warn（初回 setup 用）。値を確定したら埋める運用。
        Sha256Editor    = ''
        Sha256Templates = ''
    }
}

# NuGet pinned version (Microsoft 配信、breaking 履歴あるので pin 必須)
$NugetPinnedVersion = '6.10.0'

# ============================================================================
# パス定義
# ============================================================================

$RepoRoot     = $PSScriptRoot
$LauncherDir  = Join-Path $RepoRoot 'GCTonePrism_Launcher'
$ManagerDir   = Join-Path $RepoRoot 'GCTonePrism_Manager'
$ToolsDir     = Join-Path $RepoRoot 'tools'
$ToolsGodot   = Join-Path $ToolsDir 'godot'
$StagingRoot  = Join-Path $RepoRoot 'release'
$StagingDir   = Join-Path $StagingRoot "v$Version"
$FilesDir     = Join-Path $StagingDir 'files'
$ZipPath      = Join-Path $StagingRoot "GCTonePrism_v$Version.zip"
$ChangelogPath = Join-Path $RepoRoot 'CHANGELOG.md'

$script:ResolvedGodot       = $null
$script:ResolvedMsBuild     = $null
$script:ResolvedNuget       = $null
$script:GodotPatchUsed      = $null  # 解決された Godot patch (例: "4.6.2")
$script:LauncherVersion     = $null
$script:ManagerVersion      = $null
$script:DeleteExistingRelease = $false

# ============================================================================
# 出力ユーティリティ
# ============================================================================

function Write-Step { param([string]$Message)
    Write-Host ""; Write-Host "==> $Message" -ForegroundColor Cyan
}
function Write-Info { param([string]$Message) Write-Host "    $Message" -ForegroundColor Gray }
function Write-Ok   { param([string]$Message) Write-Host "    [OK] $Message" -ForegroundColor Green }
function Write-Warn { param([string]$Message) Write-Host "    [WARN] $Message" -ForegroundColor Yellow }
function Fail       { param([string]$Message)
    Write-Host ""; Write-Host "[FAIL] $Message" -ForegroundColor Red; exit 1
}

function Get-ToolPath { param([string]$Name)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source } else { return $null }
}

# ============================================================================
# Native command の罠なし呼び出し (冒頭の「Native command 呼び出しの方針」セクション参照)
# ============================================================================

function Invoke-NativeWithCapture {
    <#
    .SYNOPSIS
        Native command を実行し、stdout / stderr / exit code を罠なく取得する。
    .DESCRIPTION
        PowerShell の call operator `&` は「exit 非ゼロ + stderr 出力」の native
        command で NativeCommandError を生成し、$ErrorActionPreference='Stop' 下で
        terminating error として throw する。`2>&1 | Out-String` も同じ罠を踏む
        (Out-String が処理する前に PS の error stream で trap が発火)。本関数は
        System.Diagnostics.Process で stdout/stderr を直接捕捉し、PS の error
        pipeline を経由しないため罠を完全に回避できる。

        失敗 path で stderr を出すコマンド (例: gh release view 不在時 exit 1 +
        "release not found"、gh auth status 未認証時 exit 1) の preflight 系
        チェックは本関数を使うこと。
    .OUTPUTS
        PSCustomObject:
          StdOut   - 標準出力 (string、UTF-8 として decode)
          StdErr   - 標準エラー (string、UTF-8 として decode)
          ExitCode - プロセスの終了コード (int)
          Combined - StdOut + "`n" + StdErr 連結 (検索用、stdout 末尾の改行有無で
                     joining 境界がぶれないよう明示 separator)
        注 1: 子プロセス側が UTF-8 で書く前提。`gh` はデフォルトで UTF-8。
        `vswhere` はデフォルト system codepage 出力なので、本 helper 経由の
        call site では `-utf8` フラグを必ず付与して UTF-8 出力を強制する
        (Resolve-MsBuild 内、日本語 VS install path 等の非 ASCII path も正しく
        decode)。msbuild / nuget は OEM 系 codepage を出す可能性があるため
        本 helper は使わず、Invoke-ExternalProcess (直 console 出力 + encoding
        再ピン留め) 経由で扱う。
        注 2: 本関数は System.Diagnostics.Process 直叩きのため、PS 自動変数の
        `$LASTEXITCODE` は **更新されない**。caller は必ず返り値の `.ExitCode` を
        参照すること。helper 呼び出し直後に `if ($LASTEXITCODE -ne 0)` と書くと、
        その前の `&` 呼び出しの古い値を読む silent failure path になるため厳禁。
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        # gh release create のように TTY 検出で進捗描画を OFF にするコマンド向け。
        # ハングしてないことを示す経過秒数を `\r` 上書きで表示。
        [switch]$ShowProgress,
        [string]$ProgressMessage = "実行中..."
    )
    # 引数 quoting は Invoke-ExternalProcess と同じ規則
    # 既知制約: trailing backslash を持つパス引数 (例: "C:\path\") は壊れる。
    #   `"C:\path\"` に変換すると CommandLineToArgvW の規則上 `\"` が閉じ引用符と
    #   して機能せず、引数のパースが破損する (\ × N + " → \ × (N/2) + " デコード)。
    #   現 call site (zip / 一時 notes / vswhere) はいずれも trailing backslash を
    #   持たないため安全。新規 call site でディレクトリパスを渡す場合は要注意。
    # TODO (post v0.1.8): 共通化候補。MSVC argv 規則の特殊ケース (\ を引用直前) で
    #                     Invoke-ExternalProcess との 2 箇所が silent に divergence
    #                     する危険、共通 helper 切り出し時に CommandLineToArgvW 規則
    #                     準拠 (\ × N + " → \\ × N + \") に置換
    $quoted = $Arguments | ForEach-Object {
        if ($_ -match '\s|"') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = $FilePath
    $psi.Arguments              = $quoted -join ' '
    $psi.UseShellExecute        = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding  = [System.Text.Encoding]::UTF8

    $proc = $null
    try {
        # Process.Start 自体が Win32Exception を出す path (file not found / 権限不足)。
        # caller は Get-ToolPath で事前 check しているが、helper 単体としても
        # 例外メッセージに何を実行しようとしたか含める
        try {
            $proc = [System.Diagnostics.Process]::Start($psi)
        } catch {
            throw "Invoke-NativeWithCapture: '$FilePath' の起動に失敗しました: $($_.Exception.Message)"
        }

        # 両 stream を async で読みデッドロック回避 (片方のバッファが満杯になると
        # child が block する)。WaitForExit() は timeout なし → network hang する
        # コマンド (gh API 呼び出し) では preflight が無限待機する path が残る
        # → TODO (post v0.1.8): -TimeoutSeconds 引数 + WaitForExit($ms) への移行
        $outTask = $proc.StandardOutput.ReadToEndAsync()
        $errTask = $proc.StandardError.ReadToEndAsync()

        # 非 TTY (CI / log file redirect 等) では `\r` が行リセットとして機能せず、
        # progress 更新ごとに改行付きで log に展開されてノイズになる。
        # IsOutputRedirected で検出して live progress を抑止 (取得自体が例外を投げる
        # 非 console host も同様に redirected 扱いで skip = 安全側)
        $isRedirected = try { [Console]::IsOutputRedirected } catch { $true }
        if ($ShowProgress -and -not $isRedirected) {
            # 進捗描画なしコマンド (gh release create 等) のための擬似 progress。
            # 500ms 間隔で経過秒数を `\r` 上書き表示 (CR で行頭戻り、新しい数字で上書き)
            # clear 幅は console window 幅から動的算出。現在の ProgressMessage 長
            # (~20 chars + zip サイズ + elapsed) では全角文字含めても WindowWidth-1
            # 幅の空白で覆える前提に依存している (全角文字の 2 cell 幅を構造的に
            # 補正してはいない、長文 ProgressMessage を追加する場合は実 cell 幅
            # 計算 = ASCII 1 + 全角 2 への切替を検討する)。
            # Fallback 条件 (どちらも 120 cells に置換):
            #   - WindowWidth 取得が例外を投げる (ISE 等の非 console host)
            #   - WindowWidth - 1 が 30 未満 (例: WindowWidth=0 を返す non-console host /
            #     極端に狭い実コンソール)。30 は ProgressMessage 等が最低限収まる threshold
            $clearWidth = try { [Console]::WindowWidth - 1 } catch { 120 }
            if ($clearWidth -lt 30) { $clearWidth = 120 }
            $clearLine = ' ' * $clearWidth
            $start = Get-Date
            while (-not $proc.HasExited) {
                $elapsed = [int]((Get-Date) - $start).TotalSeconds
                Write-Host -NoNewline "`r    $ProgressMessage ${elapsed}s 経過"
                Start-Sleep -Milliseconds 500
            }
            # progress 行を完全に消して cursor を行頭へ (後続の出力が綺麗に流れる)
            Write-Host -NoNewline "`r$clearLine`r"
        }
        # 両 path 合流点の WaitForExit() の役割は path 毎に異なる:
        #   ShowProgress 経路: HasExited ループ後の no-op、表面的対称性のみ
        #   非 ShowProgress 経路: プロセス完了を明示待機する唯一のポイント
        # どちらも削除不可。前者は対称性 / 保険、後者は機能上の必須。
        #
        # 注: WaitForExit() (no-arg) のパイプバッファフラッシュ保証は
        # BeginOutputReadLine / BeginErrorReadLine (event-based 非同期) に対する
        # ものであり、本実装が使う ReadToEndAsync (Task-based) には適用されない。
        # 実際のフラッシュ保証は直後の $outTask.Result / $errTask.Result が EOF まで
        # ブロックすることで提供される。`.Result` を削除すると出力切り捨てバグが
        # 発生するため touch しないこと
        $proc.WaitForExit()

        # AggregateException (faulted task) は明示メッセージで rethrow し、何が
        # 失敗したか caller に伝える
        try {
            $stdout = $outTask.Result
            $stderr = $errTask.Result
        } catch {
            throw "Invoke-NativeWithCapture: '$FilePath' の出力読み取りに失敗しました: $($_.Exception.Message)"
        }

        # Combined の separator 判定 (stdout の末尾改行を軸に 2 分岐):
        #   stdout 空 or 末尾 \n あり  → 単純連結 ($stdout + $stderr)
        #     - stdout 空 + stderr 有  → $stderr そのまま (Combined 先頭 = stderr、^regex effective)
        #     - 末尾 \n あり + stderr 有 → 改行はすでに含まれる
        #     - stderr 空              → $stdout だけ (trailing \n 有無は stdout 次第)
        #   stdout 末尾 \n なし        → `\n` を明示挟む ($stdout + "`n" + $stderr)
        #     - stderr 有              → ^pattern 取りこぼし防止
        #     - stderr 空              → Combined 末尾に trailing \n が付く (caller が
        #                                TrimEnd() で吸収する想定、現 call site は全て適用済み)
        if ($stdout -and -not $stdout.EndsWith("`n")) {
            $combined = $stdout + "`n" + $stderr
        } else {
            $combined = $stdout + $stderr
        }

        return [PSCustomObject]@{
            StdOut   = $stdout
            StdErr   = $stderr
            ExitCode = $proc.ExitCode
            Combined = $combined
        }
    } finally {
        # Process オブジェクトは Dispose 必須 (handle leak 防止)
        if ($null -ne $proc) {
            $proc.Dispose()
        }
    }
}

# ============================================================================
# Download with retry + SHA256 verification
# ============================================================================

function Invoke-DownloadWithRetry {
    param(
        [string]$Url,
        [string]$OutFile,
        [string]$ExpectedSha256 = '',
        [int]$MaxRetries = 3
    )
    if ($Offline) {
        Fail "Offline モード時はダウンロードできません: $Url"
    }

    # HttpClient によるチャンク読み出し + 50MB / 5 秒ごとの進捗表示
    # PS 5.1 標準の Invoke-WebRequest はプログレスバー描画バグで DL が極端に遅くなるため不採用
    Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue

    $tempFile = "$OutFile.partial"
    for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
        try {
            Write-Info "DL [$attempt/$MaxRetries]: $Url"
            $startTime = Get-Date

            $client = New-Object System.Net.Http.HttpClient
            $client.Timeout = [System.TimeSpan]::FromMinutes(15)
            try {
                $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
                if (-not $response.IsSuccessStatusCode) {
                    throw "HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)"
                }

                $total = $response.Content.Headers.ContentLength
                $totalMb = if ($total) { [math]::Round($total / 1MB, 0) } else { 0 }
                Write-Info "サイズ: $totalMb MB"

                $netStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                $fileStream = [System.IO.File]::Create($tempFile)
                try {
                    $buffer = New-Object byte[] 81920  # 80KB chunks
                    $received = 0
                    $lastReportBytes = 0
                    $lastReportTime = $startTime
                    while (($read = $netStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $fileStream.Write($buffer, 0, $read)
                        $received += $read

                        # 50MB or 5 秒経過で進捗表示
                        $elapsed = (Get-Date) - $lastReportTime
                        if (($received - $lastReportBytes) -ge 50MB -or $elapsed.TotalSeconds -ge 5) {
                            $totalElapsed = (Get-Date) - $startTime
                            $mb = [math]::Round($received / 1MB, 0)
                            $speedMbs = if ($totalElapsed.TotalSeconds -gt 0) { [math]::Round(($received / 1MB) / $totalElapsed.TotalSeconds, 1) } else { 0 }
                            if ($total) {
                                $pct = [math]::Round(($received / $total) * 100, 0)
                                Write-Host "        $pct% ($mb / $totalMb MB, $speedMbs MB/s)" -ForegroundColor DarkGray
                            } else {
                                Write-Host "        $mb MB ($speedMbs MB/s)" -ForegroundColor DarkGray
                            }
                            $lastReportBytes = $received
                            $lastReportTime = Get-Date
                        }
                    }
                } finally {
                    $fileStream.Close()
                    $netStream.Close()
                }
            } finally {
                $client.Dispose()
            }

            if (-not (Test-Path $tempFile)) {
                throw "ダウンロード後にファイルが存在しません"
            }
            $finalElapsed = (Get-Date) - $startTime
            $finalMb = [math]::Round((Get-Item $tempFile).Length / 1MB, 1)
            $avgSpeed = if ($finalElapsed.TotalSeconds -gt 0) { [math]::Round($finalMb / $finalElapsed.TotalSeconds, 1) } else { 0 }
            Write-Info "DL 完了: $finalMb MB / $($finalElapsed.TotalSeconds.ToString('F1'))s (avg $avgSpeed MB/s)"

            if ($ExpectedSha256) {
                $actual = (Get-FileHash $tempFile -Algorithm SHA256).Hash.ToLowerInvariant()
                $expected = $ExpectedSha256.ToLowerInvariant()
                if ($actual -ne $expected) {
                    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
                    throw "SHA256 不一致 (expected=$expected, actual=$actual)"
                }
                Write-Info "SHA256 検証 OK"
            } else {
                Write-Warn "SHA256 ハッシュが未設定のため検証 skip"
            }

            Move-Item $tempFile $OutFile -Force
            return
        } catch {
            Write-Warn "DL 失敗 [$attempt/$MaxRetries]: $_"
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
            if ($attempt -eq $MaxRetries) {
                Fail "ダウンロードに $MaxRetries 回失敗しました: $Url`n        最後のエラー: $_"
            }
            Start-Sleep -Seconds (2 * $attempt)  # 2s, 4s, 6s
        }
    }
}

# ============================================================================
# Godot 解決: project.godot 読み取り → patch lookup → DL + キャッシュ
# ============================================================================

function Read-GodotMinorFromProject {
    $projectGodot = Join-Path $LauncherDir 'project.godot'
    if (-not (Test-Path $projectGodot)) {
        Fail "project.godot が見つかりません: $projectGodot"
    }
    $content = Get-Content $projectGodot -Raw
    # 例: config/features=PackedStringArray("4.6", "Forward Plus")
    $m = [regex]::Match($content, 'config/features=PackedStringArray\("(\d+\.\d+)"')
    if (-not $m.Success) {
        Fail "project.godot から Godot major.minor を抽出できませんでした"
    }
    return $m.Groups[1].Value
}

function Resolve-Godot {
    Write-Step "Godot 実行ファイルを解決"

    # project.godot から major.minor を取得し、patch を決定（-GodotExe 指定時も templates 確認のため必要）
    $minor = Read-GodotMinorFromProject
    Write-Info "project.godot 由来の Godot: $minor 系"

    $patch = $null
    $sha256Editor = ''
    $sha256Templates = ''
    if ($GodotPatch -ne "") {
        $patch = $GodotPatch
        Write-Warn "Godot patch を手動上書き: $patch (SHA256 検証 skip)"
    } elseif ($GodotPatchTable.ContainsKey($minor)) {
        $entry = $GodotPatchTable[$minor]
        $patch = $entry.Patch
        $sha256Editor = $entry.Sha256Editor
        $sha256Templates = $entry.Sha256Templates
        Write-Info "patch table から解決: $patch"
    } else {
        Fail "Godot $minor 用の patch が `$GodotPatchTable に未登録です。`n        Release.ps1 冒頭の定数を編集して patch + SHA256 を登録してください。`n        または -GodotPatch <version> で一時的に指定できます。"
    }
    $script:GodotPatchUsed = $patch

    # エディタ解決: -GodotExe 優先、なければキャッシュ確認 → 自動 DL
    if ($GodotExe -ne "") {
        if (-not (Test-Path $GodotExe)) {
            Fail "指定された Godot が見つかりません: $GodotExe"
        }
        $script:ResolvedGodot = $GodotExe
        Write-Ok "Godot エディタ (手動指定): $GodotExe"
    } else {
        $godotVerDir = Join-Path $ToolsGodot $patch
        $godotExePath = Join-Path $godotVerDir "Godot_v$patch-stable_win64.exe"
        if (Test-Path $godotExePath) {
            Write-Ok "Godot エディタ キャッシュ命中: $godotExePath"
            $script:ResolvedGodot = $godotExePath
        } else {
            if ($Offline) {
                Fail "Offline モード時は Godot キャッシュが必要です: $godotVerDir"
            }
            Write-Info "Godot エディタを DL: v$patch"
            if (-not (Test-Path $godotVerDir)) {
                New-Item -ItemType Directory -Path $godotVerDir -Force | Out-Null
            }
            $editorZipUrl = "https://github.com/godotengine/godot/releases/download/$patch-stable/Godot_v$patch-stable_win64.exe.zip"
            $editorZip    = Join-Path $godotVerDir "Godot_v$patch-stable_win64.exe.zip"
            Invoke-DownloadWithRetry -Url $editorZipUrl -OutFile $editorZip -ExpectedSha256 $sha256Editor
            Write-Info "展開中..."
            Expand-Archive -Path $editorZip -DestinationPath $godotVerDir -Force
            Remove-Item $editorZip -Force
            if (-not (Test-Path $godotExePath)) {
                Fail "展開後の Godot exe が見つかりません: $godotExePath"
            }
            Write-Ok "Godot エディタ展開完了: $godotExePath"
            $script:ResolvedGodot = $godotExePath
        }
    }

    # Export templates は %APPDATA%/Godot/export_templates/<patch>.stable/ にあるか必ず確認
    # -GodotExe を指定していてもエディタ単体ではエクスポートできないため
    $godotVerDir = Join-Path $ToolsGodot $patch  # templates DL 用の作業ディレクトリ
    if (-not (Test-Path $godotVerDir)) {
        New-Item -ItemType Directory -Path $godotVerDir -Force | Out-Null
    }
    $templatesDir = Join-Path $env:APPDATA "Godot\export_templates\$patch.stable"
    if (Test-Path (Join-Path $templatesDir 'windows_release_x86_64.exe')) {
        Write-Ok "Templates キャッシュ命中: $templatesDir"
    } else {
        if ($Offline) {
            Fail "Offline モード時は templates キャッシュが必要です: $templatesDir"
        }
        Write-Info "Templates を DL: v$patch"
        if (-not (Test-Path $templatesDir)) {
            New-Item -ItemType Directory -Path $templatesDir -Force | Out-Null
        }
        $tpzUrl = "https://github.com/godotengine/godot/releases/download/$patch-stable/Godot_v$patch-stable_export_templates.tpz"
        $tpzPath = Join-Path $godotVerDir "Godot_v$patch-stable_export_templates.tpz"
        Invoke-DownloadWithRetry -Url $tpzUrl -OutFile $tpzPath -ExpectedSha256 $sha256Templates
        # .tpz は zip 形式。中身は templates/ ディレクトリ配下にファイルが並ぶ
        $extractTemp = Join-Path $godotVerDir 'tpz_extract'
        if (Test-Path $extractTemp) { Remove-Item -Recurse -Force $extractTemp }
        # .tpz を一度 .zip に rename して Expand-Archive で展開
        $tpzAsZip = "$tpzPath.zip"
        Copy-Item $tpzPath $tpzAsZip -Force
        Expand-Archive -Path $tpzAsZip -DestinationPath $extractTemp -Force
        # 展開構造: $extractTemp/templates/*.* を $templatesDir にコピー
        $tpzInner = Join-Path $extractTemp 'templates'
        if (-not (Test-Path $tpzInner)) {
            Fail "templates 展開構造が想定外: $extractTemp 配下に 'templates' ディレクトリがありません"
        }
        Get-ChildItem $tpzInner | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $templatesDir $_.Name) -Force
        }
        Remove-Item -Recurse -Force $extractTemp
        Remove-Item $tpzPath -Force
        Remove-Item $tpzAsZip -Force
        # Release.ps1 管理下を識別するためのマーカーファイル
        # Clear-OldGodot で AppData の古い templates を安全に掃除する根拠
        Set-Content -Path (Join-Path $templatesDir '.gctone_managed') -Value "Release.ps1 v$Version installed at $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))" -NoNewline
        Write-Ok "Templates 展開完了: $templatesDir"
    }
}

# ============================================================================
# MSBuild 解決: Windows 同梱の .NET Framework MSBuild を使う
# ============================================================================

function Resolve-MsBuild {
    Write-Step "MSBuild を解決"

    if ($MsBuildExe -ne "") {
        if (-not (Test-Path $MsBuildExe)) {
            Fail "指定された MSBuild が見つかりません: $MsBuildExe"
        }
        $script:ResolvedMsBuild = $MsBuildExe
        Write-Ok "MSBuild (手動指定): $MsBuildExe"
        return
    }

    # Manager は C# 7+ (ValueTuple / string interpolation / out var 等) を使うため
    # Roslyn 2.x+ → MSBuild 14+ が必須。Windows 同梱 .NET Framework 4.0 MSBuild は
    # csc が古すぎてビルド不可。Visual Studio 2019+ または Build Tools が必要。
    #
    # 優先順位:
    # 1. Visual Studio / Build Tools (vswhere 経由)
    # 2. PATH 上の msbuild

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { $vswhere = "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe" }
    if (Test-Path $vswhere) {
        # vswhere は検索 hit なし時に exit 0 + 空 stdout を返すが、実行環境破損
        # (権限 / インストーラ破損 等) で exit 非ゼロ + stderr 出力する可能性もある。
        # `-utf8` 必須: Invoke-NativeWithCapture は stdout を UTF-8 として decode する
        # ため、vswhere の default (system codepage) 出力だと non-UTF-8 locale で
        # non-ASCII install path (日本語 VS install path 等) が mojibake → 後段の
        # Test-Path が失敗 → MSBuild 検出失敗の path に落ちる
        $vswhereResult = Invoke-NativeWithCapture -FilePath $vswhere `
            -Arguments @('-latest', '-products', '*', '-requires', 'Microsoft.Component.MSBuild', '-property', 'installationPath', '-utf8')
        # vswhere 失敗時は warn して PATH fallback path に落とす (Fail にはしない —
        # PATH に msbuild があれば release は続行できるため)。コメント上で「stderr
        # 出力する可能性」を挙げた以上、その出力をユーザーに届けないと silent pass
        if ($vswhereResult.ExitCode -ne 0) {
            Write-Warn "vswhere が exit $($vswhereResult.ExitCode) で終了 (権限 / VS Installer 破損等の可能性):"
            $vswhereStderr = $vswhereResult.StdErr.TrimEnd()
            if ($vswhereStderr -match '\S') { Write-Warn $vswhereStderr }
            Write-Info "PATH fallback に切替えて MSBuild を探します"
            # 失敗時の StdOut は意図的に破棄: 部分出力 (権限エラーで途中まで吐いた
            # 不完全 path 等) を後段の Test-Path 経路に流すと「PATH fallback に切替」
            # 宣言と矛盾、`if ($vsInstall)` ブロックを silent skip するために明示クリア
            $vsInstall = ''
        } else {
            $vsInstall = $vswhereResult.StdOut.Trim()
        }
        if ($vsInstall) {
            $vsCands = @(
                (Join-Path $vsInstall 'MSBuild\Current\Bin\MSBuild.exe'),
                (Join-Path $vsInstall 'MSBuild\17.0\Bin\MSBuild.exe'),
                (Join-Path $vsInstall 'MSBuild\15.0\Bin\MSBuild.exe')
            )
            foreach ($c in $vsCands) {
                if (Test-Path $c) {
                    $script:ResolvedMsBuild = $c
                    Write-Ok "MSBuild (Visual Studio): $c"
                    return
                }
            }
        }
    }

    # fallback: PATH 上の msbuild
    $cmd = Get-ToolPath 'msbuild'
    if ($cmd) {
        $script:ResolvedMsBuild = $cmd
        Write-Ok "MSBuild (PATH): $cmd"
        return
    }

    # MSBuild なし → 詳細な install ガイドを出して fail
    Fail @"
MSBuild が見つかりません。Manager は C# 7+ を使うため、Visual Studio 2019+ または
Visual Studio Build Tools のインストールが必要です。

【インストール方法】
1. https://visualstudio.microsoft.com/downloads/ にアクセス
2. 「Visual Studio 2022 Build Tools」または「Visual Studio Community」をダウンロード
   - Build Tools のみ (軽量、~1-2GB): https://aka.ms/vs/17/release/vs_BuildTools.exe
3. インストーラ起動後、ワークロード選択画面で「.NET デスクトップビルドツール」にチェック
4. インストール完了後、Release.ps1 を再実行

【インストールしたくない場合】
- -MsBuildExe で MSBuild.exe のフルパスを指定することも可能
- ただし MSBuild 14+ (VS 2015 以降相当) でないと Manager はビルドできない
"@
}

# ============================================================================
# NuGet 解決: tools/nuget.exe にピン留めバージョンをキャッシュ
# ============================================================================

function Resolve-Nuget {
    Write-Step "NuGet を解決"

    if ($NugetExe -ne "") {
        if (-not (Test-Path $NugetExe)) {
            Fail "指定された NuGet が見つかりません: $NugetExe"
        }
        $script:ResolvedNuget = $NugetExe
        Write-Ok "NuGet (手動指定): $NugetExe"
        return
    }

    $localNuget = Join-Path $ToolsDir "nuget-$NugetPinnedVersion.exe"
    if (Test-Path $localNuget) {
        $script:ResolvedNuget = $localNuget
        Write-Ok "NuGet キャッシュ命中: $localNuget"
        return
    }

    if ($Offline) {
        Fail "Offline モード時は NuGet キャッシュが必要です: $localNuget"
    }

    if (-not (Test-Path $ToolsDir)) {
        New-Item -ItemType Directory -Path $ToolsDir | Out-Null
    }
    # NuGet も SHA256 検証したいが、Microsoft が SHA256SUMS を提供していないため skip
    $url = "https://dist.nuget.org/win-x86-commandline/v$NugetPinnedVersion/nuget.exe"
    Write-Info "NuGet を DL (ピン留め: v$NugetPinnedVersion)"
    Invoke-DownloadWithRetry -Url $url -OutFile $localNuget
    $script:ResolvedNuget = $localNuget
    Write-Ok "NuGet DL 完了: $localNuget"
}

# ============================================================================
# git working tree が clean か検証 (Codex P1 #137 への対応)
# Set-ManifestVersions が project.godot / export_presets.cfg を書き換えうるため、
# preflight と sync 後の 2 回呼ぶ
# ============================================================================

function Assert-WorkingTreeClean {
    param([string]$Context)
    # pattern: CAPTURE_STDOUT_PASS_STDERR (冒頭集約コメント参照)
    # stdout (--porcelain の差分行) は変数に capture、stderr (git の通常 error
    # メッセージ) は console 直書きでユーザーに見せる
    # redirect を外しただけだと git 失敗時に $gitStatus が空文字になり「working
    # tree clean」と誤判定されるため、exit code チェックが必須
    $gitStatus = & git -C $RepoRoot status --porcelain
    if ($LASTEXITCODE -ne 0) {
        Fail "git status の実行に失敗しました (exit code: $LASTEXITCODE, context: $Context)"
    }
    if ($gitStatus) {
        if ($Force) {
            Write-Warn "uncommitted change がありますが -Force のため続行 ($Context)"
            ($gitStatus -split "`n") | ForEach-Object { Write-Info "    $_" }
        } else {
            Write-Host ""
            Write-Host "    uncommitted change が検出されました ($Context):" -ForegroundColor Yellow
            ($gitStatus -split "`n") | ForEach-Object { Write-Host "        $_" -ForegroundColor Yellow }
            if ($Context -like '*sync 後*') {
                Fail "Set-ManifestVersions が tracked files を書き換えました。差分をコミットしてから再実行してください (一度書き換えれば idempotent なので次回 sync は no-op)。-Force でバイパス可能。"
            } else {
                Fail "コミットしてから再実行するか、-Force で続行してください。"
            }
        }
    } else {
        Write-Ok "git working tree clean ($Context)"
    }
}

# ============================================================================
# CHANGELOG.md から [Bundle v<Version>] セクションを抽出
# ============================================================================

function Get-BundleReleaseNotes {
    param([switch]$AllowMissing)
    if (-not (Test-Path $ChangelogPath)) {
        if ($AllowMissing) { return '' }
        Fail "CHANGELOG.md が見つかりません: $ChangelogPath"
    }
    $content = [System.IO.File]::ReadAllText($ChangelogPath, [System.Text.Encoding]::UTF8)
    # `### [Bundle v0.1.0] - YYYY-MM-DD` から次の `### ` / `---` / `## ` / EOF まで
    # `\Z`: 後続セクション無しの初回 Bundle release だけが該当する保険
    $pattern = '(?ms)^### \[Bundle v' + [regex]::Escape($Version) + '\][^\r\n]*\r?\n(.*?)(?=^### |^---|^## |\Z)'
    $m = [regex]::Match($content, $pattern)
    if (-not $m.Success) { return '' }
    return $m.Groups[1].Value.Trim()
}

$script:ReleaseNotesText = ''

# ============================================================================
# Phase 0: Preflight
# ============================================================================

function Assert-Preflight {
    Write-Step "Preflight: 環境とパラメータを検証"

    # Version 形式 (CHANGELOG から取った時点で確定済みだが念のため)
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') {
        Fail "Version 形式が不正です（期待: M.m.p または M.m.p-suffix）: $Version"
    }
    Write-Ok "Bundle version (CHANGELOG 由来): $Version"

    # gh CLI
    if (-not $SkipUpload) {
        $gh = Get-ToolPath 'gh'
        if (-not $gh) {
            Fail "gh (GitHub CLI) が見つかりません。`n        https://cli.github.com/ からインストールするか、-SkipUpload で upload を抑止してください。"
        }
        Write-Ok "gh: $gh"
        # gh auth status は未認証時 exit 1 + stderr 出力 → Invoke-NativeWithCapture で
        # 罠なく捕捉。失敗原因は多様 (未認証 / token expiry / network / proxy /
        # gh install 破損)、Combined を Fail メッセージに含めて切り分け可能にする
        $authResult = Invoke-NativeWithCapture -FilePath 'gh' -Arguments @('auth', 'status')
        if ($authResult.ExitCode -ne 0) {
            Fail "gh 認証に失敗しました (`gh auth login` を実行 / network / proxy / token expiry 等を確認)。`n$($authResult.Combined.TrimEnd())"
        }
        Write-Ok "gh 認証 OK"
        # gh は exit 0 でも stderr に early warning を出すことがある (token scope 不足の
        # 予兆 / token expiry 近接通知 等)。Fail はしないが、リリース担当に気付かせる。
        # 検出は `^warning:` (gh 公式の warning prefix) のみに絞る (token expiry 特殊形式
        # まで網羅する regex は false positive 多 + gh 出力フォーマット変更に脆弱)。
        # warning は実態として stderr 出力なので StdErr を直接見る (Combined だと
        # stdout 末尾改行有無で行境界がブレうる)
        if ($authResult.StdErr -match '(?im)^warning:') {
            Write-Warn "gh auth status からの警告 (release 実行自体は継続):`n$($authResult.StdErr.TrimEnd())"
        }
    }

    # CHANGELOG から Bundle セクションを抽出できるか確認
    # (release_notes は CHANGELOG が SoT、Release.ps1 が抜き出して --notes に渡す)
    if (-not $SkipUpload) {
        $notes = Get-BundleReleaseNotes
        if (-not $notes) {
            Fail "CHANGELOG.md に [Bundle v$Version] セクションが見つかりません。`n        CHANGELOG.md の `## Bundle` 配下に該当 entry を追加してから再実行してください。"
        }
        $script:ReleaseNotesText = $notes
        Write-Ok "CHANGELOG から Bundle v$Version セクションを抽出 ($($notes.Length) 文字)"
    } else {
        $notes = Get-BundleReleaseNotes -AllowMissing
        if ($notes) {
            $script:ReleaseNotesText = $notes
            Write-Info "CHANGELOG Bundle セクション検出: $($notes.Length) 文字 (-SkipUpload のため未使用)"
        } else {
            Write-Warn "CHANGELOG に [Bundle v$Version] セクション未追加（-SkipUpload のためスキップ）"
            $script:ReleaseNotesText = ''
        }
    }

    # uncommitted change
    Assert-WorkingTreeClean -Context "preflight"

    # 既存リリースとのタグ衝突
    if (-not $SkipUpload) {
        # gh release view は release 不在時に exit 1 + stderr "release not found" を出す。
        # Invoke-NativeWithCapture で stdout/stderr/exit を罠なく分離捕捉。
        # `--json id`: 存在時の stdout を最小化 (capture 文字列が小さくなる、parse はしない)。
        # 存在判定は exit code を一次軸、stderr 文字列マッチは「不在 vs 別種失敗」の識別のみ。
        $releaseResult = Invoke-NativeWithCapture -FilePath 'gh' `
            -Arguments @('release', 'view', "v$Version", '--json', 'id')

        if ($releaseResult.ExitCode -eq 0) {
            # 既存 release あり
            if ($Force) {
                Write-Warn "タグ v$Version は既存だが -Force のため後で削除して作り直す"
                $script:DeleteExistingRelease = $true
            } else {
                Fail "GitHub Releases に v$Version が既存です。-Force で削除して作り直すか、別バージョンで実行してください。"
            }
        } else {
            # exit 非ゼロ → 不在 or 別種失敗。stderr の英文字列で識別
            # (注: gh の i18n / 文言変更で取りこぼし可能性あり、その時は再 trapping。
            #  本格的な structured 検出は --json + ConvertFrom-Json + HTTP 404 判定 etc.)
            if ($releaseResult.StdErr -match 'release not found') {
                Write-Ok "タグ衝突なし: v$Version"
                $script:DeleteExistingRelease = $false
            } else {
                # 別種の失敗 (auth / network / API rate limit / gh install 破損 等)
                # zip ビルド完了後の gh release create で fail するより、preflight で
                # 早期 fail させて時間 / 計算資源を浪費しない
                Fail "gh release view が予期せず失敗しました (exit $($releaseResult.ExitCode)):`n$($releaseResult.Combined.TrimEnd())"
            }
        }
    }
}

# ============================================================================
# Phase 1: Component versions 読み取り
# ============================================================================

function Read-LauncherVersion {
    $versionGd = Join-Path $LauncherDir 'version.gd'
    if (-not (Test-Path $versionGd)) {
        Fail "version.gd が見つかりません: $versionGd"
    }
    $content = Get-Content $versionGd -Raw
    $major = [regex]::Match($content, 'const\s+MAJOR\s*:\s*int\s*=\s*(\d+)').Groups[1].Value
    $minor = [regex]::Match($content, 'const\s+MINOR\s*:\s*int\s*=\s*(\d+)').Groups[1].Value
    $patch = [regex]::Match($content, 'const\s+PATCH\s*:\s*int\s*=\s*(\d+)').Groups[1].Value
    if (-not $major -or -not $minor -or -not $patch) {
        Fail "version.gd から MAJOR/MINOR/PATCH を読み取れませんでした"
    }
    return "$major.$minor.$patch"
}

function Read-ManagerVersion {
    $assemblyInfo = Join-Path $ManagerDir 'Properties\AssemblyInfo.cs'
    if (-not (Test-Path $assemblyInfo)) {
        Fail "AssemblyInfo.cs が見つかりません: $assemblyInfo"
    }
    $content = Get-Content $assemblyInfo -Raw
    $m = [regex]::Match($content, 'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)')
    if (-not $m.Success) {
        Fail "AssemblyInfo.cs から AssemblyVersion を読み取れませんでした"
    }
    return $m.Groups[1].Value
}

function Read-ComponentVersions {
    Write-Step "コンポーネント version を読み取り"
    $script:LauncherVersion = Read-LauncherVersion
    $script:ManagerVersion = Read-ManagerVersion
    Write-Ok "Launcher: v$script:LauncherVersion"
    Write-Ok "Manager:  v$script:ManagerVersion"
}

# ============================================================================
# Phase 2: project.godot / export_presets.cfg を Launcher version で同期
# ============================================================================

function Write-FileUtf8NoBom {
    param([string]$Path, [string]$Content)
    # Godot ConfigFile / project.godot は BOM を識別子として解釈してパースエラーを起こすため
    # UTF-8 BOM なしで書き出す
    [System.IO.File]::WriteAllText($Path, $Content, $script:Utf8NoBomEncoding)
}

function Set-ManifestVersions {
    Write-Step "派生バージョン情報を同期 (Launcher v$script:LauncherVersion 基準)"

    $launcherVer = $script:LauncherVersion
    $fourPart = "$launcherVer.0"

    # project.godot: config/version
    $projectGodot = Join-Path $LauncherDir 'project.godot'
    $content = [System.IO.File]::ReadAllText($projectGodot, [System.Text.Encoding]::UTF8)
    $new = [regex]::Replace($content, '(config/version=")[^"]*(")', "`${1}$launcherVer`${2}")
    if ($new -ne $content) {
        Write-FileUtf8NoBom -Path $projectGodot -Content $new
        Write-Ok "project.godot config/version → $launcherVer"
    } else {
        Write-Info "project.godot config/version は既に $launcherVer"
    }

    # export_presets.cfg: application/file_version & product_version
    $exportPresets = Join-Path $LauncherDir 'export_presets.cfg'
    $content = [System.IO.File]::ReadAllText($exportPresets, [System.Text.Encoding]::UTF8)
    $new = [regex]::Replace($content, '(application/file_version=")[^"]*(")', "`${1}$fourPart`${2}")
    $new = [regex]::Replace($new,     '(application/product_version=")[^"]*(")', "`${1}$fourPart`${2}")
    if ($new -ne $content) {
        Write-FileUtf8NoBom -Path $exportPresets -Content $new
        Write-Ok "export_presets.cfg file_version/product_version → $fourPart"
    } else {
        Write-Info "export_presets.cfg は既に $fourPart"
    }
}

# ============================================================================
# Phase 3: Staging
# ============================================================================

function Clear-Staging {
    Write-Step "Staging エリアを初期化: $StagingDir"
    if (Test-Path $StagingDir) {
        Remove-Item -Recurse -Force $StagingDir
        Write-Info "既存 staging を削除"
    }
    New-Item -ItemType Directory -Path $StagingDir | Out-Null
    New-Item -ItemType Directory -Path $FilesDir | Out-Null
    Write-Ok "Staging 準備完了"
}

# ============================================================================
# Phase 4: Build Launcher
# ============================================================================

function Invoke-ExternalProcess {
    # Process.Start + WaitForExit で明示的に完了を待つ。
    # `&` (call operator) は大量の stdout/stderr で非同期 return することがあり、
    # Start-Process -ArgumentList は配列要素にスペースを含むと argument 分割する問題があるため、
    # ProcessStartInfo.Arguments を自前で quoting する形を採用。
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )
    # スペース・特殊文字を含む引数はダブルクォートで囲む
    $quoted = $Arguments | ForEach-Object {
        if ($_ -match '\s|"') {
            '"' + ($_ -replace '"', '\"') + '"'
        } else {
            $_
        }
    }
    $cmdLine = $quoted -join ' '

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $cmdLine
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $false  # コンソールに直接出力
    $psi.RedirectStandardError = $false

    $proc = $null
    try {
        # Process.Start 失敗 (file not found / 権限不足) を Invoke-NativeWithCapture
        # と同じ pattern で何を起動しようとしたかを含むメッセージで rethrow、
        # 両 helper の例外メッセージ粒度を対称化
        try {
            $proc = [System.Diagnostics.Process]::Start($psi)
        } catch {
            throw "Invoke-ExternalProcess: '$FilePath' の起動に失敗しました: $($_.Exception.Message)"
        }
        $proc.WaitForExit()
        $exitCode = $proc.ExitCode
    } finally {
        if ($null -ne $proc) { $proc.Dispose() }
        # Invoke-ExternalProcess 経由の子プロセス (Godot CLI / msbuild / nuget) が
        # コンソール encoding を OEM 系に戻して去ると、後続 Write-Host で日本語が
        # doubled rendering される PS 5.1 バグの予防的再ピン留め (script 冒頭の
        # pin と二重防御)。
        # 非 console host (CI / headless 等) で console API が IOException を投げる
        # ケースを try/catch で吸収 — finally は外部ツール呼び出しの度に走るため
        # ガードなしだと release flow が abort する。
        # 注: $OutputEncoding (PS variable、pipeline → native command 送信側) は
        # 子プロセスから変更不可なので再ピン留め不要、script 冒頭の代入が継続有効
        try {
            [Console]::OutputEncoding = $script:Utf8NoBomEncoding
        } catch {
            # non-console host では再ピン留め不要 (そもそも doubled rendering は
            # 対話端末でしか起きないため、CI 等では skip OK)
        }
    }
    return $exitCode
}

function Build-Launcher {
    Write-Step "Launcher を Godot CLI でエクスポート"

    $outDir = Join-Path $FilesDir 'GCTonePrism_Launcher'
    New-Item -ItemType Directory -Path $outDir | Out-Null
    $outExe = Join-Path $outDir 'GCTonePrism_Launcher.exe'

    Write-Info "出力先: $outExe"
    Write-Info "Godot: $script:ResolvedGodot"

    $exitCode = Invoke-ExternalProcess -FilePath $script:ResolvedGodot -Arguments @(
        '--headless',
        '--path', $LauncherDir,
        '--export-release', 'Windows Desktop',
        $outExe
    )
    if ($exitCode -ne 0) {
        Fail "Godot エクスポートに失敗しました (exit code: $exitCode)"
    }
    if (-not (Test-Path $outExe)) {
        Fail "Godot エクスポート完了したが exe が見つかりません: $outExe"
    }
    Write-Ok "Launcher exe 生成完了"

    Write-Info "出力ファイル一覧:"
    Get-ChildItem $outDir -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($outDir.Length + 1)
        Write-Host "        $rel ($($_.Length) bytes)" -ForegroundColor DarkGray
    }
}

# ============================================================================
# Phase 5: Build Manager
# ============================================================================

function Build-Manager {
    Write-Step "Manager を msbuild で Release ビルド"

    $csproj = Join-Path $ManagerDir 'GCTonePrism_Manager.csproj'
    $packagesDir = Join-Path $ManagerDir 'packages'
    $binRelease = Join-Path $ManagerDir 'bin\Release'

    # nuget restore
    Write-Info "nuget restore"
    $exitCode = Invoke-ExternalProcess -FilePath $script:ResolvedNuget -Arguments @(
        'restore', $csproj,
        '-PackagesDirectory', $packagesDir
    )
    if ($exitCode -ne 0) {
        Fail "nuget restore に失敗しました (exit code: $exitCode)"
    }
    Write-Ok "nuget restore 完了"

    # 既知問題: nuget restore (packages.config 形式) は Stub.System.Data.SQLite.Core.NetFramework
    # の build/net*/x64/ build/net*/x86/ サブディレクトリを展開しない場合がある。
    # 結果として SQLite.Interop.dll がビルド成果物に含まれず Manager が起動時にクラッシュする。
    # nupkg を直接 unzip して欠損を埋める防御策。
    $sqlitePkg = Join-Path $packagesDir 'Stub.System.Data.SQLite.Core.NetFramework.1.0.119.0'
    $sqliteInteropX64 = Join-Path $sqlitePkg 'build\net46\x64\SQLite.Interop.dll'
    if (Test-Path $sqlitePkg -PathType Container) {
        if (-not (Test-Path $sqliteInteropX64)) {
            Write-Info "Stub.System.Data.SQLite package の x64/x86 native DLL を nupkg から手動抽出"
            $nupkg = Join-Path $sqlitePkg 'Stub.System.Data.SQLite.Core.NetFramework.1.0.119.0.nupkg'
            if (-not (Test-Path $nupkg)) {
                Fail "nupkg が見つかりません: $nupkg"
            }
            # .nupkg は zip 形式
            Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
            $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
            try {
                foreach ($entry in $zip.Entries) {
                    # x64/x86 配下の SQLite.Interop.dll だけ抽出
                    if ($entry.FullName -match 'build/net4[5-9]+/x(64|86)/SQLite\.Interop\.dll$') {
                        $dest = Join-Path $sqlitePkg ($entry.FullName -replace '/', '\')
                        $destDir = Split-Path $dest -Parent
                        if (-not (Test-Path $destDir)) {
                            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
                        }
                        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true)
                        Write-Info "  抽出: $($entry.FullName)"
                    }
                }
            } finally {
                $zip.Dispose()
            }
            Write-Ok "Native DLL 手動抽出完了"
        } else {
            Write-Info "Stub.System.Data.SQLite native DLL 既存"
        }
    }

    # bin/Release/ を事前に削除 (前回ビルドの runtime ゴミ
    # = 開発者が Manager を直接起動した時に発生する db / logs / backups 等を
    # release zip に紛れ込ませないため)
    if (Test-Path $binRelease) {
        Write-Info "bin/Release/ を削除して clean build"
        Remove-Item -Recurse -Force $binRelease
    }

    # msbuild
    Write-Info "msbuild /p:Configuration=Release"
    $exitCode = Invoke-ExternalProcess -FilePath $script:ResolvedMsBuild -Arguments @(
        $csproj,
        '/p:Configuration=Release',
        '/verbosity:minimal',
        '/nologo'
    )
    if ($exitCode -ne 0) {
        Fail "msbuild に失敗しました (exit code: $exitCode)"
    }
    Write-Ok "msbuild 完了"

    # bin/Release/ から staging へコピー（*.pdb 除外）
    $outDir = Join-Path $FilesDir 'GCTonePrism_Manager'
    New-Item -ItemType Directory -Path $outDir | Out-Null
    if (-not (Test-Path $binRelease)) {
        Fail "Manager ビルド出力が見つかりません: $binRelease"
    }

    Get-ChildItem $binRelease -Recurse | Where-Object {
        -not $_.PSIsContainer -and $_.Extension -ne '.pdb'
    } | ForEach-Object {
        $rel = $_.FullName.Substring($binRelease.Length + 1)
        $dest = Join-Path $outDir $rel
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item $_.FullName $dest
    }
    Write-Ok "Manager 成果物コピー完了"

    Write-Info "出力ファイル一覧:"
    Get-ChildItem $outDir -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($outDir.Length + 1)
        Write-Host "        $rel ($($_.Length) bytes)" -ForegroundColor DarkGray
    }
}

# ============================================================================
# Phase 6: Install.bat / INSTALL_README.txt + Launcher.bat / Manager.bat 同梱
# ============================================================================
# zip 配布物の構造 (SPEC §3.7.1 正規 zip 構造):
#   zip ルート:    Install.bat / INSTALL_README.txt
#   zip files/:    Launcher.bat / Manager.bat (Install.bat が <インストール先>\GCTonePrism\
#                  にコピーする payload、ユーザーは GCTonePrism\Launcher.bat 等で日常起動)

function Copy-Templates {
    Write-Step "テンプレートを staging に同梱"

    # zip ルート配置
    $rootTemplates = @(
        @{ Src = 'templates\Install.bat';        Dest = 'Install.bat';        Label = 'Install.bat' },
        @{ Src = 'templates\INSTALL_README.txt'; Dest = 'INSTALL_README.txt'; Label = 'INSTALL_README.txt' }
    )
    # files/ 配下配置 (インストール後の GCTonePrism\Launcher.bat / Manager.bat になる)
    $filesTemplates = @(
        @{ Src = 'templates\Launcher.bat'; Dest = 'files\Launcher.bat'; Label = 'Launcher.bat (shortcut)' },
        @{ Src = 'templates\Manager.bat';  Dest = 'files\Manager.bat';  Label = 'Manager.bat (shortcut)' }
    )

    foreach ($tpl in ($rootTemplates + $filesTemplates)) {
        $src = Join-Path $RepoRoot $tpl.Src
        $dst = Join-Path $StagingDir $tpl.Dest
        if (-not (Test-Path $src)) {
            Fail "テンプレートが見つかりません: $src"
        }
        $dstDir = Split-Path $dst -Parent
        if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        Copy-Item $src $dst -Force
        Write-Ok "$($tpl.Label) 同梱"
    }
}

# ============================================================================
# Phase 7: ExpectedFiles 検証
# ============================================================================

function Assert-ExpectedFiles {
    Write-Step "ExpectedFiles 検証"

    # 期待ファイル一覧 (zip ルート + files/ 配下、SPEC §3.7.1 正規構造)
    $expected = @(
        # zip ルート
        'Install.bat',
        'INSTALL_README.txt',
        # files/ 配下 = インストール後の <親>\GCTonePrism\ に展開される payload
        'files\Launcher.bat',
        'files\Manager.bat',
        'files\GCTonePrism_Launcher\GCTonePrism_Launcher.exe',
        'files\GCTonePrism_Manager\GCTonePrism_Manager.exe',
        'files\GCTonePrism_Manager\GCTonePrism_Manager.exe.config',
        'files\GCTonePrism_Manager\System.Data.SQLite.dll',
        'files\GCTonePrism_Manager\Microsoft.WindowsAPICodePack.dll',
        'files\GCTonePrism_Manager\Microsoft.WindowsAPICodePack.Shell.dll',
        'files\GCTonePrism_Manager\x64\SQLite.Interop.dll',
        'files\GCTonePrism_Manager\x86\SQLite.Interop.dll'
    )

    $missing = @()
    foreach ($rel in $expected) {
        $abs = Join-Path $StagingDir $rel
        if (-not (Test-Path $abs)) { $missing += $rel }
    }

    if ($missing.Count -gt 0) {
        Write-Host ""
        Write-Host "    以下のファイルが staging に存在しません:" -ForegroundColor Red
        $missing | ForEach-Object { Write-Host "        $_" -ForegroundColor Red }
        Fail "ExpectedFiles 検証で漏れを検出しました ($($missing.Count) 件)"
    }
    Write-Ok "ExpectedFiles 全 $($expected.Count) 件 OK"
}

# ============================================================================
# Phase 8: New-Zip
# ============================================================================

function New-Zip {
    Write-Step "zip 化: $ZipPath"
    if (Test-Path $ZipPath) {
        Remove-Item -Force $ZipPath
        Write-Info "既存 zip を削除"
    }
    $items = Get-ChildItem $StagingDir
    Compress-Archive -Path ($items.FullName) -DestinationPath $ZipPath -CompressionLevel Optimal
    if (-not (Test-Path $ZipPath)) {
        Fail "zip 生成に失敗しました"
    }
    $sizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)
    Write-Ok "zip 生成完了: $sizeMb MB"
}

# ============================================================================
# Phase 10: gh release create
# ============================================================================

function Invoke-GhRelease {
    Write-Step "GitHub Releases にアップロード"

    if ($script:DeleteExistingRelease) {
        Write-Info "既存タグ v$Version を削除"
        $delResult = Invoke-NativeWithCapture -FilePath 'gh' `
            -Arguments @('release', 'delete', "v$Version", '--yes', '--cleanup-tag') `
            -ShowProgress -ProgressMessage "既存 release を削除中..."
        if ($delResult.ExitCode -ne 0) {
            Fail "既存 release の削除に失敗しました:`n$($delResult.Combined.TrimEnd())"
        }
        # gh release delete は成功時 stdout/stderr ともに無音なので、
        # ShowProgress 行が消えた後の「無の状態」を避けるため明示的な完了表示
        Write-Ok "既存タグ v$Version を削除完了"
        # 下記は future-proofing (現行 gh では成功時 stdout 空のため発火しない、
        # 将来 version が完了メッセージ等を出力するようになった場合に表示)
        $delOut = $delResult.StdOut.TrimEnd()
        if ($delOut -match '\S') { Write-Info $delOut }
    }

    # CHANGELOG から抽出した Bundle セクション本文を --notes に渡す
    # 一時ファイル経由にする (CLI 引数の改行 / 引用符 escape を避けるため)
    $tmpNotes = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText($tmpNotes.FullName, $script:ReleaseNotesText, $script:Utf8NoBomEncoding)
        # zip サイズは MB / KB を自動切替で表示 (小さいファイル時に `0 MB` 表示で
        # 破損誤解を避ける、Phase 2 以降の Updater 単体 zip 等で発火する想定)
        $zipBytes = (Get-Item $ZipPath).Length
        $zipSize  = if ($zipBytes -ge 1MB) {
            "$([math]::Round($zipBytes / 1MB, 1)) MB"
        } elseif ($zipBytes -ge 1KB) {
            "$([math]::Round($zipBytes / 1KB, 1)) KB"
        } else {
            "$zipBytes B"
        }
        $uploadResult = Invoke-NativeWithCapture -FilePath 'gh' `
            -Arguments @('release', 'create', "v$Version", $ZipPath, '--notes-file', $tmpNotes.FullName, '--title', "v$Version") `
            -ShowProgress -ProgressMessage "アップロード中 (zip $zipSize)..."
        if ($uploadResult.ExitCode -ne 0) {
            Fail "gh release create に失敗しました:`n$($uploadResult.Combined.TrimEnd())"
        }
        # 成功時、gh は release URL を stdout に dump するのでユーザーに表示。
        # 注: gh の将来 version で stdout フォーマットが変わると URL が表示されない
        # silent path になるが、成功は exit code で確実に取れているため fatal で
        # はない (`Write-Ok "公開完了"` は出る、URL は手動で confirm 可能)。
        # 同型の脆弱性は gh release view の "release not found" stderr マッチ側でも
        # 抱えており、本格的な structured 検出は別 issue 系で対処予定。
        $uploadOut = $uploadResult.StdOut.TrimEnd()
        if ($uploadOut -match '\S') { Write-Info $uploadOut }
    } finally {
        Remove-Item $tmpNotes.FullName -Force -ErrorAction SilentlyContinue
    }
    Write-Ok "GitHub Releases v$Version 公開完了"
}

# ============================================================================
# Phase 11: 古い tools/godot/<patch>/ の自動削除（最大 2 version 残す）
# ============================================================================

function Clear-OldGodot {
    Write-Step "古い Godot キャッシュをクリーンアップ"

    # サーバー側 tools/godot/<version>/ は最新 2 version まで残す
    if (Test-Path $ToolsGodot) {
        $dirs = @(Get-ChildItem $ToolsGodot -Directory | Sort-Object Name -Descending)
        if ($dirs.Count -gt 2) {
            $toDelete = @($dirs | Select-Object -Skip 2)
            foreach ($d in $toDelete) {
                Write-Info "tools/ 削除: $($d.Name)"
                Remove-Item -Recurse -Force $d.FullName
            }
            Write-Ok "サーバー tools/ クリーンアップ: $($toDelete.Count) 件削除"
        } else {
            Write-Info "サーバー tools/: $($dirs.Count) version 保持中 (上限 2 以下なので削除なし)"
        }
    }

    # AppData 側 (%APPDATA%/Godot/export_templates/<version>.stable/) は
    # `.gctone_managed` マーカーがあり、かつ $GodotPatchTable に登録されていない version を削除
    # マーカー無し (Godot エディタ UI で部員が手動 DL した等) は触らない
    $appdataTemplatesRoot = Join-Path $env:APPDATA 'Godot\export_templates'
    if (Test-Path $appdataTemplatesRoot) {
        $managedPatches = @($GodotPatchTable.Values | ForEach-Object { $_.Patch })
        $stableDirs = @(Get-ChildItem $appdataTemplatesRoot -Directory | Where-Object { $_.Name -like '*.stable' })
        $removed = 0
        foreach ($d in $stableDirs) {
            $marker = Join-Path $d.FullName '.gctone_managed'
            if (-not (Test-Path $marker)) {
                # 外部管理 (Godot エディタ UI 等) なので触らない
                continue
            }
            $verName = $d.Name -replace '\.stable$', ''  # "4.6.2.stable" → "4.6.2"
            if ($managedPatches -notcontains $verName) {
                Write-Info "AppData 削除: $($d.Name)"
                Remove-Item -Recurse -Force $d.FullName
                $removed++
            }
        }
        if ($removed -gt 0) {
            Write-Ok "AppData クリーンアップ: $removed 件削除"
        } else {
            Write-Info "AppData: 削除対象なし (現行 patch table 内 + 外部管理は保護)"
        }
    }
}

# ============================================================================
# Main
# ============================================================================

Write-Host ""
Write-Host "GCTonePrism Release Script (Phase 1)" -ForegroundColor White
Write-Host "Bundle Version: $Version" -ForegroundColor White
Write-Host "RepoRoot:       $RepoRoot" -ForegroundColor White
if ($DryRun)     { Write-Host "Mode: DRY-RUN (zip と upload を skip)" -ForegroundColor Yellow }
if ($SkipUpload) { Write-Host "Mode: SKIP-UPLOAD" -ForegroundColor Yellow }
if ($Force)      { Write-Host "Mode: FORCE" -ForegroundColor Yellow }
if ($Offline)    { Write-Host "Mode: OFFLINE" -ForegroundColor Yellow }

Assert-Preflight
Read-ComponentVersions
Resolve-Godot
Resolve-MsBuild
Resolve-Nuget
Set-ManifestVersions
Assert-WorkingTreeClean -Context "manifest sync 後"
Clear-Staging
Build-Launcher
Build-Manager
Copy-Templates
Assert-ExpectedFiles
Clear-OldGodot

if ($DryRun) {
    Write-Host ""
    Write-Host "DRY-RUN 完了: staging エリアは $StagingDir に残っています。" -ForegroundColor Yellow
    exit 0
}

New-Zip

if ($SkipUpload) {
    Write-Host ""
    Write-Host "SKIP-UPLOAD 完了: $ZipPath を確認してください。" -ForegroundColor Yellow
    exit 0
}

# upload 直前 Y/N 確認 (#108 Phase 2 で追加):
# 「ビルド + zip 化までは自動、本番 GitHub Releases 公開は明示的同意」型のフロー。
# zip だけ作って別環境に展開テスト (Install.bat 等) したい運用シナリオで、
# `-SkipUpload` を毎回付け忘れて誤 publish するのを防ぐ。
# 完全 non-interactive 運用 (CI 等) では `-SkipUpload` を明示するか、
# `Y` を stdin に流す形で対応。
Write-Step "GitHub Releases 公開確認"
Write-Info "Bundle version: $Version"
Write-Info "zip ファイル:   $ZipPath"
$zipBytes = (Get-Item $ZipPath).Length
$zipSizeHuman = if ($zipBytes -ge 1MB) {
    "$([math]::Round($zipBytes / 1MB, 1)) MB"
} elseif ($zipBytes -ge 1KB) {
    "$([math]::Round($zipBytes / 1KB, 1)) KB"
} else { "$zipBytes B" }
Write-Info "サイズ:        $zipSizeHuman"
Write-Host ""
$confirmUpload = Read-Host "    GitHub Releases に v$Version を公開しますか？ (Y/N)"
if ($confirmUpload -inotmatch '^y') {
    Write-Host ""
    Write-Warn "アップロードをスキップしました。zip は $ZipPath に残っています。"
    Write-Info "別環境での Install.bat 検証等に流用可。後で公開する場合は同 version の release"
    Write-Info "を作り直す必要があるため、CHANGELOG に同 Bundle entry が既に書かれている前提で"
    Write-Info ".\Release.bat を再実行 → 同 zip を再生成 → Y 選択、で publish 可能。"
    exit 0
}

Invoke-GhRelease

Write-Host ""
Write-Host "全工程完了: GitHub Releases v$Version 公開済み" -ForegroundColor Green
