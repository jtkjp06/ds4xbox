// =============================================================================
// Native/HidInterop.cs
// Windows HID API の P/Invoke 定義
// DualSense コントローラーを HID デバイスとして列挙・接続・読み取りするための
// 低レベル Windows API ラッパー。
// 外部ライブラリ不使用。すべて kernel32.dll / setupapi.dll / hid.dll への直接呼び出し。
//
// 【設計判断】Overlapped I/O を使用
//   同期モードの ReadFile はコントローラーからレポートが届くまでスレッドを
//   完全にブロックするため、タスクトレイUI のフリーズを引き起こす。
//   FILE_FLAG_OVERLAPPED + WaitForSingleObject (タイムアウト付き) を使用し、
//   CancellationToken による安全なキャンセルを可能にする。
// =============================================================================

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DS4Xbox.Native;

/// <summary>
/// Windows HID (Human Interface Device) API の P/Invoke 定義と
/// デバイスの列挙・接続ユーティリティ。
/// </summary>
internal static class HidInterop
{
    // =========================================================================
    // 定数
    // =========================================================================

    // SetupDiGetClassDevs フラグ
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    // CreateFile アクセスフラグ
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    // WaitForSingleObject 戻り値
    private const uint WAIT_OBJECT_0 = 0x00000000;
    private const uint WAIT_TIMEOUT = 0x00000102;
    private const uint INFINITE = 0xFFFFFFFF;

    // DualSense のデバイス識別子
    public const ushort DUALSENSE_VENDOR_ID = 0x054C;   // Sony
    public const ushort DUALSENSE_PRODUCT_ID = 0x0CE6;  // DualSense
    public const ushort DUALSENSE_EDGE_PRODUCT_ID = 0x0DF2; // DualSense Edge

    // =========================================================================
    // 構造体
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    /// <summary>
    /// OVERLAPPED 構造体（非同期 I/O 用）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct OVERLAPPED
    {
        public UIntPtr Internal;
        public UIntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }

    // =========================================================================
    // DLL インポート: hid.dll
    // =========================================================================

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetOutputReport(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    // =========================================================================
    // DLL インポート: setupapi.dll
    // =========================================================================

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        ref int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    // =========================================================================
    // DLL インポート: kernel32.dll
    // =========================================================================

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(
        SafeFileHandle hFile,
        IntPtr buffer,
        int numberOfBytesToRead,
        out int numberOfBytesRead,
        ref OVERLAPPED overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(
        SafeFileHandle hFile,
        ref OVERLAPPED overlapped,
        out int numberOfBytesTransferred,
        bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEvent(
        IntPtr lpEventAttributes,
        bool bManualReset,
        bool bInitialState,
        string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ResetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIo(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    // =========================================================================
    // 公開メソッド
    // =========================================================================

    /// <summary>
    /// システムに接続されている全 HID デバイスを列挙し、
    /// DualSense (VID:054C / PID:0CE6) のデバイスパスを返す。
    /// 見つからない場合は null を返す。
    /// </summary>
    public static string? FindDualSenseDevicePath()
    {
        HidD_GetHidGuid(out Guid hidGuid);

        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            ref hidGuid,
            null,
            IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            return null;

        try
        {
            var interfaceData = new SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = Marshal.SizeOf(interfaceData);

            uint index = 0;
            while (SetupDiEnumDeviceInterfaces(
                deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
            {
                string? devicePath = GetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData);
                if (devicePath != null)
                {
                    // デバイスを開いて VID/PID を確認
                    // ※ここでは属性確認のみなので同期モードで開く
                    using var handle = CreateFile(
                        devicePath,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        0, // 同期モード（属性確認用の一時ハンドル）
                        IntPtr.Zero);

                    if (!handle.IsInvalid)
                    {
                        var attrs = new HIDD_ATTRIBUTES();
                        attrs.Size = Marshal.SizeOf(attrs);

                        if (HidD_GetAttributes(handle, ref attrs))
                        {
                            if (attrs.VendorID == DUALSENSE_VENDOR_ID &&
                                (attrs.ProductID == DUALSENSE_PRODUCT_ID || attrs.ProductID == DUALSENSE_EDGE_PRODUCT_ID))
                            {
                                return devicePath;
                            }
                        }
                    }
                }

                index++;
            }

            return null;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    /// <summary>
    /// DualSense のデバイスパスに対してファイルハンドルを開く。
    /// FILE_FLAG_OVERLAPPED を指定し、非同期 ReadFile を可能にする。
    /// </summary>
    public static SafeFileHandle? OpenDevice(string devicePath)
    {
        var handle = CreateFile(
            devicePath,
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED, // 非同期モード（ブロッキング防止）
            IntPtr.Zero);

        return handle.IsInvalid ? null : handle;
    }

    /// <summary>
    /// HID デバイスの入力レポート長を取得する。
    /// </summary>
    public static ushort GetInputReportLength(SafeFileHandle handle)
    {
        if (HidD_GetPreparsedData(handle, out IntPtr preparsedData))
        {
            try
            {
                int status = HidP_GetCaps(preparsedData, out HIDP_CAPS caps);
                if (status == 0x110000) // HIDP_STATUS_SUCCESS
                {
                    return caps.InputReportByteLength;
                }
            }
            finally
            {
                HidD_FreePreparsedData(preparsedData);
            }
        }

        // フォールバック: DualSense USB は通常 64 バイト
        return 64;
    }

    // =========================================================================
    // 内部ヘルパー
    // =========================================================================

    /// <summary>
    /// SetupDiGetDeviceInterfaceDetail を呼び出してデバイスパス文字列を取得する。
    /// SP_DEVICE_INTERFACE_DETAIL_DATA のマーシャリングは手動で行う
    /// （32bit/64bit でアラインメントが異なるため）。
    /// </summary>
    private static string? GetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA interfaceData)
    {
        int requiredSize = 0;

        // 1回目の呼び出し: 必要なバッファサイズを取得
        SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            ref requiredSize,
            IntPtr.Zero);

        if (requiredSize <= 0)
            return null;

        // 2回目の呼び出し: 実際のデータを取得
        IntPtr detailDataBuffer = Marshal.AllocHGlobal(requiredSize);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize を設定
            // 64bit: 8, 32bit: 6 (構造体のアラインメントに依存)
            int cbSize = IntPtr.Size == 8 ? 8 : 6;
            Marshal.WriteInt32(detailDataBuffer, cbSize);

            if (SetupDiGetDeviceInterfaceDetail(
                deviceInfoSet,
                ref interfaceData,
                detailDataBuffer,
                requiredSize,
                ref requiredSize,
                IntPtr.Zero))
            {
                // DevicePath は cbSize(4バイト) の直後から始まる
                return Marshal.PtrToStringAuto(detailDataBuffer + 4);
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(detailDataBuffer);
        }
    }
}
