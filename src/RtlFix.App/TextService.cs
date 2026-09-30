using System.Windows.Forms;
using RtlFix.Core.Transforms;
using Timer = System.Windows.Forms.Timer;

namespace RtlFix.App;

/// <summary>
/// What the hotkeys and the floating square ask for: fix what is already on the clipboard, fix the
/// selection where it stands, or show the selection in a window that reads right-to-left.
/// </summary>
sealed class TextService
{
    readonly NotifyIcon? icon;
    readonly Timer restoreTimer = new();
    string? clipboardBefore;

    public RtlSettings Settings { get; }

    public TextService(RtlSettings settings, NotifyIcon? icon = null)
    {
        Settings = settings;
        this.icon = icon;
        restoreTimer.Tick += (_, _) =>
        {
            restoreTimer.Stop();
            if (clipboardBefore is not null)
                ClipboardText.Write(clipboardBefore);
            clipboardBefore = null;
        };
    }

    public void FixClipboard()
    {
        var text = ClipboardText.Read();
        Log.Write($"fix-clipboard start ({Length(text)} characters)");
        if (string.IsNullOrEmpty(text))
        {
            Hint("The clipboard holds no text.", warning: true);
            return;
        }

        var result = RtlTransform.Rtlize(text, Options());
        if (result == text)
        {
            Hint("Nothing to change.");
            return;
        }
        if (ClipboardText.Write(result))
            Hint(result.Length > 200 ? "Clipboard fixed." : Preview(result));
    }

    public void CopyFixPaste(bool forceVisual = false)
    {
        var proc = TargetApp.ForegroundProcess();
        var terminal = IsTerminal(proc);
        var blind = forceVisual || IgnoresBidi();
        Log.Write($"copy-fix-paste start (proc: {proc}, terminal: {terminal}, blind: {blind})");

        string? before = null, selected;
        if (terminal)
        {
            before = ClipboardText.Read();
            var seqBefore = Interop.GetClipboardSequenceNumber();
            Interop.PressControlShift('C');
            selected = WaitForCopy(seqBefore, before, maxAttempts: 6);
            if (string.IsNullOrEmpty(selected))
            {
                Interop.PressControl('C');
                selected = WaitForCopy(seqBefore, before, maxAttempts: 6);
            }
            if (string.IsNullOrEmpty(selected))
            {
                selected = ClipboardText.Read();
            }
        }
        else
        {
            before = ClipboardText.Read();
            var seqBefore = Interop.GetClipboardSequenceNumber();
            Interop.PressControl('C');
            selected = WaitForCopy(seqBefore, before);
        }

        if (string.IsNullOrEmpty(selected))
        {
            Log.Write("copy-fix-paste: no text was selected");
            return;
        }

        var result = RtlTransform.Rtlize(selected, Settings.Options(targetIsBidiBlind: blind));
        if (result == selected)
        {
            Log.Write($"copy-fix-paste: nothing to change in {Length(selected)} characters");
            var hasRtl = RtlTransform.NeedsRightToLeft(selected);
            Hint(hasRtl ? "متن از قبل مرتب است." : "متن انتخاب شده انگلیسی است (حروف فارسی ندارد).", warning: !hasRtl);
            return;
        }
        if (!ClipboardText.Write(result))
        {
            Log.Write("copy-fix-paste: clipboard busy");
            Hint("The clipboard is busy; try again.", warning: true);
            return;
        }

        Thread.Sleep(25);
        if (terminal)
        {
            Interop.PressShiftInsert();
        }
        else
        {
            Interop.PressControl('V');
        }
        Log.Write($"copy-fix-paste: pasted {Length(result)} characters");

        if (Settings.RestoreClipboard && !string.IsNullOrEmpty(before))
        {
            clipboardBefore = before;
            restoreTimer.Interval = 600;
            restoreTimer.Start();
        }
    }

    RtlReader? readerForm;

    /// <summary>
    /// For a surface that cannot be written to — a web page, a chat panel, a PDF. The selection is
    /// copied and repaired, then shown in a window of its own that lays text out right-to-left, and
    /// left on the clipboard for pasting elsewhere.
    /// </summary>
    public void ReadSelection()
    {
        var proc = TargetApp.ForegroundProcess() ?? "";
        var terminal = IsTerminal(proc);
        Log.Write($"read/live start (proc: {proc}, terminal: {terminal})");

        if (terminal)
        {
            var before = ClipboardText.Read();
            var seqBefore = Interop.GetClipboardSequenceNumber();
            Interop.PressControlShift('C');
            var selected = WaitForCopy(seqBefore, before, maxAttempts: 6);
            if (string.IsNullOrEmpty(selected))
            {
                Interop.PressControl('C');
                selected = WaitForCopy(seqBefore, before, maxAttempts: 6);
            }
            if (string.IsNullOrEmpty(selected))
            {
                selected = ClipboardText.Read();
            }

            // If nothing is selected, automatically grab visible terminal screen lines
            if (string.IsNullOrEmpty(selected) || selected == before)
            {
                var screenText = GrabTerminalScreenText();
                if (!string.IsNullOrEmpty(screenText) && RtlTransform.NeedsRightToLeft(screenText))
                {
                    selected = screenText;
                }
            }

            if (!string.IsNullOrEmpty(selected) && RtlTransform.NeedsRightToLeft(selected))
            {
                readerForm?.Close();
                readerForm = new RtlReader(selected);
                readerForm.Show();
                return;
            }
        }

        // Universal live injection: refresh live RTL for any active Electron/Chromium app (Qoder, ZCode, Antigravity, etc.)
        _ = DesktopAppIntegrator.InjectActiveForegroundAsync();

        // In-place replacement: directly fix selected text in-place for any app
        CopyFixPaste();
    }

    static string? GrabTerminalScreenText()
    {
        var fg = Interop.GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;
        Interop.GetWindowThreadProcessId(fg, out var pid);
        if (pid == 0) return null;

        uint targetPid = pid;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            if (p.ProcessName.Contains("terminal", StringComparison.OrdinalIgnoreCase))
            {
                var shells = System.Diagnostics.Process.GetProcessesByName("powershell")
                    .Concat(System.Diagnostics.Process.GetProcessesByName("cmd"))
                    .Concat(System.Diagnostics.Process.GetProcessesByName("pwsh"));
                foreach (var sh in shells)
                {
                    using (sh)
                    {
                        if (!sh.HasExited)
                        {
                            targetPid = (uint)sh.Id;
                            break;
                        }
                    }
                }
            }
        }
        catch { }

        Interop.FreeConsole();
        if (!Interop.AttachConsole(targetPid)) return null;

        try
        {
            var hOut = Interop.CreateFile("CONOUT$", 0xC0000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (hOut == IntPtr.Zero || hOut == new IntPtr(-1)) return null;

            try
            {
                if (!Interop.GetConsoleScreenBufferInfo(hOut, out var info)) return null;
                var width = info.dwSize.X;
                if (width <= 0 || width > 1000) return null;

                var sb = new System.Text.StringBuilder();
                var buffer = new char[width];
                var top = Math.Max(0, (int)info.srWindow.Top);
                var bottom = Math.Min((int)info.dwSize.Y - 1, (int)info.srWindow.Bottom);

                for (var y = top; y <= bottom; y++)
                {
                    var coord = new Interop.COORD { X = 0, Y = (short)y };
                    if (Interop.ReadConsoleOutputCharacter(hOut, buffer, (uint)width, coord, out var read) && read > 0)
                    {
                        var line = new string(buffer, 0, (int)read).TrimEnd();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            sb.AppendLine(line);
                        }
                    }
                }
                return sb.ToString();
            }
            finally
            {
                Interop.CloseHandle(hOut);
            }
        }
        finally
        {
            Interop.FreeConsole();
        }
    }

    public void ToggleMode()
    {
        Settings.Mode = Settings.Mode switch
        {
            RtlMode.Visual => RtlMode.Repair,
            _ => RtlMode.Visual,
        };
        Hint(Settings.Mode == RtlMode.Visual
            ? "Visual order: for Notepad, terminals, game chat."
            : "Repaired marks: for Word, browsers, chat.");
    }

    QuickRtlWriterForm? writerForm;

    public void OpenQuickWriter()
    {
        writerForm?.Close();
        var targetHwnd = Interop.GetForegroundWindow();
        var targetProc = TargetApp.ForegroundProcess();
        var isTerminal = IsTerminal(targetProc);

        writerForm = new QuickRtlWriterForm(Settings, targetHwnd, targetProc, isTerminal, this);
        writerForm.Show();
    }

    public void PasteTextDirect(string text, bool isTerminal)
    {
        var before = Settings.RestoreClipboard ? ClipboardText.Read() : null;
        if (!ClipboardText.Write(text))
        {
            Hint("حافظه موقت مشغول است.", warning: true);
            return;
        }

        Thread.Sleep(30);
        if (isTerminal)
        {
            Interop.PressShiftInsert();
        }
        else
        {
            Interop.PressControl('V');
        }

        if (Settings.RestoreClipboard && !string.IsNullOrEmpty(before))
        {
            clipboardBefore = before;
            restoreTimer.Interval = 600;
            restoreTimer.Start();
        }
    }

    public static bool IsTerminal(string? process)
    {
        if (string.IsNullOrEmpty(process)) return false;
        return process.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase)
            || process.Equals("wt", StringComparison.OrdinalIgnoreCase)
            || process.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || process.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || process.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || process.Equals("mintty", StringComparison.OrdinalIgnoreCase)
            || process.Equals("conhost", StringComparison.OrdinalIgnoreCase)
            || process.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || process.Equals("OpenConsole", StringComparison.OrdinalIgnoreCase)
            || process.Equals("Alacritty", StringComparison.OrdinalIgnoreCase)
            || process.Equals("wezterm-gui", StringComparison.OrdinalIgnoreCase);
    }

    static readonly HashSet<string> NativeBidiWordProcessors = new(StringComparer.OrdinalIgnoreCase)
    {
        "winword", "wordpad", "soffice.bin", "powerpnt"
    };

    /// <summary>True when the foreground app needs visual RTL reordering (all apps except native word processors).</summary>
    public bool IgnoresBidi()
    {
        var process = TargetApp.ForegroundProcess();
        if (string.IsNullOrEmpty(process)) return true;
        if (NativeBidiWordProcessors.Contains(process)) return false;
        return true;
    }

    /// <summary>The foreground program renders the text, so it decides whether reordering is needed.</summary>
    RtlOptions Options(bool forceVisual = false) =>
        Settings.Options(targetIsBidiBlind: forceVisual || IgnoresBidi());

    /// <summary>The selection the target just copied, or null when the copy did not happen.</summary>
    static string? WaitForCopy(uint seqBefore, string? before, int maxAttempts = 25)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            Application.DoEvents();
            var text = ClipboardText.Read();
            var seq = Interop.GetClipboardSequenceNumber();
            if (seq != seqBefore && !string.IsNullOrEmpty(text)) return text;
            if (!string.IsNullOrEmpty(text) && text != before) return text;
            Thread.Sleep(15);
        }

        return null;
    }

    void Hint(string text, bool warning = false)
    {
        if (icon is null) return;
        icon.ShowBalloonTip(1400, "RtlFix", text, warning ? ToolTipIcon.Warning : ToolTipIcon.None);
    }

    static int Length(string? text) => text?.Length ?? -1;

    static string Preview(string text) => text.Length <= 120 ? text : text[..120] + "…";
}
