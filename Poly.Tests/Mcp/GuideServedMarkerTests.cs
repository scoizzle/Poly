using System.Text.RegularExpressions;

using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// The guide served through <c>poly_guide</c> carries no test-harness markers
/// (<c>&lt;!-- poly-verify: ... --&gt;</c>). The markers stay in <c>poly-dsl-guide.md</c>, where
/// <c>GuideExamplesTests</c> and <c>GuideClaimsTests</c> read them from disk.
/// </summary>
public class GuideServedMarkerTests {
    private static readonly Regex MarkerLine = new(@"^\s*<!--\s*poly-verify:.*-->\s*$");
    private static readonly Regex Heading = new(@"^## (\d+)\. (.+)$", RegexOptions.Multiline);

    private static string FileText => File.ReadAllText(GuideBlocks.GuidePath());

    [Test]
    public async Task Guide_EveryServedTopic_CarriesNoPolyVerifyMarkerLines() {
        await Assert.That(FileText.Contains("poly-verify", StringComparison.Ordinal)).IsTrue();

        var leaks = new List<string>();
        foreach (var topic in Topics(FileText)) {
            var response = PolySurfaceHarness.New().Guide(topic);
            var lines = (response.Summary + "\n" + response.Data?["guide"]?.GetValue<string>()).Split('\n');
            if (lines.Any(line => line.Contains("poly-verify", StringComparison.Ordinal)))
                leaks.Add(Label(topic));
        }

        await Assert.That(leaks).IsEmpty();
    }

    [Test]
    public async Task Guide_EveryServedTopic_EqualsFileWithMarkerLinesRemoved() {
        var stripped = WithoutMarkerLines(FileText);
        var mismatches = new List<string>();
        foreach (var (topic, expected) in ExpectedByTopic(stripped)) {
            var response = PolySurfaceHarness.New().Guide(topic);
            if (!response.Success || response.Data?["guide"]?.GetValue<string>() != expected)
                mismatches.Add(Label(topic));
        }

        await Assert.That(mismatches).IsEmpty();
    }

    private static IEnumerable<string?> Topics(string file) {
        yield return null;
        yield return "all";
        foreach (Match match in Heading.Matches(file)) {
            yield return match.Groups[1].Value;
            yield return match.Groups[2].Value.Trim();
        }
    }

    private static IEnumerable<(string Topic, string Expected)> ExpectedByTopic(string stripped) {
        yield return ("all", stripped);
        foreach (Match match in Heading.Matches(stripped)) {
            var section = SectionOf(stripped, match.Groups[1].Value);
            yield return (match.Groups[1].Value, section);
            yield return (match.Groups[2].Value.Trim(), section);
        }
    }

    private static string SectionOf(string stripped, string number) {
        var lines = stripped.Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith($"## {number}. ", StringComparison.Ordinal));
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        return string.Join("\n", lines[start..(end < 0 ? lines.Length : end)]).TrimEnd();
    }

    private static string WithoutMarkerLines(string text) =>
        string.Join("\n", text.Split('\n').Where(line => !MarkerLine.IsMatch(line)));

    private static string Label(string? topic) => topic ?? "(quickstart)";
}