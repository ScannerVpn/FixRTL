namespace RtlFix.Core.Tests;

/// <summary>
/// Lines of the shape people actually paste: Persian with a file name, a path, a clock time, a
/// Persian date, English on its own, and more than one paragraph.
/// </summary>
static class Corpus
{
    public static readonly string Headline =
        $"{Fa.Salam} {Fa.File} PowerMonitor.cs {Fa.Ra} {Fa.Baz} {Fa.Kon} ({Fa.Tavajoh})";

    public static readonly string BugInFile =
        $"{Fa.In} {Fa.Bagh} {Fa.Dar} MainWindow.xaml.cs {Fa.Ast}";

    public static readonly string Meeting =
        $"{Fa.Jalseh} {Fa.Saaat} 14:30 {Fa.Dar} Google Meet {Fa.Shoroo} {Fa.Shod}";

    public static readonly string PersianDate =
        $"{Fa.Tarikh} {Fa.Date} {Fa.Bud}";

    public static readonly string WindowsPath =
        $"{Fa.Logh} {Fa.Ra} {Fa.Az} C:\\Logs\\app.txt {Fa.Bekhan}";

    public static readonly string EnglishInMiddle =
        $"See the README {Fa.File} {Fa.Ra} {Fa.Bebin} before {Fa.Code} review";

    public static readonly string OnlyEnglish = "PowerMonitor.cs builds fine";

    public static readonly string TwoParagraphs =
        $"{Headline}\n{BugInFile}\n{OnlyEnglish}\n";

    public static readonly string[] RtlLines =
    [
        Headline, BugInFile, Meeting, PersianDate, WindowsPath, EnglishInMiddle,
    ];
}
