// =============================================================================
// UI/TrayApplication.cs
// タスクトレイ（通知領域）に常駐するアプリケーション UI。
// System.Windows.Forms.NotifyIcon を使用してタスクトレイ常駐 UI を提供する。
//
// 右クリックメニュー:
//   ✅ 変換 ON / ⬜ 変換 OFF
//   ⚙ Windows起動時に自動ON
//   ─────────────────
//   ❌ 終了
//
// ON/OFF 切り替え時に HidHide の Cloak も連動して制御する。
// =============================================================================

using System.Drawing;
using System.Diagnostics;
using DS4Xbox.Core;
using DS4Xbox.Native;

namespace DS4Xbox.UI;

/// <summary>
/// タスクトレイ常駐アプリケーション。
/// WinForms の ApplicationContext を継承し、メッセージループを管理する。
/// </summary>
public sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _toggleMenuItem;
    private readonly ToolStripMenuItem _autoStartMenuItem;
    private readonly HidHideController _hidHide;
    private readonly AppSettings _settings;

    // 変換エンジンの状態
    private bool _isActive;
    private bool _isStarting;
    private CancellationTokenSource? _pollingCts;
    private Thread? _pollingThread;

    // 仮想 Xbox 360 コントローラー
    private VirtualXboxController? _virtualController;

    // アイコン関連のリソース管理（GDI/HICON リーク防止）
    private readonly IntPtr _onHicon;
    private readonly IntPtr _offHicon;
    private readonly Icon _onIcon;
    private readonly Icon _offIcon;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public TrayApplication(AppSettings settings)
    {
        _settings = settings;
        _hidHide = new HidHideController();

        // アイコンの事前生成
        _onHicon = LoadTrayIconFromResource("DS4Xbox.UI.Resources.controller_logo.png", out _onIcon);
        _offHicon = LoadTrayIconFromResource("DS4Xbox.UI.Resources.controller_logo_off.png", out _offIcon);

        // --- コンテキストメニューの構築 ---
        _toggleMenuItem = new ToolStripMenuItem("変換 ON")
        {
            Checked = false,
        };
        _toggleMenuItem.Click += OnToggleClicked;

        _autoStartMenuItem = new ToolStripMenuItem("Windows起動時に自動ON")
        {
            Checked = settings.StartEnabled,
            CheckOnClick = true,
        };
        _autoStartMenuItem.Click += OnAutoStartClicked;

        var installDriversMenuItem = new ToolStripMenuItem("ドライバ自動セットアップ...");
        installDriversMenuItem.Click += async (s, e) => await CheckAndInstallDriversAsync();

        var uninstallMenuItem = new ToolStripMenuItem("アンインストール手順...");
        uninstallMenuItem.Click += OnUninstallClicked;

        var exitMenuItem = new ToolStripMenuItem("終了");
        exitMenuItem.Click += OnExitClicked;

        var versionLabel = new ToolStripMenuItem("DS4Xbox v1.0.0")
        {
            Enabled = false
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(versionLabel);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_toggleMenuItem);
        contextMenu.Items.Add(_autoStartMenuItem);
        contextMenu.Items.Add(installDriversMenuItem);
        contextMenu.Items.Add(uninstallMenuItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitMenuItem);

        // --- タスクトレイアイコンの作成 ---
        _trayIcon = new NotifyIcon
        {
            Icon = _offIcon,
            Text = "DS4Xbox - OFF",
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        // ダブルクリックでもON/OFF切り替え
        _trayIcon.DoubleClick += OnToggleClicked;

        // 異常終了時にも Cloak を確実に解除するためのフック
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // HidHide が利用可能な場合、ホワイトリストに自身を登録
        if (_hidHide.IsAvailable)
        {
            _hidHide.RegisterWhitelist();
        }

        AppLog.Info("Tray application started.");

        if (settings.StartEnabled)
        {
            ConfigureWindowsStartup(true);
            StartConversion();
        }
    }

    // =========================================================================
    // 変換の開始・停止
    // =========================================================================

    /// <summary>
    /// DualSense → Xbox 360 変換を開始する。
    /// </summary>
    private async void StartConversion()
    {
        if (_isActive || _isStarting) return;
        _isStarting = true;
        AppLog.Info("Starting conversion.");

        try
        {
            // ドライバの自動チェックとインストール
            bool driversAvailable = await CheckAndInstallDriversAsync();
            if (!driversAvailable)
            {
                FailStart("Driver check was cancelled or failed.", "必要なドライバを確認できなかったため、変換を開始できませんでした。");
                return;
            }

            _virtualController = new VirtualXboxController();
            try
            {
                _virtualController.Connect();
            }
            catch (Exception ex)
            {
                _virtualController.Dispose();
                _virtualController = null;
                AppLog.Error("Virtual Xbox 360 controller creation failed.", ex);
                FailStart("Virtual Xbox 360 controller creation failed.", "仮想 Xbox 360 コントローラーの作成に失敗しました。ViGEmBus の状態を確認してください。");
                return;
            }
            AppLog.Info($"Virtual Xbox 360 controller created. UserIndex={_virtualController.UserIndex}");

            // HidHide で DualSense を隠蔽
            if (_hidHide.IsAvailable)
            {
                string? devicePath = HidInterop.FindDualSenseDevicePath();
                if (devicePath != null)
                {
                    // \\?\hid#vid_054c&pid_0ce6#8&2be624b1&0&0000#{guid} -> HID\VID_054C&PID_0CE6\8&2BE624B1&0&0000
                    string[] parts = devicePath.Split('#');
                    if (parts.Length >= 3)
                    {
                        string instancePath = parts[0].Replace("\\\\?\\", "") + "\\" + parts[1] + "\\" + parts[2];
                        instancePath = instancePath.ToUpperInvariant();
                        if (!_hidHide.HideDeviceInstance(instancePath))
                        {
                            AppLog.Info($"HidHide device registration failed or was already configured: {instancePath}");
                        }
                    }
                }
                else
                {
                    AppLog.Info("DualSense device path was not found before enabling HidHide cloak.");
                }

                if (!_hidHide.EnableCloak())
                {
                    AppLog.Info("HidHide cloak could not be enabled.");
                }
            }

            // ポーリングスレッドを開始
            _pollingCts = new CancellationTokenSource();
            _pollingThread = new Thread(() => PollingLoop(_pollingCts.Token))
            {
                IsBackground = true,
                Name = "DS4Xbox-Polling",
                Priority = ThreadPriority.AboveNormal,
            };
            _pollingThread.Start();

            _isActive = true;
            UpdateTrayState(true);
            ShowBalloon("DS4Xbox", "変換を開始しました (PS Controller -> Xbox 360)", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            AppLog.Error("Conversion start failed unexpectedly.", ex);
            FailStart("Conversion start failed unexpectedly.", "変換の開始中にエラーが発生しました。ds4xbox.log を確認してください。");
        }
        finally
        {
            _isStarting = false;
        }
    }

    /// <summary>
    /// DualSense → Xbox 360 変換を停止する。
    /// </summary>
    private void StopConversion()
    {
        if (!_isActive) return;

        // ポーリングスレッドを停止
        _pollingCts?.Cancel();
        _pollingThread?.Join(2000); // 最大 2 秒待つ
        _pollingCts?.Dispose();
        _pollingCts = null;
        _pollingThread = null;

        // HidHide の Cloak を解除
        if (_hidHide.IsAvailable && _hidHide.IsCloakActive)
        {
            _hidHide.DisableCloak();
        }

        _virtualController?.Dispose();
        _virtualController = null;

        _isActive = false;
        UpdateTrayState(false);
        AppLog.Info("Conversion stopped.");
        ShowBalloon("DS4Xbox", "変換を停止しました", ToolTipIcon.Info);
    }

    private void FailStart(string logMessage, string userMessage)
    {
        AppLog.Error(logMessage);
        UpdateTrayState(false);
        ShowBalloon("DS4Xbox エラー", $"{userMessage}\nログ: {AppLog.LogPath}", ToolTipIcon.Error);
    }

    // =========================================================================
    // ポーリングループ（バックグラウンドスレッド）
    // =========================================================================

    /// <summary>
    /// DualSense の入力を読み取り、Xbox 360 レポートに変換して送信する
    /// メインのポーリングループ。専用スレッドで実行される。
    /// </summary>
    private void PollingLoop(CancellationToken ct)
    {
        using var reader = new DualSenseReader();
        bool wasConnected = false;
        int consecutiveSubmitFailures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // コントローラー未接続時: 接続を試みるリトライループ
                if (!reader.IsConnected)
                {
                    if (wasConnected)
                    {
                        AppLog.Info("DualSense disconnected.");
                        UpdateTrayText("DS4Xbox - コントローラー切断");
                        wasConnected = false;
                    }

                    if (reader.Connect())
                    {
                        AppLog.Info("DualSense connected.");
                        wasConnected = true;
                        UpdateTrayText("DS4Xbox - ON (接続中)");
                    }
                    else
                    {
                        // 1 秒待ってリトライ
                        ct.WaitHandle.WaitOne(1000);
                        continue;
                    }
                }

                // 入力を読み取り
                if (reader.ReadState(out DualSenseState dsState))
                {
                    // マッピング & 送信
                    if (_virtualController != null)
                    {
                        try
                        {
                            _virtualController.Submit(in dsState);
                            consecutiveSubmitFailures = 0;
                        }
                        catch (Exception ex)
                        {
                            consecutiveSubmitFailures++;
                            if (consecutiveSubmitFailures == 1 || consecutiveSubmitFailures % 100 == 0)
                            {
                                AppLog.Error($"ViGEmClient SubmitReport failed. ConsecutiveFailures={consecutiveSubmitFailures}", ex);
                            }

                            if (consecutiveSubmitFailures >= 10)
                            {
                                AppLog.Error("Stopping conversion because ViGEmClient SubmitReport keeps failing.", ex);
                                NotifyConversionFailureFromWorker(
                                    "ViGEmBus への入力送信に失敗しました。\n" +
                                    "diagnose_gamepad.bat で詳細を確認してください。");
                                return;
                            }
                        }
                    }
                }
                else
                {
                    // ReadState が false: タイムアウト or 切断
                    // タイムアウトの場合はそのままループ継続
                    // 切断の場合は IsConnected が false になるので次のイテレーションで再接続を試みる
                    if (!reader.IsConnected)
                    {
                        reader.Disconnect();
                    }
                }
            }
            catch (Exception)
            {
                // 予期しないエラー: 少し待ってリトライ
                AppLog.Error("Polling loop failed; reconnecting.");
                reader.Disconnect();
                ct.WaitHandle.WaitOne(2000);
            }
        }
    }

    private void NotifyConversionFailureFromWorker(string message)
    {
        try
        {
            _trayIcon.ContextMenuStrip?.BeginInvoke(() =>
            {
                StopConversion();
                ShowBalloon("DS4Xbox エラー", message, ToolTipIcon.Error);
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to marshal conversion failure notification to UI thread.", ex);
        }
    }

    // =========================================================================
    // UI イベントハンドラ
    // =========================================================================

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        if (_isActive)
            StopConversion();
        else
            StartConversion();
    }

    private void OnAutoStartClicked(object? sender, EventArgs e)
    {
        _settings.StartEnabled = _autoStartMenuItem.Checked;
        _settings.Save();
        ConfigureWindowsStartup(_settings.StartEnabled);
    }

    private static void ConfigureWindowsStartup(bool enabled)
    {
        string exePath = Application.ExecutablePath;
        string arguments = enabled
            ? $"/Create /TN \"DS4Xbox\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F"
            : "/Delete /TN \"DS4Xbox\" /F";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            bool exited = process?.WaitForExit(5000) == true;
            if (process == null || !exited || process.ExitCode != 0)
            {
                string error = !exited
                    ? "schtasks.exe timed out."
                    : process?.StandardError.ReadToEnd() ?? "schtasks.exe did not start.";
                AppLog.Error($"Failed to {(enabled ? "create" : "delete")} startup task. {error}");
            }
            else
            {
                AppLog.Info(enabled ? "Windows startup task registered." : "Windows startup task removed.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Windows startup task configuration failed.", ex);
        }
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        StopConversion();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        // 異常終了時にも HidHide の Cloak を確実に解除
        if (_hidHide.IsAvailable && _hidHide.IsCloakActive)
        {
            _hidHide.DisableCloak();
        }

        AppLog.Info("Process exit cleanup finished.");
    }

    /// <summary>
    /// リソースのクリーンアップを行う。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollingCts?.Cancel();
            _pollingThread?.Join(2000);
            _pollingCts?.Dispose();

            _trayIcon.Dispose();
            _onIcon.Dispose();
            _offIcon.Dispose();

            if (_onHicon != IntPtr.Zero)
            {
                DestroyIcon(_onHicon);
            }
            if (_offHicon != IntPtr.Zero)
            {
                DestroyIcon(_offHicon);
            }
        }
        base.Dispose(disposing);
    }

    // =========================================================================
    // UI ヘルパー
    // =========================================================================

    /// <summary>
    /// タスクトレイのアイコンとメニュー表示を更新する。
    /// </summary>
    private void UpdateTrayState(bool active)
    {
        _trayIcon.Icon = active ? _onIcon : _offIcon;
        _trayIcon.Text = active ? "DS4Xbox - ON" : "DS4Xbox - OFF";
        _toggleMenuItem.Text = active ? "変換 OFF にする" : "変換 ON にする";
        _toggleMenuItem.Checked = active;
    }

    /// <summary>
    /// タスクトレイのツールチップテキストを更新する（スレッドセーフ）。
    /// </summary>
    private void UpdateTrayText(string text)
    {
        if (_trayIcon.ContextMenuStrip?.InvokeRequired == true)
        {
            _trayIcon.ContextMenuStrip.BeginInvoke(() => _trayIcon.Text = text);
        }
        else
        {
            _trayIcon.Text = text;
        }
    }

    /// <summary>
    /// バルーン通知を表示する。
    /// </summary>
    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _trayIcon.ShowBalloonTip(3000, title, text, icon);
    }

    /// <summary>
    /// 必要なドライバのインストール状態を確認し、未インストールの場合は
    /// ユーザーの許可を得て公式リポジトリから安全に自動ダウンロード＆インストールを行います。
    /// </summary>
    private async Task<bool> CheckAndInstallDriversAsync()
    {
        // 1. ViGEmBus のチェック
        using (var tempHandle = ViGEmInterop.OpenBus())
        {
            if (tempHandle == null || tempHandle.IsInvalid)
            {
                var result = MessageBox.Show(
                    "DS4Xbox の実行に必要な「ViGEmBus ドライバ」が見つかりません。\n\n" +
                    "【セキュリティ＆ライセンスについて】\n" +
                    "・本機能は公式リポジトリ (nefarius/ViGEmBus) から安全な署名付きバイナリを直接HTTPSダウンロードします。\n" +
                    "・アプリ本体は公式 ViGEm クライアントライブラリを使って仮想 Xbox 360 コントローラーを作成します。\n\n" +
                    "自動的にダウンロードしてインストールを開始しますか？",
                    "ViGEmBus ドライバのインストール",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    var setupForm = new SetupForm("ViGEmBus セットアップ", "準備中...");
                    setupForm.Show();

                    bool installSuccess = await Task.Run(async () =>
                    {
                        return await DriverInstaller.InstallViGEmBusAsync((progress, status) =>
                        {
                            setupForm.UpdateProgress(progress, status);
                        });
                    });

                    setupForm.Close();

                    if (!installSuccess)
                    {
                        MessageBox.Show("ViGEmBus のインストールに失敗したか、キャンセルされました。\n手動でインストールを行うか、再度お試しください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }

                    MessageBox.Show("ViGEmBus のインストールが正常に完了しました！", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    return false;
                }
            }
        }

        // 2. HidHide のチェック
        if (!_hidHide.IsAvailable)
        {
            var result = MessageBox.Show(
                "物理コントローラーをゲームから隠し、二重入力を完全に防止する「HidHide ドライバ」が見つかりません。\n\n" +
                "【セキュリティ＆ライセンスについて】\n" +
                "・公式リポジトリ (nefarius/HidHide) から安全な署名付きバイナリを直接HTTPSダウンロードします。\n" +
                "・安全なクリーンコードにて実行されます。\n\n" +
                "自動的にダウンロードしてインストールを開始しますか？",
                "HidHide ドライバのインストール",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                var setupForm = new SetupForm("HidHide セットアップ", "準備中...");
                setupForm.Show();

                bool installSuccess = await Task.Run(async () =>
                {
                    return await DriverInstaller.InstallHidHideAsync((progress, status) =>
                    {
                        setupForm.UpdateProgress(progress, status);
                    });
                });

                setupForm.Close();

                if (!installSuccess)
                {
                    MessageBox.Show("HidHide のインストールに失敗したか、キャンセルされました。\n再度お試しください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // 3. Legacinator（セキュリティクリーナー）の実行を提案
                var legacinatorResult = MessageBox.Show(
                    "ViGEmBus のインストールに伴い、古いセキュリティリスクのあるアップデーター（失効ドメインへの不正通信の恐れ）をスキャンして削除する公式ツール「Legacinator」を実行しますか？（推奨）",
                    "セキュリティクリーナーの実行",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (legacinatorResult == DialogResult.Yes)
                {
                    var cleanerForm = new SetupForm("Legacinator セットアップ", "準備中...");
                    cleanerForm.Show();

                    await Task.Run(async () =>
                    {
                        await DriverInstaller.RunLegacinatorAsync((progress, status) =>
                        {
                            cleanerForm.UpdateProgress(progress, status);
                        });
                    });

                    cleanerForm.Close();
                }

                MessageBox.Show("HidHide のインストールが完了しました！\nドライバの有効化のために PC を再起動してください。", "完了（要再起動）", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        return true;
    }

    /// <summary>
    /// 埋め込みリソースからPNG画像を読み込み、Win32 HICONおよび Icon オブジェクトを生成する。
    /// </summary>
    private static IntPtr LoadTrayIconFromResource(string resourceName, out Icon icon)
    {
        try
        {
            using var stream = typeof(TrayApplication).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // リソースが見つからない場合のフォールバック（従来の動的描画）
                return CreateFallbackIcon(resourceName.Contains("logo_off"), out icon);
            }

            using var bitmap = new Bitmap(stream);
            
            // 透過つきで HICON に変換
            IntPtr hIcon = bitmap.GetHicon();
            icon = Icon.FromHandle(hIcon);
            return hIcon;
        }
        catch (Exception)
        {
            return CreateFallbackIcon(resourceName.Contains("logo_off"), out icon);
        }
    }

    /// <summary>
    /// リソース読み込み失敗時のための安全なフォールバック用アイコン描画。
    /// </summary>
    private static IntPtr CreateFallbackIcon(bool isOff, out Icon icon)
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color fillColor = !isOff ? Color.FromArgb(0, 200, 83) : Color.FromArgb(128, 128, 128);
            Color borderColor = !isOff ? Color.FromArgb(0, 150, 60) : Color.FromArgb(90, 90, 90);

            using var brush = new SolidBrush(fillColor);
            using var pen = new Pen(borderColor, 1.0f);

            g.FillEllipse(brush, 2, 2, 12, 12);
            g.DrawEllipse(pen, 2, 2, 12, 12);
        }

        IntPtr hIcon = bitmap.GetHicon();
        icon = Icon.FromHandle(hIcon);
        return hIcon;
    }

    /// <summary>
    /// アンインストールがクリックされた際のハンドラ。
    /// アンインストーラーバッチファイルを作成・起動して自身を安全にクローズします。
    /// </summary>
    private void OnUninstallClicked(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "DS4Xbox をアンインストールしますか？\n\n" +
            "【処理内容】\n" +
            "・本アプリケーションの設定や自動起動設定をクリーンアップします。\n" +
            "・仮想コントローラードライバ（ViGEmBus / HidHide）のアンインストール手順をご案内します。\n\n" +
            "よろしければ「はい」を押してください。自動的にアンインストーラーを起動し、アプリを終了します。",
            "DS4Xbox アンインストール",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            try
            {
                string exeDir = AppContext.BaseDirectory;
                string batPath = Path.Combine(exeDir, "uninstall.bat");

                // 最新のアンインストールバッチを書き出す（常に上書き）
                string batContent = GetUninstallBatContent();
                File.WriteAllText(batPath, batContent, System.Text.Encoding.UTF8);

                // 管理者権限でアンインストーラーバッチを起動
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batPath}\"",
                    UseShellExecute = true,
                    Verb = "runas", // UAC昇格
                    WorkingDirectory = exeDir
                };
                System.Diagnostics.Process.Start(startInfo);

                // アプリを正常終了
                OnExitClicked(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"アンインストーラーの起動に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>
    /// アンインストーラーバッチファイルの内容を生成する。
    /// </summary>
    private static string GetUninstallBatContent()
    {
        return @"@echo off
chcp 65001 > nul
title DS4Xbox アンインストーラー
echo ======================================================
echo             DS4Xbox アンインストーラー
echo ======================================================
echo.

:: 管理者権限のチェック
openfiles >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] このスクリプトは管理者権限で実行する必要があります。
    echo 右クリックして「管理者として実行」を選択してください。
    pause
    exit /b 1
)

echo [1/5] DS4Xbox プロセスを終了しています...
taskkill /f /im DS4Xbox.exe >nul 2>&1
timeout /t 1 >nul

echo [2/5] 自動起動設定を解除しています...
reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v ""DS4Xbox"" /f >nul 2>&1
schtasks /Delete /TN ""DS4Xbox"" /F >nul 2>&1

echo [3/5] HidHide のホワイトリストから登録を解除しています...
set ""HIDHIDE_CLI=C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe""
if exist ""%HIDHIDE_CLI%"" (
    ""%HIDHIDE_CLI%"" --app-unreg ""%~dp0DS4Xbox.exe"" >nul 2>&1
    ""%HIDHIDE_CLI%"" --cloak-off >nul 2>&1
    echo [INFO] HidHide の設定をクリーンアップしました。
)

echo [4/5] 設定ファイルを削除しています...
if exist ""appsettings.json"" (
    del ""appsettings.json""
    echo [INFO] appsettings.json を削除しました。
)

echo [5/5] ドライバ類のアンインストール案内...
echo.
echo ======================================================
echo 以下のカーネルドライバを完全にシステムから削除したい場合は、
echo 以下の手順で手動でアンインストールを行ってください：
echo.
echo 1. Windowsの「設定」 ➔ 「アプリ」 ➔ 「インストールされているアプリ」を開きます。
echo 2. 一覧から以下の2つを見つけてアンインストールしてください：
echo    - 「ViGEmBus Driver」
echo    - 「HidHide Driver」
echo.
echo ※ または、公式セキュリティクリーナー「Legacinator」を実行して
echo   残存ファイルを一括で安全にクリーンアップしてください。
echo ======================================================
echo.
echo アンインストール処理の準備が完了しました。
echo このバッチファイルがあるフォルダ内のファイルを削除して作業を完了してください。
echo.
pause
";
    }
}
