@echo off
REM Launcher.bat - launches GCTonePrism Launcher from the install parent folder.
REM Deployed by Install.bat to <parent>/Launcher.bat (one level above GCTonePrism/).
REM File format: UTF-8 (no BOM) + CRLF, enforced by .gitattributes (*.bat eol=crlf)
REM   and Release.ps1 Copy-Templates normalization.
start "" "%~dp0GCTonePrism\GCTonePrism_Launcher\GCTonePrism_Launcher.exe"
