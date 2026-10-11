using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

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
        var action = new Poly.DomainModeling.Ontology.Action("DoIt", InvocationResult.Void, [], [
            new CreateEntityInRelationshipEffect(effectRelationshipName, [])
        ], []);
        var customer = new Entity("Customer", [], [action], [], []);
        var rel = new Relationship("rel",
            new DomainTypeReference("Customer"), new DomainTypeReference("Order"),
            RelationshipCardinality.OneToMany, []);
        return DomainTestFactory.Create("Test", [customer, order], [rel]);
    }

    private static AnalysisContext CatalogContext(Domain domain) {
        var context = AnalysisContext.CreateDefault();
        new DomainCatalogPass().Analyze(context, domain);
        return context;
    }

    private static List<Diagnostic> AllDiagnostics(AnalysisContext context) =>
        [.. context.Diagnostics];

    // ── F1: RLM fallback parity (catalog stripped) ─────────────

    [Test]
    public async Task EffectAnalyzer_WithCatalog_ResolvesCreateIn() {
        var domain = BuildCreateInDomain();
        var context = CatalogContext(domain);

        new EffectAnalyzer().Analyze(context, domain);

        await Assert.That(AllDiagnostics(context).Any(d =>
            d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(context.HasErrors).IsFalse();
    }

    [Test]
    public async Task EffectAnalyzer_WithCatalog_UnknownRelationship_ReportsError() {
        var domain = BuildCreateInDomain("NoSuchRel");
        var context = CatalogContext(domain);

        new EffectAnalyzer().Analyze(context, domain);

        var diags = AllDiagnostics(context);
        await Assert.That(diags.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectBinding &&
            d.Severity == DiagnosticSeverity.Error)).IsTrue();
        await Assert.That(context.HasErrors).IsTrue();
    }

    // ── F1: missing bags fail closed (never silent skip) ───────

    [Test]
    public async Task EffectAnalyzer_WithoutAnyLookupBags_FailsClosedWithStructuralFailure() {
        var domain = BuildCreateInDomain();
        var context = AnalysisContext.CreateDefault();

        new EffectAnalyzer().Analyze(context, domain);

        // No Semantic bags at all → the pass must fail loud, not return quietly.
        await Assert.That(context.HasErrors).IsTrue();
        var diags = AllDiagnostics(context);
        await Assert.That(diags.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("lookup bag is unavailable", StringComparison.Ordinal))).IsTrue();
    }

    // ── F3: facts pass resolves via the same bags (no tree scan) ──

    [Test]
    public async Task EffectFactsPass_WithCatalog_PublishesResolvedTarget() {
        var domain = BuildCreateInDomain();
        var context = CatalogContext(domain);

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

    // ── D1: create-in initializers are parameter uses ──────────

    private static AnalysisResult AnalyzeDsl(string dsl) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .Apply(new PolyDslParser(dsl).Parse());
        return result.Succeeded ? DomainModelAnalyzer.Analyze(result.Root!) : result.Analysis;
    }

    private static bool UnusedHint(AnalysisResult analysis, string paramName) =>
        analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnusedParameter
            && d.Message.Contains($"'{paramName}'", StringComparison.Ordinal));

    [Test]
    public async Task CreateIn_ParameterInInitializer_NoUnusedHint() {
        var analysis = AnalyzeDsl("""
            domain Test
            Contact: entity { Name: Text Email: Text }
            Account: entity {
              contacts: many Contact
              AddContact: action (name: Text, email: Text) {
                create in contacts { Name: name Email: email }
              }
            }
            """);

        await Assert.That(UnusedHint(analysis, "name")).IsFalse();
        await Assert.That(UnusedHint(analysis, "email")).IsFalse();
    }

    [Test]
    public async Task CreateIn_ParameterAccessInitializer_NoUnusedHint() {
        var contact = new Entity("Contact", [
            new Property("Name", new DomainTypeReference("Text"), [])
        ], [], [], []);
        var action = new Poly.DomainModeling.Ontology.Action("AddContact", InvocationResult.Void, [
            new Property("name", new DomainTypeReference("Text"), [])
        ], [
            new CreateEntityInRelationshipEffect("contacts", [
                new PropertyBinding("Name", new ParameterAccess("name"))
            ])
        ], []);
        var account = new Entity("Account", [], [action], [], []);
        var rel = new Relationship("contacts",
            new DomainTypeReference("Account"), new DomainTypeReference("Contact"),
            RelationshipCardinality.OneToMany, []);
        var domain = DomainTestFactory.Create("Test", [account, contact], [rel]);
        var context = CatalogContext(domain);

        new EffectAnalyzer().Analyze(context, domain);

        await Assert.That(AllDiagnostics(context).Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectUnusedParameter)).IsFalse();
    }

    [Test]
    public async Task CreateIn_UnusedParameter_ReportsHint() {
        var analysis = AnalyzeDsl("""
            domain Test
            Contact: entity { Name: Text }
            Account: entity {
              contacts: many Contact
              AddContact: action (name: Text, extra: Text) {
                create in contacts { Name: name }
              }
            }
            """);

        await Assert.That(UnusedHint(analysis, "name")).IsFalse();
        await Assert.That(UnusedHint(analysis, "extra")).IsTrue();
    }

    [Test]
    public async Task CreateIn_PathParameter_NoUnusedHint() {
        var analysis = AnalyzeDsl("""
            domain Test
            Contact: entity { Name: Text Email: Text }
            Lead: entity { Name: Text Email: Text }
            Account: entity {
              contacts: many Contact
              ConvertLead: action (lead: Lead) {
                create in contacts { Name: lead Name Email: lead Email }
              }
            }
            """);

        await Assert.That(UnusedHint(analysis, "lead")).IsFalse();
    }
}