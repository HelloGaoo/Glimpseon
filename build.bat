@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

echo ============================================
echo   Glimpseon build
echo ============================================
echo.

echo [1/3] Stopping running instances...
taskkill /F /IM GlimpseonMain.exe >nul 2>&1
taskkill /F /IM Glimpseon.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul

echo [2/3] Building (extra args: %*)...
echo.
dotnet build Glimpseon.slnx -v q --nologo %*
if errorlevel 1 (
    echo.
    echo ##########  BUILD FAILED  ##########
    echo Check the errors above. If it says MSB3021/MSB3027 "file is locked",
    echo the app is still running - close it and run build.bat again.
    echo.
    pause
    exit /b 1
)

echo.
echo [3/3] Artifacts:
for %%F in ("Glimpseon.exe" "app-1.0.0\GlimpseonMain.exe" "app-1.0.0\GlimpseonMain.dll") do (
    if exist "%%~F" for %%T in ("%%~F") do echo     %%~F    %%~tT
)

echo.
echo ##########  BUILD OK  ##########
echo   launcher : dotnet\Glimpseon.exe
echo   app      : dotnet\app-1.0.0\GlimpseonMain.exe
echo.
pause
