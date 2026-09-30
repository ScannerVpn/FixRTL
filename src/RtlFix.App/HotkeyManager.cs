using System.Windows.Forms;

namespace RtlFix.App;

/// <summary>A key chord as typed in the settings, plus what it triggers.</summary>
readonly record struct Hotkey(string Chord, RtlAction Action)
{
    /// <summary>
    /// Parses "Ctrl+Alt+Shift+V" into the flags Win32 expects. Returns false, with a message for the
    /// settings window, when the chord cannot be registered.
    /// </summary>
    public bool TryParse(out uint modifiers, out uint key, out string error)
    {
        modifiers = Interop.ModNoRepeat;
        key = 0;
        error = string.Empty;

        foreach (var part in Chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= Interop.ModControl; break;
                case "alt": modifiers |= Interop.ModAlt; break;
                case "shift": modifiers |= Interop.ModShift; break;
                case "win": case "windows": modifiers |= Interop.ModWin; break;
                case "space": key = 0x20; break;
                case "enter": case "return": key = 0x0D; break;
                case "tab": key = 0x09; break;
                case "insert": key = 0x2D; break;
                default:
                    if (part.StartsWith('f') && int.TryParse(part[1..], out var fn) && fn is >= 1 and <= 12)
                    {
                        key = (uint)(0x70 + fn - 1);
                        break;
                    }
                    if (part.Length != 1 || !char.IsLetterOrDigit(part[0]))
                    {
                        error = $"“{part}” is not a key.";
                        return false;
                    }
                    // Letters and digits keep their ASCII codes as Win32 virtual-key codes.
                    key = (uint)char.ToUpperInvariant(part[0]);
                    break;
            }
        }

        if (key == 0) error = "No key was given.";
        else if (modifiers == Interop.ModNoRepeat) error = "A modifier (Ctrl, Alt, Shift, Win) is needed.";
        else return true;
        return false;
    }
}

/// <summary>
/// Owns the global hotkeys. They belong to a window that is created but never shown, which is how a
/// tray application keeps receiving keys while some other program has the focus.
/// </summary>
sealed class HotkeyManager : IDisposable
{
    readonly MessageWindow window = new();
    int nextId = 1;

    public Action<RtlAction>? Triggered
    {
        get => window.HotkeyPressed;
        set => window.HotkeyPressed = value;
    }

    /// <summary>Replaces the whole registration set; returns one explanation per chord that failed.</summary>
    public IReadOnlyList<string> Apply(IReadOnlyList<Hotkey> hotkeys)
    {
        window.UnregisterAll();

        var failures = new List<string>();
        foreach (var hotkey in hotkeys)
        {
            if (!hotkey.TryParse(out var modifiers, out var key, out var error))
            {
                failures.Add($"{hotkey.Chord} — {error}");
                continue;
            }
            if (!Interop.RegisterHotKey(window.Handle, nextId, modifiers, key))
            {
                failures.Add($"{hotkey.Chord} — another application already uses it.");
                continue;
            }
            window.Actions[nextId] = hotkey.Action;
            nextId++;
        }
        return failures;
    }

    public void Dispose() => window.Dispose();

    sealed class MessageWindow : Form
    {
        public readonly Dictionary<int, RtlAction> Actions = [];
        public Action<RtlAction>? HotkeyPressed;

        public MessageWindow() => ShowInTaskbar = false;

        public void UnregisterAll()
        {
            foreach (var id in Actions.Keys)
                Interop.UnregisterHotKey(Handle, id);
            Actions.Clear();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Interop.WM_HOTKEY
                && HotkeyPressed is not null
                && Actions.TryGetValue(message.WParam.ToInt32(), out var action))
                HotkeyPressed(action);
            base.WndProc(ref message);
        }
    }
}
