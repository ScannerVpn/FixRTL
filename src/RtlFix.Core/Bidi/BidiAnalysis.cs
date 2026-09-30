using System.Text;
using RtlFix.Core.Unicode;

namespace RtlFix.Core.Bidi;

/// <summary>
/// Resolved directionality for one paragraph: an embedding level per code point plus the
/// helpers every transform downstream needs.
/// </summary>
public sealed class BidiAnalysis
{
    public required Rune[] Runes { get; init; }

    /// <summary>Final embedding level per code point, after rules L1.</summary>
    public required byte[] Levels { get; init; }

    /// <summary>Original Bidi_Class values, before the W rules rewrote them.</summary>
    public required BidiClass[] Types { get; init; }

    /// <summary>Code points dropped by rule X9; they carry no direction of their own.</summary>
    public required bool[] Removed { get; init; }

    public required byte ParagraphLevel { get; init; }

    public int Length => Runes.Length;

    public bool IsRtl(int index) => (Levels[index] & 1) != 0;

    /// <summary>True when any visible character resolves to an odd level.</summary>
    public bool HasRightToLeft()
    {
        for (var i = 0; i < Levels.Length; i++)
            if (!Removed[i] && IsRtl(i))
                return true;
        return false;
    }

    public byte HighestLevel()
    {
        byte max = 0;
        for (var i = 0; i < Levels.Length; i++)
            if (!Removed[i] && Levels[i] > max)
                max = Levels[i];
        return max;
    }

    /// <summary>Maximal runs of visible code points sharing one level, in logical order.</summary>
    public List<(int Start, int End, byte Level)> LevelRuns()
    {
        var runs = new List<(int, int, byte)>();
        var start = -1;
        byte level = 0;
        for (var i = 0; i < Runes.Length; i++)
        {
            if (Removed[i]) continue;
            if (start < 0 || Levels[i] != level)
            {
                if (start >= 0) runs.Add((start, i, level));
                start = i;
                level = Levels[i];
            }
        }
        if (start >= 0) runs.Add((start, Runes.Length, level));
        return runs;
    }

    /// <summary>
    /// Rule L2: the indices of the visible code points in display order, left to right.
    /// </summary>
    /// <param name="applyL3">
    /// Rule L3 keeps combining marks after the base they belong to. The conformance suite stops at
    /// L2, so it can be switched off when comparing against it.
    /// </param>
    public List<int> VisualIndices(bool applyL3 = true)
    {
        var order = new List<int>();
        for (var i = 0; i < Runes.Length; i++)
            if (!Removed[i])
                order.Add(i);
        if (order.Count == 0) return order;

        var maxLevel = HighestLevel();
        var minOddLevel = byte.MaxValue;
        foreach (var i in order)
            if (IsRtl(i) && Levels[i] < minOddLevel)
                minOddLevel = Levels[i];
        if (minOddLevel == byte.MaxValue) return order;

        // Reverse each contiguous stretch at or above the level, from the highest level down.
        for (var level = (int)maxLevel; level >= minOddLevel; level--)
        {
            var i = 0;
            while (i < order.Count)
            {
                if (Levels[order[i]] < level)
                {
                    i++;
                    continue;
                }
                var start = i;
                while (i < order.Count && Levels[order[i]] >= level) i++;
                order.Reverse(start, i - start);
            }
        }

        // L3: the reversal above pushed combining marks in front of their base; put them back.
        if (applyL3)
        {
            var owner = new int[Runes.Length];
            var baseIndex = -1;
            for (var i = 0; i < Runes.Length; i++)
            {
                if (Removed[i]) continue;
                if (Types[i] == BidiClass.NSM && baseIndex >= 0) owner[i] = baseIndex;
                else owner[i] = baseIndex = i;
            }
            for (var start = 0; start < order.Count;)
            {
                var group = owner[order[start]];
                var end = start + 1;
                while (end < order.Count && owner[order[end]] == group) end++;
                if (end - start > 1)
                {
                    var block = order.GetRange(start, end - start);
                    block.Sort((a, b) => a.CompareTo(b));
                    for (var m = 0; m < block.Count; m++) order[start + m] = block[m];
                }
                start = end;
            }
        }
        return order;
    }

    /// <summary>Rules L2 to L4: the paragraph as it should appear on screen, left to right.</summary>
    public string VisualString(bool mirror = true)
    {
        var forms = new Rune[Runes.Length];
        var present = new bool[Runes.Length];
        Runes.CopyTo(forms, 0);
        Array.Fill(present, true);
        return VisualString(forms, present, mirror);
    }

    /// <summary>
    /// Rules L2 to L4 over caller-supplied glyphs, index-aligned with <see cref="Runes"/>, so a
    /// renderer-specific step such as presentation-form shaping can run before the reordering.
    /// </summary>
    public string VisualString(Rune[] forms, bool[] present, bool mirror = true)
    {
        var builder = new StringBuilder();
        foreach (var i in VisualIndices())
        {
            if (!present[i]) continue;
            var rune = forms[i];
            // L4: a mirrored glyph when the resolved direction is right-to-left.
            if (mirror && IsRtl(i))
            {
                var pair = UnicodeTables.GetMirror(rune);
                if (pair >= 0) rune = new Rune(pair);
            }
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }
}
