using System.Text;

namespace RtlFix.Core.Unicode;

/// <summary>
/// Hand written half of the table accessors; the range data itself lives in UnicodeTables.g.cs.
/// </summary>
public static partial class UnicodeTables
{
    static byte Lookup(int[] starts, int[] ends, byte[] values, int codePoint)
    {
        var lo = 0;
        var hi = starts.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if (codePoint < starts[mid]) hi = mid - 1;
            else if (codePoint > ends[mid]) lo = mid + 1;
            else return values[mid];
        }
        return 0;
    }

    public static BidiClass GetBidiClass(Rune rune) => GetBidiClass(rune.Value);

    public static JoiningType GetJoiningType(Rune rune) => GetJoiningType(rune.Value);

    public static int GetPairedBracket(Rune rune) => GetPairedBracket(rune.Value);

    public static BracketType GetBracketType(Rune rune) => GetBracketType(rune.Value);

    public static int GetMirror(Rune rune) => GetMirror(rune.Value);

    /// <summary>
    /// Presentation form for a joining letter, or -1 when the letter has no glyph for that form.
    /// </summary>
    public static int GetPresentationForm(Rune rune, JoiningForm form) => GetPresentation(rune.Value * 4 + (int)form);

    public static bool IsExplicitLevel(BidiClass c) =>
        c is BidiClass.LRE or BidiClass.RLE or BidiClass.LRO or BidiClass.RLO or BidiClass.PDF;

    public static bool IsIsolateInitiator(BidiClass c) =>
        c is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI;

    public static bool IsRtl(BidiClass c) => c is BidiClass.R or BidiClass.AL;

    /// <summary>
    /// Whether a rune can carry a directional value of its own (rules N0 and P2).
    /// </summary>
    public static bool IsStrong(BidiClass c) =>
        c is BidiClass.L or BidiClass.R or BidiClass.AL;
}
