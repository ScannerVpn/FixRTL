using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using RtlFix.Core.Shaping;
using RtlFix.Core.Bidi;
using RtlFix.Core.Transforms;

namespace RtlFix.Core.Tests.Transforms;

public class BidiLevel0Test
{
    private readonly ITestOutputHelper output;
    public BidiLevel0Test(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TestLevel0vs1()
    {
        var sample = "   4. کارهای استارتاپ را اختیاری کنید: EnableAll فقط با دکمه کاربر (App Manager) اجرا شود؛ UAC خودکار در بوت حذف شود؛";
        var runes = sample.EnumerateRunes().ToArray();
        var shaped = PersianShaper.ShapeRunes(runes);

        var lvl0 = BidiResolver.Resolve(runes, 0).VisualString(shaped.Forms, shaped.Present, true);
        var lvl1 = BidiResolver.Resolve(runes, 1).VisualString(shaped.Forms, shaped.Present, true);

        output.WriteLine("LEVEL 0 (LTR base): " + lvl0);
        output.WriteLine("LEVEL 1 (RTL base): " + lvl1);
    }
}