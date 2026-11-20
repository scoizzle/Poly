using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;

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
        ("(Qty + 1) == null", "Ok: policy { (Qty + 1) == null }"),
        ("null == (Qty + 1)", "Ok: policy { null == (Qty + 1) }"),
        ("(Qty + A) != null", "Ok: policy { (Qty + A) != null }"),
        ("required (A + 1) == null", "Ok: policy { (A + 1) == null }"),
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

}