using System.Text.RegularExpressions;

using Poly.Compile;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-05 corpus sweep (lock #679 6841065713, amendment 8): every shipped document is analysed through the
/// <c>poly_define</c> path (<see cref="GuideBlocks.Analyze"/>) and its verdict is pinned, so a change to what
/// analysis accepts cannot move a shipped document unseen. The corpus is every <c>*.poly</c> file in the repo
/// (packs, demos, goldens, dogfood and probe documents), every <c>```poly</c> fence of the DSL guide (composed
/// through its marker, as <c>GuideExamplesTests</c> does), the demo IDE samples, and the quickstart document
/// <c>poly_guide</c> serves. A verdict is the error codes with their counts, in code order, or <c>ok</c>.
/// Recorded on master before T-05a's production change.
/// </summary>
public class CorpusVerdictSweepTests {
    private static string Verdict(CompileResult result) {
        var codes = result.Diagnostics
            .Where(d => d.IsError)
            .GroupBy(d => d.Code ?? "(no code)", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Count() == 1 ? g.Key : $"{g.Key}x{g.Count()}")
            .ToArray();
        return codes.Length == 0 ? "ok" : string.Join("+", codes);
    }

    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".git", "node_modules", "TestResults"];

    /// <summary>Every <c>*.poly</c> under the repo root outside build output, repo-relative, in ordinal order.</summary>
    internal static IReadOnlyList<string> PolyFiles() {
        var root = GuideBlocks.RepoRoot();
        return [.. Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Where(p => !p.Split('/').Any(segment => ExcludedDirectories.Contains(segment, StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)];
    }

    private static readonly Regex RawStringConstant = new(
        "public const string (?<name>\\w+) = \"\"\"\\n(?<body>.*?)\\n(?<indent>[ ]*)\"\"\";",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The demo IDE samples, read from source: Poly.Tests does not reference the demo project.</summary>
    internal static IReadOnlyList<(string Name, string Text)> DemoSamples() {
        var path = Path.Combine(GuideBlocks.RepoRoot(), "Poly.Demo", "Poly.Demo.Shared", "Services", "PolyIdeSamples.cs");
        var source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        return [.. RawStringConstant.Matches(source).Select(m => (m.Groups["name"].Value, Dedent(m)))];
    }

    private static string Dedent(Match match) {
        var indent = match.Groups["indent"].Value.Length;
        return string.Join('\n', match.Groups["body"].Value.Split('\n')
            .Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
    }

    /// <summary>The document inside <c>poly_guide</c>'s quickstart: from its <c>domain</c> line to the first blank line.</summary>
    internal static string QuickstartDocument() {
        var path = Path.Combine(GuideBlocks.RepoRoot(), "Poly.Mcp", "Surface", "PolySurfaceTools.cs");
        var lines = File.ReadAllLines(path);
        var start = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s*domain Shop\s*$"));
        if (start < 0) throw new InvalidOperationException("The quickstart has no 'domain Shop' document.");
        var indent = lines[start].Length - lines[start].TrimStart().Length;
        var body = lines.Skip(start).TakeWhile(l => l.Trim().Length > 0)
            .Select(l => l.Length >= indent ? l[indent..] : l.TrimStart());
        return string.Join('\n', body);
    }

    internal static string SweepRows() {
        var root = GuideBlocks.RepoRoot();
        var lines = new List<string> { "document = verdict" };
        foreach (var file in PolyFiles())
            lines.Add($"{file} = {Verdict(GuideBlocks.Analyze(File.ReadAllText(Path.Combine(root, file))))}");

        var guide = GuideBlocks.Load();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var block in guide.Blocks) {
            var occurrence = seen[block.FirstLine] = seen.GetValueOrDefault(block.FirstLine) + 1;
            var label = occurrence == 1 ? $"guide fence {block.FirstLine}" : $"guide fence {block.FirstLine} ({occurrence})";
            var verdict = block.Marker is { Kind: GuideMarkerKind.Skip, Code: null }
                ? "skip (illustration)"
                : Verdict(GuideBlocks.Compose(block, guide.Wrappers).Result);
            lines.Add($"{label} = {verdict}");
        }

        foreach (var (name, text) in DemoSamples())
            lines.Add($"demo sample {name} = {Verdict(GuideBlocks.Analyze(text))}");
        lines.Add($"poly_guide quickstart = {Verdict(GuideBlocks.Analyze(QuickstartDocument()))}");
        return string.Join('\n', lines);
    }

    [Test]
    public async Task EveryShippedDocument_KeepsItsVerdict() =>
        await Assert.That(SweepRows()).IsEqualTo(Expected.Sweep);

    private static class Expected {
        public const string Sweep = """
document = verdict
demo/Poly.RestApi/library.poly = ok
demo/live/checkout.poly = ok
demo/live/domain.poly = ok
docs/missions/evolve-2026-10-13/ambig-backref.poly = DMEFF011x2+DMREL005
docs/missions/evolve-2026-10-13/billing.poly = DMEFF011+DMLIB001
docs/missions/evolve-2026-10-13/claims.poly = DMEFF011x2+DMLIB001
docs/missions/evolve-2026-10-13/disputes.poly = DMSTR006
docs/missions/evolve-2026-10-13/library.poly = DMEFF011+DMLIB001x2
docs/missions/evolve-2026-10-13/saas.poly = ok
docs/missions/evolve-2026-10-13/smarthome.poly = ok
docs/missions/evolve-2026-10-13/university-http.poly = DMEFF011x2+DMLIB001x3
docs/missions/evolve-2026-10-13/university.poly = DMEFF011x2+DMLIB001x3
docs/missions/evolve-2026-10-14/cart.poly = ok
docs/missions/evolve-2026-10-14/durtype.poly = ok
docs/missions/evolve-2026-10-14/jobqueue.poly = ok
docs/missions/evolve-2026-10-14/notifications.poly = ok
docs/missions/evolve-2026-10-14/ownedunlink.poly = ok
docs/missions/evolve-2026-10-14/ratelimit.poly = DMLIB001
docs/missions/evolve-2026-10-14/sessions.poly = DMLIB001
docs/probes/dogfood/crm.poly = ok
docs/probes/dogfood/entry-transition-in-first-stage.poly = ok
docs/probes/dogfood/hotel.poly = ok
docs/probes/dogfood/nested-invoke-type-mismatch.poly = DMSEM003
docs/probes/dogfood/peer-tracking-transition.poly = ok
docs/probes/dogfood/simulate-create-create-in.poly = ok
docs/probes/dogfood/simulate-create-in.poly = ok
docs/probes/dogfood/simulate-create-type.poly = ok
docs/probes/dogfood/university.poly = ok
docs/probes/fleet-eval/09-transport/clinic.poly = ok
docs/probes/fleet-eval/09-transport/orders.poly = ok
docs/probes/fleet-eval/09-transport/warehouse.poly = ok
docs/probes/fleet-eval/12-mcp/mcp-library.poly = ok
docs/probes/grep-2026-10-17/grep.poly = ok
docs/probes/scratch-2026-10-14/banking.poly = ok
docs/probes/scratch-2026-10-14/fleet.poly = ok
docs/probes/scratch-2026-10-14/gym.poly = ok
docs/probes/scratch-2026-10-14/insurance.poly = ok
docs/probes/scratch-2026-10-14/logistics.poly = ok
docs/probes/scratch-2026-10-14/payroll.poly = ok
docs/probes/scratch-2026-10-14/restaurant.poly = DMEFF011x2+DMSTR006
docs/probes/scratch-2026-10-14/roastery.poly = ok
docs/probes/smoke/smoke.poly = ok
guide fence // ✅ Correct — Loan references Book through a navigation property = skip (illustration)
guide fence // ✅ Correct — Loan carries the lifecycle = skip (illustration)
guide fence Patron: entity { = ok
guide fence // ✅ Correct — both props provided (a property is required by default) = ok
guide fence // ✅ Correct — Patron subscribes to its own loans = skip (illustration)
guide fence // ✅ Correct — clear, testable guards = skip (illustration)
guide fence Suspended: stage { = skip (illustration)
guide fence domain MyDomain = ok
guide fence Color: enum { = ok
guide fence Money: value { = DMSTR006
guide fence Billing: contract internal billing v1 { = DMEFF001
guide fence domain Shop = ok
guide fence Color: enum { Red, Green, Blue } = ok
guide fence Token: entity { = ok
guide fence Item: entity table("INVENTORY") { = ok
guide fence Color: enum iri("https://example.com/Color") { = ok
guide fence Orders: entity { = DSL0001
guide fence Customer: entity { = DMSTR010
guide fence Cabin: entity { = DMDEP002
guide fence OrderLine: entity { = ok
guide fence Department: entity { = ok
guide fence Draft: stage { = ok
guide fence HasNote: policy { Note is not null } = ok
guide fence Active: stage { = ok
guide fence domain Shipping = DMEFF017
guide fence domain Tokens = DMEFF019
guide fence domain Orders = ok
guide fence Tag: action (value: Text) { = ok
guide fence PlaceOrder: action -> Order { = ok
guide fence // ✅ Correct — create is the final statement = ok
guide fence // Not product: -> Number from assign alone (analysis error) = skip (illustration)
guide fence PlaceOrder: action { = ok
guide fence invoke Validate                              # self-only = DSL0001
guide fence PositiveTotal: policy { Total > 0 } = ok
guide fence Pending: stage { = ok
guide fence // Notification-only — peer values not needed = skip (illustration)
guide fence IsAdult: policy { Age >= 18 } = ok
guide fence Age >= 18 = skip (illustration)
guide fence assignee Active = skip (illustration)
guide fence HasPriorityOrder: policy { any orders where Priority > 5 } = ok
guide fence Renew: action { = DMSEM003
guide fence domain Tasks = ok
guide fence domain Board = DMEFF020
guide fence function Add(a: Number, b: Number): Number = a + b = ok
guide fence Cart: entity { = ok
guide fence SetName: action (newName: Text) { = ok
guide fence domain Orders (2) = ok
demo sample Catalog = ok
demo sample Orders = DMLIB001
demo sample Smoke = ok
poly_guide quickstart = ok
""";
    }
}