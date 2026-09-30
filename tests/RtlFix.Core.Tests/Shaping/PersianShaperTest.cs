using System.Text;
using RtlFix.Core.Shaping;
using Xunit;

namespace RtlFix.Core.Tests.Shaping;

/// <summary>
/// Golden presentation-form mappings. Within each letter's block the forms run isolated, final,
/// initial, medial (U+FE8D/FE8E are the alef isolated and final forms, U+FEDD..FEE0 the four lam
/// forms), so an off-by-one in the generated table would show up immediately here.
/// </summary>
public class PersianShaperTest
{
    static readonly string Salam = Cp(0x0633, 0x0644, 0x0627, 0x0645);
    static readonly string Rood = Cp(0x0645, 0x06CC, 0x0631, 0x0648, 0x062F);

    [Fact]
    public void Letters_that_do_not_join_pass_through_unchanged()
    {
        Assert.Equal("hello world 1234", PersianShaper.Shape("hello world 1234"));
    }

    [Fact]
    public void A_connected_word_takes_contextual_forms()
    {
        // Seen-lam-alef-meem: lam sits between two joiners so it goes medial, while the final meem
        // has nothing to its left and stands alone.
        Assert.Equal(Cp(0xFEB3, 0xFEE0, 0xFE8E, 0xFEE1), PersianShaper.Shape(Salam));
    }

    [Fact]
    public void An_alef_stops_the_chain_behind_it()
    {
        // Beh joins onwards to the alef, but an alef never joins onwards, so teh stands alone.
        Assert.Equal(Cp(0xFE91, 0xFE8E, 0xFE95), PersianShaper.Shape(Cp(0x0628, 0x0627, 0x062A)));
    }

    [Fact]
    public void A_half_space_splits_one_word_into_two()
    {
        var joined = PersianShaper.Shape(Rood);
        var halfSpaced = PersianShaper.Shape(Rood[..2] + (char)0x200C + Rood[2..]);

        // The ZWNJ disappears: the forms either side of it already say the letters do not connect.
        Assert.Equal(Rood.Length, halfSpaced.Length);
        // Rehe drops from final, joined to the yeh, to isolated.
        Assert.NotEqual(joined[2], halfSpaced[2]);
        Assert.Equal(Cp(0xFEE3, 0xFBFD, 0xFEAD, 0xFEED, 0xFEA9), halfSpaced);
    }

    [Fact]
    public void A_joiner_is_consumed_without_changing_the_forms_it_implies()
    {
        Assert.Equal(Cp(0xFEDF, 0xFE8E), PersianShaper.Shape(Cp(0x0644, 0x200D, 0x0627)));
    }

    [Fact]
    public void A_vowel_mark_does_not_break_a_connection()
    {
        var marked = PersianShaper.Shape(Cp(0x0628, 0x064E, 0x06CC, 0x062F));
        var plain = PersianShaper.Shape(Cp(0x0628, 0x06CC, 0x062F));

        // The fatha keeps its place but is transparent to joining: both letters keep their forms.
        Assert.Equal((char)0x064E, marked[1]);
        Assert.Equal(plain[0], marked[0]);
        Assert.Equal(plain[1], marked[2]);
    }

    [Fact]
    public void Every_shaped_letter_leaves_the_base_block()
    {
        var shaped = PersianShaper.Shape(Cp(0x0628, 0x0631, 0x0646, 0x0627, 0x0645, 0x0647));
        Assert.All(shaped, r => Assert.True(r >= 0xFB50, $"U+{(int)r:X4} stayed unshaped"));
    }

    static string Cp(params int[] codePoints)
    {
        var builder = new StringBuilder();
        foreach (var cp in codePoints)
            builder.Append(char.ConvertFromUtf32(cp));
        return builder.ToString();
    }
}
