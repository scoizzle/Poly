using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// The Word element (G-27): a contextual word matches one identifier with that exact text (never a
/// keyword kind with the same text, never end of input), captures nothing, sorts after kind-led rows and
/// prints its own text. Test rows only: G-29 and G-30 are the first production consumers.
/// </summary>
public sealed class GrammarWordTests {
    private static Grammar<DslToken, DslTokenKind> Table() => new GrammarBuilder<DslToken, DslTokenKind>()
        .Define("nav")
            .Pattern("via")
                .Value(DslTokenKind.Identifier, "name").Word("via").Value(DslTokenKind.Identifier, "target").Commit()
            .Pattern("to")
                .Value(DslTokenKind.Identifier, "name").Word("to").Value(DslTokenKind.Identifier, "target").Commit()
        .Define("head")
            .Pattern("initial").Word("initial").Value(DslTokenKind.Identifier, "stage").Commit()
            .Pattern("entity").Kind(DslTokenKind.Entity).Value(DslTokenKind.Identifier, "name").Commit()
        .Define("key-list")
            .Pattern("key").Word("key").Kind(DslTokenKind.LParen).Value(DslTokenKind.Identifier).Kind(DslTokenKind.RParen).Commit()
        .Define("action-header")
            .Pattern("public")
                .Kind(DslTokenKind.Public).Kind(DslTokenKind.Action).Value(DslTokenKind.Identifier, "name")
                .Kind(DslTokenKind.LParen).Value(DslTokenKind.Identifier).Kind(DslTokenKind.RParen)
                .Kind(DslTokenKind.Arrow).Value(DslTokenKind.Identifier, "result").Commit()
        .Build();

    private static MatchResult<DslToken, DslTokenKind>? Match(string rule, string text) =>
        new Matcher<DslToken, DslTokenKind>(Table(), new DslTokenReader(text)).TryMatch(rule);

    private static Printer<DslToken, DslTokenKind> DslPrinter() =>
        new(Table(), DslGrammar.CanonicalText, () => new DslTokenWriter());

    [Test]
    public async Task Word_MatchesTheIdentifierWithItsText() {
        var match = Match("nav", "Owner via Account");

        await Assert.That(match?.PatternName).IsEqualTo("via");
        await Assert.That(match!.Consumed).IsEqualTo(3);
    }

    [Test]
    public async Task Word_DoesNotMatchAnotherIdentifier() {
        await Assert.That(Match("nav", "Owner vial Account")).IsNull();
    }

    [Test]
    public async Task Word_DoesNotMatchAKeywordKindWithTheSameText() {
        // `to` is the To keyword kind, not an identifier: an identifier-only Word cannot match it (G-21 demotes it).
        await Assert.That(Match("nav", "Owner to Account")).IsNull();
    }

    [Test]
    public async Task Word_DoesNotMatchEndOfInput() {
        await Assert.That(Match("nav", "Owner")).IsNull();
        await Assert.That(Match("head", "")).IsNull();
    }

    [Test]
    public async Task Word_CreatesNoCapture() {
        var match = Match("nav", "Owner via Account");

        await Assert.That(match!.Captures.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["name", "target"]);
    }

    [Test]
    public async Task Word_SortsAfterKindLedPatterns() {
        var rows = Table().GetPatterns("head").Select(row => row.Name);

        await Assert.That(rows).IsEquivalentTo(["entity", "initial"]);
        await Assert.That(Match("head", "initial Open")?.PatternName).IsEqualTo("initial");
    }

    [Test]
    public async Task Word_PrintsItsTextWithNoFill() {
        var text = DslPrinter().Print("nav", "via", fills: Fills.Of(("name", "Owner"), ("target", "Account")));

        await Assert.That(text).IsEqualTo("Owner via Account");
    }

    [Test]
    public async Task EndToEnd_KeyList_HandlerEmitsNamesAndCommasIntoTheSameWriter() {
        var text = DslPrinter().Print("key-list", "key", ctx => {
            ctx.Emit("A");
            ctx.Emit(DslTokenKind.Comma);
            ctx.Emit("B");
        });

        await Assert.That(text).IsEqualTo("key(A, B)");
    }

    [Test]
    public async Task EndToEnd_ActionHeader_HandlerEmitsParametersIntoTheSameWriter() {
        var text = DslPrinter().Print("action-header", "public", ctx => {
            ctx.Emit("p");
            ctx.Emit(DslTokenKind.Colon);
            ctx.Emit("T");
            ctx.Emit(DslTokenKind.Comma);
            ctx.Emit("q");
            ctx.Emit(DslTokenKind.Colon);
            ctx.Emit("U");
        }, Fills.Of(("name", "Place"), ("result", "R")));

        await Assert.That(text).IsEqualTo("public action Place (p: T, q: U) -> R");
    }
}
