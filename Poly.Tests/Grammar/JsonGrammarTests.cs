using System.Text;

using Poly.Grammar;

namespace Poly.Tests.Grammar;

// ─── JSON token kind + tokenizer ──
public enum JsonKind {
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

public readonly record struct JsonToken(JsonKind Kind, string Text) : IToken<JsonKind>;

public sealed class JsonTokenizer : BufferedTokenReader<JsonToken, JsonKind> {
    private static readonly Dictionary<string, JsonKind> _keywords = new(StringComparer.Ordinal) {
        ["true"] = JsonKind.True,
        ["false"] = JsonKind.False,
        ["null"] = JsonKind.Null,
    };

    private readonly string _text;
    private int _pos;

    public JsonTokenizer(string text) => _text = text;

    public override bool EndOfStream(JsonKind kind) => kind == JsonKind.EndOfFile;

    protected override JsonToken ScanNextToken() {
        SkipWhitespace();
        if (_pos >= _text.Length)
            return new JsonToken(JsonKind.EndOfFile, "");

        var ch = _text[_pos];

        if (ch is '{' or '}' or '[' or ']' or ':' or ',') {
            _pos++;
            return ch switch {
                '{' => new JsonToken(JsonKind.LBrace, "{"),
                '}' => new JsonToken(JsonKind.RBrace, "}"),
                '[' => new JsonToken(JsonKind.LBracket, "["),
                ']' => new JsonToken(JsonKind.RBracket, "]"),
                ':' => new JsonToken(JsonKind.Colon, ":"),
                ',' => new JsonToken(JsonKind.Comma, ","),
                _ => new JsonToken(JsonKind.String, ch.ToString()),
            };
        }

        if (ch == '"') {
            _pos++; // skip opening "
            var sb = new StringBuilder();
            while (_pos < _text.Length && _text[_pos] != '"') {
                if (_text[_pos] == '\\' && _pos + 1 < _text.Length) {
                    sb.Append(_text[_pos]);
                    _pos++;
                    sb.Append(_text[_pos]);
                    _pos++;
                }
                else {
                    sb.Append(_text[_pos]);
                    _pos++;
                }
            }
            if (_pos < _text.Length) _pos++; // skip closing "
            return new JsonToken(JsonKind.String, sb.ToString());
        }

        if (char.IsDigit(ch) || ch == '-') {
            var sb = new StringBuilder();
            if (ch == '-') { sb.Append(_text[_pos]); _pos++; }
            while (_pos < _text.Length && char.IsDigit(_text[_pos])) { sb.Append(_text[_pos]); _pos++; }
            if (_pos < _text.Length && _text[_pos] == '.') {
                sb.Append(_text[_pos]); _pos++;
                while (_pos < _text.Length && char.IsDigit(_text[_pos])) { sb.Append(_text[_pos]); _pos++; }
            }
            return new JsonToken(JsonKind.Number, sb.ToString());
        }

        if (char.IsLetter(ch) || ch == '_') {
            var sb = new StringBuilder();
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) {
                sb.Append(_text[_pos]);
                _pos++;
            }
            var word = sb.ToString();
            var kind = _keywords.TryGetValue(word, out var k) ? k : JsonKind.String;
            return new JsonToken(kind, word);
        }

        throw GrammarError.Error($"Unexpected character '{ch}' in JSON");
    }

    private void SkipWhitespace() {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
    }
}

public sealed class JsonGrammarTests {
    private static Grammar<JsonToken, JsonKind> JsonValueGrammar() {
        return new GrammarBuilder<JsonToken, JsonKind>()
            .Define("value")
            .Pattern("string").Kind(JsonKind.String).Commit()
            .Pattern("number").Kind(JsonKind.Number).Commit()
            .Pattern("true").Kind(JsonKind.True).Commit()
            .Pattern("false").Kind(JsonKind.False).Commit()
            .Pattern("null").Kind(JsonKind.Null).Commit()
            .Pattern("object").Balanced(JsonKind.LBrace, JsonKind.RBrace).Commit()
            .Pattern("array").Balanced(JsonKind.LBracket, JsonKind.RBracket).Commit()
            .Build();
    }

    [Test]
    public async Task StringValue() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer(@"""hello world"""));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("string");
        await Assert.That(result.RuleName).IsEqualTo("value");
        await Assert.That(result.Consumed).IsEqualTo(1);
        await Assert.That(result.Tokens[0].Kind).IsEqualTo(JsonKind.String);
        await Assert.That(result.Tokens[0].Text).IsEqualTo("hello world");
        await Assert.That(result.Children).IsEmpty();
        await Assert.That(result.Operators).IsEmpty();
    }

    [Test]
    public async Task NumberValue() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer("42"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("number");
        await Assert.That(result.Tokens[0].Text).IsEqualTo("42");
    }

    [Test]
    public async Task DecimalValue() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer("3.14"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("number");
        await Assert.That(result.Tokens[0].Text).IsEqualTo("3.14");
    }

    [Test]
    public async Task NegativeNumber() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer("-7"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("number");
        await Assert.That(result.Tokens[0].Text).IsEqualTo("-7");
    }

    [Test]
    public async Task BooleanAndNullValues() {
        var g = JsonValueGrammar();
        foreach (var (input, expectedPattern, expectedKind) in new[] {
            ("true",  "true",  JsonKind.True),
            ("false", "false", JsonKind.False),
            ("null",  "null",  JsonKind.Null),
        }) {
            var matcher = new Matcher<JsonToken, JsonKind>(g, new JsonTokenizer(input));
            var result = matcher.TryMatch("value");
            await Assert.That(result).IsNotNull().Because($"Expected match for '{input}'");
            await Assert.That(result!.PatternName).IsEqualTo(expectedPattern);
            await Assert.That(result.Tokens[0].Kind).IsEqualTo(expectedKind);
        }
    }

    [Test]
    public async Task EmptyObject() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer("{}"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        await Assert.That(result.Consumed).IsEqualTo(2);
        await Assert.That(result.Tokens[0].Kind).IsEqualTo(JsonKind.LBrace);
        await Assert.That(result.Tokens[1].Kind).IsEqualTo(JsonKind.RBrace);
        await Assert.That(result.Children).IsEmpty();
    }

    [Test]
    public async Task ObjectWithContent() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer(@"{""name"": ""hello""}"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        await Assert.That(result.Consumed).IsEqualTo(5);
    }

    [Test]
    public async Task NestedObjectWithArray() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer(@"{""items"": [1, 2, 3]}"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        await Assert.That(result.Consumed).IsEqualTo(11);
    }

    [Test]
    public async Task EmptyArray() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer("[]"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("array");
        await Assert.That(result.Consumed).IsEqualTo(2);
        await Assert.That(result.Tokens[0].Kind).IsEqualTo(JsonKind.LBracket);
    }

    [Test]
    public async Task ArrayWithElements() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer(@"[1, ""two"", true]"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("array");
        await Assert.That(result.Consumed).IsEqualTo(7);
    }

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
            var matcher = new Matcher<JsonToken, JsonKind>(g, new JsonTokenizer(input));
            var result = matcher.TryMatch("value");
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.PatternName).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task NoMatch_ForInvalidInput() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer(""));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ScanLoop_TopLevelValues() {
        var g = JsonValueGrammar();
        var reader = new JsonTokenizer(@"""a"" 42 true null");
        var matcher = new Matcher<JsonToken, JsonKind>(g, reader);

        var patterns = new List<string>();
        while (true) {
            var result = matcher.TryMatch("value");
            if (result == null) break;
            patterns.Add(result.PatternName);
            reader.Consume(result.Consumed);
        }
        await Assert.That(patterns).IsEquivalentTo(["string", "number", "true", "null"]);
    }

    [Test]
    public async Task Balanced_TracksNestingDepth() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer(@"{""a"": [1, 2]}"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        await Assert.That(result.Consumed).IsEqualTo(9);
        var braces = result.Tokens.Where(t => t.Kind is JsonKind.LBrace or JsonKind.RBrace).ToList();
        await Assert.That(braces.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Balanced_ObjectWithNestedBraces() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer(@"{""a"": {""b"": 1}}"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.PatternName).IsEqualTo("object");
        await Assert.That(result.Consumed).IsEqualTo(9);
        var braces = result.Tokens.Where(t => t.Kind is JsonKind.LBrace or JsonKind.RBrace).ToList();
        await Assert.That(braces.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Balanced_UnmatchedClose_DoesNotMatch() {
        var matcher = new Matcher<JsonToken, JsonKind>(JsonValueGrammar(), new JsonTokenizer("}"));
        var result = matcher.TryMatch("value");
        await Assert.That(result).IsNull();
    }
}