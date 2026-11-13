using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Introspection;

using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-04: the analyzer stamps arithmetic nodes with a <see cref="BinaryTyping"/> (the result type, the numeric
/// kind, integer division, overflow possibility) and never gates on it. An absent stamp means the analyzer did
/// not state an operator; it never means "operator undefined" or "unknown type".
/// </summary>
public class TypedExpressionMetadataTests {
    // Number, Decimal and Text are declared; Float is the undeclared allowlist name (a double), whose reference
    // error is the one baseline diagnostic of every domain here (and no type error).
    private const string BaselineReferenceError = "DMSEM005";

    private static (AnalysisResult Analysis, DomainExpression Expression, DomainExpression Policy) Analyze(Func<DomainExpression> build) {
        var expression = build();
        var policyExpression = DomainExpression.Equal(expression, DomainExpression.Literal(1L));
        var host = new Entity("H",
            [
                new Property("N", new DomainTypeReference("Number"), []),
                new Property("M", new DomainTypeReference("Number"), []),
                new Property("D", new DomainTypeReference("Decimal"), []),
                new Property("F", new DomainTypeReference("Float"), []),
                new Property("T", new DomainTypeReference("Text"), []),
            ],
            [],
            [new Policy("Check", policyExpression)],
            []);
        var domain = new Domain("T", [
            new DmPrimitive("Number", PrimitiveSemantics.Number, []),
            new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
            new DmPrimitive("Text", PrimitiveSemantics.Text, []),
            host]);
        return (DomainModelAnalyzer.Analyze(domain), expression, policyExpression);
    }

    private static DomainExpression P(string name) => DomainExpression.Property(name);

    [Test]
    public async Task Divide_NumberNumber_StampsIntegerDivision() {
        var (analysis, node, _) = Analyze(() => DomainExpression.Divide(P("N"), P("M")));

        var stamp = analysis.GetMetadata<TypedExpressionMetadata>(node);
        await Assert.That(stamp).IsNotNull();
        await Assert.That(stamp!.Binary).IsNotNull();
        await Assert.That(stamp.Binary!.IsIntegerDivision).IsTrue();
        await Assert.That(stamp.Binary.MayOverflow).IsFalse();
        await Assert.That(stamp.Binary.Kind).IsEqualTo(NumericKind.Integer64);
        await Assert.That(stamp.Binary.Checked).IsFalse();
        await Assert.That(AnalyzerBehaviourPinTests.Verdict(analysis)).IsEqualTo(BaselineReferenceError);
    }

    [Test]
    public async Task Add_NumberNumber_StampsPossibleOverflow_AndNoIntegerDivision() {
        var (analysis, node, _) = Analyze(() => DomainExpression.Add(P("N"), P("M")));

        var binary = analysis.GetMetadata<TypedExpressionMetadata>(node)?.Binary;
        await Assert.That(binary).IsNotNull();
        await Assert.That(binary!.MayOverflow).IsTrue();
        await Assert.That(binary.IsIntegerDivision).IsFalse();
        await Assert.That(binary.Kind).IsEqualTo(NumericKind.Integer64);
    }

    [Test]
    public async Task Add_DecimalNumber_StampsDecimalResult() {
        var (analysis, node, _) = Analyze(() => DomainExpression.Add(P("D"), P("N")));

        var binary = analysis.GetMetadata<TypedExpressionMetadata>(node)?.Binary;
        await Assert.That(binary).IsNotNull();
        await Assert.That(binary!.Kind).IsEqualTo(NumericKind.Decimal);
        await Assert.That(binary.Result.Definition!.PrimitiveType).IsEqualTo(Poly.Introspection.PrimitiveType.Decimal);
        await Assert.That(binary.MayOverflow).IsFalse();
        await Assert.That(AnalyzerBehaviourPinTests.Verdict(analysis)).IsEqualTo(BaselineReferenceError);
    }

    [Test]
    public async Task Multiply_FloatDecimal_StampsNothing_AndIsRejected_WhileTextIsRejected() {
        var (untyped, node, _) = Analyze(() => DomainExpression.Multiply(P("F"), P("D")));
        var (rejected, _, _) = Analyze(() => DomainExpression.Multiply(P("F"), P("T")));

        // No operator is defined for Float with Decimal, so none is stated. T-05b (fork F2): the product has no type, so
        // its consumer rejects it with DMSEM006 naming the operands (the lowered analysis already refused it, T-18).
        await Assert.That(untyped.GetMetadata<TypedExpressionMetadata>(node)?.Binary).IsNull();
        await Assert.That(untyped.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error
            && d.Code == DomainModelDiagnosticCodes.SemanticUntypedExpression
            && d.Message.Contains("no arithmetic operator is defined for 'Number' and 'Decimal'", StringComparison.Ordinal))).IsTrue();
        // A positive pairing: the same shape over Text is rejected, so "stamps nothing" is not vacuous.
        await Assert.That(rejected.Diagnostics.Any(d => d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsTrue();
    }

    [Test]
    public async Task ComparisonAndPolicyNodes_StampNoBinary() {
        var (analysis, add, policy) = Analyze(() => DomainExpression.Add(P("N"), P("M")));

        await Assert.That(analysis.GetMetadata<TypedExpressionMetadata>(add)?.Binary).IsNotNull();
        await Assert.That(analysis.GetMetadata<TypedExpressionMetadata>(policy)?.Binary).IsNull();
    }

    [Test]
    public async Task ArithmeticStamp_IsNotReadBackByInferType() {
        // One `require` policy tree walked under two parameter scopes (#483): A1 binds Amt to Number, A2 to Decimal.
        // The stamp is first-writer-wins, so `Amt + 1` still carries A1's Int64 stamp when A2's walk infers it. Since
        // T-05a InferType types arithmetic itself; were the stamp read back, A2's comparison would name A1's
        // 'Number'. It names its own 'Decimal' (Decimal + Int64), so the type comes from inference, not the stamp.
        var add = DomainExpression.Add(DomainExpression.Property("Amt"), DomainExpression.Literal(1L));
        var check = new Policy("Check", DomainExpression.Equal(add, DomainExpression.Property("Name")));
        var host = new Entity("H",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            [
                new Poly.DomainModeling.Ontology.Action("A1", InvocationResult.Void,
                    [new Property("Amt", new DomainTypeReference("Number"), [])], [], [check]),
                new Poly.DomainModeling.Ontology.Action("A2", InvocationResult.Void,
                    [new Property("Amt", new DomainTypeReference("Decimal"), [])], [], [check]),
            ],
            [],
            []);
        var domain = new Domain("T", [
            new DmPrimitive("Number", PrimitiveSemantics.Number, []),
            new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
            new DmPrimitive("Text", PrimitiveSemantics.Text, []),
            host]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var stamp = analysis.GetMetadata<TypedExpressionMetadata>(add);
        await Assert.That(stamp?.Binary).IsNotNull();
        await Assert.That(stamp!.Type.DomainName).IsNull();
        await Assert.That(stamp.Binary!.Result.DomainName).IsNull();
        // The stamp is A1's: the walk under A2 found it already there.
        await Assert.That(stamp.Binary.Kind).IsEqualTo(NumericKind.Integer64);
        var messages = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)
            .Select(d => d.Message)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(string.Join(" | ", messages)).IsEqualTo(
            "comparison between incompatible types 'Decimal' and 'Text' | comparison between incompatible types 'Number' and 'Text'");
    }
    private static string[] DecimalDivisionWarnings(string assignment, string extraMembers = "") {
        var dsl = $$"""
            domain T
            E: entity {
              N: Number
              D: Decimal
              {{extraMembers}}
              Go: action { {{assignment}} }
            }
            """;
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return [.. result.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.NumericIntegerDivisionIntoDecimal)
            .Select(d => $"{d.Severity}: {d.Message}")];
    }

    /// <summary>
    /// T-07a (#657): an integral division stored into a Decimal truncates before it widens (<c>7 / 2</c> stores 3), a
    /// warning DMNUM001 naming the division; it reaches the integral division inside a sum (<c>7 / 2 + 1</c> stores 4).
    /// </summary>
    [Test]
    [Arguments("assign D to 7 / 2", "Warning: integer division '(7 / 2)' is stored into a Decimal: the quotient is truncated first; give one operand a Decimal")]
    [Arguments("assign D to N / 2", "Warning: integer division '(N / 2)' is stored into a Decimal: the quotient is truncated first; give one operand a Decimal")]
    [Arguments("assign D to 7 / 2 + 1", "Warning: integer division '(7 / 2)' is stored into a Decimal: the quotient is truncated first; give one operand a Decimal")]
    public async Task Assign_IntegralDivisionToDecimal_WarnsDmnum001(string assignment, string expected) =>
        await Assert.That(string.Join(" | ", DecimalDivisionWarnings(assignment))).IsEqualTo(expected);

    /// <summary>A division with a Decimal operand is exact, and an integral division into a Number is the language's meaning: no warning.</summary>
    [Test]
    [Arguments("assign D to D / 2")]
    [Arguments("assign D to 7.5 / 2")]
    [Arguments("assign N to 7 / 2")]
    public async Task Assign_DecimalDivisionOrNumberTarget_IsClean(string assignment) =>
        await Assert.That(DecimalDivisionWarnings(assignment)).IsEmpty();
}