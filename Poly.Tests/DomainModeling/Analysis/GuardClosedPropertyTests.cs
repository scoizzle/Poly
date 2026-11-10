using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367 C-1 guard-closed fact: a property is closed only when every writer is proven to keep it within its
/// declared constraints. Default false; each writer kind that cannot be proven is pinned as a blocker.
/// </summary>
public class GuardClosedPropertyTests {
    private static Property Prop(string name, string type, params Constraint[] constraints) =>
        new(name, new DomainTypeReference(type), constraints);

    private static AssignEffect Assign(string property, DomainExpression value) =>
        new(DomainExpression.Property(property), value);

    private static Action Act(string name, IReadOnlyList<Property>? parameters = null, params Effect[] effects) =>
        new(name, InvocationResult.Void, parameters ?? [], effects, []);

    private static (Entity Entity, Domain Domain) Build(
        IReadOnlyList<Property> properties, IReadOnlyList<Action>? actions = null, IReadOnlyList<Stage>? stages = null) {
        var entity = new Entity("Item", properties, actions ?? [], [], stages ?? []);
        return (entity, DomainTestFactory.Create("Test", [entity]));
    }

    private static GuardClosedProperties.Result Compute(
        IReadOnlyList<Property> properties, IReadOnlyList<Action>? actions = null, IReadOnlyList<Stage>? stages = null) {
        var (entity, domain) = Build(properties, actions, stages);
        return GuardClosedProperties.Compute(entity, domain);
    }

    private static Constraint Range(long min, long max) => new RangeConstraint(min, max);

    private static Constraint Default(object? value) => new DefaultValueConstraint(DomainExpression.Literal(value));

    [Test]
    public async Task OnlyInRangeLiteralWriters_IsClosed() {
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L))],
            [Act("Bump", null, Assign("Qty", DomainExpression.Literal(5L)))]);

        await Assert.That(result.Closed.Contains("Qty")).IsTrue();
    }

    [Test]
    public async Task NoWriterBeyondCreate_IsClosed() {
        var result = Compute([Prop("Qty", "Int", Range(0, 10))]);

        await Assert.That(result.Closed.Contains("Qty")).IsTrue();
    }

    [Test]
    public async Task OutOfRangeDefault_IsNotClosed() {
        var result = Compute([Prop("Qty", "Int", Range(0, 10), Default(50L))]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
        await Assert.That(result.Blockers["Qty"]).Contains("default");
    }

    [Test]
    public async Task OutOfRangeLiteralAssign_IsNotClosed() {
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L))],
            [Act("Bump", null, Assign("Qty", DomainExpression.Literal(99L)))]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
    }

    [Test]
    public async Task AssignFromUnconstrainedParameter_IsNotClosed() {
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L))],
            [Act("Set", [Prop("value", "Int")], Assign("Qty", DomainExpression.Parameter("value")))]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
        await Assert.That(result.Blockers["Qty"]).Contains("not a proven in-range literal");
    }

    [Test]
    public async Task AssignFromParameterWithSubsumedConstraint_IsStillNotClosed() {
        // Action parameter constraints are not enforced when an action runs (the executor only coerces
        // types), so even a subsumed parameter range proves nothing about the written value.
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L))],
            [Act("Set", [Prop("value", "Int", Range(2, 5))], Assign("Qty", DomainExpression.Parameter("value")))]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
    }

    [Test]
    public async Task AssignFromAnotherProperty_IsNotClosed() {
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L)), Prop("Other", "Int", Range(0, 10), Default(0L))],
            [Act("Copy", null, Assign("Qty", DomainExpression.Property("Other")))]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
        await Assert.That(result.Closed.Contains("Other")).IsTrue();
    }

    [Test]
    public async Task CalleeAssigningInRangeLiteral_DoesNotBlock() {
        // A callee reached by invoke is another action of its own entity: its assign is enumerated as a
        // writer like any other, so a proven literal keeps the property closed.
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L))],
            [
                Act("Callee", null, Assign("Qty", DomainExpression.Literal(7L))),
                Act("Caller", null, new InvokeActionEffect("Callee", []))
            ]);

        await Assert.That(result.Closed.Contains("Qty")).IsTrue();
    }

    [Test]
    public async Task CalleeAssigningFromParameter_IsNotClosed() {
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L))],
            [
                Act("Callee", [Prop("value", "Int", Range(0, 10))], Assign("Qty", DomainExpression.Parameter("value"))),
                Act("Caller", null, new InvokeActionEffect("Callee", [new PropertyBinding("value", DomainExpression.Literal(3L))]))
            ]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
    }

    [Test]
    public async Task AssignInsideConditionalBranch_IsEnumerated() {
        var result = Compute(
            [Prop("Qty", "Int", Range(0, 10), Default(0L))],
            [Act("Maybe", null, new ConditionalEffect(
                DomainExpression.Literal(true),
                [Assign("Qty", DomainExpression.Literal(99L))],
                null))]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
    }

    [Test]
    public async Task StageEntryAndExitAssigns_AreEnumerated() {
        var stage = new Stage("Open", [], [], [Assign("Qty", DomainExpression.Literal(99L))], []);
        var result = Compute([Prop("Qty", "Int", Range(0, 10), Default(0L))], null, [stage]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
    }

    [Test]
    public async Task RuntimeDefault_IsNotClosed() {
        var result = Compute([
            new Property("Stamp", new DomainTypeReference("Text"),
                [new OptionalConstraint(), new LengthConstraint(1, 40), new DefaultValueConstraint(DomainExpression.Property("Guid"))])
        ]);

        await Assert.That(result.Closed.Contains("Stamp")).IsFalse();
        await Assert.That(result.Blockers["Stamp"]).Contains("not a literal");
    }

    [Test]
    public async Task RangeUnion_IsNotClosed_BecauseTheGuardChecksOnlyTheHull() {
        var union = new RangeConstraint(0L, 100L) {
            Intervals = new RangeIntervals([new RangeInterval(0L, 10L), new RangeInterval(90L, 100L)])
        };
        var result = Compute([Prop("Qty", "Int", union)]);

        await Assert.That(result.Closed.Contains("Qty")).IsFalse();
        await Assert.That(result.Blockers["Qty"]).Contains("hull");
    }

    [Test]
    public async Task RelationalConstraint_IsNotClosed() {
        var result = Compute([
            Prop("Low", "Int", Range(0, 10), new RelationalConstraint("High", RelationalOrdering.Less | RelationalOrdering.Equal)),
            Prop("High", "Int", Range(0, 10))
        ]);

        await Assert.That(result.Closed.Contains("Low")).IsFalse();
        await Assert.That(result.Closed.Contains("High")).IsTrue();
    }

    [Test]
    public async Task RangeOnNonNumericProperty_IsNotClosed() {
        var result = Compute([Prop("Name", "Text", Range(0, 10))]);

        await Assert.That(result.Closed.Contains("Name")).IsFalse();
    }

    [Test]
    public async Task RequiredTextWithDefault_IsNotClosed() {
        var result = Compute([Prop("Name", "Text", new LengthConstraint(1, 10), Default("abc"))]);

        await Assert.That(result.Closed.Contains("Name")).IsFalse();
        await Assert.That(result.Blockers["Name"]).Contains("requiredness");
    }

    [Test]
    public async Task OptionalTextWithDefault_IsClosedWhenDefaultSatisfies() {
        var result = Compute([Prop("Name", "Text", new OptionalConstraint(), new LengthConstraint(1, 10), Default("abc"))]);

        await Assert.That(result.Closed.Contains("Name")).IsTrue();
    }

    [Test]
    public async Task EmptyLiteralIntoRequiredText_IsNotClosed() {
        var result = Compute(
            [Prop("Name", "Text", new LengthConstraint(0, 10))],
            [Act("Clear", null, Assign("Name", DomainExpression.Literal("")))]);

        await Assert.That(result.Closed.Contains("Name")).IsFalse();
    }

    [Test]
    public async Task EnumAndEntityTypedProperties_AreNotClosed() {
        var result = Compute([Prop("Kind", "Color")]);

        await Assert.That(result.Closed.Contains("Kind")).IsFalse();
    }

    [Test]
    public async Task ContractBoundAction_BlocksEveryPropertyOfTheEntity() {
        var (entity, domain) = Build(
            [Prop("Qty", "Int", Range(0, 10))],
            [Act("Pay")]);
        domain = domain with { ContractBindings = [new ContractBinding("PayBinding", "Gateway", "Charge", "Pay", "request", [])] };

        var result = GuardClosedProperties.Compute(entity, domain);

        await Assert.That(result.Closed.Count).IsEqualTo(0);
        await Assert.That(result.Blockers["Qty"]).Contains("imported contract");
    }

    [Test]
    public async Task FirstStageEntryAssign_IsProvenAsAnAssign_NotACreateArgument() {
        var open = new Stage("Open", [], [], [], [Assign("Qty", DomainExpression.Literal(5L))]);
        var result = Compute([Prop("Qty", "Int", Range(1, 10))], null, [open]);

        await Assert.That(result.Closed.Contains("Qty")).IsTrue();
    }

    [Test]
    public async Task PublishedOnEntityStructureMetadata_ForACleanModel() {
        var changes = new PolyDslParser("""
            domain Test
            Item: entity {
              Qty: Number range(0, 10) default(0)
              Score: Number range(0, 10) default(0)
              Bump: action { assign Qty to 5 }
              Reset: action { assign Score to Qty }
            }
            """).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        await Assert.That(result.Succeeded).IsTrue();

        var item = result.Root.Types.OfType<Entity>().Single();
        var closed = result.Analysis.GetStructure(item)!.GuardClosedPropertyNames!;

        await Assert.That(closed.Contains("Qty")).IsTrue();
        await Assert.That(closed.Contains("Score")).IsFalse();
    }

    [Test]
    public async Task CreateGuardCoversEveryKindTheFactAccepts() {
        // Drift guard: the fact mirrors ConstraintCheckWriter's type gates. A property the fact accepts must
        // get a create-time check for each declared value constraint; one the fact rejects for a gate must not.
        var accepted = Prop("Name", "Text", new LengthConstraint(1, 5), new PatternConstraint("^[a-z]+$"), new EqualityConstraint("abc"));
        var number = Prop("Qty", "Int", Range(0, 10));
        var (entity, domain) = Build([accepted, number]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        IReadOnlyList<ConstraintCheckWriter.Kind> KindsOf(Property property) =>
            ConstraintCheckWriter.ChecksFor(
                    entity, property, domain, analysis,
                    ConstraintCheckWriter.ConstraintCheckStyle.Create,
                    new Variable("value"), nullableValue: false, valueHasDefault: false, includeRequired: true)
                .Select(check => check.Kind).ToList();

        var textKinds = KindsOf(accepted);
        var numberKinds = KindsOf(number);
        await Assert.That(textKinds).Contains(ConstraintCheckWriter.Kind.LengthMinimum);
        await Assert.That(textKinds).Contains(ConstraintCheckWriter.Kind.LengthMaximum);
        await Assert.That(textKinds).Contains(ConstraintCheckWriter.Kind.Pattern);
        await Assert.That(textKinds).Contains(ConstraintCheckWriter.Kind.Equality);
        await Assert.That(numberKinds).Contains(ConstraintCheckWriter.Kind.RangeMinimum);
        await Assert.That(numberKinds).Contains(ConstraintCheckWriter.Kind.RangeMaximum);
        await Assert.That(GuardClosedProperties.Compute(entity, domain).Closed.Contains("Qty")).IsTrue();

        var ungated = Prop("Name", "Text", Range(0, 10));
        await Assert.That(KindsOf(ungated)).DoesNotContain(ConstraintCheckWriter.Kind.RangeMinimum);
        await Assert.That(Compute([ungated]).Closed.Contains("Name")).IsFalse();
    }
}