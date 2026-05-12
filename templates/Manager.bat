@echo off
REM ============================================================================
REM Manager.bat - GCTonePrism Manager 起動ショートカット
REM
REM 配置: <インストール先>\GCTonePrism\Manager.bat
REM 用途: 運営担当 (部員 / 顧問) が `GCTonePrism\GCTonePrism_Manager\GCTonePrism_Manager.exe`
REM       まで辿らず、ルートのこれをダブルクリックで起動する
REM
REM File format: UTF-8 (no BOM) + CRLF (`.gitattributes` の `*.bat eol=crlf` で強制)
REM ============================================================================
start "" "%~dp0GCTonePrism_Manager\GCTonePrism_Manager.exe"
