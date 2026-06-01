@echo off
REM =============================================================================
REM diagnose_gamepad.bat
REM Run DS4Xbox hardware/driver diagnostics.
REM =============================================================================

set "APP=%~dp0DS4Xbox.exe"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Administrator permission is required for DS4Xbox diagnostics.
    echo Approve the UAC prompt, then read the diagnostic result in the new window.
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%ComSpec%' -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs -Wait"
    exit /b %errorlevel%
)

if not exist "%APP%" (
    set "APP=%~dp0bin\Release\net8.0-windows\win-x64\publish\DS4Xbox.exe"
)

if exist "%APP%" (
    for %%I in ("%APP%") do set "APPDIR=%%~dpI"
    "%APP%" --diagnose --no-dialog
    set "EXITCODE=%errorlevel%"
    echo.
    echo ================================================================
    if "%EXITCODE%"=="0" (
        echo  Diagnostic finished successfully.
        echo  joy.cpl should show: Controller (XBOX 360 For Windows)
    ) else (
        echo  Diagnostic failed. Please check the FAIL line above.
    )
    echo  Result file: %APPDIR%diagnostic_result.txt
    echo  Log file: %APPDIR%ds4xbox.log
    echo ================================================================
    pause
    exit /b %EXITCODE%
)

echo ================================================================
echo  DS4Xbox publish output was not found.
echo  Please publish the project first:
echo.
echo    dotnet publish .\DS4Xbox.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
echo.
echo  After publishing, run this script again.
echo ================================================================
pause
exit /b 1
