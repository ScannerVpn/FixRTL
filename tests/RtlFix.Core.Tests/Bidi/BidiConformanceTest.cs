using System.Globalization;
using System.Text;
using RtlFix.Core.Bidi;
using Xunit;
using Xunit.Abstractions;

namespace RtlFix.Core.Tests.Bidi;

/// <summary>
/// Runs the official UBA conformance suite (BidiCharacterTest.txt) against the resolver.
/// </summary>
public class BidiConformanceTest(ITestOutputHelper output)
{
    [Fact]
    public void Every_case_matches_the_reference_levels_and_visual_order()
    {
        var path = RepoFile("data", "ucd", "BidiCharacterTest.txt");
        Assert.True(File.Exists(path), $"Missing {path}: run tools/RtlFix.GenTables first.");

        var total = 0;
        var levelFailures = new List<string>();
        var orderFailures = new List<string>();

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var fields = line.Split(';');
            var runes = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(h => new Rune(int.Parse(h, NumberStyles.HexNumber))).ToArray();
            var paragraphLevel = byte.Parse(fields[2]);
            var expectedLevels = fields[3].Split(' ');
            var expectedOrder = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse).ToArray();

            var analysis = BidiResolver.Resolve(runes, paragraphLevel);
            total++;

            for (var i = 0; i < runes.Length; i++)
            {
                var expected = expectedLevels[i];
                if (expected == "x")
                {
                    if (!analysis.Removed[i])
                        levelFailures.Add($"#{total} [{Show(runes)}] at {i}: expected removed by X9");
                    break;
                }
                if (analysis.Removed[i])
                {
                    levelFailures.Add($"#{total} [{Show(runes)}] at {i}: unexpectedly removed");
                    break;
                }
                if (analysis.Levels[i] != byte.Parse(expected))
                {
                    levelFailures.Add($"#{total} [{Show(runes)}] at {i}: expected {expected}, got {analysis.Levels[i]}");
                    break;
                }
            }

            var order = analysis.VisualIndices(applyL3: false);
            if (!order.SequenceEqual(expectedOrder))
            {
                orderFailures.Add(
                    $"#{total} [{Show(runes)}] level {paragraphLevel}\n    expected {string.Join(",", expectedOrder)}\n    actual   {string.Join(",", order)}");
            }
        }

        foreach (var failure in levelFailures.Take(10)) output.WriteLine(failure);
        foreach (var failure in orderFailures.Take(10)) output.WriteLine(failure);
        output.WriteLine($"{total} cases, {levelFailures.Count} level failures, {orderFailures.Count} order failures");

        Assert.Empty(levelFailures);
        Assert.Empty(orderFailures);
    }

    static string Show(Rune[] runes) => new string(runes.Select(r => (char)r.Value).ToArray());

    static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RtlFix.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, Path.Combine(parts));
    }
}
