using Poly.Grammar;

namespace Poly.Tests.Grammar;

// ─── RuleRef: single recursive rule reference ───────────────

public sealed class GrammarRuleRefTests {
    // Grammar: primary = Number | LBrace Rule("expr") RBrace ; expr = Rule("primary")
    private static Matcher<TestKind> NestedGroupGrammar(string input) {
        var g = new Grammar<TestKind>();
        g.Define("expr").Pattern("primary-ref").Rule("primary").Commit();
        g.Define("primary")
            .Pattern("number").Token(TestKind.Number).Commit()
            .Pattern("group").Token(TestKind.LBrace).Rule("expr").Token(TestKind.RBrace).Commit();
        return new Matcher<TestKind>(g, new TestTokenizer(input));
    }

    [Test]
    public async Task RuleRef_NestedGroup_Matches() {
        var m = NestedGroupGrammar("{1}");
        var result = m.TryMatch("expr");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Consumed).IsEqualTo(3);
        await Assert.That(result.Tokens[0].Kind).IsEqualTo(TestKind.LBrace);
        await Assert.That(result.Tokens[1].Text).IsEqualTo("1");
        await Assert.That(result.Tokens[2].Kind).IsEqualTo(TestKind.RBrace);
    }

    [Test]
    public async Task RuleRef_MissingInner_Fails() {
        var m = NestedGroupGrammar("{1");
        var result = m.TryMatch("expr");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task RuleRef_ZeroWidth_Fails() {
        // A zero-width sub-match must fail, not recurse forever.
        var g = new Grammar<TestKind>();
        g.Define("empty").Pattern("epsilon").Commit();
        g.Define("expr").Pattern("ref-empty").Rule("empty").Commit();
        var m = new Matcher<TestKind>(g, new TestTokenizer("42"));
        var result = m.TryMatch("expr");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task RuleRef_ManyZeroWidth_NoHang() {
        // Many(Rule("empty")) terminates: ManyOf's zero-width guard plus RuleRef
        // failing on zero-consume — no infinite scan loop.
        var g = new Grammar<TestKind>();
        g.Define("empty").Pattern("epsilon").Commit();
        g.Define("expr").Pattern("many-empty").Many("empty").Commit();
        var m = new Matcher<TestKind>(g, new TestTokenizer("42"));
        var result = m.TryMatch("expr");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Consumed).IsEqualTo(0);
    }

    [Test]
    public async Task RuleRef_LongestMatch_NotFirstMatch() {
        // Token-led "num" sorts before predicate-led "numPlus", so a ManyOf-style
        // first-match would pick num (1 token). RuleRef must reuse TryMatch's
        // longest-match selection and pick numPlus (3 tokens).
        var g = new Grammar<TestKind>();
        g.Define("expr")
            .Pattern("num").Token(TestKind.Number).Commit()
            .Pattern("numPlus")
                .Predicate(k => k == TestKind.Number, "number")
                .Token(TestKind.Plus)
                .Token(TestKind.Number).Commit();
        g.Define("top").Pattern("ref").Rule("expr").Commit();
        var m = new Matcher<TestKind>(g, new TestTokenizer("1 + 2"));
        var result = m.TryMatch("top");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Consumed).IsEqualTo(3);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("1");
        await Assert.That(result.Tokens[1].Kind).IsEqualTo(TestKind.Plus);
        await Assert.That(result.Tokens[2].Text).IsEqualTo("2");
    }
}