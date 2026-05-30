// =============================================================================
// AppSettings.cs
// アプリケーション設定の読み書きを行うクラス。
// System.Text.Json を使用（.NET BCL 標準、外部ライブラリ不使用）。
// =============================================================================

using System.Text.Json;

namespace DS4Xbox;

/// <summary>
/// アプリケーションの設定を管理するクラス。
/// appsettings.json から読み込み、変更時に書き戻す。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 設定ファイルのパス。
    /// 実行ファイルと同じディレクトリに配置される。
    /// </summary>
    private static readonly string SettingsPath = Path.Combine(
        AppContext.BaseDirectory,
        "appsettings.json");

    /// <summary>
    /// アプリケーション起動時に自動的に変換を開始するかどうか。
    /// </summary>
    public bool StartEnabled { get; set; }

    /// <summary>
    /// 設定ファイルから読み込む。ファイルが存在しない場合はデフォルト値を使用。
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data != null)
                {
                    return new AppSettings
                    {
                        StartEnabled = data.startEnabled,
                    };
                }
            }
        }
        catch (Exception)
        {
            // 設定ファイルの読み込みに失敗した場合はデフォルト値を使用
        }

        return new AppSettings { StartEnabled = false };
    }

    /// <summary>
    /// 現在の設定をファイルに保存する。
    /// </summary>
    public void Save()
    {
        try
        {
            var data = new SettingsData
            {
                startEnabled = StartEnabled,
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            string json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception)
        {
            // 設定ファイルの書き込みに失敗した場合は無視
            // （読み取り専用メディアなど）
        }
    }

    /// <summary>
    /// JSON シリアライズ用の内部クラス。
    /// appsettings.json のスキーマに一致させる。
    /// </summary>
    private sealed class SettingsData
    {
        public bool startEnabled { get; set; }
    }
}
