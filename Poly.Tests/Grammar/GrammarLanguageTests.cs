using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// Immutable Grammar: WithPattern/Define return a new table; Language shares
/// that table for match and print.
/// </summary>
public sealed class GrammarLanguageTests {
    private static string TestCanonical(TestKind kind) => kind switch {
        TestKind.Colon => ":",
        TestKind.Identifier => "",
        TestKind.Number => "",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No canonical text."),
    };

    private static Grammar<TestToken, TestKind> CorePrimary() =>
        new GrammarBuilder<TestToken, TestKind>()
            .Define("primary")
            .Pattern("ident").Value(TestKind.Identifier).Commit()
            .Build();

    [Test]
    public async Task Extend_DoesNotMutateSource() {
        var core = CorePrimary();
        var extended = core.Extend(b =>
            b.Define("primary").Pattern("number").Value(TestKind.Number).Commit());

        await Assert.That(core.TryGetPattern("primary", "number", out _)).IsFalse();
        await Assert.That(extended.GetPattern("primary", "ident").Name).IsEqualTo("ident");
        await Assert.That(extended.GetPattern("primary", "number").Name).IsEqualTo("number");
    }

    [Test]
    public async Task DuplicatePatternName_FailsClosed() {
        var g = CorePrimary();
        await Assert.That(() => g.ToBuilder().Define("primary").Pattern("ident").Value(TestKind.Number).Commit())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GetPattern_Unknown_FailsClosed() {
        var g = CorePrimary();
        await Assert.That(() => g.GetPattern("primary", "missing"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Language_Extend_MatchAndPrintShareTable() {
        var language = new Language<TestToken, TestKind>(CorePrimary(), TestCanonical);
        var withMagic = language.Extend(b =>
            b.Define("primary")
                .Pattern("magic", priority: 1)
                .Predicate(t => t.Kind == TestKind.Identifier && t.Text == "MAGIC", "magic")
                .Commit());

        await Assert.That(language.Grammar.TryGetPattern("primary", "magic", out _)).IsFalse();

        var match = withMagic.Matcher(new TestTokenizer("MAGIC")).TryMatch("primary");
        await Assert.That(match?.PatternName).IsEqualTo("magic");

        var at = 0;
        var printed = withMagic.Printer.Print("primary", "magic", ctx => {
            if (at++ == 0)
                ctx.Emit("MAGIC");
        });
        await Assert.That(printed).IsEqualTo("MAGIC");

        var identOnly = language.Matcher(new TestTokenizer("MAGIC")).TryMatch("primary");
        await Assert.That(identOnly?.PatternName).IsEqualTo("ident");
    }

    [Test]
    public async Task NamedValue_CapturesAndPrintFills() {
        var language = new Language<TestToken, TestKind>(
            new GrammarBuilder<TestToken, TestKind>()
                .Define("duration")
                .Pattern("span")
                .Value(TestKind.Number, "amount")
                .Value(TestKind.Identifier, "unit")
                .Commit()
                .Build(),
            TestCanonical);

        var match = language.Matcher(new TestTokenizer("12 Days")).TryMatch("duration");
        await Assert.That(match?.PatternName).IsEqualTo("span");
        await Assert.That(match!.Captures["amount"][0].Text).IsEqualTo("12");
        await Assert.That(match.Captures["unit"][0].Text).IsEqualTo("Days");

        var printed = language.Printer.Print(
            "duration",
            "span",
            fills: new Dictionary<string, string>(StringComparer.Ordinal) {
                ["amount"] = "12",
                ["unit"] = "Days",
            });
        await Assert.That(printed).IsEqualTo("12Days");
    }

    [Test]
    public async Task DuplicateCaptureName_FailsClosed() {
        await Assert.That(() =>
            new GrammarBuilder<TestToken, TestKind>()
                .Define("p")
                .Pattern("dup")
                .Value(TestKind.Number, "n")
                .Value(TestKind.Identifier, "n")
                .Commit()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task DslGrammar_Core_ExtendAddsLibraryPattern() {
        await Assert.That(ReferenceEquals(DslGrammar.Build(), DslGrammar.Core)).IsTrue();

        var forked = DslGrammar.Build(b =>
            b.Define("expr-primary")
                .Pattern("magic")
                .Predicate(t => t.Kind == DslTokenKind.Identifier && t.Text == "MAGIC", "magic")
                .Commit());

        await Assert.That(DslGrammar.Core.TryGetPattern("expr-primary", "magic", out _)).IsFalse();
        await Assert.That(forked.GetPattern("expr-primary", "magic").Name).IsEqualTo("magic");
        await Assert.That(forked.GetPattern("expr-primary", "ident").Name).IsEqualTo("ident");
    }
}