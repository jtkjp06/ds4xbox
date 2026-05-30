// =============================================================================
// Core/DriverInstaller.cs
// 必要な外部ドライバ（ViGEmBus / HidHide / Legacinator）を公式 GitHub Releases
// から安全に自動ダウンロードし、PCへのインストールを代行するユーティリティ。
// 100% C# 自前実装（ゼロパッケージ依存）。
// =============================================================================

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace DS4Xbox.Core;

/// <summary>
/// 外部ドライバの自動ダウンロードとインストールを担当するクラス。
/// </summary>
public static class DriverInstaller
{
    // 公式の安全性が確認されている直接ダウンロードURL
    private const string ViGEmBusUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_Setup.msi";
    private const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/download/v1.4.186.0/HidHideMSI.msi";
    private const string LegacinatorUrl = "https://github.com/nefarius/Legacinator/releases/download/v1.2.0/Legacinator.exe";

    /// <summary>
    /// ViGEmBus をダウンロードしてインストールする。
    /// </summary>
    public static async Task<bool> InstallViGEmBusAsync(Action<int, string> onProgress)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "ViGEmBus_Setup.msi");
        onProgress(0, "ViGEmBus ドライバを公式リポジトリからダウンロード中...");
        
        bool success = await DownloadFileAsync(ViGEmBusUrl, tempFile, (progress) => 
        {
            onProgress(progress, $"ViGEmBus をダウンロード中... ({progress}%)");
        });

        if (!success)
        {
            onProgress(0, "ViGEmBus のダウンロードに失敗しました。");
            return false;
        }

        onProgress(100, "インストーラーを起動しています。画面の指示に従って完了させてください...");
        return RunInstaller(tempFile, true);
    }

    /// <summary>
    /// HidHide をダウンロードしてインストールする。
    /// </summary>
    public static async Task<bool> InstallHidHideAsync(Action<int, string> onProgress)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "HidHideMSI.msi");
        onProgress(0, "HidHide ドライバを公式リポジトリからダウンロード中...");

        bool success = await DownloadFileAsync(HidHideUrl, tempFile, (progress) =>
        {
            onProgress(progress, $"HidHide をダウンロード中... ({progress}%)");
        });

        if (!success)
        {
            onProgress(0, "HidHide のダウンロードに失敗しました。");
            return false;
        }

        onProgress(100, "インストーラーを起動しています。画面の指示に従って完了させてください...");
        return RunInstaller(tempFile, true);
    }

    /// <summary>
    /// Legacinator をダウンロードして実行する。
    /// </summary>
    public static async Task<bool> RunLegacinatorAsync(Action<int, string> onProgress)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "Legacinator.exe");
        onProgress(0, "Legacinator を公式リポジトリからダウンロード中...");

        bool success = await DownloadFileAsync(LegacinatorUrl, tempFile, (progress) =>
        {
            onProgress(progress, $"Legacinator をダウンロード中... ({progress}%)");
        });

        if (!success)
        {
            onProgress(0, "Legacinator のダウンロードに失敗しました。");
            return false;
        }

        onProgress(100, "Legacinator を実行しています...");
        return RunInstaller(tempFile, false);
    }

    /// <summary>
    /// 指定されたURLからファイルを非同期でダウンロードし、進捗を報告する。
    /// </summary>
    private static async Task<bool> DownloadFileAsync(string url, string destinationPath, Action<int> onProgress)
    {
        try
        {
            // 既存ファイルがあれば削除
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            using var client = new HttpClient();
            // TLS1.2 / TLS1.3 を有効化
            System.Net.ServicePointManager.SecurityProtocol = 
                System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls13;

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var totalReadBytes = 0L;
            var readCount = 0;

            while ((readCount = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
            {
                await fileStream.WriteAsync(buffer, 0, readCount);
                totalReadBytes += readCount;

                if (totalBytes != -1)
                {
                    int progress = (int)((double)totalReadBytes / totalBytes * 100);
                    onProgress(progress);
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// ダウンロードされたインストーラーを実行する。
    /// </summary>
    private static bool RunInstaller(string path, bool waitForExit)
    {
        try
        {
            var isMsi = Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase);

            var startInfo = new ProcessStartInfo
            {
                FileName = isMsi ? "msiexec.exe" : path,
                Arguments = isMsi ? $"/i \"{path}\"" : "",
                UseShellExecute = true,
                Verb = "runas" // 管理者権限で起動（UACプロンプト表示）
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            if (waitForExit)
            {
                process.WaitForExit();
                // 0 = 正常終了, 3010 = 再起動が必要な正常終了
                return process.ExitCode == 0 || process.ExitCode == 3010;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
