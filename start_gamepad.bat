@echo off
REM =============================================================================
REM start_gamepad.bat
REM DS4Xbox を黒い画面（コマンドプロンプト）を表示せずにバックグラウンドで起動する。
REM
REM 使い方:
REM   1. このバッチファイルをダブルクリックする
REM   2. UAC（管理者権限）のダイアログが表示されるので「はい」を選択
REM   3. タスクトレイに DS4Xbox のアイコンが表示される
REM
REM Windows のスタートアップに登録しておけば、PC起動時から自動常駐:
REM   Win+R → shell:startup → このファイルのショートカットを配置
REM =============================================================================

REM 実行ファイルが存在するか確認
if exist "%~dp0bin\Release\net8.0-windows\DS4Xbox.exe" (
    start "" "%~dp0bin\Release\net8.0-windows\DS4Xbox.exe"
) else if exist "%~dp0bin\Debug\net8.0-windows\DS4Xbox.exe" (
    start "" "%~dp0bin\Debug\net8.0-windows\DS4Xbox.exe"
) else (
    echo ================================================================
    echo  DS4Xbox がまだビルドされていません。
    echo  以下のコマンドでビルドしてください:
    echo.
    echo    dotnet build -c Release
    echo.
    echo  ビルド後、このバッチファイルを再度実行してください。
    echo ================================================================
    pause
)
