using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Effects;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-04 closing-round pins, in addition to <see cref="AnalyzerBehaviourPinTests"/> (which is untouched since
/// commit 1). Every expected row is what MASTER's analyzer did, recorded by running this class there; the class
/// is self-contained so it runs unchanged on master. Each verdict is the full set of error codes.
/// Notes: a comparison of two typeless operands of different families is accepted because two null names
/// compare equal (a load-bearing quirk for T-05). A match whose arms are all <c>null</c> is typed Text; T-06
/// changes that case deliberately, so its rows are expected to move then.
/// </summary>
public partial class AnalyzerClosingRoundPinTests {
    private static DomainTypeReference Ref(string name) => new(name);

    private static string Verdict(AnalysisResult analysis) {
        var codes = analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Code ?? "(no code)")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return codes.Length == 0 ? "ok" : string.Join("+", codes);
    }

    private static List<DomainType> Declared() => [
        new DmPrimitive("Number", PrimitiveSemantics.Number, []),
        new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
        new DmPrimitive("Text", PrimitiveSemantics.Text, []),
        new DmPrimitive("Boolean", PrimitiveSemantics.Boolean, []),
        new DmPrimitive("Uuid", PrimitiveSemantics.Uuid, []),
        new DmPrimitive("Cents", PrimitiveSemantics.Decimal, []),
        new DmPrimitive("Label", PrimitiveSemantics.Text, []),
        new Entity("Line", [new Property("Qty", Ref("Number"), [])], [], [], []),
    ];

    private static Entity Order(
        IReadOnlyList<Property> extra, IReadOnlyList<DmAction>? actions = null, IReadOnlyList<Policy>? policies = null) =>
        new("Order",
            [new Property("N", Ref("Number"), []), new Property("M", Ref("Number"), []),
             new Property("Name", Ref("Text"), []), new Property("Other", Ref("Text"), []), .. extra],
            actions ?? [], policies ?? [], []);

    private static Domain Dom(Entity order, IReadOnlyList<DomainFunction>? functions = null) {
        var domain = DomainTestFactory.Create("T", [.. Declared(), order],
            [new Relationship("Lines", Ref("Order"), Ref("Line"), RelationshipCardinality.OneToMany, [])]);
        return domain with { Functions = functions ?? [] };
    }

    private static string Run(Domain domain) => Verdict(DomainModelAnalyzer.Analyze(domain));

    private static DomainExpression P(string name) => DomainExpression.Property(name);

    private static string AssignVerdict(string targetType, DomainExpression value) {
        var order = Order([new Property("P", Ref(targetType), [])],
            [new DmAction("Set", InvocationResult.Void, [], [new AssignEffect(P("P"), value)], [])]);
        return Run(Dom(order));
    }

    private static string PolicyVerdict(DomainExpression expression) =>
        Run(Dom(Order([], policies: [new Policy("Check", expression)])));

    private static readonly (string Label, Func<DomainExpression> Source, string Same, string[] Targets)[] TypelessSources = [
        ("Count", () => DomainExpression.Count("Lines", null), "Cents", ["Cents", "Int32", "Number", "Boolean", "Text"]),
        ("Sum", () => DomainExpression.Sum("Lines", "Qty"), "Cents", ["Cents", "Int32", "Number", "Boolean", "Text"]),
        ("N + M", () => DomainExpression.Add(P("N"), P("M")), "Cents", ["Cents", "Int32", "Number", "Boolean", "Text"]),
        ("Name + Other", () => DomainExpression.Add(P("Name"), P("Other")), "Label", ["Label", "String", "Text", "Number", "Boolean"]),
    ];

    internal static string TypelessSourceRows() {
        var lines = new List<string> { "typeless source | target = verdict" };
        foreach (var (label, source, _, targets) in TypelessSources)
            foreach (var target in targets)
                lines.Add($"{label} | {target} = {AssignVerdict(target, source())}");
        foreach (var (returnType, parameterType, body) in new[] {
            ("Cents", "Number", (Func<DomainExpression>)(() => DomainExpression.Add(P("a"), P("b")))),
            ("Number", "Number", () => DomainExpression.Add(P("a"), P("b"))),
            ("Text", "Number", () => DomainExpression.Add(P("a"), P("b"))),
            ("Label", "Text", () => DomainExpression.Add(P("a"), P("b"))),
            ("Number", "Text", () => DomainExpression.Add(P("a"), P("b"))),
        }) {
            var function = new DomainFunction("F",
                [new Property("a", Ref(parameterType), []), new Property("b", Ref(parameterType), [])],
                Ref(returnType), body());
            lines.Add($"return F(a, b: {parameterType}): {returnType} = a + b = {Run(Dom(Order([]), [function]))}");
        }
        return string.Join('\n', lines);
    }

    internal static string TypelessComparisonRows() {
        (string Label, Func<DomainExpression> Build)[] cases = [
            ("5 == \"x\"", () => DomainExpression.Equal(DomainExpression.Literal(5L), DomainExpression.Literal("x"))),
            ("true == 5", () => DomainExpression.Equal(DomainExpression.Literal(true), DomainExpression.Literal(5L))),
            ("5 < \"x\"", () => DomainExpression.LessThan(DomainExpression.Literal(5L), DomainExpression.Literal("x"))),
            ("Count == \"x\"", () => DomainExpression.Equal(DomainExpression.Count("Lines", null), DomainExpression.Literal("x"))),
            ("Count == true", () => DomainExpression.Equal(DomainExpression.Count("Lines", null), DomainExpression.Literal(true))),
            ("Count == 5", () => DomainExpression.Equal(DomainExpression.Count("Lines", null), DomainExpression.Literal(5L))),
            ("Name == 5", () => DomainExpression.Equal(P("Name"), DomainExpression.Literal(5L))),
        ];
        var lines = new List<string> { "comparison = verdict" };
        foreach (var (label, build) in cases)
            lines.Add($"{label} = {PolicyVerdict(build())}");
        return string.Join('\n', lines);
    }

    internal static string AllNullMatchRows() {
        DomainExpression Match(DomainExpression a, DomainExpression b) =>
            DomainExpression.Match([new MatchArm(DomainExpression.Literal(true), a)], b);
        var nul = DomainExpression.Literal(null);
        (string Label, string Target, Func<DomainExpression> Build)[] cases = [
            ("match { null, null }", "Uuid", () => Match(nul, nul)),
            ("match { null, null }", "Guid", () => Match(nul, nul)),
            ("match { null, null }", "Text", () => Match(nul, nul)),
            ("match { null, null }", "Number", () => Match(nul, nul)),
            ("match { null, \"x\" }", "Text", () => Match(nul, DomainExpression.Literal("x"))),
            ("match { null, \"x\" }", "Uuid", () => Match(nul, DomainExpression.Literal("x"))),
            ("match { null, 5 }", "Number", () => Match(nul, DomainExpression.Literal(5L))),
            ("match { null, 5 }", "Text", () => Match(nul, DomainExpression.Literal(5L))),
        ];
        var lines = new List<string> { "assign P := match | target = verdict" };
        foreach (var (label, target, build) in cases)
            lines.Add($"{label} | {target} = {AssignVerdict(target, build())}");
        return string.Join('\n', lines);
    }

    internal static string SameNameEnumAndEntityRows() {
        EnumType color = new("Color", ["Red"], []);
        Entity colorEntity = new("Color", [new Property("Id", Ref("Number"), [])], [], [], []);
        var lines = new List<string> { "declaration order | assign P:Color := value = verdict" };
        foreach (var entityFirst in new[] { true, false })
            foreach (var value in new[] { "Bogus", "Red" }) {
                var host = new Entity("Host", [new Property("P", Ref("Color"), [])],
                    [new DmAction("Set", InvocationResult.Void, [],
                        [new AssignEffect(P("P"), DomainExpression.Literal(value))], [])], [], []);
                List<DomainType> types = [new DmPrimitive("Number", PrimitiveSemantics.Number, []),
                    .. entityFirst ? new DomainType[] { colorEntity, color } : [color, colorEntity], host];
                lines.Add($"{(entityFirst ? "entity then enum" : "enum then entity")} | \"{value}\" = {Run(new Domain("T", types))}");
            }
        return string.Join('\n', lines);
    }

    [Test]
    public async Task TypelessSourcesIntoNamedTargets_AndFunctionReturns_AreFrozen() =>
        await Assert.That(TypelessSourceRows()).IsEqualTo(Expected.TypelessSources);

    [Test]
    public async Task TypelessComparisonsOfDifferentFamilies_AreFrozen() =>
        await Assert.That(TypelessComparisonRows()).IsEqualTo(Expected.TypelessComparisons);

    [Test]
    public async Task AllNullAndPartlyNullMatch_AreFrozen() =>
        await Assert.That(AllNullMatchRows()).IsEqualTo(Expected.AllNullMatch);

    [Test]
    public async Task EnumAndEntityOfTheSameName_AreFrozenInBothDeclarationOrders() =>
        await Assert.That(SameNameEnumAndEntityRows()).IsEqualTo(Expected.SameName);

    private static class Expected {
        public const string TypelessSources = """
typeless source | target = verdict
Count | Cents = ok
Count | Int32 = DMSEM005
Count | Number = ok
Count | Boolean = DMSEM003
Count | Text = DMSEM003
Sum | Cents = ok
Sum | Int32 = DMSEM005
Sum | Number = ok
Sum | Boolean = DMSEM003
Sum | Text = DMSEM003
N + M | Cents = ok
N + M | Int32 = DMSEM005
N + M | Number = ok
N + M | Boolean = ok
N + M | Text = ok
Name + Other | Label = ok
Name + Other | String = DMSEM005
Name + Other | Text = ok
Name + Other | Number = DMSEM003
Name + Other | Boolean = DMSEM003
return F(a, b: Number): Cents = a + b = DMFUN005
return F(a, b: Number): Number = a + b = DMFUN005
return F(a, b: Number): Text = a + b = DMFUN005+DMFUN006
return F(a, b: Text): Label = a + b = DMFUN005
return F(a, b: Text): Number = a + b = DMFUN005+DMFUN006
""";

        public const string TypelessComparisons = """
comparison = verdict
5 == "x" = ok
true == 5 = ok
5 < "x" = ok
Count == "x" = ok
Count == true = ok
Count == 5 = ok
Name == 5 = DMSEM003
""";

        public const string AllNullMatch = """
assign P := match | target = verdict
match { null, null } | Uuid = DMSEM003
match { null, null } | Guid = DMSEM003+DMSEM005
match { null, null } | Text = ok
match { null, null } | Number = DMSEM003
match { null, "x" } | Text = ok
match { null, "x" } | Uuid = DMSEM003
match { null, 5 } | Number = ok
match { null, 5 } | Text = DMSEM003
""";

        public const string SameName = """
declaration order | assign P:Color := value = verdict
entity then enum | "Bogus" = DMSEM003+DMSTR001
entity then enum | "Red" = DMSTR001
enum then entity | "Bogus" = DMSEM003+DMSTR001
enum then entity | "Red" = DMSTR001
""";

    }
}
