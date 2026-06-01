@echo off
REM =============================================================================
REM setup_gamepad.bat
REM Build/publish DS4Xbox from this source checkout, then start the app.
REM =============================================================================

setlocal
set "ROOT=%~dp0"
set "APP=%ROOT%bin\Release\net8.0-windows\win-x64\publish\DS4Xbox.exe"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ================================================================
    echo  .NET SDK was not found.
    echo.
    echo  Install .NET 8 SDK, then run this script again:
    echo  https://dotnet.microsoft.com/download/dotnet/8.0
    echo ================================================================
    pause
    exit /b 1
)

echo ================================================================
echo  Publishing DS4Xbox...
echo ================================================================
dotnet publish "%ROOT%DS4Xbox.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
if errorlevel 1 (
    echo.
    echo Publish failed.
    pause
    exit /b 1
)

if not exist "%APP%" (
    echo.
    echo Publish completed, but DS4Xbox.exe was not found:
    echo %APP%
    pause
    exit /b 1
)

echo.
echo DS4Xbox is ready.
echo Starting the app now. Approve the UAC prompt if Windows asks.
echo.
start "" "%APP%"
exit /b 0
