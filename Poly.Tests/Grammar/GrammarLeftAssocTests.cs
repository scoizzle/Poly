using Poly.Grammar;

namespace Poly.Tests.Grammar;

// ─── LeftAssoc: left-associative operator chains ────────────

public sealed class GrammarLeftAssocTests {
    // Grammar: add = LeftAssoc(primary, Plus) ; primary = Number
    private static Matcher<TestKind> AddChainGrammar(string input) {
        var g = new Grammar<TestKind>();
        g.Define("primary").Pattern("num").Token(TestKind.Number).Commit();
        g.Define("add").Pattern("chain").LeftAssoc("primary", TestKind.Plus).Commit();
        return new Matcher<TestKind>(g, new TestTokenizer(input));
    }

    [Test]
    public async Task LeftAssoc_AddChain_ConsumesAll() {
        var m = AddChainGrammar("1 + 2 + 3");
        var result = m.TryMatch("add");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Consumed).IsEqualTo(5);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("1");
        await Assert.That(result.Tokens[1].Kind).IsEqualTo(TestKind.Plus);
        await Assert.That(result.Tokens[2].Text).IsEqualTo("2");
        await Assert.That(result.Tokens[3].Kind).IsEqualTo(TestKind.Plus);
        await Assert.That(result.Tokens[4].Text).IsEqualTo("3");
    }

    [Test]
    public async Task LeftAssoc_SingleOperand() {
        var m = AddChainGrammar("42");
        var result = m.TryMatch("add");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Consumed).IsEqualTo(1);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("42");
    }

    [Test]
    public async Task LeftAssoc_TrailingOp_Fails() {
        var m = AddChainGrammar("1 +");
        var result = m.TryMatch("add");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task LeftAssoc_CapReached_TrailingOperator_FailsClosed() {
        // N1 (2026-08-08): a chain still operator-led at the 10_000-iteration
        // cap is a truncated match or a trailing operator — fail closed rather
        // than silently accepting the prefix.
        var input = "1" + string.Concat(Enumerable.Repeat(" + 1", 10_000)) + " +";
        var m = AddChainGrammar(input);
        await Assert.That(m.TryMatch("add")).IsNull();
    }

    [Test]
    public async Task LeftAssoc_NestedOperandRule_Consumes() {
        // add = LeftAssoc(mul, Plus) ; mul = LeftAssoc(primary, Star)
        var g = new Grammar<TestKind>();
        g.Define("primary").Pattern("num").Token(TestKind.Number).Commit();
        g.Define("mul").Pattern("chain").LeftAssoc("primary", TestKind.Star).Commit();
        g.Define("add").Pattern("chain").LeftAssoc("mul", TestKind.Plus).Commit();
        var m = new Matcher<TestKind>(g, new TestTokenizer("1 + 2 * 3"));
        var result = m.TryMatch("add");
        await Assert.That(result).IsNotNull();
        // add: [1][+][2 * 3] — the mul operand consumes the star chain.
        await Assert.That(result!.Consumed).IsEqualTo(5);
        await Assert.That(result.Tokens[1].Kind).IsEqualTo(TestKind.Plus);
        await Assert.That(result.Tokens[3].Kind).IsEqualTo(TestKind.Star);
    }
}