using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

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

    [Test]
    public async Task NameByNameAssign_BareNames_IsFrozen() =>
        await Assert.That(NameByNameGrid(declared: false)).IsEqualTo(Expected.NameByNameBare);

    [Test]
    public async Task NameByNameAssign_DeclaredPrimitives_IsFrozen() =>
        await Assert.That(NameByNameGrid(declared: true)).IsEqualTo(Expected.NameByNameDeclared);

    [Test]
    public async Task NameByLiteralAssign_BareNames_IsFrozen() =>
        await Assert.That(NameByLiteralGrid(declared: false)).IsEqualTo(Expected.NameByLiteralBare);

    [Test]
    public async Task NameByLiteralAssign_DeclaredPrimitives_IsFrozen() =>
        await Assert.That(NameByLiteralGrid(declared: true)).IsEqualTo(Expected.NameByLiteralDeclared);

    [Test]
    public async Task EachTypeBuildingSite_Discriminators_AreFrozen() =>
        await Assert.That(SiteRows()).IsEqualTo(Expected.Sites);

    [Test]
    public async Task InvokeArgumentNameMismatches_AreFrozen() =>
        await Assert.That(InvokeArgumentRows()).IsEqualTo(Expected.InvokeArguments);

    [Test]
    public async Task TemporalPrimitivesWithoutTheTemporalMap_AreFrozen() =>
        await Assert.That(TemporalWithoutMapRows()).IsEqualTo(Expected.TemporalWithoutMap);

    [Test]
    public async Task ArithmeticAsDateOperand_AndAssignedArithmetic_AreFrozen() =>
        // T-05a: master plus the FlipsA rows of grid "ArithmeticDateOperand" (arithmetic is typed; A-assign).
        await Assert.That(AnalyzerClosingRoundPinTests.GridDifferences(ArithmeticDateOperandRows(),
            AnalyzerClosingRoundPinTests.MasterPlusFlips("ArithmeticDateOperand", Expected.ArithmeticDateOperand))).IsEqualTo("");

    [Test]
    public async Task MatchArmUnification_IsFrozen() =>
        await Assert.That(MatchGrid()).IsEqualTo(Expected.Match);

    [Test]
    public async Task TheDiscriminators_RejectDecimalIntoNumber_AcceptLiteralFiveIntoCents_RejectInt32IntoNumber() {
        // Declared primitives: Number, Decimal and Cents are declared; Int32 is the one undeclared allowlist name.
        await Assert.That(SiteVerdict(true, "assign", "Number", "Decimal", null)).IsEqualTo("DMSEM003");
        await Assert.That(SiteVerdict(true, "assign", "Cents", null, 5L)).IsEqualTo("ok");
        await Assert.That(SiteVerdict(true, "assign", "Number", "Int32", null)).IsEqualTo("DMSEM003+DMSEM005");
    }

    // ── recorded from master (seeded by running the harness on master; rows left visible for review) ──

    private static class Expected {
        public const string NameByNameBare = """
assign P:<row> := Q:<col>, declared primitives False; cols: Text String Number Int Int64 Int32 Decimal Float Double Boolean Bool Uuid Guid Color Widget Money Cents Bin long int float string bool decimal double Nope
Text     55SSSSSSSSSSS55WS555555555
String   55SSSSSSSSSSS55WS555555555
Number   SS555SSSSSSSSS5WS555555555
Int      SS555SSSSSSSSS5WS555555555
Int64    SS555SSSSSSSSS5WS555555555
Int32    SSSSS5SSSSSSSS5WS555555555
Decimal  SS555S5SSSSSSS5WS555555555
Float    SSSSSSS5SSSSSS5WS555555555
Double   SSSSSSSS5SSSSS5WS555555555
Boolean  SSSSSSSSS55SSS5WS555555555
Bool     SSSSSSSSS55SSS5WS555555555
Uuid     SSSSSSSSSSS55S5WS555555555
Guid     SSSSSSSSSSS55S5WS555555555
Color    55SSSSSSSSSSS..VR.55555555
Widget   5555555555555..V..55555555
Money    WWWWWWWWWWWWWVVVVVWWWWWWWW
Cents    SSSSSSSSSSSSSR.V..55555555
Bin      5555555555555..V..55555555
long     555555555555555W5555555555
int      555555555555555W5555555555
float    555555555555555W5555555555
string   555555555555555W5555555555
bool     555555555555555W5555555555
decimal  555555555555555W5555555555
double   555555555555555W5555555555
Nope     555555555555555W5555555555
""";

        public const string NameByNameDeclared = """
assign P:<row> := Q:<col>, declared primitives True; cols: Text String Number Int Int64 Int32 Decimal Float Double Boolean Bool Uuid Guid Color Widget Money Cents Bin long int float string bool decimal double Nope
Text     .5RSSSRSSRSRS..VR.55555555
String   55SSSSSSSSSSS55WS555555555
Number   RS.55SRSSRSRSR.VR.55555555
Int      SS555SSSSSSSSS5WS555555555
Int64    SS555SSSSSSSSS5WS555555555
Int32    SSSSS5SSSSSSSS5WS555555555
Decimal  RS.55S.SSRSRSR.VR.55555555
Float    SSSSSSS5SSSSSS5WS555555555
Double   SSSSSSSS5SSSSS5WS555555555
Boolean  RSRSSSRSS.5RSR.VR.55555555
Bool     SSSSSSSSS55SSS5WS555555555
Uuid     RSRSSSRSSRS.5R.VR.55555555
Guid     SSSSSSSSSSS55S5WS555555555
Color    .5RSSSRSSRSRS..VR.55555555
Widget   .5.555.55.5.5..V..55555555
Money    VWVWWWVWWVWVWVVVVVWWWWWWWW
Cents    RSRSSSRSSRSRSR.V..55555555
Bin      .5.555.55.5.5..V..55555555
long     555555555555555W5555555555
int      555555555555555W5555555555
float    555555555555555W5555555555
string   555555555555555W5555555555
bool     555555555555555W5555555555
decimal  555555555555555W5555555555
double   555555555555555W5555555555
Nope     555555555555555W5555555555
""";

        public const string NameByLiteralBare = """
assign P:<row> := <literal>, declared primitives False; cols: 5 1.5 "x" true null
Text     SS5S5
String   SS5S5
Number   5SSSS
Int      5SSSS
Int64    5SSSS
Int32    5SSSS
Decimal  55SSS
Float    5SSSS
Double   5SSSS
Boolean  SSS5S
Bool     SSS5S
Uuid     SSSS5
Guid     SSSS5
Color    RRRRR
Widget   .....
Money    VVVVV
Cents    .RRRR
Bin      .....
long     55555
int      55555
float    55555
string   55555
bool     55555
decimal  55555
double   55555
Nope     55555
""";

        public const string NameByLiteralDeclared = """
assign P:<row> := <literal>, declared primitives True; cols: 5 1.5 "x" true null
Text     RR.R.
String   SS5S5
Number   .RRRR
Int      5SSSS
Int64    5SSSS
Int32    5SSSS
Decimal  ..RRR
Float    5SSSS
Double   5SSSS
Boolean  RRR.R
Bool     SSS5S
Uuid     RRRR.
Guid     SSSS5
Color    RRRRR
Widget   .....
Money    VVVVV
Cents    .RRRR
Bin      .....
long     55555
int      55555
float    55555
string   55555
bool     55555
decimal  55555
double   55555
Nope     55555
""";

        public const string Sites = """
site | case = verdict
declared=False | assign | control Number<-Number = DMSEM005
declared=False | assign | Decimal into Number = DMSEM003+DMSEM005
declared=False | assign | Int32 into Number = DMSEM003+DMSEM005
declared=False | assign | Number into Decimal = DMSEM005
declared=False | assign | Text into Number = DMSEM003+DMSEM005
declared=False | assign | literal 5 into Cents = ok
declared=False | assign | literal 5 into Int32 = DMSEM005
declared=False | assign | literal 1.5 into Number = DMSEM003+DMSEM005
declared=False | assign | literal 1.5 into Float = DMSEM003+DMSEM005
declared=False | create | control Number<-Number = DMSEM005
declared=False | create | Decimal into Number = DMSEM003+DMSEM005
declared=False | create | Int32 into Number = DMSEM003+DMSEM005
declared=False | create | Number into Decimal = DMSEM005
declared=False | create | Text into Number = DMSEM003+DMSEM005
declared=False | create | literal 5 into Cents = ok
declared=False | create | literal 5 into Int32 = DMSEM005
declared=False | create | literal 1.5 into Number = DMSEM003+DMSEM005
declared=False | create | literal 1.5 into Float = DMSEM003+DMSEM005
declared=False | invoke | control Number<-Number = DMSEM005
declared=False | invoke | Decimal into Number = DMSEM003+DMSEM005
declared=False | invoke | Int32 into Number = DMSEM003+DMSEM005
declared=False | invoke | Number into Decimal = DMSEM005
declared=False | invoke | Text into Number = DMSEM003+DMSEM005
declared=False | invoke | literal 5 into Cents = ok
declared=False | invoke | literal 5 into Int32 = DMSEM005
declared=False | invoke | literal 1.5 into Number = DMSEM003+DMSEM005
declared=False | invoke | literal 1.5 into Float = DMSEM003+DMSEM005
declared=False | call | control Number<-Number = DMSEM005
declared=False | call | Decimal into Number = DMFUN003+DMSEM005
declared=False | call | Int32 into Number = DMFUN003+DMSEM005
declared=False | call | Number into Decimal = DMSEM005
declared=False | call | Text into Number = DMFUN003+DMSEM005
declared=False | call | literal 5 into Cents = ok
declared=False | call | literal 5 into Int32 = DMSEM005
declared=False | call | literal 1.5 into Number = DMFUN003+DMSEM005
declared=False | call | literal 1.5 into Float = DMFUN003+DMSEM005
declared=False | return | control Number<-Number = DMFUN005+DMSEM005
declared=False | return | Decimal into Number = DMFUN005+DMFUN006+DMSEM005
declared=False | return | Int32 into Number = DMFUN005+DMFUN006+DMSEM005
declared=False | return | Number into Decimal = DMFUN005+DMSEM005
declared=False | return | Text into Number = DMFUN005+DMFUN006+DMSEM005
declared=False | return | literal 5 into Cents = ok
declared=False | return | literal 5 into Int32 = ok
declared=False | return | literal 1.5 into Number = DMFUN006
declared=False | return | literal 1.5 into Float = DMFUN006
declared=False | default | control Number<-Number = n/a
declared=False | default | Decimal into Number = n/a
declared=False | default | Int32 into Number = n/a
declared=False | default | Number into Decimal = n/a
declared=False | default | Text into Number = n/a
declared=False | default | literal 5 into Cents = ok
declared=False | default | literal 5 into Int32 = DMSEM005
declared=False | default | literal 1.5 into Number = DMSEM003+DMSEM005
declared=False | default | literal 1.5 into Float = DMSEM003+DMSEM005
declared=False | observed flow | control Number<-Number = DMSEM005
declared=False | observed flow | Decimal into Number = DMSEM003+DMSEM005
declared=False | observed flow | Int32 into Number = DMSEM003+DMSEM005
declared=False | observed flow | Number into Decimal = DMSEM005
declared=False | observed flow | Text into Number = DMSEM003+DMSEM005
declared=False | observed flow | literal 5 into Cents = n/a
declared=False | observed flow | literal 5 into Int32 = n/a
declared=False | observed flow | literal 1.5 into Number = n/a
declared=False | observed flow | literal 1.5 into Float = n/a
declared=True | assign | control Number<-Number = ok
declared=True | assign | Decimal into Number = DMSEM003
declared=True | assign | Int32 into Number = DMSEM003+DMSEM005
declared=True | assign | Number into Decimal = ok
declared=True | assign | Text into Number = DMSEM003
declared=True | assign | literal 5 into Cents = ok
declared=True | assign | literal 5 into Int32 = DMSEM005
declared=True | assign | literal 1.5 into Number = DMSEM003
declared=True | assign | literal 1.5 into Float = DMSEM003+DMSEM005
declared=True | create | control Number<-Number = ok
declared=True | create | Decimal into Number = DMSEM003
declared=True | create | Int32 into Number = DMSEM003+DMSEM005
declared=True | create | Number into Decimal = ok
declared=True | create | Text into Number = DMSEM003
declared=True | create | literal 5 into Cents = ok
declared=True | create | literal 5 into Int32 = DMSEM005
declared=True | create | literal 1.5 into Number = DMSEM003
declared=True | create | literal 1.5 into Float = DMSEM003+DMSEM005
declared=True | invoke | control Number<-Number = ok
declared=True | invoke | Decimal into Number = DMSEM003
declared=True | invoke | Int32 into Number = DMSEM003+DMSEM005
declared=True | invoke | Number into Decimal = ok
declared=True | invoke | Text into Number = DMSEM003
declared=True | invoke | literal 5 into Cents = ok
declared=True | invoke | literal 5 into Int32 = DMSEM005
declared=True | invoke | literal 1.5 into Number = DMSEM003
declared=True | invoke | literal 1.5 into Float = DMSEM003+DMSEM005
declared=True | call | control Number<-Number = ok
declared=True | call | Decimal into Number = DMFUN003
declared=True | call | Int32 into Number = DMFUN003+DMSEM005
declared=True | call | Number into Decimal = ok
declared=True | call | Text into Number = DMFUN003
declared=True | call | literal 5 into Cents = ok
declared=True | call | literal 5 into Int32 = DMSEM005
declared=True | call | literal 1.5 into Number = DMFUN003
declared=True | call | literal 1.5 into Float = DMFUN003+DMSEM005
declared=True | return | control Number<-Number = DMFUN005
declared=True | return | Decimal into Number = DMFUN005+DMFUN006
declared=True | return | Int32 into Number = DMFUN005+DMFUN006+DMSEM005
declared=True | return | Number into Decimal = DMFUN005
declared=True | return | Text into Number = DMFUN005+DMFUN006
declared=True | return | literal 5 into Cents = ok
declared=True | return | literal 5 into Int32 = ok
declared=True | return | literal 1.5 into Number = DMFUN006
declared=True | return | literal 1.5 into Float = DMFUN006
declared=True | default | control Number<-Number = n/a
declared=True | default | Decimal into Number = n/a
declared=True | default | Int32 into Number = n/a
declared=True | default | Number into Decimal = n/a
declared=True | default | Text into Number = n/a
declared=True | default | literal 5 into Cents = ok
declared=True | default | literal 5 into Int32 = DMSEM005
declared=True | default | literal 1.5 into Number = DMSEM003
declared=True | default | literal 1.5 into Float = DMSEM003+DMSEM005
declared=True | observed flow | control Number<-Number = ok
declared=True | observed flow | Decimal into Number = DMSEM003
declared=True | observed flow | Int32 into Number = DMSEM003+DMSEM005
declared=True | observed flow | Number into Decimal = ok
declared=True | observed flow | Text into Number = DMSEM003
declared=True | observed flow | literal 5 into Cents = n/a
declared=True | observed flow | literal 5 into Int32 = n/a
declared=True | observed flow | literal 1.5 into Number = n/a
declared=True | observed flow | literal 1.5 into Float = n/a
""";

        public const string InvokeArguments = """
invoke arg type | param type = verdict
Widget | Number = DMSEM003+DMSEM005
Widget | Widget = ok
Widget | Gadget = DMSEM003
Money | Number = DMSEM003+DMSEM005+DMSTR006
Money | Money = DMSTR006
Money | Gadget = DMSEM003+DMSTR006
Bin | Number = DMSEM003+DMSEM005
Bin | Bin = ok
Bin | Gadget = DMSEM003
Nope | Number = DMSEM003+DMSEM005
Nope | Nope = DMSEM005
Nope | Gadget = DMSEM003+DMSEM005
long | Number = DMSEM003+DMSEM005
long | long = DMSEM005
long | Gadget = DMSEM003+DMSEM005
""";

        public const string TemporalWithoutMap = """
hand-built Date/DateTime/Time/Duration, no temporal extension; session variant | case = verdict
map=False | assign Date := 5 = DMSEM003
map=False | assign Date := 1.5 = DMSEM003
map=False | assign Date := "x" = DMSEM003
map=False | assign Date := true = DMSEM003
map=False | assign Date := Date property = ok
map=False | assign Date := DateTime property = DMSEM003
map=False | assign Date := Time property = DMSEM003
map=False | assign Date := Duration property = DMSEM003
map=False | assign DateTime := 5 = DMSEM003
map=False | assign DateTime := 1.5 = DMSEM003
map=False | assign DateTime := "x" = DMSEM003
map=False | assign DateTime := true = DMSEM003
map=False | assign DateTime := Date property = ok
map=False | assign DateTime := DateTime property = ok
map=False | assign DateTime := Time property = DMSEM003
map=False | assign DateTime := Duration property = DMSEM003
map=False | assign Time := 5 = DMSEM003
map=False | assign Time := 1.5 = DMSEM003
map=False | assign Time := "x" = DMSEM003
map=False | assign Time := true = DMSEM003
map=False | assign Time := Date property = DMSEM003
map=False | assign Time := DateTime property = DMSEM003
map=False | assign Time := Time property = ok
map=False | assign Time := Duration property = DMSEM003
map=False | assign Duration := 5 = DMSEM003
map=False | assign Duration := 1.5 = DMSEM003
map=False | assign Duration := "x" = DMSEM003
map=False | assign Duration := true = DMSEM003
map=False | assign Duration := Date property = DMSEM003
map=False | assign Duration := DateTime property = DMSEM003
map=False | assign Duration := Time property = DMSEM003
map=False | assign Duration := Duration property = ok
map=True | assign Date := 5 = DMSEM003
map=True | assign Date := 1.5 = DMSEM003
map=True | assign Date := "x" = DMSEM003
map=True | assign Date := true = DMSEM003
map=True | assign Date := Date property = ok
map=True | assign Date := DateTime property = DMSEM003
map=True | assign Date := Time property = DMSEM003
map=True | assign Date := Duration property = DMSEM003
map=True | assign DateTime := 5 = DMSEM003
map=True | assign DateTime := 1.5 = DMSEM003
map=True | assign DateTime := "x" = DMSEM003
map=True | assign DateTime := true = DMSEM003
map=True | assign DateTime := Date property = ok
map=True | assign DateTime := DateTime property = ok
map=True | assign DateTime := Time property = DMSEM003
map=True | assign DateTime := Duration property = DMSEM003
map=True | assign Time := 5 = DMSEM003
map=True | assign Time := 1.5 = DMSEM003
map=True | assign Time := "x" = DMSEM003
map=True | assign Time := true = DMSEM003
map=True | assign Time := Date property = DMSEM003
map=True | assign Time := DateTime property = DMSEM003
map=True | assign Time := Time property = ok
map=True | assign Time := Duration property = DMSEM003
map=True | assign Duration := 5 = DMSEM003
map=True | assign Duration := 1.5 = DMSEM003
map=True | assign Duration := "x" = DMSEM003
map=True | assign Duration := true = DMSEM003
map=True | assign Duration := Date property = DMSEM003
map=True | assign Duration := DateTime property = DMSEM003
map=True | assign Duration := Time property = DMSEM003
map=True | assign Duration := Duration property = ok
""";

        public const string ArithmeticDateOperand = """
case = verdict
control Due + 5 Days assign = ok
A + B + 5 Days assign = DMSEM003
(A + B) + 5 Days assign = DMSEM003
(A + B) + 5 Days policy = DMSEM003
A + 5 Days policy = DMSEM003
Due + A policy = ok
Due + (A + B) policy = ok
Due + (C + C) policy = ok
(C + A) + 5 Days assign = DMSEM003
C * 2 + 5 Days policy = DMSEM003
(C + C) + 5 Days assign = DMSEM003
Stamp + A + 5 Days assign = DMSEM003
(A + B) * C assign Number = ok
assigned arithmetic stays unknown = ok
""";

        public const string Match = """
match { true -> P_<row>, else -> P_<col> }; cols: Number Decimal Date DateTime Time Text Color Guid
Number   55NNNNNN
Decimal  55NNNNNN
Date     NN55NNNN
DateTime NN55NNNN
Time     NNNN5NNN
Text     NNNNN55N
Color    NNNNN55N
Guid     NNNNNNN5
""";
    }
}