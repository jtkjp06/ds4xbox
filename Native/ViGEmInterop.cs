// =============================================================================
// Native/ViGEmInterop.cs
// ViGEmBus ドライバとの通信を行う P/Invoke 定義
// 仮想 Xbox 360 コントローラーの生成・入力レポート送信・切断を担当。
//
// 参照元: ViGEmClient (MIT License, https://github.com/nefarius/ViGEmClient)
//         のソースコードを読み込み、同等のIOCTL通信をC# P/Invokeで自前実装。
//
// 外部ライブラリ（Nefarius.ViGEm.Client NuGet 等）は一切使用しない。
// =============================================================================

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DS4Xbox.Native;

/// <summary>
/// ViGEmBus カーネルドライバとの通信を行う P/Invoke ラッパー。
/// 仮想 Xbox 360 コントローラーの生成・入力レポート送信・切断を担当する。
/// </summary>
internal static class ViGEmInterop
{
    // =========================================================================
    // ViGEmBus デバイスインターフェース GUID
    // =========================================================================

    /// <summary>
    /// ViGEmBus のデバイスインターフェース GUID。
    /// SetupDiGetClassDevs でドライバのデバイスパスを取得するために使用。
    /// </summary>
    private static readonly Guid VIGEM_BUS_INTERFACE_GUID =
        new("96E42B22-F5E9-42F8-B043-ED0F932F014F");

    // =========================================================================
    // IOCTL コード定義
    // =========================================================================
    //
    // CTL_CODE マクロの C# 実装:
    //   CTL_CODE(DeviceType, Function, Method, Access)
    //   = (DeviceType << 16) | (Access << 14) | (Function << 2) | Method
    //
    // ViGEmBus の定義:
    //   DeviceType = FILE_DEVICE_BUS_EXTENDER (0x0000002A)
    //   Method     = METHOD_BUFFERED (0)
    //   Access     = FILE_READ_ACCESS | FILE_WRITE_ACCESS (0x3)

    private const int FILE_DEVICE_BUS_EXTENDER = 0x0000002A;
    private const int METHOD_BUFFERED = 0;
    private const int FILE_ANY_ACCESS = 0;
    private const int FILE_READ_ACCESS = 1;
    private const int FILE_WRITE_ACCESS = 2;

    private static int CTL_CODE(int deviceType, int function, int method, int access)
        => (deviceType << 16) | (access << 14) | (function << 2) | method;

    // Function コード (ViGEmBus ソースの ViGEmBus.h より)
    private static readonly int IOCTL_VIGEM_CHECK_VERSION =
        CTL_CODE(FILE_DEVICE_BUS_EXTENDER, 0x800, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

    private static readonly int IOCTL_VIGEM_PLUGIN_TARGET =
        CTL_CODE(FILE_DEVICE_BUS_EXTENDER, 0x801, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

    private static readonly int IOCTL_VIGEM_UNPLUG_TARGET =
        CTL_CODE(FILE_DEVICE_BUS_EXTENDER, 0x802, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

    private static readonly int IOCTL_VIGEM_X360_SUBMIT_REPORT =
        CTL_CODE(FILE_DEVICE_BUS_EXTENDER, 0x805, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

    private static readonly int IOCTL_VIGEM_WAIT_DEVICE_READY =
        CTL_CODE(FILE_DEVICE_BUS_EXTENDER, 0x80B, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

    // =========================================================================
    // ViGEmBus 通信用構造体
    // =========================================================================

    /// <summary>
    /// ターゲットデバイスの種別。
    /// </summary>
    public enum VIGEM_TARGET_TYPE : uint
    {
        Xbox360Wired = 0,
        DualShock4Wired = 2,
    }

    /// <summary>
    /// プラグイン要求のための構造体。
    /// ViGEmBus ドライバに仮想コントローラーの生成を依頼する。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VIGEM_PLUGIN_TARGET
    {
        public uint Size;               // 構造体のサイズ
        public uint SerialNo;           // シリアル番号（0 = 自動割り当て）
        public VIGEM_TARGET_TYPE Type;  // ターゲットタイプ
        public ushort VendorId;         // 仮想デバイスの VID
        public ushort ProductId;        // 仮想デバイスの PID
    }

    /// <summary>
    /// アンプラグ要求のための構造体。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VIGEM_UNPLUG_TARGET
    {
        public uint Size;
        public uint SerialNo;
    }

    /// <summary>
    /// デバイス準備完了待機のための構造体。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VIGEM_WAIT_DEVICE_READY
    {
        public uint Size;
        public uint SerialNo;
    }

    /// <summary>
    /// バージョン確認のための構造体。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VIGEM_CHECK_VERSION
    {
        public uint Size;
        public uint Version;
    }

    /// <summary>
    /// Xbox 360 入力レポートの送信構造体。
    /// ViGEmBus に仮想コントローラーの入力状態を送信する。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XUSB_SUBMIT_REPORT
    {
        public uint Size;           // 構造体のサイズ
        public uint SerialNo;       // ターゲットのシリアル番号
        public ushort wButtons;     // ボタンビットフィールド
        public byte bLeftTrigger;   // 左トリガー (0-255)
        public byte bRightTrigger;  // 右トリガー (0-255)
        public short sThumbLX;      // 左スティック X (-32768 ~ 32767)
        public short sThumbLY;      // 左スティック Y (-32768 ~ 32767)
        public short sThumbRX;      // 右スティック X (-32768 ~ 32767)
        public short sThumbRY;      // 右スティック Y (-32768 ~ 32767)
    }

    // =========================================================================
    // Xbox 360 ボタンフラグ
    // =========================================================================

    [Flags]
    public enum XboxButton : ushort
    {
        DpadUp = 0x0001,
        DpadDown = 0x0002,
        DpadLeft = 0x0004,
        DpadRight = 0x0008,
        Start = 0x0010,
        Back = 0x0020,
        LeftThumb = 0x0040,
        RightThumb = 0x0080,
        LeftShoulder = 0x0100,
        RightShoulder = 0x0200,
        Guide = 0x0400,
        A = 0x1000,
        B = 0x2000,
        X = 0x4000,
        Y = 0x8000,
    }

    // =========================================================================
    // DLL インポート
    // =========================================================================

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData,
        ref Guid interfaceClassGuid, uint memberIndex,
        ref HidInterop.SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref HidInterop.SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        ref int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        int ioControlCode,
        ref byte inBuffer,
        int inBufferSize,
        ref byte outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        int ioControlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    // =========================================================================
    // 定数
    // =========================================================================

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    // =========================================================================
    // 公開メソッド
    // =========================================================================

    /// <summary>
    /// ViGEmBus ドライバのデバイスハンドルを開く。
    /// ドライバが未インストールの場合は null を返す。
    /// </summary>
    public static SafeFileHandle? OpenBus()
    {
        string? devicePath = FindViGEmBusDevicePath();
        if (devicePath == null)
            return null;

        var handle = CreateFile(
            devicePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        return handle.IsInvalid ? null : handle;
    }

    /// <summary>
    /// 仮想 Xbox 360 コントローラーをプラグイン（接続）する。
    /// 戻り値はターゲットのシリアル番号。失敗時は 0。
    /// </summary>
    public static uint PluginTarget(SafeFileHandle busHandle)
    {
        var plugin = new VIGEM_PLUGIN_TARGET
        {
            Size = (uint)Marshal.SizeOf<VIGEM_PLUGIN_TARGET>(),
            SerialNo = 0, // 自動割り当て
            Type = VIGEM_TARGET_TYPE.Xbox360Wired,
            VendorId = 0x045E, // Microsoft
            ProductId = 0x028E, // Xbox 360 Controller
        };

        byte[] inBuffer = StructToBytes(plugin);
        byte[] outBuffer = new byte[inBuffer.Length];

        bool success = DeviceIoControl(
            busHandle,
            IOCTL_VIGEM_PLUGIN_TARGET,
            ref inBuffer[0],
            inBuffer.Length,
            ref outBuffer[0],
            outBuffer.Length,
            out _,
            IntPtr.Zero);

        if (!success)
            return 0;

        // ドライバがシリアル番号を出力バッファに返す
        var result = BytesToStruct<VIGEM_PLUGIN_TARGET>(outBuffer);

        uint serialNo = result.SerialNo;

        // デバイスが PnP マネージャーに認識されるのを待つ
        WaitDeviceReady(busHandle, serialNo);

        return serialNo;
    }

    /// <summary>
    /// 仮想コントローラーをアンプラグ（切断）する。
    /// </summary>
    public static bool UnplugTarget(SafeFileHandle busHandle, uint serialNo)
    {
        var unplug = new VIGEM_UNPLUG_TARGET
        {
            Size = (uint)Marshal.SizeOf<VIGEM_UNPLUG_TARGET>(),
            SerialNo = serialNo,
        };

        byte[] inBuffer = StructToBytes(unplug);
        byte[] outBuffer = new byte[inBuffer.Length];

        return DeviceIoControl(
            busHandle,
            IOCTL_VIGEM_UNPLUG_TARGET,
            ref inBuffer[0],
            inBuffer.Length,
            ref outBuffer[0],
            outBuffer.Length,
            out _,
            IntPtr.Zero);
    }

    /// <summary>
    /// Xbox 360 入力レポートを仮想コントローラーに送信する。
    /// メインのポーリングループから毎フレーム呼び出される。
    /// </summary>
    public static bool SubmitReport(SafeFileHandle busHandle, XUSB_SUBMIT_REPORT report)
    {
        report.Size = (uint)Marshal.SizeOf<XUSB_SUBMIT_REPORT>();

        byte[] inBuffer = StructToBytes(report);
        byte[] outBuffer = new byte[inBuffer.Length];

        return DeviceIoControl(
            busHandle,
            IOCTL_VIGEM_X360_SUBMIT_REPORT,
            ref inBuffer[0],
            inBuffer.Length,
            ref outBuffer[0],
            outBuffer.Length,
            out _,
            IntPtr.Zero);
    }

    // =========================================================================
    // 内部ヘルパー
    // =========================================================================

    /// <summary>
    /// デバイスが PnP マネージャーに認識されるまで待つ。
    /// </summary>
    private static void WaitDeviceReady(SafeFileHandle busHandle, uint serialNo)
    {
        var wait = new VIGEM_WAIT_DEVICE_READY
        {
            Size = (uint)Marshal.SizeOf<VIGEM_WAIT_DEVICE_READY>(),
            SerialNo = serialNo,
        };

        byte[] inBuffer = StructToBytes(wait);
        byte[] outBuffer = new byte[inBuffer.Length];

        // このIOCTLはデバイスが準備完了するまでブロックする
        DeviceIoControl(
            busHandle,
            IOCTL_VIGEM_WAIT_DEVICE_READY,
            ref inBuffer[0],
            inBuffer.Length,
            ref outBuffer[0],
            outBuffer.Length,
            out _,
            IntPtr.Zero);
    }

    /// <summary>
    /// ViGEmBus デバイスのインターフェースパスを検索する。
    /// </summary>
    private static string? FindViGEmBusDevicePath()
    {
        Guid guid = VIGEM_BUS_INTERFACE_GUID;

        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            ref guid, null, IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            return null;

        try
        {
            var interfaceData = new HidInterop.SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = Marshal.SizeOf(interfaceData);

            if (!SetupDiEnumDeviceInterfaces(
                deviceInfoSet, IntPtr.Zero, ref guid, 0, ref interfaceData))
                return null;

            // 必要なバッファサイズを取得
            int requiredSize = 0;
            SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet, ref interfaceData,
                IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);

            if (requiredSize <= 0)
                return null;

            IntPtr detailDataBuffer = Marshal.AllocHGlobal(requiredSize);
            try
            {
                int cbSize = IntPtr.Size == 8 ? 8 : 6;
                Marshal.WriteInt32(detailDataBuffer, cbSize);

                if (SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet, ref interfaceData,
                    detailDataBuffer, requiredSize,
                    ref requiredSize, IntPtr.Zero))
                {
                    return Marshal.PtrToStringAuto(detailDataBuffer + 4);
                }

                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(detailDataBuffer);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    /// <summary>
    /// 構造体をバイト配列に変換する。
    /// DeviceIoControl に渡すバッファの作成に使用。
    /// </summary>
    private static byte[] StructToBytes<T>(T structure) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structure, ptr, false);
            Marshal.Copy(ptr, buffer, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return buffer;
    }

    /// <summary>
    /// バイト配列を構造体に変換する。
    /// DeviceIoControl の出力バッファの解析に使用。
    /// </summary>
    private static T BytesToStruct<T>(byte[] buffer) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(buffer, 0, ptr, size);
            return Marshal.PtrToStructure<T>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
