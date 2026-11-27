using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;
using DmValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-04 characterisation pins: what <c>ExpressionTypeAnalyzer</c> accepts and rejects, recorded from master
/// BEFORE the analyzer moved onto the type model. Every verdict is the full set of error codes the analysis
/// reports for the input (never just "no DMSEM003"), so a pin cannot pass because an unrelated error hid the
/// diagnostic. The expected rows are behaviour as recorded, not an opinion on what should be accepted; a pin
/// that goes red later is a translation error in the move.
/// Legend: <c>.</c> no error at all, <c>R</c> exactly DMSEM003, <c>M</c> exactly DMMAT002, <c>5</c> exactly
/// DMSEM005 (an undeclared type name: the allowlisted names reach the analyzer undeclared, and the reference
/// pass reports them without stopping it), <c>S</c> DMSEM003 + DMSEM005, <c>V</c> DMSTR006 (a value-type property
/// is not storable; the fixture's Money), <c>W</c>/<c>X</c>/<c>Y</c> that with DMSEM005 / DMSEM003 / both, <c>N</c>
/// DMMAT002 + DMSEM005, <c>?</c> anything else (listed in full under the grid).
/// </summary>
public class AnalyzerBehaviourPinTests {
    // ── harness ──────────────────────────────────────────────────────────────

    private static DomainTypeReference Ref(string name) => new(name);

    internal static readonly string[] Names = [
        "Text", "String", "Number", "Int", "Int64", "Int32", "Decimal", "Float", "Double", "Boolean", "Bool",
        "Uuid", "Guid", "Color", "Widget", "Money", "Cents", "Bin",
        "long", "int", "float", "string", "bool", "decimal", "double", "Nope",
    ];

    internal static readonly (string Label, object? Value)[] Literals = [
        ("5", 5L), ("1.5", 1.5m), ("\"x\"", "x"), ("true", true), ("null", null),
    ];

    private static IReadOnlyList<DomainType> BaseTypes(bool declaredPrimitives) {
        List<DomainType> types = [
            new EnumType("Color", ["Red", "Blue"], []),
            new Entity("Widget", [new Property("Id", Ref("Id64"), [])], [], [], []),
            new Entity("Gadget", [new Property("Id", Ref("Id64"), [])], [], [], []),
            new DmValueType("Money", [new Property("Amount", Ref("Id64"), [])], []),
            new DmPrimitive("Cents", PrimitiveSemantics.Decimal, []),
            new DmPrimitive("Bin", PrimitiveSemantics.Binary, []),
            new DmPrimitive("Id64", PrimitiveSemantics.Number, []),
        ];
        if (declaredPrimitives) {
            types.Add(new DmPrimitive("Text", PrimitiveSemantics.Text, []));
            types.Add(new DmPrimitive("Number", PrimitiveSemantics.Number, []));
            types.Add(new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []));
            types.Add(new DmPrimitive("Boolean", PrimitiveSemantics.Boolean, []));
            types.Add(new DmPrimitive("Uuid", PrimitiveSemantics.Uuid, []));
        }
        return types;
    }

    internal static string Verdict(AnalysisResult analysis) {
        var codes = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Code ?? "(no code)")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return codes.Length == 0 ? "ok" : string.Join("+", codes);
    }

    private static char Encode(string verdict) => verdict switch {
        "ok" => '.',
        "DMSEM003" => 'R',
        "DMMAT002" => 'M',
        "DMSEM005" => '5',
        "DMSEM003+DMSEM005" => 'S',
        "DMSTR006" => 'V',
        "DMSEM005+DMSTR006" => 'W',
        "DMSEM003+DMSTR006" => 'X',
        "DMSEM003+DMSEM005+DMSTR006" => 'Y',
        "DMMAT002+DMSEM005" => 'N',
        _ => '?',
    };

    private static Entity Host(
        string name, IReadOnlyList<Property> properties, IReadOnlyList<DmAction>? actions = null,
        IReadOnlyList<Policy>? policies = null) =>
        new(name, properties, actions ?? [], policies ?? [], []);

    private static Domain Dom(
        IReadOnlyList<DomainType> baseTypes, IEnumerable<DomainType> more,
        IReadOnlyList<DomainFunction>? functions = null, bool temporal = false) =>
        new Domain("T", [.. baseTypes, .. more]) {
            Functions = functions ?? [],
            Extensions = temporal ? [ExtensionCatalog.TemporalId] : [],
        };

    private static string Run(Domain domain) => Verdict(DomainModelAnalyzer.Analyze(domain));

    private static string AssignVerdict(
        bool declared, string targetType, string? sourceType, object? literal, bool useLiteral) {
        var value = useLiteral ? DomainExpression.Literal(literal) : DomainExpression.Property("Q");
        List<Property> props = [new("P", Ref(targetType), [])];
        if (!useLiteral) props.Add(new("Q", Ref(sourceType!), []));
        var host = Host("H", props,
            [new DmAction("Set", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("P"), value)], [])]);
        return Run(Dom(BaseTypes(declared), [host]));
    }

    private static string Render(string title, IReadOnlyList<string> rowLabels, IReadOnlyList<string> colLabels,
        Func<string, string, string> cell) {
        var lines = new List<string> { title };
        var other = new List<string>();
        foreach (var r in rowLabels) {
            var chars = new char[colLabels.Count];
            for (var i = 0; i < colLabels.Count; i++) {
                var verdict = cell(r, colLabels[i]);
                chars[i] = Encode(verdict);
                if (chars[i] == '?') other.Add($"{r} > {colLabels[i]} = {verdict}");
            }
            lines.Add($"{r,-8} {new string(chars)}");
        }
        lines.AddRange(other);
        return string.Join('\n', lines);
    }

    // ── (a) name x name assign matrix, property to property ──────────────────

    internal static string NameByNameGrid(bool declared) =>
        Render($"assign P:<row> := Q:<col>, declared primitives {declared}; cols: {string.Join(' ', Names)}",
            Names, Names, (target, source) => AssignVerdict(declared, target, source, null, useLiteral: false));

    // ── (b) name x literal matrix ────────────────────────────────────────────

    internal static string NameByLiteralGrid(bool declared) =>
        Render($"assign P:<row> := <literal>, declared primitives {declared}; cols: {string.Join(' ', Literals.Select(l => l.Label))}",
            Names, [.. Literals.Select(l => l.Label)],
            (target, literalLabel) => AssignVerdict(declared, target, null,
                Literals.First(l => l.Label == literalLabel).Value, useLiteral: true));

    // ── (c) one smoke row per site that builds its own type information ──────

    internal static readonly (string Case, string Target, string? Source, object? Literal)[] SiteCases = [
        ("control Number<-Number", "Number", "Number", null),
        ("Decimal into Number", "Number", "Decimal", null),
        ("Int32 into Number", "Number", "Int32", null),
        ("Number into Decimal", "Decimal", "Number", null),
        ("Text into Number", "Number", "Text", null),
        ("literal 5 into Cents", "Cents", null, 5L),
        ("literal 5 into Int32", "Int32", null, 5L),
        ("literal 1.5 into Number", "Number", null, 1.5m),
        ("literal 1.5 into Float", "Float", null, 1.5m),
    ];

    private static string SiteVerdict(bool declared, string site, string target, string? source, object? literal) {
        var useLiteral = source is null;
        DomainExpression Value() => useLiteral ? DomainExpression.Literal(literal) : DomainExpression.Property("Q");
        var qProps = useLiteral ? new List<Property>() : [new Property("Q", Ref(source!), [])];
        var types = BaseTypes(declared);
        switch (site) {
            case "assign": {
                    List<Property> props = [new("P", Ref(target), []), .. qProps];
                    return Run(Dom(types, [Host("H", props,
                        [new DmAction("Set", InvocationResult.Void, [],
                            [new AssignEffect(DomainExpression.Property("P"), Value())], [])])]));
                }
            case "create": {
                    var box = Host("Box", [new Property("W", Ref(target), [])]);
                    return Run(Dom(types, [box, Host("H", qProps,
                        [new DmAction("Make", InvocationResult.Void, [],
                            [new CreateEntityInstance(Ref("Box"), [new PropertyBinding("W", Value())])], [])])]));
                }
            case "invoke": {
                    return Run(Dom(types, [Host("H", qProps,
                        [new DmAction("Callee", InvocationResult.Void, [new Property("X", Ref(target), [])], [], []),
                         new DmAction("Caller", InvocationResult.Void, [],
                            [new InvokeActionEffect("Callee", [new PropertyBinding("X", Value())])], [])])]));
                }
            case "call": {
                    var function = new DomainFunction("F", [new Property("p", Ref(target), [])], Ref("Number"),
                        DomainExpression.Literal(1L));
                    var policy = new Policy("Check", DomainExpression.Equal(
                        DomainExpression.Call("F", [Value()]), DomainExpression.Literal(1L)));
                    return Run(Dom(types, [Host("H", qProps, policies: [policy])], [function]));
                }
            case "return": {
                    var body = useLiteral ? Value() : DomainExpression.Property("p");
                    var function = new DomainFunction("F",
                        useLiteral ? [] : [new Property("p", Ref(source!), [])], Ref(target), body);
                    return Run(Dom(types, [Host("H", [])], [function]));
                }
            case "default": {
                    if (!useLiteral) return "n/a";
                    return Run(Dom(types, [Host("H",
                        [new Property("P", Ref(target), [new DefaultValueConstraint(Value())])])]));
                }
            case "observed flow": {
                    if (useLiteral) return "n/a";
                    var src = Host("Src", [new Property("N", Ref(source!), [])]);
                    var dst = Host("Dst", [new Property("N", Ref(target), [])]);
                    var domain = Dom(types, [src, dst]);
                    var session = SessionBuilder.CreateEmpty().LoadCore().LoadTemporal()
                        .WithObservedFlows([new ObservedFlow("Src.N", "Dst.N")]).Build();
                    return Verdict(session.Analyze(domain));
                }
            default: throw new InvalidOperationException(site);
        }
    }

    internal static readonly string[] Sites = ["assign", "create", "invoke", "call", "return", "default", "observed flow"];

    internal static string SiteRows() {
        var lines = new List<string> { "site | case = verdict" };
        foreach (var declared in new[] { false, true })
            foreach (var site in Sites)
                foreach (var c in SiteCases)
                    lines.Add($"declared={declared} | {site} | {c.Case} = {SiteVerdict(declared, site, c.Target, c.Source, c.Literal)}");
        return string.Join('\n', lines);
    }

    // ── (d) invoke-argument name mismatches ──────────────────────────────────

    internal static readonly string[] InvokeArgTypes = ["Widget", "Money", "Bin", "Nope", "long"];

    internal static string InvokeArgumentRows() {
        var lines = new List<string> { "invoke arg type | param type = verdict" };
        foreach (var arg in InvokeArgTypes)
            foreach (var param in new[] { "Number", arg, arg == "Gadget" ? "Widget" : "Gadget" }) {
                var host = Host("H", [new Property("Q", Ref(arg), [])],
                    [new DmAction("Callee", InvocationResult.Void, [new Property("X", Ref(param), [])], [], []),
                     new DmAction("Caller", InvocationResult.Void, [],
                        [new InvokeActionEffect("Callee", [new PropertyBinding("X", DomainExpression.Property("Q"))])], [])]);
                lines.Add($"{arg} | {param} = {Run(Dom(BaseTypes(false), [host]))}");
            }
        return string.Join('\n', lines);
    }

    // ── (e) declared temporal primitives WITHOUT the temporal map ────────────

    internal static string TemporalWithoutMapRows() {
        string[] temporal = ["Date", "DateTime", "Time", "Duration"];
        var lines = new List<string> { "hand-built Date/DateTime/Time/Duration, no temporal extension; session variant | case = verdict" };
        foreach (var withMap in new[] { false, true }) {
            foreach (var t in temporal) {
                foreach (var (label, value) in Literals.Where(l => l.Label != "null")) {
                    var host = Host("H", [new Property("D", Ref(t), [])],
                        [new DmAction("Set", InvocationResult.Void, [],
                            [new AssignEffect(DomainExpression.Property("D"), DomainExpression.Literal(value))], [])]);
                    lines.Add($"map={withMap} | assign {t} := {label} = {Run(TemporalDomain(host, withMap))}");
                }
                foreach (var s in temporal) {
                    var host = Host("H", [new Property("D", Ref(t), []), new Property("S", Ref(s), [])],
                        [new DmAction("Set", InvocationResult.Void, [],
                            [new AssignEffect(DomainExpression.Property("D"), DomainExpression.Property("S"))], [])]);
                    lines.Add($"map={withMap} | assign {t} := {s} property = {Run(TemporalDomain(host, withMap))}");
                }
            }
        }
        return string.Join('\n', lines);
    }

    private static Domain TemporalDomain(Entity host, bool withMap) =>
        new Domain("T", [.. TemporalTypeCatalog.Definitions.Select(static d => new DmPrimitive(d.Name, d.Semantics, [])), host]) {
            Extensions = withMap ? [ExtensionCatalog.TemporalId] : [],
        };

    // ── (f) arithmetic used as a date operand ────────────────────────────────

    private static string DslVerdict(string body) {
        var dsl = $$"""
            domain T
            uses temporal
            Item: entity {
              A: Number
              B: Number
              C: Decimal
              Due: Date
              Stamp: DateTime
              Name: Text
              {{body}}
            }
            """;
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return Verdict(result.Analysis);
    }

    internal static readonly (string Label, string Body)[] ArithmeticDateOperandCases = [
        ("control Due + 5 Days assign", "Tick: action { assign Due to Due + 5 Days }"),
        ("A + B + 5 Days assign", "Tick: action { assign Due to A + B + 5 Days }"),
        ("(A + B) + 5 Days assign", "Tick: action { assign Due to (A + B) + 5 Days }"),
        ("(A + B) + 5 Days policy", "Ok: policy { Due < (A + B) + 5 Days }"),
        ("A + 5 Days policy", "Ok: policy { Due < A + 5 Days }"),
        ("Due + A policy", "Ok: policy { Due < Due + A }"),
        ("Due + (A + B) policy", "Ok: policy { Due < Due + (A + B) }"),
        ("Due + (C + C) policy", "Ok: policy { Due < Due + (C + C) }"),
        ("(C + A) + 5 Days assign", "Tick: action { assign Due to (C + A) + 5 Days }"),
        ("C * 2 + 5 Days policy", "Ok: policy { Due < C * 2 + 5 Days }"),
        ("(C + C) + 5 Days assign", "Tick: action { assign Due to (C + C) + 5 Days }"),
        ("Stamp + A + 5 Days assign", "Tick: action { assign Stamp to Stamp + A + 5 Days }"),
        ("(A + B) * C assign Number", "Tick: action { assign A to (A + B) * C }"),
        ("assigned arithmetic stays unknown", "Tick: action { assign Name to (A + 1) }"),
    ];

    internal static string ArithmeticDateOperandRows() {
        var lines = new List<string> { "case = verdict" };
        foreach (var (label, body) in ArithmeticDateOperandCases)
            lines.Add($"{label} = {DslVerdict(body)}");
        return string.Join('\n', lines);
    }

    // ── (g) match arm unification (DMMAT002) ─────────────────────────────────

    internal static readonly string[] MatchTypes = ["Number", "Decimal", "Date", "DateTime", "Time", "Text", "Color", "Guid"];

    internal static string MatchGrid() {
        var temporalPrimitives = TemporalTypeCatalog.Definitions.Select(static d => (DomainType)new DmPrimitive(d.Name, d.Semantics, []));
        return Render("match { true -> P_<row>, else -> P_<col> }; cols: " + string.Join(' ', MatchTypes),
            MatchTypes, MatchTypes, (a, b) => {
                var host = Host("H", [.. MatchTypes.Select(t => new Property($"P_{t}", Ref(t), []))],
                    policies: [new Policy("M", DomainExpression.Match(
                        [new MatchArm(DomainExpression.Literal(true), DomainExpression.Property($"P_{a}"))],
                        DomainExpression.Property($"P_{b}")))]);
                return Run(Dom(BaseTypes(true), [.. temporalPrimitives, host], temporal: true));
            });
    }

    // ── the pins ─────────────────────────────────────────────────────────────

    [Test]
    public async Task ControlCell_NumberIntoNumber_HasNoErrorAtAll_WhenDeclared_AndOnlyTheReferenceErrorWhenBare() {
        await Assert.That(AssignVerdict(declared: true, "Number", "Number", null, useLiteral: false)).IsEqualTo("ok");
        await Assert.That(AssignVerdict(declared: false, "Number", "Number", null, useLiteral: false)).IsEqualTo("DMSEM005");
    }

    private static string MatchDsl(string members, string body, string extra) => $$"""
        domain Unif
        uses temporal
        UnifMood: enum { Happy, Sad }
        UnifOther: enum { Calm, Angry }
        UnifInv: entity {
          Qty: Number default(3)
          Dec: Decimal default(2.5)
          Opt: Number optional
          OptDec: Decimal optional
          Tag: Text default("t")
          Flag: Boolean default(true)
          Opt2: Number optional
          Mood: UnifMood default(Happy)
          When: Date
          Stamp: DateTime
        {{members}}
          Run: action { {{body}} }
        }
        {{extra}}
        """;

    /// <summary>
    /// T-09a: a <c>match</c> has one type. Decimal with a Number arm or an integer literal unifies to Decimal in either
    /// order (so a Decimal match into a Number is DMSEM003 at the value, whichever arm comes first, an unnamed Decimal arm
    /// included); Date with DateTime, and an enum with a Text property or into a non-enum target, are DMMAT002; an optional
    /// arm is no obstacle; arithmetic over a match that may be absent is DMSEM003 only into a required target.
    /// </summary>
    [Test]
    [Arguments("Decimal then Number into Decimal", "T: Decimal default(0)", "assign T to match { Flag -> 1.5, else -> Qty }", "", "ok")]
    [Arguments("Number then Decimal into Decimal", "T: Decimal default(0)", "assign T to match { Flag -> Qty, else -> 1.5 }", "", "ok")]
    [Arguments("Number then Decimal into Number (#605)", "T: Number default(0)", "assign T to match { Flag -> Qty, else -> 1.5 }", "", "DMSEM003")]
    [Arguments("Decimal then Number into Number", "T: Number default(0)", "assign T to match { Flag -> Dec, else -> Qty }", "", "DMSEM003")]
    [Arguments("a nested Decimal, Number first, into Number", "T: Number default(0)",
        "assign T to match { Flag -> Qty, else -> match { Qty > 1 -> 1.5, else -> 2 } }", "", "DMSEM003")]
    [Arguments("a nested Decimal into Decimal", "T: Decimal default(0)",
        "assign T to match { Flag -> match { Qty > 1 -> 1.5, else -> Qty }, else -> 2 }", "", "ok")]
    [Arguments("a Decimal-typed argument", "T: Decimal default(0)", "assign T to UnifTwice(match { Flag -> Qty, else -> 1.5 })",
        "function UnifTwice(v: Decimal): Decimal = v * 2", "ok")]
    [Arguments("a Number-typed argument, Number first", "T: Number default(0)", "assign T to UnifId(match { Flag -> Qty, else -> 1.5 })",
        "function UnifId(v: Number): Number = v", "DMFUN003")]
    [Arguments("Date with DateTime into DateTime", "T: DateTime optional", "assign T to match { Flag -> When, else -> Stamp }", "", "DMMAT002")]
    [Arguments("DateTime with Date into DateTime", "T: DateTime optional", "assign T to match { Flag -> Stamp, else -> When }", "", "DMMAT002")]
    [Arguments("Date with DateTime into Date", "T: Date optional", "assign T to match { Flag -> When, else -> Stamp }", "", "DMMAT002")]
    [Arguments("Date with DateTime, no target", "T: Number default(0)",
        "if ((match { Flag -> When, else -> Stamp }) == When) { assign T to 1 }", "", "DMMAT002")]
    [Arguments("Date with Date", "T: Date optional", "assign T to match { Flag -> When, else -> When }", "", "ok")]
    [Arguments("an enum member with a Text literal into the enum", "T: UnifMood default(Happy)", "assign T to match { Flag -> Happy, else -> \"Sad\" }", "", "ok")]
    [Arguments("an enum member with a Text property into the enum", "T: UnifMood default(Happy)", "assign T to match { Flag -> Happy, else -> Tag }", "", "DMMAT002")]
    [Arguments("an enum member with a Text literal into Text", "T: Text default(\"\")", "assign T to match { Flag -> Happy, else -> \"x\" }", "", "DMMAT002")]
    [Arguments("an enum member with a Text property into Text", "T: Text default(\"\")", "assign T to match { Flag -> Happy, else -> Tag }", "", "DMMAT002")]
    [Arguments("an enum member with a Text literal, no target", "T: Number default(0)",
        "if ((match { Flag -> Happy, else -> \"x\" }) == Tag) { assign T to 1 }", "", "DMMAT002")]
    [Arguments("two enums", "T: UnifMood default(Happy)", "assign T to match { Flag -> Happy, else -> Calm }", "", "DMMAT002")]
    [Arguments("an optional Number with a Decimal literal", "T: Decimal optional", "assign T to match { Flag -> Opt, else -> 1.5 }", "", "ok")]
    [Arguments("an optional Decimal with a Number", "T: Decimal optional", "assign T to match { Flag -> OptDec, else -> Qty }", "", "ok")]
    [Arguments("a null arm first, an optional Number, a Decimal", "T: Decimal optional",
        "assign T to match { Qty > 9 -> null, Qty > 2 -> Opt, else -> 1.5 }", "", "ok")]
    [Arguments("an optional Number and a Number into an optional Decimal", "T: Decimal optional",
        "assign T to match { Flag -> Opt, else -> Qty }", "", "ok")]
    [Arguments("an optional Number with a Number", "T: Number optional", "assign T to match { Flag -> Opt, else -> Qty }", "", "ok")]
    [Arguments("null, a Number and a Decimal into an optional Decimal", "T: Decimal optional",
        "assign T to match { Qty > 9 -> null, Qty > 2 -> Qty, else -> 1.5 }", "", "ok")]
    [Arguments("arithmetic over a match that may be absent, optional target", "T: Number optional",
        "assign T to (match { Flag -> null, else -> 1 }) + 1", "", "ok")]
    [Arguments("arithmetic over a match that may be absent, required target", "T: Number default(0)",
        "assign T to (match { Flag -> null, else -> 1 }) + 1", "", "DMSEM003")]
    [Arguments("arithmetic over a match with an optional arm, required target", "T: Number default(0)",
        "assign T to (match { Flag -> Opt, else -> 1 }) * 2", "", "DMSEM003")]
    [Arguments("arithmetic over a match with an optional arm, optional target", "T: Number optional",
        "assign T to (match { Flag -> Opt, else -> 1 }) * 2", "", "ok")]
    [Arguments("arithmetic over a match whose arms are all optional", "T: Number optional",
        "assign T to (match { Flag -> Opt, else -> Opt2 }) + 1", "", "ok")]
    [Arguments("subtraction over a match that may be absent, required target", "T: Number default(0)",
        "assign T to 0 - (match { Flag -> null, else -> 1 })", "", "DMSEM003")]
    [Arguments("negation of a match that may be absent, required target", "T: Number default(0)",
        "assign T to -(match { Flag -> null, else -> 1 })", "", "DMSEM003")]
    [Arguments("negation of a match that may be absent, optional target", "T: Number optional",
        "assign T to -(match { Flag -> null, else -> 1 })", "", "ok")]
    [Arguments("an unnamed Decimal arm and a Number into a Number", "T: Number default(0)",
        "assign T to match { Flag -> Qty, else -> Dec * 2 }", "", "DMSEM003")]
    [Arguments("an unnamed Decimal arm first and a Number into a Number", "T: Number default(0)",
        "assign T to match { Flag -> Dec * 2, else -> Qty }", "", "DMSEM003")]
    [Arguments("an unnamed Decimal arm and a Number into a Decimal", "T: Decimal default(0)",
        "assign T to match { Flag -> Qty, else -> Dec * 2 }", "", "ok")]
    [Arguments("an enum member with a Text literal against an enum property", "T: Number default(0)",
        "if (Mood == (match { Flag -> Happy, else -> \"Sad\" })) { assign T to 1 }", "", "ok")]
    [Arguments("an enum member with a Text literal, the match first", "T: Number default(0)",
        "if ((match { Flag -> Happy, else -> \"Sad\" }) != Mood) { assign T to 1 }", "", "ok")]
    [Arguments("an enum member with a Text property against an enum property", "T: Number default(0)",
        "if (Mood == (match { Flag -> Happy, else -> Tag })) { assign T to 1 }", "", "DMMAT002")]
    [Arguments("a nested match with an enum member and a Text literal into the enum", "T: UnifMood default(Happy)",
        "assign T to match { Flag -> match { Flag -> Happy, else -> \"Sad\" }, else -> Happy }", "", "ok")]
    [Arguments("an enum member with a Text literal that names no member, assigned", "T: UnifMood default(Happy)",
        "assign T to match { Flag -> Happy, else -> \"Nope\" }", "", "DMSEM003")]
    [Arguments("an enum member with a Text literal that names no member, compared", "T: Number default(0)",
        "if (Mood == (match { Flag -> Happy, else -> \"Nope\" })) { assign T to 1 }", "", "DMSEM003")]
    [Arguments("an enum member with a Text literal that names a member, compared", "T: Number default(0)",
        "if (Mood == (match { Flag -> Happy, else -> \"Sad\" })) { assign T to 1 }", "", "ok")]
    [Arguments("a parameter that shadows the enum property: lowering still adapts by the property name, analysis gives no slot",
        "T: Number default(0)\n  Cmp: action (Mood: UnifMood) { if (Mood == (match { Flag -> Happy, else -> \"Sad\" })) { assign T to 1 } }",
        "assign T to 1", "", "DMMAT002")]
    [Arguments("an enum member with a Text literal as an invoke argument", "T: UnifMood default(Happy)\n  Set: action (m: UnifMood) { assign T to m }",
        "invoke Set(m: match { Flag -> Happy, else -> \"Sad\" })", "", "DMMAT002")]
    [Arguments("a comparison with a match that may be absent", "T: Number default(0)",
        "if ((match { Flag -> null, else -> Qty }) > 1) { assign T to 1 }", "", "ok")]
    public async Task MatchArmUnification_DslRows_AreTyped(
        string label, string members, string body, string extra, string expected) =>
        await Assert.That(StrictGateCensus.Verdict(MatchDsl("  " + members, body, extra))).IsEqualTo(expected).Because(label);

    /// <summary>
    /// T-09a: each refusal this slice adds is positioned at the <c>match</c> keyword: the diagnostic's node is the match and its
    /// line and column are where the keyword is written (worked out from the document text, not from the analyzer).
    /// </summary>
    [Test]
    [Arguments("DMMAT002", "T: DateTime optional", "assign T to match { Flag -> When, else -> Stamp }")]
    [Arguments("DMSEM003", "T: Number default(0)", "assign T to match { Flag -> Qty, else -> 1.5 }")]
    [Arguments("DMSEM003", "T: Number default(0)", "assign T to (match { Flag -> null, else -> 1 }) + 1")]
    [Arguments("DMSEM003", "T: Number default(0)\n  D2: Number default(match { true -> 1, else -> 2 })", "assign T to 1")]
    public async Task NewMatchRefusals_AreAtTheMatchKeyword(string code, string members, string body) {
        var dsl = MatchDsl("  " + members, body, "");
        var keyword = dsl.IndexOf("match", StringComparison.Ordinal);
        var line = dsl[..keyword].Count(c => c == '\n') + 1;
        var column = keyword - dsl[..keyword].LastIndexOf('\n');
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        var found = result.Analysis.Diagnostics.Where(d => d.Code == code && d.Node is MatchExpr).Select(d => (MatchExpr)d.Node).ToList();
        await Assert.That(found.Select(m => (m.Line, m.Col)).Distinct()).IsEquivalentTo([(line, column)]);
    }

    /// <summary>T-09a fix round 2: a string literal in an enum match that names no member of the enum is the plain form's own error
    /// (<c>CheckEnumMember</c>: DMSEM003 "is not a member of enum"), reported at the match keyword (a DSL string literal carries no
    /// position), in an assign and in a comparison.</summary>
    [Test]
    [Arguments("T: UnifMood default(Happy)", "assign T to match { Flag -> Happy, else -> \"Nope\" }")]
    [Arguments("T: Number default(0)", "if (Mood == (match { Flag -> Happy, else -> \"Nope\" })) { assign T to 1 }")]
    public async Task NonMemberLiteralInAnEnumMatch_IsReportedAtTheLiteral(string members, string body) {
        var dsl = MatchDsl("  " + members, body, "");
        var keyword = dsl.IndexOf("match", StringComparison.Ordinal);
        var line = dsl[..keyword].Count(c => c == '\n') + 1;
        var column = keyword - dsl[..keyword].LastIndexOf('\n');
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        var found = result.Analysis.Diagnostics
            .Where(d => d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility && d.Node is MatchExpr)
            .ToList();
        await Assert.That(found.Select(d => d.Message).Distinct()).IsEquivalentTo(["'Nope' is not a member of enum 'UnifMood'"]);
        await Assert.That(found.Select(d => (((MatchExpr)d.Node).Line, ((MatchExpr)d.Node).Col)).Distinct()).IsEquivalentTo([(line, column)]);
    }

    /// <summary>
    /// T-09a fix round 2 (API only): an unnamed Decimal arm (<c>Dec * 2</c>) merges to an unnamed Decimal, so a match of such arms is
    /// accepted into an API-declared Decimal alias (<c>Cents</c>), as it was on the base, and still refused into a Number. An alias
    /// cannot run on any host on the base (the simulator: "Type with name 'Cents' not found"; the C#: CS0246), so this is analysis only.
    /// </summary>
    [Test]
    [Arguments("Cents", "Dec * 2", "Dec + 1", "ok")]
    [Arguments("Cents", "Dec * 2", "0", "ok")]
    [Arguments("Decimal", "Dec * 2", "Dec * 3", "ok")]
    [Arguments("Number", "Dec * 2", "Dec * 3", "R")]
    [Arguments("Number", "Qty", "Dec * 2", "R")]
    public async Task UnnamedDecimalArms_IntoADeclaredDecimalAlias_AreAccepted_AndIntoANumberRefused(string target, string first, string second, string expected) {
        static DomainExpression Arm(string text) => text switch {
            "Dec * 2" => DomainExpression.Multiply(DomainExpression.Property("Dec"), DomainExpression.Literal(2L)),
            "Dec * 3" => DomainExpression.Multiply(DomainExpression.Property("Dec"), DomainExpression.Literal(3L)),
            "Dec + 1" => DomainExpression.Add(DomainExpression.Property("Dec"), DomainExpression.Literal(1L)),
            "Qty" => DomainExpression.Property("Qty"),
            _ => DomainExpression.Literal(long.Parse(text, System.Globalization.CultureInfo.InvariantCulture)),
        };
        var host = Host("H",
            [new Property("P", Ref(target), []), new Property("Dec", Ref("Decimal"), []), new Property("Qty", Ref("Number"), []), new Property("Flag", Ref("Boolean"), [])],
            [new DmAction("Go", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("P"),
                    DomainExpression.Match([new MatchArm(DomainExpression.Property("Flag"), Arm(first))], Arm(second)))], [])]);
        var verdict = Run(Dom(BaseTypes(true), [host]));
        await Assert.That(Encode(verdict).ToString()).IsEqualTo(expected == "ok" ? "." : expected);
    }

    [Test]
    public async Task TheDiscriminators_RejectDecimalIntoNumber_AcceptLiteralFiveIntoCents_RejectInt32IntoNumber() {
        // Declared primitives: Number, Decimal and Cents are declared; Int32 is the one undeclared allowlist name.
        await Assert.That(SiteVerdict(true, "assign", "Number", "Decimal", null)).IsEqualTo("DMSEM003");
        await Assert.That(SiteVerdict(true, "assign", "Cents", null, 5L)).IsEqualTo("ok");
        await Assert.That(SiteVerdict(true, "assign", "Number", "Int32", null)).IsEqualTo("DMSEM003+DMSEM005");
    }

}