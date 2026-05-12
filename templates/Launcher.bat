@echo off
REM Launcher.bat - launches GCTonePrism Launcher from the install parent folder.
REM Deployed by Install.bat to <parent>/Launcher.bat (one level above GCTonePrism/).
REM File format: edit source = UTF-8 (no BOM) + CRLF, staging = cp932 + CRLF
REM   (Release.ps1 Copy-Templates re-encodes, see templates/Install.bat docstring).
REM
REM Why `exit` (not `exit /b`) at the end:
REM   `start "" Launcher.exe` detaches Launcher into its own process, then this
REM   cmd has nothing more to do. With `exit /b` (or no terminator at all), some
REM   terminal hosts (e.g. Windows Terminal with `closeOnExit: graceful`) keep
REM   the spawning cmd alive briefly, which manifested as a "residual cmd window"
REM   after Install.bat auto-started Manager via this script. `exit` forces the
REM   cmd to terminate, closing the window uniformly.
REM   Trade-off: if a future caller uses `call Launcher.bat` instead of `start`/
REM   double-click, this `exit` would terminate the caller too. Acceptable since
REM   Launcher.bat is a leaf shortcut, not a building block called from other bats.
start "" "%~dp0GCTonePrism\GCTonePrism_Launcher\GCTonePrism_Launcher.exe"
exit
