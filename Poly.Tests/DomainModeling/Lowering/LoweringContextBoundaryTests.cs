using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// A lowering pass refuses a context with no analysis or no domain at construction, so the
/// refusal is positioned at the boundary and not a later null dereference.
/// </summary>
public class LoweringContextBoundaryTests {
    static Entity Widget() => new("Widget", Properties: [], Actions: [], Policies: [], Stages: []);

    [Test]
    public async Task EffectLoweringPass_NullAnalysis_ThrowsArgumentNull() {
        var entity = Widget();
        var (domain, _) = LoweringFixtures.Analyze(entity);
        await Assert.That(() => new EffectLoweringPass(entity, new LoweringContext(new ThisReference(), null!, domain)))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EffectLoweringPass_NullDomain_ThrowsArgumentNull() {
        var entity = Widget();
        var (_, analysis) = LoweringFixtures.Analyze(entity);
        await Assert.That(() => new EffectLoweringPass(entity, new LoweringContext(new ThisReference(), analysis, null!)))
            .Throws<ArgumentNullException>();
    }

    // L-04a: every body an effect pass lowers into returns a DomainResult, so a context without one is refused at
    // construction (a void body cannot carry a failure: CS0127, #825). The refusal names the entity.
    [Test]
    public async Task EffectLoweringPass_NoActionResultType_ThrowsPositioned() {
        var entity = Widget();
        var (domain, analysis) = LoweringFixtures.Analyze(entity);

        var thrown = await Assert.That(() => new EffectLoweringPass(entity, new LoweringContext(new ThisReference(), analysis, domain)))
            .Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message).Contains("ActionResultType");
        await Assert.That(thrown.Message).Contains("Widget");
    }

    [Test]
    public async Task EffectLoweringPass_WithActionResultType_Constructs() {
        var entity = Widget();
        var (domain, analysis) = LoweringFixtures.Analyze(entity);

        var pass = new EffectLoweringPass(entity, new LoweringContext(
            new ThisReference(), analysis, domain, ActionResultType: new NamedTypeReference("DomainResult")));

        await Assert.That(pass).IsNotNull();
    }

    [Test]
    public async Task DomainExpressionLoweringPass_NullAnalysis_ThrowsArgumentNull() {
        var (domain, _) = LoweringFixtures.Analyze(Widget());
        await Assert.That(() => new DomainExpressionLoweringPass(new LoweringContext(new ThisReference(), null!, domain)))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DomainExpressionLoweringPass_NullDomain_ThrowsArgumentNull() {
        var (_, analysis) = LoweringFixtures.Analyze(Widget());
        await Assert.That(() => new DomainExpressionLoweringPass(new LoweringContext(new ThisReference(), analysis, null!)))
            .Throws<ArgumentNullException>();
    }

    // ── Bag-absence boundary: lowering reads published facts, never recomputes ──────

    /// <summary>
    /// A metadata provider that hides exactly one bag type, as an analysis pass that
    /// stopped before publishing it would. Every other bag still answers.
    /// </summary>
    sealed class BagOmittedProvider(INodeMetadataProvider inner, Type omitted) : INodeMetadataProvider {
        public TMetadata? GetMetadata<TMetadata>(Node? node)
            where TMetadata : class, IAnalysisMetadata =>
            typeof(TMetadata) == omitted ? null : inner.GetMetadata<TMetadata>(node);
    }

    static Entity RequiredNameWidget() => new(
        "Widget",
        Properties: [new Property("Name", new DomainTypeReference("Text"), [])],
        Actions: [], Policies: [], Stages: []);

    /// <summary>
    /// #467: requiredness is the published structure fact. With the bag hidden the assign
    /// path fails closed instead of lowering against a recomputed required set.
    /// </summary>
    [Test]
    public async Task Lowering_WhenStructureBagMissing_FailsClosedInsteadOfRecomputing() {
        var entity = RequiredNameWidget();
        var (domain, analysis) = LoweringFixtures.Analyze(entity);
        var context = new LoweringContext(new ThisReference(), analysis, domain, ActionResultType: new NamedTypeReference("DomainResult"))
            with { Analysis = new BagOmittedProvider(analysis, typeof(EntityStructureMetadata)) };
        var pass = new EffectLoweringPass(entity, context);

        var thrown = await Assert.That(() => pass.TryLowerVmNode(new AssignEffect(
                DomainExpression.Property("Name"),
                DomainExpression.Property("name"))))
            .Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains("EntityStructureMetadata");
        await Assert.That(thrown!.Message).Contains("Widget.Name");
    }

    /// <summary>
    /// #467: the trailing create-parameter order is the published structure fact. A direct
    /// call with the bag hidden refuses by name instead of recomputing or returning a list.
    /// </summary>
    [Test]
    public async Task TrailingOmittableProperties_WhenStructureBagMissing_FailsClosedInsteadOfRecomputing() {
        var entity = RequiredNameWidget();
        var (_, analysis) = LoweringFixtures.Analyze(entity);

        var thrown = await Assert.That(() => DomainToCSharpExporter.TrailingOmittableProperties(
                entity, new BagOmittedProvider(analysis, typeof(EntityStructureMetadata))))
            .Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains("EntityStructureMetadata");
        await Assert.That(thrown!.Message).Contains("Widget");
    }

    const string SpliceVoidDsl = """
        domain Shop
        uses stripe

        Order: entity {
          Total: Number default(0)
          Pay: action (request: ChargeRequest) {
            assign Total to Total
          }
        }

        ChargeOrder: bind Stripe Charge to Pay request
        """;

    /// <summary>
    /// #467: the splice target is the published binding fact. A missing fact refuses naming
    /// the bind, before the contract-null erase, instead of falling back to the authored name.
    /// </summary>
    [Test]
    public async Task ContractSplice_WhenBindingFactMissing_ThrowsNamingTheBinding() {
        var (domain, session, analysis) = ContractContributions.Evolve(
            SpliceVoidDsl, ContractContributions.StripeLibrary());
        var hidden = new BagOmittedProvider(analysis, typeof(ContractBindingMetadata));

        var thrown = await Assert.That(() => DomainProgramProjection.ToSyntax(
                domain, hidden, null, null, session.ContractContributions))
            .Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains("ChargeOrder");
    }

    /// <summary>
    /// #467: a bind whose contract is gone must refuse, not erase the call. A2 shape: a
    /// hand-built domain with a locally resolvable parameter type and a bind whose contract
    /// was never imported; analysis records the unresolved fact (DMCON005) and it is hidden
    /// here, so only the bind remains at lowering time.
    /// </summary>
    [Test]
    public async Task ContractSplice_WhenFactAndContractMissing_ThrowsInsteadOfErasingTheCall() {
        var order = new Entity(
            "Order",
            Properties: [new Property("Total", new DomainTypeReference("Number"), [])],
            Actions: [
                new Poly.DomainModeling.Ontology.Action(
                    "Pay",
                    InvocationResult.Void,
                    [new Property("request", new DomainTypeReference("Number"), [])],
                    [new AssignEffect(
                        DomainExpression.Property("Total"),
                        DomainExpression.Property("Total"))],
                    []),
            ],
            Policies: [], Stages: []);
        var domain = Poly.DomainModeling.ValidDomain.Create("Shop", [order]) with {
            ContractBindings = [
                new Poly.DomainModeling.Ontology.Contract.ContractBinding(
                    "ChargeOrder", "Stripe", "Charge", "Pay", "request", []),
            ],
        };
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var hidden = new BagOmittedProvider(analysis, typeof(ContractBindingMetadata));

        var thrown = await Assert.That(() => DomainProgramProjection.ToSyntax(
                domain, hidden, null, null, null))
            .Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains("ChargeOrder");
    }

    /// <summary>
    /// CHARACTERIZATION (green before and after): unique reads the declared constraint, not the
    /// storage bag. A later slice must not "simplify" the path to a false when storage is absent.
    /// </summary>
    [Test]
    public async Task UniqueAssign_WhenStorageBagMissing_StillEmitsEnsureUnique() {
        var permit = new Entity(
            "Permit",
            Properties: [new Property("Plate", new DomainTypeReference("Text"), [new UniqueConstraint()])],
            Actions: [], Policies: [], Stages: []);
        var (domain, analysis) = LoweringFixtures.Analyze(permit);
        var context = new LoweringContext(new Parameter("entity", new TypeReference("Permit")), analysis, domain, ActionResultType: new NamedTypeReference("DomainResult"))
            with { Analysis = new BagOmittedProvider(analysis, typeof(StorageMappingMetadata)) };
        var pass = new EffectLoweringPass(permit, context);

        var lowered = pass.TryLowerVmNode(new AssignEffect(
            DomainExpression.Property("Plate"),
            DomainExpression.Property("plate")));

        await Assert.That(lowered).IsNotNull();
        var cs = new CSharpGenerator().Generate(lowered!);
        await Assert.That(cs).Contains("EnsureUnique");
        await Assert.That(cs).Contains("Plate");
    }
}