# DS4Xbox 実装詳細

## 1. プロジェクト構造

```
ds4xbox/
├── DS4Xbox.csproj          # プロジェクトファイル（NuGet参照ゼロ）
├── Program.cs              # エントリポイント
├── appsettings.json        # 設定ファイル
├── start_gamepad.bat       # バックグラウンド起動スクリプト
├── Native/
│   ├── HidInterop.cs       # Windows HID API P/Invoke定義
│   └── ViGEmInterop.cs     # ViGEmBus IOCTL P/Invoke定義
├── Core/
│   ├── DualSenseReader.cs  # DualSense HIDレポート読み取り・パース
│   ├── InputMapper.cs      # DualSense → Xbox入力マッピング
│   ├── HidHideController.cs # HidHideCLI制御
│   ├── DriverInstaller.cs  # [NEW] ドライバの自動ダウンロードとセットアップ
│   └── SetupForm.cs        # [NEW] インストール進捗表示フォーム
├── UI/
│   └── TrayApplication.cs  # タスクトレイUI
└── docs/
    ├── SPECIFICATION.md     # 技術仕様書
    └── IMPLEMENTATION.md    # 実装詳細（本ファイル）
```

> [!NOTE]
> `DS4Xbox.csproj` には NuGet パッケージ参照が一切ありません。すべての機能は .NET BCL と Windows P/Invoke のみで実装されています。

## 2. Native/HidInterop.cs の実装詳細

### 使用する Windows API

| DLL | 関数 | 用途 |
|---|---|---|
| `hid.dll` | `HidD_GetHidGuid` | HID クラスの GUID を取得 |
| `hid.dll` | `HidD_GetAttributes` | デバイスの Vendor ID / Product ID を取得 |
| `hid.dll` | `HidD_GetPreparsedData` | デバイスのプリパースデータを取得 |
| `hid.dll` | `HidP_GetCaps` | デバイスの機能情報（レポート長等）を取得 |
| `setupapi.dll` | `SetupDiGetClassDevs` | デバイスクラスに属するデバイス一覧を取得 |
| `setupapi.dll` | `SetupDiEnumDeviceInterfaces` | デバイスインターフェースを列挙 |
| `setupapi.dll` | `SetupDiGetDeviceInterfaceDetail` | デバイスパスを取得 |
| `setupapi.dll` | `SetupDiDestroyDeviceInfoList` | デバイス情報リストを解放 |
| `kernel32.dll` | `CreateFile` | デバイスハンドルをオープン |
| `kernel32.dll` | `ReadFile` | HID レポートを読み取り |
| `kernel32.dll` | `CloseHandle` | ハンドルをクローズ |

### 処理フロー

```mermaid
graph TD
    A["HidD_GetHidGuid()"] --> B["SetupDiGetClassDevs()<br/>DIGCF_PRESENT | DIGCF_DEVICEINTERFACE"]
    B --> C["SetupDiEnumDeviceInterfaces()<br/>各デバイスを列挙"]
    C --> D["SetupDiGetDeviceInterfaceDetail()<br/>デバイスパスを取得"]
    D --> E["CreateFile()<br/>デバイスをオープン"]
    E --> F["HidD_GetAttributes()<br/>VID/PID を確認"]
    F --> G{"DualSense?<br/>0x054C / 0x0CE6"}
    G -->|Yes| H["ReadFile()<br/>HIDレポートを継続的に読み取り"]
    G -->|No| I["CloseHandle()<br/>次のデバイスへ"]
    I --> C
```

### 重要な注意点

> [!WARNING]
> `SP_DEVICE_INTERFACE_DETAIL_DATA` のマーシャリングには特に注意が必要です。32bit と 64bit で構造体のサイズが異なります：
> - **32bit**: `cbSize = 4 + 1 = 5` (DWORD + TCHAR)
> - **64bit**: `cbSize = 4 + 1 + 3(padding) = 8`
>
> `IntPtr.Size` を使って実行時に判定してください。

- `SetupDiGetClassDevs` には `DIGCF_PRESENT | DIGCF_DEVICEINTERFACE` フラグを使用すること
- `CreateFile` は `GENERIC_READ | GENERIC_WRITE`, `FILE_SHARE_READ | FILE_SHARE_WRITE` で開くこと
- ブロッキング `ReadFile` を使用するため、専用スレッドで実行すること

## 3. Native/ViGEmInterop.cs の実装詳細

### ViGEmBus との通信プロトコル

ViGEmBus ドライバは Windows の `DeviceIoControl` API を通じて IOCTL コマンドを受け付けます。NuGet パッケージ（ViGEmClient）を使用せず、IOCTL を直接発行することで外部依存をゼロにしています。

### 使用する IOCTL コード

公式 ViGEmClient ソースコード（MIT, https://github.com/nefarius/ViGEmClient）を参照して同等の IOCTL を定義します：

| IOCTL | 用途 |
|---|---|
| `IOCTL_VIGEM_CHECK_VERSION` | ドライバのバージョン確認 |
| `IOCTL_VIGEM_PLUGIN_TARGET` | 仮想コントローラーの接続（プラグイン） |
| `IOCTL_VIGEM_UNPLUG_TARGET` | 仮想コントローラーの切断 |
| `IOCTL_VIGEM_X360_SUBMIT_REPORT` | Xbox 360 入力レポートの送信 |

### デバイスパス

ViGEmBus のデバイスインターフェース GUID を使い、`SetupDiGetClassDevs()` でデバイスパスを取得し、`CreateFile()` でハンドルを開きます。

```
GUID: {96E42B22-F5E9-42F8-B043-ED0F932F014F}
```

### 入力レポート構造体 (XUSB_SUBMIT_REPORT)

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct XUSB_SUBMIT_REPORT
{
    public uint SerialNo;       // ターゲットのシリアル番号
    public ushort wButtons;     // ボタンビットフィールド
    public byte bLeftTrigger;   // 左トリガー (0-255)
    public byte bRightTrigger;  // 右トリガー (0-255)
    public short sThumbLX;      // 左スティック X (-32768 ~ 32767)
    public short sThumbLY;      // 左スティック Y (-32768 ~ 32767)
    public short sThumbRX;      // 右スティック X (-32768 ~ 32767)
    public short sThumbRY;      // 右スティック Y (-32768 ~ 32767)
}
```

### BSOD リスクの軽減策

> [!CAUTION]
> カーネルドライバとの不正な通信は BSOD（ブルースクリーン）を引き起こす可能性があります。以下の対策を講じています。

- すべての `DeviceIoControl` 呼び出しを `try-catch` で保護
- バッファサイズを厳密に `Marshal.SizeOf` で計算
- 非同期 (Overlapped) I/O は使用せず、同期モードで安全に通信

## 4. Core/DualSenseReader.cs の実装詳細

### HID レポートのパース

64 バイト（USB）のバイト配列から以下を抽出します：

| バイト範囲 | 抽出内容 |
|---|---|
| `bytes[1-4]` | スティック軸（LX, LY, RX, RY） |
| `bytes[5-6]` | トリガー（L2, R2） |
| `bytes[8]` | ボタン群 1（D-Pad + □✕◯△） |
| `bytes[9]` | ボタン群 2（L1, R1, L2d, R2d, Create, Options, L3, R3） |
| `bytes[10]` | ボタン群 3（PS, Touchpad, Mute） |

### パース結果の構造体

```csharp
internal struct DualSenseState
{
    public byte LeftStickX;     // 0-255
    public byte LeftStickY;     // 0-255
    public byte RightStickX;    // 0-255
    public byte RightStickY;    // 0-255
    public byte L2Trigger;      // 0-255
    public byte R2Trigger;      // 0-255
    public byte DPad;           // 0-8 (Hat Switch)
    public bool Square;
    public bool Cross;
    public bool Circle;
    public bool Triangle;
    public bool L1, R1, L3, R3;
    public bool Create, Options;
    public bool PSButton;
    public bool TouchpadClick;
    public bool Mute;
}
```

### Bluetooth 対応

> [!NOTE]
> Bluetooth 接続の場合、Report ID `0x01` の簡易モードではオフセットが異なる可能性があります。実装では接続タイプ（USB/BT）を自動検出し、適切なオフセットを適用します。

検出方法:
- レポートの先頭バイト（Report ID）を確認
- `0x01` → USB または BT 簡易モード
- `0x31` → BT 拡張モード（先頭 2 バイト分のオフセットを加算）

## 5. Core/InputMapper.cs の実装詳細

### 変換処理

`DualSenseState` を受け取り、`XUSB_REPORT` に変換する**純粋関数**として実装します。

```csharp
internal static class InputMapper
{
    public static XUSB_REPORT Map(DualSenseState ds)
    {
        // ボタンマッピング
        // 軸変換
        // Hat Switch 変換
        // → XUSB_REPORT を返す
    }
}
```

**設計方針:**
- 副作用なし（ステートレス）
- テスト容易（入力と出力が明確）
- Hat Switch の変換はルックアップテーブルで実装

### Hat Switch 変換の実装

```csharp
private static readonly ushort[] DPadMap = new ushort[9]
{
    XButtons.DPAD_UP,                              // 0: N
    XButtons.DPAD_UP | XButtons.DPAD_RIGHT,        // 1: NE
    XButtons.DPAD_RIGHT,                           // 2: E
    XButtons.DPAD_DOWN | XButtons.DPAD_RIGHT,      // 3: SE
    XButtons.DPAD_DOWN,                            // 4: S
    XButtons.DPAD_DOWN | XButtons.DPAD_LEFT,       // 5: SW
    XButtons.DPAD_LEFT,                            // 6: W
    XButtons.DPAD_UP | XButtons.DPAD_LEFT,         // 7: NW
    0                                               // 8: Released
};
```

## 6. Core/HidHideController.cs の実装詳細

### HidHideCLI のパス

```
C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe
```

### 制御コマンド

| 操作 | コマンド引数 |
|---|---|
| 隠蔽 ON | `--cloak-on` |
| 隠蔽 OFF | `--cloak-off` |
| アプリ登録 | `--app-reg <実行パス>` |

### 実装方法

```csharp
internal class HidHideController
{
    private const string CliPath =
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";

    public void CloakOn()  => RunCli("--cloak-on");
    public void CloakOff() => RunCli("--cloak-off");

    private void RunCli(string args)
    {
        var psi = new ProcessStartInfo(CliPath, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi)?.WaitForExit();
    }
}
```

### 異常終了時の安全策

> [!IMPORTANT]
> アプリケーションの異常終了時にも隠蔽が解除されるよう、以下のフックを登録します：

- `AppDomain.CurrentDomain.ProcessExit` → `CloakOff()` を呼び出し
- `Console.CancelKeyPress`（Ctrl+C）→ `CloakOff()` を呼び出し

これにより、どのような終了パターンでも DualSense の隠蔽が残留しないようにします。

## 7. UI/TrayApplication.cs の実装詳細

### 基本構成

- `System.Windows.Forms.NotifyIcon` を使用してタスクトレイにアイコンを表示
- `ApplicationContext` を継承してメッセージループを管理

### コンテキストメニュー項目

| 項目 | 種類 | 動作 |
|---|---|---|
| 変換 ON/OFF | チェック付きトグル | 変換の開始/停止、HidHide の切り替え |
| 起動時自動 ON | チェック付きトグル | `appsettings.json` の `startEnabled` を更新 |
| 終了 | 通常項目 | クリーンアップ後にアプリケーションを終了 |

### アイコンの動的生成

外部アイコンファイルに依存せず、コード内で動的に生成します：

```csharp
private Icon CreateIcon(bool isEnabled)
{
    var bmp = new Bitmap(16, 16);
    using (var g = Graphics.FromImage(bmp))
    {
        g.Clear(Color.Transparent);
        var color = isEnabled ? Color.LimeGreen : Color.Gray;
        using (var brush = new SolidBrush(color))
        {
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
    }
    return Icon.FromHandle(bmp.GetHicon());
}
```

| 状態 | アイコン |
|---|---|
| ON | 🟢 緑色の丸 |
| OFF | ⚪ グレーの丸 |

## 8. Program.cs の実装詳細

### 起動シーケンス

```mermaid
graph TD
    A["アプリケーション起動"] --> B["appsettings.json を読み込み"]
    B --> C["TrayApplication を生成"]
    C --> D{"startEnabled == true?"}
    D -->|Yes| E["自動的に変換を開始"]
    D -->|No| F["待機状態で起動"]
    E --> G["Application.Run()<br/>メッセージループに入る"]
    F --> G
    G --> H["終了要求を受信"]
    H --> I["クリーンアップ処理"]
    I --> J["HidHide 隠蔽解除"]
    J --> K["ViGEm 仮想デバイス切断"]
    K --> L["アプリケーション終了"]
```

### エントリポイントのコード構造

```csharp
[STAThread]
static void Main()
{
    // 1. 設定読み込み
    var settings = LoadSettings("appsettings.json");

    // 2. 終了時フック登録
    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

    // 3. WinForms 初期化
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    // 4. TrayApplication 生成・実行
    using var app = new TrayApplication(settings);
    Application.Run(app);
}
```

## 9. スレッドモデル

```mermaid
graph LR
    subgraph "UIスレッド"
        A["WinForms メッセージループ<br/>(タスクトレイ)"]
    end
    subgraph "ポーリングスレッド"
        B["DualSense ReadFile"] --> C["パース"]
        C --> D["マッピング"]
        D --> E["ViGEmBus 送信"]
        E --> B
    end
    A -->|"CancellationToken<br/>(停止指示)"| B
    A -->|"Thread.Start()<br/>(開始指示)"| B
```

| スレッド | 役割 | 属性 |
|---|---|---|
| UI スレッド | WinForms メッセージループ（タスクトレイ） | メインスレッド、STA |
| ポーリングスレッド | DualSense 読み取り → パース → マッピング → ViGEmBus 送信 | `Thread.IsBackground = true` |

### ポーリングスレッドの制御

- **開始**: `Thread.Start()` で新しいバックグラウンドスレッドを起動
- **停止**: `CancellationToken` を使い、安全にスレッドを停止
- **スレッド属性**: `IsBackground = true` により、メインスレッド終了時に自動的に終了

## 10. エラーハンドリング

| シナリオ | 検出方法 | 対応 |
|---|---|---|
| コントローラー未接続 | デバイス列挙で DualSense が見つからない | 接続を待機するリトライループ |
| コントローラー切断 | `ReadFile` がエラーを返す | リトライモードに遷移（再接続を待機） |
| ViGEmBus 未インストール | デバイスパスが見つからない | 起動時に通知し、セットアップ手順を案内 |
| HidHide 未インストール | CLI パスが存在しない | 警告を表示するが、変換機能自体は動作させる |

> [!WARNING]
> HidHide が未インストールの場合、変換機能は動作しますが、ゲーム側に DualSense と仮想 Xbox 360 の両方が見えるため、**二重入力**が発生する可能性があります。

### リトライモードの動作

```mermaid
graph TD
    A["通常動作モード"] -->|"ReadFile エラー"| B["リトライモード"]
    B --> C["デバイス再検索<br/>(5秒間隔)"]
    C --> D{"DualSense 発見?"}
    D -->|Yes| E["デバイスオープン"]
    E --> A
    D -->|No| C
```

- リトライ間隔: **5 秒**
- リトライ中も UI スレッドはブロックされない（ポーリングスレッドのみがリトライ）
- リトライ中はタスクトレイアイコンで切断状態を表示

## 11. ドライバ自動セットアップの実装詳細

### 11.1 Core/DriverInstaller.cs

外部パッケージを一切排除し、.NET 標準の `System.Net.Http.HttpClient` と Windows API `Process.Start` を組み合わせて実装されています。

*   **非同期ダウンロード**: 進捗をパーセンテージ（0-100%）で報告する `Action<int, string>` コールバックをサポートし、バックグラウンドスレッドでストリーミングダウンロードを実行します。
*   **SSL/TLS セキュリティ**: TLS 1.2 および TLS 1.3 以外の古い暗号化プロトコルを無効化し、Man-in-the-Middle (MitM) 攻撃を防御します。
*   **UAC 権限昇格の自動化**: ドライバインストーラー（MSI/EXE）を起動する際、`ProcessStartInfo.Verb = "runas"` を指定することで、Windows のユーザーアカウント制御（UAC）ダイアログを自動的に呼び出し、カーネルドライバインストールに必要な特権を取得します。

### 11.2 Core/SetupForm.cs

WinForms の標準コントロールである `ProgressBar` と `Label` のみを配置した、ダークモード調の進捗ダイアログです。

*   **スレッドセーフな UI 更新**: 別スレッド（ダウンロードタスク）から進捗が更新された場合、`InvokeRequired` と `BeginInvoke` を用いて、WinForms の UI スレッド上で安全に表示を書き換えます。
