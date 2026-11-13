using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
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
    public async Task Multiply_FloatDecimal_StampsNothing_AndStillAccepts_WhileTextIsRejected() {
        var (accepted, node, _) = Analyze(() => DomainExpression.Multiply(P("F"), P("D")));
        var (rejected, _, _) = Analyze(() => DomainExpression.Multiply(P("F"), P("T")));

        // The accepted-but-none pair: no operator is stated, and the analyzer still accepts it.
        await Assert.That(accepted.GetMetadata<TypedExpressionMetadata>(node)?.Binary).IsNull();
        await Assert.That(accepted.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error
            && d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility)).IsFalse();
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
        // CheckMatch walks the arms (stamping `N + M`) before it infers their types, so the stamp exists when
        // InferType reads the node. Had it been read back as a typed Number, the Number arm and the Text `else`
        // would not unify (DMMAT002); on master the arithmetic arm is Unknown and the match is accepted.
        var add = DomainExpression.Add(P("N"), P("M"));
        var match = DomainExpression.Match([new MatchArm(DomainExpression.Literal(true), add)], DomainExpression.Literal("x"));
        var host = new Entity("H",
            [new Property("N", new DomainTypeReference("Number"), []), new Property("M", new DomainTypeReference("Number"), [])],
            [],
            [new Policy("Check", match)],
            []);
        var domain = new Domain("T", [new DmPrimitive("Number", PrimitiveSemantics.Number, []), host]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var stamp = analysis.GetMetadata<TypedExpressionMetadata>(add);
        await Assert.That(stamp?.Binary).IsNotNull();
        await Assert.That(stamp!.Type.DomainName).IsNull();
        await Assert.That(stamp.Binary!.Result.DomainName).IsNull();
        // T-05a (#679 lock 6841065713, flips rule A-match-arm): InferType types `N + M` itself, through the operator
        // lookup the stamp also makes, so the Number arm and the Text `else` no longer unify. Master accepted the
        // match ("ok") only because the arm was Unknown. The stamp and InferType now agree by construction, so this
        // verdict no longer distinguishes a read-back; the stamp's shape above is what still pins it.
        await Assert.That(AnalyzerBehaviourPinTests.Verdict(analysis)).IsEqualTo("DMMAT002");
    }
}
