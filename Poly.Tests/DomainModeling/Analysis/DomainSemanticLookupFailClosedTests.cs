using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Mcp.Sessions;
using Poly.Mcp.Tools;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Fail-closed coverage for the DACR semantic lookup surface (r4 follow-ups F5/F18)
/// and exporter RLM throw, DomainSemanticLookupExtensions helper contracts
/// (including the SA fallthrough regression B-1), and MCP describe routes that
/// distinguish not-found (catalog complete, name absent) from missing required
/// metadata bags (catalog/ESM stripped).
/// </summary>
public class DomainSemanticLookupFailClosedTests {

    // ── Domain builders ───────────────────────────────────────

    private static Domain BuildOrderDomain() {
        // Entity action carries a transition effect; stage copy is an empty shell.
        var entityAction = new Poly.DomainModeling.Ontology.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft", [new Poly.DomainModeling.Ontology.Action("Submit", InvocationResult.Void, [], [], [])], [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [entityAction],
            Policies: [],
            Stages: [draft, active]);
        return DomainTestFactory.Create("OrderDomain", [order], []);
    }

    private static Domain BuildParamStageCopyDomain() {
        // B-1 scenario at the model level: the stage copy carries the entity action's
        // parameters (AddActionToStageChange copies Parameters) but has no effects,
        // while the entity action carries the transition effect.
        var entityAction = new Poly.DomainModeling.Ontology.Action("Submit", InvocationResult.Void,
            [new Property("Note", new DomainTypeReference("Text"), [])],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft",
            [new Poly.DomainModeling.Ontology.Action("Submit", InvocationResult.Void,
                [new Property("Note", new DomainTypeReference("Text"), [])], [], [])],
            [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            Actions: [entityAction],
            Policies: [],
            Stages: [draft, active]);
        return DomainTestFactory.Create("OrderDomain", [order], []);
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
        return DomainTestFactory.Create("PolicyDomain", [order], []);
    }

    /// <summary>
    /// Multi-policy fixture: entity + stage + action policies and a stage-local action.
    /// Stage-effective surface must count entity+stage only (not the action policy).
    /// </summary>
    private static Domain BuildMultiPolicyFixture() {
        var adult = new Policy("Adult",
            DomainExpression.GreaterThanOrEqual(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));
        var active = new Policy("Active",
            DomainExpression.Equal(DomainExpression.Property("Status"),
                DomainExpression.Literal("Active")));
        var hasNote = new Policy("HasNote",
            DomainExpression.NotEqual(
                DomainExpression.Property("Note"),
                DomainExpression.Literal("")));
        var submit = new Poly.DomainModeling.Ontology.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))],
            [hasNote]);
        var draft = new Stage("Draft", [submit], [active], [], []);
        var activeStage = new Stage("Active", [], [], [], []);
        var order = new Entity("Order",
            [
                new Property("Age", new DomainTypeReference("Number"), []),
                new Property("Status", new DomainTypeReference("Text"), []),
                new Property("Note", new DomainTypeReference("Text"), []),
            ],
            Actions: [],
            Policies: [adult],
            Stages: [draft, activeStage]);
        return DomainTestFactory.Create("MultiPolicyDomain", [order], []);
    }

    private static Domain BuildRelationshipDomain() {
        var order = new Entity("Order", [], Actions: [], Policies: [], Stages: []);
        var line = new Entity("Line", [], Actions: [], Policies: [], Stages: []);
        var rel = new Relationship("Owns",
            new DomainTypeReference("Order"), new DomainTypeReference("Line"),
            RelationshipCardinality.OneToMany, []);
        return DomainTestFactory.Create("RelDomain", [order, line], [rel]);
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
    public async Task TryGetStage_UsesCatalog_WhenEsmMissing() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        analysis.GetMetadataStore().Remove<EntityStructureMetadata>(order);

        await Assert.That(analysis.TryGetStage(order, "Draft", out var stage)).IsTrue();
        await Assert.That(stage!.Name).IsEqualTo("Draft");
    }

    [Test]
    public async Task TryGetStage_ReturnsFalse_WhenCatalogMissing() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

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
        var entityAction = new Poly.DomainModeling.Ontology.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var stageAction = new Poly.DomainModeling.Ontology.Action("Submit", InvocationResult.Void, [],
            [new StageTransitionEffect(new StageReference("Active"))], []);
        var draft = new Stage("Draft", [stageAction], [], [], []);
        var active = new Stage("Active", [], [], [], []);
        var dom = DomainTestFactory.Create("D", [
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
    public async Task TryResolveAction_ReturnsFalse_WhenCatalogMissing() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        await Assert.That(analysis.TryResolveAction(domain, order, "Draft", "Submit", out _)).IsFalse();
    }

    [Test]
    public async Task TryResolveAction_UsesCatalog_WithoutEntityKeyedArm() {
        var domain = BuildOrderDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var entityAction = order.Actions[0];

        await Assert.That(analysis.GetMetadata<ActionResolutionMetadata>(order)).IsNull();
        await Assert.That(analysis.GetCatalog(domain)).IsNotNull();
        await Assert.That(analysis.TryResolveAction(domain, order, "Draft", "Submit", out var resolved)).IsTrue();
        await Assert.That(resolved).IsSameReferenceAs(entityAction);
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

        await Assert.That(policies.Count).IsEqualTo(1);
        await Assert.That(policies.Select(p => p.Name)).Contains("Active");
        await Assert.That(policies.Select(p => p.Name)).DoesNotContain("Adult");
    }

    [Test]
    public async Task GetEffectivePolicies_ReturnsEmpty_WhenCapabilityAndCatalogMissing() {
        var domain = BuildPolicyDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var draft = order.Stages[0];
        // StageCapability is preferred (W2); strip it plus catalog for empty.
        analysis.GetMetadataStore().Remove<StageCapabilityMetadata>(draft);
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        var policies = analysis.GetEffectivePolicies(domain, order, "Draft");

        await Assert.That(policies).IsEmpty();
    }

    [Test]
    public async Task GetEffectivePolicies_ReturnsEmpty_WhenCapabilityStripped() {
        var domain = BuildPolicyDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var draft = order.Stages[0];
        analysis.GetMetadataStore().Remove<StageCapabilityMetadata>(draft);

        await Assert.That(analysis.GetCatalog(domain)).IsNotNull();
        var policies = analysis.GetEffectivePolicies(domain, order, "Draft");

        await Assert.That(policies).IsEmpty();
    }

    [Test]
    public async Task GetEffectivePolicies_UnknownStage_ReturnsEmpty_NotEntityPolicies() {
        var domain = BuildPolicyDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];

        // Capability path: unknown stage never resolves → empty (not entity policies).
        var withCap = analysis.GetEffectivePolicies(domain, order, "DoesNotExist");
        await Assert.That(withCap).IsEmpty();

        // Catalog-compose path: strip stage capability bags so MTI fallthrough would
        // have returned entity policies before the fail-closed fix.
        foreach (var stage in order.Stages)
            analysis.GetMetadataStore().Remove<StageCapabilityMetadata>(stage);

        var viaCatalog = analysis.GetEffectivePolicies(domain, order, "DoesNotExist");
        await Assert.That(viaCatalog).IsEmpty();
        // Known stage with capability stripped is empty — no catalog recomposition.
        await Assert.That(analysis.GetEffectivePolicies(domain, order, "Draft")).IsEmpty();
    }

    [Test]
    public async Task GetEffectiveActions_UnknownStage_ReturnsEmpty() {
        var domain = BuildMultiPolicyFixture();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];

        var actions = analysis.GetEffectiveActions(domain, order, "DoesNotExist");
        await Assert.That(actions).IsEmpty();
    }

    // ── unified effective surface ─────────────────────

    [Test]
    public async Task GetEffectivePolicies_ExcludesActionPolicies_OnMultiPolicyFixture() {
        var domain = BuildMultiPolicyFixture();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var draft = order.Stages[0];

        var policies = analysis.GetEffectivePolicies(domain, order, "Draft");
        var cap = analysis.GetMetadata<StageCapabilityMetadata>(draft);

        await Assert.That(policies.Count).IsEqualTo(1);
        await Assert.That(policies.Select(p => p.Name)).Contains("Active");
        await Assert.That(policies.Select(p => p.Name)).DoesNotContain("Adult");
        await Assert.That(policies.Any(p => p.Name == "HasNote")).IsFalse();
        await Assert.That(cap).IsNotNull();
        await Assert.That(cap!.View.EffectivePolicies.Count).IsEqualTo(policies.Count);
    }

    [Test]
    public async Task GetEffectiveActions_ReturnsStageLocalActions() {
        var domain = BuildMultiPolicyFixture();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var draft = order.Stages[0];

        var actions = analysis.GetEffectiveActions(domain, order, "Draft");
        var cap = analysis.GetMetadata<StageCapabilityMetadata>(draft);

        await Assert.That(actions.Count).IsEqualTo(1);
        await Assert.That(actions[0].Name).IsEqualTo("Submit");
        await Assert.That(cap!.View.EffectiveActions.Count).IsEqualTo(actions.Count);
    }

    [Test]
    public async Task GetEffectiveActions_ReturnsEmpty_WhenCapabilityAndCatalogMissing() {
        var domain = BuildMultiPolicyFixture();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var draft = order.Stages[0];
        analysis.GetMetadataStore().Remove<StageCapabilityMetadata>(draft);
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        var actions = analysis.GetEffectiveActions(domain, order, "Draft");

        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task ActionCapability_TransitionTargets_AreRealStageRefs() {
        var domain = BuildMultiPolicyFixture();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        var order = (Entity)domain.Types[0];
        var draft = order.Stages[0];
        var active = order.Stages[1];
        var submit = draft.Actions[0];

        var cap = analysis.GetMetadata<ActionCapabilityMetadata>(submit);

        await Assert.That(cap).IsNotNull();
        await Assert.That(cap!.View.TransitionTargets.Count).IsEqualTo(1);
        await Assert.That(cap.View.TransitionTargets[0]).IsSameReferenceAs(active);
    }

    [Test]
    public async Task DescribeStage_EffectiveCounts_MatchHelpers() {
        var (sessionId, _) = McpSessionStore.Create("W2Effective");

        var outcome = McpSessionStore.Evolve(sessionId, (domain, session) =>
            new DomainEvolution(domain).Evolve()
                .AddEntity("Order")
                .AddPropertyToEntity("Order", new Property("Age", new DomainTypeReference("Number"), []))
                .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), []))
                .AddPropertyToEntity("Order", new Property("Note", new DomainTypeReference("Text"), []))
                .AddPolicyToEntity("Order", "Adult",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Age"),
                        DomainExpression.Literal(18)))
                .AddStage("Order", "Draft")
                .AddStage("Order", "Active")
                .AddPolicyToStage("Order", "Draft", "Active",
                    DomainExpression.Equal(
                        DomainExpression.Property("Status"),
                        DomainExpression.Literal("Active")))
                .AddActionToStage("Order", "Draft", "Submit")
                .AddPolicyToAction("Order", "Submit", "HasNote",
                    DomainExpression.NotEqual(
                        DomainExpression.Property("Note"),
                        DomainExpression.Literal("")))
                .Apply(session: session));
        await Assert.That(outcome).IsNotNull();
        await Assert.That(outcome!.Succeeded).IsTrue();

        var state = GetFreshState(sessionId)!;
        var analysis = state.LatestAnalysis!;
        var order = state.Domain.Types.OfType<Entity>()
            .First(e => string.Equals(e.Name, "Order", StringComparison.Ordinal));
        var helperPolicies = analysis.GetEffectivePolicies(state.Domain, order, "Draft").Count;
        var helperActions = analysis.GetEffectiveActions(state.Domain, order, "Draft").Count;
        var draft = order.Stages.First(s => s.Name == "Draft");
        var cap = analysis.GetMetadata<StageCapabilityMetadata>(draft);

        await Assert.That(helperPolicies).IsEqualTo(1);
        await Assert.That(helperActions).IsEqualTo(1);
        await Assert.That(cap!.View.EffectivePolicies.Count).IsEqualTo(helperPolicies);
        await Assert.That(cap.View.EffectiveActions.Count).IsEqualTo(helperActions);

        var desc = OracleTool.DescribeDomainElement(sessionId, "stage", "Draft", entityName: "Order");
        await Assert.That(desc.Success).IsTrue();
        await Assert.That(desc.Message).Contains($"{helperActions} effective actions");
        await Assert.That(desc.Message).Contains($"{helperPolicies} effective policies");
    }

    // ── DomainSemanticLookupExtensions: TryGetRelationship ────

    [Test]
    public async Task TryGetRelationship_ReturnsRelationship_WhenRlmPresent() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);

        await Assert.That(analysis.TryGetRelationship("Order", "Owns", out var rel)).IsTrue();
        await Assert.That(rel).IsNotNull();
        await Assert.That(rel!.Name).IsEqualTo("Owns");
    }

    [Test]
    public async Task TryGetRelationship_ReturnsFalse_WhenCatalogMissing() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        await Assert.That(analysis.TryGetRelationship(domain, "Order", "Owns", out _)).IsFalse();
    }

    [Test]
    public async Task TryGetRelationship_UsesCatalog_WhenRawRlmStripped() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        // Intermediate Semantic RLM still published; domain-keyed path uses catalog only.
        analysis.GetMetadataStore().Remove<RelationshipLookupMetadata>(null);

        await Assert.That(analysis.TryGetRelationship(domain, "Order", "Owns", out var rel)).IsTrue();
        await Assert.That(rel!.Name).IsEqualTo("Owns");
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
    public async Task TryGetEntity_ReturnsFalse_WhenCatalogMissing() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        analysis.GetMetadataStore().Remove<DomainCatalogMetadata>(domain);

        await Assert.That(analysis.TryGetEntity(domain, "Order", out _)).IsFalse();
    }

    [Test]
    public async Task TryGetEntity_UsesCatalog_WhenRawDtlmStripped() {
        var domain = BuildRelationshipDomain();
        var analysis = RuntimeAnalysisCache.GetOrAnalyze(domain);
        // Intermediate Semantic DTLM still published; domain-keyed path uses catalog only.
        analysis.GetMetadataStore().Remove<DomainTypeLookupMetadata>(null);

        await Assert.That(analysis.TryGetEntity(domain, "Order", out var entity)).IsTrue();
        await Assert.That(entity!.Name).IsEqualTo("Order");
    }

    // ── F4 / MCP describe not-found vs missing metadata ──

    private static McpSessionState? GetFreshState(string sessionId) {
        McpSessionStore.TryGet(sessionId, out var state);
        return state;
    }

    [Test]
    public async Task DescribeStage_ReturnsMissingMetadata_WhenEsmMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"Draft"}""");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        var order = state.Domain.Types.OfType<Entity>()
            .First(e => string.Equals(e.Name, "Order", StringComparison.Ordinal));
        state.LatestAnalysis!.GetMetadataStore().Remove<EntityStructureMetadata>(order);

        var desc = OracleTool.DescribeDomainElement(sessionId, "stage", "Draft", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("missing EntityStructureMetadata");
        await Assert.That(desc.Message).DoesNotContain("not found");
    }

    [Test]
    public async Task DescribeStage_ReturnsNotFound_WhenStageAbsentAndEsmPresent() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "stage", """{"entityName":"Order","name":"Draft"}""");
        await Assert.That(r2.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "stage", "NoSuchStage", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
        await Assert.That(desc.Message).DoesNotContain("missing EntityStructureMetadata");
    }

    [Test]
    public async Task DescribeAction_ReturnsMissingMetadata_WhenCatalogMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "action", """{"entityName":"Order","name":"Submit"}""");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        state.LatestAnalysis!.GetMetadataStore().Remove<DomainCatalogMetadata>(state.Domain);

        var desc = OracleTool.DescribeDomainElement(sessionId, "action", "Submit", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("missing DomainCatalogMetadata");
        await Assert.That(desc.Message).DoesNotContain("not found");
    }

    [Test]
    public async Task DescribeAction_ReturnsNotFound_WhenActionAbsentAndCatalogPresent() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "action", """{"entityName":"Order","name":"Submit"}""");
        await Assert.That(r2.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "action", "NoSuchAction", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
        await Assert.That(desc.Message).DoesNotContain("missing DomainCatalogMetadata");
    }

    [Test]
    public async Task DescribeAction_UsesCatalog_WithoutEntityKeyedArm() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "action", """{"entityName":"Order","name":"Submit"}""");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        var order = state.Domain.Types.OfType<Entity>()
            .First(e => string.Equals(e.Name, "Order", StringComparison.Ordinal));
        await Assert.That(state.LatestAnalysis!.GetMetadata<ActionResolutionMetadata>(order)).IsNull();

        var desc = OracleTool.DescribeDomainElement(sessionId, "action", "Submit", entityName: "Order");
        await Assert.That(desc.Success).IsTrue();
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(desc.Data)).Contains("Submit");
    }

    [Test]
    public async Task DescribePolicy_ReturnsMissingMetadata_WhenCatalogMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r1b = EvolveTool.Add(sessionId, "property", """{"entityName":"Order","name":"Age","typeName":"Number"}""");
        await Assert.That(r1b.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "policy", """{"entityName":"Order","name":"Adult","expression":"Age >= 18"}""");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        state.LatestAnalysis!.GetMetadataStore().Remove<DomainCatalogMetadata>(state.Domain);

        var desc = OracleTool.DescribeDomainElement(sessionId, "policy", "Adult", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("missing DomainCatalogMetadata");
        await Assert.That(desc.Message).DoesNotContain("not found");
    }

    [Test]
    public async Task DescribePolicy_ReturnsNotFound_WhenPolicyAbsentAndCatalogPresent() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r1b = EvolveTool.Add(sessionId, "property", """{"entityName":"Order","name":"Age","typeName":"Number"}""");
        await Assert.That(r1b.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "policy", """{"entityName":"Order","name":"Adult","expression":"Age >= 18"}""");
        await Assert.That(r2.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "policy", "NoSuchPolicy", entityName: "Order");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
        await Assert.That(desc.Message).DoesNotContain("missing DomainCatalogMetadata");
    }

    [Test]
    public async Task DescribePolicy_UsesCatalog_WithoutDomainKeyedMti() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r1b = EvolveTool.Add(sessionId, "property", """{"entityName":"Order","name":"Age","typeName":"Number"}""");
        await Assert.That(r1b.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "policy", """{"entityName":"Order","name":"Adult","expression":"Age >= 18"}""");
        await Assert.That(r2.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        await Assert.That(state.LatestAnalysis!.GetMetadata<MutationTargetIndexMetadata>(state.Domain)).IsNull();

        var desc = OracleTool.DescribeDomainElement(sessionId, "policy", "Adult", entityName: "Order");
        await Assert.That(desc.Success).IsTrue();
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(desc.Data)).Contains("Adult");
    }

    [Test]
    public async Task DescribeRelationship_ReturnsMissingMetadata_WhenCatalogMissing() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "entity", """{"name":"Line"}""");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.Add(sessionId, "relationship", """{"name":"Owns","source":"Order","target":"Line","cardinality":"OneToMany"}""");
        await Assert.That(r3.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        state.LatestAnalysis!.GetMetadataStore().Remove<DomainCatalogMetadata>(state.Domain);

        var desc = OracleTool.DescribeDomainElement(sessionId, "relationship", "Owns");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("missing DomainCatalogMetadata");
        await Assert.That(desc.Message).DoesNotContain("not found");
    }

    [Test]
    public async Task DescribeRelationship_ReturnsNotFound_WhenRelationshipAbsentAndCatalogPresent() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "entity", """{"name":"Line"}""");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.Add(sessionId, "relationship", """{"name":"Owns","source":"Order","target":"Line","cardinality":"OneToMany"}""");
        await Assert.That(r3.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "relationship", "NoSuchRel");
        await Assert.That(desc.Success).IsFalse();
        await Assert.That(desc.Message).Contains("not found");
        await Assert.That(desc.Message).DoesNotContain("missing DomainCatalogMetadata");
    }

    [Test]
    public async Task DescribeRelationship_UsesCatalog_WhenRawRlmStripped() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "entity", """{"name":"Line"}""");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.Add(sessionId, "relationship", """{"name":"Owns","source":"Order","target":"Line","cardinality":"OneToMany"}""");
        await Assert.That(r3.Success).IsTrue();

        var state = GetFreshState(sessionId)!;
        state.LatestAnalysis!.GetMetadataStore().Remove<RelationshipLookupMetadata>(null);

        var desc = OracleTool.DescribeDomainElement(sessionId, "relationship", "Owns");
        await Assert.That(desc.Success).IsTrue();
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(desc.Data)).Contains("Owns");
    }

    // ── F18: describe success coverage (action/relationship had zero tests) ──

    [Test]
    public async Task DescribeAction_ReturnsSuccess_WhenFound() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "action", """{"entityName":"Order","name":"Submit"}""");
        await Assert.That(r2.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "action", "Submit", entityName: "Order");
        await Assert.That(desc.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(desc.Data);
        await Assert.That(json).Contains("Submit");
    }

    [Test]
    public async Task DescribeRelationship_ReturnsSuccess_WhenFound() {
        var (sessionId, _) = McpSessionStore.Create("Test");

        var r1 = EvolveTool.Add(sessionId, "entity", """{"name":"Order"}""");
        await Assert.That(r1.Success).IsTrue();
        var r2 = EvolveTool.Add(sessionId, "entity", """{"name":"Line"}""");
        await Assert.That(r2.Success).IsTrue();
        var r3 = EvolveTool.Add(sessionId, "relationship", """{"name":"Owns","source":"Order","target":"Line","cardinality":"OneToMany"}""");
        await Assert.That(r3.Success).IsTrue();

        var desc = OracleTool.DescribeDomainElement(sessionId, "relationship", "Owns");
        await Assert.That(desc.Success).IsTrue();
        var json = System.Text.Json.JsonSerializer.Serialize(desc.Data);
        await Assert.That(json).Contains("Owns");
    }

}