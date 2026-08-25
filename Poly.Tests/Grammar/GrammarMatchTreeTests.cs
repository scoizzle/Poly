using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// Form-tree shape: nested rule matches, LeftAssoc operands/operators, Repeat
/// items, Priority inside Ref, leaf captures on the child — not bubbled.
/// </summary>
public sealed class GrammarMatchTreeTests {
    [Test]
    public async Task LayeredLeftAssoc_AddOfMul_NestsOperands() {
        var g = new GrammarBuilder<CharToken, CharKind>()
            .Define("expr-add")
            .Pattern("chain").LeftAssoc("expr-mul", CharKind.Plus).Commit()
            .Define("expr-mul")
            .Pattern("chain").LeftAssoc("expr-primary", CharKind.Star).Commit()
            .Define("expr-primary")
            .Pattern("num").Kind(CharKind.Digit).Commit()
            .Build();

        var match = new Matcher<CharToken, CharKind>(g, new CharReader("1+2*3")).TryMatch("expr-add");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.RuleName).IsEqualTo("expr-add");
        await Assert.That(match.PatternName).IsEqualTo("chain");
        await Assert.That(match.Consumed).IsEqualTo(5);
        await Assert.That(match.Children.Count).IsEqualTo(2);
        await Assert.That(match.Operators.Count).IsEqualTo(1);
        await Assert.That(match.Operators[0].Char).IsEqualTo('+');

        var left = match.Children[0];
        await Assert.That(left.RuleName).IsEqualTo("expr-mul");
        await Assert.That(left.Children.Count).IsEqualTo(1);
        await Assert.That(left.Operators).IsEmpty();
        await Assert.That(left.Children[0].RuleName).IsEqualTo("expr-primary");
        await Assert.That(left.Children[0].Tokens[0].Char).IsEqualTo('1');

        var right = match.Children[1];
        await Assert.That(right.RuleName).IsEqualTo("expr-mul");
        await Assert.That(right.Children.Count).IsEqualTo(2);
        await Assert.That(right.Operators.Count).IsEqualTo(1);
        await Assert.That(right.Operators[0].Char).IsEqualTo('*');
        await Assert.That(right.Children[0].Tokens[0].Char).IsEqualTo('2');
        await Assert.That(right.Children[1].Tokens[0].Char).IsEqualTo('3');
    }

    [Test]
    public async Task TwoRefs_AreOrderedChildren() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("value")
            .Pattern("num").Kind(TestKind.Number).Commit()
            .Define("pair")
            .Pattern("wrapped").Ref("value").Kind(TestKind.Colon).Ref("value").Commit()
            .Build();

        var match = new Matcher<TestToken, TestKind>(g, new TestTokenizer("1 : 2")).TryMatch("pair");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.RuleName).IsEqualTo("pair");
        await Assert.That(match.Children.Count).IsEqualTo(2);
        await Assert.That(match.Operators).IsEmpty();
        await Assert.That(match.Children[0].RuleName).IsEqualTo("value");
        await Assert.That(match.Children[0].Tokens[0].Text).IsEqualTo("1");
        await Assert.That(match.Children[1].Tokens[0].Text).IsEqualTo("2");
    }

    [Test]
    public async Task Repeat_RecursiveRule_EachItemIsChild() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("list")
            .Pattern("atom").Kind(TestKind.Identifier).Commit()
            .Pattern("nested").Kind(TestKind.LBrace).Ref("list").Kind(TestKind.RBrace).Commit()
            .Define("file")
            .Pattern("lists").Repeat("list", min: 1).Commit()
            .Build();

        var match = new Matcher<TestToken, TestKind>(g, new TestTokenizer("{ a } { b }")).TryMatch("file");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Children.Count).IsEqualTo(2);
        await Assert.That(match.Children[0].PatternName).IsEqualTo("nested");
        await Assert.That(match.Children[0].Children[0].PatternName).IsEqualTo("atom");
        await Assert.That(match.Children[0].Children[0].Tokens[0].Text).IsEqualTo("a");
        await Assert.That(match.Children[1].Children[0].Tokens[0].Text).IsEqualTo("b");
    }

    [Test]
    public async Task Ref_EqualLength_HigherPriorityWins() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("inner")
            .Pattern("ident").Kind(TestKind.Identifier).Commit()
            .Pattern("now", priority: 1)
                .Predicate(t => t.Kind == TestKind.Identifier && t.Text == "Now", "now")
                .Commit()
            .Define("use")
            .Pattern("r").Ref("inner").Commit()
            .Build();

        var match = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Now")).TryMatch("use");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Children[0].PatternName).IsEqualTo("now");
        await Assert.That(match.Children[0].Captures.ContainsKey("now")).IsTrue();
    }

    [Test]
    public async Task ListTokenReader_PeekConsume_ReplaysSpan() {
        var tokens = new TestToken[] {
            new(TestKind.Number, "1"),
            new(TestKind.Plus, "+"),
            new(TestKind.Number, "2"),
            new(TestKind.EndOfFile, ""),
        };
        var reader = new ListTokenReader<TestToken, TestKind>(tokens, static k => k == TestKind.EndOfFile);
        await Assert.That(reader.Peek(0).Text).IsEqualTo("1");
        reader.Consume(2);
        await Assert.That(reader.Peek(0).Text).IsEqualTo("2");
        reader.Consume(1);
        await Assert.That(reader.EndOfStream(reader.Peek(0).Kind)).IsTrue();
    }

    [Test]
    public async Task NotFollowedBy_RejectsWhenNextKindMatches() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("path")
            .Pattern("two")
                .Kind(TestKind.Identifier)
                .Kind(TestKind.Identifier)
                .NotFollowedBy(TestKind.Colon)
                .Commit()
            .Define("ident")
            .Pattern("one").Kind(TestKind.Identifier).Commit()
            .Build();

        var path = new Matcher<TestToken, TestKind>(g, new TestTokenizer("a b")).TryMatch("path");
        await Assert.That(path).IsNotNull();
        await Assert.That(path!.Consumed).IsEqualTo(2);

        var blocked = new Matcher<TestToken, TestKind>(g, new TestTokenizer("a b :")).TryMatch("path");
        await Assert.That(blocked).IsNull();

        var one = new Matcher<TestToken, TestKind>(g, new TestTokenizer("a b :")).TryMatch("ident");
        await Assert.That(one).IsNotNull();
        await Assert.That(one!.Consumed).IsEqualTo(1);
    }

    [Test]
    public async Task Captures_StayOnChild_DoNotBubble() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("primary")
            .Pattern("duration")
                .Value(TestKind.Number, "amount")
                .Predicate(t => t.Kind == TestKind.Identifier && t.Text == "days", "unit")
                .Commit()
            .Pattern("num").Kind(TestKind.Number).Commit()
            .Define("expr-add")
            .Pattern("chain").LeftAssoc("primary", TestKind.Plus).Commit()
            .Build();

        var match = new Matcher<TestToken, TestKind>(g, new TestTokenizer("1 + 2 days")).TryMatch("expr-add");

        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Captures).IsEmpty();
        await Assert.That(match.Children.Count).IsEqualTo(2);
        await Assert.That(match.Children[0].PatternName).IsEqualTo("num");
        await Assert.That(match.Children[1].PatternName).IsEqualTo("duration");
        await Assert.That(match.Children[1].Captures["amount"][0].Text).IsEqualTo("2");
        await Assert.That(match.Children[1].Captures["unit"][0].Text).IsEqualTo("days");
    }

    private enum CharKind { Digit, Plus, Star, Letter, EndOfStream }

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
}