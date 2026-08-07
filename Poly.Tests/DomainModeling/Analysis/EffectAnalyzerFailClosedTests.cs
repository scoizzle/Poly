using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Review F1/F3: EffectAnalyzer domain-bound name resolve must be catalog+RLM
/// (parity with PolicyConstraintAnalyzer) and must fail closed with a structural
/// diagnostic when no lookup bag exists — never silently skip effect checks.
/// EffectFactsPass must resolve create-in facts through the same bags so facts
/// and the validate pack agree under the same semantic source.
/// </summary>
public class EffectAnalyzerFailClosedTests {
    private static Domain BuildCreateInDomain(string effectRelationshipName = "rel") {
        var order = new Entity("Order", [], [], [], []);
        var action = new Poly.DomainModeling.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInRelationshipEffect(effectRelationshipName, [])
        ], []);
        var customer = new Entity("Customer", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        return new Domain("Test", [customer, order], [rel]);
    }

    /// <summary>Intermediate Semantic bags (DTLM/RLM) only — no DomainCatalogPass.</summary>
    private static AnalysisContext SemanticOnlyContext(Domain domain) {
        var context = AnalysisContext.CreateDefault();
        new SemanticDomainAnalyzer().Analyze(context, domain);
        return context;
    }

    private static List<Diagnostic> AllDiagnostics(AnalysisContext context) =>
        context.Diagnostics.Values.SelectMany(v => v).ToList();

    // ── F1: RLM fallback parity (catalog stripped) ─────────────

    [Test]
    public async Task EffectAnalyzer_WithoutCatalog_ResolvesCreateInViaRlmFallback() {
        var domain = BuildCreateInDomain();
        var context = SemanticOnlyContext(domain);

        new EffectAnalyzer().Analyze(context, domain);

        // Known relationship must NOT be reported unknown just because the
        // catalog is absent — the intermediate RLM fallback resolves it.
        await Assert.That(AllDiagnostics(context).Any(d =>
            d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(context.HasStructuralFailure).IsFalse();
    }

    [Test]
    public async Task EffectAnalyzer_WithoutCatalog_UnknownRelationship_ReportsError() {
        var domain = BuildCreateInDomain("NoSuchRel");
        var context = SemanticOnlyContext(domain);

        new EffectAnalyzer().Analyze(context, domain);

        // Genuinely unknown name must still be reported (RLM fallback resolves,
        // name is absent) — no soft skip, no vacuous success.
        var diags = AllDiagnostics(context);
        await Assert.That(diags.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding &&
            d.Severity == DiagnosticSeverity.Error)).IsTrue();
        await Assert.That(context.HasStructuralFailure).IsFalse();
    }

    // ── F1: missing bags fail closed (never silent skip) ───────

    [Test]
    public async Task EffectAnalyzer_WithoutAnyLookupBags_FailsClosedWithStructuralFailure() {
        var domain = BuildCreateInDomain();
        var context = AnalysisContext.CreateDefault();

        new EffectAnalyzer().Analyze(context, domain);

        // No Semantic bags at all → the pass must fail loud, not return quietly.
        await Assert.That(context.HasStructuralFailure).IsTrue();
        var diags = AllDiagnostics(context);
        await Assert.That(diags.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("lookup bag is unavailable", StringComparison.Ordinal))).IsTrue();
    }

    // ── F3: facts pass resolves via the same bags (no tree scan) ──

    [Test]
    public async Task EffectFactsPass_WithoutCatalog_PublishesResolvedTargetViaRlmFallback() {
        var domain = BuildCreateInDomain();
        var context = SemanticOnlyContext(domain);

        new EffectFactsPass().Analyze(context, domain);

        var customer = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        var createIn = customer.Actions.First().Effects.OfType<CreateEntityInRelationshipEffect>().First();
        var resolved = context.GetMetadata<ResolvedRelationshipTargetMetadata>(createIn);
        await Assert.That(resolved).IsNotNull();
        await Assert.That(resolved!.Relationship.Name).IsEqualTo("rel");
        await Assert.That(resolved.TargetEntity.Name).IsEqualTo("Order");
    }

    [Test]
    public async Task EffectFactsPass_WithoutAnyLookupBags_PublishesNothingWithoutError() {
        var domain = BuildCreateInDomain();
        var context = AnalysisContext.CreateDefault();

        new EffectFactsPass().Analyze(context, domain);

        // Facts pass is best-effort publication: no bags → no facts, no crash.
        var customer = domain.Types.OfType<Entity>().First(e => e.Name == "Customer");
        var createIn = customer.Actions.First().Effects.OfType<CreateEntityInRelationshipEffect>().First();
        await Assert.That(context.GetMetadata<ResolvedRelationshipTargetMetadata>(createIn)).IsNull();
    }
}