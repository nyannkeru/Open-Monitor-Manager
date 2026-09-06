namespace OpenMonitorManager.Services;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenMonitorManager");

    internal static string FilePath => Path.Combine(DirectoryPath, "OpenMonitorManager.log");

    internal static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var line = $"{DateTimeOffset.Now:O} {message}";
            if (exception is not null)
            {
                line += $"{Environment.NewLine}{exception}";
            }

            lock (Gate)
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the monitor controller down.
        }
    }
}
