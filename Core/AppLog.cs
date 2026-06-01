namespace DS4Xbox.Core;

internal static class AppLog
{
    private static readonly object Sync = new();
    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "ds4xbox.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception == null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break controller conversion.
        }
    }
}
