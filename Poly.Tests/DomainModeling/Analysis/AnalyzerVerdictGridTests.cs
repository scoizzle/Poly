namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// The domain analyzer's acceptance surface: every grid in <see cref="StrictGateCensus.Grids"/> (expression forms by
/// consuming position, name-by-name and name-by-literal assignment, invoke arguments, temporal and sum operands, match
/// arms, the residual and dead forms) is compared cell by cell with <c>AnalyzerVerdictGrids.txt</c>, which records each
/// cell's full set of error codes. A cell that moves fails here naming it; an intended move regenerates the file with
/// <c>POLY_REGEN_VERDICT_GRIDS=1</c> and the diff is the review. #965 C4 replaced the T-04/T-05 "master plus the flips"
/// pins with this file: its first contents are exactly what those pins expected.
/// </summary>
public class AnalyzerVerdictGridTests {
    private const string FilePath = "Poly.Tests/DomainModeling/Analysis/AnalyzerVerdictGrids.txt";

    public static IEnumerable<string> GridNames() => StrictGateCensus.Grids().Select(static g => g.Name);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Recorded = new(static () => {
        var grids = new Dictionary<string, string>(StringComparer.Ordinal);
        string? name = null;
        var rows = new List<string>();
        foreach (var line in File.ReadAllLines(Path.Combine(Poly.Tests.Docs.GuideBlocks.RepoRoot(), FilePath))) {
            if (line.StartsWith("## ", StringComparison.Ordinal)) {
                if (name is not null)
                    grids.Add(name, string.Join('\n', rows));
                name = line[3..];
                rows.Clear();
            }
            else if (name is not null) {
                rows.Add(line);
            }
        }
        if (name is not null)
            grids.Add(name, string.Join('\n', rows));
        return grids;
    });

    [Test]
    [MethodDataSource(nameof(GridNames))]
    public async Task Grid_IsTheRecordedVerdicts(string grid) {
        var rows = StrictGateCensus.Grids().Single(g => g.Name == grid).Rows();
        await Assert.That(Recorded.Value.TryGetValue(grid, out var recorded)).IsTrue().Because($"grid '{grid}' has no section in {FilePath}");
        await Assert.That(Differences(rows, recorded!)).IsEqualTo("");
    }

    [Test]
    public async Task EveryRecordedGrid_IsAGridThatRuns() =>
        await Assert.That(Recorded.Value.Keys.Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(GridNames().Order(StringComparer.Ordinal).ToArray());

    [Test]
    public async Task Regenerate_WritesTheVerdictGrids() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_VERDICT_GRIDS") is null)
            return;
        var lines = new List<string>();
        foreach (var (name, rows) in StrictGateCensus.Grids()) {
            lines.Add($"## {name}");
            lines.AddRange(rows().Split('\n'));
        }
        File.WriteAllLines(Path.Combine(Poly.Tests.Docs.GuideBlocks.RepoRoot(), FilePath), lines);
        await Assert.That(lines.Count).IsGreaterThan(100);
    }

    /// <summary>The rows where <paramref name="actual"/> and <paramref name="recorded"/> differ, one per line.</summary>
    private static string Differences(string actual, string recorded) {
        var a = actual.Split('\n');
        var r = recorded.Split('\n');
        if (a.Length != r.Length)
            return $"row count {a.Length}, recorded {r.Length}";
        return string.Join('\n', a.Zip(r).Where(static p => p.First != p.Second)
            .Select(static p => $"actual '{p.First}', recorded '{p.Second}'"));
    }
}