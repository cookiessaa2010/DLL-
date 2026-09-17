@echo off
setlocal
chcp 65001 >nul
title KaiTOR Dawi Women - Local Asset Setup

if "%~1"=="" (
  echo.
  echo Перетащи папку LOTRLOME_Armory на этот файл SETUP-DAWI-ASSETS.cmd
  echo или запусти так:
  echo SETUP-DAWI-ASSETS.cmd "D:\...\Modules\LOTRLOME_Armory"
  echo.
  pause
  exit /b 2
)

echo.
echo KaiTOR Dawi Women v0.2.0 - безопасная локальная подготовка ассетов
echo Никакого побайтового сканирования TPAC не выполняется.
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Stage-Local-DawiAssets.ps1" "%~1"
set rc=%errorlevel%

echo.
if not "%rc%"=="0" (
  echo ОШИБКА. Ассеты НЕ активированы. Код: %rc%
) else (
  echo ГОТОВО. Полностью перезапусти Bannerlord.
  echo Затем в консоли: kaitor_dawi_women.status
  echo И только если READY: kaitor_dawi_women.spawn_test
)
echo.
pause
exit /b %rc%
