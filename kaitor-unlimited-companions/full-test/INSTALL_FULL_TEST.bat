@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALL_FULL_TEST.ps1"
if errorlevel 1 (
  echo.
  echo INSTALL FAILED.
  pause
  exit /b 1
)
exit /b 0
