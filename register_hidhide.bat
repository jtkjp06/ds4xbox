@echo off
REM =============================================================================
REM register_hidhide.bat
REM DS4Xbox を HidHide のホワイトリストに登録するスクリプト（管理者権限で実行）
REM =============================================================================

:: UAC権限の確認
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo 管理者権限が必要です。UACダイアログで「はい」を選択してください...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "HIDHIDE_CLI=C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
if not exist "%HIDHIDE_CLI%" (
    echo [エラー] HidHideCLI が見つかりません。HidHide が正しくインストールされているか確認してください。
    pause
    exit /b
)

echo.
echo DS4Xbox 実行ファイルを HidHide ホワイトリストに登録しています...

set "APP=%~dp0DS4Xbox.exe"

if not exist "%APP%" (
    set "APP=%~dp0bin\Release\net8.0-windows\win-x64\publish\DS4Xbox.exe"
)

if not exist "%APP%" (
    echo [エラー] DS4Xbox.exe が見つかりません。
    echo 配布ZIPの場合はこのバッチを DS4Xbox.exe と同じフォルダに置いてください。
    echo ソースチェックアウトの場合は setup_gamepad.bat を先に実行してください。
    pause
    exit /b 1
)

"%HIDHIDE_CLI%" --app-reg "%APP%"
echo [OK] 登録完了: %APP%

echo.
echo ================================================================
echo 登録が完了しました。
echo タスクトレイの DS4Xbox を一度「終了」してから、
echo start_gamepad.bat で再度起動し直してください。
echo ================================================================
pause
