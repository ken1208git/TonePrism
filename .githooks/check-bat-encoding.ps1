# ============================================================================
# check-bat-encoding.ps1
#
# Verifies that .bat / .cmd files are stored as UTF-8 (no BOM) + CRLF.
# See SPECIFICATION.md section 3.7.9.1 for rationale (BOM breaks @echo off,
# LF-only breaks cmd.exe parser in subtle ways).
#
# Modes:
#   -Mode Staged  : iterate `git diff --cached --diff-filter=AM` (pre-commit)
#   -Mode All     : iterate `git ls-files` matching pattern (CI safety net)
#
# Exit codes:
#   0 = all checked files OK (or no .bat/.cmd files in scope)
#   1 = at least one violation detected (BOM or LF-only)
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
        # Added (A) + Modified (M); skip Deleted (D) since there is no content to check
        $raw = git diff --cached --name-only --diff-filter=AM 2>$null
    } else {
        $raw = git ls-files 2>$null
    }
    if ($LASTEXITCODE -ne 0 -or -not $raw) { return @() }
    return @($raw | Where-Object { $_ -match '\.(bat|cmd)$' })
}

function Test-FileEncoding {
    param([string]$Path)
    # Returns list of violation messages (empty = OK).
    $violations = @()
    if (-not (Test-Path -LiteralPath $Path)) {
        return $violations
    }
    $bytes = [System.IO.File]::ReadAllBytes($Path)

    # BOM check (UTF-8 BOM = EF BB BF)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $violations += "[FAIL] BOM detected: $Path (first 3 bytes: EF BB BF, must be UTF-8 no BOM)"
    }

    # CRLF check: every LF (0x0A) must be preceded by CR (0x0D)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 0x0A) {
            if ($i -eq 0 -or $bytes[$i - 1] -ne 0x0D) {
                $lineNumber = ($bytes[0..$i] | Where-Object { $_ -eq 0x0A }).Count
                $violations += "[FAIL] LF-only line ending detected: $Path (line $lineNumber, must be CRLF)"
                break
            }
        }
    }

    return $violations
}

$files = Get-TargetFiles -Mode $Mode
if ($files.Count -eq 0) {
    if ($Mode -eq 'All') {
        Write-Host "[OK] No .bat / .cmd files tracked."
    }
    exit 0
}

$allViolations = @()
foreach ($file in $files) {
    $v = Test-FileEncoding -Path $file
    if ($v.Count -gt 0) { $allViolations += $v }
}

if ($allViolations.Count -eq 0) {
    Write-Host "[OK] All .bat / .cmd files pass encoding check ($($files.Count) file(s))."
    exit 0
}

Write-Host ""
foreach ($msg in $allViolations) { Write-Host $msg }
Write-Host ""
Write-Host "Fix: re-save as UTF-8 (no BOM) + CRLF. See SPECIFICATION.md section 3.7.9.1."
Write-Host "     PowerShell one-liner to recover (replace <path>):"
Write-Host '       $c = [System.IO.File]::ReadAllText(''<path>'')'
Write-Host '       $c = $c -replace "\r?\n", "\r\n"'
Write-Host '       [System.IO.File]::WriteAllText(''<path>'', $c, [System.Text.UTF8Encoding]::new($false))'
if ($Mode -eq 'Staged') {
    Write-Host ""
    Write-Host "Bypass: git commit --no-verify  (NOT recommended for .bat / .cmd)"
}
exit 1
