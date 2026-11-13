using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Ontology.Effects;

using DmAction = Poly.DomainModeling.Ontology.Action;
using DmPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;
using DmValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-05a characterisation grids (lock #679 6841065713, amendments 1 and 2), recorded on MASTER before any
/// production edit. The forms are the expression shapes the analyzer leaves Unresolved on master (arithmetic,
/// path-prefix, for-each and peer binder, sum, owned access) plus the ones it already types (count, match, call), each placed in
/// every position that consumes a type. Every cell is the full set of error codes (<see cref="Verdict"/>), or
/// <c>n/a</c> where the form cannot be written in that position (a function body has no entity, so no
/// relationship, binder or owned read). The fixture is built through the model API, like the T-04 pins, so an
/// entity-typed property and an owned access are expressible.
/// </summary>
public partial class AnalyzerClosingRoundPinTests {
    /// <summary>The target types a position is checked against; <c>null</c> is a comparison operand only.</summary>
    internal static readonly string[] GridTypes = ["Number", "Decimal", "Text", "Boolean", "Date", "Color", "Widget"];

    private static readonly string[] ComparisonTypes = [.. GridTypes, "null"];

    /// <summary>How a form is written: in an entity scope, or (null when it cannot be) in a function body.</summary>
    private sealed record GridForm(
        string Label,
        Func<DomainExpression> InEntity,
        Func<DomainExpression>? InFunction = null,
        bool Binder = false,
        bool Owned = false,
        bool Peer = false,
        bool Extended = false,
        bool Cash = false);

    private static DomainExpression A(string parameter) => DomainExpression.Parameter(parameter);

    private static DomainExpression Nav(string relationship, string property) =>
        DomainExpression.RelationshipNav(relationship, P(property));

    private static readonly GridForm[] Forms = [
        new("N + M", () => DomainExpression.Add(P("N"), P("M")),
            () => DomainExpression.Add(A("a"), A("b"))),
        new("C + C", () => DomainExpression.Add(P("C"), P("C")),
            () => DomainExpression.Add(A("c"), A("c"))),
        new("Due + N", () => DomainExpression.Add(P("Due"), P("N")),
            () => DomainExpression.Add(A("d"), A("a"))),
        new("part Qty", () => Nav("part", "Qty")),
        new("part Label", () => Nav("part", "Label")),
        new("part Due", () => Nav("part", "Due")),
        new("part Tone", () => Nav("part", "Tone")),
        new("p Qty", () => Nav("p", "Qty"), Binder: true),
        new("p Label", () => Nav("p", "Label"), Binder: true),
        new("o Qty", () => Nav("o", "Qty"), Peer: true),
        new("o Label", () => Nav("o", "Label"), Peer: true),
        new("count parts", () => DomainExpression.Count("parts", null)),
        new("sum parts Qty", () => DomainExpression.Sum("parts", "Qty")),
        new("sum parts Price", () => DomainExpression.Sum("parts", "Price")),
        new("Cost Amount", () => DomainExpression.Owned("Cost", P("Amount")), Owned: true),
        new("match { Flag -> N + M, else -> N }",
            () => DomainExpression.Match([new MatchArm(P("Flag"), DomainExpression.Add(P("N"), P("M")))], P("N")),
            () => DomainExpression.Match([new MatchArm(A("f"), DomainExpression.Add(A("a"), A("b")))], A("a"))),
        new("G(N)", () => DomainExpression.Call("G", [P("N")]),
            () => DomainExpression.Call("G", [A("a")])),
    ];

    /// <summary>Every position, in output order. A position names the target type it is checked against.</summary>
    internal static IEnumerable<string> GridPositions() {
        foreach (var site in new[] { "assign", "create", "invoke", "call", "return", "default" })
            foreach (var type in GridTypes)
                yield return $"{site} {type}";
        foreach (var type in ComparisonTypes)
            yield return $"form == {type}";
        foreach (var type in ComparisonTypes)
            yield return $"{type} == form";
        foreach (var site in new[] { "if form", "for where form", "not form", "form and Flag", "match { form -> 1 }" })
            yield return site;
        yield return "form * N";
        yield return "N * form";
        yield return "form * Name";
        yield return "Name * form";
        foreach (var type in GridTypes)
            yield return $"match arm into {type}";
    }

    private static DomainExpression Operand(string type) =>
        type == "null" ? DomainExpression.Literal(null) : P($"P_{type}");

    private static List<DomainType> GridTypesDeclared() => [
        new DmPrimitive("Number", PrimitiveSemantics.Number, []),
        new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
        new DmPrimitive("Text", PrimitiveSemantics.Text, []),
        new DmPrimitive("Boolean", PrimitiveSemantics.Boolean, []),
        .. TemporalTypeCatalog.Definitions.Select(static d => (DomainType)new DmPrimitive(d.Name, d.Semantics, [])),
        new EnumType("Color", ["Red", "Blue"], []),
        new Entity("Widget", [new Property("Id", Ref("Number"), [])], [], [], []),
        new Entity("Part", [
            new Property("Qty", Ref("Number"), []), new Property("Price", Ref("Decimal"), []),
            new Property("Label", Ref("Text"), []), new Property("Ok", Ref("Boolean"), []),
            new Property("Due", Ref("Date"), []), new Property("Tone", Ref("Color"), []),
        ], [], [], []),
        new DmValueType("Money", [new Property("Amount", Ref("Number"), [])], []),
    ];

    /// <summary>
    /// One cell's domain: <c>Order</c> holds the operands (N, M, C, Name, Flag, Due, Tone and one <c>P_T</c> per
    /// grid type), <c>part</c> (to-one) and <c>parts</c> (to-many) navigate to <c>Part</c>, <c>Box_T</c> is the
    /// create target, <c>Callee_T</c> the invoke target, <c>F_T</c> the call target and <c>G</c> the called form.
    /// </summary>
    private static Domain GridDomain(
        IReadOnlyList<Effect> tick, IReadOnlyList<Property>? extra = null, IReadOnlyList<DomainFunction>? functions = null,
        bool owned = false, bool peer = false, bool extended = false, bool cash = false,
        IReadOnlyList<Policy>? policies = null, IReadOnlyList<Policy>? tickPolicies = null, IReadOnlyList<Stage>? stages = null) {
        List<Property> props = [
            new("N", Ref("Number"), []), new("M", Ref("Number"), []), new("C", Ref("Decimal"), []),
            new("Name", Ref("Text"), []), new("Flag", Ref("Boolean"), []), new("Due", Ref("Date"), []),
            new("Tone", Ref("Color"), []),
            .. GridTypes.Select(t => new Property($"P_{t}", Ref(t), [])),
        ];
        if (owned) props.Add(new("Cost", Ref("Money"), []));
        if (extra is not null) props.AddRange(extra);
        List<DmAction> actions = [
            new("Tick", InvocationResult.Void, [], tick, tickPolicies ?? []),
            .. GridTypes.Select(t => new DmAction($"Callee_{t}", InvocationResult.Void, [new Property("X", Ref(t), [])], [], [])),
        ];
        var order = new Entity("Order", props, peer ? [.. actions.Skip(1)] : actions, policies ?? [], stages ?? []);
        if (peer)
            order = order with { Subscriptions = [new StageSubscription("parts", "Done", tick, peerBinding: "o")] };
        var boxes = GridTypes.Select(t => (DomainType)new Entity($"Box_{t}", [new Property("W", Ref(t), [])], [], [], []));
        List<DomainFunction> fns = [
            new("G", [new Property("x", Ref("Number"), [])], Ref("Number"), A("x")),
            .. GridTypes.Select(t => new DomainFunction($"F_{t}", [new Property("v", Ref(t), [])], Ref("Number"),
                DomainExpression.Literal(1L))),
        ];
        if (functions is not null) fns.AddRange(functions);
        var types = GridTypesDeclared();
        if (peer)
            types = [.. types.Select(t => t is Entity { Name: "Part" } part ? part with {
                Stages = [
                    new Stage("Open", [new DmAction("Finish", InvocationResult.Void, [],
                        [new StageTransitionEffect(new StageReference("Done"))], [])], [], [], []),
                    new Stage("Done", [], [], [], []),
                ],
            } : t)];
        List<Relationship> relationships = [
            new Relationship("part", Ref("Order"), Ref("Part"), RelationshipCardinality.OneToOne, []),
            new Relationship("parts", Ref("Order"), Ref("Part"), RelationshipCardinality.OneToMany, []),
        ];
        if (extended || cash) {
            // F3 (fix round 1): an optional, an entity-typed and (cash only) a value-typed member behind the prefix,
            // and a second to-one hop to Book. Only the forms that need them get them, so the master cells of the
            // first grid keep their fixture.
            types = [.. types.Select(t => t is Entity { Name: "Part" } part ? part with {
                Properties = [
                    .. part.Properties,
                    .. extended
                        ? new[] { new Property("Opt", Ref("Number"), [new OptionalConstraint()]), new Property("Owner", Ref("Widget"), []) }
                        : [],
                    .. cash ? new[] { new Property("Cash", Ref("Money"), []) } : [],
                ],
            } : t)];
            types.Add(new Entity("Book", [new Property("Qty", Ref("Number"), []), new Property("Title", Ref("Text"), [])], [], [], []));
            relationships.Add(new Relationship("book", Ref("Part"), Ref("Book"), RelationshipCardinality.OneToOne, []));
        }
        var domain = DomainTestFactory.Create("T", [.. types, order, .. boxes], relationships);
        return domain with { Functions = fns, Extensions = [ExtensionCatalog.TemporalId] };
    }

    private static Effect If(DomainExpression condition) => new ConditionalEffect(condition, [], null);

    private static Effect AssignTo(string type, DomainExpression value) => new AssignEffect(P($"P_{type}"), value);

    /// <summary>The effect that places <paramref name="form"/> at <paramref name="position"/>, or null when the
    /// position is not an action effect (return, default).</summary>
    private static Effect? EffectAt(string position, Func<DomainExpression> form) {
        var space = position.IndexOf(' ', StringComparison.Ordinal);
        var head = space < 0 ? position : position[..space];
        var type = space < 0 ? "" : position[(space + 1)..];
        switch (head) {
            case "assign": return AssignTo(type, form());
            case "create": return new CreateEntityInstance(Ref($"Box_{type}"), [new PropertyBinding("W", form())]);
            case "invoke": return new InvokeActionEffect($"Callee_{type}", [new PropertyBinding("X", form())]);
            case "call": return If(DomainExpression.Equal(DomainExpression.Call($"F_{type}", [form()]), DomainExpression.Literal(1L)));
        }
        if (position.StartsWith("form == ", StringComparison.Ordinal))
            return If(DomainExpression.Equal(form(), Operand(position["form == ".Length..])));
        if (position.EndsWith(" == form", StringComparison.Ordinal))
            return If(DomainExpression.Equal(Operand(position[..^" == form".Length]), form()));
        if (position.StartsWith("match arm into ", StringComparison.Ordinal)) {
            var target = position["match arm into ".Length..];
            return AssignTo(target, DomainExpression.Match([new MatchArm(P("Flag"), form())], P($"P_{target}")));
        }
        return position switch {
            "if form" => If(form()),
            "not form" => If(DomainExpression.Not(form())),
            "form and Flag" => If(DomainExpression.And(form(), P("Flag"))),
            "match { form -> 1 }" => AssignTo("Number",
                DomainExpression.Match([new MatchArm(form(), DomainExpression.Literal(1L))], DomainExpression.Literal(2L))),
            "form * N" => AssignTo("Number", DomainExpression.Multiply(form(), P("N"))),
            "N * form" => AssignTo("Number", DomainExpression.Multiply(P("N"), form())),
            "form * Name" => AssignTo("Number", DomainExpression.Multiply(form(), P("Name"))),
            "Name * form" => AssignTo("Number", DomainExpression.Multiply(P("Name"), form())),
            _ => null,
        };
    }

    private static string GridCell(GridForm form, string position) {
        if (position.StartsWith("return ", StringComparison.Ordinal)) {
            if (form.InFunction is null) return "n/a";
            var function = new DomainFunction("R", [
                new Property("a", Ref("Number"), []), new Property("b", Ref("Number"), []),
                new Property("c", Ref("Decimal"), []), new Property("d", Ref("Date"), []),
                new Property("f", Ref("Boolean"), []),
            ], Ref(position["return ".Length..]), form.InFunction());
            return Run(GridDomain([], functions: [function], extended: form.Extended, cash: form.Cash));
        }
        if (position.StartsWith("default ", StringComparison.Ordinal)) {
            if (form.Binder || form.Peer) return "n/a";
            var type = position["default ".Length..];
            return Run(GridDomain([], [new Property($"D_{type}", Ref(type), [new DefaultValueConstraint(form.InEntity())])],
                owned: form.Owned, extended: form.Extended, cash: form.Cash));
        }
        if (form.Binder && position == "for where form")
            return Run(GridDomain([new ForEachEffect("p", "parts", form.InEntity(), [])], extended: form.Extended, cash: form.Cash));
        Effect effect = position == "for where form"
            ? new ForEachEffect("q", "parts", form.InEntity(), [])
            : EffectAt(position, form.InEntity) ?? throw new InvalidOperationException(position);
        if (form.Binder) effect = new ForEachEffect("p", "parts", null, [effect]);
        return Run(GridDomain([effect], owned: form.Owned, peer: form.Peer, extended: form.Extended, cash: form.Cash));
    }

    /// <summary>One line per (form, position): <c>form | position = verdict</c>.</summary>
    internal static string FormGridRows() {
        var lines = new List<string> { "form | position = verdict" };
        foreach (var form in Forms)
            foreach (var position in GridPositions())
                lines.Add($"{form.Label} | {position} = {GridCell(form, position)}");
        return string.Join('\n', lines);
    }

    // ── fix round 1, F3: the forms and positions the first grid lacks ──

    private static readonly GridForm[] FormsF3 = [
        new("N (control)", () => P("N"), () => A("a")),
        new("part book Qty", () => DomainExpression.RelationshipNav("part", Nav("book", "Qty")), Extended: true),
        new("p book Qty", () => DomainExpression.RelationshipNav("p", Nav("book", "Qty")), Binder: true, Extended: true),
        new("part Opt", () => Nav("part", "Opt"), Extended: true),
        new("part Owner", () => Nav("part", "Owner"), Extended: true),
        new("part Cash", () => Nav("part", "Cash"), Cash: true),
        new("match { Flag -> N, else -> N + M }",
            () => DomainExpression.Match([new MatchArm(P("Flag"), P("N"))], DomainExpression.Add(P("N"), P("M"))),
            () => DomainExpression.Match([new MatchArm(A("f"), A("a"))], DomainExpression.Add(A("a"), A("b")))),
        new("match { Flag -> null, else -> N + M }",
            () => DomainExpression.Match([new MatchArm(P("Flag"), DomainExpression.Literal(null))], DomainExpression.Add(P("N"), P("M"))),
            () => DomainExpression.Match([new MatchArm(A("f"), DomainExpression.Literal(null))], DomainExpression.Add(A("a"), A("b")))),
    ];

    /// <summary>The positions that are not an action effect: a policy, an action guard, a stage guard and onEntry.</summary>
    internal static readonly string[] PositionsF3 = [
        "policy form == Number", "policy form == Text", "policy form == null", "policy form != null",
        "require form == Number", "require form == Text", "stage require form == Text",
        "onEntry assign Number", "onEntry assign Text",
    ];

    private static string GridCellF3(GridForm form, string position) {
        if (!PositionsF3.Contains(position, StringComparer.Ordinal))
            return GridCell(form, position);
        if (form.Peer || (form.Binder && !position.StartsWith("onEntry", StringComparison.Ordinal)))
            return "n/a";
        DomainExpression Compared(string tail) => tail == "null"
            ? DomainExpression.Literal(null)
            : P($"P_{tail}");
        Policy Guard() {
            var text = position[(position.IndexOf("form ", StringComparison.Ordinal) + "form ".Length)..];
            var op = text[..2];
            var operand = Compared(text[3..]);
            return new Policy("G", op == "!="
                ? DomainExpression.NotEqual(form.InEntity(), operand)
                : DomainExpression.Equal(form.InEntity(), operand));
        }
        if (position.StartsWith("policy ", StringComparison.Ordinal))
            return Run(GridDomain([], extended: form.Extended, cash: form.Cash, owned: form.Owned, policies: [Guard()]));
        if (position.StartsWith("require ", StringComparison.Ordinal))
            return Run(GridDomain([], extended: form.Extended, cash: form.Cash, owned: form.Owned, tickPolicies: [Guard()]));
        if (position.StartsWith("stage require ", StringComparison.Ordinal))
            return Run(GridDomain([], extended: form.Extended, cash: form.Cash, owned: form.Owned,
                stages: [new Stage("S", [], [Guard()], [], [])]));
        Effect entry = AssignTo(position["onEntry assign ".Length..], form.InEntity());
        if (form.Binder) entry = new ForEachEffect("p", "parts", null, [entry]);
        return Run(GridDomain([], extended: form.Extended, cash: form.Cash, owned: form.Owned,
            stages: [new Stage("S", [], [], [entry], [])]));
    }

    /// <summary>
    /// The second grid: every form of the first grid in the positions it lacked (policy, guards, onEntry), and the
    /// forms it lacked (a multi-hop path, a binder hop, an optional, an entity-typed and a value-typed member, a
    /// form in a match's else and beside a null arm, plus a typed control) in every position.
    /// </summary>
    internal static string FormGridF3Rows() {
        var lines = new List<string> { "form | position = verdict" };
        foreach (var form in Forms)
            foreach (var position in PositionsF3)
                lines.Add($"{form.Label} | {position} = {GridCellF3(form, position)}");
        foreach (var form in FormsF3)
            foreach (var position in GridPositions().Concat(PositionsF3))
                lines.Add($"{form.Label} | {position} = {GridCellF3(form, position)}");
        return string.Join('\n', lines);
    }

    // ── fix round 1, F1 and F2: date differences beyond two bare properties; null against an optional path ──

    private static string DslVerdictF1(string body) {
        var dsl = $$"""
            domain T
            uses temporal
            Part: entity {
              Due: Date
              Stamp: DateTime
              Qty: Number optional
            }
            Item: entity {
              A: Number
              Due: Date
              Stamp: DateTime
              Qty: Number optional
              Gap: Duration optional
              part: Part
              parts: many Part
              {{body}}
            }
            """;
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return Verdict(result.Analysis);
    }

    internal static readonly (string Label, string Body)[] DateDifferenceCases = [
        ("control (Due - Due) == (Due - Due)", "Ok: policy { (Due - Due) == (Due - Due) }"),
        ("(part Due - Due) == (Due - Due)", "Ok: policy { (part Due - Due) == (Due - Due) }"),
        ("(Due - part Due) == (Due - Due)", "Ok: policy { (Due - part Due) == (Due - Due) }"),
        ("(part Due - part Due) == (Due - Due)", "Ok: policy { (part Due - part Due) == (Due - Due) }"),
        ("((Due + A) - Due) == (Due - Due)", "Ok: policy { ((Due + A) - Due) == (Due - Due) }"),
        ("(Due - (Due + A)) == (Due - Due)", "Ok: policy { (Due - (Due + A)) == (Due - Due) }"),
        ("control assign Gap to Stamp - Stamp", "Close: action { assign Gap to Stamp - Stamp }"),
        ("assign Gap to part Stamp - Stamp", "Close: action { assign Gap to part Stamp - Stamp }"),
        ("assign Gap to (Stamp + A) - Stamp", "Close: action { assign Gap to (Stamp + A) - Stamp }"),
        ("for p in parts { assign Gap to p Stamp - Stamp }", "Close: action { for p in parts { assign Gap to p Stamp - Stamp } }"),
        ("mixed assign Gap to part Stamp - Due", "Close: action { assign Gap to part Stamp - Due }"),
        ("mixed control assign Gap to Stamp - Due", "Close: action { assign Gap to Stamp - Due }"),
        ("library assign Gap to Now - Stamp", "Close: action { assign Gap to Now - Stamp }"),
        ("library (Today - Due) == (Due - Due)", "Ok: policy { (Today - Due) == (Due - Due) }"),
        ("library assign Gap to (Stamp + 1 Days) - Stamp", "Close: action { assign Gap to (Stamp + 1 Days) - Stamp }"),
    ];

    internal static string DateDifferenceRows() {
        var lines = new List<string> { "case = verdict" };
        foreach (var (label, body) in DateDifferenceCases)
            lines.Add($"{label} = {DslVerdictF1(body)}");
        return string.Join('\n', lines);
    }

    /// <summary>F2: <c>null</c> against an optional property, bare and behind a path prefix (optionality is T-06's).</summary>
    internal static readonly (string Label, string Body)[] OptionalNullCases = [
        ("Qty is not null", "Ok: policy { Qty is not null }"),
        ("Qty is null", "Ok: policy { Qty is null }"),
        ("Qty == null", "Ok: policy { Qty == null }"),
        ("part Qty is not null", "Ok: policy { part Qty is not null }"),
        ("part Qty is null", "Ok: policy { part Qty is null }"),
        ("part Qty == null", "Ok: policy { part Qty == null }"),
        ("null == part Qty", "Ok: policy { null == part Qty }"),
        ("for p in parts where p Qty is not null", "Close: action { for p in parts where p Qty is not null { assign A to 1 } }"),
    ];

    internal static string OptionalNullRows() {
        var lines = new List<string> { "case = verdict" };
        foreach (var (label, body) in OptionalNullCases)
            lines.Add($"{label} = {DslVerdictF1(body)}");
        return string.Join('\n', lines);
    }

    // ── amendment 2's extra rows: DateOperation over a typeless Date form; the imported-contract name clash ──

    private static string DslVerdict(string body) {
        var dsl = $$"""
            domain T
            uses temporal
            Part: entity {
              Due: Date
              Qty: Number
            }
            Item: entity {
              A: Number
              B: Number
              Due: Date
              Stamp: DateTime
              part: Part
              {{body}}
            }
            """;
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(new PolyDslParser(dsl).Parse());
        return Verdict(result.Analysis);
    }

    internal static readonly (string Label, string Body)[] DateOperationOverTypelessDateCases = [
        ("control assign Due to Due + 5 Days", "Tick: action { assign Due to Due + 5 Days }"),
        ("assign Due to part Due + 5 Days", "Tick: action { assign Due to part Due + 5 Days }"),
        ("assign Due to (Due + A) + 5 Days", "Tick: action { assign Due to (Due + A) + 5 Days }"),
        ("assign Stamp to (Stamp + A) + 5 Hours", "Tick: action { assign Stamp to (Stamp + A) + 5 Hours }"),
        ("policy Due < part Due + 5 Days", "Ok: policy { Due < part Due + 5 Days }"),
        ("policy part Due + 5 Days < Due", "Ok: policy { part Due + 5 Days < Due }"),
        ("policy (Due + A) + 5 Days < Due", "Ok: policy { (Due + A) + 5 Days < Due }"),
        ("assign Due to Stamp + 1", "Tick: action { assign Due to Stamp + 1 }"),
        ("assign Stamp to Due + 1", "Tick: action { assign Stamp to Due + 1 }"),
        ("assign A to part Due + 5 Days", "Tick: action { assign A to part Due + 5 Days }"),
    ];

    internal static string DateOperationOverTypelessDateRows() {
        var lines = new List<string> { "case = verdict" };
        foreach (var (label, body) in DateOperationOverTypelessDateCases)
            lines.Add($"{label} = {DslVerdict(body)}");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// A local value type and an imported contract type with the same name. The catalog lookup lets the
    /// imported one win (last wins); the type model's provider keeps the local one (first wins).
    /// </summary>
    internal static string ImportedContractNameClashRows() {
        var lines = new List<string> { "local Money { Amount: Number } and imported Money { Code: Text } | case = verdict" };
        (string Label, Func<IReadOnlyList<Effect>> Tick)[] cases = [
            ("assign P:Money := Q:Money", () => [new AssignEffect(P("P"), P("Q"))]),
            ("assign N := Q Amount (owned)", () => [new AssignEffect(P("N"), DomainExpression.Owned("Q", P("Amount")))]),
            ("assign N := Q Code (owned)", () => [new AssignEffect(P("N"), DomainExpression.Owned("Q", P("Code")))]),
            ("assign Name := Q Amount (owned)", () => [new AssignEffect(P("Name"), DomainExpression.Owned("Q", P("Amount")))]),
        ];
        foreach (var (label, tick) in cases) {
            var local = new DmValueType("Money", [new Property("Amount", Ref("Number"), [])], []);
            var imported = new DmValueType("Money", [new Property("Code", Ref("Text"), [])], []);
            var host = new Entity("Host", [
                new Property("N", Ref("Number"), []), new Property("Name", Ref("Text"), []),
                new Property("P", Ref("Money"), []), new Property("Q", Ref("Money"), []),
            ], [new DmAction("Set", InvocationResult.Void, [], tick(), [])], [], []);
            var contract = new ImportedContract("Bank", ContractSourceKind.ExternalProvider, "bank://api", "v1", []) {
                Types = [imported],
            };
            var domain = new Domain("T", [
                new DmPrimitive("Number", PrimitiveSemantics.Number, []),
                new DmPrimitive("Text", PrimitiveSemantics.Text, []),
                local, host,
            ]) { ImportedContracts = [contract] };
            lines.Add($"{label} = {Run(domain)}");
        }
        return string.Join('\n', lines);
    }

    // ── sum over each property kind (plan M1-M3): a declared alias, an undeclared spelling, a non-numeric type ──

    internal static readonly string[] SumPropertyTypes = [
        "Number", "Decimal", "Cents", "Id64", "Int32", "Float", "long", "decimal", "Text", "Boolean", "Color", "Money", "Bin", "Nope",
    ];

    /// <summary>F3: the legacy spellings the first sum grid lacked.</summary>
    internal static readonly string[] SumPropertyTypesF3 = ["Int", "Int64", "Double"];

    /// <summary>F3: a pack's CLR type-name override, per property type (fork B: analysis honours it).</summary>
    internal static readonly (string Label, string Name, string Clr)[] SumOverrides = [
        ("Number -> int", "Number", "int"), ("Decimal -> double", "Decimal", "double"),
        ("Double -> float", "Double", "float"), ("Int -> short", "Int", "short"),
    ];

    /// <summary><c>sum parts X</c> per property type of <c>X</c>, in a policy and assigned into Number and Decimal.</summary>
    internal static string SumPropertyKindRows() {
        var lines = new List<string> { "sum parts X, X: <type> | position = verdict" };
        foreach (var type in SumPropertyTypes)
            foreach (var position in new[] { "policy sum > 0", "assign Number", "assign Decimal" }) {
                var sum = DomainExpression.Sum("parts", "X");
                var item = new Entity("Item", [new Property("X", Ref(type), [])], [], [], []);
                var host = new Entity("Host", [new Property("N", Ref("Number"), []), new Property("C", Ref("Decimal"), [])],
                    position == "policy sum > 0" ? [] : [new DmAction("Set", InvocationResult.Void, [],
                        [new AssignEffect(P(position == "assign Number" ? "N" : "C"), sum)], [])],
                    position == "policy sum > 0" ? [new Policy("Big", DomainExpression.GreaterThan(sum, DomainExpression.Literal(0L)))] : [],
                    []);
                var domain = DomainTestFactory.Create("T", [
                    new DmPrimitive("Number", PrimitiveSemantics.Number, []),
                    new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
                    new DmPrimitive("Text", PrimitiveSemantics.Text, []),
                    new DmPrimitive("Boolean", PrimitiveSemantics.Boolean, []),
                    new DmPrimitive("Cents", PrimitiveSemantics.Decimal, []),
                    new DmPrimitive("Id64", PrimitiveSemantics.Number, []),
                    new DmPrimitive("Bin", PrimitiveSemantics.Binary, []),
                    new EnumType("Color", ["Red", "Blue"], []),
                    new DmValueType("Money", [new Property("Amount", Ref("Number"), [])], []),
                    item, host,
                ], [new Relationship("parts", Ref("Host"), Ref("Item"), RelationshipCardinality.OneToMany, [])]);
                lines.Add($"{type} | {position} = {Run(domain)}");
            }
        return string.Join('\n', lines);
    }

    private static Domain SumDomain(string type, string position) {
        var sum = DomainExpression.Sum("parts", "X");
        var item = new Entity("Item", [new Property("X", Ref(type), [])], [], [], []);
        var host = new Entity("Host", [new Property("N", Ref("Number"), []), new Property("C", Ref("Decimal"), [])],
            position == "policy sum > 0" ? [] : [new DmAction("Set", InvocationResult.Void, [],
                [new AssignEffect(P(position == "assign Number" ? "N" : "C"), sum)], [])],
            position == "policy sum > 0" ? [new Policy("Big", DomainExpression.GreaterThan(sum, DomainExpression.Literal(0L)))] : [],
            []);
        return DomainTestFactory.Create("T", [
            new DmPrimitive("Number", PrimitiveSemantics.Number, []),
            new DmPrimitive("Decimal", PrimitiveSemantics.Decimal, []),
            item, host,
        ], [new Relationship("parts", Ref("Host"), Ref("Item"), RelationshipCardinality.OneToMany, [])]);
    }

    /// <summary>
    /// The verdict, plus <c>(not numeric)</c> when the sum check itself fired. The legacy spellings are undeclared
    /// names, so every row carries their DMSEM005 reference error; the marker separates the sum's own DMSEM005.
    /// </summary>
    private static string SumVerdict(AnalysisResult analysis) =>
        Verdict(analysis) + (analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error
            && d.Message.Contains("which is not numeric", StringComparison.Ordinal)) ? " (not numeric)" : "");

    /// <summary>F3: <c>sum parts X</c> for the legacy spellings Int, Int64 and Double, then under each pack override.</summary>
    internal static string SumPropertyKindF3Rows() {
        var lines = new List<string> { "sum parts X, X: <type> [override] | position = verdict" };
        string[] positions = ["policy sum > 0", "assign Number", "assign Decimal"];
        foreach (var type in SumPropertyTypesF3)
            foreach (var position in positions)
                lines.Add($"{type} | {position} = {SumVerdict(DomainModelAnalyzer.Analyze(SumDomain(type, position)))}");
        foreach (var (label, name, clr) in SumOverrides)
            foreach (var position in positions) {
                var builder = SessionBuilder.CreateEmpty().LoadCore();
                builder.TypeMaps.OverrideClrTypeName(name, clr);
                lines.Add($"{name} [{label}] | {position} = {SumVerdict(builder.Build().Analyze(SumDomain(name, position)))}");
            }
        return string.Join('\n', lines);
    }

    [Test]
    public async Task FormByPositionGridF3_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(FormGridF3Rows(), MasterPlusFlips("FormGridF3", FormGridExpected.FormGridF3))).IsEqualTo("");

    [Test]
    public async Task DateDifferenceBeyondTwoProperties_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(DateDifferenceRows(),
            MasterPlusFlips("DateDifference", FormGridExpected.DateDifference))).IsEqualTo("");

    [Test]
    public async Task NullAgainstAnOptionalPath_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(OptionalNullRows(),
            MasterPlusFlips("OptionalNull", FormGridExpected.OptionalNull))).IsEqualTo("");

    [Test]
    public async Task SumOverLegacySpellingsAndPackOverrides_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(SumPropertyKindF3Rows(),
            MasterPlusFlips("SumPropertyKindsF3", FormGridExpected.SumPropertyKindsF3))).IsEqualTo("");

    [Test]
    public async Task SumOverEachPropertyKind_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(SumPropertyKindRows(),
            MasterPlusFlips("SumPropertyKinds", FormGridExpected.SumPropertyKinds))).IsEqualTo("");

    [Test]
    public async Task FormByPositionGrid_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(FormGridRows(), MasterPlusFlips("FormGrid", FormGridExpected.FormGrid))).IsEqualTo("");

    [Test]
    public async Task DateOperationOverATypelessDateForm_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(DateOperationOverTypelessDateRows(),
            MasterPlusFlips("DateOperationOverTypelessDate", FormGridExpected.DateOperationOverTypelessDate))).IsEqualTo("");

    [Test]
    public async Task ImportedContractTypeSharingALocalName_IsMasterPlusTheFlips() =>
        await Assert.That(GridDifferences(ImportedContractNameClashRows(),
            MasterPlusFlips("ImportedContractNameClash", FormGridExpected.ImportedContractNameClash))).IsEqualTo("");

    private static class FormGridExpected {
        public const string FormGrid = """
form | position = verdict
N + M | assign Number = ok
N + M | assign Decimal = ok
N + M | assign Text = ok
N + M | assign Boolean = ok
N + M | assign Date = ok
N + M | assign Color = ok
N + M | assign Widget = ok
N + M | create Number = ok
N + M | create Decimal = ok
N + M | create Text = ok
N + M | create Boolean = ok
N + M | create Date = ok
N + M | create Color = ok
N + M | create Widget = ok
N + M | invoke Number = ok
N + M | invoke Decimal = ok
N + M | invoke Text = ok
N + M | invoke Boolean = ok
N + M | invoke Date = ok
N + M | invoke Color = ok
N + M | invoke Widget = ok
N + M | call Number = ok
N + M | call Decimal = ok
N + M | call Text = DMFUN003
N + M | call Boolean = DMFUN003
N + M | call Date = DMFUN003
N + M | call Color = DMFUN003
N + M | call Widget = ok
N + M | return Number = ok
N + M | return Decimal = ok
N + M | return Text = DMFUN006
N + M | return Boolean = DMFUN006
N + M | return Date = DMFUN006
N + M | return Color = DMFUN006
N + M | return Widget = ok
N + M | default Number = ok
N + M | default Decimal = ok
N + M | default Text = ok
N + M | default Boolean = ok
N + M | default Date = ok
N + M | default Color = ok
N + M | default Widget = ok
N + M | form == Number = ok
N + M | form == Decimal = ok
N + M | form == Text = ok
N + M | form == Boolean = ok
N + M | form == Date = ok
N + M | form == Color = ok
N + M | form == Widget = ok
N + M | form == null = ok
N + M | Number == form = ok
N + M | Decimal == form = ok
N + M | Text == form = ok
N + M | Boolean == form = ok
N + M | Date == form = ok
N + M | Color == form = ok
N + M | Widget == form = ok
N + M | null == form = ok
N + M | if form = ok
N + M | for where form = ok
N + M | not form = ok
N + M | form and Flag = ok
N + M | match { form -> 1 } = ok
N + M | form * N = ok
N + M | N * form = ok
N + M | form * Name = ok
N + M | Name * form = ok
N + M | match arm into Number = ok
N + M | match arm into Decimal = ok
N + M | match arm into Text = ok
N + M | match arm into Boolean = ok
N + M | match arm into Date = ok
N + M | match arm into Color = ok
N + M | match arm into Widget = ok
C + C | assign Number = ok
C + C | assign Decimal = ok
C + C | assign Text = ok
C + C | assign Boolean = ok
C + C | assign Date = ok
C + C | assign Color = ok
C + C | assign Widget = ok
C + C | create Number = ok
C + C | create Decimal = ok
C + C | create Text = ok
C + C | create Boolean = ok
C + C | create Date = ok
C + C | create Color = ok
C + C | create Widget = ok
C + C | invoke Number = ok
C + C | invoke Decimal = ok
C + C | invoke Text = ok
C + C | invoke Boolean = ok
C + C | invoke Date = ok
C + C | invoke Color = ok
C + C | invoke Widget = ok
C + C | call Number = ok
C + C | call Decimal = ok
C + C | call Text = DMFUN003
C + C | call Boolean = DMFUN003
C + C | call Date = DMFUN003
C + C | call Color = DMFUN003
C + C | call Widget = ok
C + C | return Number = ok
C + C | return Decimal = ok
C + C | return Text = DMFUN006
C + C | return Boolean = DMFUN006
C + C | return Date = DMFUN006
C + C | return Color = DMFUN006
C + C | return Widget = ok
C + C | default Number = ok
C + C | default Decimal = ok
C + C | default Text = ok
C + C | default Boolean = ok
C + C | default Date = ok
C + C | default Color = ok
C + C | default Widget = ok
C + C | form == Number = ok
C + C | form == Decimal = ok
C + C | form == Text = ok
C + C | form == Boolean = ok
C + C | form == Date = ok
C + C | form == Color = ok
C + C | form == Widget = ok
C + C | form == null = ok
C + C | Number == form = ok
C + C | Decimal == form = ok
C + C | Text == form = ok
C + C | Boolean == form = ok
C + C | Date == form = ok
C + C | Color == form = ok
C + C | Widget == form = ok
C + C | null == form = ok
C + C | if form = ok
C + C | for where form = ok
C + C | not form = ok
C + C | form and Flag = ok
C + C | match { form -> 1 } = ok
C + C | form * N = ok
C + C | N * form = ok
C + C | form * Name = ok
C + C | Name * form = ok
C + C | match arm into Number = ok
C + C | match arm into Decimal = ok
C + C | match arm into Text = ok
C + C | match arm into Boolean = ok
C + C | match arm into Date = ok
C + C | match arm into Color = ok
C + C | match arm into Widget = ok
Due + N | assign Number = ok
Due + N | assign Decimal = ok
Due + N | assign Text = ok
Due + N | assign Boolean = ok
Due + N | assign Date = ok
Due + N | assign Color = ok
Due + N | assign Widget = ok
Due + N | create Number = ok
Due + N | create Decimal = ok
Due + N | create Text = ok
Due + N | create Boolean = ok
Due + N | create Date = ok
Due + N | create Color = ok
Due + N | create Widget = ok
Due + N | invoke Number = ok
Due + N | invoke Decimal = ok
Due + N | invoke Text = ok
Due + N | invoke Boolean = ok
Due + N | invoke Date = ok
Due + N | invoke Color = ok
Due + N | invoke Widget = ok
Due + N | call Number = ok
Due + N | call Decimal = ok
Due + N | call Text = ok
Due + N | call Boolean = ok
Due + N | call Date = ok
Due + N | call Color = ok
Due + N | call Widget = ok
Due + N | return Number = ok
Due + N | return Decimal = ok
Due + N | return Text = ok
Due + N | return Boolean = ok
Due + N | return Date = ok
Due + N | return Color = ok
Due + N | return Widget = ok
Due + N | default Number = ok
Due + N | default Decimal = ok
Due + N | default Text = ok
Due + N | default Boolean = ok
Due + N | default Date = ok
Due + N | default Color = ok
Due + N | default Widget = ok
Due + N | form == Number = ok
Due + N | form == Decimal = ok
Due + N | form == Text = ok
Due + N | form == Boolean = ok
Due + N | form == Date = ok
Due + N | form == Color = ok
Due + N | form == Widget = ok
Due + N | form == null = ok
Due + N | Number == form = ok
Due + N | Decimal == form = ok
Due + N | Text == form = ok
Due + N | Boolean == form = ok
Due + N | Date == form = ok
Due + N | Color == form = ok
Due + N | Widget == form = ok
Due + N | null == form = ok
Due + N | if form = ok
Due + N | for where form = ok
Due + N | not form = ok
Due + N | form and Flag = ok
Due + N | match { form -> 1 } = ok
Due + N | form * N = ok
Due + N | N * form = ok
Due + N | form * Name = ok
Due + N | Name * form = ok
Due + N | match arm into Number = ok
Due + N | match arm into Decimal = ok
Due + N | match arm into Text = ok
Due + N | match arm into Boolean = ok
Due + N | match arm into Date = ok
Due + N | match arm into Color = ok
Due + N | match arm into Widget = ok
part Qty | assign Number = ok
part Qty | assign Decimal = ok
part Qty | assign Text = ok
part Qty | assign Boolean = ok
part Qty | assign Date = ok
part Qty | assign Color = ok
part Qty | assign Widget = ok
part Qty | create Number = ok
part Qty | create Decimal = ok
part Qty | create Text = ok
part Qty | create Boolean = ok
part Qty | create Date = ok
part Qty | create Color = ok
part Qty | create Widget = ok
part Qty | invoke Number = ok
part Qty | invoke Decimal = ok
part Qty | invoke Text = ok
part Qty | invoke Boolean = ok
part Qty | invoke Date = ok
part Qty | invoke Color = ok
part Qty | invoke Widget = ok
part Qty | call Number = ok
part Qty | call Decimal = ok
part Qty | call Text = ok
part Qty | call Boolean = ok
part Qty | call Date = ok
part Qty | call Color = ok
part Qty | call Widget = ok
part Qty | return Number = n/a
part Qty | return Decimal = n/a
part Qty | return Text = n/a
part Qty | return Boolean = n/a
part Qty | return Date = n/a
part Qty | return Color = n/a
part Qty | return Widget = n/a
part Qty | default Number = ok
part Qty | default Decimal = ok
part Qty | default Text = ok
part Qty | default Boolean = ok
part Qty | default Date = ok
part Qty | default Color = ok
part Qty | default Widget = ok
part Qty | form == Number = ok
part Qty | form == Decimal = ok
part Qty | form == Text = ok
part Qty | form == Boolean = ok
part Qty | form == Date = ok
part Qty | form == Color = ok
part Qty | form == Widget = ok
part Qty | form == null = ok
part Qty | Number == form = ok
part Qty | Decimal == form = ok
part Qty | Text == form = ok
part Qty | Boolean == form = ok
part Qty | Date == form = ok
part Qty | Color == form = ok
part Qty | Widget == form = ok
part Qty | null == form = ok
part Qty | if form = ok
part Qty | for where form = ok
part Qty | not form = ok
part Qty | form and Flag = ok
part Qty | match { form -> 1 } = ok
part Qty | form * N = ok
part Qty | N * form = ok
part Qty | form * Name = ok
part Qty | Name * form = ok
part Qty | match arm into Number = ok
part Qty | match arm into Decimal = ok
part Qty | match arm into Text = ok
part Qty | match arm into Boolean = ok
part Qty | match arm into Date = ok
part Qty | match arm into Color = ok
part Qty | match arm into Widget = ok
part Label | assign Number = ok
part Label | assign Decimal = ok
part Label | assign Text = ok
part Label | assign Boolean = ok
part Label | assign Date = ok
part Label | assign Color = ok
part Label | assign Widget = ok
part Label | create Number = ok
part Label | create Decimal = ok
part Label | create Text = ok
part Label | create Boolean = ok
part Label | create Date = ok
part Label | create Color = ok
part Label | create Widget = ok
part Label | invoke Number = ok
part Label | invoke Decimal = ok
part Label | invoke Text = ok
part Label | invoke Boolean = ok
part Label | invoke Date = ok
part Label | invoke Color = ok
part Label | invoke Widget = ok
part Label | call Number = ok
part Label | call Decimal = ok
part Label | call Text = ok
part Label | call Boolean = ok
part Label | call Date = ok
part Label | call Color = ok
part Label | call Widget = ok
part Label | return Number = n/a
part Label | return Decimal = n/a
part Label | return Text = n/a
part Label | return Boolean = n/a
part Label | return Date = n/a
part Label | return Color = n/a
part Label | return Widget = n/a
part Label | default Number = ok
part Label | default Decimal = ok
part Label | default Text = ok
part Label | default Boolean = ok
part Label | default Date = ok
part Label | default Color = ok
part Label | default Widget = ok
part Label | form == Number = ok
part Label | form == Decimal = ok
part Label | form == Text = ok
part Label | form == Boolean = ok
part Label | form == Date = ok
part Label | form == Color = ok
part Label | form == Widget = ok
part Label | form == null = ok
part Label | Number == form = ok
part Label | Decimal == form = ok
part Label | Text == form = ok
part Label | Boolean == form = ok
part Label | Date == form = ok
part Label | Color == form = ok
part Label | Widget == form = ok
part Label | null == form = ok
part Label | if form = ok
part Label | for where form = ok
part Label | not form = ok
part Label | form and Flag = ok
part Label | match { form -> 1 } = ok
part Label | form * N = ok
part Label | N * form = ok
part Label | form * Name = ok
part Label | Name * form = ok
part Label | match arm into Number = ok
part Label | match arm into Decimal = ok
part Label | match arm into Text = ok
part Label | match arm into Boolean = ok
part Label | match arm into Date = ok
part Label | match arm into Color = ok
part Label | match arm into Widget = ok
part Due | assign Number = ok
part Due | assign Decimal = ok
part Due | assign Text = ok
part Due | assign Boolean = ok
part Due | assign Date = ok
part Due | assign Color = ok
part Due | assign Widget = ok
part Due | create Number = ok
part Due | create Decimal = ok
part Due | create Text = ok
part Due | create Boolean = ok
part Due | create Date = ok
part Due | create Color = ok
part Due | create Widget = ok
part Due | invoke Number = ok
part Due | invoke Decimal = ok
part Due | invoke Text = ok
part Due | invoke Boolean = ok
part Due | invoke Date = ok
part Due | invoke Color = ok
part Due | invoke Widget = ok
part Due | call Number = ok
part Due | call Decimal = ok
part Due | call Text = ok
part Due | call Boolean = ok
part Due | call Date = ok
part Due | call Color = ok
part Due | call Widget = ok
part Due | return Number = n/a
part Due | return Decimal = n/a
part Due | return Text = n/a
part Due | return Boolean = n/a
part Due | return Date = n/a
part Due | return Color = n/a
part Due | return Widget = n/a
part Due | default Number = ok
part Due | default Decimal = ok
part Due | default Text = ok
part Due | default Boolean = ok
part Due | default Date = ok
part Due | default Color = ok
part Due | default Widget = ok
part Due | form == Number = ok
part Due | form == Decimal = ok
part Due | form == Text = ok
part Due | form == Boolean = ok
part Due | form == Date = ok
part Due | form == Color = ok
part Due | form == Widget = ok
part Due | form == null = ok
part Due | Number == form = ok
part Due | Decimal == form = ok
part Due | Text == form = ok
part Due | Boolean == form = ok
part Due | Date == form = ok
part Due | Color == form = ok
part Due | Widget == form = ok
part Due | null == form = ok
part Due | if form = ok
part Due | for where form = ok
part Due | not form = ok
part Due | form and Flag = ok
part Due | match { form -> 1 } = ok
part Due | form * N = ok
part Due | N * form = ok
part Due | form * Name = ok
part Due | Name * form = ok
part Due | match arm into Number = ok
part Due | match arm into Decimal = ok
part Due | match arm into Text = ok
part Due | match arm into Boolean = ok
part Due | match arm into Date = ok
part Due | match arm into Color = ok
part Due | match arm into Widget = ok
part Tone | assign Number = ok
part Tone | assign Decimal = ok
part Tone | assign Text = ok
part Tone | assign Boolean = ok
part Tone | assign Date = ok
part Tone | assign Color = ok
part Tone | assign Widget = ok
part Tone | create Number = ok
part Tone | create Decimal = ok
part Tone | create Text = ok
part Tone | create Boolean = ok
part Tone | create Date = ok
part Tone | create Color = ok
part Tone | create Widget = ok
part Tone | invoke Number = ok
part Tone | invoke Decimal = ok
part Tone | invoke Text = ok
part Tone | invoke Boolean = ok
part Tone | invoke Date = ok
part Tone | invoke Color = ok
part Tone | invoke Widget = ok
part Tone | call Number = ok
part Tone | call Decimal = ok
part Tone | call Text = ok
part Tone | call Boolean = ok
part Tone | call Date = ok
part Tone | call Color = ok
part Tone | call Widget = ok
part Tone | return Number = n/a
part Tone | return Decimal = n/a
part Tone | return Text = n/a
part Tone | return Boolean = n/a
part Tone | return Date = n/a
part Tone | return Color = n/a
part Tone | return Widget = n/a
part Tone | default Number = ok
part Tone | default Decimal = ok
part Tone | default Text = ok
part Tone | default Boolean = ok
part Tone | default Date = ok
part Tone | default Color = ok
part Tone | default Widget = ok
part Tone | form == Number = ok
part Tone | form == Decimal = ok
part Tone | form == Text = ok
part Tone | form == Boolean = ok
part Tone | form == Date = ok
part Tone | form == Color = ok
part Tone | form == Widget = ok
part Tone | form == null = ok
part Tone | Number == form = ok
part Tone | Decimal == form = ok
part Tone | Text == form = ok
part Tone | Boolean == form = ok
part Tone | Date == form = ok
part Tone | Color == form = ok
part Tone | Widget == form = ok
part Tone | null == form = ok
part Tone | if form = ok
part Tone | for where form = ok
part Tone | not form = ok
part Tone | form and Flag = ok
part Tone | match { form -> 1 } = ok
part Tone | form * N = ok
part Tone | N * form = ok
part Tone | form * Name = ok
part Tone | Name * form = ok
part Tone | match arm into Number = ok
part Tone | match arm into Decimal = ok
part Tone | match arm into Text = ok
part Tone | match arm into Boolean = ok
part Tone | match arm into Date = ok
part Tone | match arm into Color = ok
part Tone | match arm into Widget = ok
p Qty | assign Number = ok
p Qty | assign Decimal = ok
p Qty | assign Text = ok
p Qty | assign Boolean = ok
p Qty | assign Date = ok
p Qty | assign Color = ok
p Qty | assign Widget = ok
p Qty | create Number = ok
p Qty | create Decimal = ok
p Qty | create Text = ok
p Qty | create Boolean = ok
p Qty | create Date = ok
p Qty | create Color = ok
p Qty | create Widget = ok
p Qty | invoke Number = ok
p Qty | invoke Decimal = ok
p Qty | invoke Text = ok
p Qty | invoke Boolean = ok
p Qty | invoke Date = ok
p Qty | invoke Color = ok
p Qty | invoke Widget = ok
p Qty | call Number = ok
p Qty | call Decimal = ok
p Qty | call Text = ok
p Qty | call Boolean = ok
p Qty | call Date = ok
p Qty | call Color = ok
p Qty | call Widget = ok
p Qty | return Number = n/a
p Qty | return Decimal = n/a
p Qty | return Text = n/a
p Qty | return Boolean = n/a
p Qty | return Date = n/a
p Qty | return Color = n/a
p Qty | return Widget = n/a
p Qty | default Number = n/a
p Qty | default Decimal = n/a
p Qty | default Text = n/a
p Qty | default Boolean = n/a
p Qty | default Date = n/a
p Qty | default Color = n/a
p Qty | default Widget = n/a
p Qty | form == Number = ok
p Qty | form == Decimal = ok
p Qty | form == Text = ok
p Qty | form == Boolean = ok
p Qty | form == Date = ok
p Qty | form == Color = ok
p Qty | form == Widget = ok
p Qty | form == null = ok
p Qty | Number == form = ok
p Qty | Decimal == form = ok
p Qty | Text == form = ok
p Qty | Boolean == form = ok
p Qty | Date == form = ok
p Qty | Color == form = ok
p Qty | Widget == form = ok
p Qty | null == form = ok
p Qty | if form = ok
p Qty | for where form = DMSEM003
p Qty | not form = ok
p Qty | form and Flag = ok
p Qty | match { form -> 1 } = ok
p Qty | form * N = ok
p Qty | N * form = ok
p Qty | form * Name = ok
p Qty | Name * form = ok
p Qty | match arm into Number = ok
p Qty | match arm into Decimal = ok
p Qty | match arm into Text = ok
p Qty | match arm into Boolean = ok
p Qty | match arm into Date = ok
p Qty | match arm into Color = ok
p Qty | match arm into Widget = ok
p Label | assign Number = ok
p Label | assign Decimal = ok
p Label | assign Text = ok
p Label | assign Boolean = ok
p Label | assign Date = ok
p Label | assign Color = ok
p Label | assign Widget = ok
p Label | create Number = ok
p Label | create Decimal = ok
p Label | create Text = ok
p Label | create Boolean = ok
p Label | create Date = ok
p Label | create Color = ok
p Label | create Widget = ok
p Label | invoke Number = ok
p Label | invoke Decimal = ok
p Label | invoke Text = ok
p Label | invoke Boolean = ok
p Label | invoke Date = ok
p Label | invoke Color = ok
p Label | invoke Widget = ok
p Label | call Number = ok
p Label | call Decimal = ok
p Label | call Text = ok
p Label | call Boolean = ok
p Label | call Date = ok
p Label | call Color = ok
p Label | call Widget = ok
p Label | return Number = n/a
p Label | return Decimal = n/a
p Label | return Text = n/a
p Label | return Boolean = n/a
p Label | return Date = n/a
p Label | return Color = n/a
p Label | return Widget = n/a
p Label | default Number = n/a
p Label | default Decimal = n/a
p Label | default Text = n/a
p Label | default Boolean = n/a
p Label | default Date = n/a
p Label | default Color = n/a
p Label | default Widget = n/a
p Label | form == Number = ok
p Label | form == Decimal = ok
p Label | form == Text = ok
p Label | form == Boolean = ok
p Label | form == Date = ok
p Label | form == Color = ok
p Label | form == Widget = ok
p Label | form == null = ok
p Label | Number == form = ok
p Label | Decimal == form = ok
p Label | Text == form = ok
p Label | Boolean == form = ok
p Label | Date == form = ok
p Label | Color == form = ok
p Label | Widget == form = ok
p Label | null == form = ok
p Label | if form = ok
p Label | for where form = DMSEM003
p Label | not form = ok
p Label | form and Flag = ok
p Label | match { form -> 1 } = ok
p Label | form * N = ok
p Label | N * form = ok
p Label | form * Name = ok
p Label | Name * form = ok
p Label | match arm into Number = ok
p Label | match arm into Decimal = ok
p Label | match arm into Text = ok
p Label | match arm into Boolean = ok
p Label | match arm into Date = ok
p Label | match arm into Color = ok
p Label | match arm into Widget = ok
o Qty | assign Number = ok
o Qty | assign Decimal = ok
o Qty | assign Text = ok
o Qty | assign Boolean = ok
o Qty | assign Date = ok
o Qty | assign Color = ok
o Qty | assign Widget = ok
o Qty | create Number = ok
o Qty | create Decimal = ok
o Qty | create Text = ok
o Qty | create Boolean = ok
o Qty | create Date = ok
o Qty | create Color = ok
o Qty | create Widget = ok
o Qty | invoke Number = ok
o Qty | invoke Decimal = ok
o Qty | invoke Text = ok
o Qty | invoke Boolean = ok
o Qty | invoke Date = ok
o Qty | invoke Color = ok
o Qty | invoke Widget = ok
o Qty | call Number = ok
o Qty | call Decimal = ok
o Qty | call Text = ok
o Qty | call Boolean = ok
o Qty | call Date = ok
o Qty | call Color = ok
o Qty | call Widget = ok
o Qty | return Number = n/a
o Qty | return Decimal = n/a
o Qty | return Text = n/a
o Qty | return Boolean = n/a
o Qty | return Date = n/a
o Qty | return Color = n/a
o Qty | return Widget = n/a
o Qty | default Number = n/a
o Qty | default Decimal = n/a
o Qty | default Text = n/a
o Qty | default Boolean = n/a
o Qty | default Date = n/a
o Qty | default Color = n/a
o Qty | default Widget = n/a
o Qty | form == Number = ok
o Qty | form == Decimal = ok
o Qty | form == Text = ok
o Qty | form == Boolean = ok
o Qty | form == Date = ok
o Qty | form == Color = ok
o Qty | form == Widget = ok
o Qty | form == null = ok
o Qty | Number == form = ok
o Qty | Decimal == form = ok
o Qty | Text == form = ok
o Qty | Boolean == form = ok
o Qty | Date == form = ok
o Qty | Color == form = ok
o Qty | Widget == form = ok
o Qty | null == form = ok
o Qty | if form = ok
o Qty | for where form = ok
o Qty | not form = ok
o Qty | form and Flag = ok
o Qty | match { form -> 1 } = ok
o Qty | form * N = ok
o Qty | N * form = ok
o Qty | form * Name = ok
o Qty | Name * form = ok
o Qty | match arm into Number = ok
o Qty | match arm into Decimal = ok
o Qty | match arm into Text = ok
o Qty | match arm into Boolean = ok
o Qty | match arm into Date = ok
o Qty | match arm into Color = ok
o Qty | match arm into Widget = ok
o Label | assign Number = ok
o Label | assign Decimal = ok
o Label | assign Text = ok
o Label | assign Boolean = ok
o Label | assign Date = ok
o Label | assign Color = ok
o Label | assign Widget = ok
o Label | create Number = ok
o Label | create Decimal = ok
o Label | create Text = ok
o Label | create Boolean = ok
o Label | create Date = ok
o Label | create Color = ok
o Label | create Widget = ok
o Label | invoke Number = ok
o Label | invoke Decimal = ok
o Label | invoke Text = ok
o Label | invoke Boolean = ok
o Label | invoke Date = ok
o Label | invoke Color = ok
o Label | invoke Widget = ok
o Label | call Number = ok
o Label | call Decimal = ok
o Label | call Text = ok
o Label | call Boolean = ok
o Label | call Date = ok
o Label | call Color = ok
o Label | call Widget = ok
o Label | return Number = n/a
o Label | return Decimal = n/a
o Label | return Text = n/a
o Label | return Boolean = n/a
o Label | return Date = n/a
o Label | return Color = n/a
o Label | return Widget = n/a
o Label | default Number = n/a
o Label | default Decimal = n/a
o Label | default Text = n/a
o Label | default Boolean = n/a
o Label | default Date = n/a
o Label | default Color = n/a
o Label | default Widget = n/a
o Label | form == Number = ok
o Label | form == Decimal = ok
o Label | form == Text = ok
o Label | form == Boolean = ok
o Label | form == Date = ok
o Label | form == Color = ok
o Label | form == Widget = ok
o Label | form == null = ok
o Label | Number == form = ok
o Label | Decimal == form = ok
o Label | Text == form = ok
o Label | Boolean == form = ok
o Label | Date == form = ok
o Label | Color == form = ok
o Label | Widget == form = ok
o Label | null == form = ok
o Label | if form = ok
o Label | for where form = ok
o Label | not form = ok
o Label | form and Flag = ok
o Label | match { form -> 1 } = ok
o Label | form * N = ok
o Label | N * form = ok
o Label | form * Name = ok
o Label | Name * form = ok
o Label | match arm into Number = ok
o Label | match arm into Decimal = ok
o Label | match arm into Text = ok
o Label | match arm into Boolean = ok
o Label | match arm into Date = ok
o Label | match arm into Color = ok
o Label | match arm into Widget = ok
count parts | assign Number = ok
count parts | assign Decimal = ok
count parts | assign Text = DMSEM003
count parts | assign Boolean = DMSEM003
count parts | assign Date = DMSEM003
count parts | assign Color = DMSEM003
count parts | assign Widget = ok
count parts | create Number = ok
count parts | create Decimal = ok
count parts | create Text = DMSEM003
count parts | create Boolean = DMSEM003
count parts | create Date = DMSEM003
count parts | create Color = DMSEM003
count parts | create Widget = ok
count parts | invoke Number = ok
count parts | invoke Decimal = ok
count parts | invoke Text = DMSEM003
count parts | invoke Boolean = DMSEM003
count parts | invoke Date = DMSEM003
count parts | invoke Color = DMSEM003
count parts | invoke Widget = ok
count parts | call Number = ok
count parts | call Decimal = ok
count parts | call Text = DMFUN003
count parts | call Boolean = DMFUN003
count parts | call Date = DMFUN003
count parts | call Color = DMFUN003
count parts | call Widget = ok
count parts | return Number = n/a
count parts | return Decimal = n/a
count parts | return Text = n/a
count parts | return Boolean = n/a
count parts | return Date = n/a
count parts | return Color = n/a
count parts | return Widget = n/a
count parts | default Number = ok
count parts | default Decimal = ok
count parts | default Text = ok
count parts | default Boolean = ok
count parts | default Date = ok
count parts | default Color = ok
count parts | default Widget = ok
count parts | form == Number = ok
count parts | form == Decimal = ok
count parts | form == Text = DMSEM003
count parts | form == Boolean = DMSEM003
count parts | form == Date = DMSEM003
count parts | form == Color = DMSEM003
count parts | form == Widget = ok
count parts | form == null = DMSEM003
count parts | Number == form = ok
count parts | Decimal == form = ok
count parts | Text == form = DMSEM003
count parts | Boolean == form = DMSEM003
count parts | Date == form = DMSEM003
count parts | Color == form = DMSEM003
count parts | Widget == form = ok
count parts | null == form = DMSEM003
count parts | if form = DMSEM003
count parts | for where form = DMSEM003
count parts | not form = DMSEM003
count parts | form and Flag = DMSEM003
count parts | match { form -> 1 } = DMMAT001
count parts | form * N = ok
count parts | N * form = ok
count parts | form * Name = DMSEM003
count parts | Name * form = DMSEM003
count parts | match arm into Number = ok
count parts | match arm into Decimal = ok
count parts | match arm into Text = DMMAT002+DMSEM003
count parts | match arm into Boolean = DMMAT002+DMSEM003
count parts | match arm into Date = DMMAT002+DMSEM003
count parts | match arm into Color = DMMAT002+DMSEM003
count parts | match arm into Widget = ok
sum parts Qty | assign Number = ok
sum parts Qty | assign Decimal = ok
sum parts Qty | assign Text = DMSEM003
sum parts Qty | assign Boolean = DMSEM003
sum parts Qty | assign Date = DMSEM003
sum parts Qty | assign Color = DMSEM003
sum parts Qty | assign Widget = ok
sum parts Qty | create Number = ok
sum parts Qty | create Decimal = ok
sum parts Qty | create Text = DMSEM003
sum parts Qty | create Boolean = DMSEM003
sum parts Qty | create Date = DMSEM003
sum parts Qty | create Color = DMSEM003
sum parts Qty | create Widget = ok
sum parts Qty | invoke Number = ok
sum parts Qty | invoke Decimal = ok
sum parts Qty | invoke Text = DMSEM003
sum parts Qty | invoke Boolean = DMSEM003
sum parts Qty | invoke Date = DMSEM003
sum parts Qty | invoke Color = DMSEM003
sum parts Qty | invoke Widget = ok
sum parts Qty | call Number = ok
sum parts Qty | call Decimal = ok
sum parts Qty | call Text = DMFUN003
sum parts Qty | call Boolean = DMFUN003
sum parts Qty | call Date = DMFUN003
sum parts Qty | call Color = DMFUN003
sum parts Qty | call Widget = ok
sum parts Qty | return Number = n/a
sum parts Qty | return Decimal = n/a
sum parts Qty | return Text = n/a
sum parts Qty | return Boolean = n/a
sum parts Qty | return Date = n/a
sum parts Qty | return Color = n/a
sum parts Qty | return Widget = n/a
sum parts Qty | default Number = ok
sum parts Qty | default Decimal = ok
sum parts Qty | default Text = ok
sum parts Qty | default Boolean = ok
sum parts Qty | default Date = ok
sum parts Qty | default Color = ok
sum parts Qty | default Widget = ok
sum parts Qty | form == Number = ok
sum parts Qty | form == Decimal = ok
sum parts Qty | form == Text = DMSEM003
sum parts Qty | form == Boolean = DMSEM003
sum parts Qty | form == Date = DMSEM003
sum parts Qty | form == Color = DMSEM003
sum parts Qty | form == Widget = ok
sum parts Qty | form == null = DMSEM003
sum parts Qty | Number == form = ok
sum parts Qty | Decimal == form = ok
sum parts Qty | Text == form = DMSEM003
sum parts Qty | Boolean == form = DMSEM003
sum parts Qty | Date == form = DMSEM003
sum parts Qty | Color == form = DMSEM003
sum parts Qty | Widget == form = ok
sum parts Qty | null == form = DMSEM003
sum parts Qty | if form = DMSEM003
sum parts Qty | for where form = DMSEM003
sum parts Qty | not form = DMSEM003
sum parts Qty | form and Flag = DMSEM003
sum parts Qty | match { form -> 1 } = DMMAT001
sum parts Qty | form * N = ok
sum parts Qty | N * form = ok
sum parts Qty | form * Name = DMSEM003
sum parts Qty | Name * form = DMSEM003
sum parts Qty | match arm into Number = ok
sum parts Qty | match arm into Decimal = ok
sum parts Qty | match arm into Text = DMMAT002+DMSEM003
sum parts Qty | match arm into Boolean = DMMAT002+DMSEM003
sum parts Qty | match arm into Date = DMMAT002+DMSEM003
sum parts Qty | match arm into Color = DMMAT002+DMSEM003
sum parts Qty | match arm into Widget = ok
sum parts Price | assign Number = ok
sum parts Price | assign Decimal = ok
sum parts Price | assign Text = DMSEM003
sum parts Price | assign Boolean = DMSEM003
sum parts Price | assign Date = DMSEM003
sum parts Price | assign Color = DMSEM003
sum parts Price | assign Widget = ok
sum parts Price | create Number = ok
sum parts Price | create Decimal = ok
sum parts Price | create Text = DMSEM003
sum parts Price | create Boolean = DMSEM003
sum parts Price | create Date = DMSEM003
sum parts Price | create Color = DMSEM003
sum parts Price | create Widget = ok
sum parts Price | invoke Number = ok
sum parts Price | invoke Decimal = ok
sum parts Price | invoke Text = DMSEM003
sum parts Price | invoke Boolean = DMSEM003
sum parts Price | invoke Date = DMSEM003
sum parts Price | invoke Color = DMSEM003
sum parts Price | invoke Widget = ok
sum parts Price | call Number = ok
sum parts Price | call Decimal = ok
sum parts Price | call Text = DMFUN003
sum parts Price | call Boolean = DMFUN003
sum parts Price | call Date = DMFUN003
sum parts Price | call Color = DMFUN003
sum parts Price | call Widget = ok
sum parts Price | return Number = n/a
sum parts Price | return Decimal = n/a
sum parts Price | return Text = n/a
sum parts Price | return Boolean = n/a
sum parts Price | return Date = n/a
sum parts Price | return Color = n/a
sum parts Price | return Widget = n/a
sum parts Price | default Number = ok
sum parts Price | default Decimal = ok
sum parts Price | default Text = ok
sum parts Price | default Boolean = ok
sum parts Price | default Date = ok
sum parts Price | default Color = ok
sum parts Price | default Widget = ok
sum parts Price | form == Number = ok
sum parts Price | form == Decimal = ok
sum parts Price | form == Text = DMSEM003
sum parts Price | form == Boolean = DMSEM003
sum parts Price | form == Date = DMSEM003
sum parts Price | form == Color = DMSEM003
sum parts Price | form == Widget = ok
sum parts Price | form == null = DMSEM003
sum parts Price | Number == form = ok
sum parts Price | Decimal == form = ok
sum parts Price | Text == form = DMSEM003
sum parts Price | Boolean == form = DMSEM003
sum parts Price | Date == form = DMSEM003
sum parts Price | Color == form = DMSEM003
sum parts Price | Widget == form = ok
sum parts Price | null == form = DMSEM003
sum parts Price | if form = DMSEM003
sum parts Price | for where form = DMSEM003
sum parts Price | not form = DMSEM003
sum parts Price | form and Flag = DMSEM003
sum parts Price | match { form -> 1 } = DMMAT001
sum parts Price | form * N = ok
sum parts Price | N * form = ok
sum parts Price | form * Name = DMSEM003
sum parts Price | Name * form = DMSEM003
sum parts Price | match arm into Number = ok
sum parts Price | match arm into Decimal = ok
sum parts Price | match arm into Text = DMMAT002+DMSEM003
sum parts Price | match arm into Boolean = DMMAT002+DMSEM003
sum parts Price | match arm into Date = DMMAT002+DMSEM003
sum parts Price | match arm into Color = DMMAT002+DMSEM003
sum parts Price | match arm into Widget = ok
Cost Amount | assign Number = DMSTR006
Cost Amount | assign Decimal = DMSTR006
Cost Amount | assign Text = DMSTR006
Cost Amount | assign Boolean = DMSTR006
Cost Amount | assign Date = DMSTR006
Cost Amount | assign Color = DMSTR006
Cost Amount | assign Widget = DMSTR006
Cost Amount | create Number = DMSTR006
Cost Amount | create Decimal = DMSTR006
Cost Amount | create Text = DMSTR006
Cost Amount | create Boolean = DMSTR006
Cost Amount | create Date = DMSTR006
Cost Amount | create Color = DMSTR006
Cost Amount | create Widget = DMSTR006
Cost Amount | invoke Number = DMSTR006
Cost Amount | invoke Decimal = DMSTR006
Cost Amount | invoke Text = DMSTR006
Cost Amount | invoke Boolean = DMSTR006
Cost Amount | invoke Date = DMSTR006
Cost Amount | invoke Color = DMSTR006
Cost Amount | invoke Widget = DMSTR006
Cost Amount | call Number = DMSTR006
Cost Amount | call Decimal = DMSTR006
Cost Amount | call Text = DMSTR006
Cost Amount | call Boolean = DMSTR006
Cost Amount | call Date = DMSTR006
Cost Amount | call Color = DMSTR006
Cost Amount | call Widget = DMSTR006
Cost Amount | return Number = n/a
Cost Amount | return Decimal = n/a
Cost Amount | return Text = n/a
Cost Amount | return Boolean = n/a
Cost Amount | return Date = n/a
Cost Amount | return Color = n/a
Cost Amount | return Widget = n/a
Cost Amount | default Number = DMSTR006
Cost Amount | default Decimal = DMSTR006
Cost Amount | default Text = DMSTR006
Cost Amount | default Boolean = DMSTR006
Cost Amount | default Date = DMSTR006
Cost Amount | default Color = DMSTR006
Cost Amount | default Widget = DMSTR006
Cost Amount | form == Number = DMSTR006
Cost Amount | form == Decimal = DMSTR006
Cost Amount | form == Text = DMSTR006
Cost Amount | form == Boolean = DMSTR006
Cost Amount | form == Date = DMSTR006
Cost Amount | form == Color = DMSTR006
Cost Amount | form == Widget = DMSTR006
Cost Amount | form == null = DMSTR006
Cost Amount | Number == form = DMSTR006
Cost Amount | Decimal == form = DMSTR006
Cost Amount | Text == form = DMSTR006
Cost Amount | Boolean == form = DMSTR006
Cost Amount | Date == form = DMSTR006
Cost Amount | Color == form = DMSTR006
Cost Amount | Widget == form = DMSTR006
Cost Amount | null == form = DMSTR006
Cost Amount | if form = DMSTR006
Cost Amount | for where form = DMSTR006
Cost Amount | not form = DMSTR006
Cost Amount | form and Flag = DMSTR006
Cost Amount | match { form -> 1 } = DMSTR006
Cost Amount | form * N = DMSTR006
Cost Amount | N * form = DMSTR006
Cost Amount | form * Name = DMSTR006
Cost Amount | Name * form = DMSTR006
Cost Amount | match arm into Number = DMSTR006
Cost Amount | match arm into Decimal = DMSTR006
Cost Amount | match arm into Text = DMSTR006
Cost Amount | match arm into Boolean = DMSTR006
Cost Amount | match arm into Date = DMSTR006
Cost Amount | match arm into Color = DMSTR006
Cost Amount | match arm into Widget = DMSTR006
match { Flag -> N + M, else -> N } | assign Number = ok
match { Flag -> N + M, else -> N } | assign Decimal = ok
match { Flag -> N + M, else -> N } | assign Text = ok
match { Flag -> N + M, else -> N } | assign Boolean = ok
match { Flag -> N + M, else -> N } | assign Date = ok
match { Flag -> N + M, else -> N } | assign Color = ok
match { Flag -> N + M, else -> N } | assign Widget = ok
match { Flag -> N + M, else -> N } | create Number = ok
match { Flag -> N + M, else -> N } | create Decimal = ok
match { Flag -> N + M, else -> N } | create Text = ok
match { Flag -> N + M, else -> N } | create Boolean = ok
match { Flag -> N + M, else -> N } | create Date = ok
match { Flag -> N + M, else -> N } | create Color = ok
match { Flag -> N + M, else -> N } | create Widget = ok
match { Flag -> N + M, else -> N } | invoke Number = ok
match { Flag -> N + M, else -> N } | invoke Decimal = ok
match { Flag -> N + M, else -> N } | invoke Text = ok
match { Flag -> N + M, else -> N } | invoke Boolean = ok
match { Flag -> N + M, else -> N } | invoke Date = ok
match { Flag -> N + M, else -> N } | invoke Color = ok
match { Flag -> N + M, else -> N } | invoke Widget = ok
match { Flag -> N + M, else -> N } | call Number = ok
match { Flag -> N + M, else -> N } | call Decimal = ok
match { Flag -> N + M, else -> N } | call Text = ok
match { Flag -> N + M, else -> N } | call Boolean = ok
match { Flag -> N + M, else -> N } | call Date = ok
match { Flag -> N + M, else -> N } | call Color = ok
match { Flag -> N + M, else -> N } | call Widget = ok
match { Flag -> N + M, else -> N } | return Number = DMFUN005
match { Flag -> N + M, else -> N } | return Decimal = DMFUN005
match { Flag -> N + M, else -> N } | return Text = DMFUN005
match { Flag -> N + M, else -> N } | return Boolean = DMFUN005
match { Flag -> N + M, else -> N } | return Date = DMFUN005
match { Flag -> N + M, else -> N } | return Color = DMFUN005
match { Flag -> N + M, else -> N } | return Widget = DMFUN005
match { Flag -> N + M, else -> N } | default Number = ok
match { Flag -> N + M, else -> N } | default Decimal = ok
match { Flag -> N + M, else -> N } | default Text = ok
match { Flag -> N + M, else -> N } | default Boolean = ok
match { Flag -> N + M, else -> N } | default Date = ok
match { Flag -> N + M, else -> N } | default Color = ok
match { Flag -> N + M, else -> N } | default Widget = ok
match { Flag -> N + M, else -> N } | form == Number = ok
match { Flag -> N + M, else -> N } | form == Decimal = ok
match { Flag -> N + M, else -> N } | form == Text = ok
match { Flag -> N + M, else -> N } | form == Boolean = ok
match { Flag -> N + M, else -> N } | form == Date = ok
match { Flag -> N + M, else -> N } | form == Color = ok
match { Flag -> N + M, else -> N } | form == Widget = ok
match { Flag -> N + M, else -> N } | form == null = ok
match { Flag -> N + M, else -> N } | Number == form = ok
match { Flag -> N + M, else -> N } | Decimal == form = ok
match { Flag -> N + M, else -> N } | Text == form = ok
match { Flag -> N + M, else -> N } | Boolean == form = ok
match { Flag -> N + M, else -> N } | Date == form = ok
match { Flag -> N + M, else -> N } | Color == form = ok
match { Flag -> N + M, else -> N } | Widget == form = ok
match { Flag -> N + M, else -> N } | null == form = ok
match { Flag -> N + M, else -> N } | if form = ok
match { Flag -> N + M, else -> N } | for where form = ok
match { Flag -> N + M, else -> N } | not form = ok
match { Flag -> N + M, else -> N } | form and Flag = ok
match { Flag -> N + M, else -> N } | match { form -> 1 } = ok
match { Flag -> N + M, else -> N } | form * N = ok
match { Flag -> N + M, else -> N } | N * form = ok
match { Flag -> N + M, else -> N } | form * Name = ok
match { Flag -> N + M, else -> N } | Name * form = ok
match { Flag -> N + M, else -> N } | match arm into Number = ok
match { Flag -> N + M, else -> N } | match arm into Decimal = ok
match { Flag -> N + M, else -> N } | match arm into Text = ok
match { Flag -> N + M, else -> N } | match arm into Boolean = ok
match { Flag -> N + M, else -> N } | match arm into Date = ok
match { Flag -> N + M, else -> N } | match arm into Color = ok
match { Flag -> N + M, else -> N } | match arm into Widget = ok
G(N) | assign Number = ok
G(N) | assign Decimal = ok
G(N) | assign Text = DMSEM003
G(N) | assign Boolean = DMSEM003
G(N) | assign Date = DMSEM003
G(N) | assign Color = DMSEM003
G(N) | assign Widget = ok
G(N) | create Number = ok
G(N) | create Decimal = ok
G(N) | create Text = DMSEM003
G(N) | create Boolean = DMSEM003
G(N) | create Date = DMSEM003
G(N) | create Color = DMSEM003
G(N) | create Widget = ok
G(N) | invoke Number = ok
G(N) | invoke Decimal = ok
G(N) | invoke Text = DMSEM003
G(N) | invoke Boolean = DMSEM003
G(N) | invoke Date = DMSEM003
G(N) | invoke Color = DMSEM003
G(N) | invoke Widget = ok
G(N) | call Number = ok
G(N) | call Decimal = ok
G(N) | call Text = DMFUN003
G(N) | call Boolean = DMFUN003
G(N) | call Date = DMFUN003
G(N) | call Color = DMFUN003
G(N) | call Widget = ok
G(N) | return Number = ok
G(N) | return Decimal = ok
G(N) | return Text = DMFUN006
G(N) | return Boolean = DMFUN006
G(N) | return Date = DMFUN006
G(N) | return Color = DMFUN006
G(N) | return Widget = ok
G(N) | default Number = DMFUN008
G(N) | default Decimal = DMFUN008
G(N) | default Text = DMFUN008
G(N) | default Boolean = DMFUN008
G(N) | default Date = DMFUN008
G(N) | default Color = DMFUN008
G(N) | default Widget = DMFUN008
G(N) | form == Number = ok
G(N) | form == Decimal = ok
G(N) | form == Text = DMSEM003
G(N) | form == Boolean = DMSEM003
G(N) | form == Date = DMSEM003
G(N) | form == Color = DMSEM003
G(N) | form == Widget = ok
G(N) | form == null = DMSEM003
G(N) | Number == form = ok
G(N) | Decimal == form = ok
G(N) | Text == form = DMSEM003
G(N) | Boolean == form = DMSEM003
G(N) | Date == form = DMSEM003
G(N) | Color == form = DMSEM003
G(N) | Widget == form = ok
G(N) | null == form = DMSEM003
G(N) | if form = DMSEM003
G(N) | for where form = DMSEM003
G(N) | not form = DMSEM003
G(N) | form and Flag = DMSEM003
G(N) | match { form -> 1 } = DMMAT001
G(N) | form * N = ok
G(N) | N * form = ok
G(N) | form * Name = DMSEM003
G(N) | Name * form = DMSEM003
G(N) | match arm into Number = ok
G(N) | match arm into Decimal = ok
G(N) | match arm into Text = DMMAT002+DMSEM003
G(N) | match arm into Boolean = DMMAT002+DMSEM003
G(N) | match arm into Date = DMMAT002+DMSEM003
G(N) | match arm into Color = DMMAT002+DMSEM003
G(N) | match arm into Widget = ok
""";

        public const string DateOperationOverTypelessDate = """
case = verdict
control assign Due to Due + 5 Days = ok
assign Due to part Due + 5 Days = DMSEM003
assign Due to (Due + A) + 5 Days = DMSEM003
assign Stamp to (Stamp + A) + 5 Hours = DMSEM003
policy Due < part Due + 5 Days = DMSEM003
policy part Due + 5 Days < Due = DMSEM003
policy (Due + A) + 5 Days < Due = DMSEM003
assign Due to Stamp + 1 = ok
assign Stamp to Due + 1 = ok
assign A to part Due + 5 Days = DMSEM003
""";

        public const string FormGridF3 = """
form | position = verdict
N + M | policy form == Number = ok
N + M | policy form == Text = ok
N + M | policy form == null = ok
N + M | policy form != null = ok
N + M | require form == Number = ok
N + M | require form == Text = ok
N + M | stage require form == Text = ok
N + M | onEntry assign Number = ok
N + M | onEntry assign Text = ok
C + C | policy form == Number = ok
C + C | policy form == Text = ok
C + C | policy form == null = ok
C + C | policy form != null = ok
C + C | require form == Number = ok
C + C | require form == Text = ok
C + C | stage require form == Text = ok
C + C | onEntry assign Number = ok
C + C | onEntry assign Text = ok
Due + N | policy form == Number = ok
Due + N | policy form == Text = ok
Due + N | policy form == null = ok
Due + N | policy form != null = ok
Due + N | require form == Number = ok
Due + N | require form == Text = ok
Due + N | stage require form == Text = ok
Due + N | onEntry assign Number = ok
Due + N | onEntry assign Text = ok
part Qty | policy form == Number = ok
part Qty | policy form == Text = ok
part Qty | policy form == null = ok
part Qty | policy form != null = ok
part Qty | require form == Number = ok
part Qty | require form == Text = ok
part Qty | stage require form == Text = ok
part Qty | onEntry assign Number = ok
part Qty | onEntry assign Text = ok
part Label | policy form == Number = ok
part Label | policy form == Text = ok
part Label | policy form == null = ok
part Label | policy form != null = ok
part Label | require form == Number = ok
part Label | require form == Text = ok
part Label | stage require form == Text = ok
part Label | onEntry assign Number = ok
part Label | onEntry assign Text = ok
part Due | policy form == Number = ok
part Due | policy form == Text = ok
part Due | policy form == null = ok
part Due | policy form != null = ok
part Due | require form == Number = ok
part Due | require form == Text = ok
part Due | stage require form == Text = ok
part Due | onEntry assign Number = ok
part Due | onEntry assign Text = ok
part Tone | policy form == Number = ok
part Tone | policy form == Text = ok
part Tone | policy form == null = ok
part Tone | policy form != null = ok
part Tone | require form == Number = ok
part Tone | require form == Text = ok
part Tone | stage require form == Text = ok
part Tone | onEntry assign Number = ok
part Tone | onEntry assign Text = ok
p Qty | policy form == Number = n/a
p Qty | policy form == Text = n/a
p Qty | policy form == null = n/a
p Qty | policy form != null = n/a
p Qty | require form == Number = n/a
p Qty | require form == Text = n/a
p Qty | stage require form == Text = n/a
p Qty | onEntry assign Number = ok
p Qty | onEntry assign Text = ok
p Label | policy form == Number = n/a
p Label | policy form == Text = n/a
p Label | policy form == null = n/a
p Label | policy form != null = n/a
p Label | require form == Number = n/a
p Label | require form == Text = n/a
p Label | stage require form == Text = n/a
p Label | onEntry assign Number = ok
p Label | onEntry assign Text = ok
o Qty | policy form == Number = n/a
o Qty | policy form == Text = n/a
o Qty | policy form == null = n/a
o Qty | policy form != null = n/a
o Qty | require form == Number = n/a
o Qty | require form == Text = n/a
o Qty | stage require form == Text = n/a
o Qty | onEntry assign Number = n/a
o Qty | onEntry assign Text = n/a
o Label | policy form == Number = n/a
o Label | policy form == Text = n/a
o Label | policy form == null = n/a
o Label | policy form != null = n/a
o Label | require form == Number = n/a
o Label | require form == Text = n/a
o Label | stage require form == Text = n/a
o Label | onEntry assign Number = n/a
o Label | onEntry assign Text = n/a
count parts | policy form == Number = ok
count parts | policy form == Text = DMSEM003
count parts | policy form == null = DMSEM003
count parts | policy form != null = DMSEM003
count parts | require form == Number = ok
count parts | require form == Text = DMSEM003
count parts | stage require form == Text = DMSEM003
count parts | onEntry assign Number = ok
count parts | onEntry assign Text = DMSEM003
sum parts Qty | policy form == Number = ok
sum parts Qty | policy form == Text = DMSEM003
sum parts Qty | policy form == null = DMSEM003
sum parts Qty | policy form != null = DMSEM003
sum parts Qty | require form == Number = ok
sum parts Qty | require form == Text = DMSEM003
sum parts Qty | stage require form == Text = DMSEM003
sum parts Qty | onEntry assign Number = ok
sum parts Qty | onEntry assign Text = DMSEM003
sum parts Price | policy form == Number = ok
sum parts Price | policy form == Text = DMSEM003
sum parts Price | policy form == null = DMSEM003
sum parts Price | policy form != null = DMSEM003
sum parts Price | require form == Number = ok
sum parts Price | require form == Text = DMSEM003
sum parts Price | stage require form == Text = DMSEM003
sum parts Price | onEntry assign Number = ok
sum parts Price | onEntry assign Text = DMSEM003
Cost Amount | policy form == Number = DMSTR006
Cost Amount | policy form == Text = DMSTR006
Cost Amount | policy form == null = DMSTR006
Cost Amount | policy form != null = DMSTR006
Cost Amount | require form == Number = DMSTR006
Cost Amount | require form == Text = DMSTR006
Cost Amount | stage require form == Text = DMSTR006
Cost Amount | onEntry assign Number = DMSTR006
Cost Amount | onEntry assign Text = DMSTR006
match { Flag -> N + M, else -> N } | policy form == Number = ok
match { Flag -> N + M, else -> N } | policy form == Text = ok
match { Flag -> N + M, else -> N } | policy form == null = ok
match { Flag -> N + M, else -> N } | policy form != null = ok
match { Flag -> N + M, else -> N } | require form == Number = ok
match { Flag -> N + M, else -> N } | require form == Text = ok
match { Flag -> N + M, else -> N } | stage require form == Text = ok
match { Flag -> N + M, else -> N } | onEntry assign Number = ok
match { Flag -> N + M, else -> N } | onEntry assign Text = ok
G(N) | policy form == Number = ok
G(N) | policy form == Text = DMSEM003
G(N) | policy form == null = DMSEM003
G(N) | policy form != null = DMSEM003
G(N) | require form == Number = ok
G(N) | require form == Text = DMSEM003
G(N) | stage require form == Text = DMSEM003
G(N) | onEntry assign Number = ok
G(N) | onEntry assign Text = DMSEM003
N (control) | assign Number = ok
N (control) | assign Decimal = ok
N (control) | assign Text = DMSEM003
N (control) | assign Boolean = DMSEM003
N (control) | assign Date = DMSEM003
N (control) | assign Color = DMSEM003
N (control) | assign Widget = ok
N (control) | create Number = ok
N (control) | create Decimal = ok
N (control) | create Text = DMSEM003
N (control) | create Boolean = DMSEM003
N (control) | create Date = DMSEM003
N (control) | create Color = DMSEM003
N (control) | create Widget = ok
N (control) | invoke Number = ok
N (control) | invoke Decimal = ok
N (control) | invoke Text = DMSEM003
N (control) | invoke Boolean = DMSEM003
N (control) | invoke Date = DMSEM003
N (control) | invoke Color = DMSEM003
N (control) | invoke Widget = ok
N (control) | call Number = ok
N (control) | call Decimal = ok
N (control) | call Text = DMFUN003
N (control) | call Boolean = DMFUN003
N (control) | call Date = DMFUN003
N (control) | call Color = DMFUN003
N (control) | call Widget = ok
N (control) | return Number = ok
N (control) | return Decimal = ok
N (control) | return Text = DMFUN006
N (control) | return Boolean = DMFUN006
N (control) | return Date = DMFUN006
N (control) | return Color = DMFUN006
N (control) | return Widget = ok
N (control) | default Number = DMSEM003
N (control) | default Decimal = DMSEM003
N (control) | default Text = DMSEM003
N (control) | default Boolean = DMSEM003
N (control) | default Date = DMSEM003
N (control) | default Color = DMSEM003
N (control) | default Widget = ok
N (control) | form == Number = ok
N (control) | form == Decimal = ok
N (control) | form == Text = DMSEM003
N (control) | form == Boolean = DMSEM003
N (control) | form == Date = DMSEM003
N (control) | form == Color = DMSEM003
N (control) | form == Widget = ok
N (control) | form == null = DMSEM003
N (control) | Number == form = ok
N (control) | Decimal == form = ok
N (control) | Text == form = DMSEM003
N (control) | Boolean == form = DMSEM003
N (control) | Date == form = DMSEM003
N (control) | Color == form = DMSEM003
N (control) | Widget == form = ok
N (control) | null == form = DMSEM003
N (control) | if form = DMSEM003
N (control) | for where form = DMSEM003
N (control) | not form = DMSEM003
N (control) | form and Flag = DMSEM003
N (control) | match { form -> 1 } = DMMAT001
N (control) | form * N = ok
N (control) | N * form = ok
N (control) | form * Name = DMSEM003
N (control) | Name * form = DMSEM003
N (control) | match arm into Number = ok
N (control) | match arm into Decimal = ok
N (control) | match arm into Text = DMMAT002+DMSEM003
N (control) | match arm into Boolean = DMMAT002+DMSEM003
N (control) | match arm into Date = DMMAT002+DMSEM003
N (control) | match arm into Color = DMMAT002+DMSEM003
N (control) | match arm into Widget = ok
N (control) | policy form == Number = ok
N (control) | policy form == Text = DMSEM003
N (control) | policy form == null = DMSEM003
N (control) | policy form != null = DMSEM003
N (control) | require form == Number = ok
N (control) | require form == Text = DMSEM003
N (control) | stage require form == Text = DMSEM003
N (control) | onEntry assign Number = ok
N (control) | onEntry assign Text = DMSEM003
part book Qty | assign Number = ok
part book Qty | assign Decimal = ok
part book Qty | assign Text = ok
part book Qty | assign Boolean = ok
part book Qty | assign Date = ok
part book Qty | assign Color = ok
part book Qty | assign Widget = ok
part book Qty | create Number = ok
part book Qty | create Decimal = ok
part book Qty | create Text = ok
part book Qty | create Boolean = ok
part book Qty | create Date = ok
part book Qty | create Color = ok
part book Qty | create Widget = ok
part book Qty | invoke Number = ok
part book Qty | invoke Decimal = ok
part book Qty | invoke Text = ok
part book Qty | invoke Boolean = ok
part book Qty | invoke Date = ok
part book Qty | invoke Color = ok
part book Qty | invoke Widget = ok
part book Qty | call Number = ok
part book Qty | call Decimal = ok
part book Qty | call Text = ok
part book Qty | call Boolean = ok
part book Qty | call Date = ok
part book Qty | call Color = ok
part book Qty | call Widget = ok
part book Qty | return Number = n/a
part book Qty | return Decimal = n/a
part book Qty | return Text = n/a
part book Qty | return Boolean = n/a
part book Qty | return Date = n/a
part book Qty | return Color = n/a
part book Qty | return Widget = n/a
part book Qty | default Number = ok
part book Qty | default Decimal = ok
part book Qty | default Text = ok
part book Qty | default Boolean = ok
part book Qty | default Date = ok
part book Qty | default Color = ok
part book Qty | default Widget = ok
part book Qty | form == Number = ok
part book Qty | form == Decimal = ok
part book Qty | form == Text = ok
part book Qty | form == Boolean = ok
part book Qty | form == Date = ok
part book Qty | form == Color = ok
part book Qty | form == Widget = ok
part book Qty | form == null = ok
part book Qty | Number == form = ok
part book Qty | Decimal == form = ok
part book Qty | Text == form = ok
part book Qty | Boolean == form = ok
part book Qty | Date == form = ok
part book Qty | Color == form = ok
part book Qty | Widget == form = ok
part book Qty | null == form = ok
part book Qty | if form = ok
part book Qty | for where form = ok
part book Qty | not form = ok
part book Qty | form and Flag = ok
part book Qty | match { form -> 1 } = ok
part book Qty | form * N = ok
part book Qty | N * form = ok
part book Qty | form * Name = ok
part book Qty | Name * form = ok
part book Qty | match arm into Number = ok
part book Qty | match arm into Decimal = ok
part book Qty | match arm into Text = ok
part book Qty | match arm into Boolean = ok
part book Qty | match arm into Date = ok
part book Qty | match arm into Color = ok
part book Qty | match arm into Widget = ok
part book Qty | policy form == Number = ok
part book Qty | policy form == Text = ok
part book Qty | policy form == null = ok
part book Qty | policy form != null = ok
part book Qty | require form == Number = ok
part book Qty | require form == Text = ok
part book Qty | stage require form == Text = ok
part book Qty | onEntry assign Number = ok
part book Qty | onEntry assign Text = ok
p book Qty | assign Number = ok
p book Qty | assign Decimal = ok
p book Qty | assign Text = ok
p book Qty | assign Boolean = ok
p book Qty | assign Date = ok
p book Qty | assign Color = ok
p book Qty | assign Widget = ok
p book Qty | create Number = ok
p book Qty | create Decimal = ok
p book Qty | create Text = ok
p book Qty | create Boolean = ok
p book Qty | create Date = ok
p book Qty | create Color = ok
p book Qty | create Widget = ok
p book Qty | invoke Number = ok
p book Qty | invoke Decimal = ok
p book Qty | invoke Text = ok
p book Qty | invoke Boolean = ok
p book Qty | invoke Date = ok
p book Qty | invoke Color = ok
p book Qty | invoke Widget = ok
p book Qty | call Number = ok
p book Qty | call Decimal = ok
p book Qty | call Text = ok
p book Qty | call Boolean = ok
p book Qty | call Date = ok
p book Qty | call Color = ok
p book Qty | call Widget = ok
p book Qty | return Number = n/a
p book Qty | return Decimal = n/a
p book Qty | return Text = n/a
p book Qty | return Boolean = n/a
p book Qty | return Date = n/a
p book Qty | return Color = n/a
p book Qty | return Widget = n/a
p book Qty | default Number = n/a
p book Qty | default Decimal = n/a
p book Qty | default Text = n/a
p book Qty | default Boolean = n/a
p book Qty | default Date = n/a
p book Qty | default Color = n/a
p book Qty | default Widget = n/a
p book Qty | form == Number = ok
p book Qty | form == Decimal = ok
p book Qty | form == Text = ok
p book Qty | form == Boolean = ok
p book Qty | form == Date = ok
p book Qty | form == Color = ok
p book Qty | form == Widget = ok
p book Qty | form == null = ok
p book Qty | Number == form = ok
p book Qty | Decimal == form = ok
p book Qty | Text == form = ok
p book Qty | Boolean == form = ok
p book Qty | Date == form = ok
p book Qty | Color == form = ok
p book Qty | Widget == form = ok
p book Qty | null == form = ok
p book Qty | if form = ok
p book Qty | for where form = ok
p book Qty | not form = ok
p book Qty | form and Flag = ok
p book Qty | match { form -> 1 } = ok
p book Qty | form * N = ok
p book Qty | N * form = ok
p book Qty | form * Name = ok
p book Qty | Name * form = ok
p book Qty | match arm into Number = ok
p book Qty | match arm into Decimal = ok
p book Qty | match arm into Text = ok
p book Qty | match arm into Boolean = ok
p book Qty | match arm into Date = ok
p book Qty | match arm into Color = ok
p book Qty | match arm into Widget = ok
p book Qty | policy form == Number = n/a
p book Qty | policy form == Text = n/a
p book Qty | policy form == null = n/a
p book Qty | policy form != null = n/a
p book Qty | require form == Number = n/a
p book Qty | require form == Text = n/a
p book Qty | stage require form == Text = n/a
p book Qty | onEntry assign Number = ok
p book Qty | onEntry assign Text = ok
part Opt | assign Number = ok
part Opt | assign Decimal = ok
part Opt | assign Text = ok
part Opt | assign Boolean = ok
part Opt | assign Date = ok
part Opt | assign Color = ok
part Opt | assign Widget = ok
part Opt | create Number = ok
part Opt | create Decimal = ok
part Opt | create Text = ok
part Opt | create Boolean = ok
part Opt | create Date = ok
part Opt | create Color = ok
part Opt | create Widget = ok
part Opt | invoke Number = ok
part Opt | invoke Decimal = ok
part Opt | invoke Text = ok
part Opt | invoke Boolean = ok
part Opt | invoke Date = ok
part Opt | invoke Color = ok
part Opt | invoke Widget = ok
part Opt | call Number = ok
part Opt | call Decimal = ok
part Opt | call Text = ok
part Opt | call Boolean = ok
part Opt | call Date = ok
part Opt | call Color = ok
part Opt | call Widget = ok
part Opt | return Number = n/a
part Opt | return Decimal = n/a
part Opt | return Text = n/a
part Opt | return Boolean = n/a
part Opt | return Date = n/a
part Opt | return Color = n/a
part Opt | return Widget = n/a
part Opt | default Number = ok
part Opt | default Decimal = ok
part Opt | default Text = ok
part Opt | default Boolean = ok
part Opt | default Date = ok
part Opt | default Color = ok
part Opt | default Widget = ok
part Opt | form == Number = ok
part Opt | form == Decimal = ok
part Opt | form == Text = ok
part Opt | form == Boolean = ok
part Opt | form == Date = ok
part Opt | form == Color = ok
part Opt | form == Widget = ok
part Opt | form == null = ok
part Opt | Number == form = ok
part Opt | Decimal == form = ok
part Opt | Text == form = ok
part Opt | Boolean == form = ok
part Opt | Date == form = ok
part Opt | Color == form = ok
part Opt | Widget == form = ok
part Opt | null == form = ok
part Opt | if form = ok
part Opt | for where form = ok
part Opt | not form = ok
part Opt | form and Flag = ok
part Opt | match { form -> 1 } = ok
part Opt | form * N = ok
part Opt | N * form = ok
part Opt | form * Name = ok
part Opt | Name * form = ok
part Opt | match arm into Number = ok
part Opt | match arm into Decimal = ok
part Opt | match arm into Text = ok
part Opt | match arm into Boolean = ok
part Opt | match arm into Date = ok
part Opt | match arm into Color = ok
part Opt | match arm into Widget = ok
part Opt | policy form == Number = ok
part Opt | policy form == Text = ok
part Opt | policy form == null = ok
part Opt | policy form != null = ok
part Opt | require form == Number = ok
part Opt | require form == Text = ok
part Opt | stage require form == Text = ok
part Opt | onEntry assign Number = ok
part Opt | onEntry assign Text = ok
part Owner | assign Number = ok
part Owner | assign Decimal = ok
part Owner | assign Text = ok
part Owner | assign Boolean = ok
part Owner | assign Date = ok
part Owner | assign Color = ok
part Owner | assign Widget = ok
part Owner | create Number = ok
part Owner | create Decimal = ok
part Owner | create Text = ok
part Owner | create Boolean = ok
part Owner | create Date = ok
part Owner | create Color = ok
part Owner | create Widget = ok
part Owner | invoke Number = ok
part Owner | invoke Decimal = ok
part Owner | invoke Text = ok
part Owner | invoke Boolean = ok
part Owner | invoke Date = ok
part Owner | invoke Color = ok
part Owner | invoke Widget = ok
part Owner | call Number = ok
part Owner | call Decimal = ok
part Owner | call Text = ok
part Owner | call Boolean = ok
part Owner | call Date = ok
part Owner | call Color = ok
part Owner | call Widget = ok
part Owner | return Number = n/a
part Owner | return Decimal = n/a
part Owner | return Text = n/a
part Owner | return Boolean = n/a
part Owner | return Date = n/a
part Owner | return Color = n/a
part Owner | return Widget = n/a
part Owner | default Number = ok
part Owner | default Decimal = ok
part Owner | default Text = ok
part Owner | default Boolean = ok
part Owner | default Date = ok
part Owner | default Color = ok
part Owner | default Widget = ok
part Owner | form == Number = ok
part Owner | form == Decimal = ok
part Owner | form == Text = ok
part Owner | form == Boolean = ok
part Owner | form == Date = ok
part Owner | form == Color = ok
part Owner | form == Widget = ok
part Owner | form == null = ok
part Owner | Number == form = ok
part Owner | Decimal == form = ok
part Owner | Text == form = ok
part Owner | Boolean == form = ok
part Owner | Date == form = ok
part Owner | Color == form = ok
part Owner | Widget == form = ok
part Owner | null == form = ok
part Owner | if form = ok
part Owner | for where form = ok
part Owner | not form = ok
part Owner | form and Flag = ok
part Owner | match { form -> 1 } = ok
part Owner | form * N = ok
part Owner | N * form = ok
part Owner | form * Name = ok
part Owner | Name * form = ok
part Owner | match arm into Number = ok
part Owner | match arm into Decimal = ok
part Owner | match arm into Text = ok
part Owner | match arm into Boolean = ok
part Owner | match arm into Date = ok
part Owner | match arm into Color = ok
part Owner | match arm into Widget = ok
part Owner | policy form == Number = ok
part Owner | policy form == Text = ok
part Owner | policy form == null = ok
part Owner | policy form != null = ok
part Owner | require form == Number = ok
part Owner | require form == Text = ok
part Owner | stage require form == Text = ok
part Owner | onEntry assign Number = ok
part Owner | onEntry assign Text = ok
part Cash | assign Number = DMSTR006
part Cash | assign Decimal = DMSTR006
part Cash | assign Text = DMSTR006
part Cash | assign Boolean = DMSTR006
part Cash | assign Date = DMSTR006
part Cash | assign Color = DMSTR006
part Cash | assign Widget = DMSTR006
part Cash | create Number = DMSTR006
part Cash | create Decimal = DMSTR006
part Cash | create Text = DMSTR006
part Cash | create Boolean = DMSTR006
part Cash | create Date = DMSTR006
part Cash | create Color = DMSTR006
part Cash | create Widget = DMSTR006
part Cash | invoke Number = DMSTR006
part Cash | invoke Decimal = DMSTR006
part Cash | invoke Text = DMSTR006
part Cash | invoke Boolean = DMSTR006
part Cash | invoke Date = DMSTR006
part Cash | invoke Color = DMSTR006
part Cash | invoke Widget = DMSTR006
part Cash | call Number = DMSTR006
part Cash | call Decimal = DMSTR006
part Cash | call Text = DMSTR006
part Cash | call Boolean = DMSTR006
part Cash | call Date = DMSTR006
part Cash | call Color = DMSTR006
part Cash | call Widget = DMSTR006
part Cash | return Number = n/a
part Cash | return Decimal = n/a
part Cash | return Text = n/a
part Cash | return Boolean = n/a
part Cash | return Date = n/a
part Cash | return Color = n/a
part Cash | return Widget = n/a
part Cash | default Number = DMSTR006
part Cash | default Decimal = DMSTR006
part Cash | default Text = DMSTR006
part Cash | default Boolean = DMSTR006
part Cash | default Date = DMSTR006
part Cash | default Color = DMSTR006
part Cash | default Widget = DMSTR006
part Cash | form == Number = DMSTR006
part Cash | form == Decimal = DMSTR006
part Cash | form == Text = DMSTR006
part Cash | form == Boolean = DMSTR006
part Cash | form == Date = DMSTR006
part Cash | form == Color = DMSTR006
part Cash | form == Widget = DMSTR006
part Cash | form == null = DMSTR006
part Cash | Number == form = DMSTR006
part Cash | Decimal == form = DMSTR006
part Cash | Text == form = DMSTR006
part Cash | Boolean == form = DMSTR006
part Cash | Date == form = DMSTR006
part Cash | Color == form = DMSTR006
part Cash | Widget == form = DMSTR006
part Cash | null == form = DMSTR006
part Cash | if form = DMSTR006
part Cash | for where form = DMSTR006
part Cash | not form = DMSTR006
part Cash | form and Flag = DMSTR006
part Cash | match { form -> 1 } = DMSTR006
part Cash | form * N = DMSTR006
part Cash | N * form = DMSTR006
part Cash | form * Name = DMSTR006
part Cash | Name * form = DMSTR006
part Cash | match arm into Number = DMSTR006
part Cash | match arm into Decimal = DMSTR006
part Cash | match arm into Text = DMSTR006
part Cash | match arm into Boolean = DMSTR006
part Cash | match arm into Date = DMSTR006
part Cash | match arm into Color = DMSTR006
part Cash | match arm into Widget = DMSTR006
part Cash | policy form == Number = DMSTR006
part Cash | policy form == Text = DMSTR006
part Cash | policy form == null = DMSTR006
part Cash | policy form != null = DMSTR006
part Cash | require form == Number = DMSTR006
part Cash | require form == Text = DMSTR006
part Cash | stage require form == Text = DMSTR006
part Cash | onEntry assign Number = DMSTR006
part Cash | onEntry assign Text = DMSTR006
match { Flag -> N, else -> N + M } | assign Number = ok
match { Flag -> N, else -> N + M } | assign Decimal = ok
match { Flag -> N, else -> N + M } | assign Text = ok
match { Flag -> N, else -> N + M } | assign Boolean = ok
match { Flag -> N, else -> N + M } | assign Date = ok
match { Flag -> N, else -> N + M } | assign Color = ok
match { Flag -> N, else -> N + M } | assign Widget = ok
match { Flag -> N, else -> N + M } | create Number = ok
match { Flag -> N, else -> N + M } | create Decimal = ok
match { Flag -> N, else -> N + M } | create Text = ok
match { Flag -> N, else -> N + M } | create Boolean = ok
match { Flag -> N, else -> N + M } | create Date = ok
match { Flag -> N, else -> N + M } | create Color = ok
match { Flag -> N, else -> N + M } | create Widget = ok
match { Flag -> N, else -> N + M } | invoke Number = ok
match { Flag -> N, else -> N + M } | invoke Decimal = ok
match { Flag -> N, else -> N + M } | invoke Text = ok
match { Flag -> N, else -> N + M } | invoke Boolean = ok
match { Flag -> N, else -> N + M } | invoke Date = ok
match { Flag -> N, else -> N + M } | invoke Color = ok
match { Flag -> N, else -> N + M } | invoke Widget = ok
match { Flag -> N, else -> N + M } | call Number = ok
match { Flag -> N, else -> N + M } | call Decimal = ok
match { Flag -> N, else -> N + M } | call Text = ok
match { Flag -> N, else -> N + M } | call Boolean = ok
match { Flag -> N, else -> N + M } | call Date = ok
match { Flag -> N, else -> N + M } | call Color = ok
match { Flag -> N, else -> N + M } | call Widget = ok
match { Flag -> N, else -> N + M } | return Number = DMFUN005
match { Flag -> N, else -> N + M } | return Decimal = DMFUN005
match { Flag -> N, else -> N + M } | return Text = DMFUN005
match { Flag -> N, else -> N + M } | return Boolean = DMFUN005
match { Flag -> N, else -> N + M } | return Date = DMFUN005
match { Flag -> N, else -> N + M } | return Color = DMFUN005
match { Flag -> N, else -> N + M } | return Widget = DMFUN005
match { Flag -> N, else -> N + M } | default Number = ok
match { Flag -> N, else -> N + M } | default Decimal = ok
match { Flag -> N, else -> N + M } | default Text = ok
match { Flag -> N, else -> N + M } | default Boolean = ok
match { Flag -> N, else -> N + M } | default Date = ok
match { Flag -> N, else -> N + M } | default Color = ok
match { Flag -> N, else -> N + M } | default Widget = ok
match { Flag -> N, else -> N + M } | form == Number = ok
match { Flag -> N, else -> N + M } | form == Decimal = ok
match { Flag -> N, else -> N + M } | form == Text = ok
match { Flag -> N, else -> N + M } | form == Boolean = ok
match { Flag -> N, else -> N + M } | form == Date = ok
match { Flag -> N, else -> N + M } | form == Color = ok
match { Flag -> N, else -> N + M } | form == Widget = ok
match { Flag -> N, else -> N + M } | form == null = ok
match { Flag -> N, else -> N + M } | Number == form = ok
match { Flag -> N, else -> N + M } | Decimal == form = ok
match { Flag -> N, else -> N + M } | Text == form = ok
match { Flag -> N, else -> N + M } | Boolean == form = ok
match { Flag -> N, else -> N + M } | Date == form = ok
match { Flag -> N, else -> N + M } | Color == form = ok
match { Flag -> N, else -> N + M } | Widget == form = ok
match { Flag -> N, else -> N + M } | null == form = ok
match { Flag -> N, else -> N + M } | if form = ok
match { Flag -> N, else -> N + M } | for where form = ok
match { Flag -> N, else -> N + M } | not form = ok
match { Flag -> N, else -> N + M } | form and Flag = ok
match { Flag -> N, else -> N + M } | match { form -> 1 } = ok
match { Flag -> N, else -> N + M } | form * N = ok
match { Flag -> N, else -> N + M } | N * form = ok
match { Flag -> N, else -> N + M } | form * Name = ok
match { Flag -> N, else -> N + M } | Name * form = ok
match { Flag -> N, else -> N + M } | match arm into Number = ok
match { Flag -> N, else -> N + M } | match arm into Decimal = ok
match { Flag -> N, else -> N + M } | match arm into Text = ok
match { Flag -> N, else -> N + M } | match arm into Boolean = ok
match { Flag -> N, else -> N + M } | match arm into Date = ok
match { Flag -> N, else -> N + M } | match arm into Color = ok
match { Flag -> N, else -> N + M } | match arm into Widget = ok
match { Flag -> N, else -> N + M } | policy form == Number = ok
match { Flag -> N, else -> N + M } | policy form == Text = ok
match { Flag -> N, else -> N + M } | policy form == null = ok
match { Flag -> N, else -> N + M } | policy form != null = ok
match { Flag -> N, else -> N + M } | require form == Number = ok
match { Flag -> N, else -> N + M } | require form == Text = ok
match { Flag -> N, else -> N + M } | stage require form == Text = ok
match { Flag -> N, else -> N + M } | onEntry assign Number = ok
match { Flag -> N, else -> N + M } | onEntry assign Text = ok
match { Flag -> null, else -> N + M } | assign Number = ok
match { Flag -> null, else -> N + M } | assign Decimal = ok
match { Flag -> null, else -> N + M } | assign Text = ok
match { Flag -> null, else -> N + M } | assign Boolean = ok
match { Flag -> null, else -> N + M } | assign Date = ok
match { Flag -> null, else -> N + M } | assign Color = ok
match { Flag -> null, else -> N + M } | assign Widget = ok
match { Flag -> null, else -> N + M } | create Number = ok
match { Flag -> null, else -> N + M } | create Decimal = ok
match { Flag -> null, else -> N + M } | create Text = ok
match { Flag -> null, else -> N + M } | create Boolean = ok
match { Flag -> null, else -> N + M } | create Date = ok
match { Flag -> null, else -> N + M } | create Color = ok
match { Flag -> null, else -> N + M } | create Widget = ok
match { Flag -> null, else -> N + M } | invoke Number = ok
match { Flag -> null, else -> N + M } | invoke Decimal = ok
match { Flag -> null, else -> N + M } | invoke Text = ok
match { Flag -> null, else -> N + M } | invoke Boolean = ok
match { Flag -> null, else -> N + M } | invoke Date = ok
match { Flag -> null, else -> N + M } | invoke Color = ok
match { Flag -> null, else -> N + M } | invoke Widget = ok
match { Flag -> null, else -> N + M } | call Number = ok
match { Flag -> null, else -> N + M } | call Decimal = ok
match { Flag -> null, else -> N + M } | call Text = ok
match { Flag -> null, else -> N + M } | call Boolean = ok
match { Flag -> null, else -> N + M } | call Date = ok
match { Flag -> null, else -> N + M } | call Color = ok
match { Flag -> null, else -> N + M } | call Widget = ok
match { Flag -> null, else -> N + M } | return Number = DMFUN005
match { Flag -> null, else -> N + M } | return Decimal = DMFUN005
match { Flag -> null, else -> N + M } | return Text = DMFUN005
match { Flag -> null, else -> N + M } | return Boolean = DMFUN005
match { Flag -> null, else -> N + M } | return Date = DMFUN005
match { Flag -> null, else -> N + M } | return Color = DMFUN005
match { Flag -> null, else -> N + M } | return Widget = DMFUN005
match { Flag -> null, else -> N + M } | default Number = ok
match { Flag -> null, else -> N + M } | default Decimal = ok
match { Flag -> null, else -> N + M } | default Text = ok
match { Flag -> null, else -> N + M } | default Boolean = ok
match { Flag -> null, else -> N + M } | default Date = ok
match { Flag -> null, else -> N + M } | default Color = ok
match { Flag -> null, else -> N + M } | default Widget = ok
match { Flag -> null, else -> N + M } | form == Number = ok
match { Flag -> null, else -> N + M } | form == Decimal = ok
match { Flag -> null, else -> N + M } | form == Text = ok
match { Flag -> null, else -> N + M } | form == Boolean = ok
match { Flag -> null, else -> N + M } | form == Date = ok
match { Flag -> null, else -> N + M } | form == Color = ok
match { Flag -> null, else -> N + M } | form == Widget = ok
match { Flag -> null, else -> N + M } | form == null = ok
match { Flag -> null, else -> N + M } | Number == form = ok
match { Flag -> null, else -> N + M } | Decimal == form = ok
match { Flag -> null, else -> N + M } | Text == form = ok
match { Flag -> null, else -> N + M } | Boolean == form = ok
match { Flag -> null, else -> N + M } | Date == form = ok
match { Flag -> null, else -> N + M } | Color == form = ok
match { Flag -> null, else -> N + M } | Widget == form = ok
match { Flag -> null, else -> N + M } | null == form = ok
match { Flag -> null, else -> N + M } | if form = ok
match { Flag -> null, else -> N + M } | for where form = ok
match { Flag -> null, else -> N + M } | not form = ok
match { Flag -> null, else -> N + M } | form and Flag = ok
match { Flag -> null, else -> N + M } | match { form -> 1 } = ok
match { Flag -> null, else -> N + M } | form * N = ok
match { Flag -> null, else -> N + M } | N * form = ok
match { Flag -> null, else -> N + M } | form * Name = ok
match { Flag -> null, else -> N + M } | Name * form = ok
match { Flag -> null, else -> N + M } | match arm into Number = ok
match { Flag -> null, else -> N + M } | match arm into Decimal = ok
match { Flag -> null, else -> N + M } | match arm into Text = ok
match { Flag -> null, else -> N + M } | match arm into Boolean = ok
match { Flag -> null, else -> N + M } | match arm into Date = ok
match { Flag -> null, else -> N + M } | match arm into Color = ok
match { Flag -> null, else -> N + M } | match arm into Widget = ok
match { Flag -> null, else -> N + M } | policy form == Number = ok
match { Flag -> null, else -> N + M } | policy form == Text = ok
match { Flag -> null, else -> N + M } | policy form == null = ok
match { Flag -> null, else -> N + M } | policy form != null = ok
match { Flag -> null, else -> N + M } | require form == Number = ok
match { Flag -> null, else -> N + M } | require form == Text = ok
match { Flag -> null, else -> N + M } | stage require form == Text = ok
match { Flag -> null, else -> N + M } | onEntry assign Number = ok
match { Flag -> null, else -> N + M } | onEntry assign Text = ok
""";

        public const string DateDifference = """
case = verdict
control (Due - Due) == (Due - Due) = ok
(part Due - Due) == (Due - Due) = ok
(Due - part Due) == (Due - Due) = ok
(part Due - part Due) == (Due - Due) = ok
((Due + A) - Due) == (Due - Due) = ok
(Due - (Due + A)) == (Due - Due) = ok
control assign Gap to Stamp - Stamp = ok
assign Gap to part Stamp - Stamp = ok
assign Gap to (Stamp + A) - Stamp = ok
for p in parts { assign Gap to p Stamp - Stamp } = ok
mixed assign Gap to part Stamp - Due = ok
mixed control assign Gap to Stamp - Due = DMSEM003
library assign Gap to Now - Stamp = DMSEM003
library (Today - Due) == (Due - Due) = DMSEM003
library assign Gap to (Stamp + 1 Days) - Stamp = DMSEM003
""";

        public const string OptionalNull = """
case = verdict
Qty is not null = DMSEM003
Qty is null = DMSEM003
Qty == null = DMSEM003
part Qty is not null = ok
part Qty is null = ok
part Qty == null = ok
null == part Qty = ok
for p in parts where p Qty is not null = ok
""";

        public const string SumPropertyKindsF3 = """
sum parts X, X: <type> [override] | position = verdict
Int | policy sum > 0 = DMSEM005
Int | assign Number = DMSEM005
Int | assign Decimal = DMSEM005
Int64 | policy sum > 0 = DMSEM005
Int64 | assign Number = DMSEM005
Int64 | assign Decimal = DMSEM005
Double | policy sum > 0 = DMSEM005
Double | assign Number = DMSEM005
Double | assign Decimal = DMSEM005
Number [Number -> int] | policy sum > 0 = ok
Number [Number -> int] | assign Number = ok
Number [Number -> int] | assign Decimal = ok
Decimal [Decimal -> double] | policy sum > 0 = ok
Decimal [Decimal -> double] | assign Number = ok
Decimal [Decimal -> double] | assign Decimal = ok
Double [Double -> float] | policy sum > 0 = DMSEM005
Double [Double -> float] | assign Number = DMSEM005
Double [Double -> float] | assign Decimal = DMSEM005
Int [Int -> short] | policy sum > 0 = DMSEM005
Int [Int -> short] | assign Number = DMSEM005
Int [Int -> short] | assign Decimal = DMSEM005
""";

        public const string SumPropertyKinds = """
sum parts X, X: <type> | position = verdict
Number | policy sum > 0 = ok
Number | assign Number = ok
Number | assign Decimal = ok
Decimal | policy sum > 0 = ok
Decimal | assign Number = ok
Decimal | assign Decimal = ok
Cents | policy sum > 0 = DMSEM005
Cents | assign Number = ok
Cents | assign Decimal = ok
Id64 | policy sum > 0 = DMSEM005
Id64 | assign Number = ok
Id64 | assign Decimal = ok
Int32 | policy sum > 0 = DMSEM005
Int32 | assign Number = DMSEM005
Int32 | assign Decimal = DMSEM005
Float | policy sum > 0 = DMSEM005
Float | assign Number = DMSEM005
Float | assign Decimal = DMSEM005
long | policy sum > 0 = DMSEM005
long | assign Number = DMSEM005
long | assign Decimal = DMSEM005
decimal | policy sum > 0 = DMSEM005
decimal | assign Number = DMSEM005
decimal | assign Decimal = DMSEM005
Text | policy sum > 0 = DMSEM005
Text | assign Number = ok
Text | assign Decimal = ok
Boolean | policy sum > 0 = DMSEM005
Boolean | assign Number = ok
Boolean | assign Decimal = ok
Color | policy sum > 0 = DMSEM005
Color | assign Number = ok
Color | assign Decimal = ok
Money | policy sum > 0 = DMSEM005+DMSTR006
Money | assign Number = DMSTR006
Money | assign Decimal = DMSTR006
Bin | policy sum > 0 = DMSEM005
Bin | assign Number = ok
Bin | assign Decimal = ok
Nope | policy sum > 0 = DMSEM005
Nope | assign Number = DMSEM005
Nope | assign Decimal = DMSEM005
""";

        public const string ImportedContractNameClash = """
local Money { Amount: Number } and imported Money { Code: Text } | case = verdict
assign P:Money := Q:Money = DMCON001+DMSTR006
assign N := Q Amount (owned) = DMCON001+DMSTR006
assign N := Q Code (owned) = DMCON001+DMSTR006
assign Name := Q Amount (owned) = DMCON001+DMSTR006
""";
    }
}