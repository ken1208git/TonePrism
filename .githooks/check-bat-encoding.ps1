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
#   -Mode All     : CI safety net. Iterates `git ls-files`, reads working
#                   tree. On a fresh actions/checkout the smudge filter has
#                   already applied `eol=crlf`, so the working-tree CRLF
#                   check in this mode is effectively a no-op and the CI's
#                   primary signal is BOM + UTF-8 validity. The CRLF check
#                   is retained for defence-in-depth in case CI environment
#                   changes (e.g. self-hosted runner without smudge).
#
# All git invocations go through Invoke-GitCapture which uses
# System.Diagnostics.Process directly (NOT PowerShell's `&` operator with
# `2>&1`), because PS 5.1 wraps native command stderr lines into
# NativeCommandError ErrorRecords and `$ErrorActionPreference='Stop'` turns
# any informational git warning (e.g. "warning: LF will be replaced by CRLF
# in foo.bat") into a script-killing exception that bypasses our friendly
# [FAIL] reporter. The helper also passes `-c core.quotepath=false` so
# non-ASCII filenames (this project root is C:\<Japanese>\TonePrism, and
# future .bat under Companions/<日本語>/ etc. is plausible) come back as
# raw UTF-8 instead of `\343\203\206...` style C-escaped quotes.
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

# Force UTF-8 for console output. PS 5.1 + JP locale defaults to CP932, so
# violation messages that interpolate a non-ASCII $Path (e.g.
# Companions/<日本語>/foo.bat) would otherwise mojibake on stdout even though
# our internal git output decoding (Invoke-GitCapture, StandardOutputEncoding=
# UTF8) is already UTF-8. Symmetric with Install-Hooks.ps1's same override
# (added in PR #159 round 3 [Claude M-2]).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Invoke-GitCapture {
    # Run git via System.Diagnostics.Process. Captures stdout / stderr / exit
    # code without involving `2>&1` (= PS 5.1 NativeCommandError trap, see
    # file-level docstring). `-Binary` makes stdout a byte[] for `cat-file`;
    # default is UTF-8 decoded string for path lists. Process / MemoryStream
    # are disposed via try/finally even on exceptions.
    param(
        [Parameter(Mandatory=$true)][string[]]$GitArgs,
        [switch]$Binary
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    # Build Arguments string with simple whitespace quoting. .NET Framework /
    # PS 5.1 lacks ProcessStartInfo.ArgumentList (added in .NET Core 2.1+),
    # so we do this by hand. .bat path strings in this repo do not contain
    # double quotes, so naive `"..."` wrapping is sufficient.
    $quoted = $GitArgs | ForEach-Object {
        if ($_ -match '\s') { "`"$_`"" } else { $_ }
    }
    $psi.Arguments = ($quoted -join ' ')
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    # StandardOutputEncoding for the text path: tell .NET to decode git's
    # stdout as UTF-8. Combined with `-c core.quotepath=false` this means
    # non-ASCII paths arrive intact instead of as `\343\203\206...` octets.
    if (-not $Binary) {
        $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    }
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8

    $process = $null
    $mem = $null
    try {
        $process = [System.Diagnostics.Process]::Start($psi)
        if ($Binary) {
            $mem = New-Object System.IO.MemoryStream
            $process.StandardOutput.BaseStream.CopyTo($mem)
            $stdout = $mem.ToArray()
        } else {
            $stdout = $process.StandardOutput.ReadToEnd()
        }
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        return [PSCustomObject]@{
            Stdout   = $stdout
            Stderr   = $stderr
            ExitCode = $process.ExitCode
        }
    } finally {
        if ($mem) { $mem.Dispose() }
        if ($process) { $process.Dispose() }
    }
}

function Get-TargetFiles {
    param([string]$Mode)
    if ($Mode -eq 'Staged') {
        # `-c core.quotepath=false` so non-ASCII paths are raw UTF-8.
        # `--diff-filter=AMR` so rename targets (R) enter the fence (Claude
        # M-1 round 1). D / C / T have no useful content to check.
        $result = Invoke-GitCapture @('-c', 'core.quotepath=false', 'diff', '--cached', '--name-only', '--diff-filter=AMR')
        $cmdLabel = 'git diff --cached --name-only --diff-filter=AMR'
    } else {
        $result = Invoke-GitCapture @('-c', 'core.quotepath=false', 'ls-files')
        $cmdLabel = 'git ls-files'
    }
    if ($result.ExitCode -ne 0) {
        # Fail-closed: any git failure exits 1 so primary fence never
        # silently passes on tooling errors (Claude M-2 round 1).
        Write-Host "[FAIL] $cmdLabel failed (exit $($result.ExitCode))."
        if ($result.Stderr) { Write-Host "       stderr: $($result.Stderr.Trim())" }
        exit 1
    }
    $raw = $result.Stdout
    if (-not $raw) { return @() }
    return @($raw -split "`r?`n" | Where-Object { $_ -match '\.(bat|cmd)$' })
}

function Get-IndexBlobBytes {
    # Read the staged blob from the git index via `git cat-file -p :<path>`
    # so that we validate what is actually being committed (Codex P1 round 1).
    # `*.bat eol=crlf` means the blob is always stored with LF line endings;
    # BOM bytes and non-ASCII byte sequences survive normalisation, which is
    # exactly what we need to inspect here.
    #
    # Note on the `:<path>` form: the leading `:` is the rev:path notation,
    # so the whole argument starts with `:` (not `-`), which means git's
    # argument parser cannot mistake a hyphen-prefixed filename for a flag.
    # `git cat-file -p` does not take a `--` separator (the second positional
    # is a rev-spec, not a pathspec), so we skip the defensive `--` here.
    param([string]$Path)
    $result = Invoke-GitCapture @('-c', 'core.quotepath=false', '--no-pager', 'cat-file', '-p', ":$Path") -Binary
    if ($result.ExitCode -ne 0) {
        $msg = "git cat-file -p :$Path failed (exit $($result.ExitCode))"
        if ($result.Stderr) { $msg += ": $($result.Stderr.Trim())" }
        throw $msg
    }
    return $result.Stdout
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
    # mojibake the file (Codex P2 round 1).
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
    #
    # In `-Mode All` on a fresh CI checkout, the working tree has already
    # been smudged through `eol=crlf` so this check is effectively a no-op;
    # it stays for defence-in-depth (self-hosted runner without smudge etc).
    #
    # Rationale (PR #159 round 4 [Claude L-4]): even when this check FAILS
    # in Mode Staged, the *committed* blob is still LF (= `.gitattributes
    # eol=crlf` already normalised) and `git checkout` on any other machine
    # will smudge back to CRLF, so the commit content itself is not bad.
    # The real value of this check is an early signal that the contributor's
    # editor saves with LF endings -- that habit will produce a broken file
    # the moment `.gitattributes` coverage misses (e.g. a new extension not
    # listed). Treat it as editor-config lint, not as a commit-content guard.
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $violations = @()
    $line = 1
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 0x0A) {
            if ($i -eq 0 -or $bytes[$i - 1] -ne 0x0D) {
                # Report only the first occurrence to keep the output bounded;
                # editors that mis-save line endings usually do so for the
                # whole file, so listing every line would just be noise.
                $violations += "[FAIL] LF-only line ending detected in working tree: $Path (line $line, must be CRLF; this is the first occurrence -- if your editor saved the whole file as LF, fix once and re-stage)"
                break
            }
            $line++
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
    if ($Mode -eq 'Staged') {
        try {
            $blobBytes = Get-IndexBlobBytes -Path $file
        } catch {
            $allViolations += "[FAIL] Could not read staged blob for $file : $_"
            continue
        }
        $allViolations += Test-BomAndUtf8 -Path $file -Bytes $blobBytes
        $allViolations += Test-WorkingTreeCrlf -Path $file
    } else {
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

# Display violations + branched recovery instructions. The naive recovery
# one-liner (ReadAllText UTF-8 default) destroys SJIS/CP932 content by
# replacing non-UTF-8 bytes with U+FFFD, so if any violation is "Invalid
# UTF-8" we show a CP932-aware variant instead of the BOM/CRLF one-liner
# (Claude M-2 round 2).
$hasUtf8Violation = $false
foreach ($v in $allViolations) {
    if ($v -match 'Invalid UTF-8 byte sequence') { $hasUtf8Violation = $true; break }
}

Write-Host ""
foreach ($msg in $allViolations) { Write-Host $msg }
Write-Host ""
Write-Host "Fix: re-save as UTF-8 (no BOM) + CRLF. See SPECIFICATION.md section 3.7.9.1."
Write-Host ""
if ($hasUtf8Violation) {
    Write-Host "     Non-UTF-8 (likely Shift-JIS / CP932 / ANSI) detected."
    Write-Host "     The plain BOM/CRLF recovery one-liner uses ReadAllText (UTF-8 default),"
    Write-Host "     which would replace your Japanese bytes with U+FFFD and destroy content."
    Write-Host "     Use the CP932-aware path instead (replace <path>; backticks are PowerShell"
    Write-Host "     escape characters, NOT backslashes -- paste verbatim):"
    Write-Host '       $sjis = [System.Text.Encoding]::GetEncoding(932)'
    Write-Host '       $c = [System.IO.File]::ReadAllText(''<path>'', $sjis)'
    Write-Host '       $c = $c -replace "`r?`n", "`r`n"'
    Write-Host '       [System.IO.File]::WriteAllText(''<path>'', $c, [System.Text.UTF8Encoding]::new($false))'
    Write-Host "     (If the source encoding is unknown, open the file in VS Code / Notepad++"
    Write-Host "      and re-save explicitly as 'UTF-8 (without BOM)'.)"
} else {
    Write-Host "     PowerShell one-liner to recover (replace <path>; backticks below are"
    Write-Host "     PowerShell escape characters, NOT backslashes -- paste verbatim):"
    Write-Host '       $c = [System.IO.File]::ReadAllText(''<path>'')'
    Write-Host '       $c = $c -replace "`r?`n", "`r`n"'
    Write-Host '       [System.IO.File]::WriteAllText(''<path>'', $c, [System.Text.UTF8Encoding]::new($false))'
}
if ($Mode -eq 'Staged') {
    Write-Host ""
    Write-Host "Bypass: git commit --no-verify  (NOT recommended for .bat / .cmd)"
}
exit 1
