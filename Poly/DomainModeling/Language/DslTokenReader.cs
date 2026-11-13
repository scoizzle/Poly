using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

using Poly.Grammar;

namespace Poly.DomainModeling.Language;

/// <summary>
/// Product DSL scanner on the buffered reader base. The base owns buffering +
/// committed position (Peek/Consume); this type owns the physical decoding seam:
/// char navigation, comments, strings, numbers, and the keyword map. A token is a
/// range into <see cref="Source"/>; line and column come from its line table.
/// </summary>
public sealed class DslTokenReader : BufferedTokenReader<DslToken, DslTokenKind> {
    private readonly string _text;
    private int _pos;
    private int _startPos;

    public DslTokenReader(string text) : this(new SourceText(text), text) {
    }

    public DslTokenReader(SourceText source) : this(source, TextOf(source)) {
    }

    private DslTokenReader(SourceText source, string text) {
        Source = source;
        _text = text;
        // A leading UTF-8 BOM is an encoding artifact, not a character of the document: start
        // scanning after it while line and column still begin at 1:1. Offsets stay relative to
        // the original text, so offset consumers (outline, edit splices, span mapping) splice
        // the caller's own string. A BOM anywhere else reaches ScanNextToken and fails there.
        _pos = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
    }

    /// <summary>The document every token of this reader points into.</summary>
    public SourceText Source { get; }

    // The string a SourceText was built over (the public constructor's own string), or a copy.
    private static string TextOf(SourceText source) {
        ArgumentNullException.ThrowIfNull(source);
        return MemoryMarshal.TryGetString(source.Text, out var text, out var start, out var length)
            && start == 0 && length == text.Length
            ? text
            : source.Text.ToString();
    }

    public override bool EndOfStream(DslTokenKind kind) => kind == DslTokenKind.EndOfFile;

    protected override DslToken ScanNextToken() {
        SkipWhitespaceAndComments();
        _startPos = _pos;

        if (_pos >= _text.Length)
            return Make(DslTokenKind.EndOfFile);

        var ch = PeekChar();

        switch (ch) {
            case ':': return Advance(DslTokenKind.Colon);
            case ',': return Advance(DslTokenKind.Comma);
            case '.': return Advance(DslTokenKind.Dot);
            case '(': return Advance(DslTokenKind.LParen);
            case ')': return Advance(DslTokenKind.RParen);
            case '{': return Advance(DslTokenKind.LBrace);
            case '}': return Advance(DslTokenKind.RBrace);
            case '[': return Advance(DslTokenKind.LBracket);
            case ']': return Advance(DslTokenKind.RBracket);
        }

        if (ch == '-' && PeekChar(1) == '>') return TwoChar(DslTokenKind.Arrow);
        if (ch == '>' && PeekChar(1) == '=') return TwoChar(DslTokenKind.Gte);
        if (ch == '<' && PeekChar(1) == '=') return TwoChar(DslTokenKind.Lte);
        if (ch == '=' && PeekChar(1) == '=') return TwoChar(DslTokenKind.Eq);
        if (ch == '=') return Advance(DslTokenKind.SingleEq);
        if (ch == '!' && PeekChar(1) == '=') return TwoChar(DslTokenKind.Neq);

        if (ch == '>') return Advance(DslTokenKind.Gt);
        if (ch == '<') return Advance(DslTokenKind.Lt);
        if (ch == '+') return Advance(DslTokenKind.Plus);
        if (ch == '-') return Advance(DslTokenKind.Minus);
        if (ch == '*') return Advance(DslTokenKind.Star);
        if (ch == '/') return Advance(DslTokenKind.Slash);

        if (ch == '"')
            return ScanString();

        if (char.IsAsciiDigit(ch))
            return ScanNumber();

        if (char.IsDigit(ch)) {
            var (digitLine, digitCol) = Source.GetLineColumn(_pos);
            throw GrammarError.Error($"Numeric literal contains non-ASCII digit 'U+{(int)ch:X4}'; use 0-9.", digitLine, digitCol);
        }

        if (IsWordStart(ch)) {
            while (_pos < _text.Length && IsWordPart(_text[_pos]))
                _pos++;
            return Make(WordToKind(_text.AsSpan(_startPos, _pos - _startPos)));
        }

        var (line, col) = Source.GetLineColumn(_pos);
        throw GrammarError.Error($"Unexpected character {Describe(ch)}", line, col);
    }

    /// <summary>
    /// A printable character in quotes, or its code point as <c>U+XXXX</c> when it has no
    /// visible glyph (BOM, zero-width space, non-breaking space, control), so a parse error
    /// names what the author cannot see.
    /// </summary>
    private static string Describe(char ch) =>
        IsInvisible(ch) ? $"U+{(int)ch:X4}" : $"'{ch}'";

    private static bool IsInvisible(char ch) =>
        char.IsWhiteSpace(ch) || char.IsControl(ch) || char.IsSurrogate(ch)
        || char.GetUnicodeCategory(ch) is UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.PrivateUse;

    private DslToken Make(DslTokenKind kind) => new(kind, _startPos, _pos - _startPos, Source);

    private DslToken Advance(DslTokenKind kind) {
        _pos++;
        return Make(kind);
    }

    private DslToken TwoChar(DslTokenKind kind) {
        _pos += 2;
        return Make(kind);
    }

    private void SkipWhitespaceAndComments() {
        while (_pos < _text.Length) {
            var ch = PeekChar();
            if (ch == '\n' || ch == '\r' || ch is ' ' or '\t') { _pos++; continue; }
            if (ch == '/' && PeekChar(1) == '/') {
                while (_pos < _text.Length && PeekChar() != '\n') _pos++;
                continue;
            }
            break;
        }
    }

    // Raw range only: the quotes and escapes stay in the slice; DslToken.Unescape decodes them.
    private DslToken ScanString() {
        _pos++; // skip opening "
        while (_pos < _text.Length) {
            var ch = PeekChar();
            if (ch == '"') { _pos++; break; }
            if (ch == '\\' && _pos + 1 < _text.Length && PeekChar(1) is '"' or '\\') {
                _pos += 2;
                continue;
            }
            _pos++;
        }
        return Make(DslTokenKind.StringLiteral);
    }

    private DslToken ScanNumber() {
        while (_pos < _text.Length && char.IsAsciiDigit(PeekChar()))
            _pos++;
        // Fractional bounds (range(0.01, 1.0)): consume a `.` and trailing digits.
        if (_pos < _text.Length && PeekChar() == '.'
            && _pos + 1 < _text.Length && char.IsAsciiDigit(_text[_pos + 1])) {
            _pos++;
            while (_pos < _text.Length && char.IsAsciiDigit(PeekChar()))
                _pos++;
        }
        return Make(DslTokenKind.Number);
    }

    private static bool IsWordStart(char ch) => char.IsLetter(ch) || ch == '_';

    private static bool IsWordPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    /// <summary>
    /// Whether <paramref name="text"/> is exactly one name the parser accepts for an entity,
    /// stage, action, policy and so on: a letter or <c>_</c>, then letters, digits or <c>_</c>,
    /// and not a keyword (<c>stage</c>, <c>length</c>, <c>Text</c>, ...). Uses the scanner's own
    /// character rules and keyword map, so the DSL and callers that build the same names by
    /// hand cannot disagree.
    /// </summary>
    public static bool IsIdentifier(string? text) => IsWord(text) && WordToKind(text) == DslTokenKind.Identifier;

    /// <summary>
    /// Whether the parser accepts <paramref name="name"/> for a property of type
    /// <paramref name="typeName"/>: any <see cref="IsIdentifier"/> name, or a primitive type keyword
    /// (<c>Text</c>, <c>Number</c>, <c>Boolean</c>) when the type is a primitive by
    /// <see cref="DslGrammar.IsPrimitiveType"/>, the test the parser uses
    /// (<c>Number: Text</c> and <c>Text: Date</c> parse, <c>Text: Money</c> does not).
    /// </summary>
    public static bool IsPropertyName(string? name, string typeName, Func<string, bool> isKnownPrimitiveName) =>
        IsIdentifier(name)
        || (IsWord(name) && DslGrammar.IsPrimitiveTypeKind(WordToKind(name))
            && IsWord(typeName) && DslGrammar.IsPrimitiveType(WordToKind(typeName), typeName, isKnownPrimitiveName));

    private static bool IsWord([NotNullWhen(true)] string? text) =>
        !string.IsNullOrEmpty(text) && IsWordStart(text[0]) && text.All(IsWordPart);

    private char PeekChar(int ahead = 0) => _pos + ahead < _text.Length ? _text[_pos + ahead] : '\0';

    private static DslTokenKind WordToKind(string word) => WordToKind(word.AsSpan());

    private static DslTokenKind WordToKind(ReadOnlySpan<char> word) => word switch {
        "domain" => DslTokenKind.Domain,
        "uses" => DslTokenKind.Uses,
        "entity" => DslTokenKind.Entity,
        "stage" => DslTokenKind.Stage,
        "action" => DslTokenKind.Action,
        "policy" => DslTokenKind.Policy,
        "when" => DslTokenKind.When,
        "require" => DslTokenKind.Require,
        "transition" => DslTokenKind.Transition,
        "to" => DslTokenKind.To,
        "assign" => DslTokenKind.Assign,
        "and" => DslTokenKind.And,
        "or" => DslTokenKind.Or,
        "not" => DslTokenKind.Not,
        "is" => DslTokenKind.Is,
        "true" => DslTokenKind.True,
        "false" => DslTokenKind.False,
        "null" => DslTokenKind.Null,
        "Text" => DslTokenKind.Text,
        "Number" => DslTokenKind.NumberType,
        "Boolean" => DslTokenKind.BooleanType,
        "optional" => DslTokenKind.Optional,
        "unique" => DslTokenKind.Unique,
        "range" => DslTokenKind.Range,
        "length" => DslTokenKind.Length,
        "pattern" => DslTokenKind.Pattern,
        "enum" => DslTokenKind.Enum,
        "default" => DslTokenKind.Equals,
        "equals" => DslTokenKind.Equality,
        "relationship" => DslTokenKind.Relationship,
        "from" => DslTokenKind.From,
        "link" => DslTokenKind.Link,
        "unlink" => DslTokenKind.Unlink,
        "one" => DslTokenKind.One,
        "many" => DslTokenKind.Many,
        "owned" => DslTokenKind.Owned,
        "create" => DslTokenKind.Create,
        "in" => DslTokenKind.In,
        "invoke" => DslTokenKind.Invoke,
        "for" => DslTokenKind.For,
        "if" => DslTokenKind.If,
        "else" => DslTokenKind.Else,
        "entry" => DslTokenKind.Entry,
        "exit" => DslTokenKind.Exit,
        "as" => DslTokenKind.As,
        "public" => DslTokenKind.Public,
        _ => DslTokenKind.Identifier,
    };
}