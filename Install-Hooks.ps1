# ============================================================================
# Install-Hooks.ps1
#
# One-time setup: point this repository's git hooks at .githooks/ instead of
# the default .git/hooks/. After running this once, `git commit` will invoke
# .githooks/pre-commit (= the BOM reject + CRLF enforce check for .bat/.cmd).
#
# Why `core.hooksPath` instead of copying into .git/hooks/:
#   - The hook lives in version control, so all contributors run the same
#     code, and updates propagate via `git pull` instead of re-running the
#     installer.
#   - No drift between repo and .git/hooks/.
#
# Usage:
#   .\Install-Hooks.ps1              # set core.hooksPath = .githooks
#   .\Install-Hooks.ps1 -Force       # overwrite even if core.hooksPath is
#                                    # already set to something else
#   .\Install-Hooks.ps1 -Uninstall   # unset core.hooksPath (only if currently
#                                    # set to .githooks; refuses to clobber
#                                    # third-party hooksPath settings)
# ============================================================================

[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# Verify we are inside a git repository
$repoRoot = git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] Not inside a git repository (or git is not installed)."
    exit 1
}
Set-Location $repoRoot

# Read current core.hooksPath (if any). git config --get returns:
#   exit 0  + value (key set)
#   exit 1  (key not present)
#   exit 5  (no section/key, treated same as 1 for our purposes)
$currentHooksPath = & git config --get core.hooksPath 2>$null
$getExit = $LASTEXITCODE
if ($getExit -eq 0) {
    $isSet = $true
} elseif ($getExit -eq 1 -or $getExit -eq 5) {
    $isSet = $false
    $currentHooksPath = $null
} else {
    Write-Host "[FAIL] git config --get core.hooksPath failed (exit $getExit)."
    exit 1
}

if ($Uninstall) {
    if (-not $isSet) {
        Write-Host "[OK] core.hooksPath was not set (no change)."
        exit 0
    }
    if ($currentHooksPath -ne '.githooks') {
        Write-Host "[WARN] core.hooksPath is set to '$currentHooksPath' (not '.githooks')."
        Write-Host "       This was not set by Install-Hooks.ps1, refusing to unset to avoid"
        Write-Host "       clobbering a third-party hooks setup (Husky / corporate hook etc)."
        Write-Host "       If you intentionally want to clear it: git config --unset core.hooksPath"
        exit 1
    }
    & git config --unset core.hooksPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] git config --unset core.hooksPath failed (exit $LASTEXITCODE)."
        exit 1
    }
    Write-Host "[OK] core.hooksPath unset (default .git/hooks/ restored)."
    exit 0
}

# Install path
if (-not (Test-Path .githooks/pre-commit)) {
    Write-Host "[FAIL] .githooks/pre-commit not found. Are you on the correct branch?"
    exit 1
}

if ($isSet -and $currentHooksPath -ne '.githooks') {
    if (-not $Force) {
        Write-Host "[WARN] core.hooksPath is already set to '$currentHooksPath' (not '.githooks')."
        Write-Host "       Refusing to overwrite to avoid clobbering a third-party hooks setup."
        Write-Host "       If you really want to switch this repo to GCTonePrism's hooks:"
        Write-Host "         .\Install-Hooks.ps1 -Force"
        exit 1
    }
    Write-Host "[INFO] Overwriting existing core.hooksPath '$currentHooksPath' with '.githooks' (-Force)."
}

if ($isSet -and $currentHooksPath -eq '.githooks') {
    Write-Host "[OK] core.hooksPath already set to .githooks (no change)."
} else {
    & git config core.hooksPath .githooks
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] git config core.hooksPath .githooks failed (exit $LASTEXITCODE)."
        exit 1
    }
    Write-Host "[OK] core.hooksPath = .githooks"
}

Write-Host "     Active hooks:"
Get-ChildItem .githooks -File | Where-Object { $_.Name -notmatch '\.(ps1|md)$' } | ForEach-Object {
    Write-Host "       - $($_.Name)"
}
Write-Host ""
Write-Host "To verify: edit a .bat file to add a UTF-8 BOM, then `git add` + `git commit`."
Write-Host "The commit should be rejected with a [FAIL] BOM detected message."
Write-Host ""
Write-Host "To uninstall: .\Install-Hooks.ps1 -Uninstall"
