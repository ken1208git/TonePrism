@echo off
REM Launcher.bat - launches GCTonePrism Launcher from the install parent folder.
REM Deployed by Install.bat to <parent>/Launcher.bat (one level above GCTonePrism/).
REM File format: edit source = UTF-8 (no BOM) + CRLF, staging = cp932 + CRLF
REM   (Release.ps1 Copy-Templates re-encodes, see templates/Install.bat docstring).
REM
REM Why `exit` (not `exit /b`) at the end:
REM   Launcher.bat is a leaf shortcut (no other bat ever calls it via `call`),
REM   so it applies the same `exit` discipline as Manager.bat preemptively.
REM   The observed "residual cmd window" problem occurred specifically when
REM   Install.bat auto-started Manager via Manager.bat — see Manager.bat docstring
REM   for the full root cause / fix. Launcher.bat itself isn't called from
REM   Install.bat (there's no Launcher auto-start path), so this is a forward-
REM   looking consistency choice rather than a bug fix observed here.
REM   Trade-off: if a future caller uses `call Launcher.bat` instead of `start`/
REM   double-click, this `exit` would terminate the caller too. Acceptable since
REM   Launcher.bat is intentionally a leaf shortcut, not a building block.
start "" "%~dp0GCTonePrism\GCTonePrism_Launcher\GCTonePrism_Launcher.exe"
exit
