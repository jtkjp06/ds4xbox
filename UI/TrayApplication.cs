// =============================================================================
// UI/TrayApplication.cs
// タスクトレイ（通知領域）に常駐するアプリケーション UI。
// System.Windows.Forms.NotifyIcon を使用（.NET BCL 標準、外部ライブラリ不使用）。
//
// 右クリックメニュー:
//   ✅ 変換 ON / ⬜ 変換 OFF
//   ⚙ 起動時に自動ON
//   ─────────────────
//   ❌ 終了
//
// ON/OFF 切り替え時に HidHide の Cloak も連動して制御する。
// =============================================================================

using System.Drawing;
using DS4Xbox.Core;
using DS4Xbox.Native;
using Microsoft.Win32.SafeHandles;

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
    private CancellationTokenSource? _pollingCts;
    private Thread? _pollingThread;

    // ViGEmBus 関連
    private SafeFileHandle? _busHandle;
    private uint _targetSerialNo;

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
        _onHicon = CreateTrayIconRaw(true, out _onIcon);
        _offHicon = CreateTrayIconRaw(false, out _offIcon);

        // --- コンテキストメニューの構築 ---
        _toggleMenuItem = new ToolStripMenuItem("変換 ON")
        {
            Checked = false,
        };
        _toggleMenuItem.Click += OnToggleClicked;

        _autoStartMenuItem = new ToolStripMenuItem("起動時に自動ON")
        {
            Checked = settings.StartEnabled,
            CheckOnClick = true,
        };
        _autoStartMenuItem.Click += OnAutoStartClicked;

        var installDriversMenuItem = new ToolStripMenuItem("ドライバ自動セットアップ...");
        installDriversMenuItem.Click += async (s, e) => await CheckAndInstallDriversAsync();

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

        // 起動時自動 ON の処理
        if (settings.StartEnabled)
        {
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
        if (_isActive) return;

        // ドライバの自動チェックとインストール
        bool driversAvailable = await CheckAndInstallDriversAsync();
        if (!driversAvailable) return;

        // ViGEmBus に接続
        _busHandle = ViGEmInterop.OpenBus();
        if (_busHandle == null)
        {
            ShowBalloon("エラー", "ViGEmBus ドライバが見つかりません。\nインストール手順は README.md を参照してください。", ToolTipIcon.Error);
            return;
        }

        // 仮想 Xbox 360 コントローラーをプラグイン
        _targetSerialNo = ViGEmInterop.PluginTarget(_busHandle);
        if (_targetSerialNo == 0)
        {
            ShowBalloon("エラー", "仮想コントローラーの作成に失敗しました。", ToolTipIcon.Error);
            _busHandle.Dispose();
            _busHandle = null;
            return;
        }

        // HidHide で DualSense を隠蔽
        if (_hidHide.IsAvailable)
        {
            _hidHide.EnableCloak();
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
        ShowBalloon("DS4Xbox", "変換を開始しました (DualSense → Xbox 360)", ToolTipIcon.Info);
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

        // 仮想コントローラーをアンプラグ
        if (_busHandle != null && _targetSerialNo != 0)
        {
            ViGEmInterop.UnplugTarget(_busHandle, _targetSerialNo);
            _targetSerialNo = 0;
        }

        _busHandle?.Dispose();
        _busHandle = null;

        _isActive = false;
        UpdateTrayState(false);
        ShowBalloon("DS4Xbox", "変換を停止しました", ToolTipIcon.Info);
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

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // コントローラー未接続時: 接続を試みるリトライループ
                if (!reader.IsConnected)
                {
                    if (wasConnected)
                    {
                        // 接続が切れた
                        UpdateTrayText("DS4Xbox - コントローラー切断");
                        wasConnected = false;
                    }

                    if (reader.Connect())
                    {
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
                    if (_busHandle != null && _targetSerialNo != 0)
                    {
                        var report = InputMapper.Map(in dsState, _targetSerialNo);
                        ViGEmInterop.SubmitReport(_busHandle, report);
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
                reader.Disconnect();
                ct.WaitHandle.WaitOne(2000);
            }
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

        // ホワイトリストからも解除
        if (_hidHide.IsAvailable)
        {
            _hidHide.UnregisterWhitelist();
        }
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
                    "・一切のサードパーティ外部ライブラリ（NuGet等）を含まない安全な自製コードによって処理されます。\n\n" +
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
                "物理DualSenseコントローラーをゲームから隠し、二重入力を完全に防止する「HidHide ドライバ」が見つかりません。\n\n" +
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
    /// タスクトレイ用のアイコンをコード内で動的に生成する。
    /// ON: 緑の丸、OFF: グレーの丸。
    /// </summary>
    private static IntPtr CreateTrayIconRaw(bool active, out Icon icon)
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            Color fillColor = active ? Color.FromArgb(0, 200, 83) : Color.FromArgb(128, 128, 128);
            Color borderColor = active ? Color.FromArgb(0, 150, 60) : Color.FromArgb(90, 90, 90);

            using var brush = new SolidBrush(fillColor);
            using var pen = new Pen(borderColor, 1.0f);

            g.FillEllipse(brush, 2, 2, 12, 12);
            g.DrawEllipse(pen, 2, 2, 12, 12);
        }

        IntPtr hIcon = bitmap.GetHicon();
        icon = Icon.FromHandle(hIcon);
        return hIcon;
    }
}
