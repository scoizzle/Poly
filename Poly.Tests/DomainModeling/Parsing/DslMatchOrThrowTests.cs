using Poly.DomainModeling.Language;
using Poly.Grammar;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-28: <see cref="DslCursor.MatchOrThrow"/> words a table-reported failure the way the hand checks it replaces word
/// theirs. An unlabelled kind is the <see cref="DslCursor.Expect"/> shape (<c>Expected {Kind}, got 'x' ({gotKind})</c>), a
/// labelled content element is the <see cref="DslCursor.ExpectIdentifier"/> shape (<c>Expected {label}, got 'x'</c>), a set
/// joins with " or ", nothing expected reads <c>Unexpected 'x'</c>, and a refusal is its message alone. The position is the
/// failing token's line and column; offset and length stay -1 and 0 as hand errors have them (G-17 adds them).
/// </summary>
public sealed class DslMatchOrThrowTests {
    private static Grammar<DslToken, DslTokenKind> Table(Action<GrammarBuilder<DslToken, DslTokenKind>> rows) => DslGrammar.Build(rows);

    // Over the whole token list, as the resolver reads (a buffered reader drops nothing on a Consume before its first Peek).
    private static DslCursor Cursor(Grammar<DslToken, DslTokenKind> grammar, string text) {
        var scanner = new DslTokenReader(text);
        var tokens = new List<DslToken>();
        for (var t = scanner.Peek(); ; t = scanner.Peek()) {
            tokens.Add(t);
            if (t.Kind == DslTokenKind.EndOfFile)
                break;
            scanner.Consume(1);
        }
        return new(new ListTokenReader<DslToken, DslTokenKind>(tokens, kind => kind == DslTokenKind.EndOfFile),
            reader => new Matcher<DslToken, DslTokenKind>(grammar, reader));
    }

    private static GrammarException Thrown(Action act) {
        try {
            act();
        }
        catch (GrammarException ex) {
            return ex;
        }
        throw new InvalidOperationException("nothing was thrown");
    }

    private static string Result(GrammarException ex) => $"@{ex.Line}:{ex.Column} off={ex.Offset} len={ex.Length} :: {ex.Detail}";

    [Test]
    public async Task MatchOrThrow_AnUnlabelledKind_IsTheExpectShape() {
        var grammar = Table(b => b.Define("t").Pattern("p").Kind(DslTokenKind.Colon).Cut().Kind(DslTokenKind.RBrace).Commit());

        var table = Thrown(() => Cursor(grammar, ":\n  \"x\"").MatchOrThrow("t"));
        var hand = Cursor(grammar, ":\n  \"x\"");
        hand.Advance();
        var expect = Thrown(() => hand.Expect(DslTokenKind.RBrace));

        await Assert.That(Result(table)).IsEqualTo(Result(expect));
        await Assert.That(table.Detail).IsEqualTo("Expected RBrace, got 'x' (StringLiteral)");
    }

    [Test]
    public async Task MatchOrThrow_ALabelledValue_IsTheExpectIdentifierShape() {
        var grammar = Table(b => b.Define("t").Pattern("p").Kind(DslTokenKind.Entity).Cut()
            .Value(DslTokenKind.Identifier, "name").Expect("entity name").Commit());

        var table = Thrown(() => Cursor(grammar, "entity 5").MatchOrThrow("t"));
        var hand = Cursor(grammar, "entity 5");
        hand.Advance();
        var expect = Thrown(() => hand.ExpectIdentifier(DslTokenKind.Identifier, "entity name"));

        await Assert.That(Result(table)).IsEqualTo(Result(expect));
        await Assert.That(table.Detail).IsEqualTo("Expected entity name, got '5'");
    }

    [Test]
    public async Task MatchOrThrow_ASetJoinsWithOr_AndFixedSyntaxShowsTheKindItGot() {
        var grammar = Table(b => b.Define("t")
            .Pattern("a").Kind(DslTokenKind.Colon).Cut().Kind(DslTokenKind.Entity).Expect("'entity'").Commit()
            .Pattern("b").Kind(DslTokenKind.Colon).Cut().Word("value").Commit());

        var ex = Thrown(() => Cursor(grammar, ": 5").MatchOrThrow("t"));

        await Assert.That(Result(ex)).IsEqualTo("@1:3 off=-1 len=0 :: Expected 'entity' or 'value', got '5' (Number)");
    }

    [Test]
    public async Task MatchOrThrow_NothingExpected_SaysUnexpected() {
        var grammar = Table(b => b.Define("t").Pattern("p").Kind(DslTokenKind.Colon).Cut().NotFollowedBy(DslTokenKind.Comma)
            .Kind(DslTokenKind.Identifier).Commit());

        var ex = Thrown(() => Cursor(grammar, ": ,").MatchOrThrow("t"));

        await Assert.That(Result(ex)).IsEqualTo("@1:3 off=-1 len=0 :: Unexpected ','");
    }

    [Test]
    public async Task MatchOrThrow_ARefusal_IsItsMessageAlone_AtTheLastMatchedToken() {
        var grammar = Table(b => b.Define("t").Pattern("p").Value(DslTokenKind.Identifier, "name").Kind(DslTokenKind.Colon)
            .Word("actor").Refuse("'{got}' is not supported (used as type for '{name}')").Commit());

        var ex = Thrown(() => Cursor(grammar, "Who:\n  actor").MatchOrThrow("t"));

        await Assert.That(Result(ex)).IsEqualTo("@2:3 off=-1 len=0 :: 'actor' is not supported (used as type for 'Who')");
    }

    [Test]
    public async Task MatchOrThrow_ASuccess_ReturnsTheMatchAndConsumesNothing() {
        var grammar = Table(b => b.Define("t").Pattern("p").Kind(DslTokenKind.Colon).Cut().Kind(DslTokenKind.Comma).Commit());
        var cursor = Cursor(grammar, ": ,");

        var match = cursor.MatchOrThrow("t");

        await Assert.That(match.Consumed).IsEqualTo(2);
        await Assert.That(cursor.Current.Kind).IsEqualTo(DslTokenKind.Colon);
    }
}