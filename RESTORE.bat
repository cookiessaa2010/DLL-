@echo off
chcp 65001 >nul
set "SCRIPT=%~dp0KaiShaderPatch.ps1"
if "%~1"=="" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Restore
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -DllPath "%~1" -Restore
)
echo.
pause
