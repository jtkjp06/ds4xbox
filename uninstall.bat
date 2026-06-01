@echo off
chcp 65001 > nul
title DS4Xbox アンインストーラー
echo ======================================================
echo             DS4Xbox アンインストーラー
echo ======================================================
echo.

:: 管理者権限のチェック
openfiles >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] このスクリプトは管理者権限で実行する必要があります。
    echo 右クリックして「管理者として実行」を選択してください。
    pause
    exit /b 1
)

echo [1/5] DS4Xbox プロセスを終了しています...
taskkill /f /im DS4Xbox.exe >nul 2>&1
timeout /t 1 >nul

echo [2/5] 自動起動設定を解除しています...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "DS4Xbox" /f >nul 2>&1

echo [3/5] HidHide のホワイトリストから登録を解除しています...
set "HIDHIDE_CLI=C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
if exist "%HIDHIDE_CLI%" (
    "%HIDHIDE_CLI%" --app-unreg "%~dp0DS4Xbox.exe" >nul 2>&1
    "%HIDHIDE_CLI%" --cloak-off >nul 2>&1
    echo [INFO] HidHide の設定をクリーンアップしました。
)

echo [4/5] 設定ファイルを削除しています...
if exist "appsettings.json" (
    del "appsettings.json"
    echo [INFO] appsettings.json を削除しました。
)

echo [5/5] ドライバ類のアンインストール案内...
echo.
echo ======================================================
echo 以下のカーネルドライバを完全にシステムから削除したい場合は、
echo 以下の手順で手動でアンインストールを行ってください：
echo.
echo 1. Windowsの「設定」 ➔ 「アプリ」 ➔ 「インストールされているアプリ」を開きます。
echo 2. 一覧から以下の2つを見つけてアンインストールしてください：
echo    - 「ViGEmBus Driver」
echo    - 「HidHide Driver」
echo.
echo ※ または、公式セキュリティクリーナー「Legacinator」を実行して
echo   残存ファイルを一括で安全にクリーンアップしてください。
echo ======================================================
echo.
echo アンインストール処理の準備が完了しました。
echo このバッチファイルがあるフォルダ内のファイルを削除して作業を完了してください。
echo.
pause
