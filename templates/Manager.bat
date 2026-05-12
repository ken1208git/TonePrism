@echo off
REM Manager.bat - launches GCTonePrism Manager from the install parent folder.
REM Deployed by Install.bat to <parent>/Manager.bat (one level above GCTonePrism/).
REM File format: UTF-8 (no BOM) + CRLF, enforced by .gitattributes (*.bat eol=crlf)
REM   and Release.ps1 Copy-Templates normalization.
start "" "%~dp0GCTonePrism\GCTonePrism_Manager\GCTonePrism_Manager.exe"
