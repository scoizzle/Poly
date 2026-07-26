using Poly.Text.Grammar;

namespace Poly.Tests.Text.Grammar;

// ─── Edge-case / stress tests using TestKind + TestTokenizer ──
//
// Uses the existing TestKind enum and TestTokenizer from GrammarMatcherTests.
//
// Covers untested code paths that the JSON and mini-DSL grammars don't reach:
//   - AnyToken wildcard
//   - Optional at pattern start
//   - Multiple Optionals in sequence
//   - Predicate as first element
//   - ManyOf on empty rule (zero patterns)
//   - ManyOf matching zero items
//   - Optional containing a Balanced
//   - Pattern with AnyToken as the *only* element
//   - Balanced hitting end-of-file (EOF guard)
//   - Scan over pure wildcards
// ──────────────────────────────────────────────────────────────

public sealed class GrammarEdgeCaseTests {
    // ═════════════════════════════════════════════════════════
    //  1. AnyToken — wildcard element
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task AnyToken_Wildcard() {
        var g = new Grammar<TestKind>();
        g.Define("stmt")
            .Pattern("decl").Token(TestKind.Entity).Any().Any().Commit();

        var m = new Matcher<TestKind>(g, new TestTokenizer("entity X 42"));
        var r = m.TryMatch("stmt");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("decl");
        await Assert.That(r.Consumed).IsEqualTo(3); // Entity + Any + Any
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Entity);
        await Assert.That(r.Tokens[1].Kind).IsEqualTo(TestKind.Identifier);  // "X"
        await Assert.That(r.Tokens[2].Kind).IsEqualTo(TestKind.Number);      // "42"
    }

    // ═════════════════════════════════════════════════════════
    //  2. AnyToken as the only pattern element
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task AnyToken_OnlyElement() {
        var g = new Grammar<TestKind>();
        g.Define("value")
            .Pattern("wild").Any().Commit();

        // Matches any single token regardless of kind
        var m1 = new Matcher<TestKind>(g, new TestTokenizer("hello"));
        var r1 = m1.TryMatch("value");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.PatternName).IsEqualTo("wild");
        await Assert.That(r1.Consumed).IsEqualTo(1);

        var m2 = new Matcher<TestKind>(g, new TestTokenizer("42"));
        var r2 = m2.TryMatch("value");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(1);

        // At EOF — AnyToken does not match EndOfFile (guards against infinite loops)
        var m3 = new Matcher<TestKind>(g, new TestTokenizer(""));
        var r3 = m3.TryMatch("value");
        await Assert.That(r3).IsNull();
    }

    // ═════════════════════════════════════════════════════════
    //  3. Optional at the start of a pattern
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task Optional_AtStart() {
        var g = new Grammar<TestKind>();
        g.Define("decl")
            .Pattern("entity-decl").Optional(TestKind.Entity)
                                   .Token(TestKind.Identifier)
                                   .Commit();

        // With optional prefix
        var m1 = new Matcher<TestKind>(g, new TestTokenizer("entity Foo"));
        var r1 = m1.TryMatch("decl");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.PatternName).IsEqualTo("entity-decl");
        await Assert.That(r1.Consumed).IsEqualTo(2);
        await Assert.That(r1.Tokens[0].Kind).IsEqualTo(TestKind.Entity);
        await Assert.That(r1.Tokens[1].Kind).IsEqualTo(TestKind.Identifier);

        // Without optional prefix
        var m2 = new Matcher<TestKind>(g, new TestTokenizer("Foo"));
        var r2 = m2.TryMatch("decl");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.PatternName).IsEqualTo("entity-decl");
        await Assert.That(r2.Consumed).IsEqualTo(1);
        await Assert.That(r2.Tokens[0].Kind).IsEqualTo(TestKind.Identifier);
    }

    // ═════════════════════════════════════════════════════════
    //  4. Multiple Optionals in sequence
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task MultipleOptionals() {
        var g = new Grammar<TestKind>();
        g.Define("prop")
            .Pattern("prop").Token(TestKind.Identifier)
                            .Optional(TestKind.Required)
                            .Optional(TestKind.Unique)
                            .Commit();

        // No optionals
        var r0 = new Matcher<TestKind>(g, new TestTokenizer("Name"));
        await Assert.That(r0.TryMatch("prop")!.Consumed).IsEqualTo(1);

        // First optional only
        var r1 = new Matcher<TestKind>(g, new TestTokenizer("Name required"));
        await Assert.That(r1.TryMatch("prop")!.Consumed).IsEqualTo(2);

        // Second optional only
        var r2 = new Matcher<TestKind>(g, new TestTokenizer("Name unique"));
        await Assert.That(r2.TryMatch("prop")!.Consumed).IsEqualTo(2);

        // Both optionals
        var r3 = new Matcher<TestKind>(g, new TestTokenizer("Name required unique"));
        await Assert.That(r3.TryMatch("prop")!.Consumed).IsEqualTo(3);
    }

    // ═════════════════════════════════════════════════════════
    //  5. Predicate as the first pattern element
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task Predicate_AsFirstElement() {
        static bool IsPrimitive(TestKind k) => k == TestKind.Identifier;

        var g = new Grammar<TestKind>();
        g.Define("type-decl")
            .Pattern("type-assign").Predicate(IsPrimitive, "type")
                                   .Token(TestKind.Colon)
                                   .Token(TestKind.Number)
                                   .Commit();

        var m = new Matcher<TestKind>(g, new TestTokenizer("Text: 42"));
        var r = m.TryMatch("type-decl");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("type-assign");
        await Assert.That(r.Consumed).IsEqualTo(3);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Identifier);
        await Assert.That(r.Tokens[1].Kind).IsEqualTo(TestKind.Colon);
    }

    // ═════════════════════════════════════════════════════════
    //  6. ManyOf on empty rule — zero patterns to match
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task ManyOf_RuleWithZeroPatterns() {
        var g = new Grammar<TestKind>();
        g.Define("file")
            .Pattern("empty").Many("body").Token(TestKind.EndOfFile).Commit();
        // "body" rule has no patterns — ManyOf immediately produces zero tokens

        var m = new Matcher<TestKind>(g, new TestTokenizer(""));
        var r = m.TryMatch("file");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("empty");
        await Assert.That(r.Consumed).IsEqualTo(1); // just EndOfFile
    }

    // ═════════════════════════════════════════════════════════
    //  7. ManyOf on a rule where all patterns are compatible
    //      — ensure many doesn't have side effects on reader
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task ManyOf_EmptyBody_ReturnsZeroTokens() {
        var g = new Grammar<TestKind>();
        g.Define("item")
            .Pattern("word").Token(TestKind.Identifier).Commit();

        g.Define("file")
            .Pattern("empty-body").Many("item").Token(TestKind.EndOfFile).Commit();

        // No items to match — ManyOf returns empty array, EndOfFile consumed
        var m = new Matcher<TestKind>(g, new TestTokenizer(""));
        var r = m.TryMatch("file");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("empty-body");
        await Assert.That(r.Consumed).IsEqualTo(1);
    }

    // ═════════════════════════════════════════════════════════
    //  8. Optional containing Balanced (nested Optional)
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task Optional_BalancedInside() {
        var g = new Grammar<TestKind>();
        g.Define("decl")
            .Pattern("with-body").Token(TestKind.Entity)
                                  .Token(TestKind.Identifier)
                                  .Optional(new Balanced<TestKind>(
                                      TestKind.LBrace, TestKind.RBrace))
                                  .Commit();

        // With body
        var m1 = new Matcher<TestKind>(g, new TestTokenizer("entity Foo { }"));
        var r1 = m1.TryMatch("decl");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.PatternName).IsEqualTo("with-body");
        await Assert.That(r1.Consumed).IsEqualTo(4); // Entity + Identifier + LBrace/Content/RBrace

        // Without body
        var m2 = new Matcher<TestKind>(g, new TestTokenizer("entity Foo"));
        var r2 = m2.TryMatch("decl");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.PatternName).IsEqualTo("with-body");
        await Assert.That(r2.Consumed).IsEqualTo(2);
    }

    // ═════════════════════════════════════════════════════════
    //  9. Balanced hitting end-of-file (unterminated block)
    //      — tests the EOF guard to prevent infinite loop
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task Balanced_EndOfFile_ReturnsNull() {
        var g = new Grammar<TestKind>();
        g.Define("value")
            .Pattern("object").Balanced(TestKind.LBrace, TestKind.RBrace).Commit();

        // Opening brace but never closes — Balanced should bail at EOF
        var m = new Matcher<TestKind>(g, new TestTokenizer("{ "));
        var r = m.TryMatch("value");
        await Assert.That(r).IsNull();
    }

    // ═════════════════════════════════════════════════════════
    //  10. Scan loop over pure wildcards — greedy AnyToken
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task ScanLoop_PureWildcards() {
        var g = new Grammar<TestKind>();
        g.Define("token")
            .Pattern("any").Any().Commit();

        var reader = new TestTokenizer("a b c");
        var matcher = new Matcher<TestKind>(g, reader);

        var count = 0;
        while (matcher.TryMatch("token") is { } r) {
            count++;
            matcher.Consume(r);
        }

        // Three tokens, consumed one by one via wildcard
        await Assert.That(count).IsEqualTo(3);
    }

    // ═════════════════════════════════════════════════════════
    //  11. ManyOf followed by EndOfFile — full file consumption
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task ManyOf_ThenEndOfFile() {
        var g = new Grammar<TestKind>();
        g.Define("item")
            .Pattern("ident").Token(TestKind.Identifier).Commit();

        g.Define("file")
            .Pattern("idents").Many("item").Token(TestKind.EndOfFile).Commit();

        var reader = new TestTokenizer("alpha beta gamma");
        var matcher = new Matcher<TestKind>(g, reader);

        var result = matcher.TryMatch("file");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("idents");
        // 3 idents + 1 EndOfFile = 4
        await Assert.That(result.Consumed).IsEqualTo(4);
    }

    // ═════════════════════════════════════════════════════════
    //  12. Predicate + AnyToken in same pattern
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task PredicateThenAnyToken() {
        static bool IsType(TestKind k) => k == TestKind.Identifier;

        var g = new Grammar<TestKind>();
        g.Define("prop")
            .Pattern("typed").Predicate(IsType, "type").Any().Commit();

        var m = new Matcher<TestKind>(g, new TestTokenizer("Text :"));
        var r = m.TryMatch("prop");
        await Assert.That(r).IsNotNull();
        await Assert.That(r!.PatternName).IsEqualTo("typed");
        await Assert.That(r.Consumed).IsEqualTo(2);
        await Assert.That(r.Tokens[0].Kind).IsEqualTo(TestKind.Identifier);
        await Assert.That(r.Tokens[1].Kind).IsEqualTo(TestKind.Colon);
    }

    // ═════════════════════════════════════════════════════════
    //  13. Optional with non-first-element — Optional at end
    //      after a Balanced
    // ═════════════════════════════════════════════════════════
    [Test]
    public async Task Optional_AfterBalanced() {
        var g = new Grammar<TestKind>();
        g.Define("decl")
            .Pattern("entity").Token(TestKind.Entity).Token(TestKind.Identifier)
                              .Balanced(TestKind.LBrace, TestKind.RBrace)
                              .Optional(TestKind.Required)
                              .Commit();

        // With trailing optional
        var m1 = new Matcher<TestKind>(g, new TestTokenizer("entity Foo { } required"));
        var r1 = m1.TryMatch("decl");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.Consumed).IsEqualTo(5); // Entity + Id + { } + required = 5

        // Without trailing optional
        var m2 = new Matcher<TestKind>(g, new TestTokenizer("entity Foo { }"));
        var r2 = m2.TryMatch("decl");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(4);
    }
}