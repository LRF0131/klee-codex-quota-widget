@echo off
setlocal
title Install Klee Codex Quota Widget

set "APP_NAME=KleeCodexQuotaWidget"
set "SOURCE_EXE=%~dp0KleeCodexQuotaWidget.exe"
set "INSTALL_DIR=%LOCALAPPDATA%\KleeCodexQuotaWidget"
set "TARGET_EXE=%INSTALL_DIR%\KleeCodexQuotaWidget.exe"

if not exist "%SOURCE_EXE%" (
  echo [ERROR] KleeCodexQuotaWidget.exe is missing.
  echo Please extract every file from the ZIP before installing.
  pause
  exit /b 1
)

taskkill /IM KleeCodexQuotaWidget.exe /F >nul 2>&1
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
copy /Y "%SOURCE_EXE%" "%TARGET_EXE%" >nul
if errorlevel 1 (
  echo [ERROR] Unable to copy the application.
  pause
  exit /b 1
)

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "%APP_NAME%" /t REG_SZ /d "\"%TARGET_EXE%\"" /f >nul
if errorlevel 1 (
  echo [ERROR] Unable to enable startup.
  pause
  exit /b 1
)

start "" "%TARGET_EXE%"
echo.
echo Installed successfully. The widget will start automatically with Windows.
echo Location: %TARGET_EXE%
pause
