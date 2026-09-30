using System.Text.Json;
using System.Text.Json.Serialization;
using RtlFix.Core.Transforms;

namespace RtlFix.App;

/// <summary>What a hotkey asks the app to do.</summary>
public enum RtlAction
{
    /// <summary>Rewrite the clipboard contents in place.</summary>
    FixClipboard,

    /// <summary>Copy the selection, fix it, and paste it back.</summary>
    CopyFixPaste,

    /// <summary>Copy the selection and paste it reordered for a renderer that knows no bidi.</summary>
    PasteVisual,

    /// <summary>Switch the tray app between repair and visual output.</summary>
    ToggleMode,

    /// <summary>Show the selection in a right-to-left reader window.</summary>
    ReadSelection,

    /// <summary>Open the quick in-place RTL writer bar.</summary>
    QuickWriter,
}

public sealed class CustomManagedApp
{
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool IsElectron { get; set; }
}

public sealed class RtlSettings
{
    public RtlMode Mode { get; set; } = RtlMode.Visual;
    public bool Shape { get; set; } = true;
    public bool Mirror { get; set; } = true;
    public bool ParagraphMark { get; set; } = true;
    public bool IsolateLeftToRightRuns { get; set; } = true;

    /// <summary>Look at the program in the foreground and reorder the text if it cannot display bidi.</summary>
    public bool DetectTargetApp { get; set; } = true;

    /// <summary>Process names, lower case, that receive visually reordered text.</summary>
    public List<string> BidiBlindApps { get; set; } =
    [
        "cmd", "conhost", "windowsterminal", "wt", "powershell", "pwsh", "tabby",
        "putty", "hyper", "mintty", "bash", "openconsole", "alacritty", "wezterm-gui",
        "qoder", "code", "notepad", "notepad++"
    ];

    /// <summary>Put the copied text back once the paste has landed, so the clipboard is not polluted.</summary>
    public bool RestoreClipboard { get; set; } = true;

    /// <summary>Custom applications registered by the user for automatic RTL.</summary>
    public List<CustomManagedApp> CustomApps { get; set; } = [];

    /// <summary>Show the clickable square that floats over every program (deprecated).</summary>
    public bool ButtonVisible { get; set; } = false;

    /// <summary>Where the square sits; a negative x means "top-right of the screen".</summary>
    public int ButtonX { get; set; } = -1;
    public int ButtonY { get; set; } = -1;

    public Dictionary<RtlAction, string> Keys { get; set; } = new()
    {
        [RtlAction.FixClipboard] = "Ctrl+Alt+R",
        [RtlAction.CopyFixPaste] = "Ctrl+Alt+V",
        [RtlAction.PasteVisual] = "Ctrl+Alt+Shift+V",
        [RtlAction.ToggleMode] = "Ctrl+Alt+T",
        [RtlAction.ReadSelection] = "Ctrl+Alt+X",
        [RtlAction.QuickWriter] = "Ctrl+Alt+Space",
    };

    public RtlOptions Options(bool targetIsBidiBlind = false) => new()
    {
        Mode = Mode,
        TargetIsBidiBlind = targetIsBidiBlind,
        Shape = Shape,
        Mirror = Mirror,
        ParagraphMark = ParagraphMark,
        IsolateLeftToRightRuns = IsolateLeftToRightRuns,
    };
}

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Path { get; }

    public SettingsStore()
        : this(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RtlFix", "settings.json"))
    {
    }

    public SettingsStore(string path) => Path = path;

    public RtlSettings Load()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<RtlSettings>(File.ReadAllText(Path), Json) ?? new RtlSettings();
            foreach (var (action, chord) in new RtlSettings().Keys)
                loaded.Keys.TryAdd(action, chord);
            return loaded;
        }
        catch (IOException)
        {
            // First run, or the file is on a drive that is not there right now.
            return new RtlSettings();
        }
        catch (JsonException)
        {
            // A hand-edited file that no longer parses should not stop the app from starting.
            return new RtlSettings();
        }
    }

    public void Save(RtlSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, Json));
    }
}
