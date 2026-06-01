// =============================================================================
// Core/InputMapper.cs
// DualSense の入力状態を Xbox 360 の XInput レポートにマッピングするクラス。
// 純粋な変換関数であり、副作用なし・テスト容易。
// =============================================================================

using DS4Xbox.Native;
using static DS4Xbox.Native.ViGEmInterop;

namespace DS4Xbox.Core;

/// <summary>
/// DualSense の入力状態を Xbox 360 XInput レポートに変換する純粋関数クラス。
/// </summary>
internal static class InputMapper
{
    // =========================================================================
    // Hat Switch (D-Pad) → Xbox D-Pad ビット変換テーブル
    // =========================================================================
    // DualSense の D-Pad は 4bit の Hat Switch 値 (0-8) で表現される。
    // Xbox の D-Pad は個別のビットフラグで表現される。
    // このテーブルで一括変換する。

    private static readonly XboxButton[] DPadMap = new XboxButton[]
    {
        XboxButton.DpadUp,                                          // 0: N (上)
        XboxButton.DpadUp | XboxButton.DpadRight,                   // 1: NE (右上)
        XboxButton.DpadRight,                                       // 2: E (右)
        XboxButton.DpadDown | XboxButton.DpadRight,                 // 3: SE (右下)
        XboxButton.DpadDown,                                        // 4: S (下)
        XboxButton.DpadDown | XboxButton.DpadLeft,                  // 5: SW (左下)
        XboxButton.DpadLeft,                                        // 6: W (左)
        XboxButton.DpadUp | XboxButton.DpadLeft,                    // 7: NW (左上)
        0,                                                          // 8: Released (なし)
    };

    // XboxButton 型を ushort にキャスト
    private static readonly ushort[] DPadMapUshort;

    static InputMapper()
    {
        DPadMapUshort = new ushort[DPadMap.Length];
        for (int i = 0; i < DPadMap.Length; i++)
            DPadMapUshort[i] = (ushort)DPadMap[i];
    }

    /// <summary>
    /// DualSense の入力状態を Xbox 360 入力レポートに変換する。
    /// </summary>
    /// <param name="ds">DualSense の入力状態</param>
    /// <param name="serialNo">ViGEmBus ターゲットのシリアル番号</param>
    /// <returns>ViGEmBus に送信する Xbox 360 入力レポート</returns>
    public static ViGEmInterop.XUSB_SUBMIT_REPORT Map(in DualSenseState ds, uint serialNo)
    {
        var report = new ViGEmInterop.XUSB_SUBMIT_REPORT();
        report.SerialNo = serialNo;

        // -----------------------------------------------------------------
        // ボタンマッピング
        // -----------------------------------------------------------------
        ushort buttons = 0;

        // フェイスボタン: ✕→A, ◯→B, △→Y, □→X
        if (ds.Cross) buttons |= (ushort)XboxButton.A;
        if (ds.Circle) buttons |= (ushort)XboxButton.B;
        if (ds.Triangle) buttons |= (ushort)XboxButton.Y;
        if (ds.Square) buttons |= (ushort)XboxButton.X;

        // ショルダー: L1→LB, R1→RB
        if (ds.L1) buttons |= (ushort)XboxButton.LeftShoulder;
        if (ds.R1) buttons |= (ushort)XboxButton.RightShoulder;

        // サムスティック押し込み: L3→LeftThumb, R3→RightThumb
        if (ds.L3) buttons |= (ushort)XboxButton.LeftThumb;
        if (ds.R3) buttons |= (ushort)XboxButton.RightThumb;

        // メニュー: Options→Start, Create→Back, PS→Guide, TouchpadClick→Guide
        if (ds.Options) buttons |= (ushort)XboxButton.Start;
        if (ds.Create) buttons |= (ushort)XboxButton.Back;
        if (ds.PSButton || ds.TouchpadClick) buttons |= (ushort)XboxButton.Guide;

        // D-Pad: Hat Switch → 個別ビット変換
        if (ds.DPad < DPadMapUshort.Length)
            buttons |= DPadMapUshort[ds.DPad];

        report.wButtons = buttons;

        // -----------------------------------------------------------------
        // トリガー (0-255 → 0-255: 変換不要)
        // -----------------------------------------------------------------
        report.bLeftTrigger = ds.L2Trigger;
        report.bRightTrigger = ds.R2Trigger;

        // -----------------------------------------------------------------
        // スティック軸変換
        // DualSense:  unsigned byte (0-255, center=128)
        // Xbox:       signed short (-32768 ~ 32767)
        //
        // 変換式: xbox = clamp((ds - 128) * 257, -32768, 32767)
        //
        // Y軸の反転:
        //   DualSense: 下が正 (255)
        //   Xbox:      上が正 (32767)
        //   → Y軸のみ符号を反転
        // -----------------------------------------------------------------
        report.sThumbLX = ConvertStickAxis(ds.LeftStickX, false);
        report.sThumbLY = ConvertStickAxis(ds.LeftStickY, true);  // Y軸反転
        report.sThumbRX = ConvertStickAxis(ds.RightStickX, false);
        report.sThumbRY = ConvertStickAxis(ds.RightStickY, true); // Y軸反転

        return report;
    }

    /// <summary>
    /// スティック軸の値を変換する。
    /// DualSense (0-255, unsigned) → Xbox (-32768 ~ 32767, signed)
    /// </summary>
    /// <param name="value">DualSense のスティック値 (0-255)</param>
    /// <param name="invertY">true の場合、Y軸を反転する</param>
    /// <returns>Xbox のスティック値 (-32768 ~ 32767)</returns>
    private static short ConvertStickAxis(byte value, bool invertY)
    {
        // 中央(128)を0として、-128 ~ 127 の範囲にシフト
        int centered = value - 128;

        // -32768 ~ 32767 にスケーリング
        // 257 は 32767 / 127.5 ≈ 256.996... の近似値
        int scaled = centered * 257;

        // Y軸反転
        if (invertY)
            scaled = -scaled;

        // クランプ (オーバーフロー防止)
        scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);

        return (short)scaled;
    }
}
