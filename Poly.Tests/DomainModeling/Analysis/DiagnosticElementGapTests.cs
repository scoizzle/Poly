using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Http;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Sqlite;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// A4 gap check: every diagnostic from the analyzer fixtures must point at a
/// <see cref="DomainObject"/>. Codes that are allowed not to are listed here by
/// name; an empty list means none. A silent skip of an unlisted code is a failure.
/// </summary>
public sealed class DiagnosticElementGapTests {
    /// <summary>
    /// Codes allowed to attach to a non-<see cref="DomainObject"/> node. Keep short
    /// and explicit; add a code here only with a reason in the PR body.
    /// </summary>
    private static readonly HashSet<string> CodesAllowedWithoutDomainElement = new(StringComparer.Ordinal) {
        // Empty on purpose: every fixture diagnostic today points at a DomainObject.
    };

    private static readonly ExtensionCatalog Catalog = ExtensionCatalog.Core
        .With(new SqliteLibrary())
        .With(new HttpLibrary());

    private static readonly string[] Seed = [.. ExtensionCatalog.ProductAuthoring, "sqlite"];

    // Live probes and demo — same roots CascadingErrorReportTests uses.
    private static readonly string[] SampleRoots = ["docs/probes", "demo/live"];
    private static readonly string[] InvalidProbes = ["nested-invoke-type-mismatch.poly"];

    // Extra analyzer fixtures: small domains that force Error/Warning diagnostics the
    // clean samples do not always produce.
    private static readonly string[] ExtraFixtures = [
        """
        domain GapDup
        A: entity { X: Text }
        A: entity { Y: Text }
        """,
        """
        domain GapStage
        A: entity {
          Open: stage { Go: action { transition to Missing } }
        }
        """,
        """
        domain GapOrphan
        Child: entity {
          Name: Text
          Open: stage { }
        }
        """,
        """
        domain GapTypes
        A: entity {
          N: Number
          Open: stage {
            Go: action { assign N to "text" }
          }
        }
        """,
    ];

    [Test]
    public async Task EveryDiagnostic_CarriesADomainElement_ExceptListedCodes() {
        var gaps = new List<string>();
        var seen = 0;

        foreach (var domain in FixtureDomains()) {
            var analysis = DomainModelAnalyzer.Analyze(domain);
            foreach (var d in analysis.Diagnostics) {
                seen++;
                if (d.Code is not null && CodesAllowedWithoutDomainElement.Contains(d.Code))
                    continue;
                if (d.Node is DomainObject)
                    continue;
                gaps.Add(
                    $"{d.Code ?? "(null)"} on {d.Node?.GetType().FullName ?? "null"} — {d.Message}");
            }
        }

        await Assert.That(seen).IsGreaterThan(0);
        await Assert.That(gaps).IsEmpty();
    }

    [Test]
    public async Task ExceptionList_OnlyContainsKnownCodes() {
        // Guard against typos in the allow-list: every entry must be a real product code.
        var known = typeof(DomainModelDiagnosticCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = CodesAllowedWithoutDomainElement.Where(c => !known.Contains(c)).ToList();
        await Assert.That(unknown).IsEmpty();
    }

    private static IEnumerable<Domain> FixtureDomains() {
        foreach (var relative in SampleDomainPaths()) {
            var poly = File.ReadAllText(Path.Combine(FindRepoRoot(), relative));
            if (TryEvolve(poly, out var domain) && domain is not null)
                yield return domain;
        }
        foreach (var poly in ExtraFixtures) {
            if (TryEvolve(poly, out var domain) && domain is not null)
                yield return domain;
        }
    }

    private static bool TryEvolve(string poly, out Domain? domain) {
        domain = null;
        try {
            var session = DomainSession.ForSource(poly, Seed, Catalog);
            var changes = DomainCompilation.WithSeed(new PolyDslParser(poly, session).Parse(), Seed);
            var outcome = new DomainEvolution(new Domain("_", [])).Apply(changes, session: session);
            // Even when evolution reports Errors, the analysis (and its diagnostics) is what we check.
            if (outcome.Root is null)
                return false;
            domain = outcome.Root;
            return true;
        }
        catch {
            return false;
        }
    }

    private static IEnumerable<string> SampleDomainPaths() {
        var root = FindRepoRoot();
        return SampleRoots
            .Select(r => Path.Combine(root, r))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.poly", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(p => !InvalidProbes.Contains(Path.GetFileName(p)))
            .Order(StringComparer.Ordinal);
    }

    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            if (File.Exists(Path.Combine(dir, "docs/CORE.md")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root from " + AppContext.BaseDirectory);
    }
}