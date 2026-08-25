using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// Smoke tests for the Grammar engine (grammar-revision tier A scaffold).
/// Exercises the from-scratch contract: IToken language ownership, bounded
/// Repeat (no magic caps), token-content predicates, LeftAssoc chains,
/// Ref recursion, EndOfStream, and the fail-closed rules (unknown rule,
/// trailing operator, zero-width guard).
/// </summary>
public class GrammarSmokeTests {
    // ── A char-level language (the shape the future Matching rebuild will use) ──

    private enum CharKind { Letter, Digit, Plus, Star, EndOfStream }

    private readonly record struct CharToken(CharKind Kind, char Char) : IToken<CharKind>;

    private sealed class CharReader : BufferedTokenReader<CharToken, CharKind> {
        private readonly string _text;
        private int _pos;

        public CharReader(string text) => _text = text;

        protected override CharToken ScanNextToken() {
            if (_pos >= _text.Length)
                return new CharToken(CharKind.EndOfStream, '\0');
            var c = _text[_pos++];
            return c switch {
                '+' => new CharToken(CharKind.Plus, c),
                '*' => new CharToken(CharKind.Star, c),
                _ when char.IsDigit(c) => new CharToken(CharKind.Digit, c),
                _ => new CharToken(CharKind.Letter, c),
            };
        }

        public override bool EndOfStream(CharKind kind) => kind == CharKind.EndOfStream;
    }

    private static Grammar<CharToken, CharKind> BuildGrammar() =>
        new GrammarBuilder<CharToken, CharKind>()
            .Define("expr")
            .Pattern("chain").LeftAssoc("term", CharKind.Plus).Commit()
            .Define("term")
            .Pattern("lit").Kind(CharKind.Digit).Commit()
            .Pattern("star").Kind(CharKind.Letter).Value(CharKind.Star).Commit()
            .Define("sentence")
            .Pattern("letters").Repeat("digit-or-letter", min: 1).Commit()
            .Define("digit-or-letter")
            .Pattern("digit").Kind(CharKind.Digit).Commit()
            .Pattern("letter").Kind(CharKind.Letter).Commit()
            .Build();

    // ── Basic longest-match + chain folding surface ──

    [Test]
    public async Task LongestMatch_ConsumesMostTokens_NotJustSortedFirst() {
        // Sorted order puts the 3-element pattern first, but it consumes fewer
        // tokens (3) than the Repeat-led sibling (4). True longest-match = most
        // tokens consumed, so "greedy" must win.
        var g = new GrammarBuilder<CharToken, CharKind>()
            .Define("digit-any")
            .Pattern("d").Kind(CharKind.Digit).Commit()
            .Define("rule")
            .Pattern("fixed").Kind(CharKind.Letter).Kind(CharKind.Digit).Kind(CharKind.Digit).Commit()
            .Pattern("greedy").Kind(CharKind.Letter).Repeat("digit-any", min: 1, max: 5).Commit()
            .Build();

        var m = new Matcher<CharToken, CharKind>(g, new CharReader("a123"));

        var r = m.TryMatch("rule");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("greedy"); // 4 > fixed's 3
        await Assert.That(r.Consumed).IsEqualTo(4);
    }

    [Test]
    public async Task TryMatch_LeftAssoc_ChainReturnsFlatTokens() {
        var g = BuildGrammar();
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("1+2+3"));

        var match = m.TryMatch("expr");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Consumed).IsEqualTo(5); // 1 + 2 + 3
        await Assert.That(match.Tokens[0].Char).IsEqualTo('1');
        await Assert.That(match.Tokens[1].Char).IsEqualTo('+');
        await Assert.That(match.Tokens[4].Char).IsEqualTo('3');
        await Assert.That(match.RuleName).IsEqualTo("expr");
        await Assert.That(match.Children.Count).IsEqualTo(3);
        await Assert.That(match.Operators.Count).IsEqualTo(2);
        await Assert.That(match.Operators[0].Char).IsEqualTo('+');
        await Assert.That(match.Children[0].RuleName).IsEqualTo("term");
        await Assert.That(match.Children[2].Tokens[0].Char).IsEqualTo('3');
    }

    [Test]
    public async Task TryMatch_LeftAssoc_TrailingOperatorFails() {
        var g = BuildGrammar();
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("1+"));

        var match = m.TryMatch("expr");

        await Assert.That(match).IsNull(); // N1: trailing operator fails the chain
    }

    [Test]
    public async Task TryMatch_Repeat_BoundedCount() {
        var g = BuildGrammar();
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("abc12"));

        var match = m.TryMatch("sentence");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Consumed).IsEqualTo(5);
    }

    [Test]
    public async Task TryMatch_Repeat_BelowMin_Fails() {
        var g = BuildGrammar();
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("+"));

        var match = m.TryMatch("sentence"); // min 1, stream starts with '+'

        await Assert.That(match).IsNull();
    }

    [Test]
    public async Task TryMatch_UnknownRule_Throws() {
        var g = BuildGrammar();
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("1"));

        await Assert.That(() => m.TryMatch("nope")).Throws<ArgumentException>();
    }

    [Test]
    public async Task TryMatch_Ref_RecursiveRule() {
        var g = BuildGrammar();
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("ab"));

        var match = m.TryMatch("term"); // star: Letter then Star — 'ab' is letter,letter

        await Assert.That(match).IsNull();
        var star = m.TryMatch("sentence"); // letters a,b both match digit-or-letter
        await Assert.That(star).IsNotNull();
        await Assert.That(star!.Consumed).IsEqualTo(2);
    }

    // ── Token-content predicates (the re-vision's semantic-predicate win) ──

    [Test]
    public async Task Predicate_SeesTokenContent_NotJustKind() {
        var g = new GrammarBuilder<CharToken, CharKind>()
            .Define("word")
            .Pattern("lower").Predicate(t => t.Char == 'a', "a-letter").Commit()
            .Define("word2")
            .Pattern("upper").Predicate(t => t.Char == 'Z', "z-letter").Commit()
            .Build();

        var m1 = new Matcher<CharToken, CharKind>(g, new CharReader("a"));
        await Assert.That(m1.TryMatch("word")).IsNotNull();

        var m2 = new Matcher<CharToken, CharKind>(g, new CharReader("b"));
        await Assert.That(m2.TryMatch("word")).IsNull(); // same kind, wrong content

        var m3 = new Matcher<CharToken, CharKind>(g, new CharReader("Z"));
        await Assert.That(m3.TryMatch("word2")).IsNotNull();
    }

    // ── Reader discipline: matcher peeks, caller consumes (committed position) ──

    [Test]
    public async Task PeekConsume_MatchesAtCommittedPosition() {
        var g = BuildGrammar();
        var reader = new CharReader("1+2");
        var m = new Matcher<CharToken, CharKind>(g, reader);

        var match = m.TryMatch("expr"); // peeks at committed position (0)
        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Consumed).IsEqualTo(3);

        reader.Consume(match.Consumed);
        await Assert.That(reader.Peek(0).Char).IsEqualTo('\0'); // EOF now at head
        await Assert.That(reader.EndOfStream(reader.Peek(0).Kind)).IsTrue();
    }

    // ── EndOfStream guard (Balanced against unterminated input) ──

    [Test]
    public async Task EndOfStream_BalancedUnterminated_Fails() {
        var g = new GrammarBuilder<CharToken, CharKind>()
            .Define("group")
            .Pattern("b").Balanced(CharKind.Plus, CharKind.Star).Commit()
            .Build();

        // open '+' present, but stream ends before any '*' close.
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("+ab"));
        var match = m.TryMatch("group");

        await Assert.That(match).IsNull(); // EndOfStream before close → fail closed

        // balanced pair '+a+b**c' — head IS the opener; nested '+' re-opens,
        // first '*' returns to depth 1, second '*' closes at depth 0.
        var m2 = new Matcher<CharToken, CharKind>(g, new CharReader("+a+b**c"));
        var match2 = m2.TryMatch("group");
        await Assert.That(match2).IsNotNull();
        await Assert.That(match2!.Consumed).IsEqualTo(6); // '+' 'a' '+' 'b' '*' '*'
    }

    // ── Balanced requires the opening delimiter at the head (no forward scan) ──

    [Test]
    public async Task Balanced_ContentBeforeOpener_DoesNotMatch() {
        var g = new GrammarBuilder<CharToken, CharKind>()
            .Define("group")
            .Pattern("b").Balanced(CharKind.Plus, CharKind.Star).Commit()
            .Build();

        // 'a' precedes the opener: Balanced must NOT scan forward past it.
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("a+*"));
        var match = m.TryMatch("group");

        await Assert.That(match).IsNull();
    }

    // ── Leaf with no named Value/Predicate holes has empty Captures ──

    [Test]
    public async Task MatchResult_Captures_EmptyWhenPatternHasNoNamedHoles() {
        var g = BuildGrammar();
        var m = new Matcher<CharToken, CharKind>(g, new CharReader("1"));

        var match = m.TryMatch("term");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Captures).IsEmpty();
        await Assert.That(match.Children).IsEmpty();
        await Assert.That(match.Operators).IsEmpty();
        await Assert.That(match.RuleName).IsEqualTo("term");
    }
}