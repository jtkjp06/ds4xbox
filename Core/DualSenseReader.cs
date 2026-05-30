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
    private byte[]? _readBuffer;
    private bool _isUsb;
    private bool _disposed;

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

        _deviceHandle = HidInterop.OpenDevice(devicePath);
        if (_deviceHandle == null)
            return false;

        // 入力レポート長を取得
        ushort reportLength = HidInterop.GetInputReportLength(_deviceHandle);
        _readBuffer = new byte[reportLength];

        // USB か Bluetooth かの判定:
        // USB の場合、入力レポート長は通常 64 バイト
        // Bluetooth の場合、78 バイト以上になることが多い
        _isUsb = reportLength <= 64;

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

        if (_deviceHandle == null || _readBuffer == null || _overlappedEvent == IntPtr.Zero)
            return false;

        // OVERLAPPED 構造体を初期化
        var overlapped = new HidInterop.OVERLAPPED();
        overlapped.hEvent = _overlappedEvent;
        HidInterop.ResetEvent(_overlappedEvent);

        // 非同期 ReadFile を開始
        bool readResult = HidInterop.ReadFile(
            _deviceHandle,
            _readBuffer,
            _readBuffer.Length,
            out _,
            ref overlapped);

        if (!readResult)
        {
            int error = Marshal.GetLastWin32Error();
            const int ERROR_IO_PENDING = 997;

            if (error != ERROR_IO_PENDING)
                return false; // I/O 以外のエラー（切断など）

            // I/O が保留中 → イベントの完了をタイムアウト付きで待つ
            uint waitResult = HidInterop.WaitForSingleObject(_overlappedEvent, ReadTimeoutMs);

            if (waitResult == 0x00000102) // WAIT_TIMEOUT
            {
                // タイムアウト: I/O をキャンセルして戻る
                HidInterop.CancelIo(_deviceHandle);
                return false;
            }

            if (waitResult != 0x00000000) // WAIT_OBJECT_0 以外
                return false;
        }

        // 転送されたバイト数を取得
        if (!HidInterop.GetOverlappedResult(_deviceHandle, ref overlapped, out int bytesRead, false))
            return false;

        if (bytesRead == 0)
            return false;

        // レポートをパース
        state = ParseReport(_readBuffer, bytesRead);
        return true;
    }

    /// <summary>
    /// HID レポートのバイト列を DualSenseState にパースする。
    /// USB と Bluetooth でオフセットが異なる。
    /// </summary>
    private DualSenseState ParseReport(byte[] report, int length)
    {
        var state = new DualSenseState();

        // オフセットの決定
        // USB:       Report ID (0x01) + データ → オフセット 1 からデータ開始
        // Bluetooth: Report ID が先頭にある場合、追加ヘッダがある場合がある
        int offset;

        if (_isUsb)
        {
            // USB: byte[0] = Report ID (0x01), byte[1] から入力データ
            offset = 1;
        }
        else
        {
            // Bluetooth 簡易モード (Report ID 0x01):
            //   USB と同じオフセット
            // Bluetooth 拡張モード (Report ID 0x31):
            //   先頭に 2 バイトのヘッダが追加される
            if (report[0] == 0x31)
            {
                offset = 2; // ヘッダ分をスキップしてからデータ開始
            }
            else
            {
                offset = 1;
            }
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
