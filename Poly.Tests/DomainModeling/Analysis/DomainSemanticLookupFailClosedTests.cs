using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Effects;
using Poly.DomainModeling.Lowering;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Fail-closed coverage for the DACR semantic lookup surface (r4 follow-ups F5/F18):
/// exporter RLM throw, DomainSemanticLookupExtensions helper contracts (including the
/// SA fallthrough regression B-1), and MCP describe routes returning not-found when
/// analysis is present but the backing metadata has been stripped.
/// </summary>
public class DomainSemanticLookupFailClosedTests {

    // ── Domain builders ───────────────────────────────────────

    private static Domain BuildOrderDomain() {
        // Entity action carries a transition effect; stage copy is an empty shell.
        var entityAction = new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft", [new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [], [], [])], [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [entityAction],
            Policies: [],
            Stages: [draft, active]);
        return new Domain("OrderDomain", [order], []);
    }

    private static Domain BuildParamStageCopyDomain() {
        // B-1 scenario at the model level: the stage copy carries the entity action's
        // parameters (AddActionToStageChange copies Parameters) but has no effects,
        // while the entity action carries the transition effect.
        var entityAction = new Poly.DomainModeling.Action("Submit", InvocationResult.Void,
            [new Property("Note", new DomainTypeReference("Text"), [])],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft",
            [new Poly.DomainModeling.Action("Submit", InvocationResult.Void,
                [new Property("Note", new DomainTypeReference("Text"), [])], [], [])],
            [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [entityAction],
            Policies: [],
            Stages: [draft, active]);
        return new Domain("OrderDomain", [order], []);
    }

    private static Domain BuildPolicyDomain() {
        var adult = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));
        var active = new Policy("Active",
            DomainExpression.Equal(DomainExpression.Property("Status"),
                DomainExpression.Literal("Active")));
        var stage = new Stage("Draft", [], [active], [], []);
        var order = new Entity("Order",
            [new Property("Age", new DomainTypeReference("Number"), [])],
            Actions: [],
            Policies: [adult],
            Stages: [stage]);
        return new Domain("PolicyDomain", [order], []);
    }

    private static Domain BuildRelationshipDomain() {
        var order = new Entity("Order", [], Actions: [], Policies: [], Stages: []);
        var line = new Entity("Line", [], Actions: [], Policies: [], Stages: []);
        var rel = new Relationship("Owns",
            new DomainTypeReference("Order"), new DomainTypeReference("Line"),
            RelationshipCardinality.OneToMany, []);
        return new Domain("RelDomain", [order, line], [rel]);
    }

    // ── F5: exporter ResolveRelationship RLM throw ────────────

    [Test]
    public async Task ResolveRelationship_Throws_WhenAnalysisPresent_ButRlmMissing() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<RelationshipLookupMetadata>(null);

        await Assert.That(() =>
            DomainToCSharpExporter.ResolveRelationship(domain.Relationships, "Owns", "Order", analysis))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ResolveRelationship_ReturnsRelationship_WhenRlmPresent_AndFound() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);

        var rel = DomainToCSharpExporter.ResolveRelationship(domain.Relationships, "Owns", "Order", analysis);

        await Assert.That(rel).IsNotNull();
        await Assert.That(rel!.Name).IsEqualTo("Owns");
    }

    [Test]
    public async Task ResolveRelationship_ReturnsNull_WhenAnalysisLookup_Completes_WithoutMatch() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);

        // RLM present, but the relationship name is not in it — not-found, not throw.
        var rel = DomainToCSharpExporter.ResolveRelationship(domain.Relationships, "Missing", "Order", analysis);

        await Assert.That(rel).IsNull();
    }

    // ── DomainSemanticLookupExtensions: TryGetStage ───────────

    [Test]
    public async Task TryGetStage_ReturnsStage_WhenEsmPresent() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];

        await Assert.That(analysis.TryGetStage(order, "Draft", out var stage)).IsTrue();
        await Assert.That(stage).IsNotNull();
        await Assert.That(stage!.Name).IsEqualTo("Draft");
    }

    [Test]
    public async Task TryGetStage_ReturnsFalse_WhenEsmMissing() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        analysis.GetMetadataStore().Remove<EntityStructureMetadata>(order);

        await Assert.That(analysis.TryGetStage(order, "Draft", out _)).IsFalse();
    }

    // ── DomainSemanticLookupExtensions: TryResolveAction ──────

    [Test]
    public async Task TryResolveAction_StageCopy_WithEffects_PreferredOverEntity() {
        // A non-empty stage action (has effects) wins over the entity action.
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];

        // Rebuild with a stage action that has effects (AddActionToStage copies them).
        var entityAction = new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var stageAction = new Poly.DomainModeling.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft", [stageAction], [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var dom = new Domain("D", [
            new Entity("Order",
                [new Property("Name", new DomainTypeReference("Text"), [])],
                Actions: [entityAction], Policies: [], Stages: [draft, active])
        ], []);
        var analysis2 = RuntimeAnalysisCache.GetOrAnalyze(dom);
        var order2 = (Entity)dom.Types[0];

        await Assert.That(analysis2.TryResolveAction(order2, "Draft", "Submit", out var resolved)).IsTrue();
        await Assert.That(resolved).IsSameReferenceAs(stageAction);
    }

    [Test]
    public async Task TryResolveAction_EmptyStageCopy_FallsThroughToEntityAction() {
        // SA semantics: empty stage copy + existing entity action → entity action.
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var entityAction = order.Actions[0];

        await Assert.That(analysis.TryResolveAction(order, "Draft", "Submit", out var resolved)).IsTrue();
        await Assert.That(resolved).IsSameReferenceAs(entityAction);
    }

    [Test]
    public async Task TryResolveAction_ParamCarryingStageCopy_FallsThroughToEntityAction() {
        // B-1 regression: the stage copy inherits the entity action's parameters
        // (AddActionToStageChange copies Parameters), so a params-carrying stage copy
        // with no effects must STILL fall through to the entity action. A
        // Parameters.Count check in the SA predicate silently no-ops here.
        var domain = BuildParamStageCopyDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var entityAction = order.Actions[0];

        await Assert.That(analysis.TryResolveAction(order, "Draft", "Submit", out var resolved)).IsTrue();
        await Assert.That(resolved).IsSameReferenceAs(entityAction);
        await Assert.That(resolved!.Effects.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TryResolveAction_ReturnsFalse_WhenArmMissing() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        analysis.GetMetadataStore().Remove<ActionResolutionMetadata>(order);

        await Assert.That(analysis.TryResolveAction(order, "Draft", "Submit", out _)).IsFalse();
    }

    [Test]
    public async Task TryResolveAction_EntityFallback_WhenNoCurrentStage() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var entityAction = order.Actions[0];

        await Assert.That(analysis.TryResolveAction(order, null, "Submit", out var resolved)).IsTrue();
        await Assert.That(resolved).IsSameReferenceAs(entityAction);
    }

    // ── DomainSemanticLookupExtensions: GetEffectivePolicies ──

    [Test]
    public async Task GetEffectivePolicies_EntityAndStagePolicies_Combined() {
        var domain = BuildPolicyDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];

        var policies = analysis.GetEffectivePolicies(domain, order, "Draft");

        await Assert.That(policies.Count).IsEqualTo(2);
        await Assert.That(policies.Select(p => p.Name)).Contains("Adult");
        await Assert.That(policies.Select(p => p.Name)).Contains("Active");
    }

    [Test]
    public async Task GetEffectivePolicies_ReturnsEmpty_WhenMtiMissing() {
        var domain = BuildPolicyDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        analysis.GetMetadataStore().Remove<MutationTargetIndexMetadata>(domain);

        var policies = analysis.GetEffectivePolicies(domain, order, "Draft");

        await Assert.That(policies).IsEmpty();
    }

    // ── DomainSemanticLookupExtensions: TryGetRelationship ────

    [Test]
    public async Task TryGetRelationship_ReturnsRelationship_WhenRlmPresent() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);

        await Assert.That(analysis.TryGetRelationship("Owns", out var rel)).IsTrue();
        await Assert.That(rel).IsNotNull();
        await Assert.That(rel!.Name).IsEqualTo("Owns");
    }

    [Test]
    public async Task TryGetRelationship_ReturnsFalse_WhenRlmMissing() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<RelationshipLookupMetadata>(null);

        await Assert.That(analysis.TryGetRelationship("Owns", out _)).IsFalse();
    }

    // ── DomainSemanticLookupExtensions: TryGetEntity ──────────

    [Test]
    public async Task TryGetEntity_ReturnsEntity_WhenTypeIsEntity() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);

        await Assert.That(analysis.TryGetEntity("Order", out var entity)).IsTrue();
        await Assert.That(entity).IsNotNull();
        await Assert.That(entity!.Name).IsEqualTo("Order");
    }

    [Test]
    public async Task TryGetEntity_ReturnsFalse_WhenTypeIsNotEntity() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);

        await Assert.That(analysis.TryGetEntity("Text", out _)).IsFalse();
    }

    [Test]
    public async Task TryGetEntity_ReturnsFalse_WhenDtlMissing() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<DomainTypeLookupMetadata>(null);

        await Assert.That(analysis.TryGetEntity("Order", out _)).IsFalse();
    }

    // ── F4: MCP describe routes fail closed on missing metadata ──

    private static McpSessionState? GetFreshState(string sessionId) {
        McpSessionStore.TryGet(sessionId, out var state);
        return state;
    }

    [Test]
    public async Task DescribeStage_ReturnsNotFound_WhenEsmMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.AddStage(sessionId, "Order", "Draft");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        var order = state.Domain.Types.OfType<Entity>()
            .First(e => string.Equals(e.Name, "Order", StringComparison.Ordinal));
        state.LatestAnalysis!.GetMetadataStore().Remove<EntityStructureMetadata>(order);

        var desc = OracleTool.DescribeDomainElement(sessionId, "stage", "Draft", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
    }

    [Test]
    public async Task DescribeAction_ReturnsNotFound_WhenArmMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.AddAction(sessionId, "Order", "Submit");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        var order = state.Domain.Types.OfType<Entity>()
            .First(e => string.Equals(e.Name, "Order", StringComparison.Ordinal));
        state.LatestAnalysis!.GetMetadataStore().Remove<ActionResolutionMetadata>(order);

        var desc = OracleTool.DescribeDomainElement(sessionId, "action", "Submit", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
    }

    [Test]
    public async Task DescribePolicy_ReturnsNotFound_WhenMtiMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();
        var r1b = EvolveTool.AddProperty(sessionId, "Order", "Age", "Number");
        await Assert.That(r1b.Success).IsTrue();
        var r2 = PolicyTool.AddPolicy(sessionId, "Order", "Adult",
            @"{""property"":""Age"",""op"":"">="",""value"":18}");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        state.LatestAnalysis!.GetMetadataStore().Remove<MutationTargetIndexMetadata>(state.Domain);

        var desc = OracleTool.DescribeDomainElement(sessionId, "policy", "Adult", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
    }

    [Test]
    public async Task DescribeRelationship_ReturnsNotFound_WhenRlmMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.AddEntity(sessionId, "Line");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.AddRelationship(sessionId, "Owns", "Order", "Line", "OneToMany");
        await Assert.That(r3.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        state.LatestAnalysis!.GetMetadataStore().Remove<RelationshipLookupMetadata>(null);

        var desc = OracleTool.DescribeDomainElement(sessionId, "relationship", "Owns");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
    }

    // ── F18: describe success coverage (action/relationship had zero tests) ──

    [Test]
    public async Task DescribeAction_ReturnsSuccess_WhenFound() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.AddAction(sessionId, "Order", "Submit");
        await Assert.That(r2.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "action", "Submit", entityName: "Order");
        await Assert.That(desc.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(desc.Data);
        await Assert.That(json).Contains("Submit");
    }

    [Test]
    public async Task DescribeRelationship_ReturnsSuccess_WhenFound() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.AddEntity(sessionId, "Order");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.AddEntity(sessionId, "Line");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.AddRelationship(sessionId, "Owns", "Order", "Line", "OneToMany");
        await Assert.That(r3.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "relationship", "Owns");
        await Assert.That(desc.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(desc.Data);
        await Assert.That(json).Contains("Owns");
    }
}