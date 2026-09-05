@echo off
setlocal
title Uninstall Klee Codex Quota Widget

set "INSTALL_DIR=%LOCALAPPDATA%\KleeCodexQuotaWidget"
set "TARGET_EXE=%INSTALL_DIR%\KleeCodexQuotaWidget.exe"

taskkill /IM KleeCodexQuotaWidget.exe /F >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "KleeCodexQuotaWidget" /f >nul 2>&1
if exist "%TARGET_EXE%" del /F /Q "%TARGET_EXE%"

echo.
echo Uninstalled. Local cache and logs were kept in:
echo %INSTALL_DIR%
echo You may delete that folder manually if you no longer need it.
pause
