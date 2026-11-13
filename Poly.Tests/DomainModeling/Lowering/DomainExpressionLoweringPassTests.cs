using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

using Parameter = Poly.Ast.Nodes.Parameter;
using SN = Poly.Ast.Nodes;

namespace Poly.Tests.DomainModeling.Lowering;

public class DomainExpressionLoweringPassTests {
    private static readonly ExpressionMeaning Temporal = TemporalMeaningHarness.Create();

    private readonly DomainExpressionLoweringPass Pass = new(
        LoweringFixtures.MinimalContext(new Parameter("entity")) with { Meaning = Temporal });

    private readonly ParameterReference Subject = new();

    [Test]
    public async Task PropertyAccess_LowersToMember() {
        var expr = DomainExpression.Property("Name");
        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Member>();
        var member = (Member)result;
        await Assert.That(member.MemberName).IsEqualTo("Name");
        await Assert.That(member.Value).IsSameReferenceAs(Subject);
    }

    [Test]
    public async Task ParameterAccess_LowersToParameter_WhenFoundInDictionary() {
        var param = new Parameter("p1");
        var pass = new DomainExpressionLoweringPass(
            LoweringFixtures.MinimalContext(new Parameter("entity")) with {
                Parameters = new Dictionary<string, Node> { ["p1"] = param }
            });

        var expr = DomainExpression.Parameter("p1");
        var result = pass.Lower(expr, Subject);

        await Assert.That(result).IsSameReferenceAs(param);
    }

    [Test]
    public async Task ParameterAccess_CreatesFreshParameter_WhenNotInDictionary() {
        var expr = DomainExpression.Parameter("fresh");
        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Parameter>();
        var param = (Parameter)result;
        await Assert.That(param.Name).IsEqualTo("fresh");
    }

    [Test]
    public async Task Literal_LowersToConstant() {
        var expr = DomainExpression.Literal(42);
        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Constant>();
        await Assert.That(((Constant)result).Value).IsEqualTo(42);
    }

    [Test]
    public async Task Literal_Null_LowersToConstantNull() {
        var expr = DomainExpression.Literal(null);
        var result = Pass.Lower(expr, Subject);

        await Assert.That(((Constant)result).Value).IsNull();
    }

    [Test]
    public async Task OwnedAccess_LowersToNestedMember() {
        var inner = DomainExpression.Property("Time");
        var expr = DomainExpression.Owned("BirthCert", inner);

        var result = Pass.Lower(expr, Subject);

        // Expected: Member(Member(subject, "BirthCert"), "Time")
        await Assert.That(result).IsTypeOf<Member>();
        var outer = (Member)result;
        await Assert.That(outer.MemberName).IsEqualTo("Time");

        await Assert.That(outer.Value).IsTypeOf<Member>();
        var innerMember = (Member)outer.Value;
        await Assert.That(innerMember.MemberName).IsEqualTo("BirthCert");
        await Assert.That(innerMember.Value).IsSameReferenceAs(Subject);
    }

    [Test]
    public async Task RelationshipNavigation_LowersToNullForgivingHop() {
        var inner = DomainExpression.Property("AvailableCopies");
        var expr = DomainExpression.RelationshipNav("Book", inner);

        var pass = new DomainExpressionLoweringPass(LoweringFixtures.MinimalContext(Subject));
        var result = pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Member>();
        var outer = (Member)result;
        await Assert.That(outer.MemberName).IsEqualTo("AvailableCopies");

        // Export/module path-prefix is NullForgiving(Member) for CS8602.
        // Unlinked require fails closed via DomainResult.Failure in
        // BuildActionBodyWithGuards — not coalesce-throw escaping DomainResult.
        await Assert.That(outer.Value).IsTypeOf<NullForgiving>();
        var forgive = (NullForgiving)outer.Value;
        await Assert.That(forgive.Operand).IsTypeOf<Member>();
        var nav = (Member)forgive.Operand;
        await Assert.That(nav.MemberName).IsEqualTo("Book");
        await Assert.That(nav.Value).IsSameReferenceAs(Subject);
    }

    [Test]
    public async Task Exists_LowersToNotEqualNull() {
        var inner = DomainExpression.Property("Name");
        var expr = DomainExpression.Exists(inner);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<NotEqual>();
        var neq = (NotEqual)result;

        await Assert.That(neq.LeftHandValue).IsTypeOf<Member>();
        await Assert.That(((Member)neq.LeftHandValue).MemberName).IsEqualTo("Name");
        await Assert.That(neq.RightHandValue).IsTypeOf<Constant>();
        await Assert.That(((Constant)neq.RightHandValue).Value).IsNull();
    }

    [Test]
    public async Task NotExists_LowersToEqualNull() {
        var expr = DomainExpression.NotExists(DomainExpression.Property("Name"));

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Equal>();
        var eq = (Equal)result;
        await Assert.That(eq.RightHandValue).IsTypeOf<Constant>();
        await Assert.That(((Constant)eq.RightHandValue).Value).IsNull();
    }

    [Test]
    public async Task Add_LowersToSyntaxAdd() {
        var result = Pass.Lower(
            DomainExpression.Add(DomainExpression.Literal(1), DomainExpression.Literal(2)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Add>();
        var add = (SN.Add)result;
        await Assert.That(((Constant)add.LeftHandValue).Value).IsEqualTo(1);
        await Assert.That(((Constant)add.RightHandValue).Value).IsEqualTo(2);
    }

    [Test]
    public async Task Subtract_LowersToSyntaxSubtract() {
        var result = Pass.Lower(
            DomainExpression.Subtract(DomainExpression.Literal(5), DomainExpression.Literal(3)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Subtract>();
    }

    [Test]
    public async Task Multiply_LowersToSyntaxMultiply() {
        var result = Pass.Lower(
            DomainExpression.Multiply(DomainExpression.Literal(2), DomainExpression.Literal(3)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Multiply>();
    }

    [Test]
    public async Task Divide_LowersToSyntaxDivide() {
        var result = Pass.Lower(
            DomainExpression.Divide(DomainExpression.Literal(10), DomainExpression.Literal(2)),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Divide>();
    }

    [Test]
    public async Task And_LowersToSyntaxAnd() {
        var result = Pass.Lower(
            DomainExpression.And(
                DomainExpression.Exists(DomainExpression.Property("A")),
                DomainExpression.Exists(DomainExpression.Property("B"))),
            Subject);

        await Assert.That(result).IsTypeOf<SN.And>();
    }

    [Test]
    public async Task Or_LowersToSyntaxOr() {
        var result = Pass.Lower(
            DomainExpression.Or(
                DomainExpression.Exists(DomainExpression.Property("X")),
                DomainExpression.Exists(DomainExpression.Property("Y"))),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Or>();
    }

    [Test]
    public async Task Not_LowersToSyntaxNot() {
        var result = Pass.Lower(
            DomainExpression.Not(DomainExpression.Exists(DomainExpression.Property("Flag"))),
            Subject);

        await Assert.That(result).IsTypeOf<SN.Not>();
    }

    [Test]
    public async Task Comparison_Equal_LowersToSyntaxEqual() {
        var result = Pass.Lower(
            DomainExpression.Equal(DomainExpression.Property("Count"), DomainExpression.Literal(5)),
            Subject);

        await Assert.That(result).IsTypeOf<Equal>();
    }

    [Test]
    public async Task Comparison_NotEqual_LowersToSyntaxNotEqual() {
        var result = Pass.Lower(
            DomainExpression.NotEqual(DomainExpression.Property("Status"), DomainExpression.Literal(0)),
            Subject);

        await Assert.That(result).IsTypeOf<NotEqual>();
    }

    [Test]
    public async Task Comparison_LessThan_LowersToSyntaxLessThan() {
        var result = Pass.Lower(
            DomainExpression.LessThan(DomainExpression.Property("Qty"), DomainExpression.Property("Min")),
            Subject);

        await Assert.That(result).IsTypeOf<LessThan>();
    }

    [Test]
    public async Task Comparison_LessThanOrEqual_LowersToSyntaxLessThanOrEqual() {
        var result = Pass.Lower(
            DomainExpression.LessThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Property("Min")),
            Subject);

        await Assert.That(result).IsTypeOf<LessThanOrEqual>();
    }

    [Test]
    public async Task Comparison_GreaterThan_LowersToSyntaxGreaterThan() {
        var result = Pass.Lower(
            DomainExpression.GreaterThan(DomainExpression.Property("Qty"), DomainExpression.Property("Max")),
            Subject);

        await Assert.That(result).IsTypeOf<GreaterThan>();
    }

    [Test]
    public async Task Comparison_GreaterThanOrEqual_LowersToSyntaxGreaterThanOrEqual() {
        var result = Pass.Lower(
            DomainExpression.GreaterThanOrEqual(DomainExpression.Property("Qty"), DomainExpression.Property("Max")),
            Subject);

        await Assert.That(result).IsTypeOf<GreaterThanOrEqual>();
    }

    [Test]
    public async Task DateOperation_AddDays_LowersToInvoke() {
        var expr = new DateOperation(
            DomainExpression.Property("DueDate"),
            DomainExpression.Literal(14),
            DateOperationKind.AddDays);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Invoke>();
        var invoke = (Invoke)result;
        await Assert.That(invoke.Delegate).IsTypeOf<Member>();
        await Assert.That(((Member)invoke.Delegate).MemberName).IsEqualTo("AddDays");
        await Assert.That(invoke.Arguments.Length).IsEqualTo(1);
    }

    [Test]
    public async Task DateOperation_AddMonths_LowersToInvoke() {
        var expr = new DateOperation(
            DomainExpression.Property("StartDate"),
            DomainExpression.Literal(3),
            DateOperationKind.AddMonths);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Invoke>();
        await Assert.That(((Member)((Invoke)result).Delegate).MemberName).IsEqualTo("AddMonths");
    }

    [Test]
    public async Task DateOperation_AddHours_LowersToInvoke() {
        var expr = new DateOperation(
            DomainExpression.Property("OpenedAt"),
            DomainExpression.Literal(2),
            DateOperationKind.AddHours);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(((Member)((Invoke)result).Delegate).MemberName).IsEqualTo("AddHours");
    }

    [Test]
    public async Task DateOperation_AddWeeks_LowersToAddDaysScaled() {
        var expr = new DateOperation(
            DomainExpression.Property("DueDate"),
            DomainExpression.Literal(2L),
            DateOperationKind.AddWeeks);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(((Member)((Invoke)result).Delegate).MemberName).IsEqualTo("AddDays");
        await Assert.That(((Constant)((Invoke)result).Arguments[0]).Value).IsEqualTo(14L);
    }

    [Test]
    public async Task DateOperation_AddWeeks_OnDate_ScalesThenIntCasts() {
        var pass = new DomainExpressionLoweringPass(LoweringFixtures.MinimalContext(new Parameter("entity")) with { PropertyTypeResolver = n => n == "DueDate" ? "Date" : null, Meaning = Temporal });
        var expr = new DateOperation(
            DomainExpression.Property("DueDate"),
            DomainExpression.Literal(2L),
            DateOperationKind.AddWeeks);

        var result = pass.Lower(expr, Subject);

        await Assert.That(((Member)((Invoke)result).Delegate).MemberName).IsEqualTo("AddDays");
        var arg = ((Invoke)result).Arguments[0];
        await Assert.That(arg).IsTypeOf<TypeCast>();
        var cast = (TypeCast)arg;
        await Assert.That(cast.Operand).IsTypeOf<Constant>();
        await Assert.That(((Constant)cast.Operand).Value).IsEqualTo(14L);
        await Assert.That(cast.TargetTypeReference).IsTypeOf<PrimitiveTypeReference>();
        await Assert.That(((PrimitiveTypeReference)cast.TargetTypeReference).PrimitiveId)
            .IsEqualTo(Poly.Introspection.PrimitiveType.Int32);
    }

    [Test]
    public async Task DateOperation_AddMilliseconds_OnTime_LowersToTimeSpanFromMilliseconds() {
        var pass = new DomainExpressionLoweringPass(LoweringFixtures.MinimalContext(new Parameter("entity")) with { PropertyTypeResolver = n => n == "Opens" ? "Time" : null, Meaning = Temporal });
        var expr = new DateOperation(
            DomainExpression.Property("Opens"),
            DomainExpression.Literal(50),
            DateOperationKind.AddMilliseconds);

        var result = pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Invoke>();
        var add = (Invoke)result;
        await Assert.That(((Member)add.Delegate).MemberName).IsEqualTo("Add");
        await Assert.That(add.Arguments.Length).IsEqualTo(1);
        await Assert.That(add.Arguments[0]).IsTypeOf<Invoke>();
        var fromMs = (Invoke)add.Arguments[0];
        await Assert.That(fromMs.Delegate).IsTypeOf<Member>();
        var fromMember = (Member)fromMs.Delegate;
        await Assert.That(fromMember.MemberName).IsEqualTo("FromMilliseconds");
        await Assert.That(fromMember.Value).IsTypeOf<NamedTypeReference>();
        await Assert.That(((NamedTypeReference)fromMember.Value).TypeName).IsEqualTo("TimeSpan");
        await Assert.That(fromMs.Arguments.Length).IsEqualTo(1);
        await Assert.That(((Constant)fromMs.Arguments[0]).Value).IsEqualTo(50);
    }

    [Test]
    public async Task DateOperation_AddYears_LowersToInvoke() {
        var expr = new DateOperation(
            DomainExpression.Property("StartDate"),
            DomainExpression.Literal(1),
            DateOperationKind.AddYears);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(((Member)((Invoke)result).Delegate).MemberName).IsEqualTo("AddYears");
    }

    [Test]
    public async Task DateOperation_DiffDays_LowersToInvoke() {
        var expr = new DateOperation(
            DomainExpression.Property("EndDate"),
            DomainExpression.Property("StartDate"),
            DateOperationKind.DiffDays);

        var result = Pass.Lower(expr, Subject);

        await Assert.That(result).IsTypeOf<Invoke>();
        await Assert.That(((Member)((Invoke)result).Delegate).MemberName).IsEqualTo("Subtract");
    }

    [Test]
    public async Task ComplexExpression_CombinesMultipleNodeTypes() {
        // Lower: And(Exists(Owned("Cert", Property("Time"))), GreaterThan(Property("Age"), Literal(18)))
        var expr = DomainExpression.And(
            DomainExpression.Exists(
                DomainExpression.Owned("Cert", DomainExpression.Property("Time"))),
            DomainExpression.GreaterThan(
                DomainExpression.Property("Age"),
                DomainExpression.Literal(18)));

        var result = Pass.Lower(expr, Subject);

        // Top-level: And
        await Assert.That(result).IsTypeOf<SN.And>();
        var and = (SN.And)result;

        // Left: NotEqual(Member(Member(subject, "Cert"), "Time"), null)
        await Assert.That(and.LeftHandValue).IsTypeOf<NotEqual>();
        var neq = (NotEqual)and.LeftHandValue;
        await Assert.That(neq.RightHandValue).IsTypeOf<Constant>();
        await Assert.That(((Constant)neq.RightHandValue).Value).IsNull();
        await Assert.That(neq.LeftHandValue).IsTypeOf<Member>();
        await Assert.That(((Member)neq.LeftHandValue).MemberName).IsEqualTo("Time");

        // Right: GreaterThan(Member(subject, "Age"), Constant(18))
        await Assert.That(and.RightHandValue).IsTypeOf<GreaterThan>();
        var gt = (GreaterThan)and.RightHandValue;
        await Assert.That(gt.LeftHandValue).IsTypeOf<Member>();
        await Assert.That(((Member)gt.LeftHandValue).MemberName).IsEqualTo("Age");
        await Assert.That(gt.RightHandValue).IsTypeOf<Constant>();
        await Assert.That(((Constant)gt.RightHandValue).Value).IsEqualTo(18);
    }

    [Test]
    public async Task ParameterAccess_ResolvesInNestedContext() {
        var param = new Parameter("status");
        var pass = new DomainExpressionLoweringPass(
            LoweringFixtures.MinimalContext(new Parameter("entity")) with {
                Parameters = new Dictionary<string, Node> { ["status"] = param }
            });

        // Owned("Details", Parameter("status"))
        var expr = DomainExpression.Owned("Details", DomainExpression.Parameter("status"));
        var result = pass.Lower(expr, Subject);

        // Expected: Member(subject, "Details") — but inner is parameter, not property
        // ParameterAccess resolves directly to the parameter node, bypassing the owned context
        await Assert.That(result).IsSameReferenceAs(param);
    }

    [Test]
    public async Task Guard_ThrowsOnNullExpression() {
        var pass = new DomainExpressionLoweringPass(LoweringFixtures.MinimalContext(new Parameter("entity")));
        var ex = Assert.Throws<ArgumentNullException>(() => pass.Lower(null!, Subject));
        await Assert.That(ex).IsNotNull();
    }

    [Test]
    public async Task Guard_ThrowsOnNullSubject() {
        var pass = new DomainExpressionLoweringPass(LoweringFixtures.MinimalContext(new Parameter("entity")));
        var ex = Assert.Throws<ArgumentNullException>(() => pass.Lower(DomainExpression.Literal(1), null!));
        await Assert.That(ex).IsNotNull();
    }

    // ── T-06: an optional Boolean in a condition means `X == true`, on both hosts ──
    // Each row runs one document on the simulator and on the generated C# compiled by Roslyn (an independent
    // authority), for Flag stored true, stored false and absent. A position lowering misses fails its row.

    private const string OptionalBooleanDsl = """
        domain T06a
        T06Line: entity {
          Flag: Boolean optional
        }
        T06Item: entity {
          Done: Boolean
          Flag: Boolean optional
          Out: Boolean optional
          lines: many T06Line
          Ok: policy { Flag }
          OkAnd: policy { Done and Flag is true }
          PAnd: action { assign Out to Done and Flag }
          POr: action { assign Out to Flag or not Done }
          PNot: action { assign Out to not Flag }
          PIf: action { if (Flag) { assign Out to true } else { assign Out to false } }
          PForWhere: action {
            assign Out to false
            for line in lines where Flag { assign Out to true }
          }
          PForWhereBinder: action {
            assign Out to false
            for line in lines where line Flag { assign Out to true }
          }
          PMatchArm: action { assign Out to match { Flag -> true, else -> false } }
          PAny: action { assign Out to any lines where Flag }
          PAll: action { assign Out to all lines where Flag }
          PNone: action { assign Out to none lines where Flag }
          PCount: action { assign Out to (count lines where Flag) > 0 }
          PRequire: action require Ok { assign Out to true }
          PRequireNot: action require not Ok { assign Out to true }
          PRequireAnd: action require OkAnd { assign Out to true }
          PIsTrue: action { assign Out to Flag is true }
          PIsFalse: action { assign Out to Flag is false }
          PNotEqualTrue: action { assign Out to Flag != true }
          PNotEqualFalse: action { assign Out to Flag != false }
          PIfIsTrue: action { if (Flag is true) { assign Out to true } else { assign Out to false } }
          PIfNotEqualTrue: action { if (Flag != true) { assign Out to true } else { assign Out to false } }
          PAndIsTrue: action { assign Out to Done and Flag is true }
          PNotIsTrue: action { assign Out to not (Flag is true) }
          PAnyIsFalse: action { assign Out to any lines where Flag is false }
          PEqualsDone: action { assign Out to Flag == Done }
          PNotIsFalse: action { assign Out to not (Flag is false) }
          PEntryIf: action { transition to Entered }
          PToGated: action { transition to Gated }
          Idle: stage { }
          Entered: stage {
            entry { if (Flag) { assign Out to true } else { assign Out to false } }
          }
          Gated: stage require Ok {
            PRequireStage: action { assign Out to true }
          }
        }
        """;

    private static readonly Lazy<ParityScenario> OptionalBoolean =
        new(() => ParityScenario.FromDsl(OptionalBooleanDsl, "T06aOptionalBooleanPositions"));

    // The module a domain lowers to is bound to that Domain once: one row drives the scenario at a time.
    private static readonly Lock OptionalBooleanGate = new();

    private static IReadOnlyList<ParityOutcome> RunOptionalBoolean(bool? flag, Action<ParitySide> step) {
        lock (OptionalBooleanGate) {
            var (simulate, printed) = OptionalBoolean.Value.Run(side => {
                (string, object?)[] flagValue = flag is { } stored ? [("Flag", stored)] : [];
                var line = side.Create("T06Line", flagValue);
                side.Create("T06Item", [("Done", true), .. flagValue, ("lines", new List<object> { line! })]);
                step(side);
            });
            var differences = ParityScenario.Differences(simulate, printed);
            if (differences.Count > 0)
                throw new InvalidOperationException($"Flag={flag?.ToString() ?? "absent"}: " + string.Join("; ", differences));
            return simulate;
        }
    }

    /// <summary>
    /// Out after the action, for Flag stored true, stored false and absent. `== true` is (True, False, False); its
    /// negation (True when absent) is (False, True, True). A blocked `require` leaves Out absent (null).
    /// </summary>
    [Test]
    [Arguments("PAnd", "True", "False", "False")]
    [Arguments("POr", "True", "False", "False")]
    [Arguments("PNot", "False", "True", "True")]
    [Arguments("PIf", "True", "False", "False")]
    [Arguments("PForWhere", "True", "False", "False")]
    [Arguments("PForWhereBinder", "True", "False", "False")]
    [Arguments("PMatchArm", "True", "False", "False")]
    [Arguments("PAny", "True", "False", "False")]
    [Arguments("PAll", "True", "False", "False")]
    [Arguments("PNone", "False", "True", "True")]
    [Arguments("PCount", "True", "False", "False")]
    [Arguments("PRequire", "True", null, null)]
    [Arguments("PRequireNot", null, "True", "True")]
    [Arguments("PRequireAnd", "True", null, null)]
    [Arguments("PIsTrue", "True", "False", "False")]
    [Arguments("PIsFalse", "False", "True", "False")]
    [Arguments("PNotEqualTrue", "False", "True", "True")]
    [Arguments("PNotEqualFalse", "True", "False", "True")]
    [Arguments("PIfIsTrue", "True", "False", "False")]
    [Arguments("PIfNotEqualTrue", "False", "True", "True")]
    [Arguments("PAndIsTrue", "True", "False", "False")]
    [Arguments("PNotIsTrue", "False", "True", "True")]
    [Arguments("PAnyIsFalse", "False", "True", "False")]
    [Arguments("PEqualsDone", "True", "False", "False")]
    [Arguments("PNotIsFalse", "True", "False", "True")]
    [Arguments("PEntryIf", "True", "False", "False")]
    [Arguments("PToGated+PRequireStage", "True", null, null)]
    public async Task OptionalBoolean_InEveryConditionPosition_MeansEqualsTrue_OnBothHosts(
        string action, string? whenTrue, string? whenFalse, string? whenAbsent) {
        bool?[] flags = [true, false, null];
        string?[] expected = [whenTrue, whenFalse, whenAbsent];
        for (var i = 0; i < flags.Length; i++) {
            var outcome = RunOptionalBoolean(flags[i], side => {
                foreach (var step in action.Split('+'))
                    side.Invoke(step);
            })[^1];
            var last = action.Split('+')[^1];
            await Assert.That(outcome.ExceptionType).IsNull();
            await Assert.That(outcome.Success).IsEqualTo(expected[i] is not null || !last.StartsWith("PRequire", StringComparison.Ordinal));
            await Assert.That(outcome.State["Out"]).IsEqualTo(expected[i]);
        }
    }

    /// <summary>A policy whose root is an optional Boolean answers `Flag == true` when evaluated, on both hosts.</summary>
    [Test]
    [Arguments(true, "True")]
    [Arguments(false, "False")]
    [Arguments(null, "False")]
    public async Task OptionalBoolean_PolicyRoot_EvaluatesEqualsTrue_OnBothHosts(bool? flag, string answer) {
        var outcome = RunOptionalBoolean(flag, side => side.EvaluatePolicy("Ok"))[^1];
        await Assert.That(outcome.State["Ok"]).IsEqualTo(answer);
    }

    // ── T-06: `null` into an optional target, including a `match` null arm (#606) ──

    private const string NullTargetDsl = """
        domain T06aNull
        T06Level: enum { Low, High }
        T06Slot: entity {
          Big: Boolean
          Q: Number optional
          Flag: Boolean optional
          Note: Text optional
          Level: T06Level optional
          ArmQ: action { assign Q to match { Big -> null, else -> 7 } }
          ArmFlag: action { assign Flag to match { Big -> null, else -> true } }
          ArmNote: action { assign Note to match { Big -> null, else -> "x" } }
          NestedQ: action { assign Q to match { Big -> match { Big -> null, else -> 1 }, else -> 7 } }
          NullQ: action { assign Q to null }
          NullFlag: action { assign Flag to null }
          QIsNull: action { assign Flag to Q == null }
          LevelIsNull: action { assign Flag to Level == null }
          NullLevel: action { assign Level to null }
        }
        """;

    private static readonly Lazy<ParityScenario> NullTarget =
        new(() => ParityScenario.FromDsl(NullTargetDsl, "T06aNullIntoOptional"));

    private static readonly Lock NullTargetGate = new();

    /// <summary>
    /// The stored value after the action, starting from Q 5, Flag false, Note "n". A `null` arm stores absent and the
    /// other arm its value; `null` stores absent; `Q == null` reads absence. Both hosts agree.
    /// </summary>
    [Test]
    [Arguments("ArmQ", true, "Q", null)]
    [Arguments("ArmQ", false, "Q", "7")]
    [Arguments("ArmFlag", true, "Flag", null)]
    [Arguments("ArmFlag", false, "Flag", "True")]
    [Arguments("ArmNote", true, "Note", null)]
    [Arguments("ArmNote", false, "Note", "x")]
    [Arguments("NestedQ", true, "Q", null)]
    [Arguments("NestedQ", false, "Q", "7")]
    [Arguments("NullQ", false, "Q", null)]
    [Arguments("NullFlag", false, "Flag", null)]
    [Arguments("QIsNull", false, "Flag", "False")]
    [Arguments("LevelIsNull", false, "Flag", "False")]
    [Arguments("NullLevel", false, "Level", null)]
    public async Task NullIntoOptionalTarget_StoresAbsent_OnBothHosts(string action, bool big, string member, string? stored) {
        IReadOnlyList<ParityOutcome> simulate;
        List<string> differences;
        lock (NullTargetGate) {
            (simulate, var printed) = NullTarget.Value.Run(side => {
                side.Create("T06Slot", ("Big", big), ("Q", 5L), ("Flag", false), ("Note", "n"), ("Level", "Low"));
                side.Invoke(action);
            });
            differences = ParityScenario.Differences(simulate, printed);
        }
        await Assert.That(differences).IsEmpty();
        await Assert.That(simulate[^1].Success).IsTrue();
        await Assert.That(simulate[^1].State[member]).IsEqualTo(stored);
    }

    // ── T-06: an optional slot of every kind takes `null`; its assign guard lets the absent value through ──

    private static string OptionalKindDsl(string type) => $$"""
        domain T06aKind
        uses temporal
        T06KindHost: entity {
          Big: Boolean
          D: {{type}} optional
          NullD: action { assign D to null }
          ArmD: action { assign D to match { Big -> null, else -> D } }
        }
        """;

    private static readonly Lock OptionalKindGate = new();

    /// <summary>
    /// `null` into an optional Date, DateTime, Duration, Decimal or Uuid stores absent on both hosts; the other arm of a
    /// `null`-arm `match` stores its value. (A domain value type is no property, DMSTR006, and an entity link is no
    /// assign target, DMEFF001. Time is not run here: its generated C# does not compile with or without `null`.)
    /// </summary>
    [Test]
    [Arguments("Date", "NullD", false, null)]
    [Arguments("Date", "ArmD", true, null)]
    [Arguments("Date", "ArmD", false, "present")]
    [Arguments("DateTime", "NullD", false, null)]
    [Arguments("DateTime", "ArmD", true, null)]
    [Arguments("DateTime", "ArmD", false, "present")]
    [Arguments("Duration", "NullD", false, null)]
    [Arguments("Duration", "ArmD", true, null)]
    [Arguments("Duration", "ArmD", false, "present")]
    [Arguments("Decimal", "NullD", false, null)]
    [Arguments("Decimal", "ArmD", true, null)]
    [Arguments("Decimal", "ArmD", false, "present")]
    [Arguments("Uuid", "NullD", false, null)]
    [Arguments("Uuid", "ArmD", true, null)]
    [Arguments("Uuid", "ArmD", false, "present")]
    public async Task NullIntoOptionalSlotOfEveryKind_StoresAbsent_OnBothHosts(string type, string action, bool big, string? stored) {
        object value = type switch {
            "Date" => new DateOnly(2026, 1, 2),
            "DateTime" => new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            "Duration" => TimeSpan.FromHours(2),
            "Decimal" => 2.5m,
            _ => Guid.Parse("00000000-0000-0000-0000-000000000007"),
        };
        IReadOnlyList<ParityOutcome> simulate;
        List<string> differences;
        lock (OptionalKindGate) {
            (simulate, var printed) = ParityScenario.FromDsl(OptionalKindDsl(type), "T06aKind" + type).Run(side => {
                side.Create("T06KindHost", ("Big", big), ("D", value));
                side.Invoke(action);
            });
            differences = ParityScenario.Differences(simulate, printed);
        }
        await Assert.That(differences).IsEmpty();
        await Assert.That(simulate[^1].Success).IsTrue();
        await Assert.That(simulate[^1].State["D"]).IsEqualTo(stored is null ? null : simulate[0].State["D"]);
    }

    private const string OptionalGuardDsl = """
        domain T06aGuard
        T06Lvl: enum { Low, High }
        T06Guarded: entity {
          Big: Boolean
          Level: T06Lvl optional
          Level2: T06Lvl optional
          Req: T06Lvl
          EqN: Number optional equals(5)
          RngN: Number optional range(0, 10)
          EqT: Text optional equals("a")
          LenT: Text optional length(2, 3)
          PatT: Text optional pattern("^a")
          ArmLevel: action { assign Level to match { Big -> null, else -> High } }
          CopyLevel: action { assign Level2 to Level }
          SetLevel: action (v: T06Lvl) { assign Level to v }
          SetReq: action (v: T06Lvl) { assign Req to v }
          ArmEqN: action { assign EqN to match { Big -> null, else -> 5 } }
          SetEqN: action (v: Number) { assign EqN to v }
          ArmRngN: action { assign RngN to match { Big -> null, else -> 4 } }
          SetRngN: action (v: Number) { assign RngN to v }
          ArmEqT: action { assign EqT to match { Big -> null, else -> "a" } }
          SetEqT: action (v: Text) { assign EqT to v }
          ArmLenT: action { assign LenT to match { Big -> null, else -> "ab" } }
          ArmPatT: action { assign PatT to match { Big -> null, else -> "ab" } }
        }
        """;

    private static readonly Lazy<ParityScenario> OptionalGuard =
        new(() => ParityScenario.FromDsl(OptionalGuardDsl, "T06aOptionalGuard"));

    private static readonly Lock OptionalGuardGate = new();

    /// <summary>
    /// The assign-time guard of an optional property lets an absent value through and still refuses a present wrong
    /// one, on both hosts: an enum's member check, an equality, a range; Length and Pattern already guarded (regression
    /// rows). A required enum still refuses a non-member. <c>arg</c> is the action's argument; "Bogus" is no member.
    /// The absent value is a <c>null</c> arm or an absent copy: a bare <c>null</c> literal into a constrained property is
    /// refused at analysis by the literal constraint check (<c>EffectAnalyzer.Assign</c>).
    /// </summary>
    [Test]
    [Arguments("ArmLevel", true, true, null, "Level", null, true)]
    [Arguments("ArmLevel", false, true, null, "Level", "High", true)]
    [Arguments("CopyLevel", false, false, null, "Level2", null, true)]
    [Arguments("CopyLevel", false, true, null, "Level2", "High", true)]
    [Arguments("SetLevel", false, true, "Bogus", "Level", "High", false)]
    [Arguments("SetReq", false, true, "Bogus", "Req", "Low", false)]
    [Arguments("ArmEqN", true, true, null, "EqN", null, true)]
    [Arguments("SetEqN", false, true, 6L, "EqN", "5", false)]
    [Arguments("ArmRngN", true, true, null, "RngN", null, true)]
    [Arguments("ArmRngN", false, true, null, "RngN", "4", true)]
    [Arguments("SetRngN", false, true, 11L, "RngN", "3", false)]
    [Arguments("ArmEqT", true, true, null, "EqT", null, true)]
    [Arguments("SetEqT", false, true, "b", "EqT", "a", false)]
    [Arguments("ArmLenT", true, true, null, "LenT", null, true)]
    [Arguments("ArmPatT", true, true, null, "PatT", null, true)]
    public async Task OptionalProperty_AssignGuard_PassesAbsent_RefusesAWrongValue_OnBothHosts(
        string action, bool big, bool levelPresent, object? arg, string member, string? stored, bool success) {
        IReadOnlyList<ParityOutcome> simulate;
        List<string> differences;
        lock (OptionalGuardGate) {
            (simulate, var printed) = OptionalGuard.Value.Run(side => {
                (string, object?)[] level = levelPresent ? [("Level", "High")] : [];
                side.Create("T06Guarded", [("Big", big), .. level, ("Level2", "Low"), ("Req", "Low"),
                    ("EqN", 5L), ("RngN", 3L), ("EqT", "a"), ("LenT", "ab"), ("PatT", "ab")]);
                if (arg is null)
                    side.Invoke(action);
                else
                    side.Invoke(action, ("v", arg));
            });
            differences = ParityScenario.Differences(simulate, printed);
        }
        await Assert.That(differences).IsEmpty();
        await Assert.That(simulate[^1].Success).IsEqualTo(success);
        await Assert.That(simulate[^1].State[member]).IsEqualTo(stored);
    }
}