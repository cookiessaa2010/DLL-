@echo off
setlocal
chcp 65001 >nul
title KaiTOR Dawi Women - Local Asset Setup v0.2.1

set "SCAN_ROOT=%~1"
if "%SCAN_ROOT%"=="" set "SCAN_ROOT=%~dp0.."

echo.
echo KaiTOR Dawi Women v0.2.1 - поиск ассетов по ИМЕНАМ ФАЙЛОВ
echo Побайтового сканирования TPAC НЕТ.
echo Корень поиска: %SCAN_ROOT%
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Stage-Local-DawiAssets.ps1" "%SCAN_ROOT%"
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
