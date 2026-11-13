using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using Assignment = Poly.Ast.Nodes.Assignment;
using Constant = Poly.Ast.Nodes.Constant;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;
using PrimType = Poly.Introspection.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// GateParity (#679, T-05): a document analysis accepts is accepted by every door, the VM, the generated C# and
/// <c>polyc check</c>. <see cref="GateParity"/> runs them; each row here is a document and the steps it simulates.
/// </summary>
public class AnalysisGateParityTests {
    private static bool Evaluated(HarnessStep step) =>
        step.Data?["result"] is { } result
            ? result.GetValue<bool>()
            : throw new InvalidOperationException($"Evaluate step {step.Index} has no 'result': {step.Data?.ToJsonString()} ({step.Summary})");

    private static readonly SimStep[] OpenTwiceThenEvaluate = [
        SimStep.Create("Cust", alias: "c"),
        SimStep.Invoke("c", "Open"),
        SimStep.Invoke("c", "Open"),
        SimStep.Evaluate("Cust", "Big", "c"),
        SimStep.Evaluate("Cust", "Exact", "c"),
    ];

    private static string Bank(string balanceType, string exact) => $$"""
        domain T
        Cust: entity {
          accounts: many Acct
          Big: policy { sum accounts Bal > 100 }
          Exact: policy { sum accounts Bal == {{exact}} }
          Open: action { create in accounts { Bal: {{(balanceType == "Decimal" ? "1.5" : "2")}} } }
        }
        Acct: entity {
          Bal: {{balanceType}}
        }
        """;

    /// <summary>The control row: the same aggregate over a Number property, every door agrees on master.</summary>
    [Test]
    public async Task SumOverNumberProperty_AnalysesAndSimulatesAndExportsAgree() {
        var result = GateParity.Check(Bank("Number", "4"), OpenTwiceThenEvaluate);

        await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
        await Assert.That(result.Holds).IsTrue().Because(result.ToString());
    }

    /// <summary>
    /// #510: <c>sum accounts Bal</c> over a Decimal property analyses clean, so every door must run it, and the
    /// sum is the exact Decimal sum (1.5 + 1.5 == 3.0), not an Int64 accumulator the VM refuses.
    /// </summary>
    [Test]
    public async Task SumOverDecimalProperty_AnalysesAndSimulatesAndExportsAgree() {
        var result = GateParity.Check(Bank("Decimal", "3.0"), OpenTwiceThenEvaluate);

        await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
        await Assert.That(result.Holds).IsTrue().Because(result.ToString());

        var surface = PolySurfaceHarness.New();
        var workspace = surface.Open("Bank");
        await Assert.That(surface.Define(workspace, Bank("Decimal", "3.0")).Success).IsTrue();
        var run = surface.Simulate(workspace, OpenTwiceThenEvaluate);
        await Assert.That(Evaluated(run.Step(3))).IsFalse();
        await Assert.That(Evaluated(run.Step(4))).IsTrue();
    }

    private static (Domain Domain, SumExpr Sum) SumDomain() {
        var sum = (SumExpr)DomainExpression.Sum("items", "Amount");
        var item = new Entity("Item", [new Property("Amount", new DomainTypeReference("Decimal"), [])], [], [], []);
        var host = new Entity("Host", [],
            [], [new Policy("Big", DomainExpression.GreaterThan(sum, DomainExpression.Literal(100L)))], []);
        var domain = DomainTestFactory.Create("T", [
            new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []), item, host,
        ], [new Relationship("items", new DomainTypeReference("Host"), new DomainTypeReference("Item"), RelationshipCardinality.OneToMany, [])]);
        return (domain, sum);
    }

    /// <summary>The seed lowering gives the sum's accumulator, read from the lowered policy body.</summary>
    private static object? LoweredSeed(Domain domain, AnalysisResult analysis, SumExpr sum) {
        var lowered = new DomainExpressionLoweringPass(new LoweringContext(
                new Poly.Ast.Nodes.Parameter("this"), analysis, domain, SourceEntityName: "Host"))
            .LowerExpression(sum, new Poly.Ast.Nodes.Parameter("this"));
        var seed = lowered.Statements.OfType<Assignment>().FirstOrDefault()
            ?? throw new InvalidOperationException("The lowered sum has no accumulator assignment.");
        return seed.Value is Constant constant
            ? constant.Value
            : throw new InvalidOperationException($"The sum's seed is not a constant: {seed.Value}.");
    }

    /// <summary>
    /// Fork B (honoured): a pack that maps <c>Decimal</c> to <c>double</c> changes the sum's type for analysis and
    /// lowering alike. Analysis types <c>sum items Amount</c> as Float64 and lowering seeds the accumulator 0.0.
    /// </summary>
    [Test]
    public async Task Sum_UnderClrOverride_AnalysisAndLoweringAgree() {
        var (domain, sum) = SumDomain();
        var builder = SessionBuilder.CreateEmpty().LoadCore();
        builder.TypeMaps.OverrideClrTypeName("Decimal", "double");
        var analysis = builder.Build().Analyze(domain);

        var typed = analysis.GetMetadata<TypedExpressionMetadata>(sum);
        await Assert.That(typed).IsNotNull().Because("analysis states the sum's type on the node");
        await Assert.That(typed!.Type.Definition?.PrimitiveType).IsEqualTo(PrimType.Float64);
        await Assert.That(LoweredSeed(domain, analysis, sum)).IsEqualTo(0.0);
    }

    /// <summary>Without an override the same sum is Decimal for both, and the seed is 0m.</summary>
    [Test]
    public async Task Sum_WithoutOverride_AnalysisAndLoweringAgreeOnDecimal() {
        var (domain, sum) = SumDomain();
        var analysis = SessionBuilder.CreateEmpty().LoadCore().Build().Analyze(domain);

        var typed = analysis.GetMetadata<TypedExpressionMetadata>(sum);
        await Assert.That(typed).IsNotNull().Because("analysis states the sum's type on the node");
        await Assert.That(typed!.Type.Definition?.PrimitiveType).IsEqualTo(PrimType.Decimal);
        await Assert.That(LoweredSeed(domain, analysis, sum)).IsEqualTo(0m);
    }
}