@echo off
REM =============================================================================
REM start_gamepad.bat
REM Start DS4Xbox in background.
REM =============================================================================

set "APP=%~dp0DS4Xbox.exe"

if not exist "%APP%" (
    set "APP=%~dp0bin\Release\net8.0-windows\win-x64\publish\DS4Xbox.exe"
)

if exist "%APP%" (
    start "" "%APP%"
    exit /b
)

echo ================================================================
echo  DS4Xbox is not built yet.
echo  If you are using the source checkout, run:
echo.
echo    setup_gamepad.bat
echo.
echo  Or publish manually:
echo.
echo    dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
echo.
echo  After building, run this script again.
echo ================================================================
pause
