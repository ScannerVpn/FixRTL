using RtlFix.Core.Shaping;
using RtlFix.Core.Transforms;
using Xunit;

namespace RtlFix.Core.Tests.Transforms;

public class RtlTransformTest
{
    const char Rlm = '\u200F';
    const char Lri = '\u2066';
    const char Pdi = '\u2069';

    static readonly RtlOptions Repair = new() { Mode = RtlMode.Repair };
    static readonly RtlOptions Visual = new() { Mode = RtlMode.Visual };
    static readonly RtlOptions UnshapedVisual = new() { Mode = RtlMode.Visual, Shape = false };

    // ---- nothing to fix -----------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("C:\\Projects\\report.pdf")]
    [InlineData("1234 5678")]
    public void Left_to_right_text_is_returned_untouched(string text)
    {
        Assert.Equal(text, RtlTransform.Rtlize(text, Repair));
        Assert.Equal(text, RtlTransform.Rtlize(text, Visual));
    }

    [Fact]
    public void Auto_reads_the_target_renderer_to_pick_a_mode()
    {
        Assert.Equal(RtlTransform.Rtlize(Fa.Salam, Repair), RtlTransform.Rtlize(Fa.Salam));
        Assert.Equal(RtlTransform.Rtlize(Fa.Salam, Visual),
            RtlTransform.Rtlize(Fa.Salam, new RtlOptions { TargetIsBidiBlind = true }));
    }

    // ---- repair: keep logical order, pin the direction -----------------------------

    [Fact]
    public void Repair_fixes_the_paragraph_direction_without_reordering_anything()
    {
        var text = Fa.Salam + " " + Fa.Khaste;
        var result = RtlTransform.Rtlize(text, Repair);

        Assert.StartsWith(Rlm.ToString(), result);
        // Nothing else moves, so the text stays searchable, editable and reversible.
        Assert.Equal(text, new string(result.Where(c => c != Rlm).ToArray()));
    }

    [Fact]
    public void Repair_wraps_a_foreign_file_name_in_one_isolate()
    {
        var result = RtlTransform.Rtlize($"{Fa.Salam} PowerMonitor.cs {Fa.Khaste}", Repair);
        Assert.Contains($"{Lri}PowerMonitor.cs{Pdi}", result);
    }

    [Fact]
    public void Repair_keeps_the_dots_of_a_file_name_inside_the_same_island()
    {
        var result = RtlTransform.Rtlize($"{Fa.File} MainWindow.xaml.cs {Fa.Ra} {Fa.Baz} {Fa.Kon}", Repair);
        Assert.Contains($"{Lri}MainWindow.xaml.cs{Pdi}", result);
    }

    [Fact]
    public void Repair_leaves_persian_digits_to_the_paragraph_direction()
    {
        // Persian dates read right to left, so boxing them as a left-to-right island would flip them.
        var result = RtlTransform.Rtlize($"{Fa.Tarikh} {Fa.Date} {Fa.Bud}", Repair);
        Assert.DoesNotContain(Lri, result);
    }

    [Fact]
    public void Repair_protects_brackets_around_an_english_phrase()
    {
        var result = RtlTransform.Rtlize($"{Fa.NarmAfzar} (PowerMonitor release) {Fa.Tavajoh}", Repair);
        Assert.Contains($"{Lri}PowerMonitor release{Pdi}", result);
    }

    [Fact]
    public void Repair_is_repeatable()
    {
        var text = $"{Fa.NarmAfzar} FullRGB.exe {Fa.Ra} {Fa.Baz} {Fa.Kon} ({Fa.Tavajoh})";
        var once = RtlTransform.Rtlize(text, Repair);
        Assert.Equal(once, RtlTransform.Rtlize(once, Repair));
    }

    [Fact]
    public void Repair_switches_suppress_the_marks_they_control()
    {
        var bare = RtlTransform.Rtlize($"{Fa.Salam} PowerMonitor.cs",
            Repair with { ParagraphMark = false, IsolateLeftToRightRuns = false });
        Assert.Equal($"{Fa.Salam} PowerMonitor.cs", bare);
    }

    // ---- visual: draw the line for a renderer that knows no bidi -------------------

    [Fact]
    public void Visual_order_draws_a_right_to_left_word_backwards()
    {
        Assert.Equal(Fa.Drawn(Fa.Salam), RtlTransform.Rtlize(Fa.Salam, UnshapedVisual));
    }

    [Fact]
    public void Visual_order_keeps_an_english_word_readable()
    {
        Assert.Equal("world " + Fa.Drawn(Fa.Salam),
            RtlTransform.Rtlize($"{Fa.Salam} world", UnshapedVisual));
    }

    [Fact]
    public void Visual_order_reverses_word_order_across_the_whole_line()
    {
        // The author's first word lands furthest right, exactly as a right-to-left editor shows it.
        var line = $"PowerMonitor.cs {Fa.Ra} {Fa.Baz} {Fa.Kon}";
        Assert.Equal(Fa.Drawn(Fa.Kon) + " " + Fa.Drawn(Fa.Baz) + " " + Fa.Drawn(Fa.Ra) + " PowerMonitor.cs",
            RtlTransform.Rtlize(line, UnshapedVisual));
    }

    [Fact]
    public void Visual_order_moves_brackets_and_mirrors_them()
    {
        // Positions swap with the run, and rule L4 turns the glyphs back the right way round.
        Assert.Equal("(" + Fa.Drawn(Fa.Salam) + ")",
            RtlTransform.Rtlize($"({Fa.Salam})", UnshapedVisual));
    }

    [Fact]
    public void Visual_order_joins_the_letters_it_draws()
    {
        var shaped = RtlTransform.Rtlize(Fa.Salam, Visual);
        Assert.Equal(4, shaped.Length);
        Assert.All(shaped, c => Assert.True(c >= 0xFB50, $"U+{(int)c:X4} stayed unshaped"));
    }

    // ---- structure -----------------------------------------------------------------

    [Fact]
    public void Visual_order_reorders_each_line_on_its_own()
    {
        var result = RtlTransform.Rtlize($"{Fa.Salam} world\n{Fa.Khaste} {Fa.Salam}", UnshapedVisual);
        var lines = result.Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal("world " + Fa.Drawn(Fa.Salam), lines[0]);
        Assert.Equal(Fa.Drawn(Fa.Salam) + " " + Fa.Drawn(Fa.Khaste), lines[1]);
    }

    [Fact]
    public void Both_modes_keep_the_line_structure()
    {
        var text = $"first\n{Fa.Salam}\nsecond world";
        foreach (var options in new[] { Repair, Visual, UnshapedVisual })
        {
            var result = RtlTransform.Rtlize(text, options);
            Assert.Equal(3, result.Split('\n').Length);
            Assert.StartsWith("first\n", result);
            Assert.EndsWith("world", result);
        }
    }

    [Fact]
    public void Command_lines_preserve_command_and_flags_at_start()
    {
        var cmd = $"git commit -m \"{Fa.Salam}\"";
        var result = RtlTransform.Rtlize(cmd, Visual);

        Assert.StartsWith("git commit -m \"", result);
        Assert.EndsWith("\"", result);
    }

    [Fact]
    public void Echo_command_preserves_echo_prefix()
    {
        var cmd = $"echo {Fa.Salam}";
        var result = RtlTransform.Rtlize(cmd, Visual);

        Assert.StartsWith("echo ", result);
    }

    [Fact]
    public void Only_the_paragraph_with_persian_in_it_is_touched()
    {
        Assert.Equal($"hello\n{Rlm}{Fa.Salam}", RtlTransform.Rtlize($"hello\n{Fa.Salam}", Repair));
    }

    // ---- salvaging rows an earlier pass already reordered --------------------------

    [Fact]
    public void Logical_from_visual_inverts_the_visual_reordering()
    {
        var options = new RtlOptions { TargetIsBidiBlind = true };
        var sentence = $"{Fa.Salam} {Fa.Ra} {Fa.File} RtlReader.cs";

        // The row an earlier pass wrote is shaped and drawn in visual order; inverting it must
        // give back the same logical line, still shaped (the invert step only reorders).
        var roundTrip = RtlTransform.LogicalFromVisual(RtlTransform.Rtlize(sentence, options));

        Assert.Equal(PersianShaper.Shape(sentence), roundTrip);
    }

    [Fact]
    public void Logical_from_visual_keeps_latin_tokens_intact()
    {
        // A row mangled by the old pass: Persian in visual order, English tokens as printed.
        var wrecked = $"RtlTransformTest \u0631\u062F \u0644\u06CC\u0627\u0641";
        var logical = RtlTransform.LogicalFromVisual(wrecked);

        Assert.Equal($"{Fa.File} \u062F\u0631 RtlTransformTest", logical);
    }
}
