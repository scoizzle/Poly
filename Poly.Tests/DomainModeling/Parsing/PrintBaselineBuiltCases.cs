using Poly.DomainModeling;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.Packs.Product;

using static Poly.Tests.DomainModeling.Parsing.ModelKit;

using Action = Poly.DomainModeling.Ontology.Action;
using Duration = Poly.DomainModeling.Libraries.Temporal.Duration;
using Entry = Poly.Tests.DomainModeling.Parsing.PrintBaselineTests.Entry;
using Not = Poly.DomainModeling.Ontology.Not;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Code-built print cases for the printer branches no shipped document reaches (G-26a, amendment 9 and the row's (b2)): each case is one
/// key of <c>PrintBaseline.txt</c>, <c>built/&lt;name&gt;</c>, printed straight from a code-built <see cref="Domain"/> (no parse, no analysis).
///
/// <para><b>The gap list is DERIVED.</b> The coverage command works in this repo: <c>dotnet run --project Poly.Tests -- --treenode-filter
/// "/*/*/PrintBaselineTests/*" --coverage --coverage-output-format cobertura --coverage-output &lt;file&gt;</c>. Run over the 42 documents, the 7 pack
/// fixtures and the guide fences alone it left 190 lines of <c>DomainDslPrinter*.cs</c> unreached (the baseline's pipeline analyses each document, and
/// only 31 of the 42 do, so the remainder cannot print). <see cref="Branches"/> names the unreached branch each case below covers and
/// <see cref="Unreachable"/> lists what no code-built domain reaches, with the reason. <c>key(A, B)</c> and <c>initial(S)</c> are among the unreached branches.</para>
/// </summary>
internal static class PrintBaselineBuiltCases {
    private const string PipelineBuilt = "built: print(code-built Domain)";

    private static DomainSession Bare => DomainSession.ForExtensions([]);

    /// <summary>The branch each built case covers (file and what), by key suffix.</summary>
    internal static readonly (string Case, string Branch)[] Branches = [
        ("key-composite-and-initial", "Entities.cs: `initial(S)` and `key(A, B)` header clauses"),
        ("key-single", "Entities.cs: `key(A)`"),
        ("initial-only", "Entities.cs: `initial(S)` without a key"),
        ("public-action", "Actions.cs: `public action` header"),
        ("public-action-in-stage", "Actions.cs: `public action` inside a stage"),
        ("composite-effect", "Effects.cs: Composite effect prints its parts in order"),
        ("create-in-via-instance", "Effects.cs: CreateEntityInstance with a relationship name prints `create in`"),
        ("link-and-unlink", "Effects.cs: `link R to e` and `unlink R from e`"),
        ("for-each", "Effects.cs: `for b in R { … }` without and with `where`"),
        ("stage-require-not", "Stages.cs: `require not` on a stage, and the negated StageRequire lookup"),
        ("value-types", "Types.cs, cs: public and internal value types"),
        ("stage-policy-not-a-require", "Stages.cs: a stage policy that is not a use of an entity policy cannot be printed: the printer throws"),
        ("stage-not-policy-without-entity-policy", "Stages.cs: a negated stage policy with no entity policy behind it cannot be printed: the printer throws"),
        ("facet-unregistered-annotation", "Types.cs: PrintFacet throws for an annotation no pack registered"),
        ("facet-unregistered-type", "Types.cs: PrintFacet throws for a facet type no pack registered"),
        ("annotation-registry-printer", "DomainDslPrinter.cs: the AnnotationRegistry constructor"),
        ("implicit-ids-elided", "DomainDslPrinter.cs: the implicit-ids constructor; `uses` elided when the extensions equal the implicit set"),
        ("implicit-ids-not-elided", "DomainDslPrinter.cs: the implicit-ids constructor; `uses` printed when the set differs"),
        ("uses-pinned", "DomainDslPrinter.cs: `uses id version` for a seeded, pinned contract"),
        ("literals", "DomainDslPrinter.cs: PrintLiteral for decimal, double and other values"),
        ("equality-literals", "DomainDslPrinter.cs: PrintLiteralValue for null, true, false, string, double and other values (`equals(…)`)"),
        ("required-constraint-omitted", "DomainDslPrinter.cs: a constraint with no DSL form is not printed"),
        ("expression-duration-unbound", "Expressions.cs: a Duration expression through its binder (T-10a)"),
        ("expression-date-operation", "Expressions.cs: DateOperation through its binder"),
        ("expression-kinds", "Expressions.cs: owned access, not, divide, not-exists, match"),
        ("entry/PrintConstraintText", "Constraints.cs: the public constraint text entry point"),
        ("entry/PrintExpressionText", "DomainDslPrinter.cs: the public expression text entry point"),
        ("entry/PrintTestExpression", "DomainDslPrinter.cs: the test expression entry point"),
        ("entry/PrintActionFragmentKey", "DomainDslPrinter.cs: the action fragment identity used by lowering"),
    ];

    /// <summary>
    /// Keys that freeze a result a later row should change, by key, with the owner and what is odd. A later row that fixes one re-records
    /// that key in its own commit and deletes the entry (the test checks each key exists and still shows its oddity).
    /// </summary>
    internal static readonly (string Key, string Owner, string Oddity, string Fragment)[] KnownGaps = [
        ("built/composite-effect", "G-33", "a composite effect's second and later parts lose the action indent (hand layout in the effect printer)", "\nassign Name to \"y\""),
        ("built/for-each", "G-33", "the closing brace of a `for` body sits two spaces short of its `for` line (hand layout in the effect printer)", "      assign Name to \"y\"\n  }\n"),
    ];

    /// <summary>Built keys whose printer throws BY DESIGN (fail closed): key suffix, the exception type, and why it is right.</summary>
    internal static readonly (string Case, string ExceptionType, string Reason)[] FailClosed = [
        ("facet-unregistered-annotation", nameof(FormatException), "an annotation no pack registered cannot be printed, and the printer says so rather than dropping it"),
        ("facet-unregistered-type", nameof(FormatException), "a facet type no pack registered cannot be printed"),
        ("stage-not-policy-without-entity-policy", nameof(InvalidOperationException), "a negated stage policy that is not the negation of an entity policy cannot be read back"),
        ("stage-policy-not-a-require", nameof(InvalidOperationException), "a stage policy that is not an entity policy's expression cannot be read back, so it is not dropped silently"),
    ];

    /// <summary>Branches no code-built domain reaches, with the reason (coverage still shows them unreached).</summary>
    internal static readonly (string Branch, string Reason)[] Unreachable = [
        ("Effects.cs: EffectPrinter.Default()", "all eleven Effect subtypes have an arm; Default runs only for a new subtype, which the dispatch guard test catches"),
        ("Entities.cs: ArgumentOutOfRangeException for an unknown member kind", "EntityMemberKind is closed; defensive"),
        ("Expressions.cs:45-46: ExpressionPrinter arms for PropertyAccess and ParameterAccess", "bound by CoreExpressionPrintBinders before the dispatch runs; dead fallbacks"),
        ("DomainDslPrinter.cs: FillPrimaryText default throw", "reached only for a binder with neither fill; no registered binder is one"),
        ("Stages.cs:95", "the closing brace of the `Not` check in StageRequire: the negated-lookup case reaches the lines either side; the brace is a coverage artefact"),
        ("DomainDslPrinter.cs: PrintComparisonKind `?`", "the comparison arm is bound by the table before dispatch; an out-of-range kind is not constructible by a parsed or analysed domain"),
    ];

    private static Entry Printed(string name, string sessionText, Func<Domain> build, Func<DomainDslPrinter>? printer = null, string pipeline = PipelineBuilt) {
        try {
            var text = (printer ?? (() => new DomainDslPrinter(Bare)))().Print(build());
            return new Entry("built/" + name, pipeline, sessionText, "printed", text.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        catch (Exception ex) {
            return new Entry("built/" + name, pipeline, sessionText, "failed:" + ex.GetType().Name, "");
        }
    }

    private static Entry Fragment(string name, string pipeline, Func<string> produce) {
        try {
            return new Entry("built/" + name, pipeline, "DomainSession.ForExtensions([])", "printed", produce().Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        }
        catch (Exception ex) {
            return new Entry("built/" + name, pipeline, "DomainSession.ForExtensions([])", "failed:" + ex.GetType().Name, "");
        }
    }

    private sealed record UnregisteredFacet : Facet;

    internal static List<Entry> All() {
        const string bare = "DomainSession.ForExtensions([])";
        var cases = new List<Entry>();
        var ok = Pol("Ok", DomainExpression.GreaterThan(Prp("Total"), Num(0)));

        // initial(S) and key(A, B)
        cases.Add(Printed("key-composite-and-initial", bare, () => Dom(Ent("A", [Prop("Name"), Prop("Code")], stages: [Stg("Draft"), Stg("Done")]) with { InitialStageName = "Done", KeyProperties = ["Name", "Code"] })));
        cases.Add(Printed("key-single", bare, () => Dom(Ent("A", [Prop("Name")]) with { KeyProperties = ["Name"] })));
        cases.Add(Printed("initial-only", bare, () => Dom(Ent("A", stages: [Stg("Draft"), Stg("Done")]) with { InitialStageName = "Done" })));

        // public action
        cases.Add(Printed("public-action", bare, () => Dom(Ent("A", [Prop("Name")], [Act("Go") with { Visibility = Visibility.Public }, Act("Stop")]))));
        cases.Add(Printed("public-action-in-stage", bare, () => Dom(Ent("A", stages: [Stg("Draft", [Act("Go", To("Done")) with { Visibility = Visibility.Public }]), Stg("Done")]))));

        // effects
        var host = (Effect[] effects) => Dom(Ent("B", [Prop("Name")]), Ent("A", [Prop("Name")], [Act("Go", effects)]) with { Navigations = [Nav("A", "bs", "B", RelationshipCardinality.OneToMany)] });
        cases.Add(Printed("composite-effect", bare, () => host([new CompositeEffect([Assign("Name", DomainExpression.Literal("x")), Assign("Name", DomainExpression.Literal("y"))])])));
        cases.Add(Printed("create-in-via-instance", bare, () => host([new CreateEntityInstance(Ref("B"), [new PropertyBinding("Name", DomainExpression.Literal("x"))], "bs")])));
        cases.Add(Printed("link-and-unlink", bare, () => host([new LinkEffect("bs", Prp("Name")), new UnlinkEffect("bs", Prp("Name"))])));
        cases.Add(Printed("for-each", bare, () => host([
            new ForEachEffect("b", "bs", null, [Assign("Name", DomainExpression.Literal("y"))]),
            new ForEachEffect("c", "bs", DomainExpression.Equal(Prp("Name"), DomainExpression.Literal("x")), [Assign("Name", DomainExpression.Literal("z"))]),
        ])));

        // stage require not
        cases.Add(Printed("stage-policy-not-a-require", bare, () => Dom(Ent("A", [Prop("Total", "Number")], stages: [Stg("Draft") with { Policies = [ok] }]))));
        cases.Add(Printed("stage-not-policy-without-entity-policy", bare, () => Dom(Ent("A", [Prop("Total", "Number")], policies: [ok], stages: [Stg("Draft") with { Policies = [new Policy("not_Ok", new Not(DomainExpression.Property("Other")))] }]))));
        cases.Add(Printed("stage-require-not", bare, () => Dom(Ent("A", [Prop("Total", "Number")], policies: [ok], stages: [Stg("Draft") with { Policies = [ok, new Policy("not_Ok", new Not(ok.Expression))] }]))));

        // value types and facets
        cases.Add(Printed("value-types", bare, () => Dom(Val("V", [Prop("X", "Number")], Visibility.Public), Val("W", [Prop("Y", "Text", new LengthConstraint(1, 5))]))));
        cases.Add(Printed("facet-unregistered-annotation", bare, () => Dom(Ent("A", [Prop("Name") with { Facets = [new Annotation("nosuch", new Dictionary<string, AnnotationValue> { ["0"] = new AnnotationString("x") })] }]))));
        cases.Add(Printed("facet-unregistered-type", bare, () => Dom(Ent("A", [Prop("Name") with { Facets = [new UnregisteredFacet()] }]))));
        cases.Add(Printed("annotation-registry-printer", "AnnotationRegistry of ExtensionCatalog.Core.Authoring",
            () => Dom(Ent("A", [Prop("Name") with { Facets = [new Annotation("column", new Dictionary<string, AnnotationValue> { ["0"] = new AnnotationString("N") })] }]) with { Facets = [new Annotation("table", new Dictionary<string, AnnotationValue> { ["0"] = new AnnotationString("T") })] }),
            () => new DomainDslPrinter(ExtensionCatalog.Core.Authoring.Annotations)));

        // uses: elision against the implicit set, and a pinned uses
        var implicitIds = ProductCatalog.ImplicitIds;
        cases.Add(Printed("implicit-ids-elided", bare + "; implicitIds=ProductCatalog.ImplicitIds",
            () => DomWith(implicitIds, Ent("A", [Prop("Name")])), () => new DomainDslPrinter(Bare, implicitIds)));
        cases.Add(Printed("implicit-ids-not-elided", bare + "; implicitIds=ProductCatalog.ImplicitIds",
            () => DomWith([ExtensionCatalog.StorageFacetsId], Ent("A", [Prop("Name")])), () => new DomainDslPrinter(Bare, implicitIds)));
        cases.Add(Printed("uses-pinned", bare,
            () => DomWith([ExtensionCatalog.StorageFacetsId], Ent("A", [Prop("Name")])) with {
                ImportedContracts = [Contract("Pkg", ContractSourceKind.ExternalProvider, "pkg", "v1", []) with { UsesId = ExtensionCatalog.StorageFacetsId, Pin = "2024-11-20" }],
            }));

        // literals
        cases.Add(Printed("literals", bare, () => Dom(Ent("A", [Prop("Total", "Decimal")], policies: [
            Pol("Dec", DomainExpression.GreaterThan(Prp("Total"), DomainExpression.Literal(1.5m))),
            Pol("Dbl", DomainExpression.GreaterThan(Prp("Total"), DomainExpression.Literal(2.5d))),
            Pol("Int", DomainExpression.GreaterThan(Prp("Total"), DomainExpression.Literal(3))),
            Pol("Nul", DomainExpression.NotEqual(Prp("Total"), DomainExpression.Literal(null))),
            Pol("Flag", DomainExpression.Equal(Prp("Total"), DomainExpression.Literal(true))),
        ]))));
        cases.Add(Printed("equality-literals", bare, () => Dom(Ent("A", [
            Prop("P1", "Text", new EqualityConstraint(null!)),
            Prop("P2", "Boolean", new EqualityConstraint(true)),
            Prop("P3", "Boolean", new EqualityConstraint(false)),
            Prop("P4", "Text", new EqualityConstraint("s\"q")),
            Prop("P5", "Decimal", new EqualityConstraint(1.5d)),
            Prop("P6", "Number", new EqualityConstraint(3L)),
            Prop("P7", "Decimal", new EqualityConstraint(2.5m)),
        ]))));
        cases.Add(Printed("required-constraint-omitted", bare, () => Dom(Ent("A", [Prop("Name", "Text", new RequiredConstraint(), new RelationalConstraint("Other", RelationalOrdering.Less))]))));

        // expressions
        cases.Add(Printed("expression-duration-unbound", bare, () => Dom(Ent("A", [Prop("Wait", "Duration")], policies: [
            Pol("Long", DomainExpression.GreaterThan(Prp("Wait"), new Duration(12, DurationUnit.Days))),
        ]))));
        cases.Add(Printed("expression-date-operation", bare, () => Dom(Ent("A", [Prop("Due", "Date")], policies: [
            Pol("Soon", DomainExpression.GreaterThan(Prp("Due"), new DateOperation(new Now(), DomainExpression.Literal(14L), DateOperationKind.AddDays))),
        ]))));
        cases.Add(Printed("expression-kinds", bare, () => Dom(Ent("B", [Prop("Name")]), Ent("A", [Prop("Total", "Number")], policies: [
            Pol("Owned", DomainExpression.Owned("Addr", Prp("City"))),
            Pol("NotCmp", DomainExpression.Not(DomainExpression.GreaterThan(Prp("Total"), Num(1)))),
            Pol("NotPlain", DomainExpression.Not(Prp("Flag"))),
            Pol("Div", DomainExpression.GreaterThan(DomainExpression.Divide(Prp("Total"), Num(2)), Num(1))),
            Pol("NoB", DomainExpression.NotExists(DomainExpression.RelationshipNav("bs", Prp("Name")))),
            Pol("Match", DomainExpression.GreaterThan(DomainExpression.Match([new MatchArm(DomainExpression.GreaterThan(Prp("Total"), Num(1)), Num(1))], Num(0)), Num(0))),
        ]) with { Navigations = [Nav("A", "bs", "B", RelationshipCardinality.OneToMany)] })));

        // public entry points other than Print(Domain)
        var printer = () => new DomainDslPrinter(Bare);
        cases.Add(Fragment("entry/PrintConstraintText", "entry: PrintConstraintText(constraints)", () => string.Join("\n", new Constraint[] {
            new LengthConstraint(1, 5), new LengthConstraint(3, 3), new PatternConstraint("^a$"), new RangeConstraint(1L, 10L), new UniqueConstraint(), new OptionalConstraint(),
            new EqualityConstraint("x"), new DefaultValueConstraint(Num(3)), new RequiredConstraint(), new RelationalConstraint("O", RelationalOrdering.Less),
        }.Select(c => printer().PrintConstraintText(c)))));
        cases.Add(Fragment("entry/PrintExpressionText", "entry: PrintExpressionText(expression)", () => printer().PrintExpressionText(
            DomainExpression.And(DomainExpression.GreaterThan(Prp("Total"), Num(1)), DomainExpression.Not(Prp("Flag"))))));
        cases.Add(Fragment("entry/PrintTestExpression", "entry: PrintTestExpression(expression)", () => printer().PrintTestExpression(
            DomainExpression.Or(DomainExpression.Equal(Prp("A"), DomainExpression.Literal("x")), DomainExpression.LessThanOrEqual(Prp("B"), Num(2))))));
        cases.Add(Fragment("entry/PrintActionFragmentKey", "entry: PrintActionFragmentKey(action, stage)", () => {
            var action = new Action("Go", InvocationResult.Void, [Prop("p", "Number")], [Assign("Total", Num(1)), To("Done")], [ok]);
            var stage = Stg("Draft", [action]) with { Policies = [ok], OnExitEffects = [Assign("Total", Num(2))] };
            return printer().PrintActionFragmentKey(action, stage);
        }));

        return cases;
    }
}