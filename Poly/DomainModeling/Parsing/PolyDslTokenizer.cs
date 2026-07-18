using System.Globalization;

namespace Poly.DomainModeling.Parsing;

public enum TokenKind {
    EndOfFile,
    Identifier,
    Number,
    StringLiteral,
    Colon,
    Comma,
    Dot,
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,
    Arrow,          // ->
    Gt,             // >
    Gte,            // >=
    Lt,             // <
    Lte,            // <=
    Eq,             // ==
    Neq,            // !=
    Is,
    Not,
    And,
    Or,
    Assign,
    To,
    Transition,
    When,
    Require,
    Domain,
    Entity,
    Stage,
    Action,
    Policy,
    True,
    False,
    Null,
    Text,
    NumberType,
    BooleanType,
    DateTimeType,
    DateType,
    Required,
    Unique,
    Range,
    Length,
    Pattern,
    Relationship,
    From,
    One,
    Many,
    Create,
    In,
    Entry,
    Exit,
}

public readonly record struct Token(TokenKind Kind, string Text, int Line, int Col);

/// <summary>
/// Hand-written scanner for the Phase 1a Poly DSL.
/// Produces a token stream consumed by <see cref="PolyDslParser"/>.
/// Zero external dependencies.
/// </summary>
public sealed class PolyDslTokenizer {
    private readonly string _text;
    private int _pos;
    private int _line = 1;
    private int _col = 1;
    private Token? _lookahead;

    public PolyDslTokenizer(string text) {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public Token Next() {
        if (_lookahead.HasValue) {
            var result = _lookahead.Value;
            _lookahead = null;
            return result;
        }
        return ScanNext();
    }

    /// <summary>
    /// Returns the next token without consuming it.
    /// Multiple calls return the same token until <see cref="Next"/> is called.
    /// </summary>
    public Token Peek() {
        if (!_lookahead.HasValue)
            _lookahead = ScanNext();
        return _lookahead.Value;
    }

    private Token ScanNext() {
        SkipWhitespaceAndComments();

        if (_pos >= _text.Length)
            return new Token(TokenKind.EndOfFile, "", _line, _col);

        var ch = CharAt();

        // Single-character tokens
        switch (ch) {
            case ':': return Advance(TokenKind.Colon);
            case ',': return Advance(TokenKind.Comma);
            case '.': return Advance(TokenKind.Dot);
            case '(': return Advance(TokenKind.LParen);
            case ')': return Advance(TokenKind.RParen);
            case '{': return Advance(TokenKind.LBrace);
            case '}': return Advance(TokenKind.RBrace);
            case '[': return Advance(TokenKind.LBracket);
            case ']': return Advance(TokenKind.RBracket);
        }

        // Two-character operators
        if (ch == '-' && CharAt(1) == '>') { _pos++; _col++; _pos++; _col++; return MakeToken("->", TokenKind.Arrow); }
        if (ch == '>' && CharAt(1) == '=') { _pos++; _col++; _pos++; _col++; return MakeToken(">=", TokenKind.Gte); }
        if (ch == '<' && CharAt(1) == '=') { _pos++; _col++; _pos++; _col++; return MakeToken("<=", TokenKind.Lte); }
        if (ch == '=' && CharAt(1) == '=') { _pos++; _col++; _pos++; _col++; return MakeToken("==", TokenKind.Eq); }
        if (ch == '!' && CharAt(1) == '=') { _pos++; _col++; _pos++; _col++; return MakeToken("!=", TokenKind.Neq); }

        // Single-character operators
        if (ch == '>') return Advance(TokenKind.Gt);
        if (ch == '<') return Advance(TokenKind.Lt);

        // String literals
        if (ch == '"')
            return ScanString();

        // Numbers
        if (char.IsDigit(ch))
            return ScanNumber();

        // Identifiers and keywords
        if (char.IsLetter(ch) || ch == '_') {
            var word = ScanWord();
            var kind = WordToKind(word);
            return new Token(kind, word, _line, _col - word.Length);
        }

        throw Error($"Unexpected character '{ch}'");
    }

    private Token Advance(TokenKind kind) {
        var text = _text[_pos].ToString();
        _pos++;
        _col++;
        return new Token(kind, text, _line, _col - 1);
    }

    private Token MakeToken(string text, TokenKind kind) =>
        new(kind, text, _line, _col - text.Length);

    private char CharAt(int offset = 0) {
        var i = _pos + offset;
        return i < _text.Length ? _text[i] : '\0';
    }

    private void SkipWhitespaceAndComments() {
        while (_pos < _text.Length) {
            var ch = _text[_pos];
            if (ch == '\n') { _line++; _col = 1; _pos++; continue; }
            if (ch == '\r') { _col++; _pos++; continue; }
            if (ch == ' ' || ch == '\t') { _col++; _pos++; continue; }

            if (ch == '/' && CharAt(1) == '/') {
                while (_pos < _text.Length && _text[_pos] != '\n') _pos++;
                continue;
            }

            break;
        }
    }

    private Token ScanString() {
        var startLine = _line;
        var startCol = _col;
        _pos++; _col++; // skip opening "
        var sb = new System.Text.StringBuilder();
        while (_pos < _text.Length) {
            var ch = _text[_pos];
            if (ch == '"') {
                _pos++; _col++;
                break;
            }
            if (ch == '\n') { _line++; _col = 1; }
            else { _col++; }
            sb.Append(ch);
            _pos++;
        }
        return new Token(TokenKind.StringLiteral, sb.ToString(), startLine, startCol);
    }

    private Token ScanNumber() {
        var startLine = _line;
        var startCol = _col;
        var sb = new System.Text.StringBuilder();
        while (_pos < _text.Length && char.IsDigit(_text[_pos])) {
            sb.Append(_text[_pos]);
            _pos++; _col++;
        }
        return new Token(TokenKind.Number, sb.ToString(), startLine, startCol);
    }

    private string ScanWord() {
        var start = _pos;
        while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) {
            _pos++; _col++;
        }
        return _text[start.._pos];
    }

    private static TokenKind WordToKind(string word) => word switch {
        "domain" => TokenKind.Domain,
        "entity" => TokenKind.Entity,
        "stage" => TokenKind.Stage,
        "action" => TokenKind.Action,
        "policy" => TokenKind.Policy,
        "when" => TokenKind.When,
        "require" => TokenKind.Require,
        "transition" => TokenKind.Transition,
        "to" => TokenKind.To,
        "assign" => TokenKind.Assign,
        "and" => TokenKind.And,
        "or" => TokenKind.Or,
        "not" => TokenKind.Not,
        "is" => TokenKind.Is,
        "true" => TokenKind.True,
        "false" => TokenKind.False,
        "null" => TokenKind.Null,
        "Text" => TokenKind.Text,
        "Number" => TokenKind.NumberType,
        "Boolean" => TokenKind.BooleanType,
        "DateTime" => TokenKind.DateTimeType,
        "Date" => TokenKind.DateType,
        "required" => TokenKind.Required,
        "unique" => TokenKind.Unique,
        "range" => TokenKind.Range,
        "length" => TokenKind.Length,
        "pattern" => TokenKind.Pattern,
        "relationship" => TokenKind.Relationship,
        "from" => TokenKind.From,
        "one" => TokenKind.One,
        "many" => TokenKind.Many,
        "create" => TokenKind.Create,
        "in" => TokenKind.In,
        "entry" => TokenKind.Entry,
        "exit" => TokenKind.Exit,
        _ => TokenKind.Identifier,
    };

    private Exception Error(string message) =>
        new FormatException($"Poly DSL parse error at line {_line}, col {_col}: {message}");
}