@echo off
setlocal
title KaiTOR Dawi Women - Local Asset Setup v0.2.2

echo.
echo KaiTOR Dawi Women v0.2.2
echo Exact filename search only. TPAC contents are NOT scanned.
echo Search root is Bannerlord\Modules.
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Stage-Local-DawiAssets.ps1"
set rc=%errorlevel%

echo.
if not "%rc%"=="0" (
  echo ERROR. Assets were NOT activated. Exit code: %rc%
) else (
  echo READY. Restart Bannerlord completely.
  echo Then run: kaitor_dawi_women.status
  echo Only if status is READY run: kaitor_dawi_women.spawn_test
)
echo.
pause
exit /b %rc%
