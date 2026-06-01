using DS4Xbox.Native;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DS4Xbox.Core;

internal sealed class VirtualXboxController : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _connected;

    public int UserIndex
    {
        get
        {
            if (_controller == null)
            {
                return -1;
            }

            try
            {
                return _controller.UserIndex;
            }
            catch
            {
                return -1;
            }
        }
    }

    public void Connect()
    {
        if (_connected)
        {
            return;
        }

        _client = new ViGEmClient();
        _controller = _client.CreateXbox360Controller();
        _controller.AutoSubmitReport = false;
        _controller.Connect();
        _connected = true;

        SubmitNeutralReport();
    }

    public void Submit(in DualSenseState state)
    {
        EnsureConnected();

        var report = InputMapper.Map(in state, serialNo: 0);
        Apply(report);
        _controller!.SubmitReport();
    }

    public void SubmitNeutralReport()
    {
        EnsureConnected();

        var report = new ViGEmInterop.XUSB_SUBMIT_REPORT
        {
            sThumbLX = 0,
            sThumbLY = 0,
            sThumbRX = 0,
            sThumbRY = 0,
            bLeftTrigger = 0,
            bRightTrigger = 0,
            wButtons = 0,
        };

        Apply(report);
        _controller!.SubmitReport();
    }

    private void Apply(in ViGEmInterop.XUSB_SUBMIT_REPORT report)
    {
        _controller!.SetButtonsFull(report.wButtons);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, report.bLeftTrigger);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, report.bRightTrigger);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, report.sThumbLX);
        _controller.SetAxisValue(Xbox360Axis.LeftThumbY, report.sThumbLY);
        _controller.SetAxisValue(Xbox360Axis.RightThumbX, report.sThumbRX);
        _controller.SetAxisValue(Xbox360Axis.RightThumbY, report.sThumbRY);
    }

    private void EnsureConnected()
    {
        if (!_connected || _controller == null)
        {
            throw new InvalidOperationException("Virtual Xbox 360 controller is not connected.");
        }
    }

    public void Dispose()
    {
        if (_controller != null)
        {
            try
            {
                if (_connected)
                {
                    _controller.Disconnect();
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Virtual Xbox 360 controller disconnect failed.", ex);
            }
        }

        _controller = null;
        _connected = false;

        _client?.Dispose();
        _client = null;
    }
}
