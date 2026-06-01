using Xunit;
using DS4Xbox.Core;
using DS4Xbox.Native;

namespace DS4Xbox.Tests;

public class InputMapperTests
{
    [Fact]
    public void TestFaceButtonsMapping()
    {
        var state = new DualSenseState
        {
            Cross = true,
            Circle = false,
            Triangle = true,
            Square = false,
            DPad = 8 // Released (D-Pad off)
        };

        var report = InputMapper.Map(state, 1);

        Assert.Equal(1u, report.SerialNo);
        // Cross -> Xbox A (0x1000)
        // Triangle -> Xbox Y (0x8000)
        // Expected buttons mask: 0x1000 | 0x8000 = 0x9000
        Assert.Equal((ushort)0x9000, report.wButtons);
    }

    [Fact]
    public void TestDPadMapping()
    {
        // Test UP (0: N)
        var state = new DualSenseState { DPad = 0 };
        var report = InputMapper.Map(state, 1);
        Assert.Equal((ushort)ViGEmInterop.XboxButton.DpadUp, report.wButtons);

        // Test DOWN + LEFT (5: SW)
        state = new DualSenseState { DPad = 5 };
        report = InputMapper.Map(state, 1);
        ushort expected = (ushort)(ViGEmInterop.XboxButton.DpadDown | ViGEmInterop.XboxButton.DpadLeft);
        Assert.Equal(expected, report.wButtons);

        // Test Released (8)
        state = new DualSenseState { DPad = 8 };
        report = InputMapper.Map(state, 1);
        Assert.Equal((ushort)0, report.wButtons);
    }

    [Fact]
    public void TestStickAxisMapping()
    {
        // Neutral sticks (center = 128)
        var state = new DualSenseState
        {
            LeftStickX = 128,
            LeftStickY = 128,
            RightStickX = 128,
            RightStickY = 128,
            DPad = 8
        };

        var report = InputMapper.Map(state, 1);
        Assert.Equal((short)0, report.sThumbLX);
        Assert.Equal((short)0, report.sThumbLY);
        Assert.Equal((short)0, report.sThumbRX);
        Assert.Equal((short)0, report.sThumbRY);

        // Max Right (255)
        state.LeftStickX = 255;
        report = InputMapper.Map(state, 1);
        Assert.Equal((short)32639, report.sThumbLX); // (255 - 128) * 257 = 127 * 257 = 32639

        // Max Down (255) -> Inverted Y-axis -> Expected: -32639
        state.LeftStickY = 255;
        report = InputMapper.Map(state, 1);
        Assert.Equal((short)-32639, report.sThumbLY);

        // Max Up (0) -> Inverted Y-axis -> Expected: 32767 (clamped from 128 * 257 = 32896)
        state.LeftStickY = 0;
        report = InputMapper.Map(state, 1);
        Assert.Equal((short)32767, report.sThumbLY);
    }

    [Fact]
    public void TestTriggerMapping()
    {
        var state = new DualSenseState
        {
            L2Trigger = 100,
            R2Trigger = 200,
            DPad = 8
        };

        var report = InputMapper.Map(state, 1);
        Assert.Equal((byte)100, report.bLeftTrigger);
        Assert.Equal((byte)200, report.bRightTrigger);
    }
}
