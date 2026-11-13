using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
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

    /// <summary>#510 as filed: the issue's document and its simulate steps, through every door.</summary>
    [Test]
    public async Task Issue510_TheFiledDocument_EveryDoorAccepts() {
        var result = GateParity.Check("""
            domain T
            Cust: entity {
              accounts: many Acct
              Big: policy { sum accounts Bal > 100 }
              Open: action { create in accounts { Bal: 1.5 } }
            }
            Acct: entity {
              Bal: Decimal
            }
            """,
            SimStep.Create("Cust", alias: "c"), SimStep.Invoke("c", "Open"), SimStep.Evaluate("Cust", "Big", "c"));

        await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
        await Assert.That(result.Holds).IsTrue().Because(result.ToString());
    }

    /// <summary>
    /// Seam N1 (lock #679 6841065713, amendment 10): <c>null</c> compared with a value type is accepted by analysis
    /// through T-05 (the leniency for entity, value type and Binary against <c>null</c>); T-06 owns removing that
    /// allowance. Pinned here: the VM and the generated C# both accept it too, so no door disagrees today (no
    /// CS0019: the generated value type compares with <c>null</c>). A value type cannot be a stored property
    /// (DMSTR006), so the reachable shapes are an action parameter and a function parameter.
    /// </summary>
    [Test]
    [Arguments("action parameter", """
        domain T
        Money: value {
          Amount: Number
        }
        Acct: entity {
          N: Number
          Set: action (m: Money) { if (m == null) { assign N to 1 } }
        }
        """, true)]
    [Arguments("function parameter", """
        domain T
        Money: value {
          Amount: Number
        }
        Acct: entity {
          N: Number
        }
        function IsNone(m: Money): Boolean = m == null
        """, false)]
    public async Task ValueTypeComparedToNull_N1_IsAcceptedByEveryDoor_UntilT06(string shape, string dsl, bool invoke) {
        SimStep[] steps = invoke
            ? [SimStep.Create("Acct", """{"N":0}""", "a"), SimStep.Invoke("a", "Set", """{"m":{"Amount":2}}""")]
            : [SimStep.Create("Acct", """{"N":0}""", "a")];
        var result = GateParity.Check(dsl, steps);

        await Assert.That(result.Analysis).IsTrue().Because($"{shape}: {result}");
        await Assert.That(result.Simulate is true).IsTrue().Because($"{shape}: {result}");
        await Assert.That(result.CSharp is true).IsTrue().Because($"{shape}: {result}");
        await Assert.That(result.PolycCheck is true).IsTrue().Because($"{shape}: {result}");
    }

    private static string DateDocument(string members) => $$"""
        domain T
        uses temporal
        Part: entity {
          Due: Date
          Stamp: DateTime
        }
        Item: entity {
          A: Number
          Due: Date
          Stamp: DateTime
          Gap: Duration optional
          part: Part
          {{members}}
        }
        """;

    /// <summary>
    /// Review F1 (fix round 1). A date minus a date is a difference only as two properties: that is the shape lowering
    /// rewrites to <c>left.Subtract(right)</c> (<c>TemporalLowering.DateAddDaysLowering</c>). Any other shape lowers
    /// to a raw subtraction, and the VM refuses it. With the analyzer's exemption widened to every date-typed operand
    /// (this round, then reverted), GateParity recorded these rows: <c>part Stamp - Stamp</c> and
    /// <c>(Stamp + A) - Stamp</c> failed <c>polyc check</c> ("arithmetic operand is not numeric (got 'DateTime' and
    /// 'DateTime')"), and <c>part Due - Due</c> also failed the generated C# (CS0019 on <c>DateOnly - DateOnly</c>).
    /// So analysis rejects them, and the two-property DateTime control passes every door. T-10 owns deciding a date
    /// difference on types, in analysis and lowering together.
    /// </summary>
    [Test]
    [Arguments("the review's repro", "Ok: policy { (part Due - Due) == (Due - Due) }", false)]
    [Arguments("a path-prefixed operand", "Close: action { assign Gap to part Stamp - Stamp }", false)]
    [Arguments("a parenthesised operand", "Close: action { assign Gap to (Stamp + A) - Stamp }", false)]
    [Arguments("control: two DateTime properties", "Close: action { assign Gap to Stamp - Stamp }", true)]
    public async Task DateDifference_AnalysisAcceptsOnlyTheShapeLoweringLowers_UntilT10(string row, string members, bool accepted) {
        var result = GateParity.Check(DateDocument(members),
            SimStep.Create("Item", """{"A":1,"Due":"2026-01-01","Stamp":"2026-01-01T00:00:00Z"}""", "i"));

        await Assert.That(result.Analysis).IsEqualTo(accepted).Because($"{row}: {result}");
        await Assert.That(result.Holds).IsTrue().Because($"{row}: {result}");
    }

    private static IReadOnlyList<string> Errors(string members) =>
        [.. new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(DateDocument(members)).Parse()).Analysis.Diagnostics
            .Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
            .Select(d => $"{d.Code}: {d.Message}")];

    /// <summary>
    /// Closing round, item 1: a same-family date difference of another shape is rejected with the reason, the shape
    /// lowering supports, naming the two operands the user wrote, not "arithmetic operand is not numeric".
    /// </summary>
    [Test]
    public async Task DateDifference_OfAnotherShape_SaysOnlyTwoPropertiesAreSupported() {
        await Assert.That(string.Join(" | ", Errors("Close: action { assign Gap to part Stamp - Stamp }"))).IsEqualTo(
            "DMSEM003: a date difference is supported only between two properties of the same entity " +
            "(got 'part Stamp' - 'Stamp')");
        await Assert.That(string.Join(" | ", Errors("Close: action { assign Gap to (Stamp + A) - Stamp }"))).IsEqualTo(
            "DMSEM003: a date difference is supported only between two properties of the same entity " +
            "(got 'Stamp + A' - 'Stamp')");
        await Assert.That(Errors("Close: action { assign Gap to Stamp - Stamp }").Count).IsEqualTo(0);
    }

    /// <summary>
    /// Found, not fixed (owner T-10): two Date (DateOnly) properties subtracted are accepted by analysis, lowered to
    /// <c>Due.Subtract(Due)</c>, and refused by the other doors, because DateOnly has no subtraction. This is master's
    /// gap, pinned so it cannot change unseen; T-10's temporal rule set removes it.
    /// </summary>
    [Test]
    public async Task DateOnlyDifferenceOfTwoProperties_AnalysisAccepts_TheDoorsRefuse_GapOwnedByT10() {
        var result = GateParity.Check(DateDocument("Ok: policy { (Due - Due) == (Due - Due) }"),
            SimStep.Create("Item", """{"A":1,"Due":"2026-01-01","Stamp":"2026-01-01T00:00:00Z"}""", "i"));

        await Assert.That(result.Analysis).IsTrue().Because(result.ToString());
        await Assert.That(result.CSharp is false).IsTrue().Because(result.ToString());
        await Assert.That(result.PolycCheck is false).IsTrue().Because(result.ToString());
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