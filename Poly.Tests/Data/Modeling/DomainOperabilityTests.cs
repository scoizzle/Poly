using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;
using EffectConditional = Poly.Data.Modeling.Effects.Conditional;

namespace Poly.Tests.Data.Modeling;

public class DomainOperabilityTests {
    [Test]
    public async Task OperabilityFacade_Capture_ReturnsTelemetryAndAnalysis() {
        var domain = new Domain("Support");
        domain.CreateMutation().AddType(new Primitive(domain, "string", TypeCategory.Text)).Apply();

        var snapshot = DomainOperabilityFacade.Capture(domain);

        await Assert.That(snapshot.Analysis.HasErrors).IsFalse();
        await Assert.That(snapshot.Analysis.Telemetry.Passes.Count).IsGreaterThan(0);
        await Assert.That(snapshot.Analysis.Diagnostics.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OperabilityFacade_AnalyzeExplainDiff_ReturnsAnalysisAndDiff() {
        var before = new Domain("Support");
        var sharedTypeId = NodeId.NewId();
        var entityId = NodeId.NewId();

        var beforeType = new Primitive(before, "string", TypeCategory.Text) { Id = sharedTypeId };
        var beforeEntity = new Entity(before, "Ticket") { Id = entityId };
        before.CreateMutation().AddType(beforeType).AddType(beforeEntity).Apply();

        var after = new Domain("Support");
        var afterType = new Primitive(after, "string", TypeCategory.Text) { Id = sharedTypeId };
        var afterEntity = new Entity(after, "TicketV2") { Id = entityId };
        after.CreateMutation().AddType(afterType).AddType(afterEntity).Apply();

        var delta = DomainOperabilityFacade.AnalyzeExplainDiff(before, after);

        await Assert.That(delta.Analysis.Telemetry.Passes.Count).IsGreaterThan(0);
        await Assert.That(delta.Diff.Changed.Any(change => change.NodeId == entityId)).IsTrue();
        await Assert.That(delta.Analysis.Diagnostics.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ApplyWithTrace_Success_CapturesStepsAndAffectedNodes() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");

        mutation.AddType(stringType);
        mutation.AddType(entity);

        var execution = mutation.ApplyWithTrace();

        await Assert.That(execution.Analysis.HasErrors).IsFalse();
        await Assert.That(execution.Trace.AppliedStepCount).IsEqualTo(2);
        await Assert.That(execution.Trace.Succeeded).IsTrue();
        await Assert.That(execution.Trace.RolledBack).IsFalse();
        await Assert.That(execution.Trace.Steps.Count).IsEqualTo(2);
        await Assert.That(execution.Trace.AffectedNodeIds.Contains(stringType.Id)).IsTrue();
        await Assert.That(execution.Trace.AffectedNodeIds.Contains(entity.Id)).IsTrue();
    }

    [Test]
    public async Task ApplyWithTrace_Error_RollsBackAndMarksTrace() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        mutation.AddType(new Primitive(domain, "string", TypeCategory.Text));
        mutation.AddType(new Primitive(domain, "string", TypeCategory.Text));

        var execution = mutation.ApplyWithTrace();

        await Assert.That(execution.Analysis.HasErrors).IsTrue();
        await Assert.That(execution.Trace.Succeeded).IsFalse();
        await Assert.That(execution.Trace.RolledBack).IsTrue();
        await Assert.That(domain.Types.Any(t => t.Name == "string")).IsFalse();
    }

    [Test]
    public async Task ApplyWithTrace_AddStageFailure_RollsBackOwnerAttachment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");

        MutationApply.AddType(domain, entity);
        MutationApply.AddStage(entity, new Stage(domain, "Open"));

        var duplicateStage = new Stage(domain, "Open");
        var execution = domain.CreateMutation()
            .AddStage(entity, duplicateStage)
            .ApplyWithTrace();

        await Assert.That(execution.Analysis.HasErrors).IsTrue();
        await Assert.That(execution.Trace.RolledBack).IsTrue();
        await Assert.That(entity.Stages.Contains(duplicateStage)).IsFalse();
        await Assert.That(duplicateStage.OwnerEntity).IsNull();
    }

    [Test]
    public async Task ApplyWithTrace_RemoveParameterFailure_PreservesParameterOrder() {
        var domain = new Domain("Support");
        var text = new Primitive(domain, "string", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");
        var action = new DomainAction(domain, "Escalate", entity);
        var first = new Property(domain, "First", text);
        var second = new Property(domain, "Second", text);
        var third = new Property(domain, "Third", text);

        MutationApply.AddType(domain, text);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);
        MutationApply.AddParameter(action, first);
        MutationApply.AddParameter(action, second);
        MutationApply.AddParameter(action, third);

        var duplicate = new Property(domain, "First", text);
        var execution = domain.CreateMutation()
            .RemoveParameter(action, second)
            .AddParameter(action, duplicate)
            .ApplyWithTrace();

        var parameterNames = action.Parameters.Select(static parameter => parameter.Name).ToArray();

        await Assert.That(execution.Analysis.HasErrors).IsTrue();
        await Assert.That(execution.Trace.RolledBack).IsTrue();
        await Assert.That(parameterNames.Length).IsEqualTo(3);
        await Assert.That(parameterNames[0]).IsEqualTo("First");
        await Assert.That(parameterNames[1]).IsEqualTo("Second");
        await Assert.That(parameterNames[2]).IsEqualTo("Third");
    }

    [Test]
    public async Task Analyze_CapturesPipelinePasses() {
        var domain = new Domain("Support");
        domain.CreateMutation().AddType(new Primitive(domain, "string", TypeCategory.Text)).Apply();

        var run = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(run.HasErrors).IsFalse();
        await Assert.That(run.Telemetry.Passes.Count).IsGreaterThan(0);
        await Assert.That(run.Telemetry.TotalElapsed).IsGreaterThanOrEqualTo(TimeSpan.Zero);
        await Assert.That(run.Telemetry.Incremental).IsFalse();

        var structural = run.Telemetry.Passes.FirstOrDefault(pass => pass.PassName == "StructuralDomainAnalyzer");
        await Assert.That(structural is not null).IsTrue();
    }

    [Test]
    public async Task Analyze_Incremental_TracksInvalidatedCount() {
        var domain = new Domain("Support");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");

        domain.CreateMutation().AddType(stringType).AddType(entity).Apply();

        var initial = DomainModelAnalyzer.Analyze(domain);

        var title = new Property(domain, "Title", stringType);
        domain.CreateMutation().AddProperty(entity, title).Apply(initial);

        var run = DomainModelAnalyzer.Analyze(domain, initial, [title]);

        await Assert.That(run.Telemetry.Incremental).IsTrue();
        await Assert.That(run.Telemetry.InvalidatedNodeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Analyze_Invalidity_ExposesDiagnostics() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();
        mutation.AddType(new Primitive(domain, "string", TypeCategory.Text));
        mutation.AddType(new Primitive(domain, "string", TypeCategory.Text));

        var analysis = mutation.Apply();

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(analysis.Diagnostics.Count).IsGreaterThan(0);
        await Assert.That(analysis.Diagnostics.Any(static diagnostic => diagnostic.Message.Contains("Duplicate", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task DomainDiff_ReportsAddedRemovedAndChangedByNodeId() {
        var domainId = NodeId.NewId();
        var stringTypeId = NodeId.NewId();
        var legacyTypeId = NodeId.NewId();
        var newTypeId = NodeId.NewId();
        var entityId = NodeId.NewId();

        var before = new Domain("Support") { Id = domainId };
        var beforeString = new Primitive(before, "string", TypeCategory.Text) { Id = stringTypeId };
        var beforeLegacy = new Primitive(before, "legacy", TypeCategory.Text) { Id = legacyTypeId };
        var beforeEntity = new Entity(before, "Ticket") { Id = entityId };

        before.CreateMutation()
            .AddType(beforeString)
            .AddType(beforeLegacy)
            .AddType(beforeEntity)
            .Apply();

        var after = new Domain("Support") { Id = domainId };
        var afterString = new Primitive(after, "string", TypeCategory.Text) { Id = stringTypeId };
        var afterNew = new Primitive(after, "new", TypeCategory.Text) { Id = newTypeId };
        var afterEntity = new Entity(after, "TicketV2") { Id = entityId };

        after.CreateMutation()
            .AddType(afterString)
            .AddType(afterNew)
            .AddType(afterEntity)
            .Apply();

        var diff = DomainDiffUtil.Compare(before, after);

        await Assert.That(diff.Added.Any(entry => entry.NodeId == newTypeId)).IsTrue();
        await Assert.That(diff.Removed.Any(entry => entry.NodeId == legacyTypeId)).IsTrue();
        await Assert.That(diff.Changed.Any(entry => entry.NodeId == entityId)).IsTrue();
    }

    [Test]
    public async Task DomainDiff_AttachesDiagnosticsForChangedNodes() {
        var domainId = NodeId.NewId();
        var entityId = NodeId.NewId();
        var propId = NodeId.NewId();
        var localTypeId = NodeId.NewId();

        var before = new Domain("Support") { Id = domainId };
        var beforeType = new Primitive(before, "string", TypeCategory.Text) { Id = localTypeId };
        var beforeEntity = new Entity(before, "Ticket") { Id = entityId };
        var beforeProp = new Property(before, "Status", beforeType) { Id = propId };

        before.CreateMutation().AddType(beforeType).AddType(beforeEntity).AddProperty(beforeEntity, beforeProp).Apply();

        var after = new Domain("Support") { Id = domainId };
        var afterType = new Primitive(after, "string", TypeCategory.Text) { Id = localTypeId };
        var afterEntity = new Entity(after, "Ticket") { Id = entityId };
        var foreignDomain = new Domain("Foreign");
        var foreignType = new Primitive(foreignDomain, "foreign", TypeCategory.Text);
        var afterProp = new Property(after, "StatusRenamed", foreignType) { Id = propId };

        new Domain.AddTypeCommand(after, afterType).Apply();
        new Domain.AddTypeCommand(after, afterEntity).Apply();
        new Entity.AddPropertyCommand(afterEntity, afterProp).Apply();

        var analysis = DomainModelAnalyzer.Analyze(after);
        var diff = DomainDiffUtil.Compare(before, after, analysis);
        var changedProperty = diff.Changed.FirstOrDefault(change => change.NodeId == propId);

        await Assert.That(changedProperty is not null).IsTrue();
        await Assert.That(changedProperty!.RelatedDiagnostics.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task AnalyzeWithTelemetry_PropertyTypeFromDifferentDomain_ReportsCompatibilityError() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var foreign = new Domain("Foreign");
        var foreignType = new Primitive(foreign, "ForeignText", TypeCategory.Text);

        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddPropertyCommand(entity, new Property(domain, "Title", foreignType)).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility &&
            d.Message.Contains("different domain", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AnalyzeWithTelemetry_PrimitiveWithNullableCategory_ReportsCompatibilityError() {
        var domain = new Domain("Support");
        var invalidPrimitive = new Primitive(domain, "NullableText", TypeCategory.Text | TypeCategory.Nullable);

        new Domain.AddTypeCommand(domain, invalidPrimitive).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility &&
            d.Message.Contains("RequiredConstraint", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AnalyzeWithTelemetry_PrimitiveWithCollectionCategory_ReportsCompatibilityError() {
        var domain = new Domain("Support");
        var invalidPrimitive = new Primitive(domain, "TextListLike", TypeCategory.Text | TypeCategory.Collection);

        new Domain.AddTypeCommand(domain, invalidPrimitive).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.HasErrors).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility &&
            d.Message.Contains("relationships", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ApplyWithTrace_LongMutationSequence_StaysDeterministic() {
        var domain = new Domain("Scale");
        var analysis = DomainModelAnalyzer.Analyze(domain);

        for (var i = 0; i < 120; i++) {
            var execution = domain.CreateMutation()
                .AddType(new Primitive(domain, $"Type{i}", TypeCategory.Text))
                .ApplyWithTrace(analysis);

            analysis = execution.Analysis;

            await Assert.That(execution.Trace.Succeeded).IsTrue();
            await Assert.That(execution.Trace.RolledBack).IsFalse();
            await Assert.That(execution.Trace.AppliedStepCount).IsEqualTo(1);
            await Assert.That(execution.Trace.AffectedNodeIds.Count).IsGreaterThanOrEqualTo(1);
        }

        await Assert.That(analysis.HasErrors).IsFalse();
        await Assert.That(domain.GetAvailablePrimitives().Count()).IsEqualTo(120);
    }

    [Test]
    public async Task DomainDiff_SnapshotsAcrossLongSequence_ReportsExpectedAdds() {
        var domain = new Domain("ScaleDiff");
        var baseline = DomainDiffUtil.CaptureSnapshot(domain);

        for (var i = 0; i < 100; i++) {
            domain.CreateMutation()
                .AddType(new Entity(domain, $"Entity{i}"))
                .Apply();
        }

        var latest = DomainDiffUtil.CaptureSnapshot(domain);
        var diff = DomainDiffUtil.CompareSnapshots(baseline, latest, analysis: null);

        await Assert.That(diff.Added.Count).IsEqualTo(100);
        await Assert.That(diff.Removed.Count).IsEqualTo(0);
        await Assert.That(diff.Changed.Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task DomainModelAnalysis_TraversalAnalyzer_VisitsNestedEffectTreeNodes() {
        var domain = new Domain("Traversal");
        var textType = new Primitive(domain, "Text", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", textType);
        var action = new DomainAction(domain, "UpdateTitle", entity);

        var assign = new Assign(domain) {
            Target = title,
            Value = new Property(domain, "NewTitle", textType)
        };

        var nestedComposite = new Composite(domain);
        nestedComposite.AddEffect(assign);

        var conditional = new EffectConditional(domain) {
            Condition = new Constant(true)
        };
        conditional.AddEffect(nestedComposite);

        var rootComposite = new Composite(domain);
        rootComposite.AddEffect(conditional);

        domain.CreateMutation()
            .AddType(textType)
            .AddType(entity)
            .AddProperty(entity, title)
            .AddAction(entity, action)
            .AddEffect(action, rootComposite)
            .Apply();

        var traversal = new TreeTraversalCoverageAnalyzer();
        var analysis = new AnalyzerBuilder()
            .UseIncrementalAnalysis()
            .UseDomainModelValidation()
            .AddAnalyzer(traversal)
            .Build()
            .Analyze(domain);
        await Assert.That(analysis.HasErrors).IsFalse();

        var expectedNodes = new Node[] {
            domain,
            textType,
            entity,
            title,
            action,
            rootComposite,
            conditional,
            nestedComposite,
            assign
        };

        foreach (var expected in expectedNodes) {
            await Assert.That(traversal.VisitedNodeIds.Contains(expected.Id)).IsTrue();
        }
    }

    private sealed class TreeTraversalCoverageAnalyzer : INodeAnalyzer {
        public static string PassId => "TestTreeTraversalCoverage";
        public HashSet<NodeId> VisitedNodeIds { get; } = new();

        public void Analyze(AnalysisContext context, Node node) {
            if (!context.TryBeginAnalyzerVisit<TreeTraversalCoverageAnalyzer>(node)) {
                return;
            }

            VisitedNodeIds.Add(node.Id);
            this.AnalyzeChildren(context, node);
        }
    }
}