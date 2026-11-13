using Poly.Tests.Docs;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// T-05b census (lock #679 6845700954, the safeguard and A9): every pinned analyzer grid plus the residual-forms grid,
/// derived by running the analyzer, never hand-written. The residual grid places each form T-05a left untyped (and
/// the controls next to it) in the positions that consume a type, as a whole DSL document per cell, so the same
/// document can go through every door (<see cref="GateParity"/>) with a scenario step: create the order, invoke the
/// action that holds the form, then move the order out of its first stage (entry and exit effects run).
/// </summary>
internal static class StrictGateCensus {
    /// <summary>A residual form, the type it would have if it typed, and whether it needs a loop binder or an entity.</summary>
    internal sealed record ResidualForm(string Label, string Dsl, string Type, bool Binder = false, string? InFunction = null);

    internal static readonly ResidualForm[] ResidualForms = [
        new("High (bare enum member)", "High", "Level", InFunction: "High"),
        new("Guid (keyword)", "Guid", "Uuid"),
        new("Stamp - Stamp (DateTime)", "Stamp - Stamp", "Duration"),
        new("Day - Day (Date)", "Day - Day", "Duration"),
        new("parts Qty (to-many hop)", "parts Qty", "Number"),
        new("part book Qty (to-one hops, control)", "part book Qty", "Number"),
        new("p book Qty (binder hops)", "p book Qty", "Number", Binder: true),
        new("Bogus (unknown name)", "Bogus", "Number", InFunction: "Bogus"),
        new("sum parts Label (#846)", "sum parts Label", "Number"),
        new("sum parts Qty (control)", "sum parts Qty", "Number"),
        new("count parts where Label == 1", "count parts where Label == 1", "Number"),
        new("any parts where Label == 1", "any parts where Label == 1", "Boolean"),
        new("Nope(N) (undeclared function)", "Nope(N)", "Number", InFunction: "Nope(a)"),
        new("match with an untyped arm", "match { Flag -> Bogus, else -> N }", "Number", InFunction: "match { f -> Bogus, else -> a }"),
        new("5 == \"x\" (typeless pair)", "5 == \"x\"", "Boolean", InFunction: "5 == \"x\""),
        new("count parts (function body)", "count parts", "Number", InFunction: "count parts"),
    ];

    internal static readonly string[] TargetTypes = ["Number", "Boolean", "Level", "Uuid", "Duration"];

    /// <summary>The positions a form of type <paramref name="type"/> is placed in, in output order.</summary>
    internal static IEnumerable<string> ResidualPositions(ResidualForm form) {
        foreach (var site in new[] { "assign", "create", "invoke", "call", "return", "form ==", "== form", "match arm", "entry", "exit", "subscription" })
            yield return site;
        if (form.Type == "Boolean")
            foreach (var site in new[] { "if form", "not form", "form and Flag", "for where form", "match { form -> 1 }" })
                yield return site;
        if (form.Type == "Number")
            yield return "form + N";
    }

    /// <summary>The whole document for one cell, or null when the form cannot be written there.</summary>
    internal static string? ResidualDocument(ResidualForm form, string position) {
        var t = form.Type;
        var f = form.Dsl;
        string? tick = null, entry = null, exit = null, subscription = null, function = null;
        switch (position) {
            case "assign": tick = $"assign T_{t} to {f}"; break;
            case "create": tick = $"create Box_{t} {{ W: {f} }}"; break;
            case "invoke": tick = $"invoke Callee_{t}(x: {f})"; break;
            case "call": tick = $"if (F_{t}({f}) == 1) {{ assign N to 2 }}"; break;
            case "form ==": tick = $"if ({f} == T_{t}) {{ assign N to 2 }}"; break;
            case "== form": tick = $"if (T_{t} == {f}) {{ assign N to 2 }}"; break;
            case "match arm": tick = $"assign T_{t} to match {{ Flag -> {f}, else -> T_{t} }}"; break;
            case "if form": tick = $"if ({f}) {{ assign N to 2 }}"; break;
            case "not form": tick = $"if (not ({f})) {{ assign N to 2 }}"; break;
            case "form and Flag": tick = $"if (({f}) and Flag) {{ assign N to 2 }}"; break;
            case "for where form": tick = $"for q in parts where {f} {{ assign N to 2 }}"; break;
            case "match { form -> 1 }": tick = $"assign N to match {{ {f} -> 1, else -> 2 }}"; break;
            case "form + N": tick = $"assign T_Number to ({f}) + N"; break;
            case "entry": entry = $"assign T_{t} to {f}"; break;
            case "exit": exit = $"assign T_{t} to {f}"; break;
            case "subscription": subscription = $"assign T_{t} to {f}"; break;
            case "return":
                if (form.InFunction is null) return null;
                function = $"function R(a: Number, f: Boolean): {t} = {form.InFunction}";
                break;
            default: throw new InvalidOperationException(position);
        }
        if (form.Binder) {
            if (tick is null) return null;
            tick = $"for p in parts {{ {tick} }}";
        }
        return Template(tick, entry, exit, subscription, function);
    }

    /// <summary>
    /// The residual fixture with the form placed by the caller: the action <c>Tick</c> body (and its <c>require</c>), the
    /// first stage's entry, exit and subscription bodies and header <c>require</c>, an entity member, and top-level
    /// functions. Every other slot holds a typed no-op.
    /// </summary>
    private static string Template(
        string? tick, string? entry, string? exit, string? subscription, string? function,
        string? orderMember = null, string? tickRequire = null, string? stageRequire = null) {
        var targets = string.Join("\n", TargetTypes.Select(type => $"  T_{type}: {type} optional"));
        var callees = string.Join("\n", TargetTypes.Select(type => $"  Callee_{type}: action (x: {type}) {{ assign T_{type} to x }}"));
        var boxes = string.Join("\n", TargetTypes.Select(type => $"Box_{type}: entity {{\n  W: {type} optional\n}}"));
        var functions = string.Join("\n", TargetTypes.Select(type => $"function F_{type}(v: {type}): Number = 1"));
        return $$"""
            domain T
            uses temporal
            Level: enum { Low, High }
            Book: entity {
              Qty: Number default(1)
            }
            Part: entity {
              Qty: Number default(1)
              Label: Text default("a")
              book: Book
              Open: stage {
                Finish: action { transition to Done }
              }
              Done: stage { }
            }
            {{boxes}}
            Order: entity {
              N: Number default(1)
              Flag: Boolean default(true)
              Stamp: DateTime
              Day: Date
            {{targets}}
              part: Part
              parts: many Part
              {{orderMember ?? ""}}
              Tick: action{{tickRequire ?? ""}} { {{tick ?? "assign N to 3"}} }
            {{callees}}
              Open: stage{{stageRequire ?? ""}} {
                entry { {{entry ?? "assign N to 4"}} }
                exit { {{exit ?? "assign N to 5"}} }
                when parts Done as o { {{subscription ?? "assign N to 6"}} }
                Go: action { transition to Closed }
              }
              Closed: stage { }
            }
            {{functions}}
            {{function ?? ""}}
            """;
    }


    /// <summary>
    /// C1 (closing round): the DEAD positions the live grid never generates, where a door may accept a form only because
    /// the code never runs: a constant-false branch, a constant-false match arm, a policy no action requires, an action
    /// require, a stage require, a default value, an action never invoked, and a function called only from an uncalled
    /// function.
    /// </summary>
    internal static readonly string[] DeadPositions = [
        "pruned assign", "pruned invoke", "dead match arm", "unused policy", "action require", "stage require",
        "default", "action never invoked", "function called from an uncalled function",
    ];

    /// <summary>The form as a Boolean: itself when it is one, else compared with a property of its type.</summary>
    private static string AsCondition(ResidualForm form) =>
        form.Type == "Boolean" ? form.Dsl : $"({form.Dsl}) == T_{form.Type}";

    /// <summary>The whole document for one dead cell, or null when the form cannot be written there.</summary>
    internal static string? DeadDocument(ResidualForm form, string position) {
        var t = form.Type;
        var f = form.Dsl;
        var policy = $"P: policy {{ {AsCondition(form)} }}";
        var doc = position switch {
            "pruned assign" => Template($"if (1 == 2) {{ {Bind(form, $"assign T_{t} to {f}")} }}", null, null, null, null),
            "pruned invoke" => Template($"if (1 == 2) {{ {Bind(form, $"invoke Callee_{t}(x: {f})")} }}", null, null, null, null),
            "dead match arm" => form.Binder ? null : Template($"assign T_{t} to match {{ 1 == 2 -> {f}, else -> T_{t} }}", null, null, null, null),
            "unused policy" => form.Binder ? null : Template(null, null, null, null, null, orderMember: policy),
            "action require" => form.Binder ? null : Template(null, null, null, null, null, orderMember: policy, tickRequire: " require P"),
            "stage require" => form.Binder ? null : Template(null, null, null, null, null, orderMember: policy, stageRequire: " require P"),
            "default" => form.Binder ? null : Template(null, null, null, null, null, orderMember: $"D: {t} optional default({f})"),
            "action never invoked" => Template(null, null, null, null, null, orderMember: $"Never: action {{ {Bind(form, $"assign T_{t} to {f}")} }}"),
            "function called from an uncalled function" => form.InFunction is null ? null
                : Template(null, null, null, null,
                    $"function R(a: Number, f: Boolean): {t} = {form.InFunction}\nfunction H(a: Number, f: Boolean): {t} = R(a, f)"),
            _ => throw new InvalidOperationException(position),
        };
        return doc;
    }

    private static string Bind(ResidualForm form, string effect) => form.Binder ? $"for p in parts {{ {effect} }}" : effect;

    /// <summary>One line per dead cell: <c>form | position = verdict</c>.</summary>
    internal static string DeadRows() {
        var lines = new List<string> { "residual form | dead position = verdict" };
        foreach (var form in ResidualForms)
            foreach (var position in DeadPositions)
                lines.Add($"{form.Label} | {position} = {(DeadDocument(form, position) is { } dsl ? Verdict(dsl) : "n/a")}");
        return string.Join('\n', lines);
    }

    /// <summary>Every dead cell analysis accepts, through every door with <see cref="ResidualSteps"/>.</summary>
    internal static string DeadDoorRows() {
        var lines = new List<string> { "residual form | dead position = doors" };
        foreach (var form in ResidualForms)
            foreach (var position in DeadPositions) {
                if (DeadDocument(form, position) is not { } dsl) continue;
                var result = GateParity.Check(dsl, ResidualSteps);
                var doors = !result.Analysis
                    ? "analysis rejects"
                    : $"simulate={Door(result.Simulate, result.SimulateSummary)} csharp={Door(result.CSharp, result.CSharpSummary)} polyc={Door(result.PolycCheck, result.PolycSummary)}";
                lines.Add($"{form.Label} | {position} = {doors}");
            }
        return string.Join('\n', lines);
    }

    /// <summary>The scenario every residual cell runs through the doors: create, the action, leave the first stage.</summary>
    internal static readonly SimStep[] ResidualSteps = [
        SimStep.Create("Order", """{"Stamp":"2026-01-02T00:00:00","Day":"2026-01-02"}""", "o"),
        SimStep.Invoke("o", "Tick"),
        SimStep.Invoke("o", "Go"),
    ];

    /// <summary>A verdict: the distinct error codes in code order, or <c>ok</c>.</summary>
    internal static string Verdict(string dsl) {
        var codes = GuideBlocks.Analyze(dsl).Diagnostics
            .Where(d => d.IsError)
            .Select(d => d.Code ?? "(no code)")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return codes.Length == 0 ? "ok" : string.Join("+", codes);
    }

    /// <summary>One line per residual cell: <c>form | position = verdict</c>; <c>n/a</c> where it cannot be written.</summary>
    internal static string ResidualRows() {
        var lines = new List<string> { "residual form | position = verdict" };
        foreach (var form in ResidualForms)
            foreach (var position in ResidualPositions(form))
                lines.Add($"{form.Label} | {position} = {(ResidualDocument(form, position) is { } dsl ? Verdict(dsl) : "n/a")}");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Every residual cell analysis accepts, through every door with <see cref="ResidualSteps"/>: the class evidence
    /// the census needs ON MASTER (a document that passes every door there and fails after the flip is a regression).
    /// On a head that rejects the cell, only analysis runs.
    /// </summary>
    internal static string ResidualDoorRows() {
        var lines = new List<string> { "residual form | position = doors" };
        foreach (var form in ResidualForms)
            foreach (var position in ResidualPositions(form)) {
                if (ResidualDocument(form, position) is not { } dsl) continue;
                var result = GateParity.Check(dsl, ResidualSteps);
                var doors = !result.Analysis
                    ? "analysis rejects"
                    : $"simulate={Door(result.Simulate, result.SimulateSummary)} csharp={Door(result.CSharp, result.CSharpSummary)} polyc={Door(result.PolycCheck, result.PolycSummary)}";
                lines.Add($"{form.Label} | {position} = {doors}");
            }
        return string.Join('\n', lines);
    }

    private static string Door(bool? ok, string? summary) =>
        ok is true ? "ok" : $"REFUSES ({(summary ?? "").Split('\n')[0].Trim()[..Math.Min(90, (summary ?? "").Split('\n')[0].Trim().Length)]})";

    /// <summary>Every grid this slice's verdicts move, by name: the T-04 and T-05a pins plus the residual grid.</summary>
    internal static IReadOnlyList<(string Name, Func<string> Rows)> Grids() => [
        ("FormGrid", AnalyzerClosingRoundPinTests.FormGridRows),
        ("FormGridF3", AnalyzerClosingRoundPinTests.FormGridF3Rows),
        ("DateDifference", AnalyzerClosingRoundPinTests.DateDifferenceRows),
        ("OptionalNull", AnalyzerClosingRoundPinTests.OptionalNullRows),
        ("DateOperationOverTypelessDate", AnalyzerClosingRoundPinTests.DateOperationOverTypelessDateRows),
        ("ImportedContractNameClash", AnalyzerClosingRoundPinTests.ImportedContractNameClashRows),
        ("SumPropertyKinds", AnalyzerClosingRoundPinTests.SumPropertyKindRows),
        ("SumPropertyKindsF3", AnalyzerClosingRoundPinTests.SumPropertyKindF3Rows),
        ("TypelessSources", AnalyzerClosingRoundPinTests.TypelessSourceRows),
        ("TypelessComparisons", AnalyzerClosingRoundPinTests.TypelessComparisonRows),
        ("AllNullMatch", AnalyzerClosingRoundPinTests.AllNullMatchRows),
        ("SameName", AnalyzerClosingRoundPinTests.SameNameEnumAndEntityRows),
        ("NameByNameBare", () => AnalyzerBehaviourPinTests.NameByNameGrid(declared: false)),
        ("NameByNameDeclared", () => AnalyzerBehaviourPinTests.NameByNameGrid(declared: true)),
        ("NameByLiteralBare", () => AnalyzerBehaviourPinTests.NameByLiteralGrid(declared: false)),
        ("NameByLiteralDeclared", () => AnalyzerBehaviourPinTests.NameByLiteralGrid(declared: true)),
        ("Sites", AnalyzerBehaviourPinTests.SiteRows),
        ("InvokeArguments", AnalyzerBehaviourPinTests.InvokeArgumentRows),
        ("TemporalWithoutMap", AnalyzerBehaviourPinTests.TemporalWithoutMapRows),
        ("ArithmeticDateOperand", AnalyzerBehaviourPinTests.ArithmeticDateOperandRows),
        ("Match", AnalyzerBehaviourPinTests.MatchGrid),
        ("Residual", ResidualRows),
        ("Dead", DeadRows),
    ];
}