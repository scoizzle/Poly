using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367 audit of every Convert.ToDouble failure-hiding site. Principle (same as ConstraintValidation.IsInRange):
/// a bound or value that cannot be ordered against a number is never skipped or passed; it is not-in-range /
/// not-verified / not-subsumed. These tests use a DateTime-bounded range on a Number property, reachable through
/// the programmatic API (the DSL only produces numeric bounds).
/// </summary>
public class ConstraintFailClosedAuditTests {
    private static readonly DmPrimitiveType Num = new("Number", PrimitiveSemantics.Number, []);
    private static readonly DateTime D1 = new(2020, 1, 1);
    private static readonly DateTime D2 = new(2021, 1, 1);

    private static Domain Dom(params DomainType[] types) => DomainTestFactory.Create("T", [Num, .. types], []);
    private static Property NumProp(string name, params Constraint[] constraints) =>
        new(name, new DomainTypeReference("Number"), constraints);
    private static DmAction Act(string name, IReadOnlyList<Property> parameters, params Effect[] effects) =>
        new(name, InvocationResult.Void, parameters, effects, []);

    private static StorageColumn QtyColumn(Domain domain) {
        var analysis = DomainModelAnalyzer.Analyze(domain);
        var topology = EffectTopologyPass.Scan(domain);
        var aggregate = OwnershipAggregatePass.BuildAggregate(domain, null, topology);
        var storage = new StorageAnalyzer(domain, analysis).Analyze(aggregate, topology);
        return storage.Entities[0].Columns.Single(c => c.Name == "Qty");
    }

    // ── StorageAnalyzer.RangeWithin / ComputeVerifiedRanges ───────────────

    [Test]
    public async Task Storage_DeclaredRangeBoundNotOrderable_IsNotVerified() {
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(D1, D2))], [], [], []);

        await Assert.That(QtyColumn(Dom(entity)).IsRangeVerified).IsFalse();
    }

    [Test]
    public async Task Storage_NumericWriterAgainstUnorderableDeclaredBound_IsNotVerified() {
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(D1, D2))],
            [Act("Set", [], new AssignEffect(DomainExpression.Property("Qty"), DomainExpression.Literal(5L)))],
            [], []);

        await Assert.That(QtyColumn(Dom(entity)).IsRangeVerified).IsFalse();
    }

    [Test]
    public async Task Storage_WriterRangeUnboundedOnOneSideAgainstBoundedDeclared_IsNotVerified() {
        // amount's lower bound cannot be ordered (DateTime), so the written range is [?, 5]: unbounded below.
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(0d, 10d))],
            [Act("Set", [NumProp("amount", new RangeConstraint(D1, 5d))],
                new AssignEffect(DomainExpression.Property("Qty"), DomainExpression.Parameter("amount")))],
            [], []);

        await Assert.That(QtyColumn(Dom(entity)).IsRangeVerified).IsFalse();
    }

    [Test]
    public async Task Storage_NumericRanges_StayVerified() {
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(0d, 100d))],
            [Act("Set", [], new AssignEffect(DomainExpression.Property("Qty"), DomainExpression.Literal(5L)))],
            [], []);

        var column = QtyColumn(Dom(entity));
        await Assert.That(column.IsRangeVerified).IsTrue();
        await Assert.That(column.VerifiedRange!.Max).IsEqualTo(100m);
    }

    // ── EffectAnalyzer.CheckDerivedRange / call-chain postcondition ───────

    [Test]
    public async Task DerivedValue_AgainstUnorderableTargetBound_IsError() {
        // 5 + 5 is the numeric range [10, 10]; the target's bounds are DateTimes, so it is not in range
        // (the literal form `assign Qty to 5` is already an error via ConstraintValidation).
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(D1, D2))],
            [Act("Set", [], new AssignEffect(DomainExpression.Property("Qty"),
                DomainExpression.Add(DomainExpression.Literal(5L), DomainExpression.Literal(5L))))],
            [], []);

        var analysis = DomainModelAnalyzer.Analyze(Dom(entity));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("is entirely outside constraint"))).IsTrue();
    }

    [Test]
    public async Task DerivedValue_AgainstNumericTarget_Within_NoDiagnostic() {
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(0d, 100d))],
            [Act("Set", [], new AssignEffect(DomainExpression.Property("Qty"),
                DomainExpression.Add(DomainExpression.Literal(5L), DomainExpression.Literal(5L))))],
            [], []);

        var analysis = DomainModelAnalyzer.Analyze(Dom(entity));

        await Assert.That(analysis.Diagnostics.Any(d => d.Severity != DiagnosticSeverity.Hint)).IsFalse();
    }

    [Test]
    public async Task CallChain_PostconditionAgainstUnorderableTargetBound_IsReported() {
        var b = Act("B", [NumProp("x")],
            new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Parameter("x")));
        var a = Act("A", [NumProp("amount", new RangeConstraint(0d, 200d))],
            new InvokeActionEffect("B", [new PropertyBinding("x", DomainExpression.Parameter("amount"))]));
        var entity = new Entity("Item",
            [NumProp("Total", new RequiredConstraint(), new RangeConstraint(D1, D2))], [a, b], [], []);

        var analysis = DomainModelAnalyzer.Analyze(Dom(entity));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Call-chain postcondition (A → B)"))).IsTrue();
    }

    [Test]
    public async Task ParameterRangeNotOrderable_AgainstProperty_StillWarnsSubsumption() {
        // Already fail-closed (EffectAnalyzer.IsRangeSubsumed returns false on an unconvertible bound): pinned.
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(D1, D2))],
            [Act("Set", [NumProp("amount", new RequiredConstraint(), new RangeConstraint(D1, D2))],
                new AssignEffect(DomainExpression.Property("Qty"), DomainExpression.Parameter("amount")))],
            [], []);

        var analysis = DomainModelAnalyzer.Analyze(Dom(entity));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("allows values outside property"))).IsTrue();
    }

    // ── ConstraintPropagationAnalyzer.AdjustRangeRange / GetLiteralValue ──

    [Test]
    public async Task Propagation_BoundCannotBeShifted_WarnsInsteadOfDroppingIt() {
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(D1, D2))],
            [Act("Set", [NumProp("amount")], new AssignEffect(DomainExpression.Property("Qty"),
                DomainExpression.Add(DomainExpression.Parameter("amount"), DomainExpression.Literal(5L))))],
            [], []);

        var analysis = DomainModelAnalyzer.Analyze(Dom(entity));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Cannot derive a bound on parameter 'amount'"))).IsTrue();
    }

    [Test]
    public async Task Propagation_OffsetLiteralNotRepresentableAsLong_WarnsInsteadOfDroppingIt() {
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(0d, 100d))],
            [Act("Set", [NumProp("amount")], new AssignEffect(DomainExpression.Property("Qty"),
                DomainExpression.Add(DomainExpression.Parameter("amount"), DomainExpression.Literal(ulong.MaxValue))))],
            [], []);

        var analysis = DomainModelAnalyzer.Analyze(Dom(entity));

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Cannot derive a bound on parameter 'amount'"))).IsTrue();
    }

    [Test]
    public async Task Propagation_NumericOffset_StillAdjustsBounds() {
        var set = Act("Set", [NumProp("amount")], new AssignEffect(DomainExpression.Property("Qty"),
            DomainExpression.Add(DomainExpression.Parameter("amount"), DomainExpression.Literal(5L))));
        var entity = new Entity("Item",
            [NumProp("Qty", new RequiredConstraint(), new RangeConstraint(0d, 100d))], [set], [], []);

        var analysis = DomainModelAnalyzer.Analyze(Dom(entity));
        var meta = analysis.GetMetadata<DownstreamConstraintsMetadata>(set.Parameters[0]);

        await Assert.That(meta).IsNotNull();
        await Assert.That(meta!.Constraints.OfType<RangeConstraint>()
            .Any(r => Equals(r.Minimum, -5m) && Equals(r.Maximum, 95m))).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d => d.Message.Contains("Cannot derive a bound"))).IsFalse();
    }
}