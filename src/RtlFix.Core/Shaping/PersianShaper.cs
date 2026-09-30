using System.Text;
using RtlFix.Core.Unicode;

namespace RtlFix.Core.Shaping;

/// <summary>
/// Turns Arabic/Persian letters into their contextual presentation forms, for renderers that do
/// not shape on their own (many terminals, game chat, bitmap font engines).
/// </summary>
/// <remarks>
/// A reduced form of the UAX #33 algorithm: the joining rules are applied in full, the lam-alif
/// and other ligature substitutions are not, since the fonts those targets use render the two
/// letters side by side acceptably and the ligature code points are compatibility characters.
/// </remarks>
public static class PersianShaper
{
    const int ZeroWidthJoiner = 0x200D;
    const int ZeroWidthNonJoiner = 0x200C;

    /// <summary>
    /// The presentation form for each input code point, index-aligned with it, plus whether the
    /// position should be emitted at all: a join control between two letters is consumed by the
    /// forms their connection implies.
    /// </summary>
    public readonly record struct Shaped(Rune[] Forms, bool[] Present);

    public static string Shape(string text)
    {
        var shaped = ShapeRunes(text.EnumerateRunes().ToArray());
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < shaped.Forms.Length; i++)
            if (shaped.Present[i])
                builder.Append(shaped.Forms[i].ToString());
        return builder.ToString();
    }

    public static Shaped ShapeRunes(Rune[] runes)
    {
        var joining = runes.Select(UnicodeTables.GetJoiningType).ToArray();
        var n = runes.Length;

        // links[i] is true when the letter at i connects to the letter at i+1. Transparent code
        // points (vowel marks) are skipped rather than treated as word boundaries, so each letter
        // is tested against its nearest shaping neighbour.
        var backward = new bool[n]; // connects to its earlier neighbour
        var forward = new bool[n];  // connects to its later neighbour

        var previous = -1;
        for (var i = 0; i < n; i++)
        {
            if (joining[i] == JoiningType.T) continue;
            // Join_Causing (tatweel, ZWJ) links both ways; Non_Joining breaks both.
            var linked = previous >= 0
                && joining[previous] is JoiningType.L or JoiningType.D or JoiningType.C
                && joining[i] is JoiningType.R or JoiningType.D or JoiningType.C;
            if (linked)
            {
                forward[previous] = true;
                backward[i] = true;
            }
            previous = i;
        }

        var forms = new Rune[n];
        var present = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var rune = runes[i];
            if (IsJoinControl(rune) && Consumed(runes, i))
            {
                present[i] = false;
                forms[i] = rune;
                continue;
            }

            present[i] = true;
            forms[i] = joining[i] switch
            {
                JoiningType.R or JoiningType.L or JoiningType.D or JoiningType.C
                    => FormOf(rune, backward[i], forward[i]),
                _ => rune,
            };
        }

        return new Shaped(forms, present);
    }

    static Rune FormOf(Rune rune, bool before, bool after)
    {
        var form = (before, after) switch
        {
            (true, true) => JoiningForm.Medial,
            (true, false) => JoiningForm.Final,
            (false, true) => JoiningForm.Initial,
            _ => JoiningForm.Isolated,
        };
        var presentation = UnicodeTables.GetPresentationForm(rune, form);
        return presentation >= 0 ? new Rune(presentation) : rune;
    }

    static bool IsJoinControl(Rune rune) =>
        rune.Value is ZeroWidthJoiner or ZeroWidthNonJoiner;

    /// <summary>
    /// True when a join control sits between two letters, where the chosen presentation forms
    /// already carry its effect and a renderer that ignores it would show a placeholder glyph.
    /// </summary>
    static bool Consumed(Rune[] runes, int index)
    {
        var before = LetterBefore(runes, index);
        var after = LetterAfter(runes, index);
        return before >= 0 && after >= 0;
    }

    static int LetterBefore(Rune[] runes, int index)
    {
        for (var i = index - 1; i >= 0; i--)
            if (UnicodeTables.GetJoiningType(runes[i]) is JoiningType.R or JoiningType.L or JoiningType.D)
                return i;
        return -1;
    }

    static int LetterAfter(Rune[] runes, int index)
    {
        for (var i = index + 1; i < runes.Length; i++)
            if (UnicodeTables.GetJoiningType(runes[i]) is JoiningType.R or JoiningType.L or JoiningType.D)
                return i;
        return -1;
    }
}
