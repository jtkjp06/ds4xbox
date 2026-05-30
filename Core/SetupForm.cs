// =============================================================================
// Core/SetupForm.cs
// ドライバ自動インストールの進捗状況を視覚的にユーザーに表示するダイアログ。
// WinForms 標準コントロールを使用（外部ライブラリ不使用、完全自作）。
// =============================================================================

using System.Drawing;
using System.Windows.Forms;

namespace DS4Xbox.Core;

/// <summary>
/// ドライバのダウンロードとインストールの進捗を表示するフォーム。
/// </summary>
public sealed class SetupForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;

    public SetupForm(string title, string initialMessage)
    {
        Text = title;
        Size = new Size(420, 160);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 30, 30); // プレミアムなダークモードカラー
        ForeColor = Color.White;

        _statusLabel = new Label
        {
            Text = initialMessage,
            Location = new Point(20, 20),
            Size = new Size(360, 45),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(20, 75),
            Size = new Size(360, 23),
            Style = ProgressBarStyle.Continuous
        };

        Controls.Add(_statusLabel);
        Controls.Add(_progressBar);
    }

    /// <summary>
    /// プログレスバーの値とステータステキストをスレッドセーフに更新する。
    /// </summary>
    public void UpdateProgress(int percentage, string statusText)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateProgress(percentage, statusText));
            return;
        }

        _progressBar.Value = Math.Clamp(percentage, 0, 100);
        _statusLabel.Text = statusText;
    }
}
