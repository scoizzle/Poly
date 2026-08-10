using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// Dedicated Ref / LeftAssoc coverage — v1's GrammarRuleRefTests and
/// GrammarLeftAssocTests were deleted in the engine cutover and recreated here.
/// Pins the recursion guards (zero-width sub-matches fail) and the fail-closed
/// rules (unknown rule throws; trailing operator fails the whole chain).
/// </summary>
public class GrammarRefAndLeftAssocTests {
    // ── Ref ──

    [Test]
    public async Task Ref_SingleOccurrence_ForwardsTokens() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("value")
            .Pattern("num").Kind(TestKind.Number).Commit();
        g.Define("pair")
            .Pattern("wrapped").Ref("value").Kind(TestKind.Colon).Ref("value").Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("1 : 2")).TryMatch("pair");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("wrapped");
        await Assert.That(r.Consumed).IsEqualTo(3);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Number);
        await Assert.That(r.Tokens[2].Kind).IsEqualTo(TestKind.Number);
    }

    [Test]
    public async Task Ref_RecursiveRule_NestedLists() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("list")
            .Pattern("atom").Kind(TestKind.Identifier).Commit()
            .Pattern("nested").Kind(TestKind.LBrace).Ref("list").Kind(TestKind.RBrace).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("{ { { a } } }")).TryMatch("list");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("nested");
        await Assert.That(r.Consumed).IsEqualTo(7);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.LBrace);
        await Assert.That(r.Tokens[^1].Kind).IsEqualTo(TestKind.RBrace);
    }

    [Test]
    public async Task Ref_ZeroWidthSubMatch_Fails() {
        // "maybe" can match zero tokens (both optionals absent); Ref treats a
        // zero-width sub-match as failure (infinite-recursion guard).
        var g = new Grammar<TestToken, TestKind>();
        g.Define("maybe")
            .Pattern("m").Optional(new MatchKind<TestToken, TestKind>(TestKind.Plus))
                         .Optional(new MatchKind<TestToken, TestKind>(TestKind.Star)).Commit();
        g.Define("use")
            .Pattern("u").Ref("maybe").Kind(TestKind.Identifier).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("a")).TryMatch("use");
        await Assert.That(r).IsNull(); // Ref consumed 0 → failure, pattern can't match
    }

    [Test]
    public async Task Ref_UnknownRule_Throws() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("use")
            .Pattern("u").Ref("missing").Kind(TestKind.Identifier).Commit();

        var m = new Matcher<TestToken, TestKind>(g, new TestTokenizer("a"));
        await Assert.That(() => m.TryMatch("use")).Throws<ArgumentException>();
    }

    // ── LeftAssoc ──

    [Test]
    public async Task LeftAssoc_MixedOperatorKinds_FoldsFlat() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("expr")
            .Pattern("chain").LeftAssoc("term", TestKind.Plus, TestKind.Star).Commit();
        g.Define("term")
            .Pattern("n").Kind(TestKind.Number).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("1 + 2 * 3 + 4")).TryMatch("expr");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.Consumed).IsEqualTo(7);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Number);
        await Assert.That(r.Tokens[1].Kind).IsEqualTo(TestKind.Plus);
        await Assert.That(r.Tokens[3].Kind).IsEqualTo(TestKind.Star);
        await Assert.That(r.Tokens[5].Kind).IsEqualTo(TestKind.Plus);
        await Assert.That(r.Tokens[6].Kind).IsEqualTo(TestKind.Number);
    }

    [Test]
    public async Task LeftAssoc_TrailingOperator_FailsWholeChain() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("expr")
            .Pattern("chain").LeftAssoc("term", TestKind.Plus).Commit();
        g.Define("term")
            .Pattern("n").Kind(TestKind.Number).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("1 +")).TryMatch("expr");
        await Assert.That(r).IsNull();
    }

    [Test]
    public async Task LeftAssoc_NoFirstOperand_Fails() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("expr")
            .Pattern("chain").LeftAssoc("term", TestKind.Plus).Commit();
        g.Define("term")
            .Pattern("n").Kind(TestKind.Number).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("+ 1")).TryMatch("expr");
        await Assert.That(r).IsNull();
    }

    [Test]
    public async Task LeftAssoc_ZeroWidthFirstOperand_Fails() {
        // Operand rule matches zero tokens (optional Number absent): the chain
        // must fail rather than accept an empty first operand (recursion guard).
        var g = new Grammar<TestToken, TestKind>();
        g.Define("expr")
            .Pattern("chain").LeftAssoc("maybe-term", TestKind.Plus).Commit();
        g.Define("maybe-term")
            .Pattern("m").Optional(new MatchKind<TestToken, TestKind>(TestKind.Number)).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("+ 1")).TryMatch("expr");
        await Assert.That(r).IsNull();
    }

    [Test]
    public async Task LeftAssoc_ZeroWidthContinuation_FailsWholeChain() {
        // "1 +" with an operand rule that can match zero tokens: the continuation
        // must not silently accept an empty operand (trailing-operator rule).
        var g = new Grammar<TestToken, TestKind>();
        g.Define("expr")
            .Pattern("chain").LeftAssoc("maybe-term", TestKind.Plus).Commit();
        g.Define("maybe-term")
            .Pattern("m").Optional(new MatchKind<TestToken, TestKind>(TestKind.Number)).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("1 +")).TryMatch("expr");
        await Assert.That(r).IsNull();
    }

    [Test]
    public async Task LeftAssoc_SingleOperand_NoOperator_MatchesOne() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("expr")
            .Pattern("chain").LeftAssoc("term", TestKind.Plus).Commit();
        g.Define("term")
            .Pattern("n").Kind(TestKind.Number).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("42")).TryMatch("expr");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("chain");
        await Assert.That(r.Consumed).IsEqualTo(1);
    }
}