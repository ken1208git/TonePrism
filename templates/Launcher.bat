@echo off
REM ============================================================================
REM Launcher.bat - GCTonePrism Launcher 起動ショートカット
REM
REM 配置: <インストール先>\GCTonePrism\Launcher.bat
REM 用途: 部員 / 来場者が `GCTonePrism\GCTonePrism_Launcher\GCTonePrism_Launcher.exe`
REM       まで辿らず、ルートのこれをダブルクリックで起動する
REM
REM File format: UTF-8 (no BOM) + CRLF (`.gitattributes` の `*.bat eol=crlf` で強制)
REM ============================================================================
start "" "%~dp0GCTonePrism_Launcher\GCTonePrism_Launcher.exe"
