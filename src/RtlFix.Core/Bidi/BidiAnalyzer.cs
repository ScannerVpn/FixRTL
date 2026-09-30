using System.Text;
using RtlFix.Core.Unicode;

namespace RtlFix.Core.Bidi;

public enum BaseDirection
{
    /// <summary>Rules P2/P3: the first strong character of the paragraph decides.</summary>
    Auto,
    LeftToRight,
    RightToLeft,
}

/// <summary>
/// Splits text into paragraphs (rule P1) and resolves each one with <see cref="BidiResolver"/>.
/// </summary>
public static class BidiAnalyzer
{
    /// <summary>One analysis per paragraph, in document order. Paragraph separators are dropped.</summary>
    public static List<BidiAnalysis> Analyze(string text, BaseDirection direction = BaseDirection.Auto)
    {
        var result = new List<BidiAnalysis>();
        foreach (var paragraph in SplitParagraphs(text))
        {
            var runes = paragraph.ToArray();
            var level = direction switch
            {
                BaseDirection.LeftToRight => (byte)0,
                BaseDirection.RightToLeft => (byte)1,
                _ => (byte)(DetectRightToLeft(runes) ? 1 : 0),
            };
            result.Add(BidiResolver.Resolve(runes, level));
        }
        return result;
    }

    /// <summary>Rule P1: a type B character ends a paragraph and is not part of it.</summary>
    public static List<List<Rune>> SplitParagraphs(string text)
    {
        var paragraphs = new List<List<Rune>> { new() };
        foreach (var rune in text.EnumerateRunes())
        {
            if (UnicodeTables.GetBidiClass(rune) == BidiClass.B)
            {
                paragraphs.Add([]);
                continue;
            }
            paragraphs[^1].Add(rune);
        }
        return paragraphs;
    }

    /// <summary>Rules P2 and P3 over one paragraph.</summary>
    public static bool DetectRightToLeft(Rune[] runes)
    {
        var depth = 0;
        foreach (var rune in runes)
        {
            switch (UnicodeTables.GetBidiClass(rune))
            {
                case BidiClass.RLI:
                case BidiClass.LRI:
                case BidiClass.FSI:
                    depth++;
                    break;
                case BidiClass.PDI:
                    if (depth > 0) depth--;
                    break;
                case BidiClass.R or BidiClass.AL when depth == 0:
                    return true;
                case BidiClass.L when depth == 0:
                    return false;
            }
        }
        return false;
    }
}
