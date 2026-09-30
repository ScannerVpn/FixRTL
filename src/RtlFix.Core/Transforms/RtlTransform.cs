using System.Text;
using RtlFix.Core.Bidi;
using RtlFix.Core.Shaping;
using RtlFix.Core.Unicode;

namespace RtlFix.Core.Transforms;

/// <summary>
/// Rewrites mixed-direction text so a chosen kind of renderer displays it the way the author meant.
/// </summary>
public static class RtlTransform
{
    public const char RightToLeftMark = '\u200F';
    const char LeftToRightIsolate = '\u2066';
    const char PopDirectionalIsolate = '\u2069';

    public static string Rtlize(string text, RtlOptions? options = null)
    {
        options ??= new RtlOptions();
        var mode = options.TargetIsBidiBlind
            ? RtlMode.Visual
            : (options.Mode == RtlMode.Visual ? RtlMode.Visual : RtlMode.Repair);

        var builder = new StringBuilder(text.Length);
        foreach (var (paragraph, separator) in SplitLines(text))
            builder.Append(mode == RtlMode.Visual
                ? VisualParagraph(paragraph, options)
                : RepairParagraph(paragraph, options)).Append(separator);
        return builder.ToString();
    }

    /// <summary>
    /// Inverts a visual-order string back to logical order: runs are walked right-to-left in
    /// reverse, right-to-left runs are flipped and left-to-right runs (English words, numbers)
    /// keep their internal order. Used to salvage rows that an earlier pass already reordered.
    /// </summary>
    public static string LogicalFromVisual(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        if (runes.Length == 0 || !ContainsRightToLeft(runes)) return text;

        var analysis = BidiResolver.Resolve(runes, 1);
        var runs = analysis.LevelRuns();
        var builder = new StringBuilder(runes.Length);
        for (var r = runs.Count - 1; r >= 0; r--)
        {
            var (start, end, level) = runs[r];
            if ((level & 1) != 0)
            {
                for (var i = end - 1; i >= start; i--) builder.Append(runes[i].ToString());
            }
            else
            {
                for (var i = start; i < end; i++) builder.Append(runes[i].ToString());
            }
        }
        return builder.ToString();
    }

    /// <summary>True when the text holds Arabic-script or Hebrew letters, ie anything to fix.</summary>
    public static bool NeedsRightToLeft(string text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (UnicodeTables.IsRtl(UnicodeTables.GetBidiClass(rune)))
                return true;
        return false;
    }

    /// <summary>
    /// Paragraphs in the sense of rule P1, each with the separator that followed it so the caller
    /// can rebuild the original structure.
    /// </summary>
    static IEnumerable<(string Paragraph, string Separator)> SplitLines(string text)
    {
        var builder = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (UnicodeTables.GetBidiClass(rune) == BidiClass.B)
            {
                yield return (builder.ToString(), rune.ToString());
                builder.Clear();
                continue;
            }
            builder.Append(rune.ToString());
        }
        yield return (builder.ToString(), string.Empty);
    }

    static string RepairParagraph(string paragraph, RtlOptions options)
    {
        var runes = StripDirectionalControls(paragraph);
        if (!ContainsRightToLeft(runes)) return paragraph;

        var isCmd = IsCommandLineOrCode(paragraph);
        var baseLvl = isCmd ? (byte)0 : BaseLevel(runes, paragraph);

        // Resolve as the author reads it: a right-to-left paragraph with embedded left-to-right runs,
        // or an LTR paragraph with embedded RTL runs for commands/code.
        var analysis = BidiResolver.Resolve(runes, baseLvl);

        var open = new HashSet<int>();
        var close = new HashSet<int>();
        if (options.IsolateLeftToRightRuns && !isCmd)
            foreach (var (start, end, level) in analysis.LevelRuns())
            {
                if ((level & 1) != 0 || !ContainsStrongLeft(runes, start, end)) continue;
                open.Add(start);
                close.Add(end);
            }

        var builder = new StringBuilder(paragraph.Length + 8);
        if (options.ParagraphMark && !isCmd) builder.Append(RightToLeftMark);
        for (var i = 0; i < runes.Length; i++)
        {
            if (open.Contains(i)) builder.Append(LeftToRightIsolate);
            builder.Append(runes[i].ToString());
            if (close.Contains(i + 1)) builder.Append(PopDirectionalIsolate);
        }
        return builder.ToString();
    }

    static string VisualParagraph(string paragraph, RtlOptions options)
    {
        var runes = paragraph.EnumerateRunes().ToArray();
        if (runes.Length == 0 || !ContainsRightToLeft(runes)) return paragraph;

        var level = BaseLevel(runes, paragraph);

        var shaped = options.Shape
            ? PersianShaper.ShapeRunes(runes)
            : new PersianShaper.Shaped(runes, AllPresent(runes.Length));
        return BidiResolver.Resolve(runes, level).VisualString(shaped.Forms, shaped.Present, options.Mirror);
    }

    /// <summary>
    /// Persian and Hebrew are written right to left whichever letter happens to open the line, so a
    /// foreign-language file name at the start must not drag the whole paragraph the other way.
    /// Commands and code statements stay left-to-right so syntax and arguments are preserved.
    /// </summary>
    static byte BaseLevel(Rune[] runes, string text)
    {
        if (!ContainsRightToLeft(runes)) return 0;
        if (IsCommandLineOrCode(text)) return 0;
        return 1;
    }

    public static bool IsCommandLineOrCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.TrimStart();

        // 1. Shell prompts or file paths
        if (trimmed.StartsWith("$ ") || trimmed.StartsWith("# ") || trimmed.StartsWith("> ") ||
            trimmed.StartsWith("./") || trimmed.StartsWith("../") || trimmed.StartsWith("/") ||
            (trimmed.Length > 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' && (trimmed[2] == '\\' || trimmed[2] == '/')))
        {
            return true;
        }

        // 2. Known CLI commands
        string[] cliCommands =
        [
            "git", "echo", "cat", "ls", "grep", "cd", "dir", "python", "python3", "py",
            "node", "npm", "npx", "pnpm", "yarn", "bun", "deno", "dotnet", "cargo",
            "rustc", "go", "gcc", "g++", "clang", "make", "cmake", "docker", "podman",
            "kubectl", "curl", "wget", "ssh", "scp", "sudo", "mkdir", "rm", "del",
            "cp", "copy", "mv", "move", "touch", "chmod", "chown", "tar", "zip",
            "unzip", "set", "export", "powershell", "pwsh", "cmd", "bash", "sh",
            "zsh", "pip", "pip3", "uv", "conda", "wt", "start", "find", "cls",
            "clear", "nano", "vim", "vi"
        ];

        foreach (var cmd in cliCommands)
        {
            if (trimmed.StartsWith(cmd, StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Length == cmd.Length || char.IsWhiteSpace(trimmed[cmd.Length]) || trimmed[cmd.Length] == '.')
                    return true;
            }
        }

        // 3. Code statement keywords
        string[] codeKeywords =
        [
            "const ", "let ", "var ", "def ", "class ", "function ", "import ",
            "return ", "public ", "private ", "protected ", "console.log", "print(", "Console.Write"
        ];
        foreach (var kw in codeKeywords)
        {
            if (trimmed.StartsWith(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 4. Command flags or quoted string pattern
        var firstWord = trimmed.Split([' ', '\t', '=', '('], 2)[0];
        if (firstWord.Length > 0 && firstWord.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            if (trimmed.Contains(" -") || trimmed.Contains(" --") || trimmed.Contains("=\"") || trimmed.Contains("='"))
                return true;
        }

        return false;
    }

    static bool[] AllPresent(int length)
    {
        var present = new bool[length];
        Array.Fill(present, true);
        return present;
    }

    static bool ContainsRightToLeft(Rune[] runes)
    {
        foreach (var rune in runes)
            if (UnicodeTables.IsRtl(UnicodeTables.GetBidiClass(rune)))
                return true;
        return false;
    }

    static bool ContainsStrongLeft(Rune[] runes, int start, int end)
    {
        for (var i = start; i < end; i++)
            if (UnicodeTables.GetBidiClass(runes[i]) == BidiClass.L)
                return true;
        return false;
    }

    /// <summary>
    /// Drops the controls the algorithm consumes (rule X9) and the paragraph marks this transform
    /// writes, so running it twice cannot accumulate them. Zero-width joiners are left alone: they
    /// carry meaning for Persian spelling.
    /// </summary>
    static Rune[] StripDirectionalControls(string text)
    {
        var builder = new List<Rune>(text.Length);
        foreach (var rune in text.EnumerateRunes())
            if (!IsDroppable(rune))
                builder.Add(rune);
        return [.. builder];
    }

    static bool IsDroppable(Rune rune) =>
        UnicodeTables.GetBidiClass(rune) is BidiClass.LRE or BidiClass.RLE or BidiClass.LRO
            or BidiClass.RLO or BidiClass.PDF or BidiClass.LRI or BidiClass.RLI
            or BidiClass.FSI or BidiClass.PDI
            // Arabic letter mark and right-to-left mark are class AL and L but carry no letter.
            || rune.Value is 0x061C or 0x200E or 0x200F;
}
