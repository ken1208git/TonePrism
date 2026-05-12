@echo off
REM Launcher.bat - launches GCTonePrism Launcher from the install root.
REM Bundled by Release.ps1 at files/Launcher.bat, deployed to <install>/GCTonePrism/Launcher.bat.
REM File format: UTF-8 (no BOM) + CRLF, enforced by .gitattributes (*.bat eol=crlf)
REM   and Release.ps1 Copy-Templates normalization.
start "" "%~dp0GCTonePrism_Launcher\GCTonePrism_Launcher.exe"
