@echo off
setlocal
cd /d "%~dp0"
set "SCRIPT=%~dp0Runtime\KaiTOR-Coop-Launcher.ps1"
if not exist "%SCRIPT%" set "SCRIPT=%~dp0KaiTOR-Coop-Launcher.ps1"
if not exist "%SCRIPT%" (
  echo KaiTOR-Coop-Launcher.ps1 not found.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%SCRIPT%"
