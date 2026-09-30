using System.Text;

namespace RtlFix.App;

/// <summary>
/// Persian samples for the preview, written as code points: a right-to-left literal in a source file
/// is unreadable in an editor and easy to corrupt without noticing.
/// </summary>
static class Sample
{
    public static readonly string Salam = Of(0x0633, 0x0644, 0x0627, 0x0645);
    public static readonly string File = Of(0x0641, 0x0627, 0x06CC, 0x0644);
    public static readonly string Ra = Of(0x0631, 0x0627);
    public static readonly string Baz = Of(0x0628, 0x0627, 0x0632);
    public static readonly string Kon = Of(0x06A9, 0x0646);
    public static readonly string Tavajoh = Of(0x062A, 0x0648, 0x062C, 0x0647);

    /// <summary>The kind of line that gets scrambled: Persian, a file name, and brackets.</summary>
    public static readonly string MixedLine = $"{Salam} {File} PowerMonitor.cs {Ra} {Baz} {Kon} ({Tavajoh})";

    public static string Of(params int[] codePoints)
    {
        var builder = new StringBuilder();
        foreach (var cp in codePoints)
            builder.Append(char.ConvertFromUtf32(cp));
        return builder.ToString();
    }
}
