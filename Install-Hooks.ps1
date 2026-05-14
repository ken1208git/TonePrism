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
#   .\Install-Hooks.ps1            # set core.hooksPath = .githooks
#   .\Install-Hooks.ps1 -Uninstall # unset core.hooksPath (revert to default)
#
# Idempotent: running multiple times is safe.
# ============================================================================

[CmdletBinding()]
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'

# Verify we are inside a git repository
$repoRoot = git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] Not inside a git repository (or git is not installed)."
    exit 1
}

Set-Location $repoRoot

if ($Uninstall) {
    git config --unset core.hooksPath 2>$null
    Write-Host "[OK] core.hooksPath unset (default .git/hooks/ restored)."
    exit 0
}

if (-not (Test-Path .githooks/pre-commit)) {
    Write-Host "[FAIL] .githooks/pre-commit not found. Are you on the correct branch?"
    exit 1
}

git config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] `git config core.hooksPath .githooks` failed."
    exit 1
}

Write-Host "[OK] core.hooksPath = .githooks"
Write-Host "     Active hooks:"
Get-ChildItem .githooks -File | Where-Object { $_.Name -notmatch '\.(ps1|md)$' } | ForEach-Object {
    Write-Host "       - $($_.Name)"
}
Write-Host ""
Write-Host "To verify: edit a .bat file to add a UTF-8 BOM, then `git add` + `git commit`."
Write-Host "The commit should be rejected with a [FAIL] BOM detected message."
Write-Host ""
Write-Host "To uninstall: .\Install-Hooks.ps1 -Uninstall"
