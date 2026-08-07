using Poly.DomainModeling;
using Poly.DomainModeling.Parsing;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// GI-2 acceptance: the DSL grammar table recognizes and dispatches the Phase 1a
/// structural constructs — top-level declarations, entity-body members, stage-body
/// members, and annotation shapes (generic + pack-registered keywords).
/// </summary>
public class DslGrammarTests {
    private static string Match(Grammar<DslTokenKind> grammar, string rule, string text) {
        var reader = new DslTokenReader(text);
        var matcher = new Matcher<DslTokenKind>(grammar, reader);
        return matcher.TryMatch(rule)?.PatternName ?? "<no match>";
    }

    private static string Match(string rule, string text) =>
        Match(DslGrammar.Build(), rule, text);

    [Test]
    public async Task Top_Dispatches_EnumAndEntity() {
        await Assert.That(Match("top", "Color: enum { Red, Green }")).IsEqualTo("enum");
        await Assert.That(Match("top", "Item: entity { }")).IsEqualTo("entity");
    }

    [Test]
    public async Task EntityBody_Dispatches_MemberKinds() {
        await Assert.That(Match("entity-body", "when loans Overdue")).IsEqualTo("entity-subscription");
        await Assert.That(Match("entity-body", "when any loans Overdue")).IsEqualTo("entity-subscription");
        await Assert.That(Match("entity-body", "Draft: stage { }")).IsEqualTo("stage");
        await Assert.That(Match("entity-body", "Submit: action { }")).IsEqualTo("action");
        await Assert.That(Match("entity-body", "Adult: policy { X }")).IsEqualTo("policy");
        await Assert.That(Match("entity-body", "Old(params): action { }")).IsEqualTo("legacy-action");
        await Assert.That(Match("entity-body", "color: Color")).IsEqualTo("typed-line");
        await Assert.That(Match("entity-body", "Name: Text")).IsEqualTo("property");
        await Assert.That(Match("entity-body", "orders: many Order")).IsEqualTo("nav-many");
        await Assert.That(Match("entity-body", "primary: one Address")).IsEqualTo("nav-one");
        await Assert.That(Match("entity-body", "doc: owned Doc")).IsEqualTo("nav-owned");
        await Assert.That(Match("entity-body", "Number: Text")).IsEqualTo("primitive-name");
    }

    [Test]
    public async Task StageBody_Dispatches_MemberKinds() {
        await Assert.That(Match("stage-body", "entry { }")).IsEqualTo("entry");
        await Assert.That(Match("stage-body", "exit { }")).IsEqualTo("exit");
        await Assert.That(Match("stage-body", "when any Tracks Active as t")).IsEqualTo("subscription");
        await Assert.That(Match("stage-body", "Submit: action { }")).IsEqualTo("stage-action");
    }

    [Test]
    public async Task Annotation_Matches_GenericShapes() {
        // Longest-match: "column()" matches both no-args and with-args (Many
        // matches zero args); with-args consumes more tokens and wins.
        await Assert.That(Match("annotation", "column()")).IsEqualTo("with-args");
        await Assert.That(Match("annotation", "column(\"Name\")")).IsEqualTo("with-args");
        await Assert.That(Match("annotation", "column(\"Name\", \"TYPE\")")).IsEqualTo("with-args");
        await Assert.That(Match("annotation", "column(42, true, null)")).IsEqualTo("with-args");
    }

    [Test]
    public async Task Annotation_UnknownShape_DoesNotMatch() {
        // Missing closing paren is not a recognized annotation shape.
        await Assert.That(Match("annotation", "column(\"Name\"")).IsEqualTo("<no match>");
    }

    [Test]
    public async Task Annotation_CustomShapeExtension_RegistersPattern() {
        // GI-4 extension point: a pack declares a non-generic argument shape
        // (bare identifier instead of quoted string) by adding a pattern to the
        // "annotation" rule through the public Grammar API.
        var grammar = DslGrammar.Build();
        grammar.Define("annotation")
            .Pattern("kw-column-bare")
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.LParen)
                .Value(DslTokenKind.Identifier).Token(DslTokenKind.RParen)
                .Commit();

        await Assert.That(Match(grammar, "annotation", "column(NAME)")).IsEqualTo("kw-column-bare");
        // The generic shape still wins for quoted args on the same grammar.
        await Assert.That(Match(grammar, "annotation", "column(\"Name\")")).IsEqualTo("with-args");
    }

    [Test]
    public async Task IsPrimitiveTypeKind_TrueForPrimitivesOnly() {
        await Assert.That(DslGrammar.IsPrimitiveTypeKind(DslTokenKind.Text)).IsTrue();
        await Assert.That(DslGrammar.IsPrimitiveTypeKind(DslTokenKind.NumberType)).IsTrue();
        await Assert.That(DslGrammar.IsPrimitiveTypeKind(DslTokenKind.BooleanType)).IsTrue();
        await Assert.That(DslGrammar.IsPrimitiveTypeKind(DslTokenKind.DateTimeType)).IsTrue();
        await Assert.That(DslGrammar.IsPrimitiveTypeKind(DslTokenKind.DateType)).IsTrue();
        await Assert.That(DslGrammar.IsPrimitiveTypeKind(DslTokenKind.Identifier)).IsFalse();
        await Assert.That(DslGrammar.IsPrimitiveTypeKind(DslTokenKind.Stage)).IsFalse();
    }
}