@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
cd /d "%~dp0"

echo Checking port 8765...
set FOUND=0
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":8765" ^| findstr "LISTENING"') do (
  set FOUND=1
  echo   stopping PID %%a
  taskkill /F /PID %%a >nul 2>&1
)
if "!FOUND!"=="1" (
  echo Old servers stopped. Waiting 1 sec...
  timeout /t 1 /nobreak >nul
)

python --version >nul 2>&1
if errorlevel 1 (
  echo ERROR: Python not found. Install Python and add it to PATH.
  pause
  exit /b 1
)

echo.
echo Macro Chronicle Server with API
echo Open: http://127.0.0.1:8765/index.html
echo Stop: Ctrl+C
echo.
python sim_server.py 8765 127.0.0.1
echo.
echo Server stopped.
pause
