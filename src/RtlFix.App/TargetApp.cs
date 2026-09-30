using System.Diagnostics;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms.Clipboard;

namespace RtlFix.App;

/// <summary>Finds out which program would receive the text, since that decides how to write it.</summary>
static class TargetApp
{
    public static string? ForegroundProcess()
    {
        var window = Interop.GetForegroundWindow();
        if (window == IntPtr.Zero) return null;

        Interop.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return null;

        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            // The window closed while we were looking at it.
            return null;
        }
    }
}

/// <summary>
/// Plain clipboard text, retried for the few milliseconds another application holds the clipboard open.
/// </summary>
static class ClipboardText
{
    public static string? Read() => Try(() => Forms.ContainsText() ? Forms.GetText() : null);

    /// <summary>Replaces the clipboard. Clearing first stops the old text being merged into the new.</summary>
    public static bool Write(string text) => Try(() =>
    {
        Forms.Clear();
        Forms.SetText(text);
        return true;
    });

    static T Try<T>(Func<T> action)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                return action();
            }
            catch (ExternalException)
            {
                Thread.Sleep(25);
            }
        }
        return default!;
    }
}
