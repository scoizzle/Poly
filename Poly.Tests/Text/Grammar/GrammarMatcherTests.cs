using System.Text;

using Poly.Text.Grammar;

namespace Poly.Tests.Text.Grammar;

// ─── Test token kind for the mini-DSL ───────────────────────
enum TestKind {
    EndOfFile,
    Identifier,
    Number,
    String,
    Colon,
    Comma,
    LBrace,
    RBrace,
    Plus,
    Star,
    Entity,
    Stage,
    Action,
    Required,
    Unique,
}

// ─── Concrete string tokenizer for TestKind ─────────────────
sealed class TestTokenizer : StringTokenReader<TestKind> {
    private static readonly Dictionary<string, TestKind> _keywords = new(StringComparer.Ordinal) {
        ["entity"] = TestKind.Entity,
        ["stage"] = TestKind.Stage,
        ["action"] = TestKind.Action,
        ["required"] = TestKind.Required,
        ["unique"] = TestKind.Unique,
        ["true"] = TestKind.Identifier,
        ["false"] = TestKind.Identifier,
        ["Text"] = TestKind.Identifier,
        ["Number"] = TestKind.Identifier,
        ["Boolean"] = TestKind.Identifier,
    };

    public TestTokenizer(string text) : base(text) { }

    public override bool IsEndOfFile(TestKind kind) => kind == TestKind.EndOfFile;

    protected override Token<TestKind> ScanNextToken() {
        SkipWhitespace();
        if (Position >= Text.Length)
            return MakeToken(TestKind.EndOfFile, "");

        var ch = Text[Position];

        // Single-char tokens
        if (ch is ':' or ',' or '{' or '}' or '+' or '*') {
            AdvanceChar();
            return ch switch {
                ':' => MakeToken(TestKind.Colon, ":"),
                ',' => MakeToken(TestKind.Comma, ","),
                '{' => MakeToken(TestKind.LBrace, "{"),
                '}' => MakeToken(TestKind.RBrace, "}"),
                '+' => MakeToken(TestKind.Plus, "+"),
                '*' => MakeToken(TestKind.Star, "*"),
                _ => MakeToken(TestKind.Identifier, ch.ToString()),
            };
        }

        // Strings
        if (ch == '"') {
            var startLine = Line;
            var startCol = Column;
            AdvanceChar(); // skip "
            var sb = new StringBuilder();
            while (Position < Text.Length && Text[Position] != '"') {
                sb.Append(AdvanceChar());
            }
            if (Position < Text.Length) AdvanceChar(); // skip closing "
            return new Token<TestKind>(TestKind.String, sb.ToString(), startLine, startCol);
        }

        // Numbers
        if (char.IsDigit(ch)) {
            var startLine = Line;
            var startCol = Column;
            var sb = new StringBuilder();
            while (Position < Text.Length && char.IsDigit(Text[Position]))
                sb.Append(AdvanceChar());
            return new Token<TestKind>(TestKind.Number, sb.ToString(), startLine, startCol);
        }

        // Identifiers / keywords
        if (char.IsLetter(ch) || ch == '_') {
            var startLine = Line;
            var startCol = Column;
            var sb = new StringBuilder();
            while (Position < Text.Length && (char.IsLetterOrDigit(Text[Position]) || Text[Position] == '_'))
                sb.Append(AdvanceChar());
            var word = sb.ToString();
            var kind = _keywords.TryGetValue(word, out var k) ? k : TestKind.Identifier;
            return new Token<TestKind>(kind, word, startLine, startCol);
        }

        throw new GrammarException($"Unexpected character '{ch}'", Line, Column);
    }
}

// ─── Tests ──────────────────────────────────────────────────

public sealed class GrammarMatcherTests {
    // ═══════════════════════════════════════════════════════════
    //  1. Basic token matching
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task MatchToken_BasicProperty() {
        // Grammar: property = Identifier Colon Identifier
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier).Commit();

        var reader = new TestTokenizer("Name: Text");
        var matcher = new Matcher<TestKind>(g, reader);

        var result = matcher.TryMatch("entity-body");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("property");
        await Assert.That(result.Consumed).IsEqualTo(3);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("Name");
        await Assert.That(result.Tokens[1].Kind).IsEqualTo(TestKind.Colon);
        await Assert.That(result.Tokens[2].Text).IsEqualTo("Text");
    }

    // ═══════════════════════════════════════════════════════════
    //  2. Longest match disambiguation
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task LongestMatch_Wins() {
        // Two patterns share a prefix. Longest match should win.
        //   property = Identifier Colon Identifier          (3 tokens)
        //   stage    = Identifier Colon Stage LBrace RBrace (5 tokens)
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier).Commit()
            .Pattern("stage").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Stage).Token(TestKind.LBrace)
                              .Token(TestKind.RBrace).Commit();

        var reader = new TestTokenizer("Draft: stage { }");
        var matcher = new Matcher<TestKind>(g, reader);

        var result = matcher.TryMatch("entity-body");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("stage");
        await Assert.That(result.Consumed).IsEqualTo(5);

        // For a property line (no stage keyword), the shorter pattern should match
        var reader2 = new TestTokenizer("Name: Text");
        var matcher2 = new Matcher<TestKind>(g, reader2);
        var result2 = matcher2.TryMatch("entity-body");
        await Assert.That(result2).IsNotNull();
        await Assert.That(result2!.PatternName).IsEqualTo("property");
        await Assert.That(result2.Consumed).IsEqualTo(3);
    }

    // ═══════════════════════════════════════════════════════════
    //  3. Many — repeating body items
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Many_RepeatingBodyItems() {
        // entity-body items repeat. Each item is a property or stage.
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier).Commit()
            .Pattern("stage").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Stage).Token(TestKind.LBrace)
                              .Token(TestKind.RBrace).Commit();

        g.Define("file")
            .Pattern("entity").Token(TestKind.Entity).Token(TestKind.Identifier)
                            .Token(TestKind.LBrace).Many("entity-body")
                            .Token(TestKind.RBrace).Commit();

        var reader = new TestTokenizer("entity Order { Name: Text Draft: stage { } }");
        var matcher = new Matcher<TestKind>(g, reader);

        // Match the top-level entity
        var result = matcher.TryMatch("file");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("entity");
        // entity Order { Name: Text(3) Draft: stage { }(5) } = 2 + 3 + 5 + 2 = 12 tokens
        await Assert.That(result.Consumed).IsEqualTo(12);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("entity");
        await Assert.That(result.Tokens[1].Text).IsEqualTo("Order");
        await Assert.That(result.Tokens[2].Kind).IsEqualTo(TestKind.LBrace);
        await Assert.That(result.Tokens[^1].Kind).IsEqualTo(TestKind.RBrace);
    }

    // ═══════════════════════════════════════════════════════════
    //  4. Balanced brace groups
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Balanced_NestedBraces() {
        // Use Balanced element instead of explicit LBrace Many RBrace
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier).Commit();

        g.Define("file")
            .Pattern("entity").Token(TestKind.Entity).Token(TestKind.Identifier)
                            .Balanced(TestKind.LBrace, TestKind.RBrace).Commit();

        var reader = new TestTokenizer("entity Order { Name: Text { inner } More: Number }");
        var matcher = new Matcher<TestKind>(g, reader);

        var result = matcher.TryMatch("file");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("entity");
        // entity + Order + { Name : Text { inner } More : Number } = 13 tokens
        await Assert.That(result.Consumed).IsEqualTo(13);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("entity");
        await Assert.That(result.Tokens[^1].Kind).IsEqualTo(TestKind.RBrace);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. Optional elements
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Optional_Element() {
        // property = Identifier Colon Identifier [Required]
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier)
                              .Optional(TestKind.Required).Commit();

        // Without optional suffix
        var reader1 = new TestTokenizer("Name: Text");
        var m1 = new Matcher<TestKind>(g, reader1);
        var r1 = m1.TryMatch("entity-body");
        await Assert.That(r1).IsNotNull();
        await Assert.That(r1!.Consumed).IsEqualTo(3);

        // With optional suffix
        var reader2 = new TestTokenizer("Name: Text required");
        var m2 = new Matcher<TestKind>(g, reader2);
        var r2 = m2.TryMatch("entity-body");
        await Assert.That(r2).IsNotNull();
        await Assert.That(r2!.Consumed).IsEqualTo(4);
        await Assert.That(r2.Tokens[3].Kind).IsEqualTo(TestKind.Required);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. Predicate matching
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Predicate_Matching() {
        // Use a predicate to match any primitive type keyword
        static bool IsPrimitive(TestKind k) => k is TestKind.Identifier;
        // Here "Text", "Number", "Boolean" are all Identifier tokens

        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Predicate(IsPrimitive, "type").Commit();

        var reader = new TestTokenizer("Name: Text");
        var matcher = new Matcher<TestKind>(g, reader);

        var result = matcher.TryMatch("entity-body");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Consumed).IsEqualTo(3);
        await Assert.That(result.Tokens[2].Text).IsEqualTo("Text");
    }

    // ═══════════════════════════════════════════════════════════
    //  7. No match returns null
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task NoMatch_ReturnsNull() {
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier).Commit();

        // "stage}" is not a valid property (no Identifier first)
        var reader = new TestTokenizer("stage { }");
        var matcher = new Matcher<TestKind>(g, reader);

        var result = matcher.TryMatch("entity-body");
        await Assert.That(result).IsNull();
    }

    // ═══════════════════════════════════════════════════════════
    //  8. Expected tokens introspection
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task ExpectedTokens_ReturnsFirstTokenOfEachPattern() {
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier).Commit()
            .Pattern("stage").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Stage).Commit();

        var matcher = new Matcher<TestKind>(g, new TestTokenizer(""));
        var expected = matcher.ExpectedTokens("entity-body").ToList();

        await Assert.That(expected.Count).IsEqualTo(1); // both start with Identifier
        await Assert.That(expected).Contains(TestKind.Identifier);
    }

    // ═══════════════════════════════════════════════════════════
    //  9. Scan loop — complete file parsing
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task ScanLoop_ParsesEntityBody() {
        // Demonstrate the scan loop: repeatedly TryMatch + Consume
        var g = new Grammar<TestKind>();
        g.Define("entity-body")
            .Pattern("property").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Identifier).Commit()
            .Pattern("stage").Token(TestKind.Identifier).Token(TestKind.Colon)
                              .Token(TestKind.Stage).Commit();

        var reader = new TestTokenizer("Name: Text Draft: stage Count: Number");
        var matcher = new Matcher<TestKind>(g, reader);

        var names = new List<string>();
        while (true) {
            var result = matcher.TryMatch("entity-body");
            if (result == null) break;
            names.Add(result.PatternName);
            matcher.Consume(result);
        }

        await Assert.That(names).IsEquivalentTo(["property", "stage", "property"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  10. Sorted order — same first-token, longer pattern first
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Sorted_LongerPatternFirst_WithinSameTokenGroup() {
        // Two patterns with same first token (Identifier), different lengths.
        // The patterns are registered short-first, but should be sorted
        // long-first. ManyOf relies on this: it takes the first match, so
        // the longer AB pattern must be tried before the shorter A pattern.
        var g = new Grammar<TestKind>();
        g.Define("body")
            .Pattern("short") // Identifier only
                .Token(TestKind.Identifier).Commit()
            .Pattern("long")  // Identifier Colon Identifier
                .Token(TestKind.Identifier).Token(TestKind.Colon)
                .Token(TestKind.Identifier).Commit();

        var patterns = g.GetPatterns("body");
        await Assert.That(patterns.Count).IsEqualTo(2);
        // "long" (3 elements) should come before "short" (1 element)
        await Assert.That(patterns[0].Name).IsEqualTo("long");
        await Assert.That(patterns[1].Name).IsEqualTo("short");
    }

    // ═══════════════════════════════════════════════════════════
    //  11. Sorted order — different first-tokens grouped by kind
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Sorted_GroupedByFirstTokenKind() {
        var g = new Grammar<TestKind>();
        g.Define("body")
            .Pattern("z").Token(TestKind.Number).Commit()
            .Pattern("a").Token(TestKind.Identifier).Token(TestKind.Colon)
                        .Token(TestKind.Identifier).Commit()
            .Pattern("b").Token(TestKind.Identifier).Commit();

        var patterns = g.GetPatterns("body");
        // Identifier group (a, b) comes first, then Number group (z)
        // Within Identifier group: "a" (longer) before "b" (shorter)
        await Assert.That(patterns[0].Name).IsEqualTo("a");
        await Assert.That(patterns[1].Name).IsEqualTo("b");
        await Assert.That(patterns[2].Name).IsEqualTo("z");
    }

    // ═══════════════════════════════════════════════════════════
    //  12. ManyOf benefits from sorted order
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task ManyOf_UsesSortedOrder() {
        // ManyOf iterates sub-patterns and takes the first match.
        // With sorted patterns, the longest pattern for a given first
        // token is tried first, so the greedy match succeeds.
        var g = new Grammar<TestKind>();
        g.Define("item")
            .Pattern("short").Token(TestKind.Identifier).Commit()
            .Pattern("medium").Token(TestKind.Identifier)
                               .Token(TestKind.Colon).Commit()
            .Pattern("long").Token(TestKind.Identifier).Token(TestKind.Colon)
                               .Token(TestKind.Identifier).Commit();

        g.Define("file")
            .Pattern("items").Many("item").Token(TestKind.EndOfFile).Commit();

        // "stage" is a keyword token (TestKind.Stage), not Identifier.
        // Use non-keyword identifiers instead.
        var reader = new TestTokenizer("alpha: beta gamma: delta");
        var matcher = new Matcher<TestKind>(g, reader);

        var result = matcher.TryMatch("file");
        await Assert.That(result).IsNotNull();
        // Sorted order = long (3) > medium (2) > short (1).
        // "alpha: beta" matches "long" (not "medium" or "short").
        // "gamma: delta" matches "long" (Identifier Colon Identifier).
        // 2 long matches (3 tokens each) + 1 EndOfFile = 7 tokens
        await Assert.That(result!.Consumed).IsEqualTo(7);
    }
}