// =============================================================================
// Core/DualSenseReader.cs
// DualSense コントローラーの HID レポートを読み取り、パースするクラス。
// USB (Report ID 0x01) および Bluetooth (Report ID 0x01/0x31) の両方に対応。
//
// 【設計判断】Overlapped I/O + WaitForSingleObject を使用
//   同期モードの ReadFile はレポート到着までスレッドをブロックし、
//   タスクトレイ UI のフリーズや CancellationToken による停止不能を
//   引き起こすため、非同期 I/O でタイムアウト付きの読み取りを行う。
//
// 【Bluetooth 段階的対応】
//   Windows 標準 BT 接続直後は簡易モード (Report ID 0x01) のみ。
//   まず 0x01 で完全なマッピングを実装し、精度に不満が出た段階で
//   Feature Report 送信による 0x31 拡張モード切り替えを追加する。
// =============================================================================

using DS4Xbox.Native;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace DS4Xbox.Core;

/// <summary>
/// DualSense コントローラーの入力状態を表す構造体。
/// HID レポートのバイト列をパースした結果がここに格納される。
/// </summary>
public struct DualSenseState
{
    // アナログスティック (0-255, center=128)
    public byte LeftStickX;
    public byte LeftStickY;
    public byte RightStickX;
    public byte RightStickY;

    // アナログトリガー (0-255)
    public byte L2Trigger;
    public byte R2Trigger;

    // フェイスボタン
    public bool Cross;      // ✕
    public bool Circle;     // ◯
    public bool Triangle;   // △
    public bool Square;     // □

    // ショルダー
    public bool L1;
    public bool R1;

    // サムスティック押し込み
    public bool L3;
    public bool R3;

    // メニューボタン
    public bool Create;
    public bool Options;
    public bool PSButton;
    public bool TouchpadClick;
    public bool Mute;

    // D-Pad (Hat Switch 値: 0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8=Released)
    public byte DPad;
}

/// <summary>
/// DualSense コントローラーの HID レポートを読み取り、
/// DualSenseState 構造体にパースするクラス。
///
/// Overlapped I/O を使用し、タイムアウト付きの非ブロッキング読み取りを行う。
/// </summary>
public sealed class DualSenseReader : IDisposable
{
    private SafeFileHandle? _deviceHandle;
    private IntPtr _readBufferPtr = IntPtr.Zero;
    private byte[]? _readBuffer;
    private int _readBufferSize;
    private bool _isUsb;
    private bool _disposed;
    private int _logCounter = 0;

    // Overlapped I/O 用のイベントハンドル
    private IntPtr _overlappedEvent = IntPtr.Zero;

    /// <summary>
    /// 読み取りタイムアウト（ミリ秒）。
    /// この時間以内にレポートが届かなければ ReadState は false を返す。
    /// CancellationToken によるキャンセルを可能にするため、短めに設定。
    /// </summary>
    public uint ReadTimeoutMs { get; set; } = 100;

    /// <summary>
    /// DualSense が接続されているかどうか。
    /// </summary>
    public bool IsConnected => _deviceHandle != null && !_deviceHandle.IsInvalid && !_deviceHandle.IsClosed;

    /// <summary>
    /// DualSense コントローラーに接続する。
    /// </summary>
    /// <returns>接続成功時 true</returns>
    public bool Connect()
    {
        Disconnect();

        string? devicePath = HidInterop.FindDualSenseDevicePath();
        if (devicePath == null)
            return false;

        AppLog.Info($"DualSense device path found: {devicePath}");
        _deviceHandle = HidInterop.OpenDevice(devicePath);
        if (_deviceHandle == null)
        {
            AppLog.Error("DualSense device could not be opened.");
            return false;
        }

        // 入力レポート長を取得
        _readBufferSize = HidInterop.GetInputReportLength(_deviceHandle);
        AppLog.Info($"DualSense input report length: {_readBufferSize}");
        _readBuffer = new byte[_readBufferSize];
        _readBufferPtr = Marshal.AllocHGlobal(_readBufferSize);

        // USB か Bluetooth かの判定:
        // 入力レポート長が 78 なら Bluetooth、それ以外(通常64)なら USB と判定する
        _isUsb = _readBufferSize != 78;
        AppLog.Info($"DualSense transport detected. USB={_isUsb}, BufferSize={_readBufferSize}");

        if (!_isUsb)
        {
            // Bluetooth 接続時:
            // DualSense を拡張モード (Report ID 0x31) に強制移行させるための Output Report を送信
            // これにより、高解像度のトリガーやジャイロ、フルボタン機能が有効になる。
            byte[] magicPacket = new byte[78];
            magicPacket[0] = 0x31; // Report ID
            magicPacket[1] = 0x02; // Flags for Bluetooth Output Report
            bool magicResult = HidInterop.HidD_SetOutputReport(_deviceHandle, magicPacket, magicPacket.Length);
            AppLog.Info($"DualSense Bluetooth output report sent. Success={magicResult}");
            
            // 少し待ってから読み取りを開始する
            Thread.Sleep(50);
        }

        // Overlapped I/O 用のイベントオブジェクトを作成
        _overlappedEvent = HidInterop.CreateEvent(IntPtr.Zero, true, false, null);

        return true;
    }

    /// <summary>
    /// DualSense から切断する。
    /// </summary>
    public void Disconnect()
    {
        if (_deviceHandle != null && !_deviceHandle.IsInvalid)
        {
            // 保留中の I/O をキャンセル
            HidInterop.CancelIo(_deviceHandle);
        }

        _deviceHandle?.Dispose();
        _deviceHandle = null;
        _readBuffer = null;

        if (_readBufferPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_readBufferPtr);
            _readBufferPtr = IntPtr.Zero;
        }

        if (_overlappedEvent != IntPtr.Zero)
        {
            HidInterop.CloseHandle(_overlappedEvent);
            _overlappedEvent = IntPtr.Zero;
        }
    }

    /// <summary>
    /// DualSense の入力状態を1フレーム分読み取る。
    /// Overlapped I/O + WaitForSingleObject による非ブロッキング読み取り。
    /// タイムアウト (ReadTimeoutMs) 以内にデータが届かない場合は false を返す。
    /// これにより、CancellationToken チェックと交互に呼び出すことで
    /// 安全にポーリングループを停止できる。
    /// </summary>
    /// <param name="state">読み取った入力状態</param>
    /// <returns>読み取り成功時 true、タイムアウト・切断・エラー時 false</returns>
    public bool ReadState(out DualSenseState state)
    {
        state = default;

        if (_deviceHandle == null || _deviceHandle.IsInvalid || _readBuffer == null || _readBufferPtr == IntPtr.Zero)
        {
            return false;
        }

        // OVERLAPPED 構造体を初期化
        var overlapped = new HidInterop.OVERLAPPED
        {
            hEvent = _overlappedEvent
        };

        // イベントをリセット（必須：これがないと2回目以降の待機が即座に通過してしまう）
        HidInterop.ResetEvent(_overlappedEvent);

        // 非同期 ReadFile を開始
        int bytesRead = 0;
        bool readResult = HidInterop.ReadFile(
            _deviceHandle,
            _readBufferPtr,
            _readBufferSize,
            out _,
            ref overlapped);

        if (!readResult)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 997) // ERROR_IO_PENDING
            {
                if (_logCounter++ % 100 == 0)
                    AppLog.Error($"DualSense ReadFile failed. Win32Error={error}");
                return false;
            }

            // タイムアウト付きで待機
            uint waitResult = HidInterop.WaitForSingleObject(_overlappedEvent, ReadTimeoutMs);
            if (waitResult == 0) // WAIT_OBJECT_0
            {
                if (!HidInterop.GetOverlappedResult(_deviceHandle, ref overlapped, out bytesRead, false))
                {
                    int ovError = Marshal.GetLastWin32Error();
                    if (_logCounter++ % 100 == 0)
                        AppLog.Error($"DualSense GetOverlappedResult failed. Win32Error={ovError}");
                    return false;
                }
            }
            else
            {
                if (_logCounter++ % 100 == 0)
                    AppLog.Info($"DualSense read timed out or wait failed. Result={waitResult}");
                // タイムアウト (WAIT_TIMEOUT) またはエラー
                HidInterop.CancelIo(_deviceHandle);
                return false;
            }
        }
        else
        {
            // 非同期なしで直ちに完了した場合
            if (!HidInterop.GetOverlappedResult(_deviceHandle, ref overlapped, out bytesRead, false))
                return false;
        }

        if (bytesRead == 0)
            return false;

        // ネイティブバッファからマネージド配列へコピー
        Marshal.Copy(_readBufferPtr, _readBuffer, 0, bytesRead);

        // レポートをパース
        state = ParseReport(_readBuffer, bytesRead);
        
        // --- 100回に1回程度ログを出す (スパム防止) ---
        if (_logCounter++ % 100 == 0)
        {
            AppLog.Info($"DualSense read {bytesRead} bytes. ReportId=0x{_readBuffer[0]:X2}, LX={state.LeftStickX}, LY={state.LeftStickY}");
        }

        return true;
    }

    /// <summary>
    /// HID レポートのバイト列を DualSenseState にパースする。
    /// USB と Bluetooth でオフセットが異なる。
    /// </summary>
    private DualSenseState ParseReport(byte[] report, int length)
    {
        var state = new DualSenseState();
        if (length == 0) return state;

        // Bluetooth 簡易モード (Report ID 0x01, 長さ10バイト前後) のパース
        if (!_isUsb && report[0] == 0x01 && length >= 10)
        {
            state.LeftStickX = report[1];
            state.LeftStickY = report[2];
            state.RightStickX = report[3];
            state.RightStickY = report[4];

            byte b1 = report[5];
            state.DPad = (byte)(b1 & 0x0F);
            state.Square = (b1 & 0x10) != 0;
            state.Cross = (b1 & 0x20) != 0;
            state.Circle = (b1 & 0x40) != 0;
            state.Triangle = (b1 & 0x80) != 0;

            byte b2 = report[6];
            state.L1 = (b2 & 0x01) != 0;
            state.R1 = (b2 & 0x02) != 0;
            state.Create = (b2 & 0x10) != 0;
            state.Options = (b2 & 0x20) != 0;
            state.L3 = (b2 & 0x40) != 0;
            state.R3 = (b2 & 0x80) != 0;

            byte b3 = report[7];
            state.PSButton = (b3 & 0x01) != 0;
            state.TouchpadClick = (b3 & 0x02) != 0;
            state.Mute = (b3 & 0x04) != 0;

            state.L2Trigger = report[8];
            state.R2Trigger = report[9];

            return state;
        }

        // オフセットの決定
        // USB:       Report ID (0x01) + データ → オフセット 1 からデータ開始
        // Bluetooth: 拡張モード (0x31) → オフセット 2 からデータ開始
        int offset;

        if (_isUsb)
        {
            offset = 1;
        }
        else
        {
            offset = 2; // 基本的に 0x31 のみここに来るはず
        }

        // 十分なデータがあるか確認
        if (length < offset + 10)
            return state;

        // --- アナログスティック ---
        state.LeftStickX = report[offset + 0];
        state.LeftStickY = report[offset + 1];
        state.RightStickX = report[offset + 2];
        state.RightStickY = report[offset + 3];

        // --- アナログトリガー ---
        state.L2Trigger = report[offset + 4];
        state.R2Trigger = report[offset + 5];

        // --- シーケンスカウンター (offset + 6) はスキップ ---

        // --- ボタングループ 1 (offset + 7) ---
        byte buttons1 = report[offset + 7];
        state.DPad = (byte)(buttons1 & 0x0F);       // Bit 0-3: D-Pad (Hat Switch)
        state.Square = (buttons1 & 0x10) != 0;       // Bit 4: □
        state.Cross = (buttons1 & 0x20) != 0;        // Bit 5: ✕
        state.Circle = (buttons1 & 0x40) != 0;       // Bit 6: ◯
        state.Triangle = (buttons1 & 0x80) != 0;     // Bit 7: △

        // --- ボタングループ 2 (offset + 8) ---
        byte buttons2 = report[offset + 8];
        state.L1 = (buttons2 & 0x01) != 0;           // Bit 0: L1
        state.R1 = (buttons2 & 0x02) != 0;           // Bit 1: R1
        // Bit 2: L2 digital (不使用、アナログ値を使う)
        // Bit 3: R2 digital (不使用、アナログ値を使う)
        state.Create = (buttons2 & 0x10) != 0;        // Bit 4: Create
        state.Options = (buttons2 & 0x20) != 0;       // Bit 5: Options
        state.L3 = (buttons2 & 0x40) != 0;            // Bit 6: L3
        state.R3 = (buttons2 & 0x80) != 0;            // Bit 7: R3

        // --- ボタングループ 3 (offset + 9) ---
        byte buttons3 = report[offset + 9];
        state.PSButton = (buttons3 & 0x01) != 0;      // Bit 0: PS Button
        state.TouchpadClick = (buttons3 & 0x02) != 0;  // Bit 1: Touchpad Click
        state.Mute = (buttons3 & 0x04) != 0;           // Bit 2: Mute

        return state;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Disconnect();
            _disposed = true;
        }
    }
}
