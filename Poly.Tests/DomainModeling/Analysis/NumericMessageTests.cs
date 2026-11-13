using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-04 messages (#581, #515): a Decimal is named "Decimal" in type-mismatch messages, and an integer-valued
/// literal beyond the 64-bit range is reported as out of range rather than as fractional. Only these wordings
/// changed; every accept/reject verdict is pinned by <see cref="AnalyzerBehaviourPinTests"/>.
/// </summary>
public class NumericMessageTests {
    private static IReadOnlyList<string> AssignErrors(string targetType, string? sourceType, object? literal) {
        List<Property> props = [new("P", new DomainTypeReference(targetType), [])];
        DomainExpression value;
        if (sourceType is null) {
            value = DomainExpression.Literal(literal);
        }
        else {
            props.Add(new("Q", new DomainTypeReference(sourceType), []));
            value = DomainExpression.Property("Q");
        }
        var host = new Entity("H", props,
            [new DmAction("Set", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("P"), value)], [])],
            [], []);
        var domain = new Domain("T", [
            new DmPrimitive("Number", PrimitiveSemantics.Number, []),
            new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
            host]);
        return [.. DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)
            .Select(d => d.Message)];
    }

    [Test]
    public async Task DecimalToNumberAssign_MessageNamesDecimal() {
        var errors = AssignErrors("Number", "Decimal", null);

        await Assert.That(errors).Contains("type mismatch in assign to property 'P': cannot assign 'Decimal' to 'Number'");
    }

    [Test]
    public async Task Int32ToNumberAssign_StillReadsNumber_OnlyDecimalIsDistinct() {
        var errors = AssignErrors("Number", "Int32", null);

        await Assert.That(errors).Contains("type mismatch in assign to property 'P': cannot assign 'Number' to 'Number'");
    }

    [Test]
    public async Task OutOfRangeIntegerLiteral_ReportsOutOfRangeNotFractional() {
        var errors = AssignErrors("Number", null, 9223372036854775808m);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0]).Contains("Integer literal 9223372036854775808 is out of range for Number (64-bit)");
        await Assert.That(errors[0]).DoesNotContain("Fractional");
    }

    [Test]
    public async Task IntegerValuedFloatingLiteralBeyondLong_5_0e19_ReportsOutOfRange() {
        var errors = AssignErrors("Number", null, 5.0e19m);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0]).Contains("Integer literal 50000000000000000000 is out of range for Number (64-bit)");
    }

    [Test]
    [Arguments(5.0)]
    [Arguments(1.5)]
    public async Task FractionalOrInLongRangeDecimalLiteral_StillSaysFractional(double value) {
        var errors = AssignErrors("Number", null, (decimal)value);

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0]).Contains("Fractional literal is not legal on Number. Use Decimal.");
    }

    // The issue's own repro goes through the DSL with the bare Decimal and Number names (nothing declared).
    [Test]
    public async Task DecimalToNumberAssign_ThroughTheDsl_WithBareNames_MessageNamesDecimal() {
        var dsl = """
            domain Cat
            Item: entity {
              Amount: Decimal
              Total: Number
              Tick: action { assign Total to Amount }
            }
            """;
        var result = new Poly.DomainModeling.Evolution.DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        var errors = result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message).ToList();

        await Assert.That(errors).Contains("type mismatch in assign to property 'Total': cannot assign 'Decimal' to 'Number'");
    }

    [Test]
    public async Task OutOfRangeIntegerDefault_ReportsOutOfRange_NotFractional() {
        var host = new Entity("H",
            [new Property("P", new DomainTypeReference("Number"), [new DefaultValueConstraint(DomainExpression.Literal(9223372036854775808m))])],
            [], [], []);
        var domain = new Domain("T", [new DmPrimitive("Number", PrimitiveSemantics.Number, []), host]);
        var errors = DomainModelAnalyzer.Analyze(domain).Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message).ToList();

        await Assert.That(errors.Any(m => m.Contains("Integer literal 9223372036854775808 is out of range for Number (64-bit)"))).IsTrue();
        await Assert.That(errors.Any(m => m.Contains("Fractional"))).IsFalse();
    }
}
