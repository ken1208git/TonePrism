# ============================================================================
# check-bat-encoding.ps1
#
# Verifies that .bat / .cmd files are stored as UTF-8 (no BOM) + CRLF.
# See SPECIFICATION.md section 3.7.9.1 for rationale (BOM breaks @echo off,
# LF-only breaks cmd.exe parser in subtle ways, non-UTF-8 bytes mojibake).
#
# Modes:
#   -Mode Staged  : pre-commit hook. Checks are split between two byte sources
#                   because .gitattributes `*.bat eol=crlf` normalises CRLF to
#                   LF in the index blob:
#                     - BOM / UTF-8 validity -> git index blob (durable bytes
#                       that survive normalisation, partial-staging proof)
#                     - CRLF                 -> working tree (blob is always
#                       LF post-normalisation, so checking the blob is moot;
#                       the working tree is the only place where Write-tool
#                       corruption to LF can actually be detected before the
#                       commit happens)
#                   Files in scope: `git diff --cached --diff-filter=AMR`
#                   (Added / Modified / Renamed; D / C / T are skipped).
#   -Mode All     : CI safety net. Reads working tree only (fresh checkout
#                   already smudged through `.gitattributes`). Iterates
#                   `git ls-files`.
#
# Exit codes:
#   0 = all checked files OK (or no .bat/.cmd files in scope)
#   1 = at least one violation detected, OR git invocation failed (fail-closed
#       so the primary fence never silently passes on tooling errors).
# ============================================================================

[CmdletBinding()]
param(
    [ValidateSet('Staged', 'All')]
    [string]$Mode = 'Staged'
)

$ErrorActionPreference = 'Stop'

function Get-TargetFiles {
    param([string]$Mode)
    if ($Mode -eq 'Staged') {
        $raw = & git diff --cached --name-only --diff-filter=AMR 2>&1
        $gitExit = $LASTEXITCODE
        $cmdLabel = 'git diff --cached --name-only --diff-filter=AMR'
    } else {
        $raw = & git ls-files 2>&1
        $gitExit = $LASTEXITCODE
        $cmdLabel = 'git ls-files'
    }
    if ($gitExit -ne 0) {
        Write-Host "[FAIL] $cmdLabel failed (exit $gitExit): $raw"
        exit 1
    }
    if (-not $raw) { return @() }
    return @($raw | Where-Object { $_ -match '\.(bat|cmd)$' })
}

function Get-IndexBlobBytes {
    # Read the staged blob from the git index via `git cat-file -p :<path>`.
    # PowerShell's native command pipeline re-encodes stdout through
    # Console.OutputEncoding which corrupts binary; we use Process +
    # MemoryStream to capture the raw byte stream.
    #
    # Note: `*.bat eol=crlf` in .gitattributes means the blob is always
    # stored with LF line endings. BOM bytes and non-ASCII byte sequences,
    # however, survive normalisation unchanged -- which is exactly what we
    # need to inspect here.
    param([string]$Path)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    # Quote `:<path>` so paths containing spaces survive arg splitting.
    $psi.Arguments = "--no-pager cat-file -p `":$Path`""
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($psi)
    $mem = New-Object System.IO.MemoryStream
    $process.StandardOutput.BaseStream.CopyTo($mem)
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "git cat-file -p :$Path failed (exit $($process.ExitCode)): $stderr"
    }
    return $mem.ToArray()
}

function Test-BomAndUtf8 {
    # Run BOM + UTF-8 validity on the bytes that are actually being committed.
    param([string]$Path, [byte[]]$Bytes)
    $violations = @()

    $hasBom = ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF)
    if ($hasBom) {
        $violations += "[FAIL] BOM detected: $Path (first 3 bytes: EF BB BF, must be UTF-8 no BOM)"
    }

    # Catches Shift-JIS / ANSI / CP932 bytes that have no BOM and accidental
    # CRLF; without this they would pass the byte-level BOM check yet still
    # mojibake the file.
    $payload = if ($hasBom) {
        if ($Bytes.Length -eq 3) { @() } else { $Bytes[3..($Bytes.Length - 1)] }
    } else {
        $Bytes
    }
    if ($payload.Length -gt 0) {
        $strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
        try {
            [void]$strictUtf8.GetString($payload)
        } catch [System.Text.DecoderFallbackException] {
            $violations += "[FAIL] Invalid UTF-8 byte sequence: $Path (file is not UTF-8, possibly Shift-JIS / ANSI / CP932)"
        }
    }

    return $violations
}

function Test-WorkingTreeCrlf {
    # Run CRLF check against the working tree bytes. Single-pass O(N) with a
    # running line counter (no array slicing). The blob is normalised to LF
    # by `.gitattributes eol=crlf`, so checking it would always fail; the
    # working tree is the only signal of "did the editor / Write-tool save
    # this as LF-only".
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        # Working tree missing despite the file being staged: could happen
        # mid-rebase / weird VCS states. Skip (blob check will handle it).
        return @()
    }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $violations = @()
    $line = 1
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 0x0A) {
            if ($i -eq 0 -or $bytes[$i - 1] -ne 0x0D) {
                $violations += "[FAIL] LF-only line ending detected in working tree: $Path (line $line, must be CRLF)"
                break
            }
            $line++
        }
    }
    return $violations
}

$files = Get-TargetFiles -Mode $Mode
if ($files.Count -eq 0) {
    # Mode All on a repo with no .bat/.cmd: surface scope to CI logs.
    # Mode Staged with no relevant staged files: silent (keep pre-commit
    # output of unrelated commits noise-free).
    if ($Mode -eq 'All') {
        Write-Host "[OK] No .bat / .cmd files tracked."
    }
    exit 0
}

$allViolations = @()
foreach ($file in $files) {
    if ($Mode -eq 'Staged') {
        # Hybrid: BOM/UTF-8 from blob (partial-staging proof), CRLF from
        # working tree (blob is always LF post-normalisation).
        try {
            $blobBytes = Get-IndexBlobBytes -Path $file
        } catch {
            $allViolations += "[FAIL] Could not read staged blob for $file : $_"
            continue
        }
        $allViolations += Test-BomAndUtf8 -Path $file -Bytes $blobBytes
        $allViolations += Test-WorkingTreeCrlf -Path $file
    } else {
        # Mode All (CI on fresh checkout): working tree is the smudged blob
        # so a single source is enough.
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $bytes = [System.IO.File]::ReadAllBytes($file)
        $allViolations += Test-BomAndUtf8 -Path $file -Bytes $bytes
        $allViolations += Test-WorkingTreeCrlf -Path $file
    }
}

if ($allViolations.Count -eq 0) {
    if ($Mode -eq 'All') {
        Write-Host "[OK] All .bat / .cmd files pass encoding check ($($files.Count) file(s))."
    }
    exit 0
}

Write-Host ""
foreach ($msg in $allViolations) { Write-Host $msg }
Write-Host ""
Write-Host "Fix: re-save as UTF-8 (no BOM) + CRLF. See SPECIFICATION.md section 3.7.9.1."
Write-Host "     PowerShell one-liner to recover (replace <path>; backticks below are"
Write-Host "     PowerShell escape characters, NOT backslashes -- paste verbatim):"
Write-Host '       $c = [System.IO.File]::ReadAllText(''<path>'')'
Write-Host '       $c = $c -replace "`r?`n", "`r`n"'
Write-Host '       [System.IO.File]::WriteAllText(''<path>'', $c, [System.Text.UTF8Encoding]::new($false))'
if ($Mode -eq 'Staged') {
    Write-Host ""
    Write-Host "Bypass: git commit --no-verify  (NOT recommended for .bat / .cmd)"
}
exit 1
