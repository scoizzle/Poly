using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #367 D-1: analysis must not read a constraint kind it does not model as satisfied.
/// An unmodelled kind is Undecided: never a violation (no false errors), never silent (DMCS003 warning).
/// </summary>
public class ConstraintValidationFailClosedTests {
    private sealed record UnmodelledConstraint : Constraint;

    private static readonly DmPrimitiveType Text = new("Text", PrimitiveSemantics.Text, []);
    private static readonly DmPrimitiveType Number = new("Number", PrimitiveSemantics.Number, []);

    [Test]
    public async Task Evaluate_UnmodelledKind_IsUndecidedNotSatisfied() {
        var verdict = ConstraintValidation.Evaluate(new UnmodelledConstraint(), 1L);

        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Undecided);
    }

    [Test]
    public async Task Evaluate_ModelledKinds_KeepTheirVerdicts() {
        await Assert.That(ConstraintValidation.Evaluate(new RangeConstraint(0, 10), 5L))
            .IsEqualTo(ConstraintVerdict.Satisfied);
        await Assert.That(ConstraintValidation.Evaluate(new RangeConstraint(0, 10), 50L))
            .IsEqualTo(ConstraintVerdict.Violated);
        await Assert.That(ConstraintValidation.Evaluate(new UniqueConstraint(), 1L))
            .IsEqualTo(ConstraintVerdict.Satisfied);
    }

    [Test]
    public async Task Assign_LiteralToPropertyWithUnmodelledConstraint_WarnsUndecidedAndDoesNotError() {
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"), [new UnmodelledConstraint()])],
            [new DmAction("SetTotal", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Literal(200))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintUndecided &&
            d.Severity == DiagnosticSeverity.Warning)).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
        // The fixture's own record has no compiler and no coverage row, so C-05 reports it: the
        // fixture used to rely on the pass being silent about a constraint nothing compiles.
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintNoCompiler
            && d.Severity == DiagnosticSeverity.Error)).IsTrue();
    }

    [Test]
    public async Task Property_EqualityWithUnmodelledConstraint_WarnsUndecidedAndDoesNotError() {
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new EqualityConstraint(5L), new UnmodelledConstraint()])],
            [],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintUndecided &&
            d.Severity == DiagnosticSeverity.Warning)).IsTrue();
        // C-05: the fixture's record has no compiler, so this is a DMLIB001 error too. Its premise
        // ("an unmodelled kind never errors") was written against the silent drop the pass removes.
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintNoCompiler
            && d.Severity == DiagnosticSeverity.Error)).IsTrue();
    }
}