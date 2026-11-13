using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using static Poly.Tests.DomainModeling.Parsing.ModelKit;

using Action = Poly.DomainModeling.Ontology.Action;
using Duration = Poly.DomainModeling.Libraries.Temporal.Duration;
using Not = Poly.DomainModeling.Ontology.Not;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>Which session a matrix cell prints and parses through.</summary>
internal enum CellSession {
    /// <summary>No extensions: <c>DomainSession.ForExtensions([])</c>.</summary>
    Bare,

    /// <summary>The authoring session (<c>ExtensionCatalog.Core.Authoring</c>): the SQL <c>column</c>/<c>table</c> annotations are registered.</summary>
    Authoring,

    /// <summary>The temporal extension: durations (<c>12 days</c>) print and parse.</summary>
    Temporal,
}

/// <summary>
/// One cell of the round-trip matrix: a domain built in code, the session it goes through, and the enumeration
/// members it covers (<paramref name="Covers"/>, the strings the reflection guard compares with the model's members:
/// <c>RelationshipCardinality.OneToMany</c>, <c>Effect.AssignEffect</c>, <c>Constraint.LengthConstraint</c>, <c>Annotation.iri</c>).
/// </summary>
internal sealed record MatrixCell(string Id, string Dimension, CellSession Session, Func<Domain> Build, IReadOnlyList<string> Covers);

/// <summary>
/// The cells of the round-trip matrix (G-26a, amendment 10), generated from the model's enumerations by reflection where an
/// enumeration exists (cardinality, visibility, quantifier, comparison, endpoint kinds, durations, primitive types) and from the
/// registered kinds elsewhere (effect subtypes, constraint kinds, annotation keywords). Navigation crosses cardinality × owned × <c>via</c>
/// (16); keyword-named targets are crossed with cardinality ONLY; every other dimension takes one factor at a time.
/// </summary>
internal static class RoundTripMatrixCells {
    private static readonly Lazy<IReadOnlyList<MatrixCell>> Cached = new(Generate);

    internal static IReadOnlyList<MatrixCell> All => Cached.Value;

    /// <summary>The keyword-named target names the keyword-target dimension uses (after the usability precheck).</summary>
    internal static IReadOnlyList<string> KeywordTargets => KeywordTargetsCached.Value;

    private static readonly Lazy<IReadOnlyList<string>> KeywordTargetsCached = new(ComputeKeywordTargets);

    private static string Cov<T>(T member) where T : struct, Enum => typeof(T).Name + "." + member;

    // ── building blocks ──────────────────────────────────────────────────────────────

    private static Entity WithNavs(Entity entity, params Relationship[] navigations) => entity with { Navigations = navigations };

    private static Domain NavDomain(string target, RelationshipCardinality cardinality, bool owned, string? via) =>
        Dom(Ent(target, [Prop("Name")]),
            WithNavs(Ent("A", [Prop("Name")]), Nav("A", "b", target, cardinality, owned, via)));

    private static Entity ActionHost(params Effect[] effects) =>
        WithNavs(Ent("A", [Prop("Name"), Prop("Total", "Number")], [Act("Go", effects)]), Nav("A", "bs", "B", RelationshipCardinality.OneToMany));

    private static Domain HostDomain(params Effect[] effects) => Dom(Ent("B", [Prop("Name")]), ActionHost(effects));

    private static DomainExpression Positive() => DomainExpression.GreaterThan(Prp("Total"), Num(0));

    private static PropertyBinding Bind(string name, DomainExpression value) => new(name, value);

    // ── generation ───────────────────────────────────────────────────────────────────

    private static IReadOnlyList<MatrixCell> Generate() {
        var cells = new List<MatrixCell>();

        void Add(string dimension, string id, Func<Domain> build, CellSession session = CellSession.Bare, params string[] covers) =>
            cells.Add(new MatrixCell(dimension + "/" + id, dimension, session, build, covers));

        // navigation: cardinality × owned × via
        foreach (var cardinality in Enum.GetValues<RelationshipCardinality>())
            foreach (var owned in new[] { false, true })
                foreach (var via in new string?[] { null, "a" })
                    Add("nav", $"{cardinality}/{(owned ? "owned" : "plain")}/{(via is null ? "novia" : "via")}",
                        () => NavDomain("B", cardinality, owned, via), covers: [Cov(cardinality)]);

        // keyword-named targets × cardinality ONLY
        foreach (var target in KeywordTargets)
            foreach (var cardinality in Enum.GetValues<RelationshipCardinality>())
                Add("keyword-target", $"{target}/{cardinality}", () => NavDomain(target, cardinality, owned: false, via: null), covers: [Cov(cardinality)]);

        // visibility: on entity, value type and action
        foreach (var visibility in Enum.GetValues<Visibility>()) {
            Add("visibility", $"entity/{visibility}", () => Dom(Ent("A", [Prop("Name")]) with { Visibility = visibility }), covers: [Cov(visibility)]);
            Add("visibility", $"value/{visibility}", () => Dom(Val("V", [Prop("X", "Number")], visibility)), covers: [Cov(visibility)]);
            Add("visibility", $"action/{visibility}", () => Dom(Ent("A", [Prop("Name")], [Act("Go") with { Visibility = visibility }])), covers: [Cov(visibility)]);
        }

        // require / require not, on a stage and on an action
        Add("require", "stage/positive", () => StageRequire(negated: false));
        Add("require", "stage/negated", () => StageRequire(negated: true));
        Add("require", "action/positive", () => ActionRequire(positive: true, negated: false));
        Add("require", "action/negated", () => ActionRequire(positive: false, negated: true));
        Add("require", "action/both", () => ActionRequire(positive: true, negated: true));

        // comparison kinds, as an entity policy
        foreach (var kind in Enum.GetValues<ComparisonKind>())
            Add("comparison", kind.ToString(), () => Dom(Ent("A", [Prop("Total", "Number")], policies: [Pol("Ok", new Comparison(Prp("Total"), Num(1), kind))])), covers: [Cov(kind)]);

        // subscription quantifiers, on a stage and on the entity; and the other shapes, one factor at a time
        foreach (var quantifier in Enum.GetValues<StageSubscriptionQuantifier>()) {
            Add("quantifier", $"stage/{quantifier}", () => SubscriptionDomain(stageLevel: true, quantifier, ["Done"], peerBinding: null), covers: [Cov(quantifier)]);
            Add("quantifier", $"entity/{quantifier}", () => SubscriptionDomain(stageLevel: false, quantifier, ["Done"], peerBinding: null), covers: [Cov(quantifier)]);
        }

        Add("subscription", "stage/binder", () => SubscriptionDomain(true, StageSubscriptionQuantifier.Each, ["Done"], "b"));
        Add("subscription", "entity/binder", () => SubscriptionDomain(false, StageSubscriptionQuantifier.Each, ["Done"], "b"));
        Add("subscription", "stage/two-stages", () => SubscriptionDomain(true, StageSubscriptionQuantifier.Each, ["Draft", "Done"], null));
        Add("subscription", "entity/two-stages", () => SubscriptionDomain(false, StageSubscriptionQuantifier.Each, ["Draft", "Done"], null));
        Add("subscription", "stage/any-binder-two-stages", () => SubscriptionDomain(true, StageSubscriptionQuantifier.Any, ["Draft", "Done"], "b"));

        // effect kinds, in an action body (stage transition in a stage action)
        Add("effect", "Assign", () => HostDomain(Assign("Name", DomainExpression.Literal("x"))), covers: ["Effect.AssignEffect"]);
        Add("effect", "StageTransition", () => Dom(Ent("A", [Prop("Name")], stages: [Stg("Draft", [Act("Go", To("Done"))]), Stg("Done")])), covers: ["Effect.StageTransitionEffect"]);
        Add("effect", "Conditional/then", () => HostDomain(new ConditionalEffect(Positive(), [Assign("Name", DomainExpression.Literal("x"))], null)), covers: ["Effect.ConditionalEffect"]);
        Add("effect", "Conditional/else", () => HostDomain(new ConditionalEffect(Positive(), [Assign("Name", DomainExpression.Literal("x"))], [Assign("Name", DomainExpression.Literal("y"))])), covers: ["Effect.ConditionalEffect"]);
        Add("effect", "Conditional/else-if", () => HostDomain(new ConditionalEffect(Positive(), [Assign("Name", DomainExpression.Literal("x"))],
            [new ConditionalEffect(DomainExpression.LessThan(Prp("Total"), Num(0)), [Assign("Name", DomainExpression.Literal("y"))], [Assign("Name", DomainExpression.Literal("z"))])])), covers: ["Effect.ConditionalEffect"]);
        Add("effect", "CreateEntityInstance", () => HostDomain(new CreateEntityInstance(Ref("B"), [Bind("Name", DomainExpression.Literal("x"))])), covers: ["Effect.CreateEntityInstance"]);
        Add("effect", "CreateEntityInstance/empty", () => HostDomain(new CreateEntityInstance(Ref("B"))), covers: ["Effect.CreateEntityInstance"]);
        Add("effect", "CreateEntityInRelationship", () => HostDomain(new CreateEntityInRelationshipEffect("bs", [Bind("Name", DomainExpression.Literal("x"))])), covers: ["Effect.CreateEntityInRelationshipEffect"]);
        Add("effect", "ForEach", () => HostDomain(new ForEachEffect("b", "bs", null, [Assign("Name", DomainExpression.Literal("y"))])), covers: ["Effect.ForEachEffect"]);
        Add("effect", "ForEach/where", () => HostDomain(new ForEachEffect("b", "bs", DomainExpression.Equal(Prp("Name"), DomainExpression.Literal("x")), [Assign("Name", DomainExpression.Literal("y"))])), covers: ["Effect.ForEachEffect"]);
        Add("effect", "ForEachInvoke/named-policy", () => HostDomain(new ForEachInvokeEffect("bs", "c", new ForEachNamedPolicy("Ok"), "Go", [Bind("p", Num(1))])), covers: ["Effect.ForEachInvokeEffect"]);
        Add("effect", "ForEachInvoke/stage-membership", () => HostDomain(new ForEachInvokeEffect("bs", "d", new ForEachStageMembership("Done"), "Go", [])), covers: ["Effect.ForEachInvokeEffect"]);
        Add("effect", "ForEachInvoke/no-predicate", () => HostDomain(new ForEachInvokeEffect("bs", "e", null, "Go", [])), covers: ["Effect.ForEachInvokeEffect"]);
        Add("effect", "InvokeAction", () => HostDomain(new InvokeActionEffect("Go", [])), covers: ["Effect.InvokeActionEffect"]);
        Add("effect", "InvokeAction/relationship-bindings", () => HostDomain(new InvokeActionEffect("Go", [Bind("p", Num(2))], "bs")), covers: ["Effect.InvokeActionEffect"]);
        Add("effect", "Link", () => HostDomain(new LinkEffect("bs", Prp("Name"))), covers: ["Effect.LinkEffect"]);
        Add("effect", "Unlink", () => HostDomain(new UnlinkEffect("bs", Prp("Name"))), covers: ["Effect.UnlinkEffect"]);

        // constraint kinds, one per property
        void Constraint(string id, string type, Constraint constraint, string kind) =>
            Add("constraint", id, () => Dom(Ent("A", [Prop("Code", type, constraint)])), covers: ["Constraint." + kind]);
        Constraint("Length/range", "Text", new LengthConstraint(1, 5), nameof(LengthConstraint));
        Constraint("Length/exact", "Text", new LengthConstraint(3, 3), nameof(LengthConstraint));
        Constraint("Pattern", "Text", new PatternConstraint("^[A-Z]+$"), nameof(PatternConstraint));
        Constraint("Range/number", "Number", new RangeConstraint(1L, 10L), nameof(RangeConstraint));
        Constraint("Range/decimal", "Decimal", new RangeConstraint(1.5m, 9.5m), nameof(RangeConstraint));
        Constraint("Unique", "Text", new UniqueConstraint(), nameof(UniqueConstraint));
        Constraint("Optional", "Text", new OptionalConstraint(), nameof(OptionalConstraint));
        Constraint("Default/number", "Number", new DefaultValueConstraint(Num(3)), nameof(DefaultValueConstraint));
        Constraint("Default/text", "Text", new DefaultValueConstraint(DomainExpression.Literal("x")), nameof(DefaultValueConstraint));
        Constraint("Equality", "Text", new EqualityConstraint("Q"), nameof(EqualityConstraint));

        // property optionality, with and without another constraint
        Add("optionality", "required", () => Dom(Ent("A", [Prop("Name")])));
        Add("optionality", "optional", () => Dom(Ent("A", [Prop("Name", "Text", new OptionalConstraint())])));
        Add("optionality", "optional-with-length", () => Dom(Ent("A", [Prop("Name", "Text", new OptionalConstraint(), new LengthConstraint(1, 5))])));

        // key and initial designations
        Add("designation", "key/single", () => Dom(Ent("A", [Prop("Name")]) with { KeyProperties = ["Name"] }));
        Add("designation", "key/composite", () => Dom(Ent("A", [Prop("Name"), Prop("Code")]) with { KeyProperties = ["Name", "Code"] }));
        Add("designation", "initial", () => Dom(Ent("A", stages: [Stg("Draft"), Stg("Done")]) with { InitialStageName = "Done" }));
        Add("designation", "initial-and-key", () => Dom(Ent("A", [Prop("Name")], stages: [Stg("Draft"), Stg("Done")]) with { InitialStageName = "Done", KeyProperties = ["Name"] }));

        // annotation kinds
        foreach (var keyword in TermFacets.Keywords.Where(k => k is not TermFacets.Term)) {
            var value = keyword == TermFacets.Iri ? "http://x/iri" : "text";
            Add("annotation", $"{keyword}/entity", () => Dom(Ent("A", [Prop("Name")]) with { Facets = [Note(keyword, value)] }), covers: ["Annotation." + keyword]);
            Add("annotation", $"{keyword}/property", () => Dom(Ent("A", [Prop("Name") with { Facets = [Note(keyword, value)] }])), covers: ["Annotation." + keyword]);
        }

        Add("annotation", "term/enum-member", () => Dom(new EnumType("Color", ["Red", "Green"], []) { MemberFacets = new Dictionary<string, IReadOnlyList<Facet>> { ["Green"] = [Note("term", "g")] } }), covers: ["Annotation.term"]);
        Add("annotation", "column/one-argument", () => Dom(Ent("A", [Prop("Name") with { Facets = [Note("column", "N")] }])), CellSession.Authoring, "Annotation.column");
        Add("annotation", "column/two-arguments", () => Dom(Ent("A", [Prop("Name") with { Facets = [new Annotation("column", new Dictionary<string, AnnotationValue> { ["0"] = new AnnotationString("N"), ["1"] = new AnnotationString("VARCHAR2(50)") })] }])), CellSession.Authoring, "Annotation.column");
        Add("annotation", "table/entity", () => Dom(Ent("A", [Prop("Name")]) with { Facets = [Note("table", "T")] }), CellSession.Authoring, "Annotation.table");

        // stage features
        Add("stage", "plain", () => Dom(Ent("A", stages: [Stg("Draft"), Stg("Done")])));
        Add("stage", "entry", () => Dom(Ent("A", [Prop("Total", "Number")], stages: [Stg("Draft") with { OnEntryEffects = [Assign("Total", Num(1))] }])));
        Add("stage", "exit", () => Dom(Ent("A", [Prop("Total", "Number")], stages: [Stg("Draft") with { OnExitEffects = [Assign("Total", Num(1))] }, Stg("Done")])));
        Add("stage", "action", () => Dom(Ent("A", stages: [Stg("Draft", [Act("Go", To("Done"))]), Stg("Done")])));
        Add("stage", "all-features", () => SubscriptionDomain(true, StageSubscriptionQuantifier.Any, ["Done"], "b", withEntryExit: true));

        // every entity member kind in one entity (guards the member kind enumeration)
        Add("entity", "all-member-kinds", () => {
            var ok = Pol("Ok", Positive());
            return Dom(Ent("B", [Prop("Name")]),
                WithNavs(Ent("A", [Prop("Name"), Prop("Total", "Number")], [Act("Pub")], [ok], [Stg("Draft")]), Nav("A", "bs", "B", RelationshipCardinality.OneToMany)));
        }, covers: [.. Enum.GetNames<EntityMemberKind>().Select(n => "EntityMemberKind." + n)]);

        // primitive types, one property each (guards the semantics enumeration)
        foreach (var semantics in Enum.GetValues<PrimitiveSemantics>())
            Add("primitive", semantics.ToString(), () => Dom(Ent("A", [Prop("P", semantics.ToString())])), covers: [Cov(semantics)]);

        // other declarations: enum, value type, function, contracts and a binding
        Add("declaration", "enum", () => Dom(new EnumType("Color", ["Red", "Green"], [])));
        Add("declaration", "value", () => Dom(Val("V", [Prop("X", "Number"), Prop("Y", "Text")])));
        Add("declaration", "function", () => Dom(Ent("A")) with { Functions = [new DomainFunction("F", [Prop("a", "Number")], Ref("Number"), DomainExpression.Add(DomainExpression.Parameter("a"), Num(1)))] });
        Add("declaration", "function/no-parameters", () => Dom(Ent("A")) with { Functions = [new DomainFunction("Pi", [], Ref("Number"), Num(3))] });

        foreach (var kind in Enum.GetValues<ContractEndpointKind>())
            foreach (var direction in Enum.GetValues<ContractEndpointDirection>())
                Add("contract", $"endpoint/{kind}/{direction}", () => ContractDomain(ContractSourceKind.ExternalProvider, kind, direction, "stripe", "v1"), covers: [Cov(kind), Cov(direction), Cov(ContractSourceKind.ExternalProvider)]);
        Add("contract", "internal-source", () => ContractDomain(ContractSourceKind.InternalDomain, ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, "Orders", "v2"), covers: [Cov(ContractSourceKind.InternalDomain)]);
        Add("contract", "quoted-source-and-version", () => ContractDomain(ContractSourceKind.ExternalProvider, ContractEndpointKind.Event, ContractEndpointDirection.Outbound, "stripe.com", "2024-11-20"));
        Add("contract", "binding", () => ContractDomain(ContractSourceKind.ExternalProvider, ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, "stripe", "v1") with {
            ContractBindings = [new ContractBinding("Bn", "Pay", "Charge", "Go", "m", [])],
        });

        // durations, one per unit (the temporal extension)
        foreach (var unit in Enum.GetValues<DurationUnit>())
            Add("duration", unit.ToString(), () => DomainWithExtensions([ExtensionCatalog.TemporalId], Ent("A", [Prop("Wait", "Duration", new DefaultValueConstraint(new Duration(12, unit)))])), CellSession.Temporal, Cov(unit));

        return cells;
    }

    private static Domain DomainWithExtensions(IReadOnlyList<string> extensions, params DomainType[] types) => DomWith(extensions, types);

    private static Annotation Note(string keyword, string value) =>
        new(keyword, new Dictionary<string, AnnotationValue> { ["0"] = new AnnotationString(value) });

    private static Domain StageRequire(bool negated) {
        var ok = Pol("Ok", Positive());
        var requires = negated ? new Policy("not_Ok", new Not(ok.Expression)) : ok;
        return Dom(Ent("A", [Prop("Total", "Number")], policies: [ok], stages: [Stg("Draft") with { Policies = [requires] }]));
    }

    private static Domain ActionRequire(bool positive, bool negated) {
        var ok = Pol("Ok", Positive());
        var policies = new List<Policy>();
        if (positive) policies.Add(ok);
        if (negated) policies.Add(new Policy("not_Ok", new Not(ok.Expression)));
        return Dom(Ent("A", [Prop("Total", "Number")], [Act("Go") with { Policies = policies }], [ok]));
    }

    private static Domain SubscriptionDomain(bool stageLevel, StageSubscriptionQuantifier quantifier, string[] stageNames, string? peerBinding, bool withEntryExit = false) {
        var subscription = new StageSubscription("bs", stageNames, quantifier, [new InvokeActionEffect("Go", [])], peerBinding);
        var draft = Stg("Draft", [Act("Go")]);
        if (withEntryExit)
            draft = draft with { OnEntryEffects = [Assign("Total", Num(1))], OnExitEffects = [Assign("Total", Num(2))] };
        if (stageLevel)
            draft = draft with { Subscriptions = [subscription] };
        var host = WithNavs(Ent("A", [Prop("Name"), Prop("Total", "Number")], stages: [draft, Stg("Done")]), Nav("A", "bs", "B", RelationshipCardinality.OneToMany));
        if (!stageLevel)
            host = host with { Subscriptions = [subscription] };
        return Dom(Ent("B", [Prop("Name")]), host);
    }

    private static Domain ContractDomain(ContractSourceKind source, ContractEndpointKind kind, ContractEndpointDirection direction, string sourceId, string version) =>
        Dom(Ent("A", [Prop("Name")], [Act("Go")])) with {
            ImportedContracts = [Contract("Pay", source, sourceId, version,
                [new ContractEndpoint("Charge", kind, direction, Ref("Money"))], [Val("Money", [Prop("Amount", "Number")])])],
        };

    // ── keyword-named targets ────────────────────────────────────────────────────────

    /// <summary>The resolver's unsupported keywords (<c>actor</c>, <c>schedule</c>, <c>parallel</c>, <c>for</c>, <c>function</c>): compared ignoring case.</summary>
    internal static IReadOnlyList<string> UnsupportedKeywords => [.. ((IEnumerable<string>)typeof(DomainResolver)
        .GetField("_unsupportedKeywords", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null)!).Order(StringComparer.Ordinal)];

    /// <summary>
    /// The names a navigation target is crossed with: the resolver's unsupported keywords (lower case and capitalised, since the resolver compares
    /// ignoring case), every word the lexer reads as a keyword (found by spelling each <see cref="DslTokenKind"/> member name three ways and keeping
    /// those <see cref="DslTokenReader.IsIdentifier"/> refuses), and the capitalised spelling of each. A name is kept only when an entity of that name
    /// round-trips ON ITS OWN, so a failing cell is the navigation's failure and not its target's declaration.
    /// </summary>
    private static IReadOnlyList<string> ComputeKeywordTargets() {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var word in UnsupportedKeywords) {
            names.Add(word);
            names.Add(Capitalise(word));
        }

        foreach (var member in Enum.GetNames<DslTokenKind>()) {
            foreach (var spelling in new[] { member, member.ToLowerInvariant(), Capitalise(member.ToLowerInvariant()) }) {
                if (!spelling.All(char.IsLetter) || DslTokenReader.IsIdentifier(spelling))
                    continue;
                names.Add(spelling);
                names.Add(Capitalise(spelling));
            }
        }

        return [.. names.Where(IsUsableTarget)];
    }

    private static string Capitalise(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    private static bool IsUsableTarget(string name) {
        var probe = new MatrixCell("probe/" + name, "probe", CellSession.Bare, () => Dom(Ent(name, [Prop("Name")])), []);
        return RoundTripMatrixTests.Run(probe).Class == "ok";
    }
}