using Poly.DomainModeling.Libraries.Http;
using Poly.Tests.Polyc;

using CompileMode = Poly.DslCompiler.CompileMode;
using Compiler = Poly.DslCompiler.DslCompiler;
using DbmsPack = Poly.DslCompiler.DbmsPack;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// #857: every <c>*.poly</c> shipped under <c>docs/</c> compiles through the same HTTP-host path a
/// shipped example is read with, <c>Compiler().Load(HttpLibrary).Compile(All, Sqlite)</c>. A document
/// that does not compile fails here by name, instead of being pinned as a failing verdict in a corpus
/// sweep. <see cref="Excluded"/> is the only escape hatch, and each entry names why that document is a
/// deliberate fixture rather than an example.
/// </summary>
public class ShippedDocsCompileTests {
    /// <summary>
    /// Documents that must not compile, keyed by their repo-relative path, each with the reason it is
    /// excluded. Every one is a historical probe record: it reproduces the pre-fix behaviour named in its
    /// mission's findings, so its analysis verdict is already pinned by
    /// <c>CorpusVerdictSweepTests</c>. A document that starts to compile fails this test until its entry
    /// leaves this list.
    /// </summary>
    private static readonly Dictionary<string, string> Excluded = new(StringComparer.Ordinal) {
        ["docs/missions/evolve-2026-10-13/ambig-backref.poly"] =
            "historical probe: records the ambiguous back-reference shape from the 2026-10-13 mission",
        ["docs/missions/evolve-2026-10-13/billing.poly"] =
            "historical probe: reproduces the Number truncation (F15) from the 2026-10-13 mission",
        ["docs/missions/evolve-2026-10-13/claims.poly"] =
            "historical probe: reproduces the C# keyword export failure (F19) from the 2026-10-13 mission",
        ["docs/missions/evolve-2026-10-13/disputes.poly"] =
            "historical probe: reproduces the value-type storage failure (F20-F22) from the 2026-10-13 mission",
        ["docs/missions/evolve-2026-10-13/university-http.poly"] =
            "historical probe: reproduces the Program-entity HTTP failure (F11) from the 2026-10-13 mission",
        ["docs/missions/evolve-2026-10-13/university.poly"] =
            "historical probe: reproduces the second-unique-dropped failure (F10) from the 2026-10-13 mission",
        ["docs/probes/dogfood/nested-invoke-type-mismatch.poly"] =
            "invalid by design: the nested-invoke type mismatch (DMSEM003) is the repro this fixture records",
        ["docs/probes/scratch-2026-10-14/restaurant.poly"] =
            "historical probe: records a domain value type stored as an entity property (DMEFF011+DMSTR006)",
    };

    private static Compiler CompilerWithHttpHost() => new Compiler().Load(new HttpLibrary());

    private static string Compile(string poly) {
        var result = CompilerWithHttpHost().Compile(poly, CompileMode.All, DbmsPack.Sqlite);
        return result.Success ? "" : string.Join(" | ", result.Errors ?? []);
    }

    private static IReadOnlyList<string> DocsDocuments() {
        var root = Path.Combine(PolycTestSupport.RepoRoot(), "docs");
        return [.. Directory.EnumerateFiles(root, "*.poly", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(PolycTestSupport.RepoRoot(), p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    [Test]
    public async Task EveryDocsDocument_Compiles() {
        var failures = new List<string>();
        var documents = DocsDocuments();
        await Assert.That(documents.Count).IsGreaterThan(0);

        foreach (var path in documents) {
            if (Excluded.ContainsKey(path))
                continue;
            var errors = Compile(File.ReadAllText(Path.Combine(PolycTestSupport.RepoRoot(), path)));
            if (errors.Length != 0)
                failures.Add($"{path}: {errors}");
        }

        await Assert.That(failures).IsEmpty();
    }

    /// <summary>A document that starts to compile is no longer a fixture, so its exclusion is stale.</summary>
    [Test]
    public async Task EveryExcludedDocument_StillDoesNotCompile() {
        var documents = DocsDocuments();
        var stale = new List<string>();
        foreach (var (path, reason) in Excluded) {
            if (!documents.Contains(path, StringComparer.Ordinal)) {
                stale.Add($"{path}: not shipped under docs/ (reason: {reason})");
                continue;
            }
            if (Compile(File.ReadAllText(Path.Combine(PolycTestSupport.RepoRoot(), path))).Length == 0)
                stale.Add($"{path}: now compiles; remove it from Excluded (reason: {reason})");
        }

        await Assert.That(stale).IsEmpty();
    }
}