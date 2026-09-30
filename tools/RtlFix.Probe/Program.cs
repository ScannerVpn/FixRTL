using System.Net;
using System.Text;
using RtlFix.Core.Bidi;
using RtlFix.Core.Transforms;

// Reads one text per input line and prints, escaped so no console can reorder it: the resolved
// embedding levels, the visual order, the repaired text, and the visually reordered text.
// With --html it writes a page instead, so a browser's own bidi engine can judge the result.

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var repair = new RtlOptions { Mode = RtlMode.Repair };
var visual = new RtlOptions { Mode = RtlMode.Visual };

var lines = new List<string>();
for (var line = Console.ReadLine(); line is not null; line = Console.ReadLine())
    lines.Add(line);

if (args.Contains("--target"))
{
    // Which program the tray app would be looking at, and what it would decide about it.
    Console.WriteLine(ForegroundReport());
    return;
}

if (args.Contains("--html"))
{
    Console.Out.Write(Page(lines, repair, visual));
    return;
}

foreach (var line in lines)
{
    var paragraphs = BidiAnalyzer.Analyze(line);
    var levels = new List<string>();
    var reordered = new StringBuilder();
    for (var p = 0; p < paragraphs.Count; p++)
    {
        if (p > 0) levels.Add("B");
        levels.AddRange(paragraphs[p].Levels.Select(l => l.ToString()));
        reordered.Append(paragraphs[p].VisualString());
    }

    Console.WriteLine(string.Join(" | ",
        [Escape(string.Join("", levels)), Escape(reordered.ToString()),
         Escape(RtlTransform.Rtlize(line, repair)), Escape(RtlTransform.Rtlize(line, visual))]));
}

static string Page(List<string> lines, RtlOptions repair, RtlOptions visual)
{
    var html = new StringBuilder();
    foreach (var line in lines)
    {
        var fixed_ = RtlTransform.Rtlize(line, repair);
        var drawn = RtlTransform.Rtlize(line, visual);
        html.Append($"""
              <tr>
                <th>{WebUtility.HtmlEncode(line)}</th>
                {Cell(line, "bad")}
                {Cell(line, "good rtl")}
                {Cell(fixed_, "good")}
                {Marks(fixed_)}
                {Cell(drawn, "good")}
              </tr>

            """);
    }

    return $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>RtlFix — sample check</title>
        <style>
          :root { color-scheme: dark; }
          body { margin: 0; padding: 3rem 2rem 6rem; font: 15px/1.5 "Segoe UI", system-ui, sans-serif;
                 background: radial-gradient(120% 80% at 20% 0%, #1b2340 0%, #0a0d18 55%, #070910 100%);
                 color: #e8ecf8; min-height: 100vh; }
          h1 { font-size: 1.6rem; font-weight: 600; letter-spacing: .02em; margin: 0 0 .4rem;
               background: linear-gradient(90deg, #9fd8ff, #d7b3ff 60%, #9fd8ff);
               -webkit-background-clip: text; background-clip: text; color: transparent; }
          p.lede { margin: 0 0 2rem; color: #9aa6c8; max-width: 60rem; }
          table { border-collapse: separate; border-spacing: 0 6px; width: 100%; }
          th, td { vertical-align: top; padding: .7rem .9rem; background: #111726;
                   border: 1px solid #232c46; font-size: 1.05rem; }
          thead th { background: transparent; border: 0; font: 600 12px/1.3 "Segoe UI", sans-serif;
                     letter-spacing: .08em; text-transform: uppercase; color: #8fa0c8; text-align: left; }
          tbody th { width: 22%; color: #b9c6e6; font-weight: 500; border-left: 3px solid #3f6fff; }
          td.good { border-left: 3px solid #2fbf71; }
          td.bad { border-left: 3px solid #ff5d5d; }
          td { direction: ltr; text-align: left; }
          td.rtl { direction: rtl; text-align: right; }
          .mark { color: #ffd166; background: #2a2410; border-radius: 4px; padding: 0 .25em;
                  font: 11px/1.6 "Cascadia Mono", Consolas, monospace; }
          .code { font: 12px/1.6 "Cascadia Mono", Consolas, monospace; color: #7f8db3;
                  white-space: pre-wrap; word-break: break-all; }
          footer { margin-top: 2.5rem; color: #67749a; font-size: 13px; max-width: 60rem; }
        </style>
        </head>
        <body>
          <h1>RtlFix — does it read correctly now?</h1>
          <p class=lede>Every pane is rendered by the browser's own Unicode bidi engine, so nothing here is
             simulated — the marks are worth nothing unless the browser draws column 4 like column 3.</p>
          <table>
            <thead>
              <tr>
                <th>sample</th>
                <th>as pasted, app assumes left-to-right</th>
                <th>reference: the text in a right-to-left paragraph</th>
                <th>repaired with marks</th>
                <th>the marks, shown</th>
                <th>visual order for a bidi-blind app</th>
              </tr>
            </thead>
            <tbody>
        {{html}}
            </tbody>
          </table>
          <footer>Column 4 should look identical to column 3: the invisible marks alone make an ordinary
            left-to-right app render the line correctly. A line that <em>starts</em> with English keeps its
            left-to-right order on purpose — the marks protect the Persian runs inside it instead of
            flipping the whole line. Column 6 is what a renderer that ignores bidi altogether draws,
            letter by letter, from left to right.</footer>
        </body>
        </html>
        """;
}

static string Cell(string text, string className) =>
    $"""<td class="{className}">{WebUtility.HtmlEncode(text)}</td>""";

/// <summary>A cell of the same text with every invisible directional mark named.</summary>
static string Marks(string text)
{
    var builder = new StringBuilder("<td class=\"code\">");
    foreach (var rune in text.EnumerateRunes())
        builder.Append(rune.Value switch
        {
            0x200F => "<span class=mark>[RLM]</span>",
            0x200E => "<span class=mark>[LRM]</span>",
            0x061C => "<span class=mark>[ALM]</span>",
            0x2066 => "<span class=mark>[LRI]</span>",
            0x2067 => "<span class=mark>[RLI]</span>",
            0x2068 => "<span class=mark>[FSI]</span>",
            0x2069 => "<span class=mark>[PDI]</span>",
            _ => WebUtility.HtmlEncode(rune.ToString()),
        });
    return builder.Append("</td>").ToString();
}

static string Escape(string text)
{
    var builder = new StringBuilder();
    foreach (var rune in text.EnumerateRunes())
        builder.Append(rune.Value > 127 ? $"U+{rune.Value:X4} " : rune.ToString());
    return builder.ToString().Trim();
}

// ---- --target: what the tray app sees when it looks at the focused window

static string ForegroundReport()
{
    GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
    if (pid == 0) return "no foreground window";

    var name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
    var file = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RtlFix", "settings.json");
    var list = new List<string>();
    if (System.IO.File.Exists(file))
    {
        var root = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file)).RootElement;
        if (root.TryGetProperty("BidiBlindApps", out var apps))
            list.AddRange(apps.EnumerateArray().Select(a => a.GetString() ?? ""));
    }

    var blind = list.Contains(name, StringComparer.OrdinalIgnoreCase);
    return $"foreground: {name} (pid {pid}) | treated as ignoring bidi: {blind} | list: {string.Join(", ", list)}";
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern IntPtr GetForegroundWindow();

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
