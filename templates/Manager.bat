@echo off
REM Manager.bat - launches GCTonePrism Manager from the install parent folder.
REM Deployed by Install.bat to <parent>/Manager.bat (one level above GCTonePrism/).
REM File format: edit source = UTF-8 (no BOM) + CRLF, staging = cp932 + CRLF
REM   (Release.ps1 Copy-Templates re-encodes, see templates/Install.bat docstring).
REM
REM Why `exit` (not `exit /b`) at the end:
REM   `start "" Manager.exe` detaches Manager into its own process, then this
REM   cmd has nothing more to do. With `exit /b` (or no terminator at all), some
REM   terminal hosts (e.g. Windows Terminal with `closeOnExit: graceful`) keep
REM   the spawning cmd alive briefly, which manifested as a "residual cmd window"
REM   after Install.bat auto-started Manager via this script. `exit` forces the
REM   cmd to terminate, closing the window uniformly.
REM   Trade-off: if a future caller uses `call Manager.bat` instead of `start`/
REM   double-click, this `exit` would terminate the caller too. Acceptable since
REM   Manager.bat is a leaf shortcut, not a building block called from other bats.
start "" "%~dp0GCTonePrism\GCTonePrism_Manager\GCTonePrism_Manager.exe"
exit
