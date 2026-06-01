using DS4Xbox.Native;
using System.Runtime.InteropServices;
using System.Text;

namespace DS4Xbox.Core;

internal static class DiagnosticRunner
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    public static int Run(bool showDialog)
    {
        EnsureConsole();

        var lines = new List<string>();
        void Step(string message)
        {
            lines.Add(message);
            Console.WriteLine(message);
            AppLog.Info($"[diagnose] {message}");
        }

        Step("DS4Xbox diagnostic started.");

        using var lowLevelBusHandle = ViGEmInterop.OpenBus();
        if (lowLevelBusHandle == null || lowLevelBusHandle.IsInvalid)
        {
            Step("FAIL: ViGEmBus could not be opened.");
            Finish(lines, showDialog);
            return 1;
        }
        Step("OK: ViGEmBus opened.");

        using var virtualController = new VirtualXboxController();
        try
        {
            virtualController.Connect();
            Step($"OK: Virtual Xbox 360 controller created. UserIndex={virtualController.UserIndex}");
        }
        catch (Exception ex)
        {
            Step($"FAIL: Virtual Xbox 360 controller could not be created. {ex.GetType().Name}: {ex.Message}");
            Finish(lines, showDialog);
            return 1;
        }

        string? devicePath = HidInterop.FindDualSenseDevicePath();
        if (devicePath == null)
        {
            Step("FAIL: DualSense / DualSense Edge HID device was not found.");
            Finish(lines, showDialog);
            return 1;
        }
        Step("OK: DualSense HID device found.");

        using var reader = new DualSenseReader();
        if (!reader.Connect())
        {
            Step("FAIL: DualSense HID device could not be opened.");
            Finish(lines, showDialog);
            return 1;
        }
        Step("OK: DualSense HID device opened.");

        bool readOk = false;
        DualSenseState state = default;
        for (int i = 0; i < 20; i++)
        {
            if (reader.ReadState(out state))
            {
                readOk = true;
                break;
            }
            Thread.Sleep(50);
        }

        if (!readOk)
        {
            Step("FAIL: DualSense input report was not read within the diagnostic window.");
            Finish(lines, showDialog);
            return 1;
        }
        Step($"OK: DualSense input read. LX={state.LeftStickX}, LY={state.LeftStickY}, DPad={state.DPad}");

        try
        {
            virtualController.Submit(in state);
            Step("OK: ViGEmClient SubmitReport succeeded.");
            Finish(lines, showDialog);
            return 0;
        }
        catch (Exception ex)
        {
            Step($"FAIL: ViGEmClient SubmitReport failed. {ex.GetType().Name}: {ex.Message}");

            uint serialNo = ViGEmInterop.PluginTarget(lowLevelBusHandle);
            if (serialNo == 0)
            {
                Step("FAIL: Low-level ViGEmBus fallback controller could not be created.");
            }
            else
            {
                try
                {
                    var report = InputMapper.Map(in state, serialNo);
                    if (!ViGEmInterop.SubmitReport(lowLevelBusHandle, report))
                    {
                        int error = Marshal.GetLastWin32Error();
                        Step($"FAIL: Low-level ViGEmBus SubmitReport failed. Win32Error={error}");
                        foreach (var probe in ViGEmInterop.ProbeSubmitReportIoctls(lowLevelBusHandle, report))
                        {
                            Step(probe.Success
                                ? $"OK: Low-level SubmitReport IOCTL candidate succeeded: {probe.Name}"
                                : $"FAIL: Low-level SubmitReport IOCTL candidate failed: {probe.Name}, Win32Error={probe.LastWin32Error}");
                        }
                    }
                    else
                    {
                        Step("OK: Low-level ViGEmBus SubmitReport succeeded.");
                    }
                }
                finally
                {
                    ViGEmInterop.UnplugTarget(lowLevelBusHandle, serialNo);
                }
            }

            Finish(lines, showDialog);
            return 1;
        }
    }

    private static void Finish(IReadOnlyCollection<string> lines, bool showDialog)
    {
        string summary = string.Join(Environment.NewLine, lines);
        string resultPath = Path.Combine(AppContext.BaseDirectory, "diagnostic_result.txt");

        try
        {
            File.WriteAllText(
                resultPath,
                summary + Environment.NewLine + Environment.NewLine + $"Log: {AppLog.LogPath}" + Environment.NewLine,
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to write diagnostic result file.", ex);
        }

        Console.WriteLine();
        Console.WriteLine($"Result: {resultPath}");
        Console.WriteLine($"Log: {AppLog.LogPath}");
        if (showDialog)
        {
            MessageBox.Show(summary, "DS4Xbox Diagnostic", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static void EnsureConsole()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
        {
            AllocConsole();
        }

        try
        {
            var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
            var error = new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(output);
            Console.SetError(error);
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // 診断自体を優先する。コンソール再接続に失敗してもログには残る。
        }
    }
}
