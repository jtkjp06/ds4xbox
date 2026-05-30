// =============================================================================
// Core/HidHideController.cs
// HidHide ドライバの CLI ツールを呼び出して、デバイスの隠蔽（排他制御）を
// ON/OFF するクラス。
//
// HidHideCLI.exe はドライバのインストール時に同梱される公式ツール。
// このクラスは Process.Start() で外部プロセスとして呼び出すだけであり、
// HidHide のソースコードを流用していない。
// =============================================================================

using System.Diagnostics;

namespace DS4Xbox.Core;

/// <summary>
/// HidHide の Cloak (デバイス隠蔽) を制御するクラス。
/// HidHideCLI.exe を通じて、DualSense を他のアプリケーションから
/// 見えなくしたり、見えるようにしたりする。
/// </summary>
public sealed class HidHideController
{
    /// <summary>
    /// HidHideCLI.exe のデフォルトインストールパス。
    /// </summary>
    private static readonly string DefaultCliPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Nefarius Software Solutions",
        "HidHide",
        "x64",
        "HidHideCLI.exe");

    private readonly string _cliPath;

    /// <summary>
    /// HidHide が利用可能かどうか。
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>
    /// 現在 Cloak (隠蔽) が有効かどうか。
    /// </summary>
    public bool IsCloakActive { get; private set; }

    public HidHideController()
    {
        _cliPath = DefaultCliPath;
        IsAvailable = File.Exists(_cliPath);
    }

    /// <summary>
    /// Cloak を有効にする（DualSense を他のアプリから隠す）。
    /// </summary>
    /// <returns>成功時 true</returns>
    public bool EnableCloak()
    {
        if (!IsAvailable) return false;

        bool success = RunCli("--cloak-on");
        if (success)
            IsCloakActive = true;

        return success;
    }

    /// <summary>
    /// Cloak を無効にする（DualSense を他のアプリに見えるようにする）。
    /// </summary>
    /// <returns>成功時 true</returns>
    public bool DisableCloak()
    {
        if (!IsAvailable) return false;

        bool success = RunCli("--cloak-off");
        if (success)
            IsCloakActive = false;

        return success;
    }

    /// <summary>
    /// 自アプリケーションの実行パスを HidHide のホワイトリストに登録する。
    /// ホワイトリストに登録されたアプリケーションは、Cloak が有効でも
    /// DualSense を読み取ることができる。
    /// </summary>
    /// <returns>成功時 true</returns>
    public bool RegisterWhitelist()
    {
        if (!IsAvailable) return false;

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        return RunCli($"--app-reg \"{exePath}\"");
    }

    /// <summary>
    /// 自アプリケーションをホワイトリストから解除する。
    /// </summary>
    /// <returns>成功時 true</returns>
    public bool UnregisterWhitelist()
    {
        if (!IsAvailable) return false;

        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        return RunCli($"--app-unreg \"{exePath}\"");
    }

    /// <summary>
    /// HidHideCLI.exe を指定した引数で実行する。
    /// </summary>
    private bool RunCli(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000); // 最大 5 秒待つ
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
