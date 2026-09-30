using System.Text.RegularExpressions;
using RtlFix.Core.Bidi;
using RtlFix.Core.Transforms;

namespace RtlFix.Core.Tests.Transforms;

/// <summary>Properties a user would call correct, checked against the lines people really paste.</summary>
public class RealSampleTest
{
    static readonly RtlOptions Repair = new() { Mode = RtlMode.Repair };
    static readonly RtlOptions Visual = new() { Mode = RtlMode.Visual };

    public static IEnumerable<object[]> Lines => Corpus.RtlLines.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Lines))]
    public void Repair_pins_the_direction_without_reordering(string line)
    {
        var fixed_ = RtlTransform.Rtlize(line, Repair);

        Assert.StartsWith(RtlTransform.RightToLeftMark.ToString(), fixed_);
        Assert.Equal(line, Strip(fixed_));
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void Repair_is_repeatable(string line)
    {
        var once = RtlTransform.Rtlize(line, Repair);
        Assert.Equal(once, RtlTransform.Rtlize(once, Repair));
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void Repair_makes_an_ordinary_app_draw_what_a_right_to_left_paragraph_would(string line)
    {
        // Marks steer the application's own algorithm instead of rearranging the text.
        Assert.Equal(Drawn(line), Drawn(RtlTransform.Rtlize(line, Repair)));
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void Latin_survives_both_modes(string line)
    {
        AssertTokenOrder(line, RtlTransform.Rtlize(line, Repair));

        var drawn = RtlTransform.Rtlize(line, Visual);
        foreach (var token in Tokens(line))
            Assert.Contains(token, drawn, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void Visual_rearranges_to_the_same_picture_a_bidi_aware_app_draws(string line)
    {
        var unshaped = new RtlOptions { Mode = RtlMode.Visual, Shape = false };

        // The dumb renderer is handed text that already lies in the order a good renderer draws.
        Assert.NotEqual(line, RtlTransform.Rtlize(line, Visual));
        Assert.Equal(Drawn(line), RtlTransform.Rtlize(line, unshaped));
    }

    [Fact]
    public void English_only_lines_are_left_alone()
    {
        Assert.Equal(Corpus.OnlyEnglish, RtlTransform.Rtlize(Corpus.OnlyEnglish, Repair));
        Assert.Equal(Corpus.OnlyEnglish, RtlTransform.Rtlize(Corpus.OnlyEnglish, Visual));
    }

    [Fact]
    public void Paragraph_structure_survives_both_modes()
    {
        foreach (var options in new[] { Repair, Visual })
        {
            var result = RtlTransform.Rtlize(Corpus.TwoParagraphs, options);
            Assert.Equal(Corpus.TwoParagraphs.Count(c => c == '\n'), result.Count(c => c == '\n'));
            Assert.Equal(3, result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }
    }

    static List<string> Tokens(string text) =>
        [.. Regex.Matches(text, "[A-Za-z][A-Za-z0-9_.:/\\\\-]*").Select(m => m.Value)];

    static void AssertTokenOrder(string from, string to)
    {
        var expected = Tokens(from);
        Assert.Equal(expected, Tokens(to));
    }

    /// <summary>What a bidi-aware application draws for a correct right-to-left paragraph, marks left out.</summary>
    static string Drawn(string text)
    {
        var visual = BidiResolver.Resolve(text.EnumerateRunes().ToArray(), paragraphLevel: 1)
                                 .VisualString(mirror: true);
        return new string([.. visual.Where(c => !Invisible(c))]);
    }

    static bool Invisible(char c) =>
        c is (char)0x200F or (char)0x200E or (char)0x061C
          or (char)0x2066 or (char)0x2067 or (char)0x2068 or (char)0x2069;

    static string Strip(string text)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var rune in text.EnumerateRunes())
            if (rune.Value is not (0x200F or 0x200E or 0x061C or 0x2066 or 0x2067 or 0x2068 or 0x2069))
                builder.Append(rune);
        return builder.ToString();
    }
}
