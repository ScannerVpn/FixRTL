using Microsoft.Win32;

namespace RtlFix.App;

/// <summary>Whether Windows starts the tray app on logon, remembered in the per-user run key.</summary>
static class Startup
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "RtlFix";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("The run key could not be opened.");

        if (enabled)
            key.SetValue(ValueName, $"\"{ApplicationPaths.Executable}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

static class ApplicationPaths
{
    /// <summary>Environment.ProcessPath survives a renamed or moved executable; the assembly name does not.</summary>
    public static string Executable => Environment.ProcessPath
        ?? System.Reflection.Assembly.GetEntryAssembly()?.Location
        ?? throw new InvalidOperationException("The application path is unknown.");
}
