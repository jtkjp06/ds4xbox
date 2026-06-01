# DS4Xbox v1.0.0 Release Notes

DS4Xbox は、PS5 DualSense / DualSense Edge を Windows 上で Xbox 360 コントローラーとして扱うための常駐型変換ツールです。

## Highlights

- DualSense HID 入力を読み取り、Xbox 360 XInput 相当の入力へ変換します。
- 仮想 Xbox 360 コントローラーの作成と入力送信には公式 `Nefarius.ViGEm.Client` を使用します。
- HidHide と連携し、変換 ON 時だけ物理 DualSense をゲームから隠して二重入力を抑えます。
- トレイアイコンから ON/OFF、自動 ON 設定、ドライバセットアップ、アンインストール案内を実行できます。
- `diagnose_gamepad.bat` で ViGEmBus、仮想 Xbox 360 作成、DualSense 検出、入力読み取り、ViGEmClient 送信を順番に診断できます。
- 診断結果は `diagnostic_result.txt`、詳細ログは `ds4xbox.log` に保存されます。
- `open_game_controllers.bat` で Windows の `joy.cpl` を開き、`Controller (XBOX 360 For Windows)` の入力反映を確認できます。

## Package Contents

`ds4xbox_release.zip` には以下を含めます。

- `DS4Xbox.exe`
- `appsettings.json`
- `start_gamepad.bat`
- `diagnose_gamepad.bat`
- `open_game_controllers.bat`
- `uninstall.bat`
- `README.md`
- `LICENSE`

## Requirements

- Windows 10 21H2 以降 / Windows 11
- ViGEmBus v1.22.0
- HidHide v1.4.x
- DualSense / DualSense Edge

配布版は self-contained publish の単一 exe を同梱するため、通常利用では .NET ランタイムの別途インストールは不要です。ソースから `setup_gamepad.bat` を使う場合は .NET 8 SDK が必要です。

## Quick Start

1. `ds4xbox_release.zip` を任意のフォルダに解凍します。
2. DualSense を USB または Bluetooth で接続します。
3. `start_gamepad.bat` を実行し、UAC が表示された場合は許可します。
4. タスクトレイの DS4Xbox アイコンから「変換 ON」を選択します。
5. `diagnose_gamepad.bat` で `OK: ViGEmClient SubmitReport succeeded.` まで進むことを確認します。
6. `open_game_controllers.bat` で `Controller (XBOX 360 For Windows)` の入力反映を確認します。

## Verification

リリース作成時に以下を実行します。

```powershell
dotnet build .\ds4xbox.sln -c Release --no-restore
dotnet test .\ds4xbox.sln -c Release --no-restore
dotnet publish .\DS4Xbox.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
