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

    private static string PolicyDomain(string guard, string body) => $$"""
        domain G
        E: entity {
        {{Props}}
          P: policy { {{guard}} }
          Go: action require P { {{body}} }
        }
        """;

    private static string ActionDomain(string body) => $$"""
        domain G
        E: entity {
        {{Props}}
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

    // ── Characterisation pins (recorded on master 1907c75d before any production edit) ──

    [Test]
    // integral strict, reversed literal, fractional literal (a fractional literal against a Number is already DMSEM003)
    [Arguments("X < 10", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X > 0", "assign X to X - 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("0 < X", "assign X to X - 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("10 > X", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X < 9.5", "assign X to X + 1", "X", "X=[0,9.5] DMEFF008?,DMSEM003!")]
    [Arguments("X > 0.5", "assign X to X - 1", "X", "X=[0.5,10] DMEFF008?,DMSEM003!")]
    [Arguments("X > 0 and X < 10", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    // != at an endpoint, inside, and on a point
    [Arguments("X != 10", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X != 0", "assign X to X - 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X != 5", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    // not(...)
    [Arguments("not (X >= 10)", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("not (X > 9)", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("not (not (X <= 9))", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("not (X < 3 or X > 7)", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("not (X > 0 and X < 10)", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    // or
    [Arguments("X <= 4 or X <= 8", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X <= 2 or X >= 9", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X > 100 or X <= 4", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    [Arguments("X <= 4 or Name == \"a\"", "assign X to X + 1", "X", "X=[0,10] DMEFF008?")]
    // guards no value satisfies
    [Arguments("X > 10", "assign X to X + 0", "X", "X=[10,10] clean")]
    [Arguments("X < 0", "assign X to X + 0", "X", "X=[0,0] clean")]
    [Arguments("not (X >= 0)", "assign X to X + 0", "X", "X=[0,10] clean")]
    [Arguments("X >= 10 and X != 10", "assign X to X + 0", "X", "X=[10,10] clean")]
    [Arguments("X == 5 and X != 5", "assign X to X + 0", "X", "X=[5,5] clean")]
    [Arguments("not (Start <= End)", "assign Start to Start + 0", "Start", "Start=[0,5] clean")]
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
    [Arguments("not (D > 5)", "assign D to D + 0", "D", "D=[0,10] clean")]
    [Arguments("D <= 4 or D <= 8", "assign D to D + 0", "D", "D=[0,10] clean")]
    public async Task Pin_PolicyGuard_Verdict(string guard, string body, string probe, string expected) {
        await Assert.That(PolicyVerdict(guard, body, probe)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("if (X < 10) { assign X to X + 1 }", "DMEFF008?")]
    [Arguments("if (X >= 10) { assign X to 10 } else { assign X to X + 1 }", "DMEFF008?")]
    [Arguments("if (X != 10) { assign X to X + 1 }", "DMEFF008?")]
    [Arguments("if (not (X >= 10)) { assign X to X + 1 }", "DMEFF008?")]
    [Arguments("if (X <= 4 or X <= 8) { assign X to X + 1 }", "DMEFF008?")]
    [Arguments("if (X == 10) { assign X to 10 } else { assign X to X + 1 }", "DMEFF008?")]
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
        await Assert.That(Codes(analysis)).IsEqualTo("DMEFF008?");
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
        await Assert.That(Codes(analysis)).IsEqualTo("DMEFF008?");
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
        await Assert.That($"W={Narrowed(Context(domain, analysis), "W")} {Codes(analysis)}").IsEqualTo("W=[0,10] DMEFF008?");
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

    private static string Kinds(Domain domain, AnalysisResult analysis, string entity) {
        var model = analysis.GetMetadata<DomainTypeModelMetadata>(domain)?.Model
            ?? throw new InvalidOperationException("no DomainTypeModelMetadata on the Domain node");
        return string.Join(",", EntityNamed(domain, entity).Properties.Select(p => $"{p.Name}:{model.KindOf(model.OfProperty(p))}"));
    }
}