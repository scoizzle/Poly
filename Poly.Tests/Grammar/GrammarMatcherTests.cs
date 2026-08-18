using Poly.DomainModeling.Ontology;
using Poly.Grammar;

// Shared TestKind/TestTokenizer from TestGrammar.cs (mirrors v1 where the
// matcher tests own them).

namespace Poly.Tests.Grammar;

/// <summary>
/// The breadth suite: basic token matching,
/// longest-match disambiguation, Repeat over body items, Balanced nesting,
/// Optional, Predicate, no-match, ExpectedTokens introspection, scan loops,
/// and the two pattern-sort invariants (longer-first within a kind group;
/// kind groups ordered).
/// </summary>
public sealed class GrammarMatcherTests {
    private static Grammar<TestToken, TestKind> EntityBodyGrammar() =>
        new GrammarBuilder<TestToken, TestKind>()
            .Define("entity-body")
            .Pattern("property").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                              .Kind(TestKind.Identifier).Commit()
            .Pattern("stage").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                              .Kind(TestKind.Stage).Kind(TestKind.LBrace)
                              .Kind(TestKind.RBrace).Commit()
            .Build();

    // ── 1. Basic token matching ──
    [Test]
    public async Task MatchToken_BasicProperty() {
        var g = EntityBodyGrammar();
        var matcher = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name: Text"));

        var result = matcher.TryMatch("entity-body");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("property");
        await Assert.That(result.Consumed).IsEqualTo(3);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("Name");
        await Assert.That(result.Tokens[1].Kind).IsEqualTo(TestKind.Colon);
        await Assert.That(result.Tokens[2].Text).IsEqualTo("Text");
    }

    // ── 2. Longest match disambiguation ──
    [Test]
    public async Task LongestMatch_Wins() {
        var g = EntityBodyGrammar();

        var result = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Draft: stage { }")).TryMatch("entity-body");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("stage");
        await Assert.That(result.Consumed).IsEqualTo(5);

        var result2 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name: Text")).TryMatch("entity-body");
        await Assert.That(result2).IsNotNull();
        await Assert.That(result2!.PatternName).IsEqualTo("property");
        await Assert.That(result2.Consumed).IsEqualTo(3);
    }

    // ── 3. Repeat — repeating body items ──
    [Test]
    public async Task Repeat_RepeatingBodyItems() {
        var g = EntityBodyGrammar()
            .ToBuilder()
            .Define("file")
            .Pattern("entity").Kind(TestKind.Entity).Kind(TestKind.Identifier)
                            .Kind(TestKind.LBrace).Repeat("entity-body")
                            .Kind(TestKind.RBrace).Commit()
            .Build();

        var result = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity Order { Name: Text Draft: stage { } }")).TryMatch("file");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("entity");
        // entity Order { Name: Text(3) Draft: stage { }(5) } = 2 + 3 + 5 + 2 = 12
        await Assert.That(result.Consumed).IsEqualTo(12);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("entity");
        await Assert.That(result.Tokens[^1].Kind).IsEqualTo(TestKind.RBrace);
    }

    // ── 4. Balanced nested braces ──
    [Test]
    public async Task Balanced_NestedBraces() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("file")
            .Pattern("entity").Kind(TestKind.Entity).Kind(TestKind.Identifier)
                            .Balanced(TestKind.LBrace, TestKind.RBrace).Commit()
            .Build();

        var result = new Matcher<TestToken, TestKind>(g, new TestTokenizer("entity Order { Name: Text { inner } More: Number }")).TryMatch("file");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("entity");
        await Assert.That(result.Consumed).IsEqualTo(13);
        await Assert.That(result.Tokens[^1].Kind).IsEqualTo(TestKind.RBrace);
    }

    // ── 5. Optional element ──
    [Test]
    public async Task Optional_Element() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("entity-body")
            .Pattern("property").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                              .Kind(TestKind.Identifier)
                              .Optional(new MatchKind<TestToken, TestKind>(TestKind.Required)).Commit()
            .Build();

        var r1 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name: Text")).TryMatch("entity-body");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.Consumed).IsEqualTo(3);

        var r2 = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name: Text required")).TryMatch("entity-body");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(4);
        await Assert.That(r2.Tokens[3].Kind).IsEqualTo(TestKind.Required);
    }

    // ── 6. Predicate matching (content-aware) ──
    [Test]
    public async Task Predicate_Matching() {
        static bool IsPrimitive(TestToken t) => t.Kind == TestKind.Identifier;

        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("entity-body")
            .Pattern("property").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                              .Predicate(IsPrimitive, "type").Commit()
            .Build();

        var result = new Matcher<TestToken, TestKind>(g, new TestTokenizer("Name: Text")).TryMatch("entity-body");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Consumed).IsEqualTo(3);
        await Assert.That(result.Tokens[2].Text).IsEqualTo("Text");
    }

    // ── 7. No match returns null ──
    [Test]
    public async Task NoMatch_ReturnsNull() {
        var g = EntityBodyGrammar();
        var result = new Matcher<TestToken, TestKind>(g, new TestTokenizer("stage { }")).TryMatch("entity-body");
        await Assert.That(result).IsNull();
    }

    // ── 8. Expected tokens introspection ──
    [Test]
    public async Task ExpectedTokens_ReturnsFirstTokenOfEachPattern() {
        var g = EntityBodyGrammar();
        var matcher = new Matcher<TestToken, TestKind>(g, new TestTokenizer(""));
        var expected = matcher.ExpectedTokens("entity-body").ToList();

        await Assert.That(expected.Count).IsEqualTo(1); // both start with Identifier
        await Assert.That(expected).Contains(TestKind.Identifier);
    }

    // ── 9. Scan loop — complete body parsing ──
    [Test]
    public async Task ScanLoop_ParsesEntityBody() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("entity-body")
            .Pattern("property").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                              .Kind(TestKind.Identifier).Commit()
            .Pattern("stage").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                              .Kind(TestKind.Stage).Commit()
            .Build();

        var reader = new TestTokenizer("Name: Text Draft: stage Count: Number");
        var matcher = new Matcher<TestToken, TestKind>(g, reader);

        var names = new List<string>();
        while (true) {
            var result = matcher.TryMatch("entity-body");
            if (result == null) break;
            names.Add(result.PatternName);
            reader.Consume(result.Consumed);
        }
        await Assert.That(names).IsEquivalentTo(["property", "stage", "property"]);
    }

    // ── 10. Sorted: longer pattern first within same kind group ──
    [Test]
    public async Task Sorted_LongerPatternFirst_WithinSameTokenGroup() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("body")
            .Pattern("short").Kind(TestKind.Identifier).Commit()
            .Pattern("long").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                            .Kind(TestKind.Identifier).Commit()
            .Build();

        var patterns = g.GetPatterns("body");
        await Assert.That(patterns.Count).IsEqualTo(2);
        await Assert.That(patterns[0].Name).IsEqualTo("long");
        await Assert.That(patterns[1].Name).IsEqualTo("short");
    }

    // ── 11. Sorted: grouped by first kind ──
    [Test]
    public async Task Sorted_GroupedByFirstTokenKind() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("body")
            .Pattern("z").Kind(TestKind.Number).Commit()
            .Pattern("a").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                        .Kind(TestKind.Identifier).Commit()
            .Pattern("b").Kind(TestKind.Identifier).Commit()
            .Build();

        var patterns = g.GetPatterns("body");
        await Assert.That(patterns[0].Name).IsEqualTo("a");
        await Assert.That(patterns[1].Name).IsEqualTo("b");
        await Assert.That(patterns[2].Name).IsEqualTo("z");
    }

    // ── 12. Repeat benefits from sorted order ──
    [Test]
    public async Task Repeat_UsesSortedOrder() {
        var g = new GrammarBuilder<TestToken, TestKind>()
            .Define("item")
            .Pattern("short").Kind(TestKind.Identifier).Commit()
            .Pattern("medium").Kind(TestKind.Identifier).Kind(TestKind.Colon).Commit()
            .Pattern("long").Kind(TestKind.Identifier).Kind(TestKind.Colon)
                            .Kind(TestKind.Identifier).Commit()
            .Define("file")
            .Pattern("items").Repeat("item").Kind(TestKind.EndOfFile).Commit()
            .Build();

        var result = new Matcher<TestToken, TestKind>(g, new TestTokenizer("alpha: beta gamma: delta")).TryMatch("file");
        await Assert.That(result).IsNotNull();
        // 2 long matches (3 tokens each) + EndOfFile = 7
        await Assert.That(result!.Consumed).IsEqualTo(7);
    }
}