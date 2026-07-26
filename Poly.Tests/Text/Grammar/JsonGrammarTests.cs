using System.Text;

using Poly.Text.Grammar;

namespace Poly.Tests.Text.Grammar;

// ─── JSON token kind ───────────────────────────────────────
enum JsonKind {
    EndOfFile,
    String,
    Number,
    True,
    False,
    Null,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Colon,
    Comma,
}

// ─── JSON tokenizer over StringTokenReader ─────────────────
sealed class JsonTokenizer : StringTokenReader<JsonKind> {
    private static readonly Dictionary<string, JsonKind> _keywords = new(StringComparer.Ordinal) {
        ["true"] = JsonKind.True,
        ["false"] = JsonKind.False,
        ["null"] = JsonKind.Null,
    };

    public JsonTokenizer(string text) : base(text) { }

    public override bool IsEndOfFile(JsonKind kind) => kind == JsonKind.EndOfFile;

    protected override Token<JsonKind> ScanNextToken() {
        SkipWhitespace();
        if (Position >= Text.Length)
            return MakeToken(JsonKind.EndOfFile, "");

        var ch = Text[Position];

        // Single-char tokens
        if (ch is '{' or '}' or '[' or ']' or ':' or ',') {
            AdvanceChar();
            return ch switch {
                '{' => MakeToken(JsonKind.LBrace, "{"),
                '}' => MakeToken(JsonKind.RBrace, "}"),
                '[' => MakeToken(JsonKind.LBracket, "["),
                ']' => MakeToken(JsonKind.RBracket, "]"),
                ':' => MakeToken(JsonKind.Colon, ":"),
                ',' => MakeToken(JsonKind.Comma, ","),
                _ => MakeToken(JsonKind.String, ch.ToString()),
            };
        }

        // String literals
        if (ch == '"') {
            var startLine = Line;
            var startCol = Column;
            AdvanceChar(); // skip opening "
            var sb = new StringBuilder();
            while (Position < Text.Length && Text[Position] != '"') {
                if (Text[Position] == '\\' && Position + 1 < Text.Length) {
                    sb.Append(AdvanceChar()); // backslash
                    sb.Append(AdvanceChar()); // escaped char
                }
                else {
                    sb.Append(AdvanceChar());
                }
            }
            if (Position < Text.Length) AdvanceChar(); // skip closing "
            return new Token<JsonKind>(JsonKind.String, sb.ToString(), startLine, startCol);
        }

        // Numbers (integers and decimals)
        if (char.IsDigit(ch) || ch == '-') {
            var startLine = Line;
            var startCol = Column;
            var sb = new StringBuilder();
            // optional minus
            if (ch == '-') sb.Append(AdvanceChar());
            // integer part
            while (Position < Text.Length && char.IsDigit(Text[Position]))
                sb.Append(AdvanceChar());
            // optional fractional part
            if (Position < Text.Length && Text[Position] == '.') {
                sb.Append(AdvanceChar());
                while (Position < Text.Length && char.IsDigit(Text[Position]))
                    sb.Append(AdvanceChar());
            }
            return new Token<JsonKind>(JsonKind.Number, sb.ToString(), startLine, startCol);
        }

        // Identifiers / keywords
        if (char.IsLetter(ch) || ch == '_') {
            var startLine = Line;
            var startCol = Column;
            var sb = new StringBuilder();
            while (Position < Text.Length && (char.IsLetterOrDigit(Text[Position]) || Text[Position] == '_'))
                sb.Append(AdvanceChar());
            var word = sb.ToString();
            var kind = _keywords.TryGetValue(word, out var k) ? k : JsonKind.String; // treat unknown as string
            return new Token<JsonKind>(kind, word, startLine, startCol);
        }

        throw new GrammarException($"Unexpected character '{ch}' in JSON", Line, Column);
    }
}

// ─── Tests ─────────────────────────────────────────────────

public sealed class JsonGrammarTests {
    private static Grammar<JsonKind> JsonValueGrammar() {
        var g = new Grammar<JsonKind>();
        g.Define("value")
            .Pattern("string").Token(JsonKind.String).Commit()
            .Pattern("number").Token(JsonKind.Number).Commit()
            .Pattern("true").Token(JsonKind.True).Commit()
            .Pattern("false").Token(JsonKind.False).Commit()
            .Pattern("null").Token(JsonKind.Null).Commit()
            // Balanced already consumes both delimiters: no extra Token() calls needed
            .Pattern("object").Balanced(JsonKind.LBrace, JsonKind.RBrace).Commit()
            .Pattern("array").Balanced(JsonKind.LBracket, JsonKind.RBracket).Commit();
        return g;
    }

    // ═══════════════════════════════════════════════════════════
    //  1. String value
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task StringValue() {
        var g = JsonValueGrammar();
        var reader = new JsonTokenizer(@"""hello world""");
        var matcher = new Matcher<JsonKind>(g, reader);

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("string");
        await Assert.That(result.Consumed).IsEqualTo(1);
        await Assert.That(result.Tokens[0].Kind).IsEqualTo(JsonKind.String);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("hello world");
    }

    // ═══════════════════════════════════════════════════════════
    //  2. Number value (integer and decimal)
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task NumberValue() {
        var g = JsonValueGrammar();
        var matcher = new Matcher<JsonKind>(g, new JsonTokenizer("42"));

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("number");
        await Assert.That(result.Tokens[0].Text).IsEqualTo("42");
    }

    [Test]
    public async Task DecimalValue() {
        var g = JsonValueGrammar();
        var matcher = new Matcher<JsonKind>(g, new JsonTokenizer("3.14"));

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("number");
        await Assert.That(result.Tokens[0].Text).IsEqualTo("3.14");
    }

    [Test]
    public async Task NegativeNumber() {
        var g = JsonValueGrammar();
        var matcher = new Matcher<JsonKind>(g, new JsonTokenizer("-7"));

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("number");
        await Assert.That(result.Tokens[0].Text).IsEqualTo("-7");
    }

    // ═══════════════════════════════════════════════════════════
    //  3. Boolean and null keywords
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task BooleanAndNullValues() {
        var g = JsonValueGrammar();
        foreach (var (input, expectedPattern, expectedKind) in new[] {
            ("true",  "true",  JsonKind.True),
            ("false", "false", JsonKind.False),
            ("null",  "null",  JsonKind.Null),
        }) {
            var matcher = new Matcher<JsonKind>(g, new JsonTokenizer(input));
            var result = matcher.TryMatch("value");
            await Assert.That(result).IsNotNull().Because($"Expected match for '{input}'");
            await Assert.That(result!.PatternName).IsEqualTo(expectedPattern);
            await Assert.That(result.Tokens[0].Kind).IsEqualTo(expectedKind);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  4. Empty object
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task EmptyObject() {
        var g = JsonValueGrammar();
        var matcher = new Matcher<JsonKind>(g, new JsonTokenizer("{}"));

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        // Balanced consumes { } = 2 tokens (open + close)
        await Assert.That(result.Consumed).IsEqualTo(2);
        await Assert.That(result.Tokens[0].Kind).IsEqualTo(JsonKind.LBrace);
        await Assert.That(result.Tokens[1].Kind).IsEqualTo(JsonKind.RBrace);
    }

    // ═══════════════════════════════════════════════════════════
    //  5. Object with content
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task ObjectWithContent() {
        var g = JsonValueGrammar();
        var reader = new JsonTokenizer(@"{""name"": ""hello""}");
        var matcher = new Matcher<JsonKind>(g, reader);

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        // Balanced consumes { "name" : "hello" } = 5 tokens (open + 3 content + close)
        await Assert.That(result.Consumed).IsEqualTo(5);
    }

    // ═══════════════════════════════════════════════════════════
    //  6. Nested structure: object containing array
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task NestedObjectWithArray() {
        var g = JsonValueGrammar();
        var reader = new JsonTokenizer(@"{""items"": [1, 2, 3]}");
        var matcher = new Matcher<JsonKind>(g, reader);

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        // Balanced consumes { "items" : [ 1 , 2 , 3 ] } = 11 tokens
        await Assert.That(result.Consumed).IsEqualTo(11);
    }

    // ═══════════════════════════════════════════════════════════
    //  7. Empty array
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task EmptyArray() {
        var g = JsonValueGrammar();
        var matcher = new Matcher<JsonKind>(g, new JsonTokenizer("[]"));

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("array");
        await Assert.That(result.Consumed).IsEqualTo(2);
        await Assert.That(result.Tokens[0].Kind).IsEqualTo(JsonKind.LBracket);
    }

    // ═══════════════════════════════════════════════════════════
    //  8. Array with elements
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task ArrayWithElements() {
        var g = JsonValueGrammar();
        var reader = new JsonTokenizer(@"[1, ""two"", true]");
        var matcher = new Matcher<JsonKind>(g, reader);

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("array");
        // Balanced [ 1 , "two" , true ] = 7 tokens
        await Assert.That(result.Consumed).IsEqualTo(7);
    }

    // ═══════════════════════════════════════════════════════════
    //  9. Longest match: object wins over empty object pattern…
    //     Actually all value patterns have distinct first tokens, so
    //     no ambiguity. But we verify the dispatch is correct.
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task DistinctFirstTokens_DispatchesCorrectly() {
        var g = JsonValueGrammar();
        var inputs = new Dictionary<string, string> {
            [@"""x"""] = "string",
            ["42"] = "number",
            ["true"] = "true",
            ["false"] = "false",
            ["null"] = "null",
            ["{}"] = "object",
            ["[]"] = "array",
        };

        foreach (var (input, expected) in inputs) {
            var matcher = new Matcher<JsonKind>(g, new JsonTokenizer(input));
            var result = matcher.TryMatch("value");
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.PatternName).IsEqualTo(expected);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  10. No match for invalid JSON
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task NoMatch_ForInvalidInput() {
        var g = JsonValueGrammar();
        // A bare keyword-like word that doesn't match any known token is
        // tokenized as String (the fallback), which DOES match the string
        // pattern. So instead test with an empty input meaning no value.
        var matcher = new Matcher<JsonKind>(g, new JsonTokenizer(""));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNull();
    }

    // ═══════════════════════════════════════════════════════════
    //  11. Expected tokens introspection
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task ExpectedTokens_ReturnsAllValueStarters() {
        var g = JsonValueGrammar();
        var matcher = new Matcher<JsonKind>(g, new JsonTokenizer(""));
        var expected = matcher.ExpectedTokens("value").OrderBy(k => k.ToString()).ToList();

        // Balanced and AnyToken elements don't contribute to ExpectedTokens
        // (only MatchToken first-elements are reported). The object and array
        // patterns start with Balanced, so they don't appear here.
        await Assert.That(expected.Count).IsEqualTo(5);
        await Assert.That(expected).Contains(JsonKind.String);
        await Assert.That(expected).Contains(JsonKind.Number);
        await Assert.That(expected).Contains(JsonKind.True);
        await Assert.That(expected).Contains(JsonKind.False);
        await Assert.That(expected).Contains(JsonKind.Null);
    }

    // ═══════════════════════════════════════════════════════════
    //  12. Scan loop: top-level values
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task ScanLoop_TopLevelValues() {
        var g = JsonValueGrammar();
        var reader = new JsonTokenizer(@"""a"" 42 true null");
        var matcher = new Matcher<JsonKind>(g, reader);

        var patterns = new List<string>();
        while (true) {
            var result = matcher.TryMatch("value");
            if (result == null) break;
            patterns.Add(result.PatternName);
            matcher.Consume(result);
        }

        await Assert.That(patterns).IsEquivalentTo(["string", "number", "true", "null"]);
    }

    // ═══════════════════════════════════════════════════════════
    //  13. Balanced tracks nesting depth — not a flat counter
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Balanced_TracksNestingDepth() {
        var g = JsonValueGrammar();
        // Array nested inside object — Balanced(LBrace,RBrace) must not
        // stop at the first RBrace (which closes the inner array's bracket).
        // It tracks only matching delimiter pairs.
        var reader = new JsonTokenizer(@"{""a"": [1, 2]}");
        var matcher = new Matcher<JsonKind>(g, reader);

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        // Balanced consumes { "a" : [ 1 , 2 ] } = 9 tokens
        // The inner RBracket (] ) does NOT decrement the { } depth counter.
        await Assert.That(result.Consumed).IsEqualTo(9);
        var braces = result.Tokens.Where(t => t.Kind is JsonKind.LBrace or JsonKind.RBrace).ToList();
        await Assert.That(braces.Count).IsEqualTo(2); // only { }
    }

    // ═══════════════════════════════════════════════════════════
    //  14. Object in object — nested braces depth counting
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Balanced_ObjectWithNestedBraces() {
        var g = JsonValueGrammar();
        var reader = new JsonTokenizer(@"{""a"": {""b"": 1}}");
        var matcher = new Matcher<JsonKind>(g, reader);

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        // Balanced consumes from first { through the final } counting nesting
        // = 9 tokens
        await Assert.That(result.Consumed).IsEqualTo(9);
        // Verify both inner and outer braces were consumed
        var braces = result.Tokens.Where(t => t.Kind is JsonKind.LBrace or JsonKind.RBrace).ToList();
        await Assert.That(braces.Count).IsEqualTo(4); // { { } }
    }

    // ═══════════════════════════════════════════════════════════
    //  15. Fail-closed: unmatched close brace
    // ═══════════════════════════════════════════════════════════
    [Test]
    public async Task Balanced_UnmatchedClose_DoesNotMatch() {
        var g = JsonValueGrammar();
        // A bare } is not a valid object start
        var reader = new JsonTokenizer("}");
        var matcher = new Matcher<JsonKind>(g, reader);

        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNull();
    }
}