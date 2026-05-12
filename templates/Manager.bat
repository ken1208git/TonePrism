@echo off
REM Manager.bat - launches GCTonePrism Manager from the install root.
REM Bundled by Release.ps1 at files/Manager.bat, deployed to <install>/GCTonePrism/Manager.bat.
REM File format: UTF-8 (no BOM) + CRLF, enforced by .gitattributes (*.bat eol=crlf)
REM   and Release.ps1 Copy-Templates normalization.
start "" "%~dp0GCTonePrism_Manager\GCTonePrism_Manager.exe"
