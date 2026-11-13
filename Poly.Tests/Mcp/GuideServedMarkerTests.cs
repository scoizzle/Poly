using System.Text.RegularExpressions;

using Poly.Mcp.Surface;
using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// The guide served through <c>poly_guide</c> carries no test-harness markers
/// (<c>&lt;!-- poly-verify: ... --&gt;</c>). The markers stay in <c>poly-dsl-guide.md</c>, where
/// <c>GuideExamplesTests</c> and <c>GuideClaimsTests</c> read them from disk. The expected text is
/// derived from <see cref="GuideBlocks"/>' own parse (the line above each marked fence), not from a
/// copy of the strip pattern.
/// </summary>
public class GuideServedMarkerTests {
    private static readonly Regex HeadingLine = new(@"^## (\d+)\. (.+)$", RegexOptions.Multiline);

    private static string FileText => GuideBlocks.ReadGuide();

    /// <summary>The 1-based line numbers of the marker lines, from the harness's own parse.</summary>
    private static HashSet<int> MarkerLines() =>
        GuideBlocks.Load().Fences
            .Where(fence => fence.Marker is not null)
            .Select(fence => fence.Line - 1)
            .ToHashSet();

    private static List<(string Number, string Title)> Headings(string file) =>
        HeadingLine.Matches(file)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value.Trim()))
            .ToList();

    /// <summary>The quickstart (null), <c>all</c>, then every heading by number and by title.</summary>
    private static List<string?> Topics(List<(string Number, string Title)> headings) {
        var topics = new List<string?> { null, "all" };
        foreach (var (number, title) in headings) {
            topics.Add(number);
            topics.Add(title);
        }
        return topics;
    }

    private static string WithoutMarkerLines(string text, IReadOnlySet<int> markerLines) =>
        string.Join("\n", text.Split('\n').Where((line, index) => !markerLines.Contains(index + 1)));

    private static string SectionOf(string text, string number) {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith($"## {number}. ", StringComparison.Ordinal));
        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));
        return string.Join("\n", lines[start..(end < 0 ? lines.Length : end)]).TrimEnd();
    }

    private static string Label(string? topic) => topic ?? "(quickstart)";

    [Test]
    public async Task Guide_EveryServedTopic_CarriesNoPolyVerifyMarkerLines() {
        var markers = MarkerLines();
        await Assert.That(markers.Count).IsGreaterThan(0);
        var headings = Headings(FileText);
        await Assert.That(headings.Count).IsGreaterThan(0);
        var topics = Topics(headings);
        await Assert.That(topics.Contains("all")).IsTrue();
        await Assert.That(topics.Contains(null)).IsTrue();

        var leaks = new List<string>();
        foreach (var topic in topics) {
            // The quickstart (null) is a constant, not file text: this loop leak-checks it, and
            // Guide_EveryServedTopic_EqualsFileWithMarkerLinesRemoved does not compare it.
            var response = PolySurfaceHarness.New().Guide(topic);
            var lines = (response.Summary + "\n" + response.Data?["guide"]?.GetValue<string>()).Split('\n');
            if (lines.Any(line => line.Contains("poly-verify", StringComparison.Ordinal)))
                leaks.Add(Label(topic));
        }

        await Assert.That(leaks).IsEmpty();
    }

    [Test]
    public async Task Guide_EveryServedTopic_EqualsFileWithMarkerLinesRemoved() {
        var markers = MarkerLines();
        await Assert.That(markers.Count).IsGreaterThan(0);
        var headings = Headings(FileText);
        await Assert.That(headings.Count).IsGreaterThan(0);
        var topics = Topics(headings);
        await Assert.That(topics.Contains("all")).IsTrue();
        await Assert.That(topics.Contains(null)).IsTrue();

        var stripped = WithoutMarkerLines(FileText, markers);
        var all = PolySurfaceHarness.New().Guide("all");
        await Assert.That(all.Data?["guide"]?.GetValue<string>()).IsEqualTo(stripped);
        await Assert.That(stripped.Split('\n').Length)
            .IsEqualTo(FileText.Split('\n').Length - markers.Count)
            .Because("topic all drops exactly the loaded marker lines and nothing else");

        var mismatches = new List<string>();
        foreach (var (number, title) in headings) {
            var expected = SectionOf(stripped, number);
            foreach (var topic in new[] { number, title }) {
                var response = PolySurfaceHarness.New().Guide(topic);
                if (!response.Success || response.Data?["guide"]?.GetValue<string>() != expected)
                    mismatches.Add(Label(topic));
            }
        }

        await Assert.That(mismatches).IsEmpty();
    }

    [Test]
    public async Task StripVerifyMarkers_MarkerLineWithLeadingWhitespace_IsRemoved() {
        const string sample = "Intro text.\n  <!-- poly-verify: valid -->\n```poly\nentity X\n```\n";

        await Assert.That(PolySurfaceTools.StripVerifyMarkers(sample))
            .IsEqualTo("Intro text.\n```poly\nentity X\n```\n");
    }

    [Test]
    public async Task StripVerifyMarkers_IndentedMarkerInACopyOfTheGuide_StripsLikeTheOriginal() {
        var markers = MarkerLines();
        var firstMarker = markers.Min();
        var indented = string.Join("\n", FileText.Split('\n')
            .Select((line, index) => index + 1 == firstMarker ? "  " + line : line));

        await Assert.That(indented.Contains("  <!-- poly-verify:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(PolySurfaceTools.StripVerifyMarkers(indented))
            .IsEqualTo(WithoutMarkerLines(FileText, markers));
    }
}