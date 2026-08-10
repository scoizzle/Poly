using System.Text;

using Poly.Grammar;

// Shared TestKind/TestTokenizer now live in TestGrammar.cs (used by the
// matcher tests too). Alias to keep this file's test bodies readable.

namespace Poly.Tests.Grammar;

// ─── Edge-case / stress tests ──
//
// Uses the shared TestKind/TestTokenizer from TestGrammar.cs so this is
// a pure engine-contract test (edge cases reuse the matcher-test helpers).
//
// Covers the untested-in-normal-grammars code paths, proving the Matcher
// handles the hard cases:
//   - AnyToken wildcard (+ only element, + at EOF)
//   - Optional at start / multiple / at end after Balanced
//   - Predicate as first element / content-aware
//   - Repeat on empty rule (zero patterns → zero tokens)
//   - Repeat zero items / full-file consumption
//   - Balanced inside Optional / hitting EOF (guard)
//   - Scan loop over pure wildcards
// ──────────────────────────────────────────────────────────────

public sealed class GrammarEdgeCaseTests {
    private static bool IsPrimitive(TestToken t) => t.Kind == TestKind.Identifier;

    // ── 1. AnyToken — wildcard element ──
    [Test]
    public async Task AnyToken_Wildcard() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("stmt")
            .Pattern("decl").Kind(TestKind.Entity).Any().Any().Commit();

        var m = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity X 42"));
        var r = m.TryMatch("stmt");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("decl");
        await Assert.That(r.Consumed).IsEqualTo(3);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Entity);
        await Assert.That(r.Tokens[1].Kind).IsEqualTo(TestKind.Identifier);
        await Assert.That(r.Tokens[2].Kind).IsEqualTo(TestKind.Number);
    }

    // ── 2. AnyToken as the only element + EOF guard ──
    [Test]
    public async Task AnyToken_OnlyElement() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("value")
            .Pattern("wild").Any().Commit();

        var r1 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("hello")).TryMatch("value");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.Consumed).IsEqualTo(1);

        var r2 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("42")).TryMatch("value");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(1);

        // At EOF — AnyToken must not match EndOfFile (guards against infinite loops)
        var r3 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("")).TryMatch("value");
        await Assert.That(r3).IsNull();
    }

    // ── 3. Optional at the start of a pattern ──
    [Test]
    public async Task Optional_AtStart() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("decl")
            .Pattern("entity-decl").Optional(new MatchKind<TestToken, TestKind>(TestKind.Entity))
                                   .Kind(TestKind.Identifier)
                                   .Commit();

        var r1 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity Foo")).TryMatch("decl");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.Consumed).IsEqualTo(2);

        var r2 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Foo")).TryMatch("decl");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(1);
    }

    // ── 4. Multiple Optionals in sequence ──
    [Test]
    public async Task MultipleOptionals() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("prop")
            .Pattern("prop").Kind(TestKind.Identifier)
                            .Optional(new MatchKind<TestToken, TestKind>(TestKind.Required))
                            .Optional(new MatchKind<TestToken, TestKind>(TestKind.Unique))
                            .Commit();

        await Assert.That(new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name")).TryMatch("prop")!.Consumed).IsEqualTo(1);
        await Assert.That(new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name required")).TryMatch("prop")!.Consumed).IsEqualTo(2);
        await Assert.That(new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name unique")).TryMatch("prop")!.Consumed).IsEqualTo(2);
        await Assert.That(new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name required unique")).TryMatch("prop")!.Consumed).IsEqualTo(3);
    }

    // ── 5. Predicate as the first element (content-aware) ──
    [Test]
    public async Task Predicate_AsFirstElement() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("type-decl")
            .Pattern("type-assign").Predicate(IsPrimitive, "type")
                                   .Kind(TestKind.Colon)
                                   .Kind(TestKind.Number)
                                   .Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Text: 42")).TryMatch("type-decl");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("type-assign");
        await Assert.That(r.Consumed).IsEqualTo(3);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Identifier);
        await Assert.That(r.Tokens[1].Kind).IsEqualTo(TestKind.Colon);
    }

    // ── 6. Repeat on empty rule — zero patterns → zero tokens ──
    [Test]
    public async Task Repeat_RuleWithZeroPatterns() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("file")
            .Pattern("empty").Repeat("body").Kind(TestKind.EndOfFile).Commit();
        // "body" rule has no patterns — Repeat immediately produces zero tokens

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("")).TryMatch("file");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("empty");
        await Assert.That(r.Consumed).IsEqualTo(1); // just EndOfFile
    }

    // ── 7. Repeat zero items ──
    [Test]
    public async Task Repeat_EmptyBody_ReturnsZeroTokens() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("item")
            .Pattern("word").Kind(TestKind.Identifier).Commit();
        g.Define("file")
            .Pattern("empty-body").Repeat("item").Kind(TestKind.EndOfFile).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("")).TryMatch("file");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("empty-body");
        await Assert.That(r.Consumed).IsEqualTo(1);
    }

    // ── 8. Optional containing Balanced (nested) ──
    [Test]
    public async Task Optional_BalancedInside() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("decl")
            .Pattern("with-body").Kind(TestKind.Entity)
                                  .Kind(TestKind.Identifier)
                                  .Optional(new Balanced<TestToken, TestKind>(TestKind.LBrace, TestKind.RBrace))
                                  .Commit();

        var r1 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity Foo { }")).TryMatch("decl");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.Consumed).IsEqualTo(4); // Entity + Identifier + { }

        var r2 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity Foo")).TryMatch("decl");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(2);
    }

    // ── 9. Balanced hitting EOF (unterminated) ──
    [Test]
    public async Task Balanced_EndOfStream_ReturnsNull() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("value")
            .Pattern("object").Balanced(TestKind.LBrace, TestKind.RBrace).Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("{ ")).TryMatch("value");
        await Assert.That(r).IsNull(); // EOF before close → fail closed
    }

    // ── 10. Scan loop over pure wildcards ──
    [Test]
    public async Task ScanLoop_PureWildcards() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("token")
            .Pattern("any").Any().Commit();

        var reader = new TestTokenizer("a b c");
        var matcher = new Matcher<TestToken, TestKind>(g, reader);

        var count = 0;
        while (matcher.TryMatch("token") is { } r) {
            count++;
            reader.Consume(r.Consumed);
        }
        await Assert.That(count).IsEqualTo(3);
    }

    // ── 11. Repeat followed by EndOfFile — full consumption ──
    [Test]
    public async Task Repeat_ThenEndOfStream() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("item")
            .Pattern("ident").Kind(TestKind.Identifier).Commit();
        g.Define("file")
            .Pattern("idents").Repeat("item").Kind(TestKind.EndOfFile).Commit();

        var reader = new TestTokenizer("alpha beta gamma");
        var matcher = new Matcher<TestToken, TestKind>(g, reader);

        var result = matcher.TryMatch("file");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("idents");
        await Assert.That(result.Consumed).IsEqualTo(4); // 3 idents + EndOfFile
    }

    // ── 12. Predicate + AnyToken in same pattern ──
    [Test]
    public async Task PredicateThenAnyToken() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("prop")
            .Pattern("typed").Predicate(IsPrimitive, "type").Any().Commit();

        var r = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Text :")).TryMatch("prop");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("typed");
        await Assert.That(r.Consumed).IsEqualTo(2);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Identifier);
        await Assert.That(r.Tokens[1].Kind).IsEqualTo(TestKind.Colon);
    }

    // ── 13. Optional after Balanced ──
    [Test]
    public async Task Optional_AfterBalanced() {
        var g = new Grammar<TestToken, TestKind>();
        g.Define("decl")
            .Pattern("entity").Kind(TestKind.Entity).Kind(TestKind.Identifier)
                              .Balanced(TestKind.LBrace, TestKind.RBrace)
                              .Optional(new MatchKind<TestToken, TestKind>(TestKind.Required))
                              .Commit();

        var r1 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity Foo { } required")).TryMatch("decl");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.Consumed).IsEqualTo(5);

        var r2 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity Foo { }")).TryMatch("decl");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(4);
    }
}