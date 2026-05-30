// =============================================================================
// Program.cs
// DS4Xbox アプリケーションのエントリポイント。
//
// 起動シーケンス:
//   1. appsettings.json を読み込み
//   2. TrayApplication（タスクトレイ常駐 UI）を生成
//   3. startEnabled が true の場合、自動的に変換を開始
//   4. WinForms のメッセージループに入る
//   5. 終了時: HidHide の Cloak を解除し、仮想コントローラーを切断
// =============================================================================

using DS4Xbox;
using DS4Xbox.UI;

namespace DS4Xbox;

internal static class Program
{
    /// <summary>
    /// アプリケーションのメインエントリポイント。
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 多重起動防止
        using var mutex = new Mutex(true, "DS4Xbox-SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "DS4Xbox は既に起動しています。\nタスクトレイを確認してください。",
                "DS4Xbox",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // WinForms の初期化
        ApplicationConfiguration.Initialize();

        // 設定の読み込み
        var settings = AppSettings.Load();

        // タスクトレイアプリケーションを起動
        var trayApp = new TrayApplication(settings);

        // メッセージループに入る（タスクトレイの「終了」が選ばれるまでここでブロック）
        Application.Run(trayApp);
    }
}
