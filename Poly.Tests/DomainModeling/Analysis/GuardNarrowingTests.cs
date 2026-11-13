using System.Globalization;

using Poly.Analysis;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// V-02 (#477, #580): guard narrowing in <c>EffectInvariantAnalyzer</c>. A guard (a <c>require</c>'d policy, an
/// <c>if</c> condition, a callee's guard) narrows the abstract environment the effects run in. The pins record the
/// verdict of each guard form (the narrowed range of the probed property and the error/warning codes); the
/// remaining tests state what each form must narrow to, and the point oracle checks that no form ever excludes
/// a value the guard admits.
/// </summary>
public class GuardNarrowingTests {
    private const string Props = """
          X: Number range(0, 10)
          D: Decimal range(0, 10)
          Y: Number
          Z: Decimal
          Name: Text
          Start: Number range(0, 5)
          End: Number range(10, 20)
        """;

    /// <summary>Parses and applies <paramref name="dsl"/> without analysis, then analyzes the applied tree, so a
    /// domain the analysis rejects is still the tree its facts are recorded on.</summary>
    private static (Domain Domain, AnalysisResult Analysis) Analyze(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        var applied = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!applied.Succeeded)
            throw new InvalidOperationException("mutation failed: " + applied.FailureSummary);
        return (applied.Root, DomainModelAnalyzer.Analyze(applied.Root));
    }

    private const string NegativeProps = """
          X: Number range(-5, 5)
          Y: Number
        """;

    private static string PolicyDomain(string guard, string body, string props = Props) => $$"""
        domain G
        E: entity {
        {{props}}
          P: policy { {{guard}} }
          Go: action require P { {{body}} }
        }
        """;

    private static string ActionDomain(string body, string props = Props) => $$"""
        domain G
        E: entity {
        {{props}}
          Go: action { {{body}} }
        }
        """;

    private static Entity EntityNamed(Domain domain, string name) =>
        domain.Types.OfType<Entity>().SingleOrDefault(e => e.Name == name)
        ?? throw new InvalidOperationException($"no entity '{name}'");

    private static ActionStageInvariant Context(Domain domain, AnalysisResult analysis, string entity = "E", string action = "Go") {
        var owner = EntityNamed(domain, entity);
        var act = owner.Actions.SingleOrDefault(a => a.Name == action)
            ?? throw new InvalidOperationException($"no action '{entity}.{action}'");
        var invariants = analysis.GetActionInvariants(act)
            ?? throw new InvalidOperationException($"no invariant metadata on '{entity}.{action}'");
        return invariants.StageContexts().Single();
    }

    private static string Num(decimal? value) =>
        value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "";

    private static string Range(ValueRange? range) => range is null ? "none" : $"[{Num(range.Min)},{Num(range.Max)}]";

    private static string Narrowed(ActionStageInvariant context, string property) =>
        context.NarrowedRanges.TryGetValue(property, out var range)
            ? Range(range)
            : throw new InvalidOperationException($"no narrowed entry for '{property}'");

    /// <summary>The error (<c>!</c>) and warning (<c>?</c>) codes, sorted; <c>clean</c> when there are none.</summary>
    private static string Codes(AnalysisResult analysis) {
        var codes = analysis.Diagnostics
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(d => d.Code + (d.Severity == DiagnosticSeverity.Error ? "!" : "?"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        return codes.Count == 0 ? "clean" : string.Join(",", codes);
    }

    private static string PolicyVerdict(string guard, string body, string probe) {
        var (domain, analysis) = Analyze(PolicyDomain(guard, body));
        return $"{probe}={Narrowed(Context(domain, analysis), probe)} {Codes(analysis)}";
    }

    // ── Characterisation pins (recorded on master 1907c75d before any production edit; a row V-02 changes on
    // purpose carries the reason and master's value) ──

    [Test]
    // integral strict, reversed literal, fractional literal (a fractional literal against a Number is already DMSEM003)
    [Arguments("X < 10", "assign X to X + 1", "X", "X=[0,9] clean")] // V-02: strict < on an integral axis is ceil(v) - 1; master "X=[0,10] DMEFF008?"
    [Arguments("X > 0", "assign X to X - 1", "X", "X=[1,10] clean")] // V-02: strict > on an integral axis is floor(v) + 1; master "X=[0,10] DMEFF008?"
    [Arguments("0 < X", "assign X to X - 1", "X", "X=[1,10] clean")] // V-02: the reversed literal flips to X > 0, now strict; master "X=[0,10] DMEFF008?"
    [Arguments("10 > X", "assign X to X + 1", "X", "X=[0,9] clean")] // V-02: the reversed literal flips to X < 10, now strict; master "X=[0,10] DMEFF008?"
    [Arguments("X < 9.5", "assign X to X + 1", "X", "X=[0,9] DMSEM003!")] // V-02: ceil(9.5) - 1 = 9 on an integral axis; master "X=[0,9.5] DMEFF008?,DMSEM003!"
    [Arguments("X > 0.5", "assign X to X - 1", "X", "X=[1,10] DMSEM003!")] // V-02: floor(0.5) + 1 = 1 on an integral axis; master "X=[0.5,10] DMEFF008?,DMSEM003!"
    [Arguments("X > 0 and X < 10", "assign X to X + 1", "X", "X=[1,9] clean")] // V-02: both strict conjuncts are exact; master "X=[0,10] DMEFF008?"
    // != at an endpoint, inside, and on a point
    [Arguments("X != 10", "assign X to X + 1", "X", "X=[0,9] clean")] // V-02: != at the integral upper endpoint moves it in by one; master "X=[0,10] DMEFF008?"
    [Arguments("X != 0", "assign X to X - 1", "X", "X=[1,10] clean")] // V-02: != at the integral lower endpoint moves it in by one; master "X=[0,10] DMEFF008?"
    [Arguments("X != 5", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    // not(...)
    [Arguments("not (X >= 10)", "assign X to X + 1", "X", "X=[0,9] clean")] // V-02: not is the complement: X < 10, strict; master "X=[0,10] DMEFF008?"
    [Arguments("not (X > 9)", "assign X to X + 1", "X", "X=[0,9] clean")] // V-02: not is the complement: X <= 9; master "X=[0,10] DMEFF008?"
    [Arguments("not (not (X <= 9))", "assign X to X + 1", "X", "X=[0,9] clean")] // V-02: not not x is x; master "X=[0,10] DMEFF008?"
    [Arguments("not (X < 3 or X > 7)", "assign X to X + 1", "X", "X=[3,7] clean")] // V-02: De Morgan: X >= 3 and X <= 7; master "X=[0,10] DMEFF008?"
    [Arguments("not (X > 0 and X < 10)", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    // or
    [Arguments("X <= 4 or X <= 8", "assign X to X + 1", "X", "X=[0,8] clean")] // V-02: or is the hull of the arms; master "X=[0,10] DMEFF008?"
    [Arguments("X <= 2 or X >= 9", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X > 100 or X <= 4", "assign X to X + 1", "X", "X=[0,4] clean")] // V-02: the dead arm X > 100 is skipped; master "X=[0,10] DMEFF008?"
    [Arguments("X <= 4 or Name == \"a\"", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    // guards no value satisfies
    [Arguments("X > 10", "assign X to X + 0", "X", "X=[0,10] DMCS001!")] // V-02: new rejection: no integer in range(0,10) is above 10; master "X=[10,10] clean"
    [Arguments("X < 0", "assign X to X + 0", "X", "X=[0,10] DMCS001!")] // V-02: new rejection: no integer in range(0,10) is below 0; master "X=[0,0] clean"
    [Arguments("not (X >= 0)", "assign X to X + 0", "X", "X=[0,10] DMCS001!")] // V-02: new rejection: not (X >= 0) is X < 0; master "X=[0,10] clean"
    [Arguments("X >= 10 and X != 10", "assign X to X + 0", "X", "X=[10,10] DMCS001!")] // V-02: new rejection: [10,10] minus 10 is empty; master "X=[10,10] clean"
    [Arguments("X == 5 and X != 5", "assign X to X + 0", "X", "X=[5,5] DMCS001!")] // V-02: new rejection: a point range minus its value is empty; master "X=[5,5] clean"
    [Arguments("not (Start <= End)", "assign Start to Start + 0", "Start", "Start=[0,5] DMCS001!")] // V-02: new rejection: the complement relation Start > End contradicts the ranges; master "Start=[0,5] clean"
    // neighbours that must not move
    [Arguments("X <= 9", "assign X to X + 1", "X", "X=[0,9] clean")]
    [Arguments("X >= 1", "assign X to X - 1", "X", "X=[1,10] clean")]
    [Arguments("9 >= X", "assign X to X + 1", "X", "X=[0,9] clean")]
    [Arguments("X >= 1 and X <= 9", "assign X to X + 1", "X", "X=[1,9] clean")]
    [Arguments("X == 5", "assign X to X + 1", "X", "X=[5,5] clean")]
    [Arguments("X > 11", "assign X to X + 0", "X", "X=[0,10] DMCS001!")]
    [Arguments("Start < End or Start == End", "assign Start to Start + 0", "Start", "Start=[0,5] clean")]
    // Decimal: strict stays inclusive (OQ-5); != never narrows a decimal axis
    [Arguments("D < 5.5", "assign D to D + 0", "D", "D=[0,5.5] clean")]
    [Arguments("D > 0.5", "assign D to D + 0", "D", "D=[0.5,10] clean")]
    [Arguments("D != 10", "assign D to D + 1", "D", "D=[0,10] DMEFF008?")]
    [Arguments("not (D >= 10)", "assign D to D + 1", "D", "D=[0,10] DMEFF008?")]
    [Arguments("not (D > 5)", "assign D to D + 0", "D", "D=[0,5] clean")] // V-02: not is the complement: D <= 5; master "D=[0,10] clean"
    [Arguments("D <= 4 or D <= 8", "assign D to D + 0", "D", "D=[0,8] clean")] // V-02: or is the hull of the arms; master "D=[0,10] clean"
    public async Task Pin_PolicyGuard_Verdict(string guard, string body, string probe, string expected) {
        await Assert.That(PolicyVerdict(guard, body, probe)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("if (X < 10) { assign X to X + 1 }", "clean")] // V-02: the then-branch strict bound is exact; master "DMEFF008?"
    [Arguments("if (X >= 10) { assign X to 10 } else { assign X to X + 1 }", "clean")] // V-02: the else-branch negation X < 10 is strict; master "DMEFF008?"
    [Arguments("if (X != 10) { assign X to X + 1 }", "clean")] // V-02: != at the integral endpoint narrows the then-branch; master "DMEFF008?"
    [Arguments("if (not (X >= 10)) { assign X to X + 1 }", "clean")] // V-02: not narrows the then-branch; master "DMEFF008?"
    [Arguments("if (X <= 4 or X <= 8) { assign X to X + 1 }", "clean")] // V-02: or narrows the then-branch to the hull; master "DMEFF008?"
    [Arguments("if (X == 10) { assign X to 10 } else { assign X to X + 1 }", "clean")] // V-02: the else-branch negation of == is != at the endpoint; master "DMEFF008?"
    [Arguments("if (X > 0 and X < 10) { assign X to 0 } else { assign X to X + 1 }", "DMEFF008?")]
    // neighbours
    [Arguments("if (X <= 9) { assign X to X + 1 }", "clean")]
    [Arguments("if (X > 9) { assign X to 10 } else { assign X to X + 1 }", "clean")]
    public async Task Pin_IfGuard_Verdict(string body, string expected) {
        var (_, analysis) = Analyze(ActionDomain(body));
        await Assert.That(Codes(analysis)).IsEqualTo(expected);
    }

    [Test]
    public async Task Pin_SelfInvoke_CalleeStrictGuard_Verdict() {
        var (_, analysis) = Analyze("""
            domain G
            E: entity {
              N: Number range(0, 100)
              Room: policy { N < 100 }
              Inc: action require Room { assign N to N + 1 }
              Go: action { invoke Inc }
            }
            """);
        // V-02: the callee's strict guard narrows through the same refiner; master "DMEFF008?".
        await Assert.That(Codes(analysis)).IsEqualTo("clean");
    }

    /// <summary>A callee entity's guard narrows with the callee's own property types: <c>B.X</c> is Decimal here
    /// while the caller's <c>X</c> is a Number, so the callee's strict guard stays inclusive and the warning
    /// stands.</summary>
    [Test]
    public async Task Pin_CrossEntityInvoke_CalleeDecimalNamedLikeCallerNumber_Verdict() {
        var (_, analysis) = Analyze("""
            domain G
            A: entity {
              X: Number range(0, 10)
              b: B
              Go: action { invoke b.Bump }
            }
            B: entity {
              X: Decimal range(0, 10)
              Room: policy { X < 10 }
              Bump: action require Room { assign X to X + 0.5 }
            }
            """);
        await Assert.That(Codes(analysis)).IsEqualTo("DMEFF008?");
    }

    [Test]
    public async Task Pin_CrossEntityInvoke_CalleeNumberNamedLikeCallerDecimal_Verdict() {
        var (_, analysis) = Analyze("""
            domain G
            A: entity {
              X: Decimal range(0, 10)
              b: B
              Go: action { invoke b.Bump }
            }
            B: entity {
              X: Number range(0, 10)
              Room: policy { X < 10 }
              Bump: action require Room { assign X to X + 1 }
            }
            """);
        // V-02: the callee B.X is a Number, so its strict guard is exact; master "DMEFF008?".
        await Assert.That(Codes(analysis)).IsEqualTo("clean");
    }

    [Test]
    public async Task Pin_ForEachInvoke_CalleeStrictGuardAndWherePolicy_Verdict() {
        var (_, analysis) = Analyze("""
            domain G
            A: entity {
              items: many B
              Go: action { for items as i where i Low invoke i.Bump }
            }
            B: entity {
              X: Number range(0, 10)
              Y: Number range(0, 10)
              a: A
              Low: policy { Y < 10 }
              Room: policy { X < 10 }
              Bump: action require Room { assign X to X + 1 assign Y to Y + 1 }
            }
            """);
        await Assert.That(Codes(analysis)).IsEqualTo("DMEFF008?");
    }

    /// <summary>PROBE 1 (V-02 closing round, finding 2): <c>if (X &gt; 10)</c> on <c>range(0, 10)</c> is now provably
    /// dead. Master analysed its then-branch with X = [10,10] and reported DMEFF008 (error) on the
    /// assignment <c>X + 1</c> (value range [11, 11], entirely outside the range); on this head the branch environment
    /// is unsatisfiable, so <c>Narrow</c> keeps the old constraint list and <c>X + 1</c> evaluates to roughly [1, 11]: the verdict
    /// becomes DMEFF008 as a warning ("can fall outside"). An assignment in a branch that cannot run is reported
    /// as a warning, not an error. A dead branch still publishes its postconditions (V-03's row).</summary>
    [Test]
    public async Task Pin_DeadBranchAssignment_Verdict() {
        var (_, analysis) = Analyze(ActionDomain("if (X > 10) { assign X to X + 1 }"));
        await Assert.That(Codes(analysis)).IsEqualTo("DMEFF008?");
    }

    /// <summary>F4: an optional property's range describes its present values, as the else-branch already
    /// does; <c>not (W &gt;= 10)</c> is also true for a null <c>W</c>.</summary>
    [Test]
    public async Task Pin_OptionalProperty_NotGuard_Verdict() {
        var (domain, analysis) = Analyze("""
            domain G
            E: entity {
              W: Number optional range(0, 10)
              P: policy { not (W >= 10) }
              Go: action require P { assign W to W + 1 }
            }
            """);
        // V-02: not (W >= 10) narrows the present values to W < 10 (F4); master "W=[0,10] DMEFF008?".
        await Assert.That($"W={Narrowed(Context(domain, analysis), "W")} {Codes(analysis)}").IsEqualTo("W=[0,9] clean");
    }

    /// <summary>Lock amendment 9: whether an inline comparison parses as an action guard (#580's
    /// <c>Dec: action require X &gt; 0</c>).</summary>
    [Test]
    public async Task Pin_InlineRequireComparison_Parse() {
        string outcome;
        try {
            var (domain, analysis) = Analyze("""
                domain G
                E: entity {
                  X: Number range(0, 10)
                  Dec: action require X > 0 { assign X to X - 1 }
                }
                """);
            outcome = $"X={Narrowed(Context(domain, analysis, action: "Dec"), "X")} {Codes(analysis)}";
        }
        catch (Exception ex) {
            outcome = "throws " + ex.GetType().Name;
        }
        // An inline comparison is not a guard in the DSL: `require` takes policy names, so V-02's tests use a named policy.
        await Assert.That(outcome).IsEqualTo("throws GrammarException");
    }

    /// <summary>S1 pin (green on master): the T-04 stamp is on the Domain node after analysis and names
    /// <c>Number</c> integral and <c>Decimal</c> decimal for DSL-declared properties.</summary>
    [Test]
    public async Task Pin_TypeModelStamp_KindOfDslNumericNames() {
        var (domain, analysis) = Analyze("""
            domain G
            E: entity {
              A: Number
              D: Decimal
            }
            """);
        await Assert.That(Kinds(domain, analysis, "E")).IsEqualTo("A:Integer64,D:Decimal");
    }

    /// <summary>S1 pin (green on master): through the session's default type maps, the model-level names
    /// <c>Int</c> and <c>Int64</c> are integral too (they are not DSL type names).</summary>
    [Test]
    public async Task Pin_TypeModelStamp_KindOfIntAndInt64() {
        var item = new Entity("Item",
            [new Property("A", new DomainTypeReference("Number"), []),
             new Property("B", new DomainTypeReference("Int"), []),
             new Property("C", new DomainTypeReference("Int64"), []),
             new Property("D", new DomainTypeReference("Decimal"), [])],
            [], [], []);
        var domain = new Domain("G", [item]);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(Kinds(domain, analysis, "Item")).IsEqualTo("A:Integer64,B:Integer64,C:Integer64,D:Decimal");
    }

    // ── What each guard form narrows to ──

    private static string NarrowedBy(string guard, string probe, string props = Props, string body = "assign Y to 0") {
        var (domain, analysis) = Analyze(PolicyDomain(guard, body, props));
        return Narrowed(Context(domain, analysis), probe);
    }

    private static bool HasCode(AnalysisResult analysis, string code) => analysis.Diagnostics.Any(d => d.Code == code);

    private static AnalysisResult AnalysisOf(string dsl) => Analyze(dsl).Analysis;

    [Test]
    public async Task StrictLessThan_OnIntegral_NarrowsToValueMinusOne() {
        // #477's repro, and its `N <= 99` control.
        const string Strict = """
            domain P1
            Counter: entity {
              N: Number default(0) range(0, 100)
              Room: policy { N < 100 }
              Inc: action require Room { assign N to N + 1 }
            }
            """;
        var (domain, analysis) = Analyze(Strict);
        var (controlDomain, control) = Analyze(Strict.Replace("N < 100", "N <= 99", StringComparison.Ordinal));

        await Assert.That(HasCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
        await Assert.That(Narrowed(Context(domain, analysis, "Counter", "Inc"), "N")).IsEqualTo("[0,99]");
        await Assert.That(Narrowed(Context(domain, analysis, "Counter", "Inc"), "N"))
            .IsEqualTo(Narrowed(Context(controlDomain, control, "Counter", "Inc"), "N"));
        await Assert.That(Codes(analysis)).IsEqualTo(Codes(control));
    }

    [Test]
    public async Task StrictGreaterThan_OnIntegral_NarrowsToValuePlusOne() {
        var analysis = AnalysisOf(PolicyDomain("X > 0", "assign X to X - 1"));
        await Assert.That(HasCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
        await Assert.That(NarrowedBy("X > 0", "X")).IsEqualTo("[1,10]");
    }

    [Test]
    public async Task ReversedLiteral_ZeroLessThanX_And_HundredGreaterThanN() {
        await Assert.That(NarrowedBy("0 < X", "X")).IsEqualTo("[1,10]");
        await Assert.That(NarrowedBy("10 > X", "X")).IsEqualTo("[0,9]");
        await Assert.That(Codes(AnalysisOf(PolicyDomain("0 < X", "assign X to X - 1")))).IsEqualTo("clean");
    }

    [Test]
    public async Task StrictBoundWithFractionalLiteral_OnIntegral() {
        // The literal is already a DMSEM003 type error against a Number; the narrowing is still exact.
        await Assert.That(NarrowedBy("X < 9.5", "X")).IsEqualTo("[0,9]");
        await Assert.That(NarrowedBy("X > 0.5", "X")).IsEqualTo("[1,10]");
    }

    [Test]
    public async Task StrictConjunction_OnIntegral_NarrowsBothSides() {
        await Assert.That(NarrowedBy("X > 0 and X < 10", "X")).IsEqualTo("[1,9]");
    }

    [Test]
    public async Task NotEqual_AtBound_NarrowsIntegral() {
        var analysis = AnalysisOf(PolicyDomain("X != 10", "assign X to X + 1"));
        await Assert.That(HasCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
        await Assert.That(NarrowedBy("X != 10", "X")).IsEqualTo("[0,9]");
        await Assert.That(NarrowedBy("X != 0", "X")).IsEqualTo("[1,10]");
    }

    [Test]
    public async Task NotEqual_InsideRange_DoesNotNarrow() {
        await Assert.That(NarrowedBy("X != 5", "X")).IsEqualTo("[0,10]");
    }

    [Test]
    public async Task NotEqual_OnDecimal_DoesNotNarrow_ExceptPoint() {
        await Assert.That(NarrowedBy("D != 10", "D")).IsEqualTo("[0,10]");
        await Assert.That(NarrowedBy("D != 0", "D")).IsEqualTo("[0,10]");
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("D == 5.5 and D != 5.5", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X == 5 and X != 5", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X >= 10 and X != 10", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
    }

    [Test]
    public async Task NotWrapper_NegatesComparison() {
        // #580's repro.
        var analysis = AnalysisOf("""
            domain D
            E: entity {
              X: Number range(0, 10)
              P: policy { not (X >= 10) }
              Go: action require P { assign X to X + 1 }
            }
            """);
        await Assert.That(HasCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
        await Assert.That(NarrowedBy("not (X >= 10)", "X")).IsEqualTo("[0,9]");
        await Assert.That(NarrowedBy("not (X > 0)", "X")).IsEqualTo("[0,0]");
        await Assert.That(NarrowedBy("not (not (X >= 1))", "X")).IsEqualTo("[1,10]");
        await Assert.That(NarrowedBy("not (X != 7)", "X")).IsEqualTo("[7,7]");
        await Assert.That(NarrowedBy("not (X == 10)", "X")).IsEqualTo("[0,9]");
    }

    [Test]
    public async Task NotOverAnd_DeMorgan_JoinsArms() {
        // not (X > 0 and X < 10) is X <= 0 or X >= 10: the hull is the whole range.
        await Assert.That(NarrowedBy("not (X > 0 and X < 10)", "X")).IsEqualTo("[0,10]");
        await Assert.That(NarrowedBy("not (X < 3 or X > 7)", "X")).IsEqualTo("[3,7]");
    }

    [Test]
    public async Task OrGuard_JoinsArms_DoesNotNarrowToOneArm() {
        await Assert.That(NarrowedBy("X <= 4 or X <= 8", "X")).IsEqualTo("[0,8]");
        await Assert.That(Codes(AnalysisOf(PolicyDomain("X <= 4 or X <= 8", "assign X to X + 1")))).IsEqualTo("clean");
        // Soundness control: either arm alone would exclude a value the guard admits.
        await Assert.That(NarrowedBy("X <= 2 or X >= 9", "X")).IsEqualTo("[0,10]");
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X <= 2 or X >= 9", "assign X to X + 1")),
            DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task OrWithArmOnOtherSymbol_DoesNotNarrow() {
        await Assert.That(NarrowedBy("X <= 4 or Name == \"a\"", "X")).IsEqualTo("[0,10]");
        await Assert.That(NarrowedBy("X <= 4 or D >= 5", "X")).IsEqualTo("[0,10]");
        await Assert.That(NarrowedBy("X <= 4 or D >= 5", "D")).IsEqualTo("[0,10]");
    }

    [Test]
    public async Task OrDeadArm_IsSkipped() {
        await Assert.That(NarrowedBy("X > 100 or X <= 4", "X")).IsEqualTo("[0,4]");
        await Assert.That(NarrowedBy("X <= 4 or X > 10", "X")).IsEqualTo("[0,4]");
    }

    /// <summary>Lock amendment 3: both arms dead leaves the incoming environment, as a dead if/else does.</summary>
    [Test]
    public async Task OrBothArmsDead_LeavesTheIncomingEnvironment() {
        var (domain, analysis) = Analyze(PolicyDomain("X > 100 or X < 0", "assign Y to 0"));
        await Assert.That(Narrowed(Context(domain, analysis), "X")).IsEqualTo("[0,10]");
        await Assert.That(HasCode(analysis, DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
    }

    [Test]
    public async Task DecimalStrict_DoesNotOverNarrow() {
        // OQ-5: a strict bound on a Decimal stays inclusive, so the endpoint stays reachable.
        await Assert.That(NarrowedBy("D < 5.5", "D")).IsEqualTo("[0,5.5]");
        await Assert.That(NarrowedBy("D > 0.5", "D")).IsEqualTo("[0.5,10]");
        await Assert.That(NarrowedBy("5.5 > D", "D")).IsEqualTo("[0,5.5]");
        await Assert.That(NarrowedBy("not (D >= 5.5)", "D")).IsEqualTo("[0,5.5]");
        await Assert.That(NarrowedBy("D < 10", "D")).IsEqualTo("[0,10]");
    }

    [Test]
    public async Task StrictGuardPastDeclaredBound_IsUnsatisfiablePrecondition() {
        var analysis = AnalysisOf(PolicyDomain("X > 10", "assign Y to 0"));
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability
            && d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("narrow property 'X' to an empty admissible set", StringComparison.Ordinal))).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X < 0", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("not (X >= 0)", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
    }

    [Test]
    public async Task NegatedRelation_RecordsTheComplement() {
        // not (Start <= End) is Start > End, which Start in [0,5] and End in [10,20] never satisfy.
        var analysis = AnalysisOf(PolicyDomain("not (Start <= End)", "assign Y to 0"));
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability
            && d.Message.Contains("the relation 'Start > End'", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task OrArm_RelationNotRecorded_NoFalseConflict() {
        // Start in [0,5], End in [10,20]: the first arm always holds. A relation recorded inside the second arm
        // would contradict the ranges and report a conflict the guard does not have.
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("Start < End or Start > End", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("not (Start < End and Start > End)", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
    }

    [Test]
    public async Task IfBranch_StrictNarrows_ElseUsesNegation() {
        await Assert.That(Codes(AnalysisOf(ActionDomain("if (X < 10) { assign X to X + 1 }")))).IsEqualTo("clean");
        await Assert.That(Codes(AnalysisOf(ActionDomain("if (X >= 10) { assign X to 10 } else { assign X to X + 1 }"))))
            .IsEqualTo("clean");
        await Assert.That(Codes(AnalysisOf(ActionDomain("if (X == 10) { assign X to 10 } else { assign X to X + 1 }"))))
            .IsEqualTo("clean");
        await Assert.That(Codes(AnalysisOf(ActionDomain("if (not (X >= 10)) { assign X to X + 1 }")))).IsEqualTo("clean");
        await Assert.That(Codes(AnalysisOf(ActionDomain("if (X <= 4 or X <= 8) { assign X to X + 1 }")))).IsEqualTo("clean");
        // The negation of a conjunction is the hull of the negated arms: X <= 0 or X >= 10 can still be 10.
        await Assert.That(HasCode(AnalysisOf(ActionDomain("if (X > 0 and X < 10) { assign X to 0 } else { assign X to X + 1 }")),
            DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task InvokeCalleeGuard_StrictNarrows_SameAsInline() {
        var analysis = AnalysisOf("""
            domain G
            E: entity {
              N: Number range(0, 100)
              Room: policy { N < 100 }
              Inc: action require Room { assign N to N + 1 }
              Go: action { invoke Inc }
            }
            """);
        await Assert.That(Codes(analysis)).IsEqualTo("clean");
    }

    /// <summary>Lock amendment 2: a cross-entity callee narrows with its own entity's integrality.</summary>
    [Test]
    public async Task CrossEntityInvoke_UsesTheTargetEntitysIntegrality() {
        var exact = AnalysisOf("""
            domain G
            A: entity {
              X: Decimal range(0, 10)
              b: B
              Go: action { invoke b.Bump }
            }
            B: entity {
              X: Number range(0, 10)
              Room: policy { X < 10 }
              Bump: action require Room { assign X to X + 1 }
            }
            """);
        await Assert.That(Codes(exact)).IsEqualTo("clean");
        // Soundness: the callee's X is Decimal, so `X < 10` keeps 10 and X + 0.5 can reach 10.5.
        var inclusive = AnalysisOf("""
            domain G
            A: entity {
              X: Number range(0, 10)
              b: B
              Go: action { invoke b.Bump }
            }
            B: entity {
              X: Decimal range(0, 10)
              Room: policy { X < 10 }
              Bump: action require Room { assign X to X + 0.5 }
            }
            """);
        await Assert.That(HasCode(inclusive, DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    /// <summary>The fan-out callee's own guard and the where-policy both narrow the target entity's environment; the
    /// caller's call-chain postconditions read it.</summary>
    [Test]
    public async Task ForEachInvoke_StrictGuardsNarrowTheTargetEntity() {
        var (domain, analysis) = Analyze("""
            domain G
            A: entity {
              items: many B
              Go: action { for items as i where i Low invoke i.Bump }
            }
            B: entity {
              X: Number range(0, 10)
              Y: Number range(0, 10)
              a: A
              Low: policy { Y < 10 }
              Room: policy { X < 10 }
              Bump: action require Room { assign X to X + 1 assign Y to Y + 1 }
            }
            """);
        var bump = EntityNamed(domain, "B").Actions.Single(a => a.Name == "Bump");
        var chain = Context(domain, analysis, "A", "Go").Postconditions
            .Where(p => ReferenceEquals(p.DeclaringAction, bump))
            .ToDictionary(p => p.TargetProperty, p => Range(p.ValueRange));
        await Assert.That(chain["X"]).IsEqualTo("[1,10]");
        await Assert.That(chain["Y"]).IsEqualTo("[1,10]");
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Message.Contains("Call-chain postcondition (Go", StringComparison.Ordinal))).IsFalse();
    }

    /// <summary>F4: an optional property's range describes its present values (the else-branch precedent).</summary>
    [Test]
    public async Task OptionalProperty_NotGuard_NarrowsThePresentValues() {
        var (domain, analysis) = Analyze("""
            domain G
            E: entity {
              W: Number optional range(0, 10)
              P: policy { not (W >= 10) }
              Go: action require P { assign W to W + 1 }
            }
            """);
        await Assert.That($"W={Narrowed(Context(domain, analysis), "W")} {Codes(analysis)}").IsEqualTo("W=[0,9] clean");
    }

    private const string OptionalDomain = """
        domain G
        E: entity {
          W: Number optional range(0, 10)
          V: Number optional range(5, 5)
          X: Number range(0, 10)
          S: Number optional range(0, 5)
          End: Number range(10, 20)
          P: policy { GUARD }
          Go: action require P { assign X to X + 0 }
        }
        """;

    /// <summary>
    /// A comparison is false for a null value except <c>!=</c>: a positive <c>!=</c> or a negated comparison holds for
    /// a null optional property, so it never empties one (the property is then null) and so never makes an
    /// <c>or</c> arm dead. Without this rule the strict and <c>!=</c> narrowing V-02 adds would report a false
    /// unsatisfiable precondition, or narrow another symbol through an arm that does hold.
    /// </summary>
    [Test]
    public async Task OptionalProperty_GuardThatHoldsForNull_NeverEmptiesIt() {
        await Assert.That(Codes(AnalysisOf(OptionalDomain.Replace("GUARD", "not (W >= 0)", StringComparison.Ordinal))))
            .IsEqualTo("clean");
        await Assert.That(Codes(AnalysisOf(OptionalDomain.Replace("GUARD", "V != 5", StringComparison.Ordinal))))
            .IsEqualTo("clean");
        await Assert.That(Codes(AnalysisOf(OptionalDomain.Replace("GUARD", "not (S <= End)", StringComparison.Ordinal))))
            .IsEqualTo("clean");
        var (domain, analysis) = Analyze(OptionalDomain.Replace("GUARD", "not (W >= 0) or X <= 4", StringComparison.Ordinal));
        await Assert.That(Narrowed(Context(domain, analysis), "X")).IsEqualTo("[0,10]");
        // A guard that is false for a null value still empties the property: the precondition is unsatisfiable.
        await Assert.That(HasCode(AnalysisOf(OptionalDomain.Replace("GUARD", "W > 10", StringComparison.Ordinal)),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
    }

    /// <summary>The else-branch of <c>if (W &lt;= 20)</c> runs when the optional <c>W</c> is null, so it is not dead
    /// and its assignment reaches the join.</summary>
    [Test]
    public async Task OptionalProperty_ElseBranchThatHoldsForNull_IsNotDead() {
        var analysis = AnalysisOf("""
            domain G
            E: entity {
              W: Number optional range(0, 10)
              X: Number range(0, 10)
              Go: action {
                if (W <= 20) { assign X to 0 } else { assign X to 10 }
                assign X to X + 1
              }
            }
            """);
        await Assert.That(HasCode(analysis, DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    // ── Fail closed without the type model ──

    [Test]
    public async Task Pipeline_RunsTheExpressionTypePassBeforeEffectInvariant() {
        var (_, analysis) = Analyze(PolicyDomain("X < 10", "assign Y to 0"));
        var order = analysis.Telemetry.Passes.Select(p => p.PassName).ToList();
        await Assert.That(order.IndexOf(ExpressionTypeAnalyzer.Id)).IsGreaterThanOrEqualTo(0);
        await Assert.That(order.IndexOf(ExpressionTypeAnalyzer.Id)).IsLessThan(order.IndexOf(EffectInvariantAnalyzer.Id));
    }

    [Test]
    public async Task EffectInvariant_WithoutTheTypeModelStamp_FailsClosed() {
        var item = new Entity("Item",
            [new Property("X", new DomainTypeReference("Number"), [new RangeConstraint(0L, 10L)])],
            [], [], []);
        var domain = new Domain("G", [item]);
        var analyzer = new AnalyzerBuilder()
            .AddAnalyzer(new DomainCatalogPass())
            .AddAnalyzer(new EffectInvariantAnalyzer())
            .Build();

        InvalidOperationException? thrown = null;
        try { analyzer.Analyze(domain); }
        catch (InvalidOperationException ex) { thrown = ex; }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).Contains(nameof(DomainTypeModelMetadata));
    }

    // ── Point oracles (pins: green on master, which only over-approximates) ──

    private static readonly string[] IntegralGuards = [
        "X < 5", "X <= 5", "X > 5", "X >= 5", "X == 5", "X != 5", "X != 0", "X != 10", "5 < X", "5 > X",
        "X < 4.5", "X > 4.5", "X > 10", "X < 0", "not (X >= 0)", "not (X < 5)", "not (X != 10)", "not (X == 0)",
        "not (not (X > 3))", "X > 2 and X < 8", "X < 2 or X > 8", "X <= 4 or X <= 8", "X > 100 or X <= 4",
        "not (X > 2 and X < 8)", "not (X < 3 or X > 7)", "X >= 10 and X != 10", "X == 5 and X != 5",
        "X > 100 or X < 0", "not (X != 0 and X != 10)",
    ];

    private static readonly string[] DecimalGuards = [
        "D < 5.5", "D <= 5.5", "D > 5.5", "D >= 5.5", "D == 5.5", "D != 5.5", "D != 10", "D != 0", "5.5 > D",
        "not (D >= 5.5)", "not (D <= 5.5)", "not (D == 5.5)", "not (D != 5.5)", "D < 2.5 or D > 7.5",
        "not (D > 2.5 and D < 7.5)", "D == 5.5 and D != 5.5",
    ];

    private static IEnumerable<decimal> IntegralSamples() => Enumerable.Range(0, 11).Select(i => (decimal)i);

    private static IEnumerable<decimal> DecimalSamples() => Enumerable.Range(0, 41).Select(i => i * 0.25m);

    private static bool Contains(AbstractValue value, decimal v) =>
        !value.Unsatisfiable
        && (value.NumericRange is not { } r || ((r.Min is null || r.Min <= v) && (r.Max is null || v <= r.Max)));

    /// <summary>The then-arm and else-arm values of <c>if (guard) { assign copy to probe } else { … }</c>.</summary>
    private static (AbstractValue Then, AbstractValue Else) Arms(string guard, string probe, string copy, string props) {
        var (domain, analysis) = Analyze(ActionDomain(
            $"if ({guard}) {{ assign {copy} to {probe} }} else {{ assign {copy} to {probe} }}", props));
        var posts = Context(domain, analysis).Postconditions.Where(p => p.TargetProperty == copy).ToList();
        if (posts.Count != 2) throw new InvalidOperationException($"expected two '{copy}' postconditions for '{guard}', got {posts.Count}");
        return (posts[0].Value, posts[1].Value);
    }

    private static List<string> OracleMisses(string[] guards, string probe, string copy, IEnumerable<decimal> samples,
        Truth admitted, bool negatedArm, string props = Props) {
        var misses = new List<string>();
        foreach (var guard in guards) {
            var arms = Arms(guard, probe, copy, props);
            var arm = negatedArm ? arms.Else : arms.Then;
            var (policyDomain, policyAnalysis) = Analyze(PolicyDomain(guard, "assign Y to 0", props));
            var policyExpr = EntityNamed(policyDomain, "E").Policies.Single(p => p.Name == "P").Expression;
            foreach (var v in samples) {
                var point = new Dictionary<string, AbstractValue>(StringComparer.Ordinal) {
                    [probe] = AbstractValue.From([new RangeConstraint(v, v)]),
                };
                if (ProvableTruth.Decide(policyExpr, point) != admitted) continue;
                if (!Contains(arm, v))
                    misses.Add($"{(negatedArm ? "else" : "then")} of '{guard}' excludes {v}");
                if (!negatedArm) {
                    if (!Contains(AbstractValue.From([.. Context(policyDomain, policyAnalysis).MergedConstraints[probe]]), v))
                        misses.Add($"policy '{guard}' excludes {v}");
                    if (HasCode(policyAnalysis, DomainModelDiagnosticCodes.ConstraintSatisfiability))
                        misses.Add($"policy '{guard}' reported unsatisfiable but admits {v}");
                }
            }
        }
        return misses;
    }

    [Test]
    public async Task Refine_PointOracle_NeverExcludesATrueValue() {
        var misses = OracleMisses(IntegralGuards, "X", "Y", IntegralSamples(), Truth.True, negatedArm: false);
        misses.AddRange(OracleMisses(DecimalGuards, "D", "Z", DecimalSamples(), Truth.True, negatedArm: false));
        await Assert.That(misses).IsEmpty();
    }

    [Test]
    public async Task RefineNegated_PointOracle_NeverExcludesAFalseValue() {
        var misses = OracleMisses(IntegralGuards, "X", "Y", IntegralSamples(), Truth.False, negatedArm: true);
        misses.AddRange(OracleMisses(DecimalGuards, "D", "Z", DecimalSamples(), Truth.False, negatedArm: true));
        await Assert.That(misses).IsEmpty();
    }

    private static readonly string[] NegativeGuards = [
        "X < 0", "X > 0", "X <= 0", "X >= 0", "X < 0.5", "X > 0.5", "X < 4.5", "X > 4.5", "X != 5", "X == 0",
        "X > 0 and X < 3", "X < 0 or X > 3", "not (X > 0)", "not (X < 0)", "not (X != 5)", "not (X <= 0 or X >= 3)",
        "0 < X", "0.5 > X", "X < 5", "X > 5", "X <= 0 and X != 0", "X >= 0 and X != 0",
    ];

    /// <summary>PROBE 3 (V-02 closing round): the integral steps stay sound on a range with negative bounds. The
    /// sample range runs from -5 to 5. The oracle's guards come from the DSL, which has no negative literal
    /// (<c>X &gt; -0.5</c> does not parse), so negative literals are pinned by
    /// <see cref="StrictBoundWithNegativeFractionalLiteral_RoundsTowardTheAdmittedSide"/> through the API. The
    /// oracle has no optional properties: <see cref="AbstractValue"/> cannot carry a null, so the null rule is
    /// covered by <c>OptionalNumberNullCompareParityTests</c> and the hand tests above.</summary>
    [Test]
    public async Task PointOracle_NegativeIntegralRange_NeverExcludesAValue() {
        var samples = Enumerable.Range(-5, 11).Select(i => (decimal)i).ToList();
        var misses = OracleMisses(NegativeGuards, "X", "Y", samples, Truth.True, negatedArm: false, NegativeProps);
        misses.AddRange(OracleMisses(NegativeGuards, "X", "Y", samples, Truth.False, negatedArm: true, NegativeProps));
        await Assert.That(misses).IsEmpty();
    }

    private static string NegativeLiteralNarrowing(DomainExpression guard) {
        var policy = new Policy("P", guard);
        var item = new Entity("Item",
            [new Property("X", new DomainTypeReference("Number"), [new RangeConstraint(-5L, 5L)]),
             new Property("Y", new DomainTypeReference("Number"), [])],
            [new Poly.DomainModeling.Ontology.Action("Go", InvocationResult.Void, [],
                [new Poly.DomainModeling.Ontology.Effects.AssignEffect(DomainExpression.Property("Y"), DomainExpression.Literal(0L))], [policy])],
            [policy], []);
        var domain = new Domain("G", [item]);
        return Narrowed(Context(domain, DomainModelAnalyzer.Analyze(domain), "Item"), "X");
    }

    /// <summary>PROBE 3: a literal below zero rounds toward the admitted side: <c>X &gt; -0.5</c> admits the integers
    /// from 0, <c>X &lt; -0.5</c> those up to -1. Built through the API: the DSL has no negative literal.</summary>
    [Test]
    public async Task StrictBoundWithNegativeFractionalLiteral_RoundsTowardTheAdmittedSide() {
        static DomainExpression X() => DomainExpression.Property("X");
        await Assert.That(NegativeLiteralNarrowing(DomainExpression.GreaterThan(X(), DomainExpression.Literal(-0.5m)))).IsEqualTo("[0,5]");
        await Assert.That(NegativeLiteralNarrowing(DomainExpression.LessThan(X(), DomainExpression.Literal(-0.5m)))).IsEqualTo("[-5,-1]");
        await Assert.That(NegativeLiteralNarrowing(DomainExpression.GreaterThan(X(), DomainExpression.Literal(-2L)))).IsEqualTo("[-1,5]");
        await Assert.That(NegativeLiteralNarrowing(DomainExpression.LessThan(X(), DomainExpression.Literal(-2L)))).IsEqualTo("[-5,-3]");
        await Assert.That(NegativeLiteralNarrowing(DomainExpression.Not(DomainExpression.GreaterThanOrEqual(X(), DomainExpression.Literal(-0.5m))))).IsEqualTo("[-5,-1]");
    }

    /// <summary>F1: an integral property whose guards leave no integer between strict bounds is unsatisfiable
    /// (<c>X &gt; 5 and X &lt; 6</c> is X &gt;= 6 and X &lt;= 5). Master kept [5,10] and [0,6] and accepted it.</summary>
    [Test]
    public async Task IntegralGuards_WithNoIntegerBetweenStrictBounds_AreUnsatisfiable() {
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X > 5 and X < 6", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X > 5 and X < 7", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
    }

    /// <summary>F1: an equality combined with a strict comparison at the same value is unsatisfiable.</summary>
    [Test]
    public async Task EqualityWithStrictComparisonAtTheSameValue_IsUnsatisfiable() {
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X == 5 and X > 5", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X == 5 and X < 5", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("X == 5 and X >= 5", "assign Y to 0")),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
    }

    /// <summary>F1: a negated <c>!=</c> is an equality; outside the declared range it is unsatisfiable.</summary>
    [Test]
    public async Task NegatedNotEqual_OutsideTheRange_IsUnsatisfiable() {
        const string Narrow = "  X: Number range(0, 4)\n  Y: Number";
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("not (X != 5)", "assign Y to 0", Narrow)),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsTrue();
        await Assert.That(HasCode(AnalysisOf(PolicyDomain("not (X != 3)", "assign Y to 0", Narrow)),
            DomainModelDiagnosticCodes.ConstraintSatisfiability)).IsFalse();
    }

    /// <summary>F3: a property whose type does not resolve is NON-integral to the narrowing, so a strict bound keeps
    /// its inclusive endpoint. That is conservative: it can keep a warning and never drops one. T-05b changes the
    /// provider's name resolution (lowercase spellings such as <c>long</c>); a name that starts to resolve to an
    /// integral type becomes integral here, so a pin on such a name is then edited on purpose (<c>[0,9]</c>).
    /// <c>Mystery</c> resolves to nothing and must stay non-integral.</summary>
    [Test]
    public async Task UnresolvedPropertyType_IsTreatedAsNonIntegral() {
        var guard = new Policy("P", DomainExpression.LessThan(DomainExpression.Property("L"), DomainExpression.Literal(10L)));
        var item = new Entity("Item",
            [new Property("L", new DomainTypeReference("Mystery"), [new RangeConstraint(0L, 10L)]),
             new Property("N", new DomainTypeReference("Number"), [new RangeConstraint(0L, 10L)]),
             new Property("Y", new DomainTypeReference("Number"), [])],
            [new Poly.DomainModeling.Ontology.Action("Go", InvocationResult.Void, [],
                [new Poly.DomainModeling.Ontology.Effects.AssignEffect(DomainExpression.Property("Y"), DomainExpression.Literal(0L))], [guard])],
            [guard], []);
        var domain = new Domain("G", [item]);
        var analysis = DomainModelAnalyzer.Analyze(domain);
        await Assert.That(Kinds(domain, analysis, "Item")).IsEqualTo("L:None,N:Integer64,Y:Integer64");
        await Assert.That(Narrowed(Context(domain, analysis, "Item"), "L")).IsEqualTo("[0,10]");
    }

    private static string Kinds(Domain domain, AnalysisResult analysis, string entity) {
        var model = analysis.GetMetadata<DomainTypeModelMetadata>(domain)?.Model
            ?? throw new InvalidOperationException("no DomainTypeModelMetadata on the Domain node");
        return string.Join(",", EntityNamed(domain, entity).Properties.Select(p => $"{p.Name}:{model.KindOf(model.OfProperty(p))}"));
    }
}