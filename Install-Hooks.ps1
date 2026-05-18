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

# Force UTF-8 for decoding native command stdout (e.g. `git rev-parse`).
# Windows PowerShell 5.1 + JP locale defaults to CP932, so when git emits a
# UTF-8 path that contains Japanese (this repo's worktree root is literally
# `C:\【ゲームセンターTONE】\TonePrism\`), `& git rev-parse --show-toplevel`
# would otherwise mojibake the result and `Set-Location` would fail with
# PathNotFoundException. The check-bat-encoding.ps1 side handles this via
# `Invoke-GitCapture` with `StandardOutputEncoding=UTF8`; here a simple
# global override is sufficient because the helper does only a couple of
# `&` calls.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# Verify we are inside a git repository
$repoRoot = git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "[FAIL] Not inside a git repository (or git is not installed)."
    exit 1
}

# Push the repo root onto the location stack with -LiteralPath so any wildcard
# characters in the path (`[`, `]`, `*`, `?` -- this project's worktree is
# C:\【ゲームセンターTONE】\... which does NOT contain wildcards but forks
# might) are treated verbatim. try/finally guarantees Pop-Location even on
# `exit N` paths, so the caller's cwd is not mutated as a side effect of
# running the installer (PowerShell location is runspace-scoped, not
# script-scoped).
Push-Location -LiteralPath $repoRoot
try {

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
    if (-not (Test-Path -LiteralPath .githooks/pre-commit)) {
        Write-Host "[FAIL] .githooks/pre-commit not found. Are you on the correct branch?"
        exit 1
    }

    if ($isSet -and $currentHooksPath -ne '.githooks') {
        if (-not $Force) {
            Write-Host "[WARN] core.hooksPath is already set to '$currentHooksPath' (not '.githooks')."
            Write-Host "       Refusing to overwrite to avoid clobbering a third-party hooks setup."
            Write-Host "       If you really want to switch this repo to TonePrism's hooks:"
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
    # Whitelist by "extensionless filename" = git hook name convention. Auxiliary
    # files (check-bat-encoding.ps1, future .psm1 / .py / README.md etc.) are
    # inert from git's standpoint and should not appear in this listing.
    Get-ChildItem -LiteralPath .githooks -File | Where-Object { $_.Extension -eq '' } | ForEach-Object {
        Write-Host "       - $($_.Name)"
    }
    Write-Host ""
    # Use single-quote string so the backticks around `git add` / `git commit`
    # are preserved as markdown-style code markers (in PS double-quote strings
    # the backtick is the escape character and would be silently consumed).
    Write-Host 'To verify: edit a .bat file to add a UTF-8 BOM, then `git add` + `git commit`.'
    Write-Host "The commit should be rejected with a [FAIL] BOM detected message."
    Write-Host ""
    Write-Host "To uninstall: .\Install-Hooks.ps1 -Uninstall"

} finally {
    Pop-Location
}
