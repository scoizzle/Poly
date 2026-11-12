using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// The round-trip idempotence guard over the shared golden corpus (theme #678, slice
/// G-01): for every <see cref="GoldenDomains.Compilable"/> entry, generating the DSL
/// twice in a row is a fixed point — <c>print(parse(print(parse(x))))</c> equals
/// <c>print(parse(x))</c>. The source is deliberately not compared: the printer drops
/// <c>uses temporal</c>, so a source comparison would be red for a reason this guard does not own.
///
/// <para>The corpus is <see cref="GoldenDomains.Compilable"/>, which is frozen — it is
/// exactly the set <c>EmitGoldenTests</c> snapshots. A later G slice adds its own
/// round-trip case to the inline <see cref="SyntaxCases"/> source rather than editing the
/// hardened corpus.</para>
///
/// <para><b>What this guard is not.</b> It catches instability, not loss. A printer that
/// silently drops a facet still round-trips, so these pass here: the <c>uses temporal</c>
/// drop on print and a deterministic change to
/// parser associativity (<c>DslExprParityTests</c> carries precedence and associativity,
/// including for G-10). An injection that is stable after one round (#655's
/// <c>uses storage</c>) is not caught here either; G-18's
/// <c>Print_DoesNotAddUsesStorage_WhenDocumentDidNotDeclareIt</c> carries it.</para>
/// </summary>
public sealed class DslRoundTripCorpusTests {
    /// <summary>The frozen EmitGolden corpus, one case per domain.</summary>
    public static IEnumerable<GoldenDomain> CompilableDomains() => GoldenDomains.Compilable;

    /// <summary>
    /// Inline round-trip cases for slices that own a shape the frozen corpus does not
    /// carry. Add a case here (name + document) and the same idempotence body runs over
    /// it; do not edit <see cref="GoldenDomains.Compilable"/>.
    /// </summary>
    public static IEnumerable<(string Name, string Poly)> SyntaxCases() => [
        ("minimal-entity", """
            domain Minimal

            Task: entity {
              Title: Text
            }
            """),
    ];

    /// <summary>
    /// One corpus domain: parse and print, then parse and print that text, and compare
    /// the two generated documents. The session comes from the document's own
    /// <c>uses</c>, so temporal folds and binders are live for the first generation.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(CompilableDomains))]
    public async Task Print_Then_Parse_Then_Print_IsIdempotent(GoldenDomain domain) =>
        await AssertIdempotent(domain.Name, domain.Poly);

    /// <summary>One inline case: the same fixed-point body as the corpus rung.</summary>
    [Test]
    [MethodDataSource(nameof(SyntaxCases))]
    public async Task Print_Then_Parse_Then_Print_IsIdempotent_SyntaxCase((string Name, string Poly) syntaxCase) =>
        await AssertIdempotent(syntaxCase.Name, syntaxCase.Poly);

    /// <summary>
    /// Anti-vacuity: a data-source refactor must not turn the guard into a green with
    /// zero cases.
    /// </summary>
    [Test]
    public async Task Corpus_IsNotEmpty() {
        await Assert.That(CompilableDomains().Count()).IsEqualTo(GoldenDomains.Compilable.Count);
        await Assert.That(CompilableDomains()).IsNotEmpty();
    }

    private static async Task AssertIdempotent(string label, string poly) {
        var (first, firstText) = Generate(poly);
        var (second, secondText) = Generate(firstText);

        await Assert.That(first.Succeeded).IsTrue().Because($"{label}: {first.FailureSummary}");
        await Assert.That(second.Succeeded).IsTrue().Because($"{label}: {second.FailureSummary}");
        await Assert.That(secondText).IsEqualTo(firstText).Because(label);
    }

    /// <summary>
    /// Parses, analyses and prints one generation of the document. The seed is the same
    /// implicit set <c>polyc</c> gives a document that lists no <c>uses</c>
    /// (<see cref="ProductCatalog.ImplicitIds"/>), taken from its one source of truth rather
    /// than restated here: a bare <c>unique</c> is <c>DMLIB001</c> (C-05), and the fleet-eval
    /// documents in the corpus carry one without a <c>uses</c> line.
    /// </summary>
    private static (EvolutionResult Result, string Text) Generate(string poly) {
        var session = DomainSession.Create(DslSyntaxTree.Parse(poly).Uses, new SessionOptions(ExtensionCatalog.Core, ProductCatalog.ImplicitIds));
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
        return (result, new DomainDslPrinter(session).Print(result.Root));
    }
}