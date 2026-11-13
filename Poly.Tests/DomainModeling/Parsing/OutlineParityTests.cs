using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Tests.Docs;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-26b parity, part 1: for every valid shipped document, the outline's head for each declaration (kind and path) against what the
/// resolver recognises. A difference is a frozen row (<c>OutlineHeadParity.txt</c>, stale-checked both ways) and belongs to a named class
/// with an owner, so a new difference cannot hide inside the baseline. Heads inside opaque bodies (contract endpoints, subscriptions) are
/// not outline nodes and are not compared.
/// </summary>
public sealed class OutlineParityTests {
    private const string FilePath = "Poly.Tests/DomainModeling/Parsing/OutlineHeadParity.txt";

    /// <summary>The classes of known difference. An owner of <c>none</c> means no row of the programme owns it: it is reported, not tagged.</summary>
    internal static readonly (string Class, string Owner, string Why)[] DifferenceClasses = [
        ("bare-navigation", "none", "the outline reads tokens with no session, so `P: Entity` is a property; the resolver knows the entity and says navigation"),
        ("top-level-function", "G-29", "the outline has no head for `function Name(...)`: it reads the parameter `a: Number` as an unknown top-level declaration and every declaration after it is missing"),
    ];

    // ── the two sides ────────────────────────────────────────────────────────────────

    internal static List<string> ResolverHeads(string text) {
        var heads = new List<string>();
        foreach (var change in ParityDocuments.Resolve(text)) {
            switch (change) {
                case SetDomainNameChange d: heads.Add($"domain|{d.Name}"); break;
                case AddDomainExtensionChange u: heads.Add($"uses|{u.ExtensionId}"); break;
                case AddEntityChange e: heads.Add($"entity|{e.Name}"); break;
                case AddEnumTypeChange e: heads.Add($"enum|{e.Name}"); break;
                case AddValueTypeChange v: heads.Add($"value|{v.Name}"); break;
                case AddFunctionChange f: heads.Add($"function|{f.Name}"); break;
                case AddImportedContractChange c: heads.Add($"contract|{c.Name}"); break;
                case AddContractBindingChange b: heads.Add($"bind|{b.Name}"); break;
                case AddPropertyToEntityChange p: heads.Add($"property|{p.EntityName}.{p.Property.Name}"); break;
                case AddRelationshipChange r: heads.Add($"navigation|{r.Source.TypeName}.{r.Name}"); break;
                case AddStageChange s: heads.Add($"stage|{s.EntityName}.{s.Name}"); break;
                case AddActionChange a: heads.Add($"action|{a.EntityName}.{a.Name}"); break;
                case AddActionToStageChange a: heads.Add($"action|{a.EntityName}.{a.StageName}.{a.Name}"); break;
                case AddPolicyToEntityChange p: heads.Add($"policy|{p.EntityName}.{p.Policy.Name}"); break;
            }
        }

        return heads;
    }

    internal static List<string> OutlineHeads(string text) =>
        [.. DslOutline.Build(text).All().Where(n => n.Kind != "unnamed")
            .Select(n => $"{n.Kind}|{(n.Kind is "domain" or "uses" ? n.Name : n.Path)}")];

    /// <summary>The heads only one side has, sorted: <c>resolver-only</c> and <c>outline-only</c> lines. Pure.</summary>
    internal static List<string> Differences(IEnumerable<string> outline, IEnumerable<string> resolver) {
        var o = outline.ToList();
        var r = resolver.ToList();
        return [
            .. r.Except(o).Select(h => "resolver-only\t" + h).OrderBy(h => h, StringComparer.Ordinal),
            .. o.Except(r).Select(h => "outline-only\t" + h).OrderBy(h => h, StringComparer.Ordinal),
        ];
    }

    /// <summary>Sorts every difference of one document into a known class; whatever is left is unclassified.</summary>
    internal static (Dictionary<string, int> Counts, List<string> Unclassified) Classify(IReadOnlyList<string> differences) {
        var counts = DifferenceClasses.ToDictionary(c => c.Class, _ => 0, StringComparer.Ordinal);
        var left = differences.ToList();
        foreach (var d in differences.Where(d => d.StartsWith("resolver-only\tnavigation|", StringComparison.Ordinal)).ToList()) {
            var path = d["resolver-only\tnavigation|".Length..];
            if (left.Remove("outline-only\tproperty|" + path)) {
                left.Remove(d);
                counts["bare-navigation"]++;
            }
        }
        if (differences.Any(d => d.StartsWith("resolver-only\tfunction|", StringComparison.Ordinal))) {
            // `function Twice(a: Number, ...)`: the outline reads `a:` as a top-level `name:` head of kind unknown and the
            // balanced scan that follows swallows the rest of the document, so every later declaration is missing too.
            counts["top-level-function"] += left.Count;
            left.Clear();
        }

        return (counts, left);
    }

    // ── the frozen file ──────────────────────────────────────────────────────────────

    private static List<string> ComputeRows() {
        var rows = new List<string>();
        foreach (var (key, text) in ParityDocuments.All) {
            foreach (var d in Differences(OutlineHeads(text), ResolverHeads(text)))
                rows.Add($"H\t{key}\t{d}");
        }

        return rows;
    }

    private static List<string> ReadRows() =>
        [.. File.ReadAllLines(Path.Combine(GuideBlocks.RepoRoot(), FilePath)).Where(l => l.StartsWith("H\t", StringComparison.Ordinal))];

    /// <summary><c>POLY_REGEN_OUTLINE_PARITY=1</c> rewrites the differences from the current code. Run it only to delete a row a fixer removed.</summary>
    [Test]
    public async Task Regenerate_WritesTheDifferences() {
        if (Environment.GetEnvironmentVariable("POLY_REGEN_OUTLINE_PARITY") is null)
            return;
        var lines = new List<string> {
            "# G-26b: heads the outline and the resolver disagree on, per document. H doc<TAB>side<TAB>kind|path. Classes: OutlineParityTests.DifferenceClasses.",
        };
        lines.AddRange(ComputeRows());
        File.WriteAllLines(Path.Combine(GuideBlocks.RepoRoot(), FilePath), lines);
        await Assert.That(lines.Count).IsGreaterThan(1);
    }

    // ── tests ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Documents_AreEveryShippedPolyAndTheSweepDocuments() {
        await Assert.That(ParityDocuments.All.Count(d => !d.Key.StartsWith("sweep:", StringComparison.Ordinal))).IsEqualTo(42);
        await Assert.That(ParityDocuments.All.Count(d => d.Key.StartsWith("sweep:", StringComparison.Ordinal))).IsEqualTo(5);
    }

    [Test]
    public async Task Heads_DifferFromTheResolverOnlyByTheFrozenRows() {
        var actual = ComputeRows();
        var frozen = ReadRows();

        await Assert.That(actual.Except(frozen)).IsEmpty().Because("a head differs that no row records");
        await Assert.That(frozen.Except(actual)).IsEmpty().Because("a recorded difference is gone: delete its row");
        await Assert.That(actual.Count).IsEqualTo(frozen.Count);
    }

    [Test]
    public async Task EveryDifference_BelongsToAKnownClassAndEveryClassStillHasOne() {
        var counts = DifferenceClasses.ToDictionary(c => c.Class, _ => 0, StringComparer.Ordinal);
        var unclassified = new List<string>();
        foreach (var (key, text) in ParityDocuments.All) {
            var (docCounts, left) = Classify(Differences(OutlineHeads(text), ResolverHeads(text)));
            foreach (var (name, count) in docCounts)
                counts[name] += count;
            unclassified.AddRange(left.Select(l => key + ": " + l));
        }

        await Assert.That(unclassified).IsEmpty();
        await Assert.That(counts.Where(c => c.Value == 0).Select(c => c.Key)).IsEmpty().Because("a class with no difference is stale: delete it");
        await Assert.That(DifferenceClasses.Select(c => c.Owner).Except(["none", "G-29", "G-30", "G-05", "S-09", "G-27", "G-13c", "T-15"])).IsEmpty();
    }

    // ── self-tests ───────────────────────────────────────────────────────────────────

    [Test]
    public async Task SelfTest_OneOutlineHeadDisagreeing_IsADifference() {
        var (_, text) = ParityDocuments.All.First(d => d.Key == "sweep:entities");
        var outline = OutlineHeads(text);
        var resolver = ResolverHeads(text);
        var clean = Differences(outline, resolver);

        var mutated = outline.ToList();
        mutated[mutated.FindIndex(h => h.StartsWith("entity|", StringComparison.Ordinal))] = "entity|Renamed";
        var differences = Differences(mutated, resolver);

        await Assert.That(differences.Count).IsGreaterThan(clean.Count);
        await Assert.That(differences.Any(d => d.Contains("outline-only\tentity|Renamed", StringComparison.Ordinal))).IsTrue();
        await Assert.That(Classify(differences).Unclassified).IsNotEmpty();
    }

    [Test]
    public async Task SelfTest_AFrozenRowThatNoLongerAppears_IsStale() {
        var frozen = ReadRows();
        frozen.Add("H\tsweep:entities\tresolver-only\tentity|NoSuchEntity");

        await Assert.That(frozen.Except(ComputeRows())).IsNotEmpty();
    }
}