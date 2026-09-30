using System.Text;

namespace RtlFix.Core.Tests;

/// <summary>
/// Persian samples written as code points: a bidirectional literal in a source file is unreadable
/// in an editor and easy to corrupt, and these strings are the fixtures every test checks.
/// </summary>
static class Fa
{
    public static readonly string Salam = Of(0x0633, 0x0644, 0x0627, 0x0645); // hello
    public static readonly string Khaste = Of(0x062E, 0x0633, 0x062A, 0x0647); // tired
    public static readonly string Ra = Of(0x0631, 0x0627); // (object marker)
    public static readonly string Baz = Of(0x0628, 0x0627, 0x0632); // open
    public static readonly string Kon = Of(0x06A9, 0x0646); // do (imperative)
    public static readonly string File = Of(0x0641, 0x0627, 0x06CC, 0x0644); // file
    public static readonly string Tarikh = Of(0x062A, 0x0627, 0x0631, 0x06CC, 0x062E); // date
    public static readonly string Bud = Of(0x0628, 0x0648, 0x062F); // was
    public static readonly string NarmAfzar = Of(0x0646, 0x0631, 0x0645, 0x200C, 0x0627, 0x0641, 0x0632, 0x0627, 0x0631); // software
    public static readonly string Tavajoh = Of(0x062A, 0x0648, 0x062C, 0x0647); // attention
    public static readonly string Date = Of(0x06F1, 0x06F3, 0x06F9, 0x06F5, 0x002F, 0x06F0, 0x06F4, 0x002F, 0x06F0, 0x06F1);

    public static readonly string In = Of(0x0627, 0x06CC, 0x0646); // this
    public static readonly string Bagh = Of(0x0628, 0x0627, 0x06AF); // bug
    public static readonly string Dar = Of(0x062F, 0x0631); // in
    public static readonly string Ast = Of(0x0627, 0x0633, 0x062A); // is
    public static readonly string Jalseh = Of(0x062C, 0x0644, 0x0633, 0x0647); // meeting
    public static readonly string Saaat = Of(0x0633, 0x0627, 0x0639, 0x062A); // hour
    public static readonly string Shoroo = Of(0x0634, 0x0631, 0x0648, 0x0639); // start
    public static readonly string Shod = Of(0x0634, 0x062F); // became
    public static readonly string Logh = Of(0x0644, 0x0627, 0x06AF); // log
    public static readonly string Az = Of(0x0627, 0x0632); // from
    public static readonly string Bekhan = Of(0x0628, 0x062E, 0x0648, 0x0627, 0x0646); // read
    public static readonly string Bebin = Of(0x0628, 0x0628, 0x06CC, 0x0646); // see
    public static readonly string Va = Of(0x0648); // and
    public static readonly string Code = Of(0x06A9, 0x062F); // code

    public static string Of(params int[] codePoints)
    {
        var builder = new StringBuilder();
        foreach (var cp in codePoints)
            builder.Append(char.ConvertFromUtf32(cp));
        return builder.ToString();
    }

    /// <summary>The code points of a right-to-left run in the order they are drawn, left to right.</summary>
    public static string Drawn(string text) => new(text.Reverse().ToArray());
}
