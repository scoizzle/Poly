using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Mcp;

/// <summary>
/// G-18 (#678, #655): export prints in declaration order and only the document's own
/// <c>uses</c>, so export, define, export is a fixed point through the product's MCP path and
/// the model survives the round trip.
/// </summary>
public sealed class ExportRoundTripOrderTests {
    private const string ZedAlpha = """
        domain D
        Zed: entity {
          Name: Text
        }
        Alpha: entity {
          Zed1: Text
          mgr: Zed
          buddy: Alpha optional
          Age: Number
        }
        """;

    public static IEnumerable<GoldenDomain> CompilableDomains() => GoldenDomains.Compilable;

    private static readonly PolySurfaceHarness Surface = McpTestTools.Surface;

    private static string Apply(string poly) {
        var workspace = Surface.Open("RoundTrip");
        var applied = Surface.Define(workspace, poly);
        if (!applied.Success)
            throw new InvalidOperationException(applied.Summary + " " + string.Join("; ", applied.Findings.Select(f => f.Message)));
        return workspace;
    }

    // Content throws, with the envelope's code and summary, unless the export succeeded with non-empty text.
    private static string ExportDsl(string workspace) => Surface.Export(workspace, "dsl").Content;

    private static string ExportCSharp(string workspace) => Surface.Export(workspace, "csharp").Content;

    [Test]
    public async Task Export_ZedAlpha_KeepsOrder_AddsNoUses_AndIsAFixedPointThroughMcp() {
        var first = Apply(ZedAlpha);
        var text1 = ExportDsl(first);
        var second = Apply(text1);
        var text2 = ExportDsl(second);

        await Assert.That(text1.Contains("uses ", StringComparison.Ordinal)).IsFalse();
        await Assert.That(text1.IndexOf("Zed: entity", StringComparison.Ordinal))
            .IsLessThan(text1.IndexOf("Alpha: entity", StringComparison.Ordinal));
        var alphaBody = text1[text1.IndexOf("Alpha: entity", StringComparison.Ordinal)..];
        await Assert.That(alphaBody.IndexOf("Zed1:", StringComparison.Ordinal))
            .IsLessThan(alphaBody.IndexOf("mgr:", StringComparison.Ordinal));
        await Assert.That(alphaBody.IndexOf("mgr:", StringComparison.Ordinal))
            .IsLessThan(alphaBody.IndexOf("buddy:", StringComparison.Ordinal));
        await Assert.That(alphaBody.IndexOf("buddy:", StringComparison.Ordinal))
            .IsLessThan(alphaBody.IndexOf("Age:", StringComparison.Ordinal));
        await Assert.That(text2).IsEqualTo(text1);
        await Assert.That(ExportCSharp(second)).IsEqualTo(ExportCSharp(first));
    }

    [Test]
    public async Task Export_AuthoredUses_SurvivesALaterApplyThatListsNone() {
        var sessionId = Surface.Open("UsesHttp");
        var first = Surface.Define(sessionId, """
            domain D
            uses http
            E: entity {
              N: Number
            }
            """);
        await Assert.That(first.Success).IsTrue();
        await Assert.That(ExportDsl(sessionId)).Contains("uses http");

        var second = Surface.Define(sessionId, """
            domain D
            E: entity {
              N: Number
            }
            """);
        await Assert.That(second.Success).IsTrue();

        await Assert.That(ExportDsl(sessionId)).Contains("uses http");
    }

    [Test]
    [MethodDataSource(nameof(CompilableDomains))]
    public async Task Export_Define_Export_IsByteIdentical_ThroughMcp(GoldenDomain domain) {
        var text1 = ExportDsl(Apply(domain.Poly));
        var text2 = ExportDsl(Apply(text1));

        await Assert.That(text2).IsEqualTo(text1).Because(domain.Name);
    }

    [Test]
    [MethodDataSource(nameof(CompilableDomains))]
    public async Task ExportRoundTrip_PreservesModel_ForEveryCompilableDomain(GoldenDomain domain) {
        var (model1, text1) = Generate(domain.Poly);
        var (model2, text2) = Generate(text1);

        await Assert.That(text2).IsEqualTo(text1).Because(domain.Name);
        await Assert.That(string.Join(",", model2.Types.Select(t => t.GetType().Name + ":" + t.Name).Order()))
            .IsEqualTo(string.Join(",", model1.Types.Select(t => t.GetType().Name + ":" + t.Name).Order()));
        foreach (var kind in new[] { typeof(Entity), typeof(EnumType), typeof(Poly.DomainModeling.Ontology.ValueType) })
            await Assert.That(string.Join(",", model2.Types.Where(kind.IsInstanceOfType).Select(t => t.Name)))
                .IsEqualTo(string.Join(",", model1.Types.Where(kind.IsInstanceOfType).Select(t => t.Name)))
                .Because(domain.Name + " " + kind.Name);
        await Assert.That(string.Join(",", model2.Functions.Select(f => f.Name)))
            .IsEqualTo(string.Join(",", model1.Functions.Select(f => f.Name)));
        await Assert.That(string.Join(",", model2.ImportedContracts.Select(c => c.Name)))
            .IsEqualTo(string.Join(",", model1.ImportedContracts.Select(c => c.Name)));
        await Assert.That(string.Join(",", model2.ContractBindings.Select(b => b.Name)))
            .IsEqualTo(string.Join(",", model1.ContractBindings.Select(b => b.Name)));
        // The printer drops `temporal` and Load re-supplies it; that loss is G-18's documented non-goal.
        await Assert.That(string.Join(",", model2.Extensions.Where(e => e != ExtensionCatalog.TemporalId)))
            .IsEqualTo(string.Join(",", model1.Extensions.Where(e => e != ExtensionCatalog.TemporalId)));
        foreach (var entity1 in model1.Types.OfType<Entity>()) {
            var entity2 = model2.Types.OfType<Entity>().Single(e => e.Name == entity1.Name);
            await Assert.That(string.Join(",", EntityDeclarationOrder.Members(entity2).Select(m => m.Kind + ":" + MemberName(m.Node))))
                .IsEqualTo(string.Join(",", EntityDeclarationOrder.Members(entity1).Select(m => m.Kind + ":" + MemberName(m.Node))))
                .Because(domain.Name + "." + entity1.Name);
            await Assert.That(string.Join(",", entity2.MemberOrder.Select(m => m.Kind + ":" + m.Name)))
                .IsEqualTo(string.Join(",", entity1.MemberOrder.Select(m => m.Kind + ":" + m.Name)))
                .Because(domain.Name + "." + entity1.Name);
        }
    }

    /// <summary>Anti-vacuity: the corpus is the whole catalog, and one entity's declared order differs from the per-kind fallback.</summary>
    [Test]
    public async Task Corpus_IsPinned_AndDeclaredOrderDiffersFromFallbackSomewhere() {
        await Assert.That(CompilableDomains().Count()).IsEqualTo(10);
        await Assert.That(CompilableDomains()).IsNotEmpty();

        var differs = CompilableDomains()
            .SelectMany(d => Generate(d.Poly).Model.Types.OfType<Entity>())
            .Any(e => EntityDeclarationOrder.Members(e).Select(m => m.Node)
                .SequenceEqual(EntityDeclarationOrder.Members(e with { MemberOrder = [] }).Select(m => m.Node)) == false);
        await Assert.That(differs).IsTrue();
    }

    private static string MemberName(Node node) => node switch {
        Property p => p.Name,
        Relationship r => r.Name,
        Stage s => s.Name,
        Poly.DomainModeling.Ontology.Action a => a.Name,
        Policy p => p.Name,
        _ => throw new InvalidOperationException(node.GetType().Name),
    };

    private static (Domain Model, string Text) Generate(string poly) {
        var session = DomainSession.Create(DslSyntaxTree.Parse(poly).Uses, new SessionOptions(ExtensionCatalog.Core, ProductCatalog.ImplicitIds));
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly, session).Parse(), session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return (result.Root, new DomainDslPrinter(session, ProductCatalog.ImplicitIds).Print(result.Root));
    }
}