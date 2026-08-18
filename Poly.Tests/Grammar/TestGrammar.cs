using System.Text;

using Poly.DomainModeling.Ontology;
using Poly.Grammar;

namespace Poly.Tests.Grammar;

// ─── Shared test grammar for the Grammar engine (TestKind + tokenizer) ──
// Used by the edge-case and matcher test suites.

internal enum TestKind {
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

internal readonly record struct TestToken(TestKind Kind, string Text) : IToken<TestKind>;

internal sealed class TestTokenizer : BufferedTokenReader<TestToken, TestKind> {
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

    private readonly string _text;
    private int _pos;

    public TestTokenizer(string text) => _text = text;

    public override bool EndOfStream(TestKind kind) => kind == TestKind.EndOfFile;

    protected override TestToken ScanNextToken() {
        SkipWhitespace();
        if (_pos >= _text.Length)
            return new TestToken(TestKind.EndOfFile, "");

        var ch = _text[_pos];

        if (ch is ':' or ',' or '{' or '}' or '+' or '*') {
            _pos++;
            return ch switch {
                ':' => new TestToken(TestKind.Colon, ":"),
                ',' => new TestToken(TestKind.Comma, ","),
                '{' => new TestToken(TestKind.LBrace, "{"),
                '}' => new TestToken(TestKind.RBrace, "}"),
                '+' => new TestToken(TestKind.Plus, "+"),
                '*' => new TestToken(TestKind.Star, "*"),
                _ => new TestToken(TestKind.Identifier, ch.ToString()),
            };
        }

        if (ch == '"') {
            _pos++;
            var sb = new StringBuilder();
            while (_pos < _text.Length && _text[_pos] != '"') {
                sb.Append(_text[_pos]);
                _pos++;
            }
            if (_pos < _text.Length) _pos++;
            return new TestToken(TestKind.String, sb.ToString());
        }

        if (char.IsDigit(ch)) {
            var start = _pos;
            while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
            return new TestToken(TestKind.Number, _text[start.._pos]);
        }

        if (char.IsLetter(ch) || ch == '_') {
            var start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
            var word = _text[start.._pos];
            return new TestToken(_keywords.GetValueOrDefault(word, TestKind.Identifier), word);
        }

        _pos++;
        return new TestToken(TestKind.Identifier, ch.ToString());
    }

    private void SkipWhitespace() {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
    }
}