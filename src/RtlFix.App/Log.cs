namespace RtlFix.App;

/// <summary>
/// One line per notable event. A tray app has no window and its balloons expire in a second, so
/// without this nothing about a failed hotkey is ever recoverable.
/// </summary>
static class Log
{
    const long MaxBytes = 200_000;
    static readonly string path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RtlFix", "log.txt");

    static readonly object gate = new();

    public static void Write(string message)
    {
        lock (gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
