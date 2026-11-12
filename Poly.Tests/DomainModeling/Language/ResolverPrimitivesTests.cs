using Poly.Analysis;
using Poly.Compile;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// S-08 (#478): a document that declares no entity — function-only or value-only — resolves
/// the canonical built-in primitives exactly as an entity document does, because the resolver
/// seeds them once per document. An unknown type on a function parameter is reported as a
/// function parameter, not as a property.
/// </summary>
public sealed class ResolverPrimitivesTests {
    private const string FunctionOnlyDsl = """
        domain P
        function F(x: Number): Number = x
        """;

    private const string ValueOnlyDsl = """
        domain P
        Money: value {
          Amount: Number
        }
        """;

    private const string EntityOnlyDsl = """
        domain P
        E: entity {
          Q: Number
        }
        """;

    private const string ContractOnlyDsl = """
        domain P
        Billing: contract internal billing v1 {
          ChargeRequest: value {
            Amount: Number
          }
          Charge: outbound operation ChargeRequest
        }
        """;

    private static (bool Succeeded, Domain Root, AnalysisResult Analysis) ApplyAndAnalyze(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return (result.Succeeded, result.Root, result.Analysis);
    }

    private static IReadOnlyList<Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    private static IReadOnlyList<string> Primitives(Domain root) =>
        root.Types.OfType<PrimitiveType>().Select(p => p.Name).ToList();

    private static IReadOnlyList<CompileDiagnostic> ProductionDiagnostics(string dsl) =>
        new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ExtensionCatalog.ProductAuthoring))
            .Analyze(dsl, "builtin-primitives.poly")
            .Diagnostics;

    [Test]
    public async Task FunctionOnlyDocument_ResolvesNumber() {
        var (_, _, analysis) = ApplyAndAnalyze(FunctionOnlyDsl);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsEmpty();
    }

    [Test]
    public async Task ValueTypeOnlyDocument_ResolvesNumber() {
        var (_, _, analysis) = ApplyAndAnalyze(ValueOnlyDsl);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsEmpty();
    }

    [Test]
    public async Task FunctionParameterUnknownType_SaysFunctionParameter() {
        var (_, _, analysis) = ApplyAndAnalyze("""
            domain P
            function F(x: Money): Number = x
            """);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.SemanticReferenceResolution);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("Function parameter 'x'");
        await Assert.That(errors[0].Message).Contains("Money");
        await Assert.That(errors[0].Message).DoesNotContain("Property");
    }

    /// <summary>
    /// J1: the session loads temporal, so the resolver seeds the canonical six plus the
    /// loaded libraries' primitives. Assert the superset and that no name repeats.
    /// </summary>
    [Test]
    public async Task FunctionOnlyDocument_CarriesBuiltInPrimitives() {
        var (succeeded, root, _) = ApplyAndAnalyze(FunctionOnlyDsl);

        await Assert.That(succeeded).IsTrue();
        var names = Primitives(root);
        foreach (var builtIn in new[] { "Boolean", "Number", "Decimal", "Text", "Uuid", "Binary" })
            await Assert.That(names).Contains(builtIn);
        await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(names.Count);
    }

    /// <summary>Guard: an entity document keeps resolving (the seed runs inside the entity body today).</summary>
    [Test]
    public async Task EntityOnlyDocument_ResolvesNumber() {
        var (_, _, analysis) = ApplyAndAnalyze(EntityOnlyDsl);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsEmpty();
    }

    /// <summary>Guard: a contract-only document already seeds the primitives in the contract body.</summary>
    [Test]
    public async Task ContractOnlyDocument_ResolvesNumber() {
        var (_, _, analysis) = ApplyAndAnalyze(ContractOnlyDsl);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.SemanticReferenceResolution)).IsEmpty();
    }

    /// <summary>
    /// Guard: the relabel is scoped to function parameters; an entity property keeps its wording.
    /// Built through <see cref="ValidDomain"/> because the parser folds an unknown identifier in
    /// an entity body into its navigation-path refusal, so the analyzer is the only door to it.
    /// </summary>
    [Test]
    public async Task EntityPropertyUnknownType_StillSaysProperty() {
        var domain = ValidDomain.Create("P", [
            new Entity("E", [new Property("Q", new DomainTypeReference("Money"), [])], [], [], []),
        ]);

        var errors = DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution)
            .ToList();
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo("Property 'Q' references unknown type 'Money'.");
    }

    /// <summary>
    /// J2: the <c>uses</c> loop emits <c>AddDomainExtensionChange("temporal")</c> before the
    /// resolver seeds the primitives, so the seed's name filter skips temporal's own
    /// <c>Date</c> and the document carries exactly one. This document never takes the
    /// seed-append path; <see cref="SeedOnlyTemporalDocument_CarriesExactlyOneDate"/> covers it.
    /// </summary>
    [Test]
    public async Task FunctionOnlyDocumentWithTemporalUse_CarriesExactlyOneDate() {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser("""
                domain P
                uses temporal
                function F(x: Date): Date = x
                """).Parse());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Root.Types.OfType<PrimitiveType>().Count(p => p.Name == "Date"))
            .IsEqualTo(1);
    }

    /// <summary>
    /// S-08 follow-up: a host that seeds <c>temporal</c> while the document names no
    /// <c>uses</c> reaches the seed-append path, and still carries exactly one <c>Date</c>.
    /// </summary>
    [Test]
    public async Task SeedOnlyTemporalDocument_CarriesExactlyOneDate() {
        var result = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ["temporal"]))
            .Analyze("""
                domain P
                function F(x: Date): Date = x
                """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Domain!.Types.OfType<PrimitiveType>().Count(p => p.Name == "Date"))
            .IsEqualTo(1);
    }

    /// <summary>
    /// S-08 follow-up, double-seed pin: an entity, a value, a function and a contract in one
    /// document share one seed, so every primitive appears exactly once.
    /// </summary>
    [Test]
    public async Task MixedDocument_CarriesEachPrimitiveOnce() {
        var (succeeded, root, _) = ApplyAndAnalyze("""
            domain P
            uses temporal
            Money: value {
              Amount: Number
            }
            Billing: contract internal billing v1 {
              ChargeRequest: value {
                Amount: Number
              }
              Charge: outbound operation ChargeRequest
            }
            E: entity {
              Q: Number
            }
            function F(x: Number): Number = x
            """);

        await Assert.That(succeeded).IsTrue();
        var names = Primitives(root);
        await Assert.That(names.Where(n => names.Count(o => o == n) > 1).ToList()).IsEmpty();
        foreach (var builtIn in new[] { "Boolean", "Number", "Decimal", "Text", "Uuid", "Binary", "Date" })
            await Assert.That(names).Contains(builtIn);
    }

    /// <summary>#478 repro 1 through the production door: no unknown type, no duplicate name.</summary>
    [Test]
    public async Task ProductionDoor_FunctionOnlyDocument_NoUnknownTypeNoDuplicate() {
        var diagnostics = ProductionDiagnostics(FunctionOnlyDsl);
        await Assert.That(diagnostics.Where(d => d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution))
            .IsEmpty();
        await Assert.That(diagnostics.Where(d => d.Code == DomainModelDiagnosticCodes.StructuralDuplicate))
            .IsEmpty();
    }

    /// <summary>#478 repro 2 through the production door: no unknown type, no duplicate name.</summary>
    [Test]
    public async Task ProductionDoor_ValueTypeOnlyDocument_NoUnknownTypeNoDuplicate() {
        var diagnostics = ProductionDiagnostics(ValueOnlyDsl);
        await Assert.That(diagnostics.Where(d => d.Code == DomainModelDiagnosticCodes.SemanticReferenceResolution))
            .IsEmpty();
        await Assert.That(diagnostics.Where(d => d.Code == DomainModelDiagnosticCodes.StructuralDuplicate))
            .IsEmpty();
    }
}